using System.Net;
using Microsoft.Extensions.Options;
using Raven.Api.Data;
using Raven.Api.Features.Crawling;

namespace Raven.Api.Tests;

public sealed class CloudRunDeploymentTests
{
    [Fact]
    public void Seed_database_is_copied_once_and_existing_data_is_preserved()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"raven-seed-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var seed = Path.Combine(directory, "seed.db");
            var runtime = Path.Combine(directory, "data", "runtime.db");
            File.WriteAllText(seed, "initial");

            SqliteSeedDatabase.CopyIfMissing($"Data Source={runtime}", seed);
            Assert.Equal("initial", File.ReadAllText(runtime));

            File.WriteAllText(runtime, "user data");
            SqliteSeedDatabase.CopyIfMissing($"Data Source={runtime}", seed);
            Assert.Equal("user data", File.ReadAllText(runtime));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Private_crawler_request_keeps_crawl_token_and_adds_cloud_run_identity()
    {
        var metadata = new StubHandler(request =>
        {
            Assert.Equal("Google", request.Headers.GetValues("Metadata-Flavor").Single());
            Assert.Contains("audience=https%3A%2F%2Fcrawler.example.run.app", request.RequestUri!.Query);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("identity-token") };
        });
        var target = new StubHandler(request =>
        {
            Assert.Equal("Bearer crawl-token", request.Headers.Authorization!.ToString());
            Assert.Equal("Bearer identity-token", request.Headers.GetValues("X-Serverless-Authorization").Single());
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var handler = new CloudRunCrawlerIdentityHandler(
            Options.Create(new Crawl4AiLocalOptions { CloudRunAudience = "https://crawler.example.run.app" }),
            new StubClientFactory(metadata)) { InnerHandler = target };
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://crawler.example.run.app/crawl");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "crawl-token");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class StubClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
