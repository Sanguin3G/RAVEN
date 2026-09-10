namespace Raven.Api.Features.Research.Planning;

/// <summary>
/// A URL discovered while inspecting an official company website.
/// This is deliberately a transport-neutral value: discovering a link does not
/// mean that the link has been crawled or accepted as profile evidence.
/// </summary>
public sealed record OfficialSiteLink(
    string Url,
    string? Title = null,
    string? Snippet = null,
    int DiscoveryRank = 0);

/// <summary>
/// Input to <see cref="OfficialSiteDiscoveryPlanner"/>.
/// </summary>
public sealed record OfficialSiteDiscoveryRequest(
    string OfficialWebsite,
    IReadOnlyCollection<OfficialSiteLink> Links,
    int MaximumCandidates = OfficialSiteDiscoveryPlanner.DefaultMaximumCandidates);

/// <summary>
/// A bounded, ranked official-site URL recommendation. The URL is only a
/// discovery candidate; it must still be selected and acquired by the research
/// workflow before its content can be treated as evidence.
/// </summary>
public sealed record OfficialSiteCandidate(
    string Url,
    string NormalizedUrl,
    string Domain,
    string Title,
    string? Snippet,
    int Priority,
    IReadOnlyList<string> RecommendationReasons,
    bool Recommended,
    int DiscoveryRank);

/// <summary>
/// Plans a small set of useful pages from an already identified official
/// website. It does not make network requests and never recursively follows
/// links. The caller supplies links obtained from a homepage, navigation, or
/// sitemap retrieval.
/// </summary>
public sealed class OfficialSiteDiscoveryPlanner(SourceUrlNormalizer urlNormalizer)
{
    public const int DefaultMaximumCandidates = 20;
    public const int AbsoluteMaximumCandidates = 20;

    private static readonly IReadOnlyDictionary<string, PathSignal> PathSignals =
        new Dictionary<string, PathSignal>(StringComparer.OrdinalIgnoreCase)
        {
            ["about"] = new(100, "About/company page"),
            ["about-us"] = new(100, "About/company page"),
            ["company"] = new(100, "About/company page"),
            ["company-profile"] = new(100, "About/company page"),
            ["who-we-are"] = new(100, "About/company page"),
            ["products"] = new(92, "Products page"),
            ["product"] = new(92, "Products page"),
            ["services"] = new(92, "Services page"),
            ["service"] = new(92, "Services page"),
            ["solutions"] = new(92, "Solutions page"),
            ["solution"] = new(92, "Solutions page"),
            ["industries"] = new(88, "Industries page"),
            ["industry"] = new(88, "Industries page"),
            ["markets"] = new(88, "Markets page"),
            ["market"] = new(88, "Markets page"),
            ["leadership"] = new(86, "Leadership page"),
            ["management"] = new(86, "Management page"),
            ["locations"] = new(78, "Locations page"),
            ["location"] = new(78, "Locations page"),
            ["offices"] = new(78, "Office locations page"),
            ["office"] = new(78, "Office locations page"),
            ["contact"] = new(74, "Contact page"),
            ["investor"] = new(72, "Investor information page"),
            ["investors"] = new(72, "Investor information page"),
            ["investor-relations"] = new(72, "Investor information page"),
            ["reports"] = new(70, "Reports page"),
            ["report"] = new(70, "Report page"),
            ["download"] = new(66, "Company document page"),
            ["downloads"] = new(66, "Company document page"),
            ["resources"] = new(64, "Company resources page"),
            ["careers"] = new(52, "Company careers page")
        };

