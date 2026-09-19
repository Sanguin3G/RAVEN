using Raven.Api.Features.Companies;
using Raven.Api.Features.Research;
using Raven.Api.Features.Search;

namespace Raven.Api.Features.Chat;

/// <summary>Chat-specific deterministic ranking; profile research selection must not be reused here.</summary>
public sealed class ChatWebSearchReranker(SourceUrlNormalizer urlNormalizer)
{
    private const int MaximumCandidates = 5;
    private const int MaximumPerDomain = 2;

    public IReadOnlyList<ChatRankedSearchResult> Rank(Company company, string query, IEnumerable<SearchResult> results)
    {
        var queryTerms = Tokenize(query);
        var officialHost = Host(company.Website);
        return results.Select(result => ToCandidate(result, queryTerms, officialHost)).Where(candidate => candidate is not null).Cast<ChatRankedSearchResult>()
            .GroupBy(candidate => candidate.NormalizedUrl, StringComparer.Ordinal).Select(group => group.OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.SearchRank).First())
            .OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.SearchRank)
            .GroupBy(candidate => candidate.Domain, StringComparer.OrdinalIgnoreCase).SelectMany(group => group.Take(MaximumPerDomain)).Take(MaximumCandidates).ToArray();
    }

    private ChatRankedSearchResult? ToCandidate(SearchResult result, IReadOnlySet<string> queryTerms, string? officialHost)
    {
        var normalized = urlNormalizer.Normalize(result.Url);
        if (normalized is null || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.ToLowerInvariant(); var path = uri.AbsolutePath.ToLowerInvariant();
        if (IsLowValue(host, path)) return null;
        var corpus = $"{result.Title} {result.Snippet}";
        var relevance = queryTerms.Count == 0 ? 0 : (int)Math.Round(35d * Tokenize(corpus).Intersect(queryTerms).Count() / queryTerms.Count);
        var providerRank = Math.Max(0, 20 - Math.Min(20, Math.Max(0, result.Rank - 1) * 4));
        var authority = Authority(host, path, officialHost);
        var identity = officialHost is not null && (host == officialHost || host.EndsWith($".{officialHost}", StringComparison.Ordinal)) ? 15 : 0;
        var freshness = HasFreshnessSignal(corpus) ? 5 : 0;
        var urlQuality = path.Length < 100 && !uri.Query.Contains("utm_", StringComparison.OrdinalIgnoreCase) ? 5 : 2;
        return new(result.Url, normalized, result.Title, result.Snippet, result.Rank, relevance + providerRank + authority + identity + freshness + urlQuality, host, BuildReason(relevance, authority, identity, freshness));
    }
    private static bool IsLowValue(string host, string path) => path.Contains("/login", StringComparison.Ordinal) || path.Contains("/sign-in", StringComparison.Ordinal) || path.Contains("/search", StringComparison.Ordinal) || host is "facebook.com" or "linkedin.com" or "x.com" or "twitter.com" || host.Contains("google.", StringComparison.Ordinal);
    private static int Authority(string host, string path, string? officialHost) => officialHost is not null && (host == officialHost || host.EndsWith($".{officialHost}", StringComparison.Ordinal)) ? 20 : path.Contains("news", StringComparison.Ordinal) || path.Contains("press", StringComparison.Ordinal) || path.Contains("investor", StringComparison.Ordinal) ? 12 : 6;
    private static bool HasFreshnessSignal(string text) => text.Contains("2026", StringComparison.Ordinal) || text.Contains("2025", StringComparison.Ordinal) || text.Contains("news", StringComparison.OrdinalIgnoreCase);
    private static string BuildReason(int relevance, int authority, int identity, int freshness) => string.Join(", ", new[] { relevance > 0 ? "query relevance" : null, authority >= 12 ? "authoritative path" : null, identity > 0 ? "official domain" : null, freshness > 0 ? "freshness signal" : null }.Where(value => value is not null));
    private static string? Host(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : null;
    private static HashSet<string> Tokenize(string value) => value.ToLowerInvariant().Split([' ', '\t', '\r', '\n', ',', '.', ':', ';', '?', '!'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(term => term.Length > 2).ToHashSet(StringComparer.Ordinal);
}
public sealed record ChatRankedSearchResult(string Url, string NormalizedUrl, string Title, string? Snippet, int SearchRank, int Score, string Domain, string Reason);