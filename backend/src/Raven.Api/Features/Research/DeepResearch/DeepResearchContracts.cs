using Raven.Api.Features.Crawling;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Research;
using Raven.Api.Features.Search;

namespace Raven.Api.Features.DeepResearch;

/// <summary>The durable lifecycle of one company-scoped Deep Research execution.</summary>
public enum DeepResearchRunStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>Public, provider-neutral activity states. They intentionally omit model messages and reasoning.</summary>
public enum DeepResearchActivityStatus
{
    Working,
    Completed,
    Failed,
    Skipped
}

/// <summary>
/// A bounded, company-scoped budget for an agent run. Every external tool call
/// consumes both a general tool unit and (where applicable) a capability unit.
/// </summary>
public sealed record DeepResearchBudget(
    int MaxToolCalls = DeepResearchBudget.Defaults.MaxToolCalls,
    int MaxSearchCalls = DeepResearchBudget.Defaults.MaxSearchCalls,
    int MaxCrawlCalls = DeepResearchBudget.Defaults.MaxCrawlCalls,
    int MaxDocuments = DeepResearchBudget.Defaults.MaxDocuments,
    TimeSpan? MaxDuration = null)
{
    public static DeepResearchBudget Default { get; } = new();

    public TimeSpan EffectiveMaxDuration => MaxDuration ?? Defaults.MaxDuration;

    public DeepResearchBudget Normalize()
    {
        if (MaxToolCalls < 1 || MaxSearchCalls < 0 || MaxCrawlCalls < 0 || MaxDocuments < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxToolCalls), "Deep Research budgets must be non-negative and allow at least one tool call.");
        }

        if (EffectiveMaxDuration <= TimeSpan.Zero || EffectiveMaxDuration > Defaults.MaximumAllowedDuration)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDuration), $"Deep Research duration must be greater than zero and no longer than {Defaults.MaximumAllowedDuration}.");
        }

        return this with { MaxDuration = EffectiveMaxDuration };
    }

    public static class Defaults
    {
        public const int MaxToolCalls = 15;
        public const int MaxSearchCalls = 6;
        public const int MaxCrawlCalls = 10;
        public const int MaxDocuments = 12;
        public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan MaximumAllowedDuration = TimeSpan.FromMinutes(30);
    }
}

/// <summary>
/// Persisted execution state. It deliberately does not reference a profile
/// candidate or expose any mutating profile operation.
/// </summary>
public sealed class DeepResearchRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CompanyId { get; init; }
    public Guid? ConversationId { get; init; }
    public required string Question { get; init; }
    public required string Model { get; init; }
    public DeepResearchRunStatus Status { get; set; } = DeepResearchRunStatus.Queued;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelRequestedAt { get; set; }
    public int ToolCalls { get; set; }
    public int SearchCalls { get; set; }
    public int CrawlCalls { get; set; }
    public int DocumentsRead { get; set; }
    public string? ResultMarkdown { get; set; }
    public string? Error { get; set; }

    public bool IsTerminal => Status is DeepResearchRunStatus.Completed or DeepResearchRunStatus.Failed or DeepResearchRunStatus.Cancelled;

    public void Start(DateTimeOffset? now = null)
    {
        if (Status != DeepResearchRunStatus.Queued)
        {
            throw new InvalidOperationException($"A Deep Research run in {Status} state cannot start.");
        }

        Status = DeepResearchRunStatus.Running;
        StartedAt = now ?? DateTimeOffset.UtcNow;
        Error = null;
    }

    public void RequestCancellation(DateTimeOffset? now = null)
    {
        if (IsTerminal)
        {
            return;
        }

        CancelRequestedAt ??= now ?? DateTimeOffset.UtcNow;
    }

    public void Complete(string markdown, DateTimeOffset? now = null)
    {
        if (Status != DeepResearchRunStatus.Running)
        {
            throw new InvalidOperationException($"A Deep Research run in {Status} state cannot complete.");
        }

        Status = DeepResearchRunStatus.Completed;
        CompletedAt = now ?? DateTimeOffset.UtcNow;
        ResultMarkdown = markdown;
        Error = null;
    }

    public void Fail(string error, DateTimeOffset? now = null)
    {
        if (IsTerminal)
        {
            return;
        }

        Status = DeepResearchRunStatus.Failed;
        CompletedAt = now ?? DateTimeOffset.UtcNow;
        Error = DeepResearchText.Bound(error, 4_000);
    }

    public void Cancel(DateTimeOffset? now = null)
    {
        if (IsTerminal && Status != DeepResearchRunStatus.Cancelled)
        {
            return;
        }

        Status = DeepResearchRunStatus.Cancelled;
        CompletedAt ??= now ?? DateTimeOffset.UtcNow;
        ResultMarkdown = null;
        Error = null;
    }
}

