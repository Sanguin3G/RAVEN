using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Research.Planning;

namespace Raven.Api.Features.Research.Coverage;

public static class ResearchCoverageEndpoints
{
    public static IEndpointRouteBuilder MapResearchCoverageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/research-runs/{researchRunId:guid}/coverage", GetRunCoverageAsync)
            .WithTags("Research")
            .WithSummary("Evaluate acquired evidence coverage before profile generation")
            .Produces<EvidenceCoverageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
        app.MapGet("/api/companies/{companyId:guid}/coverage", GetCompanyCoverageAsync)
            .WithTags("Companies")
            .WithSummary("Evaluate accepted company evidence coverage")
            .Produces<EvidenceCoverageResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
        return app;
    }

    private static async Task<Results<Ok<EvidenceCoverageResponse>, NotFound>> GetRunCoverageAsync(
        Guid researchRunId, RavenDbContext dbContext, IEvidenceCoverageEvaluator evaluator, CancellationToken cancellationToken)
    {
        var run = await dbContext.ResearchRuns.AsNoTracking().SingleOrDefaultAsync(item => item.Id == researchRunId, cancellationToken);
        if (run is null) return TypedResults.NotFound();
        var sources = await dbContext.SourceDocuments.AsNoTracking().Where(item => item.ResearchRunId == run.Id).ToListAsync(cancellationToken);
        var targets = DeserializeTargets(run.ResearchTargetsJson);
        return TypedResults.Ok(evaluator.Evaluate(run.CompanyId, run.Id, sources, targets, run.QueriesTotal >= TargetedQueryPlanner.MaximumQueries));
    }

    private static async Task<Results<Ok<EvidenceCoverageResponse>, NotFound>> GetCompanyCoverageAsync(
        Guid companyId, RavenDbContext dbContext, IEvidenceCoverageEvaluator evaluator, CancellationToken cancellationToken)
    {
        if (!await dbContext.Companies.AsNoTracking().AnyAsync(item => item.Id == companyId, cancellationToken)) return TypedResults.NotFound();
        var latest = await dbContext.CompanyProfileVersions.AsNoTracking()
            .Where(item => item.CompanyId == companyId).OrderByDescending(item => item.Version).FirstOrDefaultAsync(cancellationToken);
        if (latest is null) return TypedResults.Ok(evaluator.Evaluate(companyId, null, []));
        var evidenceJson = await dbContext.ProfileEvidences.AsNoTracking()
            .Where(item => item.CompanyProfileVersionId == latest.Id)
            .Select(item => item.SourceDocumentIdsJson).ToListAsync(cancellationToken);
        var acceptedSourceIds = evidenceJson.SelectMany(DeserializeSourceIds).ToHashSet();
        var sources = await dbContext.SourceDocuments.AsNoTracking()
            .Where(item => item.CompanyId == companyId && acceptedSourceIds.Contains(item.Id)).ToListAsync(cancellationToken);
        return TypedResults.Ok(evaluator.Evaluate(companyId, null, sources));
    }

    private static IReadOnlyList<ResearchTarget> DeserializeTargets(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<ResearchTarget[]>(json) ?? []; }
        catch (System.Text.Json.JsonException) { return []; }
    }

    private static IReadOnlyList<Guid> DeserializeSourceIds(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<Guid[]>(json) ?? []; }
        catch (System.Text.Json.JsonException) { return []; }
    }
}
