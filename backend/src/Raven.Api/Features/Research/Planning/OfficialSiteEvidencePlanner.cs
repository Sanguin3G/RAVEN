using Raven.Api.Features.Research.Coverage;

namespace Raven.Api.Features.Research.Planning;

/// <summary>
/// Input to <see cref="OfficialSiteEvidencePlanner"/>. The links are expected
/// to have come from an already approved official root (for example, a
/// homepage, navigation document, or sitemap). Planning is deliberately
/// side-effect free: it does not fetch links or follow them recursively.
/// </summary>
public sealed record OfficialSiteEvidenceRequest(
    string OfficialWebsite,
    IReadOnlyCollection<OfficialSiteLink> Links,
    IReadOnlyCollection<ResearchTarget>? Targets = null,
    int MaximumPages = OfficialSiteEvidencePlanner.DefaultMaximumPages);

/// <summary>
/// A bounded recommendation for a page on an approved official domain.
/// <see cref="MatchedTargets"/> explains which evidence gaps the page may
/// help address; it is not a claim that the page contains those facts.
/// </summary>
public sealed record OfficialSiteEvidenceCandidate(
    string Url,
    string NormalizedUrl,
    string Domain,
    string Title,
    string? Snippet,
    int Priority,
    IReadOnlyList<ResearchTarget> MatchedTargets,
    IReadOnlyList<string> RecommendationReasons,
    int DiscoveryRank);

/// <summary>
/// Ranks pages discovered below an already selected official company root.
/// This planner is intentionally separate from <see cref="OfficialSiteDiscoveryPlanner"/>
/// so existing discovery behavior remains stable while target-aware
/// enrichment can adopt a tighter, field-specific policy.
/// </summary>
public sealed class OfficialSiteEvidencePlanner(SourceUrlNormalizer urlNormalizer)
{
    public const int DefaultMaximumPages = 12;
    public const int AbsoluteMaximumPages = 12;

    private static readonly IReadOnlyDictionary<ResearchTarget, TargetSignal[]> TargetSignals =
        new Dictionary<ResearchTarget, TargetSignal[]>
        {
            [ResearchTarget.LegalIdentity] =
            [
                new("legal", 35, "Legal/company information page"),
                new("company", 30, "Company information page"),
                new("corporate", 30, "Corporate information page"),
                new("imprint", 28, "Legal imprint page"),
                new("about", 24, "About/company page")
            ],
            [ResearchTarget.TaxRegistration] =
            [
                new("tax", 36, "Tax information page"),
                new("registration", 34, "Registration information page"),
                new("legal", 25, "Legal/company information page"),
                new("company", 22, "Company information page")
            ],
            [ResearchTarget.FoundedHistory] =
            [
                new("history", 58, "Company history page"),
                new("founded", 56, "Company founding information"),
                new("about", 42, "About/company page"),
                new("about-us", 42, "About/company page"),
                new("who-we-are", 42, "About/company page"),
                new("company-profile", 40, "Company profile page"),
                new("company", 36, "Company information page")
            ],
            [ResearchTarget.Industry] =
            [
                new("industry", 54, "Industry page"),
                new("industries", 54, "Industries page"),
                new("about", 40, "About/company page"),
                new("about-us", 40, "About/company page"),
                new("company", 36, "Company information page")
            ],
            [ResearchTarget.EmployeeScale] =
            [
                new("careers", 44, "Careers/workforce page"),
                new("about", 38, "About/company page"),
                new("about-us", 38, "About/company page"),
                new("company", 34, "Company information page"),
                new("team", 32, "Team page")
            ],
            [ResearchTarget.ProductsServices] =
            [
                new("products", 78, "Products page"),
                new("product", 78, "Product page"),
                new("services", 78, "Services page"),
                new("service", 78, "Service page"),
                new("solutions", 76, "Solutions page"),
                new("solution", 76, "Solution page"),
                new("platform", 70, "Platform page"),
                new("technology", 64, "Technology page"),
                new("products-services", 76, "Products and services page")
            ],
            [ResearchTarget.Markets] =
            [
                new("markets", 78, "Markets page"),
                new("market", 78, "Market page"),
                new("customers", 74, "Customers page"),
                new("customer", 74, "Customer page"),
                new("industries", 70, "Industries page"),
                new("global", 68, "Global presence page"),
                new("countries", 66, "Countries/markets page"),
                new("partners", 62, "Partners page")
            ],
            [ResearchTarget.Leadership] =
            [
                new("leadership", 82, "Leadership page"),
                new("management", 82, "Management page"),
                new("executive", 78, "Executive page"),
                new("board", 74, "Board page"),
                new("team", 68, "Team page"),
                new("about", 54, "About/company page"),
                new("about-us", 54, "About/company page"),
                new("executive-team", 78, "Executive page"),
                new("board-of-directors", 74, "Board page")
            ],
            [ResearchTarget.Locations] =
            [
                new("locations", 80, "Locations page"),
                new("location", 80, "Location page"),
                new("offices", 78, "Office locations page"),
                new("office", 78, "Office locations page"),
                new("contact", 68, "Contact page")
            ]
        };

