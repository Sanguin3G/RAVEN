using Raven.Api.Features.Crawling;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.Routing;
using Raven.Api.Features.Search;
using Raven.Api.Features.Settings;

namespace Raven.Api.Tests;

public sealed class ProviderRoutingTests
{
    [Fact]
    public async Task Search_falls_back_for_rate_limit_and_preserves_route_diagnostics()
    {
        var first = new FakeSearchProvider("brave", (_, _) => throw new ProviderException(
            "brave", "rate limited", ProviderFailureKind.RateLimited));
        var second = new FakeSearchProvider("exa", (_, _) => Task.FromResult(
            new SearchResponse("exa", [new SearchResult("FPT", "https://fpt.com", null, 1)])));
        var router = new RoutingSearchProvider(
            new ProviderCatalog<ISearchProvider>([first, second]),
            new FixedSettings(searchProviders: ["brave", "exa"]));

        var response = await router.SearchAsync(new SearchRequest("FPT", 5));

        Assert.Equal("exa", response.Provider);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(1, second.CallCount);
        Assert.NotNull(router.LastRoute);
        Assert.Equal("brave", router.LastRoute!.RequestedProvider);
        Assert.Equal("exa", router.LastRoute.ActualProvider);
        var attempt = Assert.Single(router.LastRoute.Attempts);
        Assert.Equal("brave", attempt.ProviderId);
        Assert.Equal(ProviderRouteFailureKind.RateLimited, attempt.FailureKind);
    }

    [Fact]
    public async Task Search_does_not_fall_back_for_authentication_failure()
    {
        var first = new FakeSearchProvider("brave", (_, _) => throw new ProviderException(
            "brave", "authentication failed", ProviderFailureKind.Authentication));
        var second = new FakeSearchProvider("exa", (_, _) => Task.FromResult(
            new SearchResponse("exa", [])));
        var router = new RoutingSearchProvider(
            new ProviderCatalog<ISearchProvider>([first, second]),
            new FixedSettings(searchProviders: ["brave", "exa"]));

        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            router.SearchAsync(new SearchRequest("FPT", 5)));

        Assert.Equal(ProviderFailureKind.Authentication, exception.Kind);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public async Task Search_exhaustion_is_a_safe_provider_exception_with_route_context()
    {
        var first = new FakeSearchProvider("brave", (_, _) => throw new ProviderException(
            "brave", "temporarily unavailable", ProviderFailureKind.Unavailable));
        var second = new FakeSearchProvider("exa", (_, _) => throw new ProviderException(
            "exa", "timed out", ProviderFailureKind.Timeout));
        var router = new RoutingSearchProvider(
            new ProviderCatalog<ISearchProvider>([first, second]),
            new FixedSettings(searchProviders: ["brave", "exa"]));

        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            router.SearchAsync(new SearchRequest("FPT", 5)));

        Assert.Equal(ProviderFailureKind.Unavailable, exception.Kind);
        Assert.DoesNotContain("key", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(ProviderRouteContext.TryGetRoute(exception, out var route));
        Assert.Equal("brave", route!.RequestedProvider);
        Assert.Null(route.ActualProvider);
        Assert.Equal(["brave", "exa"], route.Attempts.Select(attempt => attempt.ProviderId));
    }

    [Fact]
    public async Task Search_uses_only_persisted_priority_and_reports_missing_registration()
    {
        var router = new RoutingSearchProvider(
            new ProviderCatalog<ISearchProvider>([
                new FakeSearchProvider("brave", (_, _) => Task.FromResult(new SearchResponse("brave", [])))
            ]),
            new FixedSettings(searchProviders: ["exa"]));

        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            router.SearchAsync(new SearchRequest("FPT", 5)));

