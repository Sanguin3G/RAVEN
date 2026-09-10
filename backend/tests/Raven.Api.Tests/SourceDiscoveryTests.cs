using Raven.Api.Features.Companies;
using Raven.Api.Features.Research;
using Raven.Api.Features.Search;

namespace Raven.Api.Tests;

public sealed class SourceDiscoveryTests
{
    private readonly SourceUrlNormalizer normalizer = new();

    [Theory]
    [InlineData("https://EXAMPLE.com/about/#team", "https://example.com/about")]
    [InlineData("https://example.com/about/?utm_source=google&ref=products", "https://example.com/about?ref=products")]
    [InlineData("http://example.com/", "http://example.com/")]
    public void Normalize_removes_fragments_tracking_and_noncanonical_trailing_slashes(string source, string expected)
    {
        Assert.Equal(expected, normalizer.Normalize(source));
    }

    [Theory]
    [InlineData("mailto:research@example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    public void Normalize_rejects_unsupported_or_invalid_urls(string source)
    {
        Assert.Null(normalizer.Normalize(source));
    }

    [Fact]
    public void Select_deduplicates_urls_and_prioritizes_the_official_domain()
    {
        var company = new Company { Name = "FPT Software", Website = "https://fptsoftware.com" };
        var selector = new SourceCandidateSelector(normalizer);
        var selected = selector.Select(company,
        [
            new SearchResult("Directory entry", "https://directory.example/fpt", null, 1),
            new SearchResult("About FPT", "https://fptsoftware.com/about/?utm_source=brave", null, 5),
            new SearchResult("About FPT duplicate", "https://fptsoftware.com/about/#history", null, 2),
            new SearchResult("Privacy", "https://fptsoftware.com/privacy", null, 1)
        ]);

        Assert.Equal(2, selected.Count);
        Assert.Equal("https://fptsoftware.com/about", selected[0].NormalizedUrl);
        Assert.Contains("official domain", selected[0].Reason);
        Assert.DoesNotContain(selected, candidate => candidate.NormalizedUrl.Contains("privacy", StringComparison.Ordinal));
    }
}
