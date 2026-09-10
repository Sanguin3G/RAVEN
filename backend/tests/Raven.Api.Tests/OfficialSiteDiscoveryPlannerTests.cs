using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Planning;

namespace Raven.Api.Tests;

public sealed class OfficialSiteDiscoveryPlannerTests
{
    private readonly OfficialSiteDiscoveryPlanner planner = new(new SourceUrlNormalizer());

    [Fact]
    public void Plan_keeps_only_the_official_host_with_www_equivalence()
    {
        var candidates = Plan(
            "https://www.example.com",
            new OfficialSiteLink("https://example.com/about", "About"),
            new OfficialSiteLink("https://www.example.com/contact", "Contact"),
            new OfficialSiteLink("https://blog.example.com/company", "Blog"),
            new OfficialSiteLink("https://example.com.evil.test/about", "Spoof"));

        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate => Assert.Contains(candidate.Domain, new[] { "example.com", "www.example.com" }));
    }

    [Fact]
    public void Plan_deduplicates_normalized_urls_and_keeps_the_best_metadata()
    {
        var candidates = Plan(
            "https://example.com",
            new OfficialSiteLink("https://example.com/about/?utm_source=search#team", "About duplicate", "lower", 5),
            new OfficialSiteLink("https://EXAMPLE.com/about", "About", "better", 2));

        var candidate = Assert.Single(candidates);
        Assert.Equal("https://example.com/about", candidate.NormalizedUrl);
        Assert.Equal("About", candidate.Title);
        Assert.Equal(2, candidate.DiscoveryRank);
    }

    [Fact]
    public void Plan_is_bounded_and_orders_high_value_company_pages_first()
    {
        var links = Enumerable.Range(1, 30)
            .Select(index => new OfficialSiteLink($"https://example.com/page-{index}", $"Page {index}", DiscoveryRank: index))
            .Append(new OfficialSiteLink("https://example.com/products", "Products", DiscoveryRank: 30))
            .Append(new OfficialSiteLink("https://example.com/about-us", "About", DiscoveryRank: 29))
            .Append(new OfficialSiteLink("https://example.com/leadership", "Leadership", DiscoveryRank: 28));

        var candidates = planner.Plan(new OfficialSiteDiscoveryRequest("https://example.com", links.ToArray()));

        Assert.Equal(20, candidates.Count);
        Assert.Equal("https://example.com/about-us", candidates[0].NormalizedUrl);
        Assert.Contains("About/company page", candidates[0].RecommendationReasons);
        Assert.Equal("https://example.com/products", candidates[1].NormalizedUrl);
    }

    [Fact]
    public void Plan_rejects_auth_privacy_taxonomy_pagination_and_media_junk()
    {
        var candidates = Plan(
            "https://example.com",
            new OfficialSiteLink("https://example.com/login", "Login"),
            new OfficialSiteLink("https://example.com/privacy", "Privacy"),
            new OfficialSiteLink("https://example.com/blog/tag/technology", "Tag"),
            new OfficialSiteLink("https://example.com/news?page=2", "News page"),
            new OfficialSiteLink("https://example.com/search?q=company", "Search"),
            new OfficialSiteLink("https://example.com/assets/logo.png", "Logo"),
            new OfficialSiteLink("https://example.com/terms", "Terms"));

        Assert.Empty(candidates);
    }

    [Fact]
    public void Plan_prioritizes_useful_official_documents()
    {
        var candidates = Plan(
            "https://example.com",
            new OfficialSiteLink("https://example.com/downloads/annual-report-2025.pdf", "Annual Report"),
            new OfficialSiteLink("https://example.com/downloads/company-brochure.pdf", "Brochure"),
            new OfficialSiteLink("https://example.com/about", "About"),
            new OfficialSiteLink("https://example.com/assets/random.pdf", "Document", DiscoveryRank: 1));

        Assert.Equal(4, candidates.Count);
        var annualReport = Assert.Single(candidates, candidate => candidate.Url.Contains("annual-report", StringComparison.Ordinal));
        Assert.Contains("Official document", annualReport.RecommendationReasons);
        Assert.Contains("Useful company report/profile document", annualReport.RecommendationReasons);
        Assert.True(annualReport.Priority > candidates[^1].Priority);
    }

    [Fact]
    public void Plan_discards_invalid_official_site_input()
    {
        var candidates = planner.Plan(new OfficialSiteDiscoveryRequest(
            "not a url",
            [new OfficialSiteLink("https://example.com/about")]));

        Assert.Empty(candidates);
    }

    private IReadOnlyList<OfficialSiteCandidate> Plan(string officialWebsite, params OfficialSiteLink[] links) =>
        planner.Plan(new OfficialSiteDiscoveryRequest(officialWebsite, links));
}
