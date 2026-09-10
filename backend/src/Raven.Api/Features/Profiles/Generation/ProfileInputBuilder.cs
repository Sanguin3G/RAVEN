using System.Text;
using System.Text.Json;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Sources;

namespace Raven.Api.Features.Profiles.Generation;

public sealed class ProfileInputBuilderOptions
{
    public int MaxSources { get; set; } = 20;
    public int MaxCharactersPerSource { get; set; } = 20_000;
    public int MaxTotalCharacters { get; set; } = 100_000;
    public int MaxStructuredFactsCharacters { get; set; } = 2_000;
}

/// <summary>
/// Builds a bounded, source-shaped evidence package for profile generation.
/// The package contains source IDs and acquired evidence only; it never loads
/// anything from persistence and never forwards an unbounded document.
/// </summary>
public sealed class ProfileInputBuilder : IProfileInputBuilder
{
    private static readonly SourceField[] AuthorityFields = Enum.GetValues<SourceField>();

    private readonly ISourceAuthorityPolicy authorityPolicy;
    private readonly ProfileInputBuilderOptions options;

    public ProfileInputBuilder(
        ISourceAuthorityPolicy authorityPolicy,
        ProfileInputBuilderOptions? options = null)
    {
        this.authorityPolicy = authorityPolicy ?? throw new ArgumentNullException(nameof(authorityPolicy));
        this.options = options ?? new ProfileInputBuilderOptions();
        ValidateOptions(this.options);
    }

    public ProfileEvidencePackage Build(
        ProfileIdentityHints identityHints,
        IReadOnlyCollection<SourceDocument> sourceDocuments)
    {
        ArgumentNullException.ThrowIfNull(identityHints);
        ArgumentNullException.ThrowIfNull(sourceDocuments);

        var hints = BuildIdentityHints(identityHints);
        var rankedDocuments = sourceDocuments
            .Where(document => IsHttpUrl(document.Url))
            .GroupBy(document => document.NormalizedUrl?.Trim() ?? document.Url.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(GetAuthorityRank)
                .ThenByDescending(document => document.RetrievedAt)
                .First())
            .OrderByDescending(GetAuthorityRank)
            .ThenByDescending(document => document.RetrievedAt)
            .Take(options.MaxSources);

        var sources = new List<ProfileInputSource>();
        var characterCount = 0;

        foreach (var document in rankedDocuments)
        {
            if (characterCount >= options.MaxTotalCharacters)
            {
                break;
            }

            var remaining = options.MaxTotalCharacters - characterCount;
            var content = CleanAndBoundContent(document.Content, Math.Min(options.MaxCharactersPerSource, remaining));
            var structuredFacts = ParseStructuredFacts(document.StructuredFactsJson);
            var title = CleanScalar(document.Title, 500) ?? document.SourceDomain ?? "Public source";
            var url = document.Url.Trim();

            var evidence = new AiEvidenceItem(
                document.Id.ToString("D"),
                document.SourceKind.ToString(),
                title,
                url,
                content,
                structuredFacts);

            sources.Add(new ProfileInputSource(document.Id, evidence, GetAuthorityRank(document)));
            characterCount += content.Length;
        }

        return new ProfileEvidencePackage(
            new AiEvidencePayload(hints, sources.Select(source => source.Evidence).ToArray()),
            sources,
            characterCount);
    }

    private int GetAuthorityRank(SourceDocument document) =>
        AuthorityFields.Max(field => authorityPolicy.GetRank(field, document.SourceKind));

    private static IReadOnlyDictionary<string, string?>? BuildIdentityHints(ProfileIdentityHints identityHints)
    {
        var hints = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["DisplayName"] = CleanScalar(identityHints.DisplayName, 500),
            ["LegalName"] = CleanScalar(identityHints.LegalName, 500),
            ["Website"] = CleanScalar(identityHints.Website, 1_000),
            ["Country"] = CleanScalar(identityHints.Country, 200),
            ["Headquarters"] = CleanScalar(identityHints.Headquarters, 1_000),
            ["RegistrationNumberOrTaxId"] = CleanScalar(identityHints.RegistrationNumberOrTaxId, 200),
            ["ResearchHint"] = CleanScalar(identityHints.ResearchHint, 2_000)
        };

        var populated = hints
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return populated.Count == 0 ? null : populated;
    }

    private IReadOnlyDictionary<string, string?>? ParseStructuredFacts(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > options.MaxStructuredFactsCharacters)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var facts = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var value = property.Value;
                string? text = value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
                    JsonValueKind.Null => null,
                    _ => null
                };

                if (text is not null)
                {
                    facts[property.Name] = CleanScalar(text, 500);
                }
            }

            return facts.Count == 0 ? null : facts;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string CleanAndBoundContent(string? content, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(content) || maximumLength <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(content.Length, maximumLength));
        foreach (var character in content)
        {
            if (character == '\0' || char.IsControl(character) && character is not ('\r' or '\n' or '\t'))
            {
                continue;
            }

            builder.Append(character);
            if (builder.Length >= maximumLength)
            {
                break;
            }
        }

        return builder.ToString().Trim();
    }

    private static string? CleanScalar(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = new string(value
            .Where(character => character == '\t' || !char.IsControl(character))
            .ToArray())
            .Trim();

        return cleaned.Length == 0 ? null : cleaned[..Math.Min(cleaned.Length, maximumLength)];
    }

    private static bool IsHttpUrl(string? value)
    {
        return Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
               && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                   || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateOptions(ProfileInputBuilderOptions options)
    {
        if (options.MaxSources < 1 || options.MaxCharactersPerSource < 1 || options.MaxTotalCharacters < 1 || options.MaxStructuredFactsCharacters < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Profile evidence bounds must be greater than zero.");
        }
    }
}
