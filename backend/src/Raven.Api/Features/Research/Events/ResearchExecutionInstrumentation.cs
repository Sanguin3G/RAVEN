using System.Diagnostics;
using System.Text.Json;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Research.Routing;
using Raven.Api.Features.Search;

namespace Raven.Api.Features.Research.Events;

/// <summary>
/// The ambient scope for execution telemetry. Provider contracts intentionally
/// remain provider-neutral and do not grow a ResearchRunId parameter; a caller
/// that owns a run can push its identity around the external calls instead.
/// </summary>
public interface IResearchExecutionContext
{
    Guid? ResearchRunId { get; }
    Guid? ConversationId { get; }
    ResearchStage? Stage { get; }

    IDisposable Push(
        Guid? researchRunId,
        Guid? conversationId = null,
        ResearchStage? stage = null);
}

public sealed class ResearchExecutionContext : IResearchExecutionContext
{
    private readonly AsyncLocal<Scope?> current = new();

    public Guid? ResearchRunId => current.Value?.ResearchRunId;
    public Guid? ConversationId => current.Value?.ConversationId;
    public ResearchStage? Stage => current.Value?.Stage;

    public IDisposable Push(
        Guid? researchRunId,
        Guid? conversationId = null,
        ResearchStage? stage = null)
    {
        var previous = current.Value;
        current.Value = new Scope(researchRunId, conversationId, stage);
        return new PopScope(current, previous);
    }

    private sealed record Scope(Guid? ResearchRunId, Guid? ConversationId, ResearchStage? Stage);

    private sealed class PopScope(AsyncLocal<Scope?> slot, Scope? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            slot.Value = previous;
        }
    }
}

/// <summary>
/// Common, non-fatal persistence helper used by provider-boundary decorators.
/// An unavailable telemetry sink must never change the provider result.
/// </summary>
internal static class ResearchExecutionTelemetry
{
    public const string SearchOperation = "web_search";
    public const string CrawlOperation = "page_crawl";
    public const string DefaultAiOperation = "profile_generation";

    private const int MaxInputSummaryCharacters = 500;
    private const int MaxOutputSummaryCharacters = 500;
    public static async Task TryWriteAsync(
        IResearchEventWriter writer,
        ResearchEventDraft draft,
        CancellationToken cancellationToken)
    {
        try
        {
            // Create a single completed/failed operation row. The EF writer
            // assigns a process-local sequence without querying MAX(sequence).
            await writer.WriteAsync(ResearchEventSanitizer.Create(draft, sequence: 0), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Telemetry is diagnostic. Do not turn a successful search, crawl,
            // or model response into a failed research operation.
        }
        catch (OperationCanceledException)
        {
            // Cancellation of telemetry is also non-fatal to its caller.
        }
    }

    public static string? BoundedSummary(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maximum ? normalized : normalized[..Math.Max(1, maximum - 1)].TrimEnd() + "…";
    }

    public static string? BoundedQuery(string? query) => BoundedSummary(query, MaxInputSummaryCharacters);
    public static string? BoundedUrl(string? url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return BoundedSummary(url, MaxInputSummaryCharacters);
        }

        // Keep the route useful without persisting query strings or fragments,
        // which can contain user data and make telemetry needlessly noisy.
        return BoundedSummary($"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}", MaxInputSummaryCharacters);
    }

    public static string? BoundedOutput(string? output) => BoundedSummary(output, MaxOutputSummaryCharacters);

    public static string? RouteMetadata(ProviderRoute? route)
    {
        if (route is null)
        {
            return null;
        }

        var payload = new
        {
            requestedProvider = BoundedSummary(route.RequestedProvider, 100),
            actualProvider = BoundedSummary(route.ActualProvider, 100),
            providerAttempts = Math.Max(1, route.Attempts.Count + (route.ActualProvider is null ? 0 : 1)),
            fallbackAttempts = route.Attempts.Count,
            attempts = route.Attempts.Select(attempt => new
            {
                provider = BoundedSummary(attempt.ProviderId, 100),
                failure = attempt.FailureKind.ToString(),
                message = BoundedSummary(attempt.SafeMessage, 300)
            }).ToArray()
        };

        // The sanitizer applies the final metadata bound while preserving
        // valid JSON. Do not truncate the serialized document as plain text.
        return JsonSerializer.Serialize(payload);
    }

    public static ProviderRoute? RouteFrom(Exception exception, IProviderRouteDiagnostics? diagnostics)
    {
        if (ProviderRouteContext.TryGetRoute(exception, out var exceptionRoute))
        {
            return exceptionRoute;
        }

        return diagnostics?.LastRoute;
    }

    public static string ErrorCode(Exception exception) => exception switch
    {
        ProviderException providerException => providerException.Kind.ToString().ToLowerInvariant(),
        HttpRequestException => "unavailable",
        OperationCanceledException => "timeout",
        _ => "provider_error"
    };

