namespace Raven.Api.Features.Research.Intelligence;

/// <summary>
/// Input for the bounded second-pass search that looks for an organization's
/// parent, subsidiaries, affiliates, and brands. The candidates are discovery
/// metadata only; this planner never calls a provider or accepts an entity as
/// verified.
/// </summary>
public sealed record CorporateFamilyDiscoveryRequest(
    ResearchIdentityInput Identity,
    IReadOnlyList<GroundingSourceCandidate> InitialCandidates,
    string? OfficialDomain = null,
    int MaximumQueries = CorporateFamilyDiscoveryPlanner.DefaultMaximumQueries);

/// <summary>
/// Deterministic ambiguity signals used to decide whether corporate-family
/// discovery is useful. Signals are intentionally generic and do not contain
/// a list of well-known company names.
/// </summary>
public sealed record CorporateFamilyAmbiguityAnalysis(
    bool ShouldPlan,
    IReadOnlyList<string> Signals)
{
    public bool IsAmbiguous => ShouldPlan;
}

/// <summary>
/// Plans a bounded second pass for underspecified or conflicting company
/// identities. It is deliberately transport-neutral: callers still send the
/// returned queries through the configured search provider and must review the
/// resulting candidates before acquisition.
/// </summary>
public sealed class CorporateFamilyDiscoveryPlanner
{
    public const int DefaultMaximumQueries = 5;
    public const int AbsoluteMaximumQueries = 5;

    private const int MaximumIdentityTextLength = 160;
    private const int MaximumCountryTextLength = 80;

    private static readonly string[] ParentSignals =
    [
        "parent",
        "group",
        "holding",
        "corporation",
        "corp",
        "tập đoàn",
        "cong ty me",
        "công ty mẹ"
    ];

    private static readonly string[] RelatedEntitySignals =
    [
        "subsidiary",
        "subsidiaries",
        "affiliate",
        "affiliates",
        "member company",
        "member companies",
        "business unit",
        "division",
        "brand",
        "brands",
        "part of",
        "thành viên",
        "cong ty con",
        "công ty con",
        "liên kết",
        "thuong hieu",
        "thương hiệu"
    ];

    /// <summary>
    /// Returns at most five family-discovery queries. An empty result means
    /// that the supplied metadata did not indicate a family ambiguity, or
    /// that the identity input was not usable.
    /// </summary>
    public IReadOnlyList<string> Plan(
        CorporateFamilyDiscoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Plan(
            request.Identity,
            request.InitialCandidates,
            request.OfficialDomain,
            request.MaximumQueries);
    }

    /// <summary>
    /// Plans family discovery directly from existing identity and discovery
    /// contracts. The optional domain may be a host (for example,
    /// <c>example.com</c>) or an HTTP(S) URL.
    /// </summary>
    public IReadOnlyList<string> Plan(
        ResearchIdentityInput identity,
        IReadOnlyList<GroundingSourceCandidate>? initialCandidates,
        string? officialDomain = null,
        int maximumQueries = DefaultMaximumQueries)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var maximum = Math.Clamp(maximumQueries, 0, AbsoluteMaximumQueries);
        if (maximum == 0 || string.IsNullOrWhiteSpace(identity.Name))
        {
            return [];
        }

        var candidates = initialCandidates ?? [];
        var analysis = Analyze(identity, candidates, officialDomain);
        if (!analysis.ShouldPlan)
        {
            return [];
        }

        var name = QuoteAndBound(identity.Name, MaximumIdentityTextLength);
        if (name is null)
        {
            return [];
        }

        var country = QuoteAndBound(identity.Country, MaximumCountryTextLength);
        var countrySuffix = country is null ? string.Empty : $" {country}";
        var domain = NormalizeDomain(officialDomain) ?? NormalizeHost(identity.Website);

        // Keep the five slots complementary. The first three official-domain
        // queries cover group structure and the final two broad queries cover
        // common relationship labels that often appear outside the group site.
        var templates = domain is null
            ? new[]
            {
                $"{name} group subsidiaries{countrySuffix}",
                $"{name} corporation member companies{countrySuffix}",
                $"{name} parent group{countrySuffix}",
                $"{name} affiliates{countrySuffix}",
                $"{name} brands{countrySuffix}"
            }
            : new[]
            {
                $"site:{domain} {name} group companies{countrySuffix}",
                $"site:{domain} {name} subsidiaries{countrySuffix}",
                $"site:{domain} {name} member companies{countrySuffix}",
                $"{name} affiliates{countrySuffix}",
                $"{name} brands{countrySuffix}"
            };

