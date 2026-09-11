namespace Raven.Api.Features.Research.Intelligence;

/// <summary>
/// The deterministic signals that explain why an identity should be sent to
/// the grounding model. These are deliberately kept separate from the model's
/// semantic decision so the workflow remains predictable and auditable.
/// </summary>
public sealed record IdentityAmbiguityAnalysis(
    bool ShouldResolve,
    IReadOnlyList<string> Signals)
{
    public bool IsAmbiguous => ShouldResolve;
}

/// <summary>
/// Detects identity situations where a short, underspecified, or conflicting
/// company search would benefit from a model-assisted resolution step.
/// </summary>
public sealed class IdentityAmbiguityAnalyzer
{
    private static readonly HashSet<string> ShortGeneralNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "fpt",
        "vin",
        "sun",
        "meta",
        "delta",
        "shell",
        "orange",
        "apple"
    };

    private static readonly string[] ParentSignals =
    [
        "parent",
        "group",
        "holding",
        "corporation"
    ];

    private static readonly string[] RelatedEntitySignals =
    [
        "subsidiary",
        "affiliate",
        "division",
        "business unit",
        "brand",
        "americas",
        "europe"
    ];

    public IdentityAmbiguityAnalysis Analyze(
        ResearchIdentityInput identity,
        IReadOnlyList<GroundingSourceCandidate> candidates) =>
        Analyze(new IdentityResolutionRequest(identity, candidates));

    public IdentityAmbiguityAnalysis Analyze(IdentityResolutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var signals = new List<string>();
        var identity = request.Identity;
        var normalizedName = Normalize(identity.Name);

        if (normalizedName.Length <= 4 || ShortGeneralNames.Contains(normalizedName))
        {
            signals.Add("company name is short or general");
        }

        if (string.IsNullOrWhiteSpace(identity.Website))
        {
            signals.Add("no website was supplied");
        }

        if (string.IsNullOrWhiteSpace(identity.RegistrationNumber))
        {
            signals.Add("no registration or tax identifier was supplied");
        }

        var distinctDomains = request.Candidates
            .Select(candidate => NormalizeDomain(candidate.Domain, candidate.Url))
            .Where(domain => domain.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (distinctDomains >= 3)
        {
            signals.Add("search results span several domains");
        }

        var officialDomains = request.Candidates
            .Where(candidate => candidate.OfficialDomain)
            .Select(candidate => NormalizeDomain(candidate.Domain, candidate.Url))
            .Where(domain => domain.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (officialDomains >= 2)
        {
            signals.Add("multiple official-looking domains were found");
        }

        if (request.Candidates.Count(candidate => candidate.SourceKind == Sources.SourceKind.LinkedIn) >= 2)
        {
            signals.Add("multiple LinkedIn company identities were found");
        }

        var candidateText = request.Candidates
            .Select(candidate => $"{candidate.Title} {candidate.Snippet}")
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        var hasParentSignal = candidateText.Any(ContainsAnyParentSignal);
        var hasRelatedEntitySignal = candidateText.Any(ContainsAnyRelatedEntitySignal);
        if (hasParentSignal && hasRelatedEntitySignal)
        {
            signals.Add("results mix a parent organization with related entities");
        }

        return new IdentityAmbiguityAnalysis(
            signals.Count > 0,
            signals);
    }

    public bool ShouldResolve(
        IdentityResolutionRequest request,
        GroundingMode mode) =>
        mode switch
        {
            GroundingMode.Always => true,
            GroundingMode.Off => false,
            _ => Analyze(request).ShouldResolve
        };

    public bool RequiresGrounding(
        IdentityResolutionRequest request,
        GroundingMode mode) => ShouldResolve(request, mode);

    private static bool ContainsAnyParentSignal(string text) =>
        ParentSignals.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAnyRelatedEntitySignal(string text) =>
        RelatedEntitySignals.Any(signal => text.Contains(signal, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string? value) =>
        string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim();

    private static string NormalizeDomain(string? domain, string? url)
    {
        var candidateDomain = Normalize(domain);
        if (candidateDomain.Length > 0)
        {
            return candidateDomain.Trim().TrimEnd('.').ToLowerInvariant();
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("http" or "https"))
        {
            return string.Empty;
        }

        return parsed.Host.TrimEnd('.').ToLowerInvariant();
    }
}
