using Microsoft.AspNetCore.Http.HttpResults;

namespace Raven.Api.Features.Chat;

public static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}/chat/conversations").WithTags("Chat");
        group.MapPost("/", CreateAsync).Produces<ChatConversationResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet("/{conversationId:guid}", GetAsync).Produces<ChatConversationResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{conversationId:guid}/messages", SendAsync).Produces<SendChatMessageResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status502BadGateway);
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

    private static async Task<Ok<SendChatMessageResponse>> SendAsync(Guid companyId, Guid conversationId, CreateChatMessageRequest request, ICompanyChatService service, CancellationToken cancellationToken)
    {
        return TypedResults.Ok(await service.SendMessageAsync(companyId, conversationId, request, cancellationToken));
    }
}
