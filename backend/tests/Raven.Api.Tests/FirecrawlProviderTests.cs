using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Firecrawl;
using Raven.Api.Features.Search;

namespace Raven.Api.Tests;

public sealed class FirecrawlProviderTests
{
    [Fact]
    public async Task Search_maps_web_results_and_sends_bounded_authenticated_request()
    {
        string? requestBody = null;
        using var client = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v2/search", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer firecrawl-test-key", request.Headers.Authorization!.ToString());
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json("""
                {
                  "success": true,
                  "data": {
                    "web": [
                      {"title":"FPT Software","url":"https://fptsoftware.com","description":"Official company site"},
                      {"url":"https://example.com/about","description":"About   the company"},
                      {"title":"Invalid","url":"javascript:alert(1)"}
                    ]
                  }
                }
                """);
        })) { BaseAddress = new Uri("https://api.firecrawl.dev") };

        var provider = new FirecrawlSearchProvider(
            client,
            Options.Create(new FirecrawlOptions { ApiKey = "firecrawl-test-key", TimeoutSeconds = 7 }));

        var response = await provider.SearchAsync(new SearchRequest("FPT Software", 50, "vn"));

        Assert.Equal(FirecrawlSearchProvider.ProviderId, response.Provider);
        Assert.Equal(2, response.Results.Count);
        Assert.Equal("FPT Software", response.Results[0].Title);
        Assert.Equal("Official company site", response.Results[0].Snippet);
        Assert.Equal(1, response.Results[0].Rank);
        Assert.Equal("https://example.com/about", response.Results[1].Url);
        Assert.Equal("About the company", response.Results[1].Snippet);
        Assert.Equal(2, response.Results[1].Rank);

        using var body = JsonDocument.Parse(requestBody!);
        Assert.Equal("FPT Software", body.RootElement.GetProperty("query").GetString());
        Assert.Equal(20, body.RootElement.GetProperty("limit").GetInt32());
        Assert.Equal("web", body.RootElement.GetProperty("sources")[0].GetString());
        Assert.Equal("VN", body.RootElement.GetProperty("country").GetString());
        Assert.Equal(7_000, body.RootElement.GetProperty("timeout").GetInt32());
    }

    [Fact]
    public async Task Search_uses_metadata_title_and_bounds_description()
    {
        using var client = new HttpClient(new StubHandler(_ => Json("""
            {
              "success": true,
              "data": {
                "web": [{"url":"https://example.com","metadata":{"title":"Example"},"description":"one   two   three"}]
              }
            }
            """))) { BaseAddress = new Uri("https://api.firecrawl.dev") };
        var provider = new FirecrawlSearchProvider(
            client,
            Options.Create(new FirecrawlOptions { ApiKey = "key", MaxSnippetCharacters = 9 }));

        var result = Assert.Single((await provider.SearchAsync(new SearchRequest("example", 1))).Results);

        Assert.Equal("Example", result.Title);
        Assert.Equal("one two…", result.Snippet);
    }

    [Fact]
    public async Task Search_requires_api_key_without_network_call()
    {
        var called = false;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            called = true;
            return Json("{\"success\":true,\"data\":{\"web\":[]}}");
        })) { BaseAddress = new Uri("https://api.firecrawl.dev") };
        var provider = new FirecrawlSearchProvider(client, Options.Create(new FirecrawlOptions()));

        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.SearchAsync(new SearchRequest("FPT", 1)));

        Assert.Equal(ProviderFailureKind.Configuration, exception.Kind);
        Assert.False(called);
        Assert.Contains(FirecrawlOptions.ApiKeyEnvironmentVariable, exception.Message);
    }

    [Fact]
    public async Task Search_rejects_empty_query_before_network_call()
    {
        using var client = new HttpClient(new StubHandler(_ => throw new InvalidOperationException("network should not be called")))
        { BaseAddress = new Uri("https://api.firecrawl.dev") };
        var provider = new FirecrawlSearchProvider(
            client,
            Options.Create(new FirecrawlOptions { ApiKey = "key" }));

        await Assert.ThrowsAsync<ArgumentException>(() => provider.SearchAsync(new SearchRequest(" ", 1)));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ProviderFailureKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, ProviderFailureKind.Authentication)]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.RequestTimeout, ProviderFailureKind.Timeout)]
    [InlineData(HttpStatusCode.PaymentRequired, ProviderFailureKind.Configuration)]
    [InlineData(HttpStatusCode.BadRequest, ProviderFailureKind.InvalidResponse)]
    [InlineData(HttpStatusCode.BadGateway, ProviderFailureKind.Unavailable)]
    public async Task Search_maps_http_failures_to_provider_taxonomy(
        HttpStatusCode statusCode,
        ProviderFailureKind expectedKind)
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(statusCode)))
        { BaseAddress = new Uri("https://api.firecrawl.dev") };
        var provider = new FirecrawlSearchProvider(
            client,
            Options.Create(new FirecrawlOptions { ApiKey = "key" }));

        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.SearchAsync(new SearchRequest("FPT", 1)));

        Assert.Equal(expectedKind, exception.Kind);
    }

    [Fact]
    public async Task Search_maps_timeout_and_invalid_json_without_exposing_key()
    {
        using var timeoutClient = new HttpClient(new StubHandler(_ => throw new TaskCanceledException()))
        { BaseAddress = new Uri("https://api.firecrawl.dev") };
        var timeoutProvider = new FirecrawlSearchProvider(
            timeoutClient,
            Options.Create(new FirecrawlOptions { ApiKey = "secret-firecrawl-key" }));

        var timeout = await Assert.ThrowsAsync<ProviderException>(() =>
            timeoutProvider.SearchAsync(new SearchRequest("FPT", 1)));

        Assert.Equal(ProviderFailureKind.Timeout, timeout.Kind);
        Assert.DoesNotContain("secret-firecrawl-key", timeout.ToString());

        using var invalidJsonClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json", Encoding.UTF8, "application/json")
            })) { BaseAddress = new Uri("https://api.firecrawl.dev") };
        var invalidJsonProvider = new FirecrawlSearchProvider(
            invalidJsonClient,
            Options.Create(new FirecrawlOptions { ApiKey = "key" }));

        var invalidJson = await Assert.ThrowsAsync<ProviderException>(() =>
            invalidJsonProvider.SearchAsync(new SearchRequest("FPT", 1)));

        Assert.Equal(ProviderFailureKind.InvalidResponse, invalidJson.Kind);
    }

    [Fact]
    public async Task Crawl_maps_markdown_metadata_and_sends_scrape_options()
    {
        string? requestBody = null;
        using var client = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v2/scrape", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer firecrawl-test-key", request.Headers.Authorization!.ToString());
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json("""
                {
                  "success": true,
                  "data": {
                    "markdown": "# About\n\nFPT Software",
                    "metadata": {
                      "title": "About us",
                      "url": "https://example.com/about"
                    }
                  }
                }
                """);
        })) { BaseAddress = new Uri("https://api.firecrawl.dev") };

        var provider = new FirecrawlCrawlerProvider(
            client,
            Options.Create(new FirecrawlOptions { ApiKey = "firecrawl-test-key", TimeoutSeconds = 12 }));

        var result = await provider.CrawlAsync(new CrawlRequest("https://example.com/about"));

        Assert.True(result.Success);
        Assert.Equal(FirecrawlCrawlerProvider.ProviderId, result.Provider);
        Assert.Equal("https://example.com/about", result.FinalUrl);
        Assert.Equal("About us", result.Title);
        Assert.Equal("# About\n\nFPT Software", result.Markdown);

        using var body = JsonDocument.Parse(requestBody!);
        Assert.Equal("https://example.com/about", body.RootElement.GetProperty("url").GetString());
        Assert.Equal("markdown", body.RootElement.GetProperty("formats")[0].GetString());
        Assert.True(body.RootElement.GetProperty("onlyMainContent").GetBoolean());
        Assert.True(body.RootElement.GetProperty("removeBase64Images").GetBoolean());
        Assert.Equal(12_000, body.RootElement.GetProperty("timeout").GetInt32());
    }

    [Fact]
    public async Task Crawl_bounds_markdown_and_preserves_requested_url_when_metadata_is_missing()
    {
        using var client = new HttpClient(new StubHandler(_ => Json("""
            {"success":true,"data":{"markdown":"  one two three four  "}}
            """))) { BaseAddress = new Uri("https://api.firecrawl.dev") };
        var provider = new FirecrawlCrawlerProvider(
            client,
            Options.Create(new FirecrawlOptions { ApiKey = "key", MaxMarkdownCharacters = 1_000 }));

        var result = await provider.CrawlAsync(new CrawlRequest("https://example.com"));

        Assert.True(result.Success);
        Assert.Equal("https://example.com", result.FinalUrl);
        Assert.Equal("one two three four", result.Markdown);
    }

    [Fact]
    public async Task Crawl_invalid_url_returns_page_failure_without_network_call()
    {
        var called = false;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            called = true;
            return Json("{}");
        })) { BaseAddress = new Uri("https://api.firecrawl.dev") };
        var provider = new FirecrawlCrawlerProvider(
            client,
            Options.Create(new FirecrawlOptions { ApiKey = "key" }));

        var result = await provider.CrawlAsync(new CrawlRequest("javascript:alert(1)"));

        Assert.False(result.Success);
        Assert.Contains("HTTP(S)", result.Error);
        Assert.False(called);
    }

    [Fact]
    public async Task Crawl_requires_api_key_without_network_call()
    {
        var called = false;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            called = true;
            return Json("{}");
        })) { BaseAddress = new Uri("https://api.firecrawl.dev") };
        var provider = new FirecrawlCrawlerProvider(client, Options.Create(new FirecrawlOptions()));

        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.CrawlAsync(new CrawlRequest("https://example.com")));

        Assert.Equal(ProviderFailureKind.Configuration, exception.Kind);
        Assert.False(called);
        Assert.Contains(FirecrawlOptions.ApiKeyEnvironmentVariable, exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ProviderFailureKind.Authentication)]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.RequestTimeout, ProviderFailureKind.Timeout)]
    [InlineData(HttpStatusCode.PaymentRequired, ProviderFailureKind.Configuration)]
    [InlineData(HttpStatusCode.BadRequest, ProviderFailureKind.InvalidResponse)]
    [InlineData(HttpStatusCode.BadGateway, ProviderFailureKind.Unavailable)]
    public async Task Crawl_maps_http_failures_to_provider_taxonomy(
        HttpStatusCode statusCode,
        ProviderFailureKind expectedKind)
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(statusCode)))
        { BaseAddress = new Uri("https://api.firecrawl.dev") };
        var provider = new FirecrawlCrawlerProvider(
            client,
            Options.Create(new FirecrawlOptions { ApiKey = "key" }));

        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.CrawlAsync(new CrawlRequest("https://example.com")));

        Assert.Equal(expectedKind, exception.Kind);
    }

    [Fact]
    public async Task Crawl_returns_failure_for_success_false_without_leaking_provider_details()
    {
        using var client = new HttpClient(new StubHandler(_ => Json("""
            {"success":false,"error":"target could not be read"}
            """))) { BaseAddress = new Uri("https://api.firecrawl.dev") };
        var provider = new FirecrawlCrawlerProvider(
            client,
            Options.Create(new FirecrawlOptions { ApiKey = "key" }));

        var result = await provider.CrawlAsync(new CrawlRequest("https://example.com"));

        Assert.False(result.Success);
        Assert.Contains("successful scrape result", result.Error);
    }

    [Fact]
    public async Task Crawl_maps_timeout_and_transport_failure_without_exposing_key()
    {
        using var timeoutClient = new HttpClient(new StubHandler(_ => throw new TaskCanceledException()))
        { BaseAddress = new Uri("https://api.firecrawl.dev") };
        var timeoutProvider = new FirecrawlCrawlerProvider(
            timeoutClient,
            Options.Create(new FirecrawlOptions { ApiKey = "secret-firecrawl-key" }));

        var timeout = await Assert.ThrowsAsync<ProviderException>(() =>
            timeoutProvider.CrawlAsync(new CrawlRequest("https://example.com")));

        Assert.Equal(ProviderFailureKind.Timeout, timeout.Kind);
        Assert.DoesNotContain("secret-firecrawl-key", timeout.ToString());

        using var unavailableClient = new HttpClient(new StubHandler(_ => throw new HttpRequestException()))
        { BaseAddress = new Uri("https://api.firecrawl.dev") };
        var unavailableProvider = new FirecrawlCrawlerProvider(
            unavailableClient,
            Options.Create(new FirecrawlOptions { ApiKey = "secret-firecrawl-key" }));

        var unavailable = await Assert.ThrowsAsync<ProviderException>(() =>
            unavailableProvider.CrawlAsync(new CrawlRequest("https://example.com")));

        Assert.Equal(ProviderFailureKind.Unavailable, unavailable.Kind);
        Assert.DoesNotContain("secret-firecrawl-key", unavailable.ToString());
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