public sealed record StartDeepResearchRequest(
    string Question,
    Guid? ConversationId = null,
    string? Model = null,
    DeepResearchBudget? Budget = null);

public sealed record DeepResearchRunResponse(
    Guid Id,
    Guid CompanyId,
    Guid? ConversationId,
    string Question,
    DeepResearchRunStatus Status,
    string Model,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelRequestedAt,
    int ToolCalls,
    int SearchCalls,
    int CrawlCalls,
    int DocumentsRead,
    string? ResultMarkdown,
    string? Error);

/// <summary>
/// The only public activity payload. It intentionally has no prompt, model
/// message, tool arguments, tool output, chain-of-thought, or exception body.
/// </summary>
public sealed record DeepResearchActivityEvent(
    string Type,
    DeepResearchActivityStatus Status,
    string Label,
    string? Detail = null,
    string? Provider = null,
    IReadOnlyList<Guid>? SourceDocumentIds = null)
{
    public IReadOnlyList<Guid> SourceDocumentIds { get; init; } = SourceDocumentIds ?? [];
}

public interface IDeepResearchRunStore
{
    Task AddAsync(DeepResearchRun run, CancellationToken cancellationToken = default);
    Task<DeepResearchRun?> GetAsync(Guid runId, CancellationToken cancellationToken = default);
    Task UpdateAsync(DeepResearchRun run, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeepResearchRun>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);
}

public interface IDeepResearchActivityStore
{
    Task AddAsync(Guid runId, DeepResearchActivityEvent activity, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeepResearchActivityEvent>> ListAsync(Guid runId, CancellationToken cancellationToken = default);
}

public interface IDeepResearchActivitySink
{
    Task PublishAsync(Guid runId, DeepResearchActivityEvent activity, CancellationToken cancellationToken = default);
}

public sealed record DeepResearchAgentRequest(
    Guid RunId,
    Guid CompanyId,
    string Question,
    string Model,
    DeepResearchBudget Budget,
    IDeepResearchToolset Tools,
    IDeepResearchActivitySink? Activity = null);

public sealed record DeepResearchUsage(
    int ToolCalls,
    int SearchCalls,
    int CrawlCalls,
    int DocumentsRead);

public sealed record DeepResearchAgentResult(
    string? ResultMarkdown,
    string? Error,
    DeepResearchUsage Usage,
    bool Cancelled = false);

public interface IDeepResearchAgent
{
    Task<DeepResearchAgentResult> RunAsync(
        DeepResearchAgentRequest request,
        CancellationToken cancellationToken = default);
}

public enum DeepResearchToolKind
{
    GetCompanyProfile,
    GetCompanySources,
    SearchWeb,
    CrawlPage,
    SearchStoredSourceText
}

public sealed record DeepResearchProfile(
    Guid CompanyId,
    int? Version,
    string? DisplayName,
    string? LegalName,
    string? Website,
    string? Country,
    string? Headquarters,
    string? PrimaryIndustry,
    string? Summary);

public sealed record DeepResearchSource(
    Guid SourceDocumentId,
    string Url,
    string? Title,
    string SourceKind,
    string Content,
    DateTimeOffset RetrievedAt);

public sealed record DeepResearchSearchHit(string Title, string Url, string? Snippet, int Rank);

public sealed record DeepResearchCrawlPage(
    string RequestedUrl,
    string? FinalUrl,
    string? Title,
    string? Markdown,
    string Provider,
    DateTimeOffset RetrievedAt,
    Guid? SourceDocumentId = null);

public sealed record DeepResearchToolResult<T>(
    bool Succeeded,
    T? Value,
    string? Error,
    string? Provider = null,
    IReadOnlyList<Guid>? SourceDocumentIds = null)
{
    public IReadOnlyList<Guid> SourceDocumentIds { get; init; } = SourceDocumentIds ?? [];
}

/// <summary>Read-only tool boundary exposed to the MAF agent.</summary>
public interface IDeepResearchToolset
{
    bool SupportsStoredSourceTextSearch => false;

    Task<DeepResearchToolResult<DeepResearchProfile>> GetCompanyProfileAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<DeepResearchToolResult<IReadOnlyList<DeepResearchSource>>> GetCompanySourcesAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<DeepResearchToolResult<IReadOnlyList<DeepResearchSearchHit>>> SearchWebAsync(string query, int maxResults, CancellationToken cancellationToken = default);
    Task<DeepResearchToolResult<DeepResearchCrawlPage>> CrawlPageAsync(string url, CancellationToken cancellationToken = default);
    Task<DeepResearchToolResult<IReadOnlyList<DeepResearchSource>>> SearchStoredSourceTextAsync(Guid companyId, string query, int maxResults, CancellationToken cancellationToken = default);
}

internal static class DeepResearchText
{
    public static string Bound(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim()[..Math.Min(maxLength, value.Trim().Length)];
}
