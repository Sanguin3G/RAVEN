using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.ManagedResearch;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Profiles.Persistence;
using Raven.Api.Features.Research.Briefings;

namespace Raven.Api.Features.Research.SavedArtifacts;

/// <summary>Company-scoped view and handling state across saved and managed research.</summary>
public sealed class InvestigationService(RavenDbContext db, ISavedResearchArtifactService artifacts, ICompanyProfilePersistenceService profiles)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<InvestigationResponse>> ListAsync(Guid companyId, CancellationToken ct)
    {
        var saved = await artifacts.ListAsync(companyId, ct);
        var jobs = await db.ManagedResearchJobs.AsNoTracking().Where(job => job.CompanyId == companyId).ToListAsync(ct);
        var states = await db.InvestigationReviewStates.AsNoTracking().Where(state => state.CompanyId == companyId).ToListAsync(ct);
        var profile = await profiles.GetCurrentProfileAsync(companyId, ct);
        var hasProfile = profile is not null && CompanyProfileReadiness.IsUsableAcceptedProfile(profile);
        var stateByKey = states.ToDictionary(state => (state.MaterialKind, state.MaterialId));
        var results = new List<InvestigationResponse>(saved.Count + jobs.Count);
        foreach (var artifact in saved)
        {
            stateByKey.TryGetValue((InvestigationMaterialKind.Saved, artifact.Id), out var state);
            var updatedAt = artifact.CompletedAt ?? artifact.CreatedAt;
            results.Add(new InvestigationResponse(
                artifact.Id, companyId, InvestigationMaterialKind.Saved, artifact.Id,
                artifact.Title, artifact.Objective ?? artifact.Question, artifact.Summary,
                artifact.Origin switch
                {
                    SavedResearchOrigin.ExternalImport => "External AI Assist",
                    SavedResearchOrigin.ManagedAi => "Deep Research",
                    _ => "RAVEN Research"
                }, artifact.Purpose, TopicsFor(artifact),
                state?.IsDone(updatedAt) == true ? InvestigationStatus.Done : InvestigationStatus.Ready,
                updatedAt, state?.DoneThrough, state?.DoneAt, state?.AppliedProfileVersionId,
                state?.AppliedAt, artifact.Purpose == InvestigationPurpose.ProfileImprovement && !hasProfile,
                artifact.Provider, artifact.Claims.ToArray(), artifact.SourceLeads.ToArray(),
                artifact.Uncertainties.ToArray(), artifact.Summary, artifact.RawResponse, []));
        }

        foreach (var job in jobs)
        {
            stateByKey.TryGetValue((InvestigationMaterialKind.Managed, job.Id), out var state);
            var result = Deserialize(job.ResultJson);
            var updatedAt = job.CompletedAt ?? job.CreatedAt;
            var status = job.Status switch
            {
                ManagedResearchJobStatus.Completed => state?.IsDone(updatedAt) == true
                    ? InvestigationStatus.Done : InvestigationStatus.Ready,
                ManagedResearchJobStatus.Failed or ManagedResearchJobStatus.Cancelled => InvestigationStatus.Failed,
                _ => InvestigationStatus.Running
            };
            var leads = (result?.Sources ?? []).Select(source => new ResearchSourceLead(source.Url, source.Title, source.Publisher)).ToArray();
            var claims = (result?.Claims ?? []).Select(claim => new ResearchClaim(claim.Topic, claim.Statement)).ToArray();
            results.Add(new InvestigationResponse(
                job.Id, companyId, InvestigationMaterialKind.Managed, job.InvestigationId,
                job.Objective, job.Objective, result?.Summary ?? job.Error ?? "Research is in progress.",
                "Deep Research", InvestigationPurposeMapping.FromManaged(job.Purpose),
                InvestigationTopics.Suggest($"{job.Objective} {string.Join(' ', claims.Select(claim => claim.Field))}"),
                status, updatedAt, state?.DoneThrough, state?.DoneAt, state?.AppliedProfileVersionId,
                state?.AppliedAt, job.Purpose == ManagedResearchPurpose.ProfileImprovement && !hasProfile,
                job.Provider, claims, leads, result?.Uncertainties ?? [], result?.Summary ?? job.Error,
                null, []));
        }
        var briefings = await db.ResearchBriefings.AsNoTracking().Where(brief => brief.CompanyId == companyId && brief.ArchivedAt == null).ToListAsync(ct);
        if (briefings.Count == 0) return results.OrderByDescending(item => item.MaterialUpdatedAt).ToArray();
        var briefingIds = briefings.Select(brief => brief.Id).ToArray();
        var versions = await db.ResearchBriefingVersions.AsNoTracking().Where(version => briefingIds.Contains(version.BriefingId)).ToListAsync(ct);
        var usage = new Dictionary<Guid, List<Guid>>();
        foreach (var brief in briefings)
        {
            var current = versions.Where(version => version.BriefingId == brief.Id).MaxBy(version => version.VersionNumber);
            if (current is null) continue;
            BriefingSourceSnapshot[] sources;
            try { sources = JsonSerializer.Deserialize<BriefingSourceSnapshot[]>(current.SourcesJson, JsonOptions) ?? []; }
            catch (JsonException) { continue; }
            foreach (var source in sources)
            {
                if (!usage.TryGetValue(source.InvestigationId, out var usedBy)) usage[source.InvestigationId] = usedBy = [];
                usedBy.Add(brief.Id);
            }
        }
        return results.Select(item => item with { BriefingIds = usage.GetValueOrDefault(item.Id)?.ToArray() ?? [] })
            .OrderByDescending(item => item.MaterialUpdatedAt).ToArray();
    }

    public async Task<InvestigationResponse?> GetAsync(Guid companyId, Guid id, CancellationToken ct) =>
        (await ListAsync(companyId, ct)).FirstOrDefault(item => item.Id == id || item.MaterialId == id);

    public async Task<InvestigationResponse?> MarkDoneAsync(Guid companyId, Guid id, CancellationToken ct)
    {
        var item = await GetAsync(companyId, id, ct);
        if (item is null) return null;
        if (item.Status is InvestigationStatus.Running or InvestigationStatus.Failed)
            throw new InvalidOperationException("Only ready investigations can be marked done.");
        var state = await GetOrCreateStateAsync(item, ct);
        state.DoneThrough = item.MaterialUpdatedAt;
        state.DoneAt = DateTimeOffset.UtcNow;
        state.ReopenedAt = null;
        await db.SaveChangesAsync(ct);
        return await GetAsync(companyId, item.Id, ct);
    }

    public async Task<InvestigationResponse?> ReopenAsync(Guid companyId, Guid id, CancellationToken ct)
    {
        var item = await GetAsync(companyId, id, ct);
        if (item is null) return null;
        if (item.Status is InvestigationStatus.Running or InvestigationStatus.Failed)
            throw new InvalidOperationException("Only completed investigations can be reopened.");
        var state = await GetOrCreateStateAsync(item, ct);
        state.ReopenedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(companyId, item.Id, ct);
    }

    private async Task<InvestigationReviewState> GetOrCreateStateAsync(InvestigationResponse item, CancellationToken ct)
    {
        var state = await db.InvestigationReviewStates.SingleOrDefaultAsync(value =>
            value.CompanyId == item.CompanyId && value.MaterialKind == item.MaterialKind && value.MaterialId == item.Id, ct);
        if (state is not null) return state;
        state = new InvestigationReviewState { CompanyId = item.CompanyId, MaterialKind = item.MaterialKind, MaterialId = item.Id };
        db.InvestigationReviewStates.Add(state);
        return state;
    }

    private static string[] TopicsFor(SavedResearchArtifact artifact)
    {
        var topics = InvestigationTopics.Parse(artifact.TopicsJson);
        return topics.Length > 0 ? topics : InvestigationTopics.Suggest($"{artifact.Title} {artifact.Question} {string.Join(' ', artifact.Claims.Select(claim => claim.Field))}");
    }

    private static ManagedResearchResult? Deserialize(string? json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<ManagedResearchResult>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }
}
