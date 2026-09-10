using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Research;
using Raven.Api.Features.Search;

namespace Raven.Api.Tests;

public sealed class ResearchApiTests(RavenApiFactory factory) : IClassFixture<RavenApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Research_persists_successful_sources_and_tolerates_one_crawl_failure()
    {
        using var client = CreateClient(new SuccessfulSearchProvider(), new PartiallyFailingCrawlerProvider());
        var company = await CreateCompanyAsync(client);

        var response = await client.PostAsync($"/api/companies/{company.Id}/research", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<ResearchRunResponse>(JsonOptions);
        Assert.NotNull(run);
        Assert.Equal(ResearchRunStatus.Completed, run.Status);
        Assert.Equal(9, run.SourcesFound);
        Assert.Equal(3, run.SourcesSelected);
        Assert.Equal(2, run.SourcesCrawled);
        Assert.Contains("Some selected URLs", run.Error);

        var fetchedRun = await client.GetFromJsonAsync<ResearchRunResponse>($"/api/research-runs/{run.Id}", JsonOptions);
        Assert.NotNull(fetchedRun);
        Assert.Equal(run.Id, fetchedRun.Id);

        var sources = await client.GetFromJsonAsync<SourceDocumentResponse[]>($"/api/companies/{company.Id}/sources", JsonOptions);
        Assert.NotNull(sources);
        Assert.Equal(2, sources.Length);
        Assert.All(sources, source => Assert.Equal("crawl4ai-local", source.CrawlerProvider));

        var detail = await client.GetFromJsonAsync<SourceDocumentDetailResponse>($"/api/sources/{sources[0].Id}", JsonOptions);
        Assert.NotNull(detail);
        Assert.StartsWith("evidence content", detail.Content);
        Assert.Equal(64, detail.ContentHash.Length);
    }

    [Fact]
    public async Task Research_records_a_failed_run_when_search_fails()
    {
        using var client = CreateClient(new FailingSearchProvider(), new PartiallyFailingCrawlerProvider());
        var company = await CreateCompanyAsync(client);

        var response = await client.PostAsync($"/api/companies/{company.Id}/research", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<ResearchRunResponse>(JsonOptions);
        Assert.NotNull(run);
        Assert.Equal(ResearchRunStatus.Failed, run.Status);
        Assert.Contains("not configured", run.Error);
    }

    [Fact]
    public async Task Research_records_a_failed_run_when_every_selected_url_fails_to_crawl()
    {
        using var client = CreateClient(new SuccessfulSearchProvider(), new AlwaysFailingCrawlerProvider());
        var company = await CreateCompanyAsync(client);

        var response = await client.PostAsync($"/api/companies/{company.Id}/research", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<ResearchRunResponse>(JsonOptions);
        Assert.NotNull(run);
        Assert.Equal(ResearchRunStatus.Failed, run.Status);
        Assert.Equal(0, run.SourcesCrawled);
        Assert.Contains("No selected URLs could be crawled", run.Error);
    }

    [Fact]
    public async Task Research_returns_not_found_for_an_unknown_company()
    {
        using var client = CreateClient(new SuccessfulSearchProvider(), new PartiallyFailingCrawlerProvider());

        var response = await client.PostAsync($"/api/companies/{Guid.NewGuid()}/research", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient CreateClient(ISearchProvider searchProvider, ICrawlerProvider crawlerProvider) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISearchProvider>();
            services.RemoveAll<ICrawlerProvider>();
            services.AddScoped(_ => searchProvider);
            services.AddScoped(_ => crawlerProvider);
        })).CreateClient();

    private static async Task<CompanyResponse> CreateCompanyAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/companies", new CreateCompanyRequest("FPT Software", "https://fptsoftware.com", "Vietnam"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CompanyResponse>())!;
    }

    private sealed class SuccessfulSearchProvider : ISearchProvider
    {
        public string Id => "brave";

        public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new SearchResponse(Id,
            [
                new SearchResult("About FPT", "https://fptsoftware.com/about", null, 1),
                new SearchResult("FPT services", "https://fptsoftware.com/services?utm_source=brave", null, 2),
                new SearchResult("FPT markets", "https://fptsoftware.com/markets", null, 3)
            ]));
    }

    private sealed class FailingSearchProvider : ISearchProvider
    {
        public string Id => "brave";

        public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default) =>
            throw new ProviderException(Id, "Brave Search is not configured.", ProviderFailureKind.Configuration);
    }

    private sealed class PartiallyFailingCrawlerProvider : ICrawlerProvider
    {
        public string Id => "crawl4ai-local";

        public Task<CrawlResult> CrawlAsync(CrawlRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(request.Url.Contains("services", StringComparison.Ordinal)
                ? new CrawlResult(Id, request.Url, null, null, null, false, "blocked", DateTimeOffset.UtcNow)
                : new CrawlResult(Id, request.Url, request.Url, "FPT", $"evidence content {request.Url}", true, null, DateTimeOffset.UtcNow));
    }

    private sealed class AlwaysFailingCrawlerProvider : ICrawlerProvider
    {
        public string Id => "crawl4ai-local";

        public Task<CrawlResult> CrawlAsync(CrawlRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CrawlResult(Id, request.Url, null, null, null, false, "unavailable", DateTimeOffset.UtcNow));
    }
}
