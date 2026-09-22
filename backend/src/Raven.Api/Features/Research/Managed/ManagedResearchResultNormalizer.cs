using System.Globalization;
using System.Text.Json;

namespace Raven.Api.Features.ManagedResearch;

/// <summary>
/// Converts provider output into bounded, provider-neutral research material.
/// Provider confidence is retained as advisory metadata and is never promoted
/// to accepted profile evidence.
/// </summary>
public static class ManagedResearchResultNormalizer
{
    public static ManagedResearchResult Normalize(
        string objective,
        ManagedResearchProviderRun run,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Status != ManagedResearchProviderRunStatus.Completed)
        {
            throw new ArgumentException("Only a completed provider run can be normalized.", nameof(run));
        }

        var output = run.Output;
        var structured = output?.Structured;
        var summary = ReadString(structured, "summary", "researchSummary", "answer") ?? output?.Text;
        summary = ManagedResearchText.Bound(summary, ManagedResearchLimits.MaxSummaryLength);

        var claims = ReadClaims(structured);
        var sources = ReadSources(structured);
        AddGroundingSources(sources, output?.Grounding ?? [], claims);

        if (string.IsNullOrWhiteSpace(summary) && claims.Count == 0 && sources.Count == 0)
        {
            throw new ManagedResearchNormalizationException("The managed research provider returned no usable research material.");
        }

        var uncertainties = ReadStringArray(structured, "uncertainties", "unknowns", "caveats")
            .Take(ManagedResearchLimits.MaxUncertainties)
            .ToArray();
        var metadata = new Dictionary<string, string>(run.SafeMetadata, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(run.StopReason))
        {
            metadata["stopReason"] = ManagedResearchText.Bound(run.StopReason, 100);
        }

        if (run.CostDollars is not null)
        {
            metadata["costDollars"] = run.CostDollars.Value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        return new ManagedResearchResult(
            run.Provider,
            ManagedResearchText.Bound(objective, ManagedResearchLimits.MaxObjectiveLength),
            summary,
            claims,
            sources,
            uncertainties,
            run.CompletedAt ?? completedAt,
            metadata,
            run.CostDollars);
    }