    public static string? SafeExceptionMessage(Exception exception) =>
        BoundedSummary(exception.Message, MaxOutputSummaryCharacters);

    public static long ElapsedMilliseconds(Stopwatch stopwatch) =>
        Math.Max(0, stopwatch.ElapsedMilliseconds);
}

/// <summary>
/// Records one logical routed search operation. Provider fallback attempts are
/// represented in bounded route metadata instead of separate lifecycle rows.
/// </summary>
public sealed class InstrumentedSearchProvider(
    ISearchProvider inner,
    IResearchEventWriter eventWriter,
    IResearchExecutionContext executionContext) : ISearchProvider
{
    public string Id => inner.Id;

    public async Task<SearchResponse> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await inner.SearchAsync(request, cancellationToken);
            var route = (inner as IProviderRouteDiagnostics)?.LastRoute;
            await ResearchExecutionTelemetry.TryWriteAsync(eventWriter, new ResearchEventDraft
            {
                ResearchRunId = executionContext.ResearchRunId,
                ConversationId = executionContext.ConversationId,
                Stage = executionContext.Stage,
                Category = ResearchEventCategory.Search,
                Operation = ResearchExecutionTelemetry.SearchOperation,
                Status = ResearchEventStatus.Completed,
                Provider = route?.ActualProvider ?? response.Provider,
                InputSummary = ResearchExecutionTelemetry.BoundedQuery(request.Query),
                OutputSummary = ResearchExecutionTelemetry.BoundedOutput($"{response.Results.Count} results"),
                DurationMs = ResearchExecutionTelemetry.ElapsedMilliseconds(stopwatch),
                MetadataJson = ResearchExecutionTelemetry.RouteMetadata(route)
            }, cancellationToken);
            return response;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var route = ResearchExecutionTelemetry.RouteFrom(exception, inner as IProviderRouteDiagnostics);
            await ResearchExecutionTelemetry.TryWriteAsync(eventWriter, new ResearchEventDraft
            {
                ResearchRunId = executionContext.ResearchRunId,
                ConversationId = executionContext.ConversationId,
                Stage = executionContext.Stage,
                Category = ResearchEventCategory.Search,
                Operation = ResearchExecutionTelemetry.SearchOperation,
                Status = ResearchEventStatus.Failed,
                Provider = route?.ActualProvider ?? route?.RequestedProvider ?? inner.Id,
                InputSummary = ResearchExecutionTelemetry.BoundedQuery(request.Query),
                OutputSummary = "search failed",
                DurationMs = ResearchExecutionTelemetry.ElapsedMilliseconds(stopwatch),
                ErrorCode = ResearchExecutionTelemetry.ErrorCode(exception),
                ErrorMessage = ResearchExecutionTelemetry.SafeExceptionMessage(exception),
                MetadataJson = ResearchExecutionTelemetry.RouteMetadata(route)
            }, cancellationToken);
            throw;
        }
    }
}

/// <summary>Records one logical routed page acquisition operation.</summary>
public sealed class InstrumentedCrawlerProvider(
    ICrawlerProvider inner,
    IResearchEventWriter eventWriter,
    IResearchExecutionContext executionContext) : ICrawlerProvider
{
    public string Id => inner.Id;

    public async Task<CrawlResult> CrawlAsync(
        CrawlRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await inner.CrawlAsync(request, cancellationToken);
            var route = (inner as IProviderRouteDiagnostics)?.LastRoute;
            await ResearchExecutionTelemetry.TryWriteAsync(eventWriter, new ResearchEventDraft
            {
                ResearchRunId = executionContext.ResearchRunId,
                ConversationId = executionContext.ConversationId,
                Stage = executionContext.Stage,
                Category = ResearchEventCategory.Crawl,
                Operation = ResearchExecutionTelemetry.CrawlOperation,
                Status = result.Success ? ResearchEventStatus.Completed : ResearchEventStatus.Failed,
                Provider = route?.ActualProvider ?? result.Provider,
                InputSummary = ResearchExecutionTelemetry.BoundedUrl(request.Url),
                OutputSummary = result.Success ? "content acquired" : "crawl failed",
                DurationMs = ResearchExecutionTelemetry.ElapsedMilliseconds(stopwatch),
                ErrorCode = result.Success ? null : "retrieval_failure",
                ErrorMessage = result.Success ? null : ResearchExecutionTelemetry.BoundedOutput(result.Error),
                MetadataJson = ResearchExecutionTelemetry.RouteMetadata(route)
            }, cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var route = ResearchExecutionTelemetry.RouteFrom(exception, inner as IProviderRouteDiagnostics);
            await ResearchExecutionTelemetry.TryWriteAsync(eventWriter, new ResearchEventDraft
            {
                ResearchRunId = executionContext.ResearchRunId,
                ConversationId = executionContext.ConversationId,
                Stage = executionContext.Stage,
                Category = ResearchEventCategory.Crawl,
                Operation = ResearchExecutionTelemetry.CrawlOperation,
                Status = ResearchEventStatus.Failed,
                Provider = route?.ActualProvider ?? route?.RequestedProvider ?? inner.Id,
                InputSummary = ResearchExecutionTelemetry.BoundedUrl(request.Url),
                OutputSummary = "crawl failed",
                DurationMs = ResearchExecutionTelemetry.ElapsedMilliseconds(stopwatch),
                ErrorCode = ResearchExecutionTelemetry.ErrorCode(exception),
                ErrorMessage = ResearchExecutionTelemetry.SafeExceptionMessage(exception),
                MetadataJson = ResearchExecutionTelemetry.RouteMetadata(route)
            }, cancellationToken);
            throw;
        }
    }
}

