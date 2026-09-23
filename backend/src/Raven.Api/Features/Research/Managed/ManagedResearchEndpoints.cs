using Microsoft.AspNetCore.Http.HttpResults;

namespace Raven.Api.Features.ManagedResearch;

/// <summary>HTTP transport for asynchronous, provider-managed research jobs.</summary>
public static class ManagedResearchEndpoints
{
    public static IEndpointRouteBuilder MapManagedResearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/companies/{companyId:guid}/managed-research/preview", PreviewBriefAsync)
            .WithTags("Managed Research")
            .WithName("PreviewManagedResearchBrief")
            .WithSummary("Rewrite a request into one reviewable Deep Research question")
            .WithDescription("Uses bounded AI rewriting and current company context. It does not queue research or contact the managed research provider.")
            .Produces<ManagedResearchBriefPreviewResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/companies/{companyId:guid}/managed-research", StartAsync)
            .WithTags("Managed Research")
            .WithName("StartManagedResearch")
            .WithSummary("Queue managed AI research for a company")
            .WithDescription("Starts an asynchronous provider run. Results remain investigation material and never update the accepted Company Profile automatically.")
            .Produces<ManagedResearchJobResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/companies/{companyId:guid}/managed-research/{jobId:guid}", GetAsync)
            .WithTags("Managed Research")
            .WithName("GetManagedResearch")
            .WithSummary("Poll a managed AI research job")
            .Produces<ManagedResearchJobResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/api/companies/{companyId:guid}/managed-research", ListAsync)
            .WithTags("Managed Research")
            .WithName("ListManagedResearch")
            .WithSummary("List durable managed AI research jobs for a company")
            .Produces<ManagedResearchJobResponse[]>(StatusCodes.Status200OK);

        app.MapGet("/api/companies/{companyId:guid}/research-context-attachments", ListAttachmentsAsync)
            .WithTags("Research Context")
            .WithName("ListResearchContextAttachments")
            .WithSummary("List investigations explicitly attached to a Chat conversation")
            .Produces<ResearchContextAttachmentResponse[]>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        app.MapPost("/api/companies/{companyId:guid}/managed-research/{investigationId:guid}/context-attachments", AttachContextAsync)
            .WithTags("Research Context")
            .WithName("AttachResearchContext")
            .WithSummary("Attach a completed investigation to a Chat conversation")
            .Produces<ResearchContextAttachmentResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        app.MapDelete("/api/companies/{companyId:guid}/managed-research/{investigationId:guid}/context-attachments", RemoveContextAsync)
            .WithTags("Research Context")
            .WithName("RemoveResearchContext")
            .WithSummary("Remove an investigation from a Chat conversation")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/api/companies/{companyId:guid}/managed-research/{jobId:guid}/cancel", CancelAsync)
            .WithTags("Managed Research")
            .WithName("CancelManagedResearch")
            .WithSummary("Request cancellation of managed AI research")
            .Produces<ManagedResearchJobResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<ManagedResearchBriefPreviewResponse>, BadRequest, NotFound, ProblemHttpResult>> PreviewBriefAsync(
        Guid companyId,
        ManagedResearchBriefPreviewRequest request,
        IManagedResearchBriefPreviewService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok(await service.PreviewAsync(companyId, request, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (ArgumentException)
        {
            return TypedResults.BadRequest();
        }
        catch (ManagedResearchBriefGenerationException exception)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Research question unavailable",
                detail: exception.Message,
                extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
        }
    }

    private static async Task<Results<Accepted<ManagedResearchJobResponse>, BadRequest, NotFound, Conflict>> StartAsync(
        Guid companyId,
        StartManagedResearchRequest request,
        IManagedResearchJobService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await service.StartAsync(companyId, request, cancellationToken);
            return TypedResults.Accepted(
                $"/api/companies/{companyId:D}/managed-research/{response.Id:D}",
                response);
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (ArgumentException)
        {
            return TypedResults.BadRequest();
        }
        catch (ManagedResearchPreviewStaleException)
        {
            return TypedResults.Conflict();
        }
        catch (InvalidOperationException) when (request.AnswerInChat)
        {
            return TypedResults.Conflict();
        }
    }

    private static async Task<Results<Ok<ManagedResearchJobResponse>, NotFound>> GetAsync(
        Guid companyId,
        Guid jobId,
        IManagedResearchJobService service,
        CancellationToken cancellationToken)
    {
        var response = await service.GetAsync(companyId, jobId, cancellationToken);
        return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
    }

    private static async Task<Ok<ManagedResearchJobResponse[]>> ListAsync(
        Guid companyId,
        IManagedResearchJobService service,
        CancellationToken cancellationToken) =>
        TypedResults.Ok((await service.ListForCompanyAsync(companyId, cancellationToken)).ToArray());

    private static async Task<Results<Ok<ResearchContextAttachmentResponse[]>, BadRequest, NotFound>> ListAttachmentsAsync(
        Guid companyId,
        Guid conversationId,
        IResearchContextAttachmentService service,
        CancellationToken cancellationToken)
    {
        try
        {
            return TypedResults.Ok((await service.ListAsync(companyId, conversationId, cancellationToken)).ToArray());
        }
        catch (ArgumentException)
        {
            return TypedResults.BadRequest();
        }
        catch (KeyNotFoundException) { return TypedResults.NotFound(); }
    }

    private static async Task<Results<Created<ResearchContextAttachmentResponse>, BadRequest, NotFound>> AttachContextAsync(
        Guid companyId,
        Guid investigationId,
        AttachResearchContextRequest request,
        IResearchContextAttachmentService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await service.AttachAsync(companyId, investigationId, request, cancellationToken);
            return TypedResults.Created(
                $"/api/companies/{companyId:D}/managed-research/{investigationId:D}/context-attachments?conversationId={request.ConversationId:D}",
                response);
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (ArgumentException)
        {
            return TypedResults.BadRequest();
        }
    }

    private static async Task<Results<NoContent, BadRequest, NotFound>> RemoveContextAsync(
        Guid companyId,
        Guid investigationId,
        Guid conversationId,
        IResearchContextAttachmentService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var removed = await service.RemoveAsync(companyId, conversationId, investigationId, cancellationToken);
            return removed ? TypedResults.NoContent() : TypedResults.NotFound();
        }
        catch (ArgumentException)
        {
            return TypedResults.BadRequest();
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
    }

    private static async Task<Results<Ok<ManagedResearchJobResponse>, NotFound>> CancelAsync(
        Guid companyId,
        Guid jobId,
        IManagedResearchJobService service,
        CancellationToken cancellationToken)
    {
        var response = await service.CancelAsync(companyId, jobId, cancellationToken);
        return response is null ? TypedResults.NotFound() : TypedResults.Ok(response);
    }

}
