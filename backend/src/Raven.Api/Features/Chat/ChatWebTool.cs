using System.Diagnostics;
using System.Net;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Research;
using Raven.Api.Features.Search;

namespace Raven.Api.Features.Chat;

/// <summary>
/// Request-scoped, bounded access to the normal RAVEN Search/Crawl providers.
/// It only exposes candidates returned by Search, never an arbitrary URL, and
/// returns drafts that a later Chat turn may persist as Web Evidence Snapshots.
/// </summary>
public sealed class ChatWebTool(
    ISearchProvider searchProvider,
    ICrawlerProvider crawlerProvider,
    ChatWebSearchReranker reranker,
    ChatEvidenceReranker evidenceReranker,
    SourceUrlNormalizer urlNormalizer)
{
    private const int MaximumSearchCalls = 2;
    private const int MaximumCrawlCalls = 3;
    private const int MaximumSearchResults = 5;
    private readonly Dictionary<string, ChatWebCandidate> candidates = new(StringComparer.Ordinal);
    private int searchCalls;
    private int crawlCalls;
    private int candidateSequence;

    // Keeps focused tests and small consumers source-compatible while production DI uses the chat-specific rankers.
    public ChatWebTool(ISearchProvider searchProvider, ICrawlerProvider crawlerProvider, SourceCandidateSelector _, SourceUrlNormalizer urlNormalizer)
        : this(searchProvider, crawlerProvider, new ChatWebSearchReranker(urlNormalizer), new ChatEvidenceReranker(), urlNormalizer) { }

    public async Task<ChatWebSearchResult> SearchAsync(Company company, string query, CancellationToken cancellationToken)
    {
        var normalizedQuery = ChatText.NormalizeQuestion(query);
        if (normalizedQuery.Length is < 1 or > 500)
        {
            return ChatWebSearchResult.Failure("validation_failed", "Search query must contain between 1 and 500 characters.");
        }

        if (searchCalls++ >= MaximumSearchCalls)
        {
            return ChatWebSearchResult.Failure("search_budget_exhausted", "The Web Search budget is exhausted for this answer.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await searchProvider.SearchAsync(
                new SearchRequest(normalizedQuery, MaximumSearchResults, company.Country),
                cancellationToken);
            stopwatch.Stop();

            var results = reranker.Rank(company, normalizedQuery, response.Results.Where(result => IsSafePublicHttpUrl(result.Url)))
                .Select(candidate =>
                {
                    var result = new ChatWebCandidate(
                        $"r{++candidateSequence}",
                        candidate.Url,
                        candidate.NormalizedUrl,
                        candidate.Title,
                        candidate.Snippet,
                        candidate.SearchRank,
                        candidate.Score,
                        candidate.Reason,
                        response.Provider);
                    candidates.Add(result.Id, result);
                    return result;
                })
                .ToArray();

            Console.WriteLine($"[Ask RAVEN Web Search] provider={response.Provider}; pages={results.Length}");
            foreach (var candidate in results) Console.WriteLine($"  #{candidate.SearchRank} score={candidate.Score} {candidate.Url}");

            return ChatWebSearchResult.Success(response.Provider, results, new ChatWebToolExecution(
                "search_web",
                response.Provider,
                "succeeded",
                stopwatch.ElapsedMilliseconds,
                ChatText.Bound(normalizedQuery, 500),
                $"{results.Length} ranked candidate(s)",
                null));
        }
        catch (ProviderException exception)
        {
            stopwatch.Stop();
            return ChatWebSearchResult.Failure(
                ProviderErrorCode(exception),
                "Search provider could not complete this request.",
                new ChatWebToolExecution("search_web", exception.Provider, "failed", stopwatch.ElapsedMilliseconds, ChatText.Bound(normalizedQuery, 500), null, ProviderErrorCode(exception)));
        }
    }

    public async Task<ChatWebReadResult> ReadAsync(string candidateId, CancellationToken cancellationToken)
    {
        if (!candidates.TryGetValue(candidateId, out var candidate))
        {
            return ChatWebReadResult.Failure("candidate_not_found", "The requested web candidate is not available in this Chat turn.");
        }

        if (!IsSafePublicHttpUrl(candidate.NormalizedUrl))
        {
            return ChatWebReadResult.Failure("unsafe_candidate_url", "The requested web candidate is not safe to crawl.");
        }

        if (crawlCalls++ >= MaximumCrawlCalls)
        {
            return ChatWebReadResult.Failure("crawl_budget_exhausted", "The Web Crawl budget is exhausted for this answer.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            Console.WriteLine($"[Ask RAVEN Web Crawl] start {candidate.Url}");
            var crawl = await crawlerProvider.CrawlAsync(new CrawlRequest(candidate.NormalizedUrl), cancellationToken);
            stopwatch.Stop();
            if (!crawl.Success || string.IsNullOrWhiteSpace(crawl.Markdown))
            {
                return ChatWebReadResult.Failure(
                    "crawl_failed",
                    "Crawler could not retrieve readable evidence from this candidate.",
                    new ChatWebToolExecution("read_web_page", crawl.Provider, "failed", stopwatch.ElapsedMilliseconds, candidate.Id, null, "crawl_failed"));
            }

            var finalUrl = crawl.FinalUrl ?? candidate.Url;
            if (!IsSafePublicHttpUrl(finalUrl))
            {
                return ChatWebReadResult.Failure(
                    "unsafe_final_url",
                    "Crawler returned an unsafe final URL.",
                    new ChatWebToolExecution("read_web_page", crawl.Provider, "failed", stopwatch.ElapsedMilliseconds, candidate.Id, null, "unsafe_final_url"));
            }

            var normalizedUrl = urlNormalizer.Normalize(finalUrl) ?? candidate.NormalizedUrl;
            var evidence = new ChatWebEvidenceDraft(
                candidate.Id,
                finalUrl,
                normalizedUrl,
                crawl.Title ?? candidate.Title,
                candidate.Snippet,
                ChatText.Bound(evidenceReranker.Select(candidate.Title ?? candidate.Id, crawl.Markdown), 8_000),
                candidate.SearchProvider,
                crawl.Provider,
                candidate.SearchRank,
                crawl.RetrievedAt);
            Console.WriteLine($"[Ask RAVEN Web Crawl] success {finalUrl} provider={crawl.Provider}");
            return ChatWebReadResult.Success(evidence, new ChatWebToolExecution(
                "read_web_page",
                crawl.Provider,
                "succeeded",
                stopwatch.ElapsedMilliseconds,
                candidate.Id,
                $"{evidence.ContentExcerpt.Length} evidence characters",
                null));
        }
        catch (ProviderException exception)
        {
            stopwatch.Stop();
            return ChatWebReadResult.Failure(
                ProviderErrorCode(exception),
                "Crawler provider could not complete this request.",
                new ChatWebToolExecution("read_web_page", exception.Provider, "failed", stopwatch.ElapsedMilliseconds, candidate.Id, null, ProviderErrorCode(exception)));
        }
    }

    private static bool IsSafePublicHttpUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
        var host = uri.Host.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return false;
        return !IPAddress.TryParse(host, out var address) || IsPublicAddress(address);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) return IsPublicAddress(address.MapToIPv4());
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return !IPAddress.IsLoopback(address) && !address.Equals(IPAddress.IPv6Any) && !address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal;
        }

        var octets = address.GetAddressBytes();
        return octets[0] switch
        {
            0 or 10 or 127 => false,
            169 when octets[1] == 254 => false,
            172 when octets[1] is >= 16 and <= 31 => false,
            192 when octets[1] == 168 => false,
            _ => true
        };
    }
    private static string ProviderErrorCode(ProviderException exception) =>
        $"provider_{exception.Kind.ToString().ToLowerInvariant()}";
}

