using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Ai;

namespace Raven.Api.Tests;

public sealed class AiProviderTests
{
    [Fact]
    public async Task Gemini_sends_structured_request_and_maps_json_response()
    {
        using var client = new HttpClient(new StubHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1beta/models/gemini-test:generateContent", request.RequestUri!.AbsolutePath);
            Assert.Equal("gemini-secret", request.Headers.GetValues("x-goog-api-key").Single());
            Assert.DoesNotContain("gemini-secret", request.RequestUri.ToString(), StringComparison.Ordinal);

            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var root = body.RootElement;
            Assert.Equal("application/json", root.GetProperty("generationConfig").GetProperty("responseMimeType").GetString());
            Assert.Equal("object", root.GetProperty("generationConfig").GetProperty("responseSchema").GetProperty("type").GetString());

            var prompt = root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString();
            Assert.Contains("Extract the company", prompt, StringComparison.Ordinal);
            Assert.Contains("PROMPT_TEMPLATE_VERSION: profile-v1", prompt, StringComparison.Ordinal);
            Assert.Contains("SOURCE_ID: source-1", prompt, StringComparison.Ordinal);
            Assert.Contains("Official website content", prompt, StringComparison.Ordinal);

            return Json("""
                {
                  "responseId": "response-1",
                  "usageMetadata": {
                    "promptTokenCount": 11,
                    "candidatesTokenCount": 7,
                    "totalTokenCount": 18,
                    "thoughtsTokenCount": 2,
                    "cachedContentTokenCount": 3
                  },
                  "candidates": [{
                    "content": {"parts": [{"text": "{\"displayName\":\"FPT\"}"}]}
                  }]
                }
                """);
        }))
        { BaseAddress = new Uri("https://generativelanguage.googleapis.com") };

        var provider = new GeminiProvider(
            client,
            Options.Create(new GeminiOptions { ApiKey = "gemini-secret" }));

        var result = await provider.GenerateStructuredAsync(CreateRequest());

        Assert.True(result.Succeeded);
        Assert.Equal("gemini", result.Provider);
        Assert.Equal("gemini-test", result.Model);
        Assert.Equal("response-1", result.ExternalRequestId);
        Assert.Equal("FPT", result.StructuredJson!.Value.GetProperty("displayName").GetString());
        Assert.NotNull(result.Usage);
        Assert.Equal(11, result.Usage!.PromptTokens);
        Assert.Equal(7, result.Usage.OutputTokens);
        Assert.Equal(18, result.Usage.TotalTokens);
        Assert.Equal(2, result.Usage.ThinkingTokens);
        Assert.Equal(3, result.Usage.CachedInputTokens);
    }

    [Fact]
    public async Task Gemini_bounds_serialized_evidence_before_sending()
    {
        string? sentBody = null;
        using var client = new HttpClient(new StubHandler(async request =>
        {
            sentBody = await request.Content!.ReadAsStringAsync();
            return Json("""{"candidates":[{"content":{"parts":[{"text":"{}"}]}}]}""");
        }))
        { BaseAddress = new Uri("https://generativelanguage.googleapis.com") };

        var provider = new GeminiProvider(
            client,
            Options.Create(new GeminiOptions { ApiKey = "key", MaxEvidenceCharacters = 80 }));

        var request = CreateRequest() with
        {
            Evidence = new AiEvidencePayload(
                new Dictionary<string, string?> { ["country"] = "Vietnam" },
                [new AiEvidenceItem("source-1", "OfficialWebsite", "Company", "https://example.com", "CONTENT_MARKER_THAT_IS_TOO_LONG_TO_FIT")])
        };

        var result = await provider.GenerateStructuredAsync(request);

        Assert.True(result.Succeeded);
        Assert.NotNull(sentBody);
        Assert.DoesNotContain("CONTENT_MARKER_THAT_IS_TOO_LONG_TO_FIT", sentBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gemini_missing_key_returns_configuration_failure_without_http_call()
    {
        var called = false;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            called = true;
            return Task.FromResult(Json("{}"));
        }))
        { BaseAddress = new Uri("https://generativelanguage.googleapis.com") };

        var provider = new GeminiProvider(client, Options.Create(new GeminiOptions()));

        var result = await provider.GenerateStructuredAsync(CreateRequest());

        Assert.False(result.Succeeded);
        Assert.Equal("configuration", result.Failure!.Code);
        Assert.DoesNotContain("gemini-secret", result.Failure.Message, StringComparison.Ordinal);
        Assert.False(called);
    }

    [Fact]
    public async Task Gemini_authentication_failure_is_sanitized()
    {
        using var client = new HttpClient(new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("api key gemini-secret was rejected", Encoding.UTF8, "text/plain")
            })))
        { BaseAddress = new Uri("https://generativelanguage.googleapis.com") };
        var provider = new GeminiProvider(
            client,
            Options.Create(new GeminiOptions { ApiKey = "gemini-secret" }));

        var result = await provider.GenerateStructuredAsync(CreateRequest());

        Assert.False(result.Succeeded);
        Assert.Equal("authentication", result.Failure!.Code);
        Assert.Equal(401, result.Failure.HttpStatus);
        Assert.DoesNotContain("gemini-secret", result.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gemini_maps_rate_limit_and_malformed_response_failures()
    {
        using var rateClient = new HttpClient(new StubHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests))))
        { BaseAddress = new Uri("https://generativelanguage.googleapis.com") };
        var rateProvider = new GeminiProvider(
            rateClient,
            Options.Create(new GeminiOptions { ApiKey = "key" }));

        var rateResult = await rateProvider.GenerateStructuredAsync(CreateRequest());

        Assert.Equal("rate_limited", rateResult.Failure!.Code);
        Assert.True(rateResult.Failure.Retryable);

        using var malformedClient = new HttpClient(new StubHandler(_ =>
            Task.FromResult(Json("""{"candidates":[{"content":{"parts":[{"text":"not-json"}]}}]}"""))))
        { BaseAddress = new Uri("https://generativelanguage.googleapis.com") };
        var malformedProvider = new GeminiProvider(
            malformedClient,
            Options.Create(new GeminiOptions { ApiKey = "key" }));

        var malformedResult = await malformedProvider.GenerateStructuredAsync(CreateRequest());

        Assert.Equal("invalid_response", malformedResult.Failure!.Code);
        Assert.DoesNotContain("not-json", malformedResult.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Gemini_options_use_the_day_three_model_defaults()
    {
        var options = new GeminiOptions();

        Assert.Equal("gemini-3.5-flash-lite", options.FastModel);
        Assert.Equal("gemini-3.8-flash", options.DeepModel);
        Assert.Equal("v1beta", options.ApiVersion);
    }

    private static AiModelRequest CreateRequest() => new(
        "gemini-test",
        "Use only supplied evidence. Return null for unsupported scalar facts.",
        "Extract the company profile.",
        "profile-v1",
        new AiEvidencePayload(
            new Dictionary<string, string?> { ["displayName"] = "FPT Software" },
            [new AiEvidenceItem(
                "source-1",
                "OfficialWebsite",
                "About us",
                "https://example.com/about",
                "Official website content")]),
        JsonSchema());

    private static JsonElement JsonSchema()
    {
        using var document = JsonDocument.Parse("""{"type":"object","properties":{"displayName":{"type":"string"}}}""");
        return document.RootElement.Clone();
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(request);
    }
}
