using System.Text.Json;

namespace Raven.Api.Features.ManagedResearch;

/// <summary>The durable lifecycle of one managed, company-scoped research job.</summary>
public enum ManagedResearchJobStatus
{
    Queued,
    Researching,
    Completed,
    Failed,
    Cancelled
}

/// <summary>The provider lifecycle exposed by a managed research adapter.</summary>
public enum ManagedResearchProviderRunStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>Provider effort values accepted by Exa Agent.</summary>
public enum ManagedResearchEffort
{
    Auto,
    Low,
    Medium,
    High,
    XHigh
}

/// <summary>
/// Durable state for a managed research execution. The result is research
/// material only; this entity has no profile mutation operation by design.
/// </summary>
public sealed class ManagedResearchJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CompanyId { get; init; }
    public Guid? ConversationId { get; init; }
    public Guid? ChatMessageId { get; init; }
    public required string Objective { get; init; }
    public required string ProviderQuery { get; init; }
    public string Effort { get; init; } = "auto";
    public ManagedResearchJobStatus Status { get; set; } = ManagedResearchJobStatus.Queued;
    public string? Provider { get; set; }
    public string? ProviderRunId { get; set; }
    public ManagedResearchProviderRunStatus? ProviderRunStatus { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelRequestedAt { get; set; }
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
    public decimal? ProviderCostDollars { get; set; }
    public Guid? InvestigationId { get; set; }

    public bool IsTerminal => Status is
        ManagedResearchJobStatus.Completed or
        ManagedResearchJobStatus.Failed or
        ManagedResearchJobStatus.Cancelled;

    public void MarkResearching(
        string providerRunId,
        DateTimeOffset now,
        ManagedResearchProviderRunStatus providerStatus = ManagedResearchProviderRunStatus.Queued)
    {
        if (IsTerminal)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(providerRunId))
        {
            throw new ArgumentException("A provider run ID is required.", nameof(providerRunId));
        }

        ProviderRunId = providerRunId.Trim();
        ProviderRunStatus = providerStatus;
        Status = ManagedResearchJobStatus.Researching;
        StartedAt ??= now;
        Error = null;
    }

    public void Complete(
        string resultJson,
        Guid investigationId,
        DateTimeOffset now,
        decimal? providerCostDollars = null)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            throw new ArgumentException("A completed managed research job requires a result.", nameof(resultJson));
        }

        if (investigationId == Guid.Empty)
        {
            throw new ArgumentException("A completed managed research job requires an investigation ID.", nameof(investigationId));
        }

        Status = ManagedResearchJobStatus.Completed;
        ProviderRunStatus = ManagedResearchProviderRunStatus.Completed;
        CompletedAt = now;
        ResultJson = resultJson;
        InvestigationId = investigationId;
        ProviderCostDollars = providerCostDollars;
        Error = null;
    }

    public void Fail(string error, DateTimeOffset now)
    {
        Status = ManagedResearchJobStatus.Failed;
        ProviderRunStatus = ManagedResearchProviderRunStatus.Failed;
        CompletedAt ??= now;
        Error = ManagedResearchText.Bound(error, ManagedResearchLimits.MaxErrorLength);
        ResultJson = null;
    }

    public void RequestCancellation(DateTimeOffset now)
    {
        if (!IsTerminal)
        {
            CancelRequestedAt ??= now;
        }
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status == ManagedResearchJobStatus.Completed || Status == ManagedResearchJobStatus.Failed)
        {
            return;
        }

        Status = ManagedResearchJobStatus.Cancelled;
        ProviderRunStatus = ManagedResearchProviderRunStatus.Cancelled;
        CompletedAt ??= now;
        ResultJson = null;
        Error = null;
    }
}

/// <summary>HTTP/application input for starting managed research.</summary>
public sealed record StartManagedResearchRequest(
    string Objective,
    Guid? ConversationId = null,
    Guid? ChatMessageId = null,
    ManagedResearchEffort Effort = ManagedResearchEffort.Auto);

/// <summary>Safe, pollable managed research job response.</summary>
public sealed record ManagedResearchJobResponse(
    Guid Id,
    Guid CompanyId,
    Guid? ConversationId,
    Guid? ChatMessageId,
    string Objective,
    string Provider,
    ManagedResearchJobStatus Status,
    string? ProviderRunId,
    ManagedResearchProviderRunStatus? ProviderRunStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CancelRequestedAt,
    ManagedResearchResult? Result,
    decimal? ProviderCostDollars,
    Guid? InvestigationId,
    string? Error);

/// <summary>
/// Read-only company/profile context used to build a focused provider query.
/// Accepted profile values are hints to the provider, never profile mutations.
/// </summary>
public sealed record ManagedResearchCompanyContext
{
    public required Guid CompanyId { get; init; }
    public required string DisplayName { get; init; }
    public string? LegalName { get; init; }
    public string? OfficialWebsite { get; init; }
    public string? Country { get; init; }
    public string? Headquarters { get; init; }
    public string? AcceptedProfileSummary { get; init; }
    public IReadOnlyList<string> EvidenceGaps { get; init; } = [];
}

/// <summary>Read-only application seam for obtaining company and profile context.</summary>
public interface IManagedResearchCompanyContextReader
{
    Task<ManagedResearchCompanyContext?> GetAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);
}

/// <summary>Provider-neutral create request passed to a managed research client.</summary>
public sealed record ManagedResearchAgentCreateRequest(
    string Query,
    ManagedResearchEffort Effort = ManagedResearchEffort.Auto);

/// <summary>A provider citation retained as a source lead.</summary>
public sealed record ManagedResearchCitation(string Url, string? Title = null, string? Field = null);

