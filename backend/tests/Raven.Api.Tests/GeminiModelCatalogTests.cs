using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Ai;

namespace Raven.Api.Tests;

public sealed class GeminiModelCatalogTests
{
    [Fact]
    public async Task GetAsync_matches_project_models_by_base_id_or_resource_name()
    {
        using var client = new HttpClient(new StubHandler(async request =>
        {
            Assert.Equal("/v1beta/models", request.RequestUri!.AbsolutePath);
            Assert.Equal("1000", request.RequestUri.Query.TrimStart('?').Split('=')[1]);
            Assert.Equal("test-key", request.Headers.GetValues("x-goog-api-key").Single());
            Assert.DoesNotContain("test-key", request.RequestUri.ToString(), StringComparison.Ordinal);

            return Json("""
                {
                  "models": [
                    { "baseModelId": "gemini-3.5-flash-lite", "supportedGenerationMethods": ["generateContent"] },
                    { "baseModelId": "gemini-3.5-flash-001", "name": "models/gemini-3.5-flash", "supportedGenerationMethods": ["generateContent"] },
                    { "baseModelId": "gemini-3.8-flash", "supportedGenerationMethods": ["generateContent"] }
                  ]
                }
                """);
        }))
        { BaseAddress = new Uri("https://generativelanguage.googleapis.com") };

        var catalog = new GeminiModelCatalog(
            client,
            Options.Create(new GeminiOptions { ApiKey = "test-key" }));

        var response = await catalog.GetAsync();

        Assert.True(response.ProjectAvailabilityVerified);
        Assert.All(response.Models, model => Assert.Equal("Available", model.Availability));
    }

    [Fact]
    public async Task GetAsync_follows_a_next_page_when_a_compatible_model_is_not_on_the_first_page()
    {
        var page = 0;
        using var client = new HttpClient(new StubHandler(request =>
        {
            page++;
            if (page == 1)
            {
                Assert.Contains("pageSize=1000", request.RequestUri!.Query, StringComparison.Ordinal);
                return Task.FromResult(Json("""
                    { "models": [{ "baseModelId": "gemini-3.5-flash-lite", "supportedGenerationMethods": ["generateContent"] }], "nextPageToken": "second-page" }
                    """));
            }

            Assert.Contains("pageToken=second-page", request.RequestUri!.Query, StringComparison.Ordinal);
            return Task.FromResult(Json("""
                {
                  "models": [
                    { "baseModelId": "gemini-3.5-flash", "supportedGenerationMethods": ["generateContent"] },
                    { "baseModelId": "gemini-3.8-flash", "supportedGenerationMethods": ["generateContent"] }
                  ]
                }
                """));
        }))
        { BaseAddress = new Uri("https://generativelanguage.googleapis.com") };

        var catalog = new GeminiModelCatalog(
            client,
            Options.Create(new GeminiOptions { ApiKey = "test-key" }));

        var response = await catalog.GetAsync();

        Assert.Equal(2, page);
        Assert.All(response.Models, model => Assert.Equal("Available", model.Availability));
    }

    [Fact]
    public async Task GetAsync_marks_project_availability_unverified_when_catalog_request_fails()
    {
        using var client = new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))))
        { BaseAddress = new Uri("https://generativelanguage.googleapis.com") };
        var catalog = new GeminiModelCatalog(
            client,
            Options.Create(new GeminiOptions { ApiKey = "test-key" }));

        var response = await catalog.GetAsync();

        Assert.False(response.ProjectAvailabilityVerified);
        Assert.All(response.Models, model => Assert.Equal("Unverified", model.Availability));
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
