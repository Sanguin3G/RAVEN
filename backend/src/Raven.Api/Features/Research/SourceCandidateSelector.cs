using Raven.Api.Features.Companies;
using Raven.Api.Features.Search;

namespace Raven.Api.Features.Research;

public sealed record SourceCandidate(
    string Url,
    string NormalizedUrl,
    string Title,
    string? Snippet,
    string SourceDomain,
    int Score,
    string Reason,
    int SearchRank);

public sealed class SourceCandidateSelector(SourceUrlNormalizer urlNormalizer)
{
    private const int MaximumCandidates = 5;
    private const int MaximumDiscoveryCandidates = 50;

    public IReadOnlyList<SourceCandidate> Select(Company company, IEnumerable<SearchResult> searchResults)
        => Discover(company, searchResults).Take(MaximumCandidates).ToArray();

    /// <summary>
    /// Normalizes, filters, de-duplicates, and ranks discovery results without
    /// applying the small Day-2 acquisition limit. Research review uses this
    /// bounded list so a person can choose among more than five candidates.
    /// </summary>
    public IReadOnlyList<SourceCandidate> Discover(Company company, IEnumerable<SearchResult> searchResults)
    {
        var officialHost = GetHost(company.Website);
        var candidates = new List<SourceCandidate>();
        foreach (var result in searchResults)
        {
            var normalizedUrl = urlNormalizer.Normalize(result.Url);
            if (normalizedUrl is null || !Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
            {
                continue;
            }

            var (score, reason) = Score(uri, result, officialHost);
            if (score < 0)
            {
                continue;
            }

            candidates.Add(new SourceCandidate(result.Url, normalizedUrl, result.Title, result.Snippet, uri.Host, score, reason, result.Rank));
        }

        return candidates
            .GroupBy(candidate => candidate.NormalizedUrl, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.SearchRank).First())
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.SearchRank)
            .Take(MaximumDiscoveryCandidates)
            .ToArray();
    }

    private static (int Score, string Reason) Score(Uri uri, SearchResult result, string? officialHost)
    {
        var score = Math.Max(0, 100 - result.Rank);
        var reasons = new List<string> { "search rank" };
        var path = uri.AbsolutePath.ToLowerInvariant();
        var host = uri.Host.ToLowerInvariant();

        if (officialHost is not null && (host == officialHost || host.EndsWith($".{officialHost}", StringComparison.Ordinal)))
        {
            score += 1_000;
            reasons.Add("official domain");
        }

        if (new[] { "/about", "/about-us", "/company", "/who-we-are", "/products", "/services", "/solutions", "/markets", "/locations", "/global", "/investor", "/investors" }
            .Any(signal => path.Contains(signal, StringComparison.Ordinal)))
        {
            score += 100;
            reasons.Add("company-information path");
        }

        if (new[] { "/login", "/sign-in", "/privacy", "/terms", "/search" }.Any(signal => path.Contains(signal, StringComparison.Ordinal)))
        {
            score -= 2_000;
            reasons.Add("low-value path");
        }

        if (host is "facebook.com" or "linkedin.com" or "x.com" or "twitter.com")
        {
            score -= 250;
            reasons.Add("social profile");
        }

        return (score, string.Join(", ", reasons));
    }

    private static string? GetHost(string? website) =>
        Uri.TryCreate(website, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : null;
}
