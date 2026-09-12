using System.Text.Json;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Research.Routing;
using Raven.Api.Features.Search;

namespace Raven.Api.Tests;

public sealed class ResearchExecutionInstrumentationTests
{
    [Fact]
    public async Task Search_records_one_logical_operation_and_bounded_route_metadata()
    {
        var writer = new RecordingEventWriter();
        var context = new ResearchExecutionContext();
        var runId = Guid.NewGuid();
        var provider = new SearchProviderWithRoute
        {
            Response = new SearchResponse("exa", [new SearchResult("FPT", "https://fpt.com", null, 1)]),
            LastRoute = new ProviderRoute(
                "search",
                "brave",
                "exa",
                [new ProviderRouteAttempt("brave", ProviderRouteFailureKind.Timeout, "timed out")])
        };

        var instrumented = new InstrumentedSearchProvider(provider, writer, context);
        using (context.Push(runId, stage: ResearchStage.Discovering))
        {
            var result = await instrumented.SearchAsync(new SearchRequest("FPT leadership", 5));
            Assert.Single(result.Results);
        }

        var telemetry = Assert.Single(writer.Events);
        Assert.Equal(runId, telemetry.ResearchRunId);
        Assert.Equal(ResearchStage.Discovering, telemetry.Stage);
        Assert.Equal(ResearchEventCategory.Search, telemetry.Category);
        Assert.Equal("web_search", telemetry.Operation);
        Assert.Equal(ResearchEventStatus.Completed, telemetry.Status);
        Assert.Equal("exa", telemetry.Provider);
        Assert.Equal("1 results", telemetry.OutputSummary);

        using var metadata = JsonDocument.Parse(telemetry.MetadataJson!);
        Assert.Equal(2, metadata.RootElement.GetProperty("providerAttempts").GetInt32());
        Assert.Equal(1, metadata.RootElement.GetProperty("fallbackAttempts").GetInt32());
    }

    [Fact]
    public async Task Search_failure_is_recorded_without_changing_provider_failure()
    {
        var writer = new RecordingEventWriter();
        var instrumented = new InstrumentedSearchProvider(
            new ThrowingSearchProvider(),
            writer,
            new ResearchExecutionContext());

        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            instrumented.SearchAsync(new SearchRequest("FPT", 5)));

        Assert.Equal(ProviderFailureKind.Timeout, exception.Kind);
        var telemetry = Assert.Single(writer.Events);
        Assert.Equal(ResearchEventCategory.Search, telemetry.Category);
        Assert.Equal(ResearchEventStatus.Failed, telemetry.Status);
        Assert.Equal("timeout", telemetry.ErrorCode);
    }

    [Fact]
    public async Task Crawl_records_failure_and_removes_url_query_from_summary()
    {
        var writer = new RecordingEventWriter();
        var provider = new FixedCrawlerProvider(new CrawlResult(
            "crawl4ai-local",
            "https://example.com/about?token=should-not-be-logged",
            null,
            null,
            null,
            false,
            "blocked",
            DateTimeOffset.UtcNow));
        var instrumented = new InstrumentedCrawlerProvider(
            provider,
            writer,
            new ResearchExecutionContext());

        var result = await instrumented.CrawlAsync(new CrawlRequest("https://example.com/about?token=secret"));

        Assert.False(result.Success);
        var telemetry = Assert.Single(writer.Events);
        Assert.Equal(ResearchEventCategory.Crawl, telemetry.Category);
        Assert.Equal("page_crawl", telemetry.Operation);
        Assert.Equal(ResearchEventStatus.Failed, telemetry.Status);
        Assert.Equal("https://example.com/about", telemetry.InputSummary);
        Assert.DoesNotContain("secret", telemetry.InputSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ai_records_model_usage_prompt_version_and_operation()
    {
        var writer = new RecordingEventWriter();
        using var document = JsonDocument.Parse("{\"entities\":[]}");
        var provider = new FixedAiProvider(new AiModelResult(
            "gemini",
            "gemini-3.5-flash-lite",
            document.RootElement.Clone(),
            TimeSpan.FromMilliseconds(15),
            new AiUsage(PromptTokens: 2_341, OutputTokens: 312, ThinkingTokens: 7, CachedInputTokens: 19),
            ExternalRequestId: "request-123"));
        var instrumented = new InstrumentedAiModelProvider(
            provider,
            writer,
            new ResearchExecutionContext());

        var result = await instrumented.GenerateStructuredAsync(CreateRequest("company-identity-grounding-v1"));

        Assert.True(result.Succeeded);
        var telemetry = Assert.Single(writer.Events);
        Assert.Equal(ResearchEventCategory.AI, telemetry.Category);
        Assert.Equal("identity_resolution", telemetry.Operation);
        Assert.Equal(ResearchEventStatus.Completed, telemetry.Status);
        Assert.Equal("gemini", telemetry.Provider);
        Assert.Equal("gemini-3.5-flash-lite", telemetry.Model);
        Assert.Equal(2_341, telemetry.InputTokens);
        Assert.Equal(312, telemetry.OutputTokens);
        Assert.Equal(19, telemetry.CachedTokens);
        Assert.Equal(7, telemetry.ThinkingTokens);
        Assert.Equal("company-identity-grounding-v1", telemetry.PromptTemplateVersion);
        Assert.Equal("request-123", telemetry.ExternalRequestId);
    }

    [Fact]
    public async Task Telemetry_writer_failure_does_not_change_successful_result()
    {
        var writer = new RecordingEventWriter(throwOnWrite: true);
        var expected = new SearchResponse("brave", []);
        var instrumented = new InstrumentedSearchProvider(
            new SearchProviderWithRoute { Response = expected },
            writer,
            new ResearchExecutionContext());

        var actual = await instrumented.SearchAsync(new SearchRequest("FPT", 5));

        Assert.Same(expected, actual);
        Assert.True(writer.WriteAttempted);
    }

    private static AiModelRequest CreateRequest(string promptTemplateVersion) => new(
        "gemini-3.5-flash-lite",
        "Use only supplied evidence.",
        "Resolve the company.",
        promptTemplateVersion,
        AiEvidencePayload.Empty,
        JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone());

    private sealed class RecordingEventWriter(bool throwOnWrite = false) : IResearchEventWriter
    {
        public List<ResearchEvent> Events { get; } = [];
        public bool WriteAttempted { get; private set; }

        public Task WriteAsync(ResearchEvent researchEvent, CancellationToken cancellationToken = default)
        {
            WriteAttempted = true;
            if (throwOnWrite)
            {
                throw new InvalidOperationException("telemetry sink unavailable");
            }

            Events.Add(researchEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class SearchProviderWithRoute : ISearchProvider, IProviderRouteDiagnostics
    {
        public string Id => "routing-search";
        public SearchResponse Response { get; init; } = new("brave", []);
        public ProviderRoute? LastRoute { get; init; }

        public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Response);
    }

    private sealed class ThrowingSearchProvider : ISearchProvider
    {
        public string Id => "brave";

        public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) =>
            throw new ProviderException(Id, "provider timeout", ProviderFailureKind.Timeout);
    }

    private sealed class FixedCrawlerProvider(CrawlResult result) : ICrawlerProvider
    {
        public string Id => result.Provider;

        public Task<CrawlResult> CrawlAsync(CrawlRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FixedAiProvider(AiModelResult result) : IAiModelProvider
    {
        public string Id => result.Provider;

        public Task<AiModelResult> GenerateStructuredAsync(AiModelRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}
