using Raven.Api.Features.Companies;
using Raven.Api.Features.Research;
using Raven.Api.Features.Search;
using System.Text.RegularExpressions;

namespace Raven.Api.Features.Chat;

/// <summary>Chat-specific deterministic ranking; profile research selection must not be reused here.</summary>
public sealed class ChatWebSearchReranker(SourceUrlNormalizer urlNormalizer)
{
    private const int MaximumCandidates = 5;
    private const int MaximumPerDomain = 2;

    public IReadOnlyList<ChatRankedSearchResult> Rank(Company company, string query, IEnumerable<SearchResult> results)
        => Rank(company, query, results, company.Website);

    public IReadOnlyList<ChatRankedSearchResult> Rank(Company company, string query, IEnumerable<SearchResult> results, string? acceptedProfileWebsite)
    {
        var queryTerms = Tokenize(query);
        var companyTerms = Tokenize(company.Name);
        var requestedYears = Regex.Matches(query, @"\b(?:19|20)\d{2}\b").Select(match => match.Value).ToHashSet(StringComparer.Ordinal);
        var officialHost = Host(acceptedProfileWebsite) ?? Host(company.Website);
        return results.Select(result => ToCandidate(result, company.Name, queryTerms, companyTerms, requestedYears, officialHost)).Where(candidate => candidate is not null).Cast<ChatRankedSearchResult>()
            .GroupBy(candidate => candidate.NormalizedUrl, StringComparer.Ordinal).Select(group => group.OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.SearchRank).First())
            .OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.SearchRank)
            .GroupBy(candidate => candidate.Domain, StringComparer.OrdinalIgnoreCase).SelectMany(group => group.Take(MaximumPerDomain)).Take(MaximumCandidates).ToArray();
    }

    private ChatRankedSearchResult? ToCandidate(SearchResult result, string companyName, IReadOnlySet<string> queryTerms, IReadOnlySet<string> companyTerms, IReadOnlySet<string> requestedYears, string? officialHost)
    {
        var normalized = urlNormalizer.Normalize(result.Url);
        if (normalized is null || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.ToLowerInvariant(); var path = uri.AbsolutePath.ToLowerInvariant();
        if (IsLowValue(host, path)) return null;
        var corpus = $"{result.Title} {result.Snippet}";
        var corpusTerms = Tokenize(corpus);
        var relevance = queryTerms.Count == 0 ? 0 : (int)Math.Round(25d * corpusTerms.Intersect(queryTerms).Count() / queryTerms.Count);
        var official = officialHost is not null && (host == officialHost || host.EndsWith($".{officialHost}", StringComparison.Ordinal));
        var exactCompany = corpus.Contains(companyName, StringComparison.OrdinalIgnoreCase);
        var entity = official || exactCompany ? 30 : companyTerms.Count == 0 ? 0 : (int)Math.Round(20d * corpusTerms.Intersect(companyTerms).Count() / companyTerms.Count);
        var timeCoverage = requestedYears.Count == 0 ? 10 : (int)Math.Round(20d * requestedYears.Count(year => corpus.Contains(year, StringComparison.Ordinal)) / requestedYears.Count);
        var providerRank = Math.Max(0, 5 - Math.Min(5, Math.Max(0, result.Rank - 1)));
        var authority = Authority(host, path, officialHost);
        var urlQuality = path.Length < 100 && !uri.Query.Contains("utm_", StringComparison.OrdinalIgnoreCase) ? 5 : 2;
        return new(result.Url, normalized, result.Title, result.Snippet, result.Rank, relevance + entity + timeCoverage + authority + providerRank + urlQuality, host, BuildReason(relevance, entity, timeCoverage, authority));
    }
    private static bool IsLowValue(string host, string path) => path.Contains("/login", StringComparison.Ordinal) || path.Contains("/sign-in", StringComparison.Ordinal) || path.Contains("/search", StringComparison.Ordinal) || host is "facebook.com" or "linkedin.com" or "x.com" or "twitter.com" || host.Contains("google.", StringComparison.Ordinal);
    private static int Authority(string host, string path, string? officialHost) => officialHost is not null && (host == officialHost || host.EndsWith($".{officialHost}", StringComparison.Ordinal)) ? 15 : path.Contains("news", StringComparison.Ordinal) || path.Contains("press", StringComparison.Ordinal) || path.Contains("investor", StringComparison.Ordinal) || path.Contains("award", StringComparison.Ordinal) ? 10 : 5;
    private static string BuildReason(int relevance, int entity, int timeCoverage, int authority) => string.Join(", ", new[] { entity >= 20 ? "company identity" : null, relevance > 0 ? "query relevance" : null, timeCoverage > 0 ? "time coverage" : null, authority >= 10 ? "authoritative source" : null }.Where(value => value is not null));
    private static string? Host(string? url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : null;
    private static HashSet<string> Tokenize(string value) => Regex.Split(value.ToLowerInvariant(), @"[^\p{L}\p{N}]+").Where(term => term.Length > 2).ToHashSet(StringComparer.Ordinal);
}
public sealed record ChatRankedSearchResult(string Url, string NormalizedUrl, string Title, string? Snippet, int SearchRank, int Score, string Domain, string Reason);
