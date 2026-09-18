using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Profiles.Persistence;
using Raven.Api.Features.Research.Coverage;
using Raven.Api.Features.Research;
using Raven.Api.Features.ManagedResearch;
using Raven.Api.Features.Research.ExternalImport;

namespace Raven.Api.Features.Companies.Workspace;

public sealed record WorkspaceReviewApiRequest(bool IncludeArchived = false, bool IncludeAiSuggestions = true);

public static class CompanyWorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapCompanyWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/companies/workspace-review", ReviewAsync)
            .WithTags("Companies")
            .WithSummary("Return read-only company health, duplicate, and review recommendations")
            .Produces<WorkspaceReviewResponse>(StatusCodes.Status200OK);
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
        var companies = await dbContext.Companies.AsNoTracking()
            .Where(company => request.IncludeArchived || company.ArchivedAt == null)
            .OrderBy(company => company.Name).ToListAsync(cancellationToken);
        var snapshots = new List<CompanyWorkspaceSnapshot>(companies.Count);
        foreach (var company in companies)
        {
            var profile = await profiles.GetCurrentProfileAsync(company.Id, cancellationToken);
            var sources = await dbContext.SourceDocuments.AsNoTracking().Where(source => source.CompanyId == company.Id).ToListAsync(cancellationToken);
            var runCount = await dbContext.ResearchRuns.AsNoTracking().CountAsync(run => run.CompanyId == company.Id, cancellationToken);
            var pending = await dbContext.ResearchRuns.AsNoTracking().AnyAsync(run => run.CompanyId == company.Id &&
                run.Stage == ResearchStage.AwaitingProfileConfirmation, cancellationToken);
            snapshots.Add(CompanyWorkspaceSnapshot.FromCompany(company, profile,
                coverage.Evaluate(company.Id, null, sources).Items, sources.Count, runCount, pending, company.ArchivedAt is not null));
        }

        var review = await workspace.ReviewAsync(new WorkspaceReviewRequest(snapshots, request.IncludeArchived,
            IncludeAiSuggestions: request.IncludeAiSuggestions), cancellationToken);
        var companyNames = companies.ToDictionary(company => company.Id, company => company.Name);
        var companyIds = companies.Select(company => company.Id).ToArray();
        var acceptedProfileCompanyIds = snapshots
            .Where(snapshot => snapshot.AcceptedProfile is not null && CompanyProfileReadiness.IsUsableAcceptedProfile(snapshot.AcceptedProfile))
            .Select(snapshot => snapshot.Company.Id)
            .ToHashSet();
        var ready = new List<WorkspaceResearchReviewItem>();
        var issues = new List<WorkspaceResearchReviewItem>();

        var nativeRuns = await dbContext.ResearchRuns.AsNoTracking()
            .Where(run => companyIds.Contains(run.CompanyId) &&
                (run.Status == ResearchRunStatus.Completed || run.Status == ResearchRunStatus.Failed || run.Status == ResearchRunStatus.Cancelled))
            .ToListAsync(cancellationToken);
        foreach (var run in nativeRuns.OrderByDescending(run => run.CompletedAt ?? run.StartedAt).Take(40))
        {
            var item = new WorkspaceResearchReviewItem(run.Id, run.CompanyId, companyNames[run.CompanyId], "RAVEN Research",
                run.Mode == ResearchMode.TargetedEnrichment ? "Targeted profile research" : "Company research",
                run.Status == ResearchRunStatus.Completed ? "Ready" : "Issue",
                run.CompletedAt ?? run.StartedAt,
                run.Error);
            if (run.Status == ResearchRunStatus.Completed) ready.Add(item); else issues.Add(item);
        }

        var managedJobs = await dbContext.ManagedResearchJobs.AsNoTracking()
            .Where(job => companyIds.Contains(job.CompanyId) && job.Status != ManagedResearchJobStatus.Queued && job.Status != ManagedResearchJobStatus.Researching)
            .ToListAsync(cancellationToken);
        foreach (var job in managedJobs.OrderByDescending(job => job.CompletedAt ?? job.CreatedAt).Take(40))
        {
            // A profile-improvement investigation is useful material, but it
            // cannot be reviewed as a profile update until Profile v1 exists.
            // Keep it out of the workspace queue rather than sending the user
            // into an action that cannot yet do anything useful.
            if (job.Status == ManagedResearchJobStatus.Completed &&
                job.Purpose == ManagedResearchPurpose.ProfileImprovement &&
                !acceptedProfileCompanyIds.Contains(job.CompanyId))
            {
                continue;
            }

            var item = new WorkspaceResearchReviewItem(job.Id, job.CompanyId, companyNames[job.CompanyId], "Deep Research",
                job.Objective, job.Status == ManagedResearchJobStatus.Completed ? "Ready" : "Issue",
                job.CompletedAt ?? job.CreatedAt, job.Error, job.InvestigationId);
            if (job.Status == ManagedResearchJobStatus.Completed) ready.Add(item); else issues.Add(item);
        }

        var externalJobs = await dbContext.ExternalResearchAnalysisJobs.AsNoTracking()
            .Where(job => companyIds.Contains(job.CompanyId) && job.Status != ExternalResearchAnalysisStatus.Queued && job.Status != ExternalResearchAnalysisStatus.Analyzing)
            .ToListAsync(cancellationToken);
        foreach (var job in externalJobs.OrderByDescending(job => job.CompletedAt ?? job.CreatedAt).Take(40))
        {
            var item = new WorkspaceResearchReviewItem(job.Id, job.CompanyId, companyNames[job.CompanyId], "External AI Assist",
                job.Question, job.Status == ExternalResearchAnalysisStatus.Completed ? "Ready" : "Issue",
                job.CompletedAt ?? job.CreatedAt, job.Error);
            if (job.Status == ExternalResearchAnalysisStatus.Completed) ready.Add(item); else issues.Add(item);
        }

        return TypedResults.Ok(review with
        {
            ResearchReady = ready.OrderByDescending(item => item.UpdatedAt).Take(20).ToArray(),
            ResearchIssues = issues.OrderByDescending(item => item.UpdatedAt).Take(20).ToArray()
        });
    }
}