        return templates
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(maximum)
            .ToArray();
    }

    /// <summary>
    /// Explains why a second-pass family search is or is not required.
    /// </summary>
    public CorporateFamilyAmbiguityAnalysis Analyze(
        ResearchIdentityInput identity,
        IReadOnlyList<GroundingSourceCandidate>? initialCandidates,
        string? officialDomain = null)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var candidates = initialCandidates ?? [];
        if (string.IsNullOrWhiteSpace(identity.Name) || candidates.Count == 0)
        {
            return new CorporateFamilyAmbiguityAnalysis(false, []);
        }

        var signals = new List<string>();
        var normalizedName = NormalizeText(identity.Name);
        var suppliedDomain = NormalizeHost(identity.Website);
        var resolvedDomain = NormalizeDomain(officialDomain);
        var domains = candidates
            .Select(candidate => NormalizeHost(candidate.Domain) ?? NormalizeHost(candidate.Url))
            .Where(domain => domain is not null)
            .Select(domain => domain!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var officialDomains = candidates
            .Where(candidate => candidate.OfficialDomain)
            .Select(candidate => NormalizeHost(candidate.Domain) ?? NormalizeHost(candidate.Url))
            .Where(domain => domain is not null)
            .Select(domain => domain!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var candidateText = candidates
            .Select(candidate => $"{candidate.Title} {candidate.Snippet}")
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        var hasParentSignal = candidateText.Any(ContainsAnyParentSignal);
        var hasRelatedSignal = candidateText.Any(ContainsAnyRelatedEntitySignal);

        if (IsExplicitlyIdentified(identity, suppliedDomain))
        {
            return new CorporateFamilyAmbiguityAnalysis(false, []);
        }

        if (IsShortOrUnderspecified(normalizedName))
        {
            signals.Add("company name is short or general");
        }

        if (domains.Length >= 2)
        {
            signals.Add("search results span several domains");
        }

        if (officialDomains.Length >= 2 ||
            resolvedDomain is not null && domains.Any(domain => !domain.Equals(resolvedDomain, StringComparison.OrdinalIgnoreCase)))
        {
            signals.Add("multiple official-looking domains or competing domains were found");
        }

        if (hasParentSignal && hasRelatedSignal)
        {
            signals.Add("results mix a parent organization with related entities");
        }
        else if (hasParentSignal || hasRelatedSignal)
        {
            signals.Add("results contain corporate-family relationship language");
        }

        if (string.IsNullOrWhiteSpace(identity.Country) && domains.Length >= 2)
        {
            signals.Add("country was not supplied for a multi-entity search");
        }

        // A family pass is useful for a conflict even when the name itself is
        // not short. For example, two distinct organizations can share a
        // longer name while appearing on separate domains.
        var shouldPlan = signals.Count > 0;
        return new CorporateFamilyAmbiguityAnalysis(shouldPlan, signals);
    }

    /// <summary>
    /// Convenience predicate for callers that only need the decision.
    /// </summary>
    public bool ShouldPlan(
        ResearchIdentityInput identity,
        IReadOnlyList<GroundingSourceCandidate>? initialCandidates,
        string? officialDomain = null) =>
        Analyze(identity, initialCandidates, officialDomain).ShouldPlan;

    private static bool IsExplicitlyIdentified(
        ResearchIdentityInput identity,
        string? suppliedDomain)
    {
        // A registration/tax identifier is a direct identity hint. The
        // planner must not force a user through family selection when one is
        // already available.
        if (!string.IsNullOrWhiteSpace(identity.RegistrationNumber))
        {
            return true;
        }

        if (suppliedDomain is null)
        {
            return false;
        }

        // A user-supplied website is a specific target hint. It takes
        // precedence over competing discovery metadata; the caller can still
        // inspect those candidates as related context after the target is
        // selected. This prevents a generic family pass from asking the user
        // to clarify an entity they already identified by domain.
        return true;
    }

    private static bool IsShortOrUnderspecified(string normalizedName)
    {
        if (normalizedName.Length == 0)
        {
            return true;
        }

        var tokens = normalizedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var alphanumericLength = normalizedName.Count(char.IsLetterOrDigit);
        return alphanumericLength <= 4 || tokens.Length == 1 && alphanumericLength <= 8;
    }

    private static bool ContainsAnyParentSignal(string text) =>
        ParentSignals.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAnyRelatedEntitySignal(string text) =>
        RelatedEntitySignals.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeText(string? value) =>
        string.Join(' ', (value ?? string.Empty)
            .Replace('\0', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim();

    private static string? QuoteAndBound(string? value, int maximumLength)
    {
        var normalized = NormalizeText(value);
        if (normalized.Length == 0)
        {
            return null;
        }

        normalized = normalized.Replace('"', ' ');
        normalized = NormalizeText(normalized);
        if (normalized.Length > maximumLength)
        {
            normalized = normalized[..maximumLength].Trim();
        }

        return normalized.Length == 0 ? null : $"\"{normalized}\"";
    }

    private static string? NormalizeDomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            if (!Uri.TryCreate($"https://{candidate}", UriKind.Absolute, out uri))
            {
                return null;
            }
        }

        if (uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.UserInfo.Length > 0 ||
            uri.AbsolutePath is not ("" or "/") ||
            !string.IsNullOrWhiteSpace(uri.Query) ||
            !string.IsNullOrWhiteSpace(uri.Fragment))
        {
            return null;
        }

        var host = uri.Host.TrimEnd('.').ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    private static string? NormalizeHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
            !Uri.TryCreate($"https://{candidate}", UriKind.Absolute, out uri))
        {
            return null;
        }

        if (uri.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.UserInfo.Length > 0)
        {
            return null;
        }

        var host = uri.Host.TrimEnd('.').ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }
}
