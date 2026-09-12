using System.Text.RegularExpressions;

namespace Raven.Api.Features.Profiles;

/// <summary>
/// The server-owned identity context used to validate evidence references. Both
/// sets are required so an ID cannot be smuggled in from another company or run.
/// </summary>
public sealed record ProfileValidationContext(
    Guid CompanyId,
    Guid ResearchRunId,
    IReadOnlySet<Guid> CompanySourceDocumentIds,
    IReadOnlySet<Guid> ResearchRunSourceDocumentIds,
    bool AllowCompanyOwnedSources = false);

public sealed record ProfileValidationResult(
    CompanyProfileCandidate Candidate,
    IReadOnlyList<string> Warnings,
    bool IsValid)
{
    public bool HasWarnings => Warnings.Count > 0;
}

/// <summary>
/// Deterministically validates model-provided evidence before it can be shown as
/// provenance or persisted. Invalid references and field paths are dropped with
/// warnings; a candidate belonging to another company/run is rejected.
/// </summary>
public static partial class CompanyProfileValidator
{
    private static readonly IReadOnlyDictionary<string, string> ScalarAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["displayName"] = "displayName",
            ["identity.displayName"] = "displayName",
            ["legalName"] = "legalName",
            ["identity.legalName"] = "legalName",
            ["website"] = "website",
            ["identity.website"] = "website",
            ["country"] = "country",
            ["identity.country"] = "country",
            ["headquarters"] = "headquarters",
            ["identity.headquarters"] = "headquarters",
            ["registrationNumberOrTaxId"] = "registrationNumberOrTaxId",
            ["registrationNumber"] = "registrationNumberOrTaxId",
            ["identity.registrationNumberOrTaxId"] = "registrationNumberOrTaxId",
            ["identity.registrationNumber"] = "registrationNumberOrTaxId",
            ["foundedYear"] = "foundedYear",
            ["identity.foundedYear"] = "foundedYear",
            ["primaryIndustry"] = "primaryIndustry",
            ["classification.primaryIndustry"] = "primaryIndustry",
            ["companySize"] = "companySize",
            ["classification.companySize"] = "companySize",
            ["employeeCount"] = "employeeCount",
            ["classification.employeeCount"] = "employeeCount",
            ["employeeCountRange"] = "employeeCountRange",
            ["classification.employeeCountRange"] = "employeeCountRange",
            ["summary"] = "summary",
            ["description.summary"] = "summary"
        };

    private static readonly IReadOnlyDictionary<string, string> CollectionAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["secondaryIndustries"] = "secondaryIndustries",
            ["classification.secondaryIndustries"] = "secondaryIndustries",
            ["productsServices"] = "productsServices",
            ["description.productsServices"] = "productsServices",
            ["markets"] = "markets",
            ["description.markets"] = "markets",
            ["leadership"] = "leadership",
            ["leadership.people"] = "leadership",
            ["locations"] = "locations",
            ["operatingLocations"] = "locations",
            ["publicLinks"] = "publicLinks"
        };

    [GeneratedRegex("^(?<root>secondaryIndustries|productsServices|markets|leadership|locations|publicLinks)(?:\\[(?<index>[0-9]+)\\])?(?:\\.(?<property>[A-Za-z][A-Za-z0-9]*))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CollectionPathPattern();

    public static ProfileValidationResult Validate(
        CompanyProfileCandidate candidate,
        ProfileValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(context);

        var warnings = new List<string>();
        var sanitized = candidate.Clone();

        if (candidate.CompanyId != context.CompanyId)
        {
            warnings.Add("Profile candidate company does not match the research company.");
        }

        if (candidate.ResearchRunId != context.ResearchRunId)
        {
            warnings.Add("Profile candidate research run does not match the current run.");
        }

        var evidenceIsAllowed = candidate.CompanyId == context.CompanyId && candidate.ResearchRunId == context.ResearchRunId;
        sanitized.Evidence.Clear();

        var evidenceByFieldPath = new Dictionary<string, ProfileEvidence>(StringComparer.Ordinal);
        foreach (var evidence in candidate.Evidence)
        {
            if (!TryNormalizeFieldPath(evidence.FieldPath, out var fieldPath))
            {
                warnings.Add($"Dropped evidence with unsupported field path '{evidence.FieldPath}'.");
                continue;
            }

            var acceptedSourceIds = new List<Guid>();
            foreach (var sourceDocumentId in evidence.SourceDocumentIds)
            {
                if (!evidenceIsAllowed || !context.CompanySourceDocumentIds.Contains(sourceDocumentId))
                {
                    warnings.Add($"Dropped source '{sourceDocumentId}' for '{fieldPath}' because it is not owned by the company.");
                    continue;
                }

                if (!context.AllowCompanyOwnedSources && !context.ResearchRunSourceDocumentIds.Contains(sourceDocumentId))
                {
                    warnings.Add($"Dropped source '{sourceDocumentId}' for '{fieldPath}' because it is not owned by the research run.");
                    continue;
                }

                if (!acceptedSourceIds.Contains(sourceDocumentId))
                {
                    acceptedSourceIds.Add(sourceDocumentId);
                }
            }

            if (!evidenceByFieldPath.TryGetValue(fieldPath, out var normalizedEvidence))
            {
                normalizedEvidence = new ProfileEvidence
                {
                    CompanyProfileCandidateId = candidate.Id,
                    FieldPath = fieldPath
                };
                evidenceByFieldPath.Add(fieldPath, normalizedEvidence);
                sanitized.Evidence.Add(normalizedEvidence);
            }

            foreach (var sourceDocumentId in acceptedSourceIds)
            {
                if (!normalizedEvidence.SourceDocumentIds.Contains(sourceDocumentId))
                {
                    normalizedEvidence.SourceDocumentIds.Add(sourceDocumentId);
                }
            }
        }

        foreach (var normalizedEvidence in sanitized.Evidence.Where(evidence => evidence.SourceDocumentIds.Count == 0).ToArray())
        {
            sanitized.Evidence.Remove(normalizedEvidence);
            warnings.Add($"Dropped evidence for '{normalizedEvidence.FieldPath}' because it had no valid source documents.");
        }

        foreach (var warning in warnings)
        {
            sanitized.ValidationWarnings.Add(warning);
        }

        return new ProfileValidationResult(sanitized, warnings, candidate.CompanyId == context.CompanyId && candidate.ResearchRunId == context.ResearchRunId);
    }

    public static bool IsPermittedFieldPath(string? fieldPath) =>
        TryNormalizeFieldPath(fieldPath, out _);

    public static bool TryNormalizeFieldPath(string? fieldPath, out string normalizedFieldPath)
    {
        normalizedFieldPath = string.Empty;
        var trimmed = fieldPath?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (ScalarAliases.TryGetValue(trimmed, out var scalarPath))
        {
            normalizedFieldPath = scalarPath;
            return true;
        }

        if (CollectionAliases.TryGetValue(trimmed, out var collectionPath))
        {
            normalizedFieldPath = collectionPath;
            return true;
        }

        var match = CollectionPathPattern().Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        var root = match.Groups["root"].Value;
        if (!CollectionPropertyIsPermitted(root, match.Groups["property"].Value))
        {
            return false;
        }

        normalizedFieldPath = CanonicalRootName(root);
        if (match.Groups["index"].Success)
        {
            normalizedFieldPath += $"[{match.Groups["index"].Value}" + "]";
        }

        if (match.Groups["property"].Success)
        {
            normalizedFieldPath += $".{CanonicalPropertyName(root, match.Groups["property"].Value)}";
        }

        return true;
    }

    private static bool CollectionPropertyIsPermitted(string root, string property) =>
        string.IsNullOrEmpty(property) || root.ToLowerInvariant() switch
        {
            "secondaryindustries" => false,
            "productsservices" => property.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                                   property.Equals("type", StringComparison.OrdinalIgnoreCase) ||
                                   property.Equals("description", StringComparison.OrdinalIgnoreCase),
            "markets" => property.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                          property.Equals("type", StringComparison.OrdinalIgnoreCase),
            "leadership" => property.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                             property.Equals("title", StringComparison.OrdinalIgnoreCase),
            "locations" => property.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                            property.Equals("address", StringComparison.OrdinalIgnoreCase) ||
                            property.Equals("country", StringComparison.OrdinalIgnoreCase) ||
                            property.Equals("type", StringComparison.OrdinalIgnoreCase),
            "publiclinks" => property.Equals("url", StringComparison.OrdinalIgnoreCase) ||
                             property.Equals("kind", StringComparison.OrdinalIgnoreCase) ||
                             property.Equals("label", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    private static string CanonicalPropertyName(string root, string property) =>
        root.ToLowerInvariant() switch
        {
            "secondaryindustries" => "secondaryIndustries",
            "productsservices" when property.Equals("name", StringComparison.OrdinalIgnoreCase) => "name",
            "productsservices" when property.Equals("type", StringComparison.OrdinalIgnoreCase) => "type",
            "productsservices" => "description",
            "markets" when property.Equals("name", StringComparison.OrdinalIgnoreCase) => "name",
            "markets" => "type",
            "leadership" when property.Equals("name", StringComparison.OrdinalIgnoreCase) => "name",
            "leadership" => "title",
            "locations" when property.Equals("name", StringComparison.OrdinalIgnoreCase) => "name",
            "locations" when property.Equals("address", StringComparison.OrdinalIgnoreCase) => "address",
            "locations" when property.Equals("country", StringComparison.OrdinalIgnoreCase) => "country",
            "locations" => "type",
            "publiclinks" when property.Equals("url", StringComparison.OrdinalIgnoreCase) => "url",
            "publiclinks" when property.Equals("kind", StringComparison.OrdinalIgnoreCase) => "kind",
            _ => "label"
        };

    private static string CanonicalRootName(string root) =>
        root.ToLowerInvariant() switch
        {
            "secondaryindustries" => "secondaryIndustries",
            "productsservices" => "productsServices",
            "markets" => "markets",
            "leadership" => "leadership",
            "locations" => "locations",
            "publiclinks" => "publicLinks",
            _ => root
        };
}
