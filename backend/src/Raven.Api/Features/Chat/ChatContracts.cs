using Raven.Api.Features.Companies;
using Raven.Api.Features.Profiles;

namespace Raven.Api.Features.Chat;

// Answered is the only factual-answer status and therefore requires accepted
// profile evidence or a message-scoped Web Evidence Snapshot. Conversational
// and Guidance are deliberately non-factual.
public enum ChatAnswerStatus { Answered, Conversational, Guidance, ClarificationRequired, InsufficientEvidence, UnsupportedScope }
public enum ChatMessageRole { User, Assistant }
public enum ChatMessageStatus { Pending, Completed, Failed }
public enum ChatCitationOrigin { Profile, Web, Investigation, Briefing }
public enum ChatProgressStage { Analyzing, CheckingProfile, WebSearching, Crawling, Composing, Completed, Failed }

public sealed record CreateChatMessageRequest(string Question);

public sealed record ChatProgressEvent(
    ChatProgressStage Stage,
    string Message,
    int? Completed = null,
    int? Total = null);

public sealed record UpdateChatCapabilitiesRequest(bool WebSearchEnabled);

public sealed record ChatCitationResponse(
    ChatCitationOrigin Origin,
    Guid? SourceDocumentId,
    Guid? WebEvidenceSnapshotId,
    string? FieldPath,
    string? Title,
    string Url,
    DateTimeOffset RetrievedAt,
    Guid? InvestigationId = null,
    Guid? BriefingId = null,
    Guid? BriefingVersionId = null,
    int? BriefingVersionNumber = null);

public sealed record ChatWebEvidenceSnapshotResponse(
    Guid Id,
    string Url,
    string? Title,
    string? SearchSnippet,
    string ContentExcerpt,
    string SearchProvider,
    string? CrawlerProvider,
    int SearchRank,
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
    string? Activity,
    IReadOnlyList<ChatCitationResponse> Citations,
    IReadOnlyList<ChatWebEvidenceSnapshotResponse> WebEvidenceSnapshots,
    IReadOnlyList<ChatToolExecutionResponse> ToolExecutions,
    DateTimeOffset CreatedAt);

public sealed record ChatConversationResponse(
    Guid Id,
    Guid CompanyId,
    Guid ProfileVersionId,
    int ProfileVersion,
    bool WebSearchEnabled,
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
    IReadOnlyList<ChatWebEvidenceSnapshotResponse> WebEvidenceSnapshots,
    IReadOnlyList<ChatToolExecutionResponse> ToolExecutions,
    string? FollowUpQuestion);

public sealed record ChatAgentResult(
    ChatAnswerStatus Status,
    string Answer,
    IReadOnlyList<Guid> CitedSourceDocumentIds,
    string? FollowUpQuestion,
    IReadOnlyList<string>? CitedWebEvidenceCandidateIds = null,
    IReadOnlyList<Guid>? CitedInvestigationIds = null,
    IReadOnlyList<Guid>? CitedBriefingVersionIds = null);

public sealed record ChatAgentCompletion(
    ChatAgentResult Result,
    string Provider,
    string Model,
    IReadOnlyList<ChatToolExecution> ToolExecutions,
    IReadOnlyList<ChatWebEvidenceDraft>? WebEvidenceDrafts = null);

public sealed record ChatAgentRequest(
    Guid CompanyId,
    Guid ConversationId,
    Company Company,
    CompanyProfileVersion Profile,
    IReadOnlyList<ChatMessage> RecentMessages,
    string Question,
    bool WebSearchEnabled = false,
    Guid AssistantMessageId = default,
    IChatProgressReporter? ProgressReporter = null,
    IReadOnlyList<ChatInvestigationContext>? Investigations = null,
    Guid? RequiredInvestigationId = null,
    IReadOnlyList<ChatBriefingContext>? Briefings = null);

public sealed record ChatInvestigationContext(Guid Id, string Objective, string Summary, string Material, DateTimeOffset CompletedAt);
public sealed record ChatBriefingContext(Guid BriefingId, Guid VersionId, int VersionNumber, string Title,
    string Template, string Objective, string Material, DateTimeOffset GeneratedAt, DateTimeOffset ResearchThrough);

public interface IChatProgressReporter
{
    Task ReportAsync(ChatProgressEvent progress, CancellationToken cancellationToken);
}

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
    Task<ChatConversationResponse> UpdateCapabilitiesAsync(Guid companyId, Guid conversationId, UpdateChatCapabilitiesRequest request, CancellationToken cancellationToken);
    Task<SendChatMessageResponse> SendMessageAsync(Guid companyId, Guid conversationId, CreateChatMessageRequest request, CancellationToken cancellationToken);
    Task<SendChatMessageResponse> SendMessageStreamAsync(Guid companyId, Guid conversationId, CreateChatMessageRequest request, IChatProgressReporter progressReporter, CancellationToken cancellationToken);
    Task AnswerManagedResearchAsync(Guid companyId, Guid conversationId, Guid userMessageId, Guid jobId, CancellationToken cancellationToken);
}