/// <summary>Provider output before RAVEN normalizes it into research material.</summary>
public sealed record ManagedResearchProviderOutput(
    string? Text,
    JsonElement? Structured,
    IReadOnlyList<ManagedResearchCitation>? Grounding = null)
{
    public IReadOnlyList<ManagedResearchCitation> Grounding { get; init; } = Grounding ?? [];
}

/// <summary>Provider-neutral lifecycle result from the managed research client.</summary>
public sealed record ManagedResearchProviderRun(
    string Id,
    ManagedResearchProviderRunStatus Status,
    string Provider,
    ManagedResearchProviderOutput? Output = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? CompletedAt = null,
    string? Error = null,
    string? StopReason = null,
    decimal? CostDollars = null,
    IReadOnlyDictionary<string, string>? SafeMetadata = null)
{
    public IReadOnlyDictionary<string, string> SafeMetadata { get; init; } = SafeMetadata ??
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Normalized claim retained for review; provider confidence is advisory.</summary>
public sealed record ManagedResearchClaim(
    string Topic,
    string Statement,
    IReadOnlyList<string>? SupportingSourceUrls = null,
    decimal? ProviderConfidence = null,
    string? Notes = null)
{
    public IReadOnlyList<string> SupportingSourceUrls { get; init; } = SupportingSourceUrls ?? [];
}

/// <summary>A cited source lead associated with managed research.</summary>
public sealed record ManagedResearchSource(
    string Title,
    string Url,
    string? Publisher = null,
    DateTimeOffset? PublishedAt = null,
    string? Supports = null);

/// <summary>
/// Provider-neutral managed research material. It is never accepted Company
/// Profile truth without the normal RAVEN review/evidence workflow.
/// </summary>
public sealed record ManagedResearchResult(
    string Provider,
    string Objective,
    string Summary,
    IReadOnlyList<ManagedResearchClaim>? Claims = null,
    IReadOnlyList<ManagedResearchSource>? Sources = null,
    IReadOnlyList<string>? Uncertainties = null,
    DateTimeOffset? CompletedAt = null,
    IReadOnlyDictionary<string, string>? SafeMetadata = null,
    decimal? ProviderCostDollars = null)
{
    public IReadOnlyList<ManagedResearchClaim> Claims { get; init; } = Claims ?? [];
    public IReadOnlyList<ManagedResearchSource> Sources { get; init; } = Sources ?? [];
    public IReadOnlyList<string> Uncertainties { get; init; } = Uncertainties ?? [];
    public IReadOnlyDictionary<string, string> SafeMetadata { get; init; } = SafeMetadata ??
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Client boundary for Exa Agent or a future managed provider.</summary>
public interface IManagedResearchAgentClient
{
    Task<ManagedResearchProviderRun> CreateAsync(
        ManagedResearchAgentCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<ManagedResearchProviderRun> GetAsync(
        string providerRunId,
        CancellationToken cancellationToken = default);

    Task<ManagedResearchProviderRun> CancelAsync(
        string providerRunId,
        CancellationToken cancellationToken = default);
}

/// <summary>Durable job persistence seam. EF can implement this without changing the provider client.</summary>
public interface IManagedResearchJobStore
{
    Task AddAsync(ManagedResearchJob job, CancellationToken cancellationToken = default);
    Task<ManagedResearchJob?> GetAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<ManagedResearchJob?> GetAsync(Guid companyId, Guid jobId, CancellationToken cancellationToken = default);
    Task UpdateAsync(ManagedResearchJob job, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManagedResearchJob>> ListActiveAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManagedResearchJob>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);
}

/// <summary>Persisted investigation handoff created after provider completion.</summary>
public sealed class ManagedResearchInvestigation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid JobId { get; init; }
    public Guid CompanyId { get; init; }
    public Guid? ConversationId { get; init; }
    public Guid? ChatMessageId { get; init; }
    public required string Origin { get; init; } = "ManagedAi";
    public required string Objective { get; init; }
    public required string Summary { get; init; }
    public required string ResultJson { get; init; }
    public DateTimeOffset CompletedAt { get; init; }
}

/// <summary>Persistence seam for a managed investigation; it cannot write profile truth.</summary>
public interface IManagedResearchInvestigationStore
{
    Task SaveAsync(ManagedResearchInvestigation investigation, CancellationToken cancellationToken = default);
    Task<ManagedResearchInvestigation?> GetAsync(Guid companyId, Guid investigationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManagedResearchInvestigation>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);
}

/// <summary>Application service for queueing, polling, and cancelling managed jobs.</summary>
public interface IManagedResearchJobService
{
    Task<ManagedResearchJobResponse> StartAsync(Guid companyId, StartManagedResearchRequest request, CancellationToken cancellationToken = default);
    Task<ManagedResearchJobResponse?> GetAsync(Guid companyId, Guid jobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ManagedResearchJobResponse>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<ManagedResearchJobResponse?> CancelAsync(Guid companyId, Guid jobId, CancellationToken cancellationToken = default);
    Task<ManagedResearchJobResponse?> ProcessAsync(Guid jobId, CancellationToken cancellationToken = default);
}

/// <summary>Small in-process queue; the durable job row remains the source of truth.</summary>
public interface IManagedResearchJobQueue
{
    ValueTask EnqueueAsync(Guid jobId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken = default);
}

public static class ManagedResearchLimits
{
    public const int MaxObjectiveLength = 4_000;
    public const int MaxQueryLength = 12_000;
    public const int MaxSummaryLength = 100_000;
    public const int MaxErrorLength = 4_000;
    public const int MaxClaims = 200;
    public const int MaxSources = 500;
    public const int MaxUncertainties = 100;
}

internal static class ManagedResearchText
{
    public static string Bound(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim()[..Math.Min(maxLength, value.Trim().Length)];
}