    private static List<ManagedResearchClaim> ReadClaims(JsonElement? structured)
    {
        var claims = new List<ManagedResearchClaim>();
        if (!TryGetProperty(structured, out var rawClaims, "claims", "findings") ||
            rawClaims.ValueKind != JsonValueKind.Array)
        {
            return claims;
        }

        foreach (var rawClaim in rawClaims.EnumerateArray().Take(ManagedResearchLimits.MaxClaims))
        {
            if (rawClaim.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var statement = ReadString(rawClaim, "statement", "claim", "finding");
            if (string.IsNullOrWhiteSpace(statement))
            {
                continue;
            }

            var topic = ReadString(rawClaim, "topic", "field", "area") ?? "General";
            var sourceUrls = ReadUrlArray(rawClaim, "supportingSourceUrls", "sourceUrls", "supportingUrls", "urls");
            var confidence = ReadDecimal(rawClaim, "providerConfidence", "confidence");
            if (confidence is not null)
            {
                confidence = Math.Clamp(confidence.Value, 0m, 1m);
            }

            claims.Add(new ManagedResearchClaim(
                ManagedResearchText.Bound(topic, 300),
                ManagedResearchText.Bound(statement, 8_000),
                sourceUrls,
                confidence,
                BoundOptional(ReadString(rawClaim, "notes", "note"), 2_000)));
        }

        return claims;
    }

    private static List<ManagedResearchSource> ReadSources(JsonElement? structured)
    {
        var sources = new List<ManagedResearchSource>();
        if (!TryGetProperty(structured, out var rawSources, "sources", "references") ||
            rawSources.ValueKind != JsonValueKind.Array)
        {
            return sources;
        }

        foreach (var rawSource in rawSources.EnumerateArray().Take(ManagedResearchLimits.MaxSources))
        {
            if (rawSource.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var url = NormalizeUrl(ReadString(rawSource, "url", "sourceUrl", "link"));
            if (url is null)
            {
                continue;
            }

            var title = ReadString(rawSource, "title", "name") ?? url;
            var publisher = ReadString(rawSource, "publisher", "domain", "source");
            var date = ReadDate(rawSource, "publishedAt", "publishedDate", "date", "updatedAt");
            var supports = ReadString(rawSource, "supports", "supportsClaim", "relevance");
            AddSource(sources, new ManagedResearchSource(
                ManagedResearchText.Bound(title, 500),
                url,
                BoundOptional(publisher, 300),
                date,
                BoundOptional(supports, 1_000)));
        }

        return sources;
    }

    private static void AddGroundingSources(
        ICollection<ManagedResearchSource> sources,
        IReadOnlyList<ManagedResearchCitation> grounding,
        IReadOnlyList<ManagedResearchClaim> claims)
    {
        foreach (var citation in grounding.Take(ManagedResearchLimits.MaxSources))
        {
            var url = NormalizeUrl(citation.Url);
            if (url is null)
            {
                continue;
            }

            var support = string.IsNullOrWhiteSpace(citation.Field)
                ? null
                : $"Provider grounding for {ManagedResearchText.Bound(citation.Field, 200)}";
            AddSource(sources, new ManagedResearchSource(
                ManagedResearchText.Bound(citation.Title, 500) is { Length: > 0 } title ? title : url,
                url,
                TryGetPublisher(url),
                null,
                support));
        }

        // Keep source URLs cited by claims discoverable even when the provider
        // omitted a separate sources array.
        foreach (var url in claims.SelectMany(claim => claim.SupportingSourceUrls).Take(ManagedResearchLimits.MaxSources))
        {
            var normalized = NormalizeUrl(url);
            if (normalized is not null)
            {
                AddSource(sources, new ManagedResearchSource(normalized, normalized, TryGetPublisher(normalized)));
            }
        }
    }

    private static void AddSource(ICollection<ManagedResearchSource> sources, ManagedResearchSource source)
    {
        if (!sources.Any(item => string.Equals(item.Url, source.Url, StringComparison.OrdinalIgnoreCase)))
        {
            sources.Add(source);
        }
    }

    private static IReadOnlyList<string> ReadUrlArray(JsonElement parent, params string[] names)
    {
        if (!TryGetProperty(parent, out var value, names) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => NormalizeUrl(item.GetString()))
            .Where(item => item is not null)
            .Select(item => item!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement? parent, params string[] names)
    {
        if (!TryGetProperty(parent, out var value, names) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => ManagedResearchText.Bound(item.GetString(), 1_000))
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ReadString(JsonElement? parent, params string[] names) =>
        TryGetProperty(parent, out var value, names) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? ReadDecimal(JsonElement parent, params string[] names)
    {
        if (!TryGetProperty(parent, out var value, names))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static DateTimeOffset? ReadDate(JsonElement parent, params string[] names)
    {
        var value = ReadString(parent, names);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;
    }

    private static bool TryGetProperty(JsonElement? parent, out JsonElement value, params string[] names)
    {
        if (parent is not { ValueKind: JsonValueKind.Object } objectValue)
        {
            value = default;
            return false;
        }

        foreach (var property in objectValue.EnumerateObject())
        {
            if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? NormalizeUrl(string? value)
    {
        var normalized = value?.Trim();
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri) &&
               uri.Scheme is "http" or "https"
            ? uri.ToString()
            : null;
    }

    private static string? TryGetPublisher(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;

    private static string? BoundOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return ManagedResearchText.Bound(value, maxLength);
    }
}

/// <summary>Raised when completed provider output cannot be treated as research material.</summary>
public sealed class ManagedResearchNormalizationException(string message) : InvalidOperationException(message);
