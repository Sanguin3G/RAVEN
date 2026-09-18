using Raven.Api.Features.Chat;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Research;
using Raven.Api.Features.Search;

namespace Raven.Api.Tests;

public sealed class ChatWebToolTests
{
    [Fact]
    public async Task Search_ranks_official_candidates_and_read_returns_bounded_evidence_draft()
    {
        var search = new FakeSearchProvider(new SearchResponse("brave", [
            new SearchResult("Directory", "https://directory.example.net/company", "Directory entry", 1),
            new SearchResult("Official", "https://example.com/about?utm_source=test", "Official company page", 2)
        ]));
        var crawler = new FakeCrawlerProvider(request => new CrawlResult(
            "crawl4ai-local",
            request.Url,
            request.Url,
            "Official about",
            new string('x', 8_100),
            true,
            null,
            DateTimeOffset.Parse("2026-09-17T00:00:00Z")));
        var tool = new ChatWebTool(search, crawler, new SourceCandidateSelector(new SourceUrlNormalizer()), new SourceUrlNormalizer());
        var company = new Company { Name = "Example", Website = "https://example.com", Country = "Vietnam" };

        var searchResult = await tool.SearchAsync(company, "Example recent updates", CancellationToken.None);

        Assert.True(searchResult.Succeeded);
        Assert.Equal(5, search.Request!.MaxResults);
        var official = searchResult.Candidates[0];
        Assert.Equal("https://example.com/about", official.NormalizedUrl);
        Assert.Equal("brave", official.SearchProvider);

        var readResult = await tool.ReadAsync(official.Id, CancellationToken.None);

        Assert.True(readResult.Succeeded);
        Assert.Equal("https://example.com/about", crawler.RequestedUrl);
        Assert.NotNull(readResult.Evidence);
        Assert.Equal("brave", readResult.Evidence.SearchProvider);
        Assert.Equal("crawl4ai-local", readResult.Evidence.CrawlerProvider);
        Assert.InRange(readResult.Evidence.ContentExcerpt.Length, 1, 8_000);
    }

    [Fact]
    public async Task Read_rejects_unknown_candidates_and_search_stays_within_its_budget()
    {
        var search = new FakeSearchProvider(new SearchResponse("brave", [new SearchResult("Official", "https://example.com", null, 1)]));
        var crawler = new FakeCrawlerProvider(request => throw new InvalidOperationException($"Crawler should not be called for {request.Url}."));
        var tool = new ChatWebTool(search, crawler, new SourceCandidateSelector(new SourceUrlNormalizer()), new SourceUrlNormalizer());
        var company = new Company { Name = "Example", Website = "https://example.com" };

        var unknown = await tool.ReadAsync("r999", CancellationToken.None);
        await tool.SearchAsync(company, "Example one", CancellationToken.None);
        await tool.SearchAsync(company, "Example two", CancellationToken.None);
        var exhausted = await tool.SearchAsync(company, "Example three", CancellationToken.None);

        Assert.False(unknown.Succeeded);
        Assert.Equal("candidate_not_found", unknown.ErrorCode);
        Assert.False(exhausted.Succeeded);
        Assert.Equal("search_budget_exhausted", exhausted.ErrorCode);
        Assert.Equal(2, search.CallCount);
    }

    [Fact]
    public async Task Search_filters_private_candidates_and_rejects_an_unsafe_crawler_final_url()
    {
        var search = new FakeSearchProvider(new SearchResponse("brave", [
            new SearchResult("Local", "http://127.0.0.1/admin", null, 1),
            new SearchResult("Private", "http://10.0.0.5/internal", null, 2),
            new SearchResult("Public", "https://example.com/news", null, 3)
        ]));
        var crawler = new FakeCrawlerProvider(request => new CrawlResult(
            "crawl4ai-local", request.Url, "http://127.0.0.1/redirected", "Redirected", "content", true, null, DateTimeOffset.UtcNow));
        var tool = new ChatWebTool(search, crawler, new SourceCandidateSelector(new SourceUrlNormalizer()), new SourceUrlNormalizer());

        var searchResult = await tool.SearchAsync(new Company { Name = "Example", Website = "https://example.com" }, "Example update", CancellationToken.None);
        var candidate = Assert.Single(searchResult.Candidates);
        var readResult = await tool.ReadAsync(candidate.Id, CancellationToken.None);

        Assert.Equal("https://example.com/news", candidate.NormalizedUrl);
        Assert.Equal("https://example.com/news", crawler.RequestedUrl);
        Assert.False(readResult.Succeeded);
        Assert.Equal("unsafe_final_url", readResult.ErrorCode);
    }

    [Fact]
    public async Task One_failed_crawl_does_not_prevent_a_later_candidate_from_returning_evidence()
    {
        var search = new FakeSearchProvider(new SearchResponse("brave", [
            new SearchResult("First", "https://example.com/first", null, 1),
            new SearchResult("Second", "https://example.com/second", null, 2)
        ]));
        var crawler = new FakeCrawlerProvider(request => request.Url.EndsWith("/first", StringComparison.Ordinal)
            ? new CrawlResult("crawl4ai-local", request.Url, request.Url, "First", null, false, "unavailable", DateTimeOffset.UtcNow)
            : new CrawlResult("crawl4ai-local", request.Url, request.Url, "Second", "verified evidence", true, null, DateTimeOffset.UtcNow));
        var tool = new ChatWebTool(search, crawler, new SourceCandidateSelector(new SourceUrlNormalizer()), new SourceUrlNormalizer());

        var searchResult = await tool.SearchAsync(new Company { Name = "Example", Website = "https://example.com" }, "Example update", CancellationToken.None);
        var first = await tool.ReadAsync(searchResult.Candidates[0].Id, CancellationToken.None);
        var second = await tool.ReadAsync(searchResult.Candidates[1].Id, CancellationToken.None);

        Assert.False(first.Succeeded);
        Assert.Equal("crawl_failed", first.ErrorCode);
        Assert.True(second.Succeeded);
        Assert.Equal("verified evidence", second.Evidence!.ContentExcerpt);
    }
    private sealed class FakeSearchProvider(SearchResponse response) : ISearchProvider
    {
        public string Id => "fake-search";
        public int CallCount { get; private set; }
        public SearchRequest? Request { get; private set; }

        public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            Request = request;
            return Task.FromResult(response);
        }
    }

    private sealed class FakeCrawlerProvider(Func<CrawlRequest, CrawlResult> crawl) : ICrawlerProvider
    {
        public string Id => "fake-crawler";
        public string? RequestedUrl { get; private set; }

        public Task<CrawlResult> CrawlAsync(CrawlRequest request, CancellationToken cancellationToken = default)
        {
            RequestedUrl = request.Url;
            return Task.FromResult(crawl(request));
        }
    }
}
