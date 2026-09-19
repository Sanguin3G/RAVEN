using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.ManagedResearch;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Profiles.Persistence;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Coverage;
using Raven.Api.Features.Research.ExternalImport;

namespace Raven.Api.Features.Companies.Workspace;

public sealed record WorkspaceReviewApiRequest(bool IncludeArchived = false, bool IncludeAiSuggestions = true);

public static class CompanyWorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapCompanyWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/companies/workspace-review", ReviewAsync)
            .WithTags("Companies")
            .WithSummary("Return company health and the compact research review queue")
            .Produces<WorkspaceReviewResponse>(StatusCodes.Status200OK);

        app.MapPost("/api/companies/workspace-review/acknowledge", AcknowledgeAsync)
            .WithTags("Companies")
            .WithSummary("Mark workspace research review groups as handled")
            .Produces<WorkspaceResearchReviewCommandResponse>(StatusCodes.Status200OK);

        app.MapPost("/api/companies/workspace-review/cleanup", CleanupAsync)
            .WithTags("Companies")
            .WithSummary("Clear safe, redundant workspace research issue groups")
            .Produces<WorkspaceResearchReviewCommandResponse>(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<Ok<WorkspaceReviewResponse>> ReviewAsync(
        WorkspaceReviewApiRequest? request,
        RavenDbContext dbContext,
        ICompanyProfilePersistenceService profiles,
        IEvidenceCoverageEvaluator coverage,
        ICompanyWorkspaceReviewService workspace,
        CancellationToken cancellationToken)
    {
        request ??= new WorkspaceReviewApiRequest();
        var loaded = await LoadQueueAsync(request.IncludeArchived, dbContext, profiles, coverage, cancellationToken);
        var review = await workspace.ReviewAsync(new WorkspaceReviewRequest(
            loaded.Snapshots,
            request.IncludeArchived,
            IncludeAiSuggestions: request.IncludeAiSuggestions), cancellationToken);

        return TypedResults.Ok(review with
        {
            ResearchReady = loaded.Queue.Ready.Take(20).ToArray(),
            ResearchIssues = loaded.Queue.Issues.Take(20).ToArray(),
            ResearchCleanupCandidates = loaded.Queue.CleanupCandidates.Take(50).ToArray()
        });
    }

    private static async Task<Ok<WorkspaceResearchReviewCommandResponse>> AcknowledgeAsync(
        WorkspaceResearchReviewAcknowledgeRequest? request,
        RavenDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var items = (request?.Items ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.ReviewKey) && item.AcknowledgedThrough != default)
            .GroupBy(item => item.ReviewKey.Trim(), StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.AcknowledgedThrough).First())
            .ToArray();
        var acknowledged = 0;
        var skipped = 0;

        foreach (var item in items)
        {
            if (!TryParseReviewKey(item.ReviewKey, out var method, out var companyId, out var topicKey) ||
                !await dbContext.Companies.AsNoTracking().AnyAsync(company => company.Id == companyId, cancellationToken))
            {
                skipped++;
                continue;
            }

            var state = await dbContext.WorkspaceResearchReviewStates
                .SingleOrDefaultAsync(existing => existing.ReviewKey == item.ReviewKey, cancellationToken);
            if (state is null)
            {
                dbContext.WorkspaceResearchReviewStates.Add(new WorkspaceResearchReviewState
                {
                    ReviewKey = item.ReviewKey,
                    CompanyId = companyId,
                    Method = method,
                    TopicKey = topicKey,
                    AcknowledgedThrough = item.AcknowledgedThrough,
                    AcknowledgedAt = DateTimeOffset.UtcNow
                });
                acknowledged++;
                continue;
            }

            if (item.AcknowledgedThrough > state.AcknowledgedThrough)
            {
                state.AcknowledgedThrough = item.AcknowledgedThrough;
                state.AcknowledgedAt = DateTimeOffset.UtcNow;
                acknowledged++;
            }
            else
            {
                skipped++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new WorkspaceResearchReviewCommandResponse(acknowledged, acknowledged, skipped));
    }

    private static async Task<Ok<WorkspaceResearchReviewCommandResponse>> CleanupAsync(
        WorkspaceReviewApiRequest? request,
        RavenDbContext dbContext,
        ICompanyProfilePersistenceService profiles,
        IEvidenceCoverageEvaluator coverage,
        CancellationToken cancellationToken)
    {
        request ??= new WorkspaceReviewApiRequest();
        var loaded = await LoadQueueAsync(request.IncludeArchived, dbContext, profiles, coverage, cancellationToken);
        var candidates = loaded.Queue.CleanupCandidates;
        var acknowledged = 0;

        foreach (var item in candidates)
        {
            var state = await dbContext.WorkspaceResearchReviewStates
                .SingleOrDefaultAsync(existing => existing.ReviewKey == item.ReviewKey, cancellationToken);
            if (!TryParseReviewKey(item.ReviewKey, out _, out _, out var topicKey))
            {
                continue;
            }
            if (state is null)
            {
                dbContext.WorkspaceResearchReviewStates.Add(new WorkspaceResearchReviewState
                {
                    ReviewKey = item.ReviewKey,
                    CompanyId = item.CompanyId,
                    Method = item.Method,
                    TopicKey = topicKey,
                    AcknowledgedThrough = item.AcknowledgedThrough,
                    AcknowledgedAt = DateTimeOffset.UtcNow
                });
                acknowledged++;
            }
            else if (item.AcknowledgedThrough > state.AcknowledgedThrough)
            {
                state.AcknowledgedThrough = item.AcknowledgedThrough;
                state.AcknowledgedAt = DateTimeOffset.UtcNow;
                acknowledged++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new WorkspaceResearchReviewCommandResponse(
            acknowledged,
            candidates.Sum(item => item.OccurrenceCount),
            candidates.Count - acknowledged));
    }

    private static async Task<QueueLoad> LoadQueueAsync(
        bool includeArchived,
        RavenDbContext dbContext,
        ICompanyProfilePersistenceService profiles,
        IEvidenceCoverageEvaluator coverage,
        CancellationToken cancellationToken)
    {
        var companies = await dbContext.Companies.AsNoTracking()
            .Where(company => includeArchived || company.ArchivedAt == null)
            .OrderBy(company => company.Name)
            .ToListAsync(cancellationToken);
        var snapshots = new List<CompanyWorkspaceSnapshot>(companies.Count);

        foreach (var company in companies)
        {
            var profile = await profiles.GetCurrentProfileAsync(company.Id, cancellationToken);
            var sources = await dbContext.SourceDocuments.AsNoTracking()
                .Where(source => source.CompanyId == company.Id)
                .ToListAsync(cancellationToken);
            var runCount = await dbContext.ResearchRuns.AsNoTracking()
                .CountAsync(run => run.CompanyId == company.Id, cancellationToken);
            var pending = await dbContext.ResearchRuns.AsNoTracking().AnyAsync(run =>
                run.CompanyId == company.Id && run.Stage == ResearchStage.AwaitingProfileConfirmation,
                cancellationToken);
            snapshots.Add(CompanyWorkspaceSnapshot.FromCompany(
                company,
                profile,
                coverage.Evaluate(company.Id, null, sources).Items,
                sources.Count,
                runCount,
                pending,
                company.ArchivedAt is not null));
        }

        var companyIds = companies.Select(company => company.Id).ToArray();
        var nativeRuns = await dbContext.ResearchRuns.AsNoTracking()
            .Where(run => companyIds.Contains(run.CompanyId) &&
                (run.Status == ResearchRunStatus.Completed ||
                 run.Status == ResearchRunStatus.Failed ||
                 run.Status == ResearchRunStatus.Cancelled))
            .ToListAsync(cancellationToken);
        var managedJobs = await dbContext.ManagedResearchJobs.AsNoTracking()
            .Where(job => companyIds.Contains(job.CompanyId) && job.Status != ManagedResearchJobStatus.Queued && job.Status != ManagedResearchJobStatus.Researching)
            .ToListAsync(cancellationToken);
        var externalJobs = await dbContext.ExternalResearchAnalysisJobs.AsNoTracking()
            .Where(job => companyIds.Contains(job.CompanyId) && job.Status != ExternalResearchAnalysisStatus.Queued && job.Status != ExternalResearchAnalysisStatus.Analyzing)
            .ToListAsync(cancellationToken);
        var states = await dbContext.WorkspaceResearchReviewStates.AsNoTracking()
            .Where(state => companyIds.Contains(state.CompanyId))
            .ToListAsync(cancellationToken);

        var names = companies.ToDictionary(company => company.Id, company => company.Name);
        var acceptedProfiles = snapshots.ToDictionary(snapshot => snapshot.Company.Id, snapshot => snapshot.AcceptedProfile);
        var queue = WorkspaceResearchReviewQueue.Build(nativeRuns, managedJobs, externalJobs, names, acceptedProfiles, states);
        return new QueueLoad(snapshots, queue);
    }

    private static bool TryParseReviewKey(string value, out string method, out Guid companyId, out string topicKey)
    {
        method = string.Empty;
        companyId = Guid.Empty;
        topicKey = string.Empty;
        var firstSeparator = value.IndexOf(':');
        var secondSeparator = firstSeparator < 0 ? -1 : value.IndexOf(':', firstSeparator + 1);
        if (firstSeparator <= 0 || secondSeparator <= firstSeparator + 1 || secondSeparator >= value.Length - 1 ||
            !Guid.TryParse(value[(firstSeparator + 1)..secondSeparator], out companyId))
        {
            return false;
        }

        method = value[..firstSeparator];
        topicKey = value[(secondSeparator + 1)..];
        return !string.IsNullOrWhiteSpace(topicKey);
    }

    private sealed record QueueLoad(
        IReadOnlyList<CompanyWorkspaceSnapshot> Snapshots,
        WorkspaceResearchReviewQueue.Result Queue);
}
