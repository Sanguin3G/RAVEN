using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Profiles.Persistence;
using Raven.Api.Features.Research.Coverage;
using Raven.Api.Features.Research;

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

        return TypedResults.Ok(await workspace.ReviewAsync(new WorkspaceReviewRequest(snapshots, request.IncludeArchived,
            IncludeAiSuggestions: request.IncludeAiSuggestions), cancellationToken));
    }
}
