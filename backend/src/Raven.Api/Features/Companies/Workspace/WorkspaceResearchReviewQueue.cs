using System.Text.Json;
using Raven.Api.Features.ManagedResearch;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Coverage;
using Raven.Api.Features.Research.ExternalImport;

namespace Raven.Api.Features.Companies.Workspace;

/// <summary>
/// Builds the compact, actionable research queue from terminal workflow rows.
/// The raw rows remain in their source tables; this class only derives a
/// review projection and safe cleanup candidates.
/// </summary>
public static class WorkspaceResearchReviewQueue
{
    private const int MaxTopicKeyLength = 480;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public sealed record Result(
        IReadOnlyList<WorkspaceResearchReviewItem> Ready,
        IReadOnlyList<WorkspaceResearchReviewItem> Issues,
        IReadOnlyList<WorkspaceResearchReviewCleanupCandidate> CleanupCandidates);

    public static Result Build(
        IReadOnlyCollection<ResearchRun> nativeRuns,
        IReadOnlyCollection<ManagedResearchJob> managedJobs,
        IReadOnlyCollection<ExternalResearchAnalysisJob> externalJobs,
        IReadOnlyDictionary<Guid, string> companyNames,
        IReadOnlyDictionary<Guid, CompanyProfileVersion?> acceptedProfiles,
        IReadOnlyCollection<WorkspaceResearchReviewState> states)
    {
        var candidates = new List<Candidate>();

        foreach (var run in nativeRuns)
        {
            var topicKey = run.Mode == ResearchMode.TargetedEnrichment
                ? TargetSetKey(ParseTargets(run.ResearchTargetsJson))
                : "initial";
            candidates.Add(new Candidate(
                run.Id,
                run.CompanyId,
                companyNames[run.CompanyId],
                "RAVEN Research",
                run.Mode == ResearchMode.TargetedEnrichment ? "Targeted profile research" : "Company research",
                topicKey,
                run.Status == ResearchRunStatus.Completed,
                run.CompletedAt ?? run.StartedAt,
                run.Error,
                null,
                run.Status == ResearchRunStatus.Completed &&
                acceptedProfiles.TryGetValue(run.CompanyId, out var profile) && profile?.ResearchRunId == run.Id));
        }

        foreach (var job in managedJobs)
        {
            if (job.Status == ManagedResearchJobStatus.Completed &&
                job.Purpose == ManagedResearchPurpose.ProfileImprovement &&
                (!acceptedProfiles.TryGetValue(job.CompanyId, out var profile) || profile is null || !CompanyProfileReadiness.IsUsableAcceptedProfile(profile)))
            {
                continue;
            }

            candidates.Add(new Candidate(
                job.Id,
                job.CompanyId,
                companyNames[job.CompanyId],
                "Deep Research",
                job.Objective,
                NormalizeText(job.Objective),
                job.Status == ManagedResearchJobStatus.Completed,
                job.CompletedAt ?? job.CreatedAt,
                job.Error,
                job.InvestigationId,
                false));
        }

        foreach (var job in externalJobs)
        {
            candidates.Add(new Candidate(
                job.Id,
                job.CompanyId,
                companyNames[job.CompanyId],
                "External AI Assist",
                job.Question,
                NormalizeText(job.Question),
                job.Status == ExternalResearchAnalysisStatus.Completed,
                job.CompletedAt ?? job.CreatedAt,
                job.Error,
                null,
                false));
        }

        var acknowledged = states.ToDictionary(state => state.ReviewKey, StringComparer.Ordinal);
        var ready = new List<WorkspaceResearchReviewItem>();
        var issues = new List<WorkspaceResearchReviewItem>();
        var cleanup = new List<WorkspaceResearchReviewCleanupCandidate>();

        foreach (var group in candidates.GroupBy(item => (item.CompanyId, item.Method, item.TopicKey)))
        {
            var ordered = group.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.ItemId).ToArray();
            var latest = ordered[0];
            var reviewKey = BuildReviewKey(latest.CompanyId, latest.Method, latest.TopicKey);
            var occurrenceCount = ordered.Length;
            var item = new WorkspaceResearchReviewItem(
                latest.ItemId,
                latest.CompanyId,
                latest.CompanyName,
                latest.Method,
                latest.Title,
                latest.IsReady ? "Ready" : "Issue",
                latest.UpdatedAt,
                latest.Detail,
                latest.InvestigationId,
                reviewKey,
                occurrenceCount,
                occurrenceCount > 1 ? $"{occurrenceCount - 1} older result{(occurrenceCount == 2 ? "" : "s")} grouped here." : null);

            if (latest.AutoReviewed || IsAcknowledged(acknowledged, reviewKey, latest.UpdatedAt))
            {
                continue;
            }

            if (latest.IsReady) ready.Add(item);
            else issues.Add(item);

            if (!latest.IsReady && occurrenceCount > 1)
            {
                cleanup.Add(new WorkspaceResearchReviewCleanupCandidate(
                    reviewKey,
                    latest.CompanyId,
                    latest.CompanyName,
                    latest.Method,
                    latest.Title,
                    "Repeated terminal results are represented by one latest issue.",
                    occurrenceCount,
                    latest.UpdatedAt));
            }
        }

        return new Result(
            ready.OrderByDescending(item => item.UpdatedAt).ToArray(),
            issues.OrderByDescending(item => item.UpdatedAt).ToArray(),
            cleanup.OrderByDescending(item => item.AcknowledgedThrough).ToArray());
    }

    public static string BuildReviewKey(Guid companyId, string method, string topicKey) =>
        $"{method}:{companyId:D}:{topicKey}";

    private static bool IsAcknowledged(
        IReadOnlyDictionary<string, WorkspaceResearchReviewState> states,
        string reviewKey,
        DateTimeOffset updatedAt) =>
        states.TryGetValue(reviewKey, out var state) && state.AcknowledgedThrough >= updatedAt;

    private static IReadOnlyList<ResearchTarget> ParseTargets(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ResearchTarget[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string TargetSetKey(IReadOnlyCollection<ResearchTarget> targets) =>
        targets.Count == 0
            ? "targeted"
            : $"targets:{string.Join(",", targets.Distinct().OrderBy(target => target).Select(target => target.ToString()))}";

    private static string NormalizeText(string? value) =>
        TruncateTopic(string.Join(' ', (value ?? string.Empty).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant());

    private static string TruncateTopic(string value) =>
        value.Length <= MaxTopicKeyLength ? value : value[..MaxTopicKeyLength];

    private sealed record Candidate(
        Guid ItemId,
        Guid CompanyId,
        string CompanyName,
        string Method,
        string Title,
        string TopicKey,
        bool IsReady,
        DateTimeOffset UpdatedAt,
        string? Detail,
        Guid? InvestigationId,
        bool AutoReviewed);
}
