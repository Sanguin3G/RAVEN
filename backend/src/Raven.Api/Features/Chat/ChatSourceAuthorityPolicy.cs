namespace Raven.Api.Features.Chat;

public enum ChatSourceTier { Primary, Independent, Reference, Community, Unknown }

public sealed record ChatSourceAuthorityScore(ChatSourceTier Tier, int Score, string Reason);

/// <summary>Chat-only URL authority hints. These ranks help select pages; they do not verify claims.</summary>
public sealed class ChatSourceAuthorityPolicy
{
    private static readonly string[] ThirdPartyDomains =
        ["wikipedia.org", "fandom.com", "crunchbase.com", "reddit.com", "linkedin.com",
         "reuters.com", "apnews.com", "bloomberg.com", "ft.com", "forbes.com"];
    private static readonly string[] IndependentDomains =
        ["reuters.com", "apnews.com", "bloomberg.com", "ft.com", "forbes.com"];

    public string? OfficialHost(string? acceptedProfileWebsite, string? companyWebsite)
    {
        foreach (var website in new[] { acceptedProfileWebsite, companyWebsite })
        {
            if (!Uri.TryCreate(website, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                continue;
            var host = uri.Host.TrimEnd('.').ToLowerInvariant();
            if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
            if (ThirdPartyDomains.Any(domain => Matches(host, domain)) || IsGovernment(host)) continue;
            return host;
        }
        return null;
    }

    public ChatSourceAuthorityScore Evaluate(string host, string? officialHost, string? preference)
    {
        host = host.TrimEnd('.').ToLowerInvariant();
        var preferredIndependent = preference == "independent_or_regulatory";
        if (officialHost is not null && Matches(host, officialHost))
            return new(ChatSourceTier.Primary, preference == "company_primary" ? 38 : 30, "official company domain");
        if (IsGovernment(host))
            return new(ChatSourceTier.Primary, preferredIndependent ? 42 : 30, "government or regulator");
        if (IndependentDomains.Any(domain => Matches(host, domain)))
            return new(ChatSourceTier.Independent, preferredIndependent ? 32 : 20, "independent publication");
        if (Matches(host, "wikipedia.org") || Matches(host, "crunchbase.com"))
            return new(ChatSourceTier.Reference, 8, "reference source");
        if (Matches(host, "fandom.com") || Matches(host, "reddit.com"))
            return new(ChatSourceTier.Community, -10, "community source");
        return new(ChatSourceTier.Unknown, 5, "unclassified source");
    }

    private static bool Matches(string host, string domain) =>
        host.Equals(domain, StringComparison.Ordinal) || host.EndsWith($".{domain}", StringComparison.Ordinal);

    private static bool IsGovernment(string host) =>
        host.EndsWith(".gov", StringComparison.Ordinal) || host.EndsWith(".gov.vn", StringComparison.Ordinal) ||
        host.EndsWith(".gov.uk", StringComparison.Ordinal);
}
