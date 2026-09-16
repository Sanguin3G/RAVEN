using Raven.Api.Features.Companies;
using Raven.Api.Features.Profiles;

namespace Raven.Api.Features.Chat;

// Answered is the only factual-answer status and therefore requires profile
// evidence citations. Conversational and Guidance are deliberately non-factual.
public enum ChatAnswerStatus { Answered, Conversational, Guidance, ClarificationRequired, InsufficientEvidence, UnsupportedScope }
public enum ChatMessageRole { User, Assistant }
public enum ChatMessageStatus { Pending, Completed, Failed }
public enum ChatCitationOrigin { Profile }

public sealed record CreateChatMessageRequest(string Question);

public sealed record ChatCitationResponse(
    ChatCitationOrigin Origin,
    Guid SourceDocumentId,
    string? FieldPath,
    string? Title,
    string Url,
    DateTimeOffset RetrievedAt);

public sealed record ChatToolExecutionResponse(
    string Tool,
    string Provider,
    string Status,
    long DurationMs,
    string? ErrorCode);

public sealed record ChatHistoryMessage(
    Guid Id,
    ChatMessageRole Role,
    string Content,
    ChatMessageStatus Status,
    ChatAnswerStatus? AnswerStatus,
    string? FollowUpQuestion,
    IReadOnlyList<ChatCitationResponse> Citations,
    IReadOnlyList<ChatToolExecutionResponse> ToolExecutions,
    DateTimeOffset CreatedAt);

public sealed record ChatConversationResponse(
    Guid Id,
    Guid CompanyId,
    Guid ProfileVersionId,
    int ProfileVersion,
    string? Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ChatHistoryMessage> Messages);

public sealed record SendChatMessageResponse(
    Guid ConversationId,
    Guid MessageId,
    Guid CompanyId,
    int ProfileVersion,
    ChatAnswerStatus Status,
    string Answer,
    IReadOnlyList<ChatCitationResponse> Citations,
    IReadOnlyList<ChatToolExecutionResponse> ToolExecutions,
    string? FollowUpQuestion);

public sealed record ChatAgentResult(
    ChatAnswerStatus Status,
    string Answer,
    IReadOnlyList<Guid> CitedSourceDocumentIds,
    string? FollowUpQuestion);

public sealed record ChatAgentCompletion(
    ChatAgentResult Result,
    string Provider,
    string Model,
    IReadOnlyList<ChatToolExecution> ToolExecutions);

public sealed record ChatAgentRequest(
    Guid CompanyId,
    Guid ConversationId,
    Company Company,
    CompanyProfileVersion Profile,
    IReadOnlyList<ChatMessage> RecentMessages,
    string Question);

public interface ICompanyChatAgent
{
    Task<ChatAgentCompletion> RunAsync(ChatAgentRequest request, CancellationToken cancellationToken = default);
}

public interface ICompanyChatAgentFactory
{
    ICompanyChatAgent Create();
}

public interface ICompanyChatService
{
    Task<ChatConversationResponse> CreateConversationAsync(Guid companyId, CancellationToken cancellationToken);
    Task<ChatConversationResponse> GetConversationAsync(Guid companyId, Guid conversationId, CancellationToken cancellationToken);
    Task<SendChatMessageResponse> SendMessageAsync(Guid companyId, Guid conversationId, CreateChatMessageRequest request, CancellationToken cancellationToken);
}
