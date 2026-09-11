using Microsoft.AspNetCore.Http.HttpResults;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Research.SavedArtifacts;

namespace Raven.Api.Features.DeepResearch;

/// <summary>HTTP transport for starting an application-owned Deep Research run.</summary>
public sealed record StartCompanyDeepResearchRequest(string Question, Guid? ConversationId = null);

/// <summary>Polling response including only safe, user-visible activity.</summary>
public sealed record DeepResearchRunDetailResponse(
    DeepResearchRunResponse Run,
    IReadOnlyList<DeepResearchActivityEvent> Activity);

/// <summary>Request to save the server-owned result of a completed Deep Research run.</summary>
public sealed record SaveDeepResearchArtifactRequest(Guid DeepResearchRunId, string? Title = null);

public static class DeepResearchEndpoints
{
    public static IEndpointRouteBuilder MapDeepResearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/companies/{companyId:guid}/deep-research", StartAsync)
            .WithTags("Deep Research")
            .WithName("StartCompanyDeepResearch")
            .WithSummary("Queue a bounded, company-scoped Deep Research run")
            .WithDescription("The API returns immediately. Poll the run endpoint for sanitized activity and the final grounded answer.")
            .Produces<DeepResearchRunResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/deep-research-runs/{runId:guid}", GetAsync)
            .WithTags("Deep Research")
            .WithName("GetDeepResearchRun")
            .WithSummary("Poll a Deep Research run and its safe activity stream")
            .Produces<DeepResearchRunDetailResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/companies/{companyId:guid}/deep-research-runs", ListAsync)
            .WithTags("Deep Research")
            .WithName("ListCompanyDeepResearchRuns")
            .WithSummary("List a company's Deep Research runs")
            .Produces<DeepResearchRunResponse[]>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/companies/{companyId:guid}/saved-research", SaveCompletedDeepResearchAsync)
            .WithTags("Saved Research")
            .WithName("SaveCompletedDeepResearch")
            .WithSummary("Save a completed Deep Research result without changing the Company Profile")
            .Produces<SavedResearchArtifactResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/companies/{companyId:guid}/saved-research", ListSavedAsync)
            .WithTags("Saved Research")
            .WithName("ListCompanySavedResearch")
            .WithSummary("List saved research artifacts for a company")
            .Produces<SavedResearchArtifactResponse[]>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/companies/{companyId:guid}/saved-research/{artifactId:guid}", GetSavedAsync)
            .WithTags("Saved Research")
            .WithName("GetCompanySavedResearch")
            .WithSummary("Get one saved research artifact scoped to a company")
            .Produces<SavedResearchArtifactResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Accepted<DeepResearchRunResponse>, BadRequest, NotFound>> StartAsync(
        Guid companyId,
        StartCompanyDeepResearchRequest request,
        ICompanyService companies,
        IDeepResearchRunService research,
        IDeepResearchQueue queue,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return TypedResults.BadRequest();
        }

        if (await companies.GetByIdAsync(companyId, cancellationToken) is null)
        {
            return TypedResults.NotFound();
        }

        try
        {
            var run = await research.StartAsync(
                companyId,
                new StartDeepResearchRequest(request.Question, request.ConversationId),
                cancellationToken);
            await queue.EnqueueAsync(run.Id, cancellationToken);
            return TypedResults.Accepted($"/api/deep-research-runs/{run.Id:D}", run);
        }
        catch (ArgumentException)
        {
            return TypedResults.BadRequest();
        }
    }

    private static async Task<Results<Ok<DeepResearchRunDetailResponse>, NotFound>> GetAsync(
        Guid runId,
        IDeepResearchRunService research,
        IDeepResearchActivityStore activities,
        CancellationToken cancellationToken)
    {
        var run = await research.GetAsync(runId, cancellationToken);
        if (run is null)
        {
            return TypedResults.NotFound();
        }

        var activity = await activities.ListAsync(runId, cancellationToken);
        return TypedResults.Ok(new DeepResearchRunDetailResponse(run, activity));
    }

    private static async Task<Results<Ok<DeepResearchRunResponse[]>, NotFound>> ListAsync(
        Guid companyId,
        ICompanyService companies,
        IDeepResearchRunService research,
        CancellationToken cancellationToken)
    {
        if (await companies.GetByIdAsync(companyId, cancellationToken) is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok((await research.ListForCompanyAsync(companyId, cancellationToken)).ToArray());
    }

    private static async Task<Results<Created<SavedResearchArtifactResponse>, BadRequest, NotFound>> SaveCompletedDeepResearchAsync(
        Guid companyId,
        SaveDeepResearchArtifactRequest request,
        ICompanyService companies,
        IDeepResearchRunService research,
        IDeepResearchActivityStore activities,
        ISavedResearchArtifactService artifacts,
        CancellationToken cancellationToken)
    {
        if (await companies.GetByIdAsync(companyId, cancellationToken) is null)
        {
            return TypedResults.NotFound();
        }

        var run = await research.GetAsync(request.DeepResearchRunId, cancellationToken);
        if (run is null || run.CompanyId != companyId)
        {
            return TypedResults.NotFound();
        }

        if (run.Status != DeepResearchRunStatus.Completed || string.IsNullOrWhiteSpace(run.ResultMarkdown))
        {
            return TypedResults.BadRequest();
        }

        var sourceIds = (await activities.ListAsync(run.Id, cancellationToken))
            .SelectMany(item => item.SourceDocumentIds)
            .Distinct()
            .ToArray();
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? DeepResearchText.Bound(run.Question, 120)
            : request.Title.Trim();

        try
        {
            var artifact = await artifacts.CreateAsync(new SavedResearchArtifactRequest(
                companyId,
                title,
                run.Question,
                run.ResultMarkdown,
                SavedResearchType.Deep,
                run.Model,
                sourceIds,
                run.ConversationId,
                run.Id), cancellationToken);
            var response = SavedResearchArtifactResponse.FromEntity(artifact);
            return TypedResults.Created($"/api/companies/{companyId:D}/saved-research/{artifact.Id:D}", response);
        }
        catch (SavedResearchArtifactValidationException)
        {
            return TypedResults.BadRequest();
        }
    }

    private static async Task<Results<Ok<SavedResearchArtifactResponse[]>, NotFound>> ListSavedAsync(
        Guid companyId,
        ICompanyService companies,
        ISavedResearchArtifactService artifacts,
        CancellationToken cancellationToken)
    {
        if (await companies.GetByIdAsync(companyId, cancellationToken) is null)
        {
            return TypedResults.NotFound();
        }

        var response = (await artifacts.ListAsync(companyId, cancellationToken))
            .Select(SavedResearchArtifactResponse.FromEntity)
            .ToArray();
        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<SavedResearchArtifactResponse>, NotFound>> GetSavedAsync(
        Guid companyId,
        Guid artifactId,
        ISavedResearchArtifactService artifacts,
        CancellationToken cancellationToken)
    {
        var artifact = await artifacts.GetAsync(companyId, artifactId, cancellationToken);
        return artifact is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(SavedResearchArtifactResponse.FromEntity(artifact));
    }
}