        Assert.Equal(ProviderFailureKind.Unavailable, exception.Kind);
        Assert.True(ProviderRouteContext.TryGetRoute(exception, out var route));
        Assert.Equal("exa", route!.RequestedProvider);
        Assert.Empty(route.Attempts);
    }

    [Fact]
    public async Task Crawler_falls_back_for_retrieval_failure()
    {
        var first = new FakeCrawlerProvider("crawl4ai-local", (_, _) => Task.FromResult(
            Failure("crawl4ai-local", "could not be reached")));
        var second = new FakeCrawlerProvider("firecrawl", (_, _) => Task.FromResult(
            new CrawlResult("firecrawl", "https://fpt.com", "https://fpt.com", "FPT", "content", true, null, DateTimeOffset.UtcNow)));
        var router = new RoutingCrawlerProvider(
            new ProviderCatalog<ICrawlerProvider>([first, second]),
            new FixedSettings(crawlerProviders: ["crawl4ai-local", "firecrawl"]));

        var result = await router.CrawlAsync(new CrawlRequest("https://fpt.com"));

        Assert.True(result.Success);
        Assert.Equal("firecrawl", result.Provider);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(1, second.CallCount);
        Assert.Equal(ProviderRouteFailureKind.Unavailable, Assert.Single(router.LastRoute!.Attempts).FailureKind);
    }

    [Fact]
    public async Task Crawler_does_not_fall_back_for_client_error_result()
    {
        var first = new FakeCrawlerProvider("crawl4ai-local", (_, _) => Task.FromResult(
            Failure("crawl4ai-local", "Crawl4AI returned 400: bad request")));
        var second = new FakeCrawlerProvider("firecrawl", (_, _) => Task.FromResult(
            new CrawlResult("firecrawl", "https://fpt.com", "https://fpt.com", "FPT", "content", true, null, DateTimeOffset.UtcNow)));
        var router = new RoutingCrawlerProvider(
            new ProviderCatalog<ICrawlerProvider>([first, second]),
            new FixedSettings(crawlerProviders: ["crawl4ai-local", "firecrawl"]));

        var result = await router.CrawlAsync(new CrawlRequest("https://fpt.com"));

        Assert.False(result.Success);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(0, second.CallCount);
        Assert.Empty(router.LastRoute!.Attempts);
    }

    [Fact]
    public async Task Crawler_does_not_fall_back_for_authentication_failure()
    {
        var first = new FakeCrawlerProvider("crawl4ai-local", (_, _) => throw new ProviderException(
            "crawl4ai-local", "authentication failed", ProviderFailureKind.Authentication));
        var second = new FakeCrawlerProvider("firecrawl", (_, _) => Task.FromResult(
            new CrawlResult("firecrawl", "https://fpt.com", "https://fpt.com", "FPT", "content", true, null, DateTimeOffset.UtcNow)));
        var router = new RoutingCrawlerProvider(
            new ProviderCatalog<ICrawlerProvider>([first, second]),
            new FixedSettings(crawlerProviders: ["crawl4ai-local", "firecrawl"]));

        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            router.CrawlAsync(new CrawlRequest("https://fpt.com")));

        Assert.Equal(ProviderFailureKind.Authentication, exception.Kind);
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public async Task Crawler_rejects_invalid_url_before_provider_selection()
    {
        var provider = new FakeCrawlerProvider("crawl4ai-local", (_, _) => throw new InvalidOperationException());
        var router = new RoutingCrawlerProvider(
            new ProviderCatalog<ICrawlerProvider>([provider]),
            new FixedSettings(crawlerProviders: ["crawl4ai-local"]));

        var result = await router.CrawlAsync(new CrawlRequest("javascript:alert(1)"));

        Assert.False(result.Success);
        Assert.Equal("routing-crawler", result.Provider);
        Assert.Equal(0, provider.CallCount);
        Assert.Empty(router.LastRoute!.Attempts);
    }

    private static CrawlResult Failure(string provider, string error) =>
        new(provider, "https://fpt.com", null, null, null, false, error, DateTimeOffset.UtcNow);

    private sealed class FixedSettings(
        IReadOnlyList<string>? searchProviders = null,
        IReadOnlyList<string>? crawlerProviders = null) : IResearchSettingsService
    {
        private readonly ResearchSettingsResponse response = new(
            GroundingMode.Auto,
            "gemini-3.5-flash-lite",
            "gemini-3.5-flash-lite",
            "gemini-3.8-flash",
            true,
            ProviderPreset.Custom,
            searchProviders ?? ["brave"],
            crawlerProviders ?? ["crawl4ai-local"],
            DateTimeOffset.UtcNow);

        public Task<ResearchSettingsResponse> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(response);

        public Task<ResearchSettingsResponse> UpdateAsync(
            UpdateResearchSettingsRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(response);

        public Task<ResearchSettingsResponse> ResetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(response);
    }

    private sealed class FakeSearchProvider(
        string id,
        Func<SearchRequest, CancellationToken, Task<SearchResponse>> search) : ISearchProvider
    {
        public string Id { get; } = id;
        public int CallCount { get; private set; }

        public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return search(request, cancellationToken);
        }
    }

    private sealed class FakeCrawlerProvider(
        string id,
        Func<CrawlRequest, CancellationToken, Task<CrawlResult>> crawl) : ICrawlerProvider
    {
        public string Id { get; } = id;
        public int CallCount { get; private set; }

        public Task<CrawlResult> CrawlAsync(CrawlRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return crawl(request, cancellationToken);
        }
    }
}
