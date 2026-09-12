using Raven.Api.Features.Research;

namespace Raven.Api.Features.Research.Events;

/// <summary>
/// A persisted, bounded description of something that happened during research.
/// Event payloads deliberately contain summaries and references, not raw provider
/// responses or hidden model reasoning.
/// </summary>
public sealed record ResearchEvent
{
    public const string LegacyOperation = "legacy";

    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? ResearchRunId { get; init; }
    public Guid? ConversationId { get; init; }
    public long Sequence { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public long? DurationMs { get; init; }
    public ResearchStage? Stage { get; init; }
    public ResearchEventCategory Category { get; init; }
    /// <summary>
    /// Stable operation purpose, independent of lifecycle status. Existing rows
    /// are retained with <c>legacy</c> and are interpreted from their category by
    /// the execution read model.
    /// </summary>
    public string Operation { get; init; } = LegacyOperation;
    public ResearchEventStatus Status { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? ToolName { get; init; }
    public string? InputSummary { get; init; }
    public string? OutputSummary { get; init; }
    public int? HttpStatus { get; init; }
    public string? ExternalRequestId { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? CachedTokens { get; init; }
    public int? ThinkingTokens { get; init; }
    public decimal? EstimatedCost { get; init; }
    public string? PromptTemplateVersion { get; init; }
    public string? InputHash { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? MetadataJson { get; init; }
}

/// <summary>
/// Event types are intentionally provider-neutral. Provider adapters report these
/// facts; they do not expose their SDK event models to the research domain.
/// </summary>
public enum ResearchEventCategory
{
    // Canonical execution categories. Lifecycle is represented by Status and
    // operation purpose by Operation; these values are intentionally stable.
    Search,
    Crawl,
    AI,
    Tool,
    Parsing,
    Identity,
    Profile,
    Research,
    Monitoring,

    // Legacy names remain readable so existing ResearchEvents can be loaded
    // after the vocabulary cleanup. New execution telemetry should use one of
    // the canonical values above and a bounded Operation.
    SearchRequested,
    SearchCompleted,
    CandidateDiscovery,
    CandidateRanking,
    OfficialDomainDiscovery,
    GroundingRequested,
    GroundingCompleted,
    GroundingFailed,
    IdentitySelected,
    SourceSemanticRerankStarted,
    SourceSemanticRerankCompleted,
    ProviderFallback,
    CrawlRequested,
    CrawlCompleted,
    CrawlFailed,
    DuplicateSkipped,
    SourcePersisted,
    StructuredSourceParsed,
    AiRequested,
    AiCompleted,
    AiFailed,
    ProfileValidated,
    ProfileConfirmed,
    MonitoringRunStarted,
    MonitoringUpdateReady
}

/// <summary>
/// Status is suitable for activity timelines as well as diagnostics. A separate
/// category records what happened, so a failed crawl can be represented as
/// CrawlFailed/Failed without inventing provider-specific states.
/// </summary>
public enum ResearchEventStatus
{
    Working,
    WaitingForUser,
    Completed,
    Failed,
    Skipped
}

/// <summary>
/// Input used by instrumentation before persistence assigns event identity and
/// sequence. It is intentionally separate from <see cref="ResearchEvent"/> so
/// callers cannot accidentally reuse a provider response as an event entity.
/// </summary>
public sealed record ResearchEventDraft
{
    public Guid? ResearchRunId { get; init; }
    public Guid? ConversationId { get; init; }
    public long? DurationMs { get; init; }
    public ResearchStage? Stage { get; init; }
    public ResearchEventCategory Category { get; init; }
    public string Operation { get; init; } = ResearchEvent.LegacyOperation;
    public ResearchEventStatus Status { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? ToolName { get; init; }
    public string? InputSummary { get; init; }
    public string? OutputSummary { get; init; }
    public int? HttpStatus { get; init; }
    public string? ExternalRequestId { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? CachedTokens { get; init; }
    public int? ThinkingTokens { get; init; }
    public decimal? EstimatedCost { get; init; }
    public string? PromptTemplateVersion { get; init; }
    public string? InputHash { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? MetadataJson { get; init; }
}
