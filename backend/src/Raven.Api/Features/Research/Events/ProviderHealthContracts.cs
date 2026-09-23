using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.ManagedResearch;

namespace Raven.Api.Features.Research.Events;

/// <summary>Recent provider health derived from real RAVEN requests, without active provider probes.</summary>
public sealed record ProviderHealthResponse(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ProviderModelHealthResponse> Models,
    IReadOnlyList<ProviderActivityItemResponse> RecentActivity);

/// <summary>Recent health for one provider/model combination observed by RAVEN.</summary>
public sealed record ProviderModelHealthResponse(
    string Provider,
    string? Model,
    string State,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    int? LastFailureHttpStatus,
    string? LastFailureCode,
    string? LastFailureSummary,
    int RequestsLastMinute);

/// <summary>A safe, bounded summary of one persisted provider operation.</summary>
public sealed record ProviderActivityItemResponse(
    DateTimeOffset Timestamp,
    string Provider,
    string? Model,
    string Operation,
    string Status,
    int? HttpStatus,
    string? FailureKind);

public interface IProviderHealthService
{
    Task<ProviderHealthResponse> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>Builds a read-only provider health view from existing execution telemetry.</summary>
public sealed class ProviderHealthService(RavenDbContext db) : IProviderHealthService
{
    public async Task<ProviderHealthResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var dayAgo = now.AddHours(-24);
        var minuteAgo = now.AddMinutes(-1);
        var recentEvents = await db.ResearchEvents.AsNoTracking()
            .Where(item => item.Provider != null && item.Sequence >= dayAgo.UtcTicks)
            .OrderByDescending(item => item.Sequence)
            .Take(500)
            .ToListAsync(cancellationToken);
        var events = recentEvents
            .Where(item => item.Timestamp >= dayAgo && IsProviderCallCategory(item.Category))
            .ToArray();
        var managedJobSnapshots = await db.ManagedResearchJobs.AsNoTracking()
            .Where(job => job.Provider != null)
            .Select(job => new ManagedResearchJobHealthSnapshot(
                job.Provider, job.Status, job.CreatedAt, job.StartedAt, job.CompletedAt))
            .Take(500)
            .ToListAsync(cancellationToken);
        var managedJobs = managedJobSnapshots
            .Where(job => ManagedActivityTimestamp(job) >= dayAgo)
            .OrderByDescending(ManagedActivityTimestamp)
            .Take(100)
            .ToArray();

        var models = events.GroupBy(item => new { Provider = item.Provider!, item.Model })
            .Select(group => ToModelHealth(group.ToArray(), minuteAgo))
            .Concat(managedJobs.Length == 0 ? [] : [ToManagedModelHealth(managedJobs, minuteAgo)])
            .OrderBy(item => item.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Model, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var activity = events.Select(ToActivity).Concat(managedJobs.Select(ToManagedActivity))
            .OrderByDescending(item => item.Timestamp).Take(30).ToArray();
        return new ProviderHealthResponse(now, models, activity);
    }

    private static ProviderModelHealthResponse ToModelHealth(IReadOnlyCollection<ResearchEvent> events, DateTimeOffset minuteAgo)
    {
        var latest = events.MaxBy(item => item.Timestamp)!;
        var lastSuccess = events.Where(IsSuccessful).MaxBy(item => item.Timestamp);
        var lastFailure = events.Where(item => item.Status == ResearchEventStatus.Failed).MaxBy(item => item.Timestamp);
        var failureIsCurrent = lastFailure is not null && (lastSuccess is null || lastFailure.Timestamp > lastSuccess.Timestamp);
        var state = failureIsCurrent ? "Degraded" : lastSuccess is not null ? "RecentlyHealthy" : "Degraded";
        return new ProviderModelHealthResponse(
            latest.Provider!, latest.Model, state, lastSuccess?.Timestamp, lastFailure?.Timestamp,
            lastFailure?.HttpStatus, lastFailure?.ErrorCode,
            failureIsCurrent ? FailureSummary(lastFailure!) : null,
            events.Count(item => item.Timestamp >= minuteAgo));
    }

    private static ProviderActivityItemResponse ToActivity(ResearchEvent item) =>
        new(item.Timestamp, item.Provider!, item.Model, ResearchEventSemantics.OperationFor(item),
            item.Status.ToString(), item.HttpStatus,
            item.Status == ResearchEventStatus.Failed ? FailureKind(item.HttpStatus, item.ErrorCode) : null);

