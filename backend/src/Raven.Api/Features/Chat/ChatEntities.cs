using Raven.Api.Features.Companies;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Research;

namespace Raven.Api.Features.Chat;

public sealed class ChatConversation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CompanyId { get; init; }
    public Company Company { get; init; } = null!;
    public Guid ProfileVersionId { get; init; }
    public CompanyProfileVersion ProfileVersion { get; init; } = null!;
    public string? Title { get; set; }
    /// <summary>
    /// User-controlled permission for web-enabled turns. Enabling this
    /// capability never requires the agent to call Search or Crawl.
    /// </summary>
    public bool WebSearchEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<ChatMessage> Messages { get; } = new List<ChatMessage>();
}

public sealed class ChatMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ConversationId { get; init; }
    public ChatConversation Conversation { get; init; } = null!;
    public ChatMessageRole Role { get; init; }
    public required string Content { get; set; }
    public ChatMessageStatus Status { get; set; } = ChatMessageStatus.Pending;
    /// <summary>
    /// Compatibility column retained because databases created by the first
    /// chatbot migration made this field non-nullable. Current web evidence is
    /// persisted through ChatWebEvidenceSnapshot; this field remains the safe
    /// default for older SQLite schemas.
    /// </summary>
    public bool WebLookupIncomplete { get; set; }
    public ChatAnswerStatus? AnswerStatus { get; set; }
    public string? FollowUpQuestion { get; set; }
    public string? AiProvider { get; set; }
    public string? AiModel { get; set; }
    public string? Activity { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public ICollection<ChatCitation> Citations { get; } = new List<ChatCitation>();
    public ICollection<ChatWebEvidenceSnapshot> WebEvidenceSnapshots { get; } = new List<ChatWebEvidenceSnapshot>();
    public ICollection<ChatToolExecution> ToolExecutions { get; } = new List<ChatToolExecution>();
}

/// <summary>
/// Bounded, immutable evidence captured by a web-enabled Chat turn.
/// It belongs to the conversation message only: it is not a SourceDocument and
/// cannot become accepted Company Profile evidence without an explicit flow.
/// </summary>
public sealed class ChatWebEvidenceSnapshot
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ChatMessageId { get; init; }
    public ChatMessage ChatMessage { get; init; } = null!;
    public required string Url { get; init; }
    public required string NormalizedUrl { get; init; }
    public string? Title { get; init; }
    public string? SearchSnippet { get; init; }
    public required string ContentExcerpt { get; init; }
    public required string SearchProvider { get; init; }
    public string? CrawlerProvider { get; init; }
    public int SearchRank { get; init; }
    public DateTimeOffset RetrievedAt { get; init; } = DateTimeOffset.UtcNow;
}
public sealed class ChatCitation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ChatMessageId { get; init; }
    public ChatMessage ChatMessage { get; init; } = null!;
    public Guid? SourceDocumentId { get; init; }
    public SourceDocument? SourceDocument { get; init; }
    public Guid? WebEvidenceSnapshotId { get; init; }
    public ChatWebEvidenceSnapshot? WebEvidenceSnapshot { get; init; }
    public string? FieldPath { get; init; }
    public ChatCitationOrigin Origin { get; init; } = ChatCitationOrigin.Profile;
    public string? Excerpt { get; init; }
}

public sealed class ChatToolExecution
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ChatMessageId { get; init; }
    public ChatMessage ChatMessage { get; init; } = null!;
    public Guid? ResearchRunId { get; init; }
    public required string Tool { get; init; }
    public required string Provider { get; init; }
    public required string Status { get; init; }
    public long DurationMs { get; init; }
    public string? InputSummary { get; init; }
    public string? OutputSummary { get; init; }
    public string? ErrorCode { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public ResearchRun? ResearchRun { get; init; }
}
