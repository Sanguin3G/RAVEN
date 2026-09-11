using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Crawling.Exa;
using Raven.Api.Features.Search;

namespace Raven.Api.Tests;

public sealed class ExaCrawlerProviderTests
{
    [Fact]
    public async Task Crawl_maps_contents_result_and_sends_bounded_authenticated_request()
    {
        string? requestBody = null;
        using var client = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/contents", request.RequestUri!.AbsolutePath);
            Assert.Equal("exa-test-key", request.Headers.GetValues("x-api-key").Single());
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json("""
                {
                  "results": [
                    {
                      "id": "https://example.com/about",
                      "url": "https://example.com/about/",
                      "title": "Example About",
                      "text": "# Example\n\nPublic company information."
                    }
                  ],
                  "statuses": [
                    {"id":"https://example.com/about","status":"success","source":"cached"}
                  ]
                }
                """);
        })) { BaseAddress = new Uri("https://api.exa.ai") };

        var provider = new ExaCrawlerProvider(
            client,
            Options.Create(new ExaCrawlerOptions
            {
                ApiKey = "exa-test-key",
                MaxTextCharacters = 8_000
            }));

        var result = await provider.CrawlAsync(new CrawlRequest("https://example.com/about"));

        Assert.True(result.Success);
        Assert.Equal(ExaCrawlerProvider.ProviderId, result.Provider);
        Assert.Equal("https://example.com/about", result.RequestedUrl);
        Assert.Equal("https://example.com/about/", result.FinalUrl);
        Assert.Equal("Example About", result.Title);
        Assert.Equal("# Example\n\nPublic company information.", result.Markdown);

        using var body = JsonDocument.Parse(requestBody!);
        Assert.Equal("https://example.com/about", body.RootElement.GetProperty("urls")[0].GetString());
        Assert.Equal(8_000, body.RootElement.GetProperty("text").GetProperty("maxCharacters").GetInt32());
    }

    [Fact]
    public async Task Crawl_returns_page_failure_for_non_success_target_status()
    {
        using var client = new HttpClient(new StubHandler(_ => Json("""
            {
              "results": [],
              "statuses": [{"id":"https://example.com","status":"error"}]
            }
            """))) { BaseAddress = new Uri("https://api.exa.ai") };
        var provider = new ExaCrawlerProvider(
            client,
            Options.Create(new ExaCrawlerOptions { ApiKey = "key" }));

        var result = await provider.CrawlAsync(new CrawlRequest("https://example.com"));

        Assert.False(result.Success);
        Assert.Null(result.Markdown);
        Assert.Contains("could not retrieve", result.Error);
    }

    [Fact]
    public async Task Crawl_bounds_text_and_uses_requested_url_when_final_url_is_missing()
    {
        using var client = new HttpClient(new StubHandler(_ => Json("""
            {
              "results": [{"text":"one two three four"}],
              "statuses": [{"status":"success"}]
            }
            """))) { BaseAddress = new Uri("https://api.exa.ai") };
        var provider = new ExaCrawlerProvider(
            client,
            Options.Create(new ExaCrawlerOptions { ApiKey = "key", MaxTextCharacters = 1_000 }));

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
        })) { BaseAddress = new Uri("https://api.exa.ai") };
        var provider = new ExaCrawlerProvider(
            client,
            Options.Create(new ExaCrawlerOptions { ApiKey = "key" }));

        var result = await provider.CrawlAsync(new CrawlRequest("javascript:alert(1)"));

        Assert.False(result.Success);
        Assert.False(called);
        Assert.Contains("HTTP(S)", result.Error);
    }

    [Fact]
    public async Task Crawl_requires_api_key_without_network_call()
    {
        var called = false;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            called = true;
            return Json("{}");
        })) { BaseAddress = new Uri("https://api.exa.ai") };
        var provider = new ExaCrawlerProvider(client, Options.Create(new ExaCrawlerOptions()));

        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.CrawlAsync(new CrawlRequest("https://example.com")));

        Assert.Equal(ProviderFailureKind.Configuration, exception.Kind);
        Assert.False(called);
        Assert.Contains(ExaCrawlerOptions.ApiKeyEnvironmentVariable, exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ProviderFailureKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, ProviderFailureKind.Authentication)]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.RequestTimeout, ProviderFailureKind.Timeout)]
    [InlineData(HttpStatusCode.BadRequest, ProviderFailureKind.InvalidResponse)]
    [InlineData(HttpStatusCode.UnprocessableEntity, ProviderFailureKind.InvalidResponse)]
    [InlineData(HttpStatusCode.BadGateway, ProviderFailureKind.Unavailable)]
    public async Task Crawl_maps_http_failures_to_exa_taxonomy(
        HttpStatusCode statusCode,
        ProviderFailureKind expectedKind)
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(statusCode)))
        { BaseAddress = new Uri("https://api.exa.ai") };
        var provider = new ExaCrawlerProvider(
            client,
            Options.Create(new ExaCrawlerOptions { ApiKey = "key" }));

        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.CrawlAsync(new CrawlRequest("https://example.com")));

        Assert.Equal(expectedKind, exception.Kind);
    }

    [Fact]
    public async Task Crawl_maps_timeout_and_transport_failure_without_exposing_key()
    {
        using var timeoutClient = new HttpClient(new StubHandler(_ => throw new TaskCanceledException()))
        { BaseAddress = new Uri("https://api.exa.ai") };
        var timeoutProvider = new ExaCrawlerProvider(
            timeoutClient,
            Options.Create(new ExaCrawlerOptions { ApiKey = "secret-exa-key" }));

        var timeout = await Assert.ThrowsAsync<ProviderException>(() =>
            timeoutProvider.CrawlAsync(new CrawlRequest("https://example.com")));

        Assert.Equal(ProviderFailureKind.Timeout, timeout.Kind);
        Assert.DoesNotContain("secret-exa-key", timeout.ToString());

        using var unavailableClient = new HttpClient(new StubHandler(_ => throw new HttpRequestException()))
        { BaseAddress = new Uri("https://api.exa.ai") };
        var unavailableProvider = new ExaCrawlerProvider(
            unavailableClient,
            Options.Create(new ExaCrawlerOptions { ApiKey = "secret-exa-key" }));

        var unavailable = await Assert.ThrowsAsync<ProviderException>(() =>
            unavailableProvider.CrawlAsync(new CrawlRequest("https://example.com")));

        Assert.Equal(ProviderFailureKind.Unavailable, unavailable.Kind);
        Assert.DoesNotContain("secret-exa-key", unavailable.ToString());
    }

    [Fact]
    public async Task Crawl_maps_invalid_json_or_missing_results_to_invalid_response()
    {
        using var invalidJsonClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json", Encoding.UTF8, "application/json")
            })) { BaseAddress = new Uri("https://api.exa.ai") };
        var invalidJsonProvider = new ExaCrawlerProvider(
            invalidJsonClient,
            Options.Create(new ExaCrawlerOptions { ApiKey = "key" }));

        var invalidJson = await Assert.ThrowsAsync<ProviderException>(() =>
            invalidJsonProvider.CrawlAsync(new CrawlRequest("https://example.com")));

        Assert.Equal(ProviderFailureKind.InvalidResponse, invalidJson.Kind);

        using var missingResultsClient = new HttpClient(new StubHandler(_ => Json("{\"requestId\":\"id\"}")))
        { BaseAddress = new Uri("https://api.exa.ai") };
        var missingResultsProvider = new ExaCrawlerProvider(
            missingResultsClient,
            Options.Create(new ExaCrawlerOptions { ApiKey = "key" }));

        var missingResults = await Assert.ThrowsAsync<ProviderException>(() =>
            missingResultsProvider.CrawlAsync(new CrawlRequest("https://example.com")));

        Assert.Equal(ProviderFailureKind.InvalidResponse, missingResults.Kind);
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
