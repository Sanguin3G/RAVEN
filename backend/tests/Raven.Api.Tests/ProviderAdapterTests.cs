using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Search;

namespace Raven.Api.Tests;

public sealed class ProviderAdapterTests
{
    [Fact]
    public async Task Brave_maps_web_results_to_neutral_search_results()
    {
        using var client = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal("brave-key", request.Headers.GetValues("X-Subscription-Token").Single());
            Assert.Contains("q=FPT%20Software", request.RequestUri!.Query);
            return Json("""{"web":{"results":[{"title":"FPT","url":"https://fptsoftware.com","description":"Official site"}]}}""");
        })) { BaseAddress = new Uri("https://api.search.brave.com") };
        var provider = new BraveSearchProvider(client, Options.Create(new BraveSearchOptions { ApiKey = "brave-key" }));

        var response = await provider.SearchAsync(new SearchRequest("FPT Software", 5));

        var result = Assert.Single(response.Results);
        Assert.Equal("brave", response.Provider);
        Assert.Equal("FPT", result.Title);
        Assert.Equal(1, result.Rank);
    }

    [Fact]
    public async Task Brave_reports_configuration_and_authentication_failures_explicitly()
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))) { BaseAddress = new Uri("https://api.search.brave.com") };
        var missingKey = new BraveSearchProvider(client, Options.Create(new BraveSearchOptions()));
        var missingKeyException = await Assert.ThrowsAsync<ProviderException>(() => missingKey.SearchAsync(new SearchRequest("FPT", 1)));
        Assert.Equal(ProviderFailureKind.Configuration, missingKeyException.Kind);

        var provider = new BraveSearchProvider(client, Options.Create(new BraveSearchOptions { ApiKey = "wrong" }));
        var authException = await Assert.ThrowsAsync<ProviderException>(() => provider.SearchAsync(new SearchRequest("FPT", 1)));
        Assert.Equal(ProviderFailureKind.Authentication, authException.Kind);
    }

    [Fact]
    public async Task Crawl4Ai_maps_markdown_and_sends_bearer_authentication()
    {
        using var client = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("crawl-token", request.Headers.Authorization.Parameter);
            Assert.Equal("/crawl", request.RequestUri!.AbsolutePath);
            return Json("""{"success":true,"results":[{"success":true,"url":"https://example.com/about","markdown":{"fit_markdown":"# About"},"metadata":{"title":"About us"}}]}""");
        })) { BaseAddress = new Uri("http://localhost:11235") };
        var provider = new Crawl4AiLocalProvider(client, Options.Create(new Crawl4AiLocalOptions { ApiToken = "crawl-token" }));

        var result = await provider.CrawlAsync(new CrawlRequest("https://example.com/about"));

        Assert.True(result.Success);
        Assert.Equal("# About", result.Markdown);
        Assert.Equal("About us", result.Title);
    }

    [Fact]
    public async Task Crawl4Ai_uses_raw_markdown_when_fit_markdown_is_empty()
    {
        using var client = new HttpClient(new StubHandler(_ =>
            Json("""{"success":true,"results":[{"success":true,"url":"https://example.com/about","markdown":{"fit_markdown":"","raw_markdown":"# Full page"},"metadata":{}}]}""")))
        { BaseAddress = new Uri("http://localhost:11235") };
        var provider = new Crawl4AiLocalProvider(client, Options.Create(new Crawl4AiLocalOptions { ApiToken = "crawl-token" }));

        var result = await provider.CrawlAsync(new CrawlRequest("https://example.com/about"));

        Assert.True(result.Success);
        Assert.Equal("# Full page", result.Markdown);
    }

    [Fact]
    public async Task Crawl4Ai_returns_a_page_failure_without_throwing()
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway))) { BaseAddress = new Uri("http://localhost:11235") };
        var provider = new Crawl4AiLocalProvider(client, Options.Create(new Crawl4AiLocalOptions { ApiToken = "crawl-token" }));

        var result = await provider.CrawlAsync(new CrawlRequest("https://example.com/about"));

        Assert.False(result.Success);
        Assert.Contains("502", result.Error);
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