/// <summary>Records model usage at the neutral AI provider boundary.</summary>
public sealed class InstrumentedAiModelProvider(
    IAiModelProvider inner,
    IResearchEventWriter eventWriter,
    IResearchExecutionContext executionContext) : IAiModelProvider
{
    public string Id => inner.Id;

    public async Task<AiModelResult> GenerateStructuredAsync(
        AiModelRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await inner.GenerateStructuredAsync(request, cancellationToken);
            await WriteResultAsync(request, result, stopwatch, cancellationToken);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            await ResearchExecutionTelemetry.TryWriteAsync(eventWriter, new ResearchEventDraft
            {
                ResearchRunId = executionContext.ResearchRunId,
                ConversationId = executionContext.ConversationId,
                Stage = executionContext.Stage,
                Category = ResearchEventCategory.AI,
                Operation = AiOperation(request),
                Status = ResearchEventStatus.Failed,
                Provider = inner.Id,
                Model = request.Model,
                InputSummary = AiInputSummary(request),
                OutputSummary = "model call failed",
                DurationMs = ResearchExecutionTelemetry.ElapsedMilliseconds(stopwatch),
                PromptTemplateVersion = request.PromptTemplateVersion,
                ErrorCode = ResearchExecutionTelemetry.ErrorCode(exception),
                ErrorMessage = ResearchExecutionTelemetry.SafeExceptionMessage(exception)
            }, cancellationToken);
            throw;
        }
    }

    private async Task WriteResultAsync(
        AiModelRequest request,
        AiModelResult result,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        var usage = result.Usage;
        var failure = result.Failure;
        await ResearchExecutionTelemetry.TryWriteAsync(eventWriter, new ResearchEventDraft
        {
            ResearchRunId = executionContext.ResearchRunId,
            ConversationId = executionContext.ConversationId,
            Stage = executionContext.Stage,
            Category = ResearchEventCategory.AI,
            Operation = AiOperation(request),
            Status = result.Succeeded ? ResearchEventStatus.Completed : ResearchEventStatus.Failed,
            Provider = string.IsNullOrWhiteSpace(result.Provider) ? inner.Id : result.Provider,
            Model = string.IsNullOrWhiteSpace(result.Model) ? request.Model : result.Model,
            InputSummary = AiInputSummary(request),
            OutputSummary = result.Succeeded ? "structured response" : "model call failed",
            DurationMs = ResearchExecutionTelemetry.ElapsedMilliseconds(stopwatch),
            InputTokens = usage?.PromptTokens,
            OutputTokens = usage?.OutputTokens,
            CachedTokens = usage?.CachedInputTokens,
            ThinkingTokens = usage?.ThinkingTokens,
            ExternalRequestId = result.ExternalRequestId,
            PromptTemplateVersion = request.PromptTemplateVersion,
            HttpStatus = failure?.HttpStatus,
            ErrorCode = failure?.Code,
            ErrorMessage = failure?.Message
        }, cancellationToken);
    }

    private static string AiOperation(AiModelRequest request)
    {
        var template = request.PromptTemplateVersion ?? string.Empty;
        if (template.Contains("identity", StringComparison.OrdinalIgnoreCase)) return "identity_resolution";
        if (template.Contains("rerank", StringComparison.OrdinalIgnoreCase) || template.Contains("relevance", StringComparison.OrdinalIgnoreCase)) return "source_relevance";
        if (template.Contains("patch", StringComparison.OrdinalIgnoreCase)) return "profile_patch_generation";
        if (template.Contains("deep-research", StringComparison.OrdinalIgnoreCase)) return "deep_research_decision";
        return ResearchExecutionTelemetry.DefaultAiOperation;
    }

    private static string AiInputSummary(AiModelRequest request) =>
        ResearchExecutionTelemetry.BoundedSummary(
            $"template={request.PromptTemplateVersion}; evidence={request.Evidence.Sources.Count} sources",
            500)!;
}
