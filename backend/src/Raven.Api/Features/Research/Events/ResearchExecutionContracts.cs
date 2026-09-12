using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;

namespace Raven.Api.Features.Research.Events;

/// <summary>Developer-facing execution details for one research run.</summary>
public sealed record ResearchExecutionResponse(
    Guid ResearchRunId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    ResearchExecutionSummaryResponse Summary,
    IReadOnlyList<ExecutionOperationResponse> Operations);

/// <summary>A safe, persisted execution operation projection.</summary>
public sealed record ExecutionOperationResponse(
    Guid Id,
    Guid? ResearchRunId,
    Guid? ConversationId,
    long Sequence,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    long? DurationMs,
    ResearchEventCategory Category,
    string Operation,
    ResearchEventStatus Status,
    string? Provider,
    string? Model,
    string? ToolName,
    string? InputSummary,
    string? OutputSummary,
    int? InputTokens,
    int? OutputTokens,
    int? CachedTokens,
    int? ThinkingTokens,
    int? HttpStatus,
    string? ExternalRequestId,
    string? ErrorCode,
    string? ErrorMessage,
    string? PromptTemplateVersion,
    string? MetadataJson);

/// <summary>
/// Aggregated execution metrics. Nullable token values deliberately distinguish
/// "provider did not expose usage" from a real zero-token result.
/// </summary>
public sealed record ResearchExecutionSummaryResponse(
    Guid ResearchRunId,
    long TotalWallClockDurationMs,
    int SearchCalls,
    int ProviderAttempts,
    int Fallbacks,
    int CrawlCalls,
    int SuccessfulCrawls,
    int FailedCrawls,
    int AiCalls,
    int? InputTokens,
    int? OutputTokens,
    int? CachedTokens,
    int? ThinkingTokens,
    int Failures,
    int DocumentsAcquired,
    int TelemetryOperations)
{
    // Names used by the execution vocabulary and benchmark reports.
    public int LogicalSearchCalls => SearchCalls;
    public long TotalDurationMs => TotalWallClockDurationMs;
}

public interface IResearchExecutionService
{
    Task<ResearchExecutionResponse?> GetAsync(Guid researchRunId, CancellationToken cancellationToken = default);
}

/// <summary>Reads execution telemetry without changing the research workflow.</summary>
public sealed class ResearchExecutionService(RavenDbContext dbContext) : IResearchExecutionService
{
    public async Task<ResearchExecutionResponse?> GetAsync(
        Guid researchRunId,
        CancellationToken cancellationToken = default)
    {
        var run = await dbContext.ResearchRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == researchRunId, cancellationToken);
        if (run is null)
        {
            return null;
        }

        var events = await dbContext.ResearchEvents
            .AsNoTracking()
            .Where(item => item.ResearchRunId == researchRunId)
            .OrderBy(item => item.Timestamp)
            .ThenBy(item => item.Sequence)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

        var operations = events.Select(ToResponse).ToArray();
        return new ResearchExecutionResponse(
            run.Id,
            run.StartedAt,
            run.CompletedAt,
            ResearchExecutionSummaryAggregator.Aggregate(run, events),
            operations);
    }

    private static ExecutionOperationResponse ToResponse(ResearchEvent item)
    {
        var operation = ResearchEventSemantics.OperationFor(item);
        DateTimeOffset? completedAt = item.Status is ResearchEventStatus.Completed or ResearchEventStatus.Failed or ResearchEventStatus.Skipped
            ? item.DurationMs is { } duration
                ? item.Timestamp.AddMilliseconds(Math.Max(0, duration))
                : item.Timestamp
            : null;

        return new ExecutionOperationResponse(
            item.Id,
            item.ResearchRunId,
            item.ConversationId,
            item.Sequence,
            item.Timestamp,
            completedAt,
            item.DurationMs,
            ResearchEventSemantics.CanonicalCategory(item.Category),
            operation,
            item.Status,
            item.Provider,
            item.Model,
            item.ToolName,
            item.InputSummary,
            item.OutputSummary,
            item.InputTokens,
            item.OutputTokens,
            item.CachedTokens,
            item.ThinkingTokens,
            item.HttpStatus,
            item.ExternalRequestId,
            item.ErrorCode,
            item.ErrorMessage,
            item.PromptTemplateVersion,
            item.MetadataJson);
    }
}