public sealed record ChatWebCandidate(
    string Id,
    string Url,
    string NormalizedUrl,
    string Title,
    string? Snippet,
    int SearchRank,
    int Score,
    string RankReason,
    string SearchProvider);

public sealed record ChatWebEvidenceDraft(
    string CandidateId,
    string Url,
    string NormalizedUrl,
    string? Title,
    string? SearchSnippet,
    string ContentExcerpt,
    string SearchProvider,
    string CrawlerProvider,
    int SearchRank,
    DateTimeOffset RetrievedAt)
{
    public string ToPromptText() => $"""
        WEB_EVIDENCE_ID: {CandidateId}
        TITLE: {Title}
        URL: {Url}
        SEARCH_RANK: {SearchRank}
        CONTENT:
        {ContentExcerpt}
        """;
}

public sealed record ChatWebToolExecution(
    string Tool,
    string Provider,
    string Status,
    long DurationMs,
    string? InputSummary,
    string? OutputSummary,
    string? ErrorCode);

public sealed record ChatWebSearchResult(
    bool Succeeded,
    string? Provider,
    IReadOnlyList<ChatWebCandidate> Candidates,
    ChatWebToolExecution? Execution,
    string? ErrorCode,
    string? Error)
{
    public static ChatWebSearchResult Success(string provider, IReadOnlyList<ChatWebCandidate> candidates, ChatWebToolExecution execution) =>
        new(true, provider, candidates, execution, null, null);

    public static ChatWebSearchResult Failure(string errorCode, string error, ChatWebToolExecution? execution = null) =>
        new(false, null, [], execution, errorCode, error);
}

public sealed record ChatWebReadResult(
    bool Succeeded,
    ChatWebEvidenceDraft? Evidence,
    ChatWebToolExecution? Execution,
    string? ErrorCode,
    string? Error)
{
    public static ChatWebReadResult Success(ChatWebEvidenceDraft evidence, ChatWebToolExecution execution) =>
        new(true, evidence, execution, null, null);

    public static ChatWebReadResult Failure(string errorCode, string error, ChatWebToolExecution? execution = null) =>
        new(false, null, execution, errorCode, error);
}