using Microsoft.AspNetCore.Http.HttpResults;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Research.Identity;

namespace Raven.Api.Features.Research;

public static class ResearchEndpoints
{
    public static IEndpointRouteBuilder MapResearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/research/identity/resolve", ResolveIdentityAsync)
            .WithTags("Research Identity")
            .WithName("ResolveCompanyIdentity")
            .WithSummary("Resolve a company identity before public-source research")
            .WithDescription("Uses explicit identity hints or one bounded knowledge call. It never searches or crawls.")
            .Produces<IdentityResolutionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        app.MapPost("/api/companies/{companyId:guid}/research/discover", DiscoverResearchAsync)
            .WithTags("Research")
            .WithName("DiscoverCompanyResearch")
            .WithSummary("Discover and classify public source candidates for a company")
            .WithDescription("Runs bounded deterministic discovery and leaves the run waiting for human source selection.")
            .Produces<ResearchRunResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/companies/{companyId:guid}/research/start", StartBackgroundResearchAsync)
            .WithTags("Research")
            .WithName("StartBackgroundCompanyResearch")
            .WithSummary("Start bounded research in the background")
            .Produces<ResearchRunResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/companies/{companyId:guid}/research", ResearchCompanyAsync)
            .WithTags("Research")
            .WithName("ResearchCompany")
            .WithSummary("Discover and acquire recommended public source documents for a company")
            .WithDescription("Compatibility endpoint that performs discovery and automatically acquires recommended candidates.")
            .Produces<ResearchRunResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/research-runs/{researchRunId:guid}", GetResearchRunAsync)
            .WithTags("Research")
            .WithName("GetResearchRun")
            .Produces<ResearchRunResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/research-runs/{researchRunId:guid}/execution", GetResearchExecutionAsync)
            .WithTags("Research")
            .WithName("GetResearchExecution")
            .WithSummary("Get developer-facing execution telemetry and run summary")
            .Produces<ResearchExecutionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/research-runs/{researchRunId:guid}/cancel", CancelResearchAsync)
            .WithTags("Research")
            .WithName("CancelResearchRun")
            .Produces<ResearchRunResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/research-runs/active", ListActiveResearchAsync)
            .WithTags("Research")
            .WithName("ListActiveResearchRuns")
            .Produces<ActiveResearchRunResponse[]>(StatusCodes.Status200OK);

        app.MapGet("/api/companies/{companyId:guid}/research-runs", ListResearchRunsAsync)
            .WithTags("Research")
            .WithName("ListCompanyResearchRuns")
            .Produces<ResearchRunResponse[]>(StatusCodes.Status200OK);

        app.MapGet("/api/research-runs/{researchRunId:guid}/candidates", ListResearchCandidatesAsync)
            .WithTags("Research")
            .WithName("ListResearchCandidates")
            .WithSummary("List persisted source candidates for a research run")
            .Produces<ResearchCandidateResponse[]>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/research-runs/{researchRunId:guid}/identity-candidates", ListIdentityCandidatesAsync)
            .WithTags("Research")
            .WithName("ListResearchIdentityCandidates")
            .WithSummary("List possible real-world research targets")
            .Produces<ResearchIdentityCandidateResponse[]>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/research-runs/{researchRunId:guid}/identity/select", SelectIdentityAsync)
            .WithTags("Research")
            .WithName("SelectResearchIdentity")
            .WithSummary("Select a real-world identity and rebuild targeted discovery")
            .Produces<ResearchRunResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/research-runs/{researchRunId:guid}/acquire", AcquireResearchAsync)
            .WithTags("Research")
            .WithName("AcquireResearchCandidates")
            .WithSummary("Acquire selected source candidates")
            .WithDescription("The server accepts candidate IDs persisted for this run; arbitrary URLs are not accepted.")
            .Produces<ResearchRunResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/research-runs/{researchRunId:guid}/sources", ListResearchSourcesAsync)
            .WithTags("Sources")
            .WithName("ListResearchRunSources")
            .WithSummary("List source documents acquired by a research run")
            .Produces<SourceDocumentResponse[]>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

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

    private static async Task<Results<Ok<IdentityResolutionResponse>, ValidationProblem>> ResolveIdentityAsync(
        IdentityResolutionRequest? request,
        IIdentityResolutionService identity,
        CancellationToken cancellationToken)
    {
        var result = await identity.ResolveAsync(request, cancellationToken);
        return result.IsValid
            ? TypedResults.Ok(result.Response!)
            : TypedResults.ValidationProblem(result.ValidationErrors);
    }

    private static async Task<Results<Ok<ResearchRunResponse>, NotFound>> DiscoverResearchAsync(
        Guid companyId,
        DiscoverResearchRequest? request,
        IResearchCompanyService research,
        CancellationToken cancellationToken)
    {
        var run = await research.DiscoverAsync(companyId, request, cancellationToken);
        return run is null ? TypedResults.NotFound() : TypedResults.Ok(run);
    }

    private static async Task<Results<Accepted<ResearchRunResponse>, NotFound>> StartBackgroundResearchAsync(
        Guid companyId,
        DiscoverResearchRequest? request,
        IResearchCompanyService research,
        IResearchRunBackgroundQueue queue,
        CancellationToken cancellationToken)
    {
        var run = await research.CreateQueuedRunAsync(companyId, request, cancellationToken);
        if (run is null) return TypedResults.NotFound();
        await queue.EnqueueAsync(run.Id, companyId, request ?? new DiscoverResearchRequest(), cancellationToken);
        return TypedResults.Accepted($"/api/research-runs/{run.Id}", run);
    }

