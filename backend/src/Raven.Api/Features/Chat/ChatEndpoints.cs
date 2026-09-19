using Microsoft.AspNetCore.Http.HttpResults;

namespace Raven.Api.Features.Chat;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}/chat/conversations").WithTags("Chat");
        group.MapPost("/", CreateAsync).Produces<ChatConversationResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet("/{conversationId:guid}", GetAsync).Produces<ChatConversationResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPatch("/{conversationId:guid}/capabilities", UpdateCapabilitiesAsync).Produces<ChatConversationResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/{conversationId:guid}/messages", SendAsync).Produces<SendChatMessageResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status502BadGateway);
        group.MapPost("/{conversationId:guid}/messages/stream", SendStreamAsync).Produces(StatusCodes.Status200OK, contentType: "text/event-stream");
        return app;
    }

    private static async Task<Created<ChatConversationResponse>> CreateAsync(Guid companyId, ICompanyChatService service, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var response = await service.CreateConversationAsync(companyId, cancellationToken);
        return TypedResults.Created($"{httpContext.Request.Path}/{response.Id}", response);
    }

    private static async Task<Ok<ChatConversationResponse>> GetAsync(Guid companyId, Guid conversationId, ICompanyChatService service, CancellationToken cancellationToken)
    {
        return TypedResults.Ok(await service.GetConversationAsync(companyId, conversationId, cancellationToken));
    }

    private static async Task<Ok<ChatConversationResponse>> UpdateCapabilitiesAsync(
        Guid companyId,
        Guid conversationId,
        UpdateChatCapabilitiesRequest request,
        ICompanyChatService service,
        CancellationToken cancellationToken)
    {
        return TypedResults.Ok(await service.UpdateCapabilitiesAsync(companyId, conversationId, request, cancellationToken));
    }

    private static async Task<Ok<SendChatMessageResponse>> SendAsync(Guid companyId, Guid conversationId, CreateChatMessageRequest request, ICompanyChatService service, CancellationToken cancellationToken)
    {
        return TypedResults.Ok(await service.SendMessageAsync(companyId, conversationId, request, cancellationToken));
    }

    private static async Task SendStreamAsync(Guid companyId, Guid conversationId, CreateChatMessageRequest request, ICompanyChatService service, HttpContext httpContext, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        httpContext.Response.ContentType = "text/event-stream";
        httpContext.Response.Headers.CacheControl = "no-cache";
        httpContext.Response.Headers.Append("X-Accel-Buffering", "no");
        var reporter = new HttpSseChatProgressReporter(httpContext.Response);
        try
        {
            var response = await service.SendMessageStreamAsync(companyId, conversationId, request, reporter, cancellationToken);
            await reporter.CompleteAsync(response, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The client left; the service marks its pending assistant message as failed.
        }
        catch (ChatProblemException failure)
        {
            await reporter.FailAsync(failure.Code, failure.ProblemDetail, CancellationToken.None);
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger(typeof(ChatEndpoints)).LogError(exception, "Chat stream failed for conversation {ConversationId}", conversationId);
            await reporter.FailAsync("chat_failed", "Ask RAVEN could not complete this response.", CancellationToken.None);
        }
    }
}