/// <summary>Pure aggregation logic, kept independently testable from EF.</summary>
public static class ResearchExecutionSummaryAggregator
{
    public static ResearchExecutionSummaryResponse Aggregate(
        ResearchRun run,
        IReadOnlyCollection<ResearchEvent> events,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        return Aggregate(run.Id, run.StartedAt, run.CompletedAt, run.DocumentsAdded, events, now);
    }

    public static ResearchExecutionSummaryResponse Aggregate(
        Guid researchRunId,
        DateTimeOffset startedAt,
        DateTimeOffset? completedAt,
        int documentsAcquired,
        IReadOnlyCollection<ResearchEvent> events,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(events);

        var searchCalls = 0;
        var providerAttempts = 0;
        var crawlCalls = 0;
        var successfulCrawls = 0;
        var failedCrawls = 0;
        var aiCalls = 0;
        var fallbacks = 0;
        var failures = 0;

        foreach (var item in events)
        {
            var category = ResearchEventSemantics.CanonicalCategory(item.Category);
            var isCall = ResearchEventSemantics.IsLogicalCallStart(item);

            if (category is ResearchEventCategory.Search or ResearchEventCategory.Crawl or ResearchEventCategory.AI && isCall)
            {
                var attempts = ReadProviderAttemptCount(item);
                providerAttempts += attempts;
                if (category == ResearchEventCategory.Search) searchCalls++;
                if (category == ResearchEventCategory.Crawl) crawlCalls++;
                if (category == ResearchEventCategory.AI) aiCalls++;
            }

            // Canonical execution rows carry the result status on the same row.
            // Historical lifecycle rows carry outcomes on a subsequent row.
            if (category == ResearchEventCategory.Crawl &&
                (isCall || ResearchEventSemantics.IsLegacyOutcome(item)))
            {
                if (item.Status == ResearchEventStatus.Completed) successfulCrawls++;
                if (item.Status == ResearchEventStatus.Failed) failedCrawls++;
            }

            if (category == ResearchEventCategory.Research &&
                string.Equals(ResearchEventSemantics.OperationFor(item), "provider_fallback", StringComparison.Ordinal))
            {
                fallbacks++;
            }

            if (item.Status == ResearchEventStatus.Failed)
            {
                failures++;
            }
        }

        var end = completedAt ?? events
            .Select(item => item.Timestamp.AddMilliseconds(Math.Max(0, item.DurationMs ?? 0)))
            .DefaultIfEmpty(now ?? DateTimeOffset.UtcNow)
            .Max();
        var duration = Math.Max(0, (long)(end - startedAt).TotalMilliseconds);

        return new ResearchExecutionSummaryResponse(
            researchRunId,
            duration,
            searchCalls,
            providerAttempts,
            fallbacks,
            crawlCalls,
            successfulCrawls,
            failedCrawls,
            aiCalls,
            SumNullable(events.Select(item => item.InputTokens)),
            SumNullable(events.Select(item => item.OutputTokens)),
            SumNullable(events.Select(item => item.CachedTokens)),
            SumNullable(events.Select(item => item.ThinkingTokens)),
            failures,
            Math.Max(0, documentsAcquired),
            events.Count);
    }

    private static int ReadProviderAttemptCount(ResearchEvent item)
    {
        if (string.IsNullOrWhiteSpace(item.MetadataJson))
        {
            return 1;
        }

        try
        {
            using var document = JsonDocument.Parse(item.MetadataJson);
            var root = document.RootElement;
            foreach (var propertyName in new[] { "providerAttempts", "attemptCount", "attempts" })
            {
                if (!root.TryGetProperty(propertyName, out var property)) continue;
                if (property.TryGetInt32(out var count)) return Math.Max(1, count);
                if (property.ValueKind == JsonValueKind.Array) return Math.Max(1, property.GetArrayLength());
            }
        }
        catch (JsonException)
        {
            // Metadata is diagnostic. An unavailable count must not break the
            // run-level execution surface.
        }

        return 1;
    }

    private static int? SumNullable(IEnumerable<int?> values)
    {
        var materialized = values.ToArray();
        return materialized.Any(value => value.HasValue)
            ? materialized.Sum(value => value ?? 0)
            : null;
    }
}
