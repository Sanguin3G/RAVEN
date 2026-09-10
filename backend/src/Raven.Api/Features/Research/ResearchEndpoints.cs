using Microsoft.AspNetCore.Http.HttpResults;

namespace Raven.Api.Features.Research;

public static class ResearchEndpoints
{
    public static IEndpointRouteBuilder MapResearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/companies/{companyId:guid}/research", ResearchCompanyAsync)
            .WithTags("Research")
            .WithName("ResearchCompany")
            .WithSummary("Discover and acquire public source documents for a company")
            .Produces<ResearchRunResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/research-runs/{researchRunId:guid}", GetResearchRunAsync)
            .WithTags("Research")
            .WithName("GetResearchRun")
            .Produces<ResearchRunResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/companies/{companyId:guid}/research-runs", ListResearchRunsAsync)
            .WithTags("Research")
            .WithName("ListCompanyResearchRuns")
            .Produces<ResearchRunResponse[]>(StatusCodes.Status200OK);

        app.MapGet("/api/companies/{companyId:guid}/sources", ListCompanySourcesAsync)
            .WithTags("Sources")
            .WithName("ListCompanySources")
            .Produces<SourceDocumentResponse[]>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/sources/{sourceId:guid}", GetSourceAsync)
            .WithTags("Sources")
            .WithName("GetSource")
            .Produces<SourceDocumentDetailResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<ResearchRunResponse>, NotFound>> ResearchCompanyAsync(Guid companyId, IResearchCompanyService research, CancellationToken cancellationToken)
    {
        var run = await research.ResearchAsync(companyId, cancellationToken);
        return run is null ? TypedResults.NotFound() : TypedResults.Ok(run);
    }

    private static async Task<Results<Ok<ResearchRunResponse>, NotFound>> GetResearchRunAsync(Guid researchRunId, IResearchCompanyService research, CancellationToken cancellationToken)
    {
        var run = await research.GetRunAsync(researchRunId, cancellationToken);
        return run is null ? TypedResults.NotFound() : TypedResults.Ok(run);
    }

    private static async Task<Ok<ResearchRunResponse[]>> ListResearchRunsAsync(Guid companyId, IResearchCompanyService research, CancellationToken cancellationToken) =>
        TypedResults.Ok((await research.ListRunsAsync(companyId, cancellationToken)).ToArray());

    private static async Task<Results<Ok<SourceDocumentResponse[]>, NotFound>> ListCompanySourcesAsync(Guid companyId, IResearchCompanyService research, CancellationToken cancellationToken)
    {
        var sources = await research.ListSourcesAsync(companyId, cancellationToken);
        return sources is null ? TypedResults.NotFound() : TypedResults.Ok(sources.ToArray());
    }

    private static async Task<Results<Ok<SourceDocumentDetailResponse>, NotFound>> GetSourceAsync(Guid sourceId, IResearchCompanyService research, CancellationToken cancellationToken)
    {
        var source = await research.GetSourceAsync(sourceId, cancellationToken);
        return source is null ? TypedResults.NotFound() : TypedResults.Ok(source);
    }
}