    private static ProviderModelHealthResponse ToManagedModelHealth(IReadOnlyCollection<ManagedResearchJobHealthSnapshot> jobs, DateTimeOffset minuteAgo)
    {
        var lastSuccess = jobs.Where(job => job.Status is ManagedResearchJobStatus.Completed or ManagedResearchJobStatus.Researching)
            .MaxBy(ManagedActivityTimestamp);
        var lastFailure = jobs.Where(job => job.Status == ManagedResearchJobStatus.Failed).MaxBy(ManagedActivityTimestamp);
        DateTimeOffset? lastSuccessAt = lastSuccess is null ? null : ManagedActivityTimestamp(lastSuccess);
        DateTimeOffset? lastFailureAt = lastFailure is null ? null : ManagedActivityTimestamp(lastFailure);
        var failureIsCurrent = lastFailureAt is not null && (lastSuccessAt is null || lastFailureAt > lastSuccessAt);
        return new ProviderModelHealthResponse(
            jobs.First().Provider ?? "exa-agent", null,
            failureIsCurrent ? "Degraded" : lastSuccessAt is not null ? "RecentlyHealthy" : "Configured",
            lastSuccessAt, lastFailureAt, null, failureIsCurrent ? "provider_error" : null,
            failureIsCurrent ? "Managed Deep Research request failed." : null,
            jobs.Count(job => (job.StartedAt ?? job.CreatedAt) >= minuteAgo));
    }

    private static ProviderActivityItemResponse ToManagedActivity(ManagedResearchJobHealthSnapshot job) =>
        new(ManagedActivityTimestamp(job), job.Provider ?? "exa-agent", null, "managed_deep_research",
            job.Status switch
            {
                ManagedResearchJobStatus.Completed => "Completed",
                ManagedResearchJobStatus.Failed => "Failed",
                ManagedResearchJobStatus.Researching => "Working",
                ManagedResearchJobStatus.Cancelled => "Skipped",
                _ => "Queued"
            }, null, job.Status == ManagedResearchJobStatus.Failed ? "ProviderError" : null);

    private static DateTimeOffset ManagedActivityTimestamp(ManagedResearchJobHealthSnapshot job) =>
        job.CompletedAt ?? job.StartedAt ?? job.CreatedAt;

    private static bool IsProviderCallCategory(ResearchEventCategory category) => category is
        ResearchEventCategory.AI or ResearchEventCategory.Research or ResearchEventCategory.Tool or
        ResearchEventCategory.Search or ResearchEventCategory.Crawl or ResearchEventCategory.AiRequested or
        ResearchEventCategory.AiCompleted or ResearchEventCategory.AiFailed or ResearchEventCategory.SearchRequested or
        ResearchEventCategory.SearchCompleted or ResearchEventCategory.CrawlRequested or
        ResearchEventCategory.CrawlCompleted or ResearchEventCategory.CrawlFailed;

    private static bool IsSuccessful(ResearchEvent item) => item.Status == ResearchEventStatus.Completed;

    private static string FailureSummary(ResearchEvent item) => FailureKind(item.HttpStatus, item.ErrorCode) switch
    {
        "RateLimited" => "Rate limited by the provider.",
        "ProviderBusy" => "Provider temporarily unavailable.",
        "Credentials" => "Provider credentials were rejected.",
        "RequestRejected" => "The provider rejected the request.",
        "TimedOut" => "The provider request timed out.",
        _ => "The provider request failed."
    };

    private static string FailureKind(int? httpStatus, string? code)
    {
        if (httpStatus == 429 || code?.Contains("rate", StringComparison.OrdinalIgnoreCase) == true ||
            code?.Contains("quota", StringComparison.OrdinalIgnoreCase) == true ||
            code?.Contains("resource_exhausted", StringComparison.OrdinalIgnoreCase) == true) return "RateLimited";
        if (httpStatus == 503 || httpStatus == 502 || code?.Contains("unavailable", StringComparison.OrdinalIgnoreCase) == true) return "ProviderBusy";
        if (httpStatus is 401 or 403) return "Credentials";
        if (httpStatus == 400) return "RequestRejected";
        if (code?.Contains("timeout", StringComparison.OrdinalIgnoreCase) == true) return "TimedOut";
        return "ProviderError";
    }

    private sealed record ManagedResearchJobHealthSnapshot(
        string? Provider,
        ManagedResearchJobStatus Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt);
}