    private static readonly HashSet<string> RejectedPathSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "login", "log-in", "signin", "sign-in", "signup", "sign-up", "register",
        "auth", "oauth", "account", "privacy", "terms", "term", "cookie", "cookies",
        "search", "sitemap", "robots", "feed", "rss", "tag", "tags", "category",
        "categories", "author", "authors", "archive", "archives"
    };

    private static readonly HashSet<string> RejectedFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg", ".ico", ".bmp", ".tif", ".tiff",
        ".mp3", ".wav", ".mp4", ".webm", ".avi", ".mov", ".zip", ".rar", ".7z", ".exe"
    };

    private static readonly HashSet<string> DocumentFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp"
    };

    private static readonly HashSet<string> PaginationQueryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "page", "p", "pageNo", "pageNumber", "offset", "start", "from", "skip", "limit",
        "sort", "order", "filter", "query", "q", "search", "tag", "category"
    };

    /// <summary>
    /// Returns at most <see cref="AbsoluteMaximumCandidates"/> candidates,
    /// ordered by usefulness and then by the source discovery order.
    /// </summary>
    public IReadOnlyList<OfficialSiteCandidate> Plan(OfficialSiteDiscoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetOfficialSite(request.OfficialWebsite, out var officialSite))
        {
            return [];
        }

        var maximumCandidates = Math.Clamp(request.MaximumCandidates, 1, AbsoluteMaximumCandidates);
        var candidates = new List<ScoredCandidate>();

        foreach (var link in request.Links ?? [])
        {
            if (!TryPrepareLink(link, officialSite, out var prepared))
            {
                continue;
            }

            var scored = Score(prepared);
            if (scored is not null)
            {
                candidates.Add(scored);
            }
        }

        return candidates
            .GroupBy(candidate => candidate.Candidate.NormalizedUrl, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Candidate.DiscoveryRank)
                .First())
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Candidate.DiscoveryRank)
            .ThenBy(candidate => candidate.Candidate.NormalizedUrl, StringComparer.Ordinal)
            .Take(maximumCandidates)
            .Select(candidate => candidate.Candidate)
            .ToArray();
    }

    private bool TryPrepareLink(
        OfficialSiteLink link,
        OfficialSiteOrigin officialSite,
        out PreparedLink prepared)
    {
        prepared = default;
        if (link is null || string.IsNullOrWhiteSpace(link.Url))
        {
            return false;
        }

        var normalizedUrl = urlNormalizer.Normalize(link.Url);
        if (normalizedUrl is null || !Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri) ||
            !IsOfficialOrigin(uri, officialSite))
        {
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .Select(segment => segment.Trim().ToLowerInvariant())
            .Where(segment => segment.Length > 0)
            .ToArray();

        if (segments.Any(RejectedPathSegments.Contains) || IsNewsArchive(segments) || HasPaginationQuery(uri))
        {
            return false;
        }

        var extension = Path.GetExtension(uri.AbsolutePath);
        if (RejectedFileExtensions.Contains(extension))
        {
            return false;
        }

        prepared = new PreparedLink(
            uri,
            normalizedUrl,
            segments,
            extension,
            link.Title?.Trim() ?? string.Empty,
            link.Snippet?.Trim(),
            link.DiscoveryRank);
        return true;
    }

    private static ScoredCandidate? Score(PreparedLink link)
    {
        var reasons = new List<string>();
        var score = link.Segments.Count == 0 ? 45 : 20;

        if (link.Segments.Count == 0)
        {
            reasons.Add("Official homepage");
        }

        foreach (var segment in link.Segments)
        {
            if (PathSignals.TryGetValue(segment, out var signal))
            {
                score = Math.Max(score, signal.Score);
                if (!reasons.Contains(signal.Reason, StringComparer.Ordinal))
                {
                    reasons.Add(signal.Reason);
                }
            }
        }

        if (DocumentFileExtensions.Contains(link.Extension))
        {
            score = Math.Max(score, 60);
            reasons.Add("Official document");

            if (ContainsDocumentSignal(link))
            {
                // A clearly named annual report, company profile, or similar
                // document is often more useful than a generic navigation
                // page, while still remaining only a recommendation until it
                // is acquired and reviewed.
                score = Math.Max(score, 104);
                reasons.Add("Useful company report/profile document");
            }
        }

        if (link.Segments.Any(segment => segment.Equals("news", StringComparison.OrdinalIgnoreCase)))
        {
            // Individual news items can be useful context, but they should not
            // displace identity and company-information pages.
            score = Math.Min(score, 48);
            reasons.Add("Supporting company news");
        }

        if (link.Segments.Count > 0 && score == 20)
        {
            reasons.Add("Official-domain page");
        }

        var title = string.IsNullOrWhiteSpace(link.Title) ? BuildTitle(link.Uri, link.Segments) : link.Title;
        return new ScoredCandidate(
            new OfficialSiteCandidate(
                link.Uri.AbsoluteUri,
                link.NormalizedUrl,
                link.Uri.Host,
                title,
                link.Snippet,
                score,
                reasons.Distinct(StringComparer.Ordinal).ToArray(),
                score >= 45,
                link.DiscoveryRank),
            score);
    }

    private static bool TryGetOfficialSite(string website, out OfficialSiteOrigin officialSite)
    {
        officialSite = default;
        if (!Uri.TryCreate(website, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        officialSite = new OfficialSiteOrigin(
            RemoveWww(uri.Host),
            GetEffectivePort(uri));
        return true;
    }

    private static bool IsOfficialOrigin(Uri candidate, OfficialSiteOrigin officialSite) =>
        RemoveWww(candidate.Host).Equals(officialSite.Host, StringComparison.OrdinalIgnoreCase) &&
        GetEffectivePort(candidate) == officialSite.Port;

    private static string RemoveWww(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;

    private static int GetEffectivePort(Uri uri) => uri.IsDefaultPort ? -1 : uri.Port;

    private static bool IsNewsArchive(IReadOnlyList<string> segments)
    {
        var newsIndex = Array.FindIndex(segments.ToArray(), segment =>
            segment.Equals("news", StringComparison.OrdinalIgnoreCase));
        if (newsIndex < 0)
        {
            return false;
        }

        var following = segments.Skip(newsIndex + 1).ToArray();
        return following.Length == 0 ||
               following.Any(segment => segment is "page" or "tag" or "tags" or "category" or "categories" or "archive" or "archives") ||
               following.All(segment => segment.All(char.IsDigit) || segment.Length == 4 && segment.All(char.IsDigit));
    }

    private static bool HasPaginationQuery(Uri uri)
    {
        if (string.IsNullOrWhiteSpace(uri.Query))
        {
            return false;
        }

        var queryNames = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)[0])
            .Select(Uri.UnescapeDataString)
            .ToArray();

        return queryNames.Length > 2 || queryNames.Any(PaginationQueryNames.Contains);
    }

    private static bool ContainsDocumentSignal(PreparedLink link)
    {
        var searchable = $"{link.Uri.AbsolutePath} {link.Title} {link.Snippet}";
        return new[] { "annual", "company-profile", "profile", "brochure", "investor", "esg", "sustainability", "fact-sheet", "report" }
            .Any(signal => searchable.Contains(signal, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildTitle(Uri uri, IReadOnlyList<string> segments) =>
        segments.Count == 0
            ? uri.Host
            : string.Join(" ", segments[^1].Split('-', '_', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]));

    private readonly record struct OfficialSiteOrigin(string Host, int Port);

    private readonly record struct PreparedLink(
        Uri Uri,
        string NormalizedUrl,
        IReadOnlyList<string> Segments,
        string Extension,
        string Title,
        string? Snippet,
        int DiscoveryRank);

    private sealed record ScoredCandidate(OfficialSiteCandidate Candidate, int Score);

    private readonly record struct PathSignal(int Score, string Reason);
}
