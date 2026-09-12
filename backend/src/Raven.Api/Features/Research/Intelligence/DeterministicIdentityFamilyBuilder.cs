using System.Text;
using Raven.Api.Features.Research.Sources;

namespace Raven.Api.Features.Research.Intelligence;

/// <summary>
/// Builds cautious identity choices from bounded search metadata when the
/// grounding model cannot provide a complete family view. This is not an
/// assertion that an entity is correct: every result is marked for review and
/// carries only facts visible in the candidate title, URL, and snippet.
/// </summary>
public sealed class DeterministicIdentityFamilyBuilder
{
    public const int DefaultMaximumEntities = 8;

    private static readonly string[] ParentSignals =
    [
        "parent",
        "group",
        "holding",
        "corporation",
        "corp"
    ];

    private static readonly string[] RelatedSignals =
    [
        "subsidiary",
        "subsidiaries",
        "affiliate",
        "affiliates",
        "member compan",
        "business unit",
        "division",
        "brand",
        "brands"
    ];

    private static readonly HashSet<string> GenericTitleSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "about",
        "about us",
        "home",
        "history",
        "contact",
        "company",
        "company profile",
        "official website",
        "member companies",
        "our leaders",
        "leadership",
        "careers",
        "jobs",
        "services",
        "solutions",
        "products",
        "linkedin",
        "facebook",
        "wikipedia"
    };

    public IReadOnlyList<ResolvedResearchEntity> Build(
        ResearchIdentityInput identity,
        IReadOnlyList<GroundingSourceCandidate>? candidates,
        int maximumEntities = DefaultMaximumEntities)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var maximum = Math.Clamp(maximumEntities, 2, DefaultMaximumEntities);
        var inputName = Normalize(identity.Name);
        var inputTokens = inputName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 2)
            .ToArray();

        if (inputName.Length == 0 || candidates is null || candidates.Count == 0)
        {
            return [];
        }

        var projections = candidates
            .Select(candidate => Project(candidate, inputTokens))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .GroupBy(candidate => candidate.Domain ?? candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(candidate => candidate.Official)
                .ThenByDescending(candidate => candidate.NameScore)
                .ThenBy(candidate => candidate.SearchRank)
                .First())
            .Where(candidate => candidate.IsPlausible)
            .OrderByDescending(candidate => candidate.ParentSignal)
            .ThenByDescending(candidate => candidate.ExactNameMatch)
            .ThenByDescending(candidate => candidate.Official)
            .ThenBy(candidate => candidate.SearchRank)
            .Take(maximum)
            .ToArray();

        if (projections.Length < 2)
        {
            return [];
        }

        var recommendedIndex = Array.FindIndex(projections, candidate => candidate.ParentSignal || candidate.ExactNameMatch);
        if (recommendedIndex < 0)
        {
            recommendedIndex = Array.FindIndex(projections, candidate => candidate.Official);
        }

        return projections.Select((candidate, index) =>
        {
            var entityType = candidate.ParentSignal
                ? GroundedEntityType.ParentGroup
                : candidate.RelatedSignal || !candidate.ExactNameMatch && candidate.NameScore > 0
                    ? GroundedEntityType.Subsidiary
                    : GroundedEntityType.Company;
            var displayName = candidate.DisplayName;
            var relationship = entityType switch
            {
                GroundedEntityType.ParentGroup => "Likely parent or group organization from discovery metadata.",
                GroundedEntityType.Subsidiary => "Likely related subsidiary or affiliate from discovery metadata.",
                _ => "Distinct company candidate from discovery metadata."
            };

            return new ResolvedResearchEntity(
                BuildTemporaryId(displayName, candidate.Domain, index),
                displayName,
                null,
                identity.Country,
                BuildWebsite(candidate.Domain),
                candidate.Official ? candidate.Domain : null,
                entityType,
                relationship,
                candidate.Official ? GroundingConfidence.Medium : GroundingConfidence.Low,
                "Deterministic fallback candidate from bounded public search metadata; verify before continuing.",
                [candidate.CandidateId],
                index == recommendedIndex);
        }).ToArray();
    }

    private static CandidateProjection? Project(
        GroundingSourceCandidate candidate,
        IReadOnlyList<string> inputTokens)
    {
        var domain = NormalizeDomain(candidate.Domain) ?? NormalizeDomain(candidate.Url);
        var text = Normalize($"{candidate.Title} {candidate.Snippet} {string.Join(' ', candidate.RecommendationReasons ?? [])}");
        var displayName = ExtractDisplayName(candidate.Title, inputTokens);
        if (displayName is null || domain is null)
        {
            return null;
        }

        var normalizedDisplay = Normalize(displayName);
        var exactNameMatch = inputTokens.Count > 0 &&
                             normalizedDisplay.Equals(string.Join(' ', inputTokens), StringComparison.OrdinalIgnoreCase);
        var parentSignal = ContainsAny(text, ParentSignals) || ContainsAny(normalizedDisplay, ParentSignals);
        var relatedSignal = ContainsAny(text, RelatedSignals);
        var nameScore = inputTokens.Count(token => normalizedDisplay.Contains(token, StringComparison.OrdinalIgnoreCase));
        var official = candidate.OfficialDomain ||
                       candidate.SourceKind is SourceKind.OfficialWebsite or SourceKind.OfficialDocument;
        var hasIdentitySignal = nameScore > 0 || parentSignal || relatedSignal;
        var isPlausible = hasIdentitySignal && (official || nameScore > 0);

        return new CandidateProjection(
            candidate.CandidateId,
            displayName,
            domain,
            candidate.SearchRank,
            official,
            exactNameMatch,
            parentSignal,
            relatedSignal,
            nameScore,
            isPlausible);
    }

    private static string? ExtractDisplayName(string? title, IReadOnlyList<string> inputTokens)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var segments = title
            .Split(['|', '—', '–', '·', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Normalize)
            .Where(segment => segment.Length >= 3)
            .Where(segment => !GenericTitleSegments.Contains(segment))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (segments.Length == 0)
        {
            return null;
        }

        return segments
            .OrderByDescending(segment => inputTokens.Count(token => segment.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .ThenByDescending(segment => ContainsAny(segment, ParentSignals) || ContainsAny(segment, RelatedSignals))
            .ThenByDescending(segment => segment.Length)
            .First();
    }

    private static bool ContainsAny(string value, IEnumerable<string> signals)
    {
        var normalized = Normalize(value).ToLowerInvariant();
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return signals.Any(signal => signal.Contains(' ', StringComparison.Ordinal)
            ? normalized.Contains(signal, StringComparison.OrdinalIgnoreCase)
            : tokens.Any(token => token.Equals(signal, StringComparison.OrdinalIgnoreCase)));
    }

    private static string? NormalizeDomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var candidate = value.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
            !Uri.TryCreate($"https://{candidate}", UriKind.Absolute, out uri)) return null;
        if (uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host)) return null;
        var host = uri.Host.TrimEnd('.').ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    private static string? BuildWebsite(string? domain) =>
        string.IsNullOrWhiteSpace(domain) ? null : $"https://{domain}";

    private static string BuildTemporaryId(string displayName, string? domain, int index)
    {
        var builder = new StringBuilder();
        foreach (var character in $"{displayName}-{domain ?? "candidate"}-{index}")
        {
            if (char.IsLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }

        return builder.ToString().Trim('-');
    }

    private static string Normalize(string? value) =>
        string.Join(' ', (value ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Trim();

    private sealed record CandidateProjection(
        Guid CandidateId,
        string DisplayName,
        string Domain,
        int SearchRank,
        bool Official,
        bool ExactNameMatch,
        bool ParentSignal,
        bool RelatedSignal,
        int NameScore,
        bool IsPlausible);
}