    private static async Task<Results<Ok<ResearchRunResponse>, NotFound>> CancelResearchAsync(
        Guid researchRunId,
        IResearchCompanyService research,
        IResearchRunBackgroundQueue queue,
        CancellationToken cancellationToken)
    {
        queue.Cancel(researchRunId);
        var run = await research.CancelAsync(researchRunId, cancellationToken);
        return run is null ? TypedResults.NotFound() : TypedResults.Ok(run);
    }

    private static async Task<Ok<ActiveResearchRunResponse[]>> ListActiveResearchAsync(
        IResearchCompanyService research,
        CancellationToken cancellationToken) =>
        TypedResults.Ok((await research.ListActiveRunsAsync(cancellationToken)).ToArray());

    private static async Task<Results<Ok<ResearchRunResponse>, NotFound>> ResearchCompanyAsync(
        Guid companyId,
        IResearchCompanyService research,
        CancellationToken cancellationToken)
    {
        var run = await research.ResearchAsync(companyId, cancellationToken);
        return run is null ? TypedResults.NotFound() : TypedResults.Ok(run);
    }

    private static async Task<Results<Ok<ResearchRunResponse>, NotFound>> AcquireResearchAsync(
        Guid researchRunId,
        AcquireResearchCandidatesRequest request,
        IResearchCompanyService research,
        CancellationToken cancellationToken)
    {
        var run = await research.AcquireAsync(researchRunId, request, cancellationToken);
        return run is null ? TypedResults.NotFound() : TypedResults.Ok(run);
    }

    private static async Task<Results<Ok<ResearchRunResponse>, NotFound>> GetResearchRunAsync(
        Guid researchRunId,
        IResearchCompanyService research,
        CancellationToken cancellationToken)
    {
        var run = await research.GetRunAsync(researchRunId, cancellationToken);
        return run is null ? TypedResults.NotFound() : TypedResults.Ok(run);
    }

    private static async Task<Results<Ok<ResearchExecutionResponse>, NotFound>> GetResearchExecutionAsync(
        Guid researchRunId,
        IResearchExecutionService execution,
        CancellationToken cancellationToken)
    {
        var result = await execution.GetAsync(researchRunId, cancellationToken);
        return result is null ? TypedResults.NotFound() : TypedResults.Ok(result);
    }

    private static async Task<Ok<ResearchRunResponse[]>> ListResearchRunsAsync(
        Guid companyId,
        IResearchCompanyService research,
        CancellationToken cancellationToken) =>
        TypedResults.Ok((await research.ListRunsAsync(companyId, cancellationToken)).ToArray());

    private static async Task<Results<Ok<ResearchCandidateResponse[]>, NotFound>> ListResearchCandidatesAsync(
        Guid researchRunId,
        IResearchCompanyService research,
        CancellationToken cancellationToken)
    {
        var candidates = await research.ListCandidatesAsync(researchRunId, cancellationToken);
        return candidates is null ? TypedResults.NotFound() : TypedResults.Ok(candidates.ToArray());
    }

    private static async Task<Results<Ok<ResearchIdentityCandidateResponse[]>, NotFound>> ListIdentityCandidatesAsync(
        Guid researchRunId,
        IResearchCompanyService research,
        CancellationToken cancellationToken)
    {
        var candidates = await research.ListIdentityCandidatesAsync(researchRunId, cancellationToken);
        return candidates is null ? TypedResults.NotFound() : TypedResults.Ok(candidates.ToArray());
    }

    private static async Task<Results<Ok<ResearchRunResponse>, NotFound>> SelectIdentityAsync(
        Guid researchRunId,
        SelectResearchIdentityRequest request,
        IResearchCompanyService research,
        CancellationToken cancellationToken)
    {
        var run = await research.SelectIdentityAsync(researchRunId, request, cancellationToken);
        return run is null ? TypedResults.NotFound() : TypedResults.Ok(run);
    }

    private static async Task<Results<Ok<SourceDocumentResponse[]>, NotFound>> ListResearchSourcesAsync(
        Guid researchRunId,
        IResearchCompanyService research,
        CancellationToken cancellationToken)
    {
        var sources = await research.ListRunSourcesAsync(researchRunId, cancellationToken);
        return sources is null ? TypedResults.NotFound() : TypedResults.Ok(sources.ToArray());
    }

    private static async Task<Results<Ok<SourceDocumentResponse[]>, NotFound>> ListCompanySourcesAsync(
        Guid companyId,
        IResearchCompanyService research,
        CancellationToken cancellationToken)
    {
        var sources = await research.ListSourcesAsync(companyId, cancellationToken);
        return sources is null ? TypedResults.NotFound() : TypedResults.Ok(sources.ToArray());
    }

    private static async Task<Results<Ok<SourceDocumentDetailResponse>, NotFound>> GetSourceAsync(
        Guid sourceId,
        IResearchCompanyService research,
        CancellationToken cancellationToken)
    {
        var source = await research.GetSourceAsync(sourceId, cancellationToken);
        return source is null ? TypedResults.NotFound() : TypedResults.Ok(source);
    }
}
