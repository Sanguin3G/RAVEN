using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Search;
using Raven.Api.Features.Search.Exa;

namespace Raven.Api.Tests;

public sealed class ExaSearchProviderTests
{
    [Fact]
    public async Task Maps_exa_results_to_neutral_search_results_and_sends_bounded_request()
    {
        string? requestBody = null;
        using var client = new HttpClient(new StubHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/search", request.RequestUri!.AbsolutePath);
            Assert.Equal("exa-test-key", request.Headers.GetValues("x-api-key").Single());
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json("""
                {
                  "results": [
                    {"title":"FPT Software","url":"https://fptsoftware.com","summary":"Official company site"},
                    {"url":"https://example.com/about","highlights":["About the company", "Products and services"]},
                    {"title":"No URL"}
                  ],
                  "requestId":"exa-request-id"
                }
                """);
        })) { BaseAddress = new Uri("https://api.exa.ai") };

        var provider = new ExaSearchProvider(
            client,
            Options.Create(new ExaSearchOptions { ApiKey = "exa-test-key" }));

        var response = await provider.SearchAsync(new SearchRequest("FPT Software", 50, "vn"));

        Assert.Equal("exa", response.Provider);
        Assert.Equal(2, response.Results.Count);
        Assert.Equal("FPT Software", response.Results[0].Title);
        Assert.Equal("Official company site", response.Results[0].Snippet);
        Assert.Equal(1, response.Results[0].Rank);
        Assert.Equal("https://example.com/about", response.Results[1].Url);
        Assert.Equal("About the company Products and services", response.Results[1].Snippet);
        Assert.Equal(2, response.Results[1].Rank);

        using var body = JsonDocument.Parse(requestBody!);
        Assert.Equal("FPT Software", body.RootElement.GetProperty("query").GetString());
        Assert.Equal("auto", body.RootElement.GetProperty("type").GetString());
        Assert.Equal(20, body.RootElement.GetProperty("numResults").GetInt32());
        Assert.Equal("VN", body.RootElement.GetProperty("userLocation").GetString());
        Assert.True(body.RootElement.GetProperty("contents").GetProperty("highlights").GetBoolean());
    }

    [Fact]
    public async Task Uses_url_as_title_and_text_as_bounded_fallback_snippet()
    {
        using var client = new HttpClient(new StubHandler(_ => Json("""
            {"results":[{"url":"https://example.com","text":"one   two   three four"}]}
            """))) { BaseAddress = new Uri("https://api.exa.ai") };
        var provider = new ExaSearchProvider(
            client,
            Options.Create(new ExaSearchOptions { ApiKey = "key", MaxSnippetCharacters = 9 }));

        var result = Assert.Single((await provider.SearchAsync(new SearchRequest("example", 1))).Results);

        Assert.Equal("https://example.com", result.Title);
        Assert.Equal("one two…", result.Snippet);
    }

    [Fact]
    public async Task Missing_api_key_is_configuration_failure_without_network_call()
    {
        var called = false;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            called = true;
            return Json("{\"results\":[]}");
        })) { BaseAddress = new Uri("https://api.exa.ai") };
        var provider = new ExaSearchProvider(client, Options.Create(new ExaSearchOptions()));

        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.SearchAsync(new SearchRequest("FPT", 1)));

        Assert.Equal(ProviderFailureKind.Configuration, exception.Kind);
        Assert.False(called);
        Assert.Contains(ExaSearchOptions.ApiKeyEnvironmentVariable, exception.Message);
    }

    [Fact]
    public async Task Empty_query_is_rejected_before_network_call()
    {
        using var client = new HttpClient(new StubHandler(_ => throw new InvalidOperationException("network should not be called")))
        { BaseAddress = new Uri("https://api.exa.ai") };
        var provider = new ExaSearchProvider(
            client,
            Options.Create(new ExaSearchOptions { ApiKey = "key" }));

        await Assert.ThrowsAsync<ArgumentException>(() => provider.SearchAsync(new SearchRequest(" ", 1)));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ProviderFailureKind.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, ProviderFailureKind.Authentication)]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.BadRequest, ProviderFailureKind.InvalidResponse)]
    [InlineData(HttpStatusCode.UnprocessableEntity, ProviderFailureKind.InvalidResponse)]
    [InlineData(HttpStatusCode.BadGateway, ProviderFailureKind.Unavailable)]
    public async Task Maps_http_failures_to_provider_failure_taxonomy(
        HttpStatusCode statusCode,
        ProviderFailureKind expectedKind)
    {
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(statusCode)))
        { BaseAddress = new Uri("https://api.exa.ai") };
        var provider = new ExaSearchProvider(
            client,
            Options.Create(new ExaSearchOptions { ApiKey = "key" }));

        var exception = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.SearchAsync(new SearchRequest("FPT", 1)));

        Assert.Equal(expectedKind, exception.Kind);
    }

    [Fact]
    public async Task Maps_timeout_and_transport_failure_without_exposing_credentials()
    {
        using var timeoutClient = new HttpClient(new StubHandler(_ => throw new TaskCanceledException()))
        { BaseAddress = new Uri("https://api.exa.ai") };
        var timeoutProvider = new ExaSearchProvider(
            timeoutClient,
            Options.Create(new ExaSearchOptions { ApiKey = "secret-exa-key" }));

        var timeout = await Assert.ThrowsAsync<ProviderException>(() =>
            timeoutProvider.SearchAsync(new SearchRequest("FPT", 1)));

        Assert.Equal(ProviderFailureKind.Timeout, timeout.Kind);
        Assert.DoesNotContain("secret-exa-key", timeout.ToString());

        using var unavailableClient = new HttpClient(new StubHandler(_ => throw new HttpRequestException()))
        { BaseAddress = new Uri("https://api.exa.ai") };
        var unavailableProvider = new ExaSearchProvider(
            unavailableClient,
            Options.Create(new ExaSearchOptions { ApiKey = "secret-exa-key" }));

        var unavailable = await Assert.ThrowsAsync<ProviderException>(() =>
            unavailableProvider.SearchAsync(new SearchRequest("FPT", 1)));

        Assert.Equal(ProviderFailureKind.Unavailable, unavailable.Kind);
        Assert.DoesNotContain("secret-exa-key", unavailable.ToString());
    }

    [Fact]
    public async Task Invalid_json_or_missing_results_is_invalid_response()
    {
        using var invalidJsonClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not-json", Encoding.UTF8, "application/json")
            })) { BaseAddress = new Uri("https://api.exa.ai") };
        var invalidJsonProvider = new ExaSearchProvider(
            invalidJsonClient,
            Options.Create(new ExaSearchOptions { ApiKey = "key" }));

        var invalidJson = await Assert.ThrowsAsync<ProviderException>(() =>
            invalidJsonProvider.SearchAsync(new SearchRequest("FPT", 1)));

        Assert.Equal(ProviderFailureKind.InvalidResponse, invalidJson.Kind);

        using var missingResultsClient = new HttpClient(new StubHandler(_ => Json("{\"requestId\":\"id\"}")))
        { BaseAddress = new Uri("https://api.exa.ai") };
        var missingResultsProvider = new ExaSearchProvider(
            missingResultsClient,
            Options.Create(new ExaSearchOptions { ApiKey = "key" }));

        var missingResults = await Assert.ThrowsAsync<ProviderException>(() =>
            missingResultsProvider.SearchAsync(new SearchRequest("FPT", 1)));

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
