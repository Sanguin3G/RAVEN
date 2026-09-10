using System.Globalization;
using System.Text;

namespace Raven.Api.Features.Research.Sources;

public sealed record SourceClassificationInput(
    string? Url,
    string? Title,
    string? Snippet,
    string? OfficialHost = null);

/// <summary>
/// The result of deterministic source classification. Recommendation reasons are
/// deliberately written for a human reviewer; internal scoring does not cross this
/// boundary.
/// </summary>
public sealed record SourceClassificationResult(
    SourceKind SourceKind,
    IReadOnlyList<string> RecommendationReasons,
    string? Domain)
{
    public SourceKind Kind => SourceKind;
}

public interface ISourceClassifier
{
    SourceClassificationResult Classify(SourceClassificationInput input);
}

/// <summary>
/// Classifies search candidates using stable host/path signals only. It never
/// performs network requests and should therefore be safe to use before source
/// selection or acquisition.
/// </summary>
public sealed class SourceClassifier : ISourceClassifier
{
    private static readonly HashSet<string> NewsHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "vnexpress.net",
        "dantri.com.vn",
        "tuoitre.vn",
        "thanhnien.vn",
        "cafef.vn",
        "nhipcaudautu.vn",
        "reuters.com",
        "bloomberg.com",
        "techcrunch.com",
        "forbes.com",
        "theguardian.com",
        "nytimes.com"
    };

    private static readonly string[] RegistryHostSignals =
    [
        "masothue",
        "dangkykinhdoanh",
        "businessregistration",
        "business-registry",
        "companyregistry",
        "company-register",
        "thongtincongty",
        "gdt.gov.vn"
    ];

    private static readonly string[] RegistryTextSignals =
    [
        "ma so thue",
        "tax identification",
        "tax id",
        "business registration",
        "company registration",
        "registered address",
        "dang ky kinh doanh",
        "doanh nghiep"
    ];

    private static readonly string[] NewsTextSignals =
    [
        "press release",
        "news article",
        "tin tuc",
        "bao chi"
    ];

    private static readonly string[] DocumentTextSignals =
    [
        "annual report",
        "company profile",
        "investor presentation",
        "investor relations",
        "corporate brochure",
        "esg report",
        "fact sheet",
        "bao cao thuong nien",
        "ho so cong ty"
    ];

    private static readonly string[] DocumentPathSignals =
    [
        "/report",
        "/reports",
        "/investor",
        "/investors",
        "/download",
        "/downloads",
        "/document",
        "/documents",
        "/publication",
        "/publications",
        "/resources"
    ];

    public SourceClassificationResult Classify(SourceClassificationInput input)
    {
        var domain = TryGetHost(input.Url);
        var officialHost = TryGetHost(input.OfficialHost);
        var text = NormalizeForMatching(string.Join(' ', input.Title, input.Snippet, input.Url));
        var isOfficial = domain is not null && officialHost is not null && IsSameOrChildDomain(domain, officialHost);

        if (domain is not null && IsTopCvDomain(domain))
        {
            return Result(SourceKind.TopCv, domain,
                "TopCV company pages can provide practical workforce and company details.");
        }

        if (domain is not null && IsLinkedInDomain(domain))
        {
            return Result(SourceKind.LinkedIn, domain,
                "LinkedIn is a useful supporting source for company identity and public presence.");
        }

        if ((domain is not null && HasRegistryHostSignal(domain)) || HasAnySignal(text, RegistryTextSignals))
        {
            return Result(SourceKind.BusinessRegistry, domain,
                "Business-registry sources are especially useful for legal identity and registered address.");
        }

        if (isOfficial && IsOfficialDocument(input.Url, input.Title, input.Snippet))
        {
            return Result(SourceKind.OfficialDocument, domain,
                "This document is hosted on the identified official domain and may contain authoritative company facts.");
        }

        if (isOfficial)
        {
            return Result(SourceKind.OfficialWebsite, domain,
                "This page is on the identified official company domain.");
        }

        if ((domain is not null && NewsHosts.Any(host => IsSameOrChildDomain(domain, host))) ||
            HasAnySignal(text, NewsTextSignals) ||
            HasNewsPath(input.Url))
        {
            return Result(SourceKind.News, domain,
                "News can provide useful supporting context, but claims should be checked against stronger sources where available.");
        }

        if (domain is not null)
        {
            return Result(SourceKind.ExternalWebsite, domain,
                "An external website may provide supporting company context for review.");
        }

        return Result(SourceKind.SearchResult, null,
            "This result contains discovery metadata only; acquire the underlying page before treating it as evidence.");
    }

    private static SourceClassificationResult Result(
        SourceKind kind,
        string? domain,
        string reason) => new(kind, [reason], domain);

    private static bool IsOfficialDocument(string? url, string? title, string? snippet)
    {
        var normalizedUrl = NormalizeForMatching(url);
        var path = TryGetPath(url);

        return IsDocumentExtension(path) ||
               DocumentPathSignals.Any(normalizedUrl.Contains) ||
               HasAnySignal(NormalizeForMatching(string.Join(' ', title, snippet)), DocumentTextSignals);
    }

    private static bool IsDocumentExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".doc", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".docx", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".xls", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasNewsPath(string? url)
    {
        var path = TryGetPath(url);
        return path is not null && (path.Contains("/news/", StringComparison.OrdinalIgnoreCase) ||
                                    path.Contains("/article/", StringComparison.OrdinalIgnoreCase) ||
                                    path.Contains("/press/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTopCvDomain(string domain) =>
        IsSameOrChildDomain(domain, "topcv.vn") || IsSameOrChildDomain(domain, "topcv.com");

    private static bool IsLinkedInDomain(string domain) =>
        IsSameOrChildDomain(domain, "linkedin.com") || IsSameOrChildDomain(domain, "lnkd.in");

    private static bool HasRegistryHostSignal(string domain) =>
        RegistryHostSignals.Any(signal => domain.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static bool HasAnySignal(string text, IEnumerable<string> signals) =>
        signals.Any(text.Contains);

    private static bool IsSameOrChildDomain(string domain, string expectedHost) =>
        domain.Equals(expectedHost, StringComparison.OrdinalIgnoreCase) ||
        domain.EndsWith('.' + expectedHost, StringComparison.OrdinalIgnoreCase);

    private static string? TryGetHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = "https://" + candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        var host = uri.Host.TrimEnd('.').ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    private static string? TryGetPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = "https://" + candidate;
        }

        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ? uri.AbsolutePath : null;
    }

    private static string NormalizeForMatching(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}
