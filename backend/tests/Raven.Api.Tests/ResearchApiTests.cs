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
using Raven.Api.Features.Research.Sources;
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
        Assert.Equal(ResearchStage.EvidenceReady, run.Stage);
        Assert.Equal(12, run.SourcesFound);
        Assert.Equal(3, run.SourcesSelected);
        Assert.Equal(2, run.SourcesCrawled);
        Assert.Equal(3, run.CrawlTotal);
        Assert.Equal(3, run.CrawlCompleted);
        Assert.Equal(2, run.CrawlSucceeded);
        Assert.Equal(1, run.CrawlFailed);
        Assert.Equal(2, run.DocumentsAdded);
        Assert.Equal(0, run.DuplicatesSkipped);
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
        Assert.Equal(ResearchStage.Failed, run.Stage);
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
        Assert.Equal(ResearchStage.Failed, run.Stage);
        Assert.Equal(0, run.SourcesCrawled);
        Assert.Equal(3, run.CrawlFailed);
        Assert.Contains("No selected URLs could be crawled", run.Error);
    }

    [Fact]
    public async Task Discover_persists_candidates_and_waits_for_selection_before_crawling()
    {
        var crawler = new TrackingCrawlerProvider();
        using var client = CreateClient(new SuccessfulSearchProvider(), crawler);
        var company = await CreateCompanyAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{company.Id}/research/discover",
            new DiscoverResearchRequest("products and locations"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = await response.Content.ReadFromJsonAsync<ResearchRunResponse>(JsonOptions);
        Assert.NotNull(run);
        Assert.Equal(ResearchStage.AwaitingSourceSelection, run.Stage);
        Assert.Equal(ResearchRunStatus.Searching, run.Status);
        Assert.Equal(5, run.QueriesTotal);
        Assert.Equal(5, run.QueriesCompleted);
        Assert.Equal(15, run.SourcesFound);
        Assert.Equal(3, run.UniqueCandidates);
        Assert.Equal(3, run.RecommendedCandidates);
        Assert.Empty(crawler.RequestedUrls);

        var candidates = await client.GetFromJsonAsync<ResearchCandidateResponse[]>(
            $"/api/research-runs/{run.Id}/candidates",
            JsonOptions);
        Assert.NotNull(candidates);
        Assert.Equal(3, candidates.Length);
        Assert.All(candidates, candidate =>
        {
            Assert.Equal(run.Id, candidate.ResearchRunId);
            Assert.True(candidate.Recommended);
            Assert.Equal(SourceKind.OfficialWebsite, candidate.SourceKind);
            Assert.NotEmpty(candidate.RecommendationReasons);
            Assert.NotNull(candidate.IconUrl);
        });

        var acquireResponse = await client.PostAsJsonAsync(
            $"/api/research-runs/{run.Id}/acquire",
            new AcquireResearchCandidatesRequest(candidates.Take(2).Select(candidate => candidate.Id).ToArray()));

        Assert.Equal(HttpStatusCode.OK, acquireResponse.StatusCode);
        var acquired = await acquireResponse.Content.ReadFromJsonAsync<ResearchRunResponse>(JsonOptions);
        Assert.NotNull(acquired);
        Assert.Equal(ResearchStage.EvidenceReady, acquired.Stage);
        Assert.Equal(2, acquired.SourcesSelected);
        Assert.Equal(2, acquired.CrawlTotal);
        Assert.Equal(2, acquired.CrawlCompleted);
        Assert.Equal(2, acquired.DocumentsAdded);
        Assert.Equal(2, crawler.RequestedUrls.Count);

        var sourceResponse = await client.GetAsync($"/api/research-runs/{run.Id}/sources");
        var sourceContent = await sourceResponse.Content.ReadAsStringAsync();
        Assert.True(sourceResponse.IsSuccessStatusCode, sourceContent);
        var sources = JsonSerializer.Deserialize<SourceDocumentResponse[]>(sourceContent, JsonOptions);
        Assert.NotNull(sources);
        Assert.Equal(2, sources.Length);
    }

    [Fact]
    public async Task Acquire_rejects_empty_or_foreign_candidate_ids()
    {
        using var client = CreateClient(new SuccessfulSearchProvider(), new TrackingCrawlerProvider());
        var company = await CreateCompanyAsync(client);
        var discoverResponse = await client.PostAsJsonAsync(
            $"/api/companies/{company.Id}/research/discover",
            new DiscoverResearchRequest());
        var run = (await discoverResponse.Content.ReadFromJsonAsync<ResearchRunResponse>(JsonOptions))!;

        var emptyResponse = await client.PostAsJsonAsync(
            $"/api/research-runs/{run.Id}/acquire",
            new AcquireResearchCandidatesRequest([]));
        Assert.Equal(HttpStatusCode.BadRequest, emptyResponse.StatusCode);

        var foreignResponse = await client.PostAsJsonAsync(
            $"/api/research-runs/{run.Id}/acquire",
            new AcquireResearchCandidatesRequest([Guid.NewGuid()]));
        Assert.Equal(HttpStatusCode.BadRequest, foreignResponse.StatusCode);
    }

    [Fact]
    public async Task Acquire_counts_duplicate_content_as_duplicate_and_not_as_document()
    {
        using var client = CreateClient(new SuccessfulSearchProvider(), new DuplicateContentCrawlerProvider());
        var company = await CreateCompanyAsync(client);
        var discoverResponse = await client.PostAsJsonAsync(
            $"/api/companies/{company.Id}/research/discover",
            new DiscoverResearchRequest());
        var run = (await discoverResponse.Content.ReadFromJsonAsync<ResearchRunResponse>(JsonOptions))!;
        var candidates = (await client.GetFromJsonAsync<ResearchCandidateResponse[]>(
            $"/api/research-runs/{run.Id}/candidates", JsonOptions))!;

        var acquireResponse = await client.PostAsJsonAsync(
            $"/api/research-runs/{run.Id}/acquire",
            new AcquireResearchCandidatesRequest(candidates.Select(candidate => candidate.Id).ToArray()));
        var acquired = (await acquireResponse.Content.ReadFromJsonAsync<ResearchRunResponse>(JsonOptions))!;

        Assert.Equal(3, acquired.CrawlSucceeded);
        Assert.Equal(1, acquired.DocumentsAdded);
        Assert.Equal(2, acquired.DuplicatesSkipped);
        Assert.Equal(ResearchStage.EvidenceReady, acquired.Stage);
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

    private sealed class TrackingCrawlerProvider : ICrawlerProvider
    {
        public string Id => "crawl4ai-local";

        public List<string> RequestedUrls { get; } = [];

        public Task<CrawlResult> CrawlAsync(CrawlRequest request, CancellationToken cancellationToken = default)
        {
            RequestedUrls.Add(request.Url);
            return Task.FromResult(new CrawlResult(
                Id,
                request.Url,
                request.Url,
                "FPT",
                $"evidence content {request.Url}",
                true,
                null,
                DateTimeOffset.UtcNow));
        }
    }

    private sealed class DuplicateContentCrawlerProvider : ICrawlerProvider
    {
        public string Id => "crawl4ai-local";

        public Task<CrawlResult> CrawlAsync(CrawlRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CrawlResult(
                Id,
                request.Url,
                request.Url,
                "FPT",
                "same evidence content",
                true,
                null,
                DateTimeOffset.UtcNow));
    }

    private sealed class AlwaysFailingCrawlerProvider : ICrawlerProvider
    {
        public string Id => "crawl4ai-local";

        public Task<CrawlResult> CrawlAsync(CrawlRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CrawlResult(Id, request.Url, null, null, null, false, "unavailable", DateTimeOffset.UtcNow));
    }
}
