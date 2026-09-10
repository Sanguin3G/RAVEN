using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Raven.Api.Features.Ai;

namespace Raven.Api.Features.Profiles.Generation;

/// <summary>
/// Deterministic orchestration around the neutral AI provider. The provider
/// proposes a profile candidate; this service parses only the frozen schema and
/// the shared validator decides which provenance references may survive.
/// </summary>
public sealed class ProfileGenerationService : IProfileGenerationService
{
    public const string ProfilePromptTemplateVersion = "company-profile-v1";

    private static readonly JsonElement ResponseSchema = BuildResponseSchema();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IAiModelProvider aiProvider;
    private readonly IProfileInputBuilder inputBuilder;
    private readonly ProfileGenerationOptions options;

    public ProfileGenerationService(
        IAiModelProvider aiProvider,
        IProfileInputBuilder inputBuilder,
        ProfileGenerationOptions? options = null)
    {
        this.aiProvider = aiProvider ?? throw new ArgumentNullException(nameof(aiProvider));
        this.inputBuilder = inputBuilder ?? throw new ArgumentNullException(nameof(inputBuilder));
        this.options = options ?? new ProfileGenerationOptions();
    }

    public async Task<ProfileGenerationResult> GenerateAsync(
        ProfileGenerationInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var model = string.IsNullOrWhiteSpace(options.Model) ? "gemini-3.5-flash-lite" : options.Model.Trim();
        var templateVersion = string.IsNullOrWhiteSpace(options.PromptTemplateVersion)
            ? ProfilePromptTemplateVersion
            : options.PromptTemplateVersion.Trim();

        if (input.ValidationContext.CompanyId != input.CompanyId || input.ValidationContext.ResearchRunId != input.ResearchRunId)
        {
            return Failure(
                model,
                templateVersion,
                "invalid_context",
                "The profile validation context does not match the requested company and research run.");
        }

        var package = inputBuilder.Build(input.IdentityHints, input.SourceDocuments);
        var request = new AiModelRequest(
            model,
            SystemInstruction,
            BuildPrompt(input.IdentityHints),
            templateVersion,
            package.Payload,
            ResponseSchema);

        AiModelResult aiResult;
        try
        {
            aiResult = await aiProvider.GenerateStructuredAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new ProfileGenerationResult(
                null,
                ["The profile model provider failed before returning a result."],
                aiProvider.Id,
                model,
                templateVersion,
                TimeSpan.Zero,
                Failure: new AiFailure("provider_error", "The profile model provider failed.", true));
        }

        if (!aiResult.Succeeded || !aiResult.StructuredJson.HasValue)
        {
            var failure = aiResult.Failure ?? new AiFailure("invalid_response", "The profile model returned no structured profile.", true);
            return new ProfileGenerationResult(
                null,
                [failure.Message],
                string.IsNullOrWhiteSpace(aiResult.Provider) ? aiProvider.Id : aiResult.Provider,
                string.IsNullOrWhiteSpace(aiResult.Model) ? model : aiResult.Model,
                templateVersion,
                aiResult.Duration,
                aiResult.Usage,
                aiResult.ExternalRequestId,
                failure);
        }

        CompanyProfileCandidate candidate;
        var parseWarnings = new List<string>();
        try
        {
            candidate = ParseCandidate(
                aiResult.StructuredJson.Value,
                input,
                string.IsNullOrWhiteSpace(aiResult.Provider) ? aiProvider.Id : aiResult.Provider,
                string.IsNullOrWhiteSpace(aiResult.Model) ? model : aiResult.Model,
                templateVersion,
                parseWarnings);
        }
        catch (JsonException)
        {
            return new ProfileGenerationResult(
                null,
                ["The profile model returned malformed structured JSON."],
                aiResult.Provider,
                aiResult.Model,
                templateVersion,
                aiResult.Duration,
                aiResult.Usage,
                aiResult.ExternalRequestId,
                new AiFailure("invalid_response", "The profile model returned malformed structured JSON.", true));
        }

        foreach (var warning in parseWarnings)
        {
            candidate.ValidationWarnings.Add(warning);
        }

        var validation = CompanyProfileValidator.Validate(candidate, input.ValidationContext);
        var warnings = parseWarnings.Concat(validation.Warnings).Distinct(StringComparer.Ordinal).ToArray();

        return new ProfileGenerationResult(
            validation.Candidate,
            warnings,
            candidate.AiProvider ?? aiProvider.Id,
            candidate.AiModel ?? model,
            templateVersion,
            aiResult.Duration,
            aiResult.Usage,
            aiResult.ExternalRequestId,
            IsValid: validation.IsValid);
    }

