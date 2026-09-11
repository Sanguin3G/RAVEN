using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Coverage;
using Raven.Api.Features.Research.Planning;

namespace Raven.Api.Tests;

public sealed class OfficialSiteEvidencePlannerTests
{
    private readonly OfficialSiteEvidencePlanner planner = new(new SourceUrlNormalizer());

    [Fact]
    public void Plan_keeps_pages_on_the_approved_domain_only()
    {
        var candidates = Plan(
            "https://www.example.com",
            [ResearchTarget.ProductsServices],
            new OfficialSiteLink("https://example.com/products", "Products"),
            new OfficialSiteLink("https://www.example.com/services", "Services"),
            new OfficialSiteLink("https://blog.example.com/products", "Blog products"),
            new OfficialSiteLink("https://example.com.evil.test/products", "Spoof"));

        Assert.Equal(2, candidates.Count);
        Assert.All(candidates, candidate => Assert.Equal("example.com", candidate.Domain.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Plan_deduplicates_normalized_urls_before_applying_page_budget()
    {
        var candidates = Plan(
            "https://example.com",
            [ResearchTarget.Leadership],
            new OfficialSiteLink("https://example.com/leadership/?utm_source=search#team", "lower", DiscoveryRank: 3),
            new OfficialSiteLink("https://EXAMPLE.com/leadership", "Leadership", DiscoveryRank: 1));

        var candidate = Assert.Single(candidates);
        Assert.Equal("https://example.com/leadership", candidate.NormalizedUrl);
        Assert.Equal("Leadership", candidate.Title);
        Assert.Equal(1, candidate.DiscoveryRank);
    }

    [Fact]
    public void Plan_prioritizes_target_specific_pages()
    {
        var candidates = Plan(
            "https://example.com",
            [ResearchTarget.Leadership],
            new OfficialSiteLink("https://example.com/about", "About", DiscoveryRank: 1),
            new OfficialSiteLink("https://example.com/management", "Management", DiscoveryRank: 2),
            new OfficialSiteLink("https://example.com/contact", "Contact", DiscoveryRank: 3));

        Assert.Equal("https://example.com/management", candidates[0].NormalizedUrl);
        Assert.Contains(ResearchTarget.Leadership, candidates[0].MatchedTargets);
        Assert.Contains("Management page", candidates[0].RecommendationReasons);
    }

    [Fact]
    public void Plan_prioritizes_products_markets_and_locations_for_their_targets()
    {
        var candidates = Plan(
            "https://example.com",
            [ResearchTarget.ProductsServices, ResearchTarget.Markets, ResearchTarget.Locations],
            new OfficialSiteLink("https://example.com/products", "Products"),
            new OfficialSiteLink("https://example.com/customers", "Customers"),
            new OfficialSiteLink("https://example.com/offices", "Offices"),
            new OfficialSiteLink("https://example.com/overview", "Overview"));

        Assert.Equal("https://example.com/offices", candidates[0].NormalizedUrl);
        Assert.Equal("https://example.com/products", candidates[1].NormalizedUrl);
        Assert.Equal("https://example.com/customers", candidates[2].NormalizedUrl);
        Assert.Contains(ResearchTarget.Locations, candidates[0].MatchedTargets);
        Assert.Contains(ResearchTarget.ProductsServices, candidates[1].MatchedTargets);
        Assert.Contains(ResearchTarget.Markets, candidates[2].MatchedTargets);
    }

    [Fact]
    public void Plan_rejects_noise_and_news_archive_pages()
    {
        var candidates = Plan(
            "https://example.com",
            [ResearchTarget.Markets],
            new OfficialSiteLink("https://example.com/login", "Login"),
            new OfficialSiteLink("https://example.com/privacy", "Privacy"),
            new OfficialSiteLink("https://example.com/news", "News"),
            new OfficialSiteLink("https://example.com/news/page/2", "News page"),
            new OfficialSiteLink("https://example.com/news/2025", "News year"),
            new OfficialSiteLink("https://example.com/search?q=market", "Search"),
            new OfficialSiteLink("https://example.com/assets/logo.png", "Logo"),
            new OfficialSiteLink("https://example.com/terms", "Terms"));

        Assert.Empty(candidates);
    }

    [Fact]
    public void Plan_enforces_the_twelve_page_budget_and_does_not_follow_links()
    {
        var links = Enumerable.Range(1, 50)
            .Select(index => new OfficialSiteLink($"https://example.com/page-{index}", $"Page {index}", DiscoveryRank: index))
            .ToArray();

        var candidates = planner.Plan(new OfficialSiteEvidenceRequest(
            "https://example.com",
            links,
            [ResearchTarget.ProductsServices],
            MaximumPages: 500));

        Assert.Equal(OfficialSiteEvidencePlanner.AbsoluteMaximumPages, candidates.Count);
        Assert.All(candidates, candidate => Assert.StartsWith("https://example.com/", candidate.NormalizedUrl, StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_keeps_individual_news_article_as_low_priority_supporting_context()
    {
        var candidates = Plan(
            "https://example.com",
            [ResearchTarget.Leadership],
            new OfficialSiteLink("https://example.com/news/ceo-appointed", "Leadership announcement"),
            new OfficialSiteLink("https://example.com/leadership", "Leadership"));

        Assert.Equal("https://example.com/leadership", candidates[0].NormalizedUrl);
        Assert.Contains("Supporting company news", candidates[1].RecommendationReasons);
        Assert.True(candidates[0].Priority > candidates[1].Priority);
    }

    private IReadOnlyList<OfficialSiteEvidenceCandidate> Plan(
        string officialWebsite,
        IReadOnlyCollection<ResearchTarget> targets,
        params OfficialSiteLink[] links) =>
        planner.Plan(new OfficialSiteEvidenceRequest(officialWebsite, links, targets));
}