    private static readonly HashSet<string> RejectedPathSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "login", "log-in", "signin", "sign-in", "signup", "sign-up", "register",
        "auth", "oauth", "account", "privacy", "terms", "term", "cookie", "cookies",
        "search", "sitemap", "robots", "feed", "rss", "tag", "tags", "category",
        "categories", "author", "authors", "archive", "archives", "wp-admin"
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
    /// Returns at most <see cref="AbsoluteMaximumPages"/> same-domain pages,
    /// ordered by target relevance and then discovery order. Duplicate
    /// normalized URLs are collapsed before the page budget is applied.
    /// </summary>
    public IReadOnlyList<OfficialSiteEvidenceCandidate> Plan(OfficialSiteEvidenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryGetOfficialSite(request.OfficialWebsite, out var officialSite))
        {
            return [];
        }

        var maximumPages = Math.Clamp(request.MaximumPages, 1, AbsoluteMaximumPages);
        var targets = NormalizeTargets(request.Targets);
        var candidates = new List<ScoredCandidate>();

        foreach (var link in request.Links ?? [])
        {
            if (!TryPrepareLink(link, officialSite, out var prepared))
            {
                continue;
            }

            var scored = Score(prepared, targets);
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
                .ThenBy(candidate => candidate.Candidate.NormalizedUrl, StringComparer.Ordinal)
                .First())
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Candidate.DiscoveryRank)
            .ThenBy(candidate => candidate.Candidate.NormalizedUrl, StringComparer.Ordinal)
            .Take(maximumPages)
            .Select(candidate => candidate.Candidate)
            .ToArray();
    }

    private static IReadOnlyList<ResearchTarget> NormalizeTargets(IReadOnlyCollection<ResearchTarget>? targets) =>
        (targets ?? []).Distinct().ToArray();

    private bool TryPrepareLink(
        OfficialSiteLink? link,
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

    private static ScoredCandidate? Score(
        PreparedLink link,
        IReadOnlyList<ResearchTarget> targets)
    {
        var reasons = new List<string>();
        var matchedTargets = new List<ResearchTarget>();
        var score = link.Segments.Count == 0 ? 42 : 12;

        if (link.Segments.Count == 0)
        {
            reasons.Add("Official homepage");
        }

        foreach (var target in targets)
        {
            if (!TargetSignals.TryGetValue(target, out var signals))
            {
                continue;
            }

            var matched = signals
                .Where(signal => link.Segments.Any(segment =>
                                     segment.Equals(signal.Segment, StringComparison.OrdinalIgnoreCase) ||
                                     segment.Contains(signal.Segment, StringComparison.OrdinalIgnoreCase)) ||
                                 ContainsTextSignal(link, signal.Segment))
                .OrderByDescending(signal => signal.Score)
                .FirstOrDefault();
            if (matched is null)
            {
                continue;
            }

            matchedTargets.Add(target);
            score = Math.Max(score, matched.Score);
            reasons.Add(matched.Reason);
        }

        if (DocumentFileExtensions.Contains(link.Extension))
        {
            score = Math.Max(score, 55);
            reasons.Add("Official document");
            if (ContainsDocumentSignal(link))
            {
                score = Math.Max(score, 92);
                reasons.Add("Useful company report/profile document");
            }
        }

        // A page that was found under an approved root remains useful as a
        // bounded fallback even when no target-specific path signal matched.
        if (link.Segments.Count > 0 && score == 12)
        {
            reasons.Add("Official-domain page");
        }

        if (link.Segments.Any(segment => segment.Equals("news", StringComparison.OrdinalIgnoreCase)))
        {
            // Individual news articles can corroborate leadership or markets,
            // but they should not displace company-information pages.
            score = Math.Min(score, 48);
            reasons.Add("Supporting company news");
        }

        var title = string.IsNullOrWhiteSpace(link.Title) ? BuildTitle(link.Uri, link.Segments) : link.Title;
        return new ScoredCandidate(
            new OfficialSiteEvidenceCandidate(
                link.Uri.AbsoluteUri,
                link.NormalizedUrl,
                link.Uri.Host,
                title,
                link.Snippet,
                score,
                matchedTargets.Distinct().ToArray(),
                reasons.Distinct(StringComparer.Ordinal).ToArray(),
                link.DiscoveryRank),
            score);
    }

    private static bool ContainsTextSignal(PreparedLink link, string signal) =>
        $"{link.Title} {link.Snippet}".Contains(signal, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetOfficialSite(string website, out OfficialSiteOrigin officialSite)
    {
        officialSite = default;
        if (!Uri.TryCreate(website, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        officialSite = new OfficialSiteOrigin(RemoveWww(uri.Host), GetEffectivePort(uri));
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
               following.All(segment => segment.Length == 4 && segment.All(char.IsDigit));
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

    private sealed record ScoredCandidate(OfficialSiteEvidenceCandidate Candidate, int Score);

    private sealed record TargetSignal(string Segment, int Score, string Reason);
}