    private static CompanyProfileCandidate ParseCandidate(
        JsonElement root,
        ProfileGenerationInput input,
        string provider,
        string model,
        string templateVersion,
        ICollection<string> warnings)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Profile response root must be an object.");
        }

        var candidate = new CompanyProfileCandidate
        {
            CompanyId = input.CompanyId,
            ResearchRunId = input.ResearchRunId,
            GeneratedAt = DateTimeOffset.UtcNow,
            AiProvider = provider,
            AiModel = model,
            PromptTemplateVersion = templateVersion,
            DisplayName = ReadString(root, "displayName", warnings),
            LegalName = ReadString(root, "legalName", warnings),
            Website = ReadString(root, "website", warnings),
            Country = ReadString(root, "country", warnings),
            Headquarters = ReadString(root, "headquarters", warnings),
            RegistrationNumberOrTaxId = ReadString(root, "registrationNumberOrTaxId", warnings),
            FoundedYear = ReadInt(root, "foundedYear", warnings),
            PrimaryIndustry = ReadString(root, "primaryIndustry", warnings),
            CompanySize = ReadString(root, "companySize", warnings),
            EmployeeCount = ReadInt(root, "employeeCount", warnings),
            EmployeeCountRange = ReadString(root, "employeeCountRange", warnings),
            Summary = ReadString(root, "summary", warnings)
        };

        ReadStringArray(root, "secondaryIndustries", candidate.SecondaryIndustries, warnings);
        ReadProducts(root, candidate.ProductsServices, warnings);
        ReadMarkets(root, candidate.Markets, warnings);
        ReadLeadership(root, candidate.Leadership, warnings);
        ReadLocations(root, candidate.Locations, warnings);
        ReadPublicLinks(root, candidate.PublicLinks, warnings);
        ReadEvidence(root, candidate, warnings);
        return candidate;
    }

    private static string? ReadString(JsonElement parent, string name, ICollection<string> warnings)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            warnings.Add($"Ignored profile field '{name}' because it was not a string or null.");
            return null;
        }

        var text = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static int? ReadInt(JsonElement parent, string name, ICollection<string> warnings)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            warnings.Add($"Ignored profile field '{name}' because it was not an integer or null.");
            return null;
        }

        if (number < 0 || name.Equals("foundedYear", StringComparison.Ordinal) && (number < 1000 || number > DateTime.UtcNow.Year + 1))
        {
            warnings.Add($"Ignored profile field '{name}' because its value was outside the permitted range.");
            return null;
        }

        return number;
    }

    private static void ReadStringArray(
        JsonElement parent,
        string name,
        ICollection<string> target,
        ICollection<string> warnings)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            warnings.Add($"Ignored profile collection '{name}' because it was not an array.");
            return;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            {
                target.Add(item.GetString()!.Trim());
            }
            else
            {
                warnings.Add($"Ignored one item in profile collection '{name}' because it was not a string.");
            }
        }
    }

    private static void ReadProducts(JsonElement parent, ICollection<ProfileProductService> target, ICollection<string> warnings)
    {
        if (!TryGetArray(parent, "productsServices", out var values, warnings)) return;
        foreach (var item in values.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("Ignored one productsServices item because it was not an object.");
                continue;
            }

            var name = ReadString(item, "name", warnings);
            if (name is null)
            {
                warnings.Add("Ignored one productsServices item because name was missing.");
                continue;
            }

            target.Add(new ProfileProductService(name, ReadString(item, "type", warnings), ReadString(item, "description", warnings)));
        }
    }

    private static void ReadMarkets(JsonElement parent, ICollection<ProfileMarket> target, ICollection<string> warnings)
    {
        if (!TryGetArray(parent, "markets", out var values, warnings)) return;
        foreach (var item in values.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("Ignored one markets item because it was not an object.");
                continue;
            }

            var name = ReadString(item, "name", warnings);
            if (name is null)
            {
                warnings.Add("Ignored one markets item because name was missing.");
                continue;
            }

            target.Add(new ProfileMarket(name, ReadString(item, "type", warnings)));
        }
    }

    private static void ReadLeadership(JsonElement parent, ICollection<ProfileLeader> target, ICollection<string> warnings)
    {
        if (!TryGetArray(parent, "leadership", out var values, warnings)) return;
        foreach (var item in values.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("Ignored one leadership item because it was not an object.");
                continue;
            }

            var name = ReadString(item, "name", warnings);
            if (name is null)
            {
                warnings.Add("Ignored one leadership item because name was missing.");
                continue;
            }

            target.Add(new ProfileLeader(name, ReadString(item, "title", warnings)));
        }
    }

    private static void ReadLocations(JsonElement parent, ICollection<ProfileLocation> target, ICollection<string> warnings)
    {
        if (!TryGetArray(parent, "locations", out var values, warnings)) return;
        foreach (var item in values.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("Ignored one locations item because it was not an object.");
                continue;
            }

            var location = new ProfileLocation(
                ReadString(item, "name", warnings),
                ReadString(item, "address", warnings),
                ReadString(item, "country", warnings),
                ReadString(item, "type", warnings));
            if (location is { Name: null, Address: null, Country: null, Type: null })
            {
                warnings.Add("Ignored one locations item because it had no supported values.");
                continue;
            }

            target.Add(location);
        }
    }

    private static void ReadPublicLinks(JsonElement parent, ICollection<ProfilePublicLink> target, ICollection<string> warnings)
    {
        if (!TryGetArray(parent, "publicLinks", out var values, warnings)) return;
        foreach (var item in values.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("Ignored one publicLinks item because it was not an object.");
                continue;
            }

            var url = ReadString(item, "url", warnings);
            if (!IsHttpUrl(url))
            {
                warnings.Add("Ignored one publicLinks item because its URL was not HTTP(S).");
                continue;
            }

            target.Add(new ProfilePublicLink(url!, ReadString(item, "kind", warnings), ReadString(item, "label", warnings)));
        }
    }

    private static void ReadEvidence(JsonElement parent, CompanyProfileCandidate candidate, ICollection<string> warnings)
    {
        if (!TryGetArray(parent, "evidence", out var values, warnings)) return;
        foreach (var item in values.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("Ignored one evidence item because it was not an object.");
                continue;
            }

            var fieldPath = ReadString(item, "fieldPath", warnings);
            if (fieldPath is null)
            {
                warnings.Add("Ignored one evidence item because fieldPath was missing.");
                continue;
            }

            var evidence = new ProfileEvidence { CompanyProfileCandidateId = candidate.Id, FieldPath = fieldPath };
            if (item.TryGetProperty("sourceDocumentIds", out var sourceIds))
            {
                if (sourceIds.ValueKind != JsonValueKind.Array)
                {
                    warnings.Add($"Ignored sourceDocumentIds for '{fieldPath}' because it was not an array.");
                }
                else
                {
                    foreach (var sourceId in sourceIds.EnumerateArray())
                    {
                        if (sourceId.ValueKind == JsonValueKind.String && Guid.TryParse(sourceId.GetString(), out var id))
                        {
                            evidence.SourceDocumentIds.Add(id);
                        }
                        else
                        {
                            warnings.Add($"Ignored one source reference for '{fieldPath}' because it was not a GUID.");
                        }
                    }
                }
            }

            candidate.Evidence.Add(evidence);
        }
    }

    private static bool TryGetArray(JsonElement parent, string name, out JsonElement array, ICollection<string> warnings)
    {
        array = default;
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            warnings.Add($"Ignored profile collection '{name}' because it was not an array.");
            return false;
        }

        array = value;
        return true;
    }

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    private static string BuildPrompt(ProfileIdentityHints hints) =>
        $"Generate one evidence-grounded CompanyProfile candidate for the supplied identity. " +
        $"Treat all identity values as research hints, not verified facts. " +
        $"Identity hints: display name={hints.DisplayName ?? "null"}; legal name={hints.LegalName ?? "null"}; " +
        $"website={hints.Website ?? "null"}; country={hints.Country ?? "null"}; " +
        $"headquarters={hints.Headquarters ?? "null"}; registration/tax ID={hints.RegistrationNumberOrTaxId ?? "null"}; " +
        $"research hint={hints.ResearchHint ?? "null"}. Return only the requested JSON object.";

    private static ProfileGenerationResult Failure(string model, string templateVersion, string code, string message) =>
        new(
            null,
            [message],
            "raven",
            model,
            templateVersion,
            TimeSpan.Zero,
            Failure: new AiFailure(code, message, false));

    private const string SystemInstruction = """
        You are RAVEN's deterministic Company Profile normalizer. Use only the supplied identity hints and supplied source evidence.
        Identity hints are not verified facts. Search snippets are discovery leads, not evidence.
        Never use unsupplied world knowledge, plausible assumptions, or fabricated completeness.
        If supplied evidence does not support a scalar, return null. If it does not support a collection, return [].
        Do not convert a vague employee range into an exact employee count.
        Name a leader only when the evidence explicitly associates that person with this company and role.
        Prefer the most authoritative source for each field. When sources disagree, prefer higher field-specific authority or leave the value null and be cautious.
        Every non-null factual value should have one or more evidence entries citing supplied SOURCE_ID values where practical.
        Evidence entries may cite only SOURCE_ID values present in the supplied evidence. Never invent source IDs.
        Return only JSON matching the response schema. Do not return markdown, commentary, or chain-of-thought.
        """;

    private static JsonElement BuildResponseSchema()
    {
        const string schema = """
        {
          "type": "OBJECT",
          "properties": {
            "displayName": { "type": "STRING", "nullable": true },
            "legalName": { "type": "STRING", "nullable": true },
            "website": { "type": "STRING", "nullable": true },
            "country": { "type": "STRING", "nullable": true },
            "headquarters": { "type": "STRING", "nullable": true },
            "registrationNumberOrTaxId": { "type": "STRING", "nullable": true },
            "foundedYear": { "type": "INTEGER", "nullable": true },
            "primaryIndustry": { "type": "STRING", "nullable": true },
            "secondaryIndustries": { "type": "ARRAY", "items": { "type": "STRING" } },
            "companySize": { "type": "STRING", "nullable": true },
            "employeeCount": { "type": "INTEGER", "nullable": true },
            "employeeCountRange": { "type": "STRING", "nullable": true },
            "summary": { "type": "STRING", "nullable": true },
            "productsServices": {
              "type": "ARRAY",
              "items": {
                "type": "OBJECT",
                "properties": {
                  "name": { "type": "STRING" },
                  "type": { "type": "STRING", "nullable": true },
                  "description": { "type": "STRING", "nullable": true }
                }
              }
            },
            "markets": {
              "type": "ARRAY",
              "items": {
                "type": "OBJECT",
                "properties": {
                  "name": { "type": "STRING" },
                  "type": { "type": "STRING", "nullable": true }
                }
              }
            },
            "leadership": {
              "type": "ARRAY",
              "items": {
                "type": "OBJECT",
                "properties": {
                  "name": { "type": "STRING" },
                  "title": { "type": "STRING", "nullable": true }
                }
              }
            },
            "locations": {
              "type": "ARRAY",
              "items": {
                "type": "OBJECT",
                "properties": {
                  "name": { "type": "STRING", "nullable": true },
                  "address": { "type": "STRING", "nullable": true },
                  "country": { "type": "STRING", "nullable": true },
                  "type": { "type": "STRING", "nullable": true }
                }
              }
            },
            "publicLinks": {
              "type": "ARRAY",
              "items": {
                "type": "OBJECT",
                "properties": {
                  "url": { "type": "STRING" },
                  "kind": { "type": "STRING", "nullable": true },
                  "label": { "type": "STRING", "nullable": true }
                }
              }
            },
            "evidence": {
              "type": "ARRAY",
              "items": {
                "type": "OBJECT",
                "properties": {
                  "fieldPath": { "type": "STRING" },
                  "sourceDocumentIds": { "type": "ARRAY", "items": { "type": "STRING" } }
                }
              }
            }
          }
        }
        """;

        using var document = JsonDocument.Parse(schema);
        return document.RootElement.Clone();
    }
}
