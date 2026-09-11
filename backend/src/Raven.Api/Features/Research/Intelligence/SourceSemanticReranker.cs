using System.Text.Json;
using System.Text.Json.Serialization;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Settings;

namespace Raven.Api.Features.Research.Intelligence;

/// <summary>
/// Uses a structured Gemini response to add semantic context to the
/// deterministic source ranking. It only returns assessments for candidate IDs
/// supplied by the caller; the caller remains responsible for the final source
/// selection policy.
/// </summary>
public sealed class GeminiSourceSemanticReranker : ISourceSemanticReranker
{
    public const string SourceSemanticPromptTemplateVersion = "source-semantic-rerank-v1";
    public const string DefaultModel = "gemini-3.5-flash-lite";

    private const int MaxCandidates = 100;
    private const int MaxTextCharacters = 600;
    private const int MaxRationaleCharacters = 240;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonElement ResponseSchema = BuildResponseSchema();

    private readonly IAiModelProvider aiProvider;
    private readonly string? model;
    private readonly IResearchSettingsService? researchSettings;

    public GeminiSourceSemanticReranker(
        IAiModelProvider aiProvider,
        string? model = null,
        IResearchSettingsService? researchSettings = null)
    {
        this.aiProvider = aiProvider ?? throw new ArgumentNullException(nameof(aiProvider));
        this.model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        this.researchSettings = researchSettings;
    }

    public async Task<SourceSemanticRerankResult> RerankAsync(
        SourceSemanticRerankRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Target is null)
        {
            return InvalidRequest("A resolved research target is required.");
        }

        if (request.Candidates is null)
        {
            return InvalidRequest("Source candidates are required.");
        }

        if (request.Candidates.Count == 0)
        {
            return new SourceSemanticRerankResult([]);
        }

        var candidates = request.Candidates
            .Take(MaxCandidates)
            .ToArray();

        var candidateIds = candidates.Select(candidate => candidate.CandidateId).ToHashSet();
        var selectedModel = await ResolveModelAsync(cancellationToken);
        var evidence = new AiEvidencePayload(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["DisplayName"] = request.Target.DisplayName,
                ["LegalName"] = request.Target.LegalName,
                ["Country"] = request.Target.Country,
                ["Website"] = request.Target.Website,
                ["OfficialDomain"] = request.Target.OfficialDomain,
                ["EntityType"] = request.Target.EntityType.ToString()
            },
            candidates.Select(ToEvidenceItem).ToArray());

        var aiRequest = new AiModelRequest(
            selectedModel,
            SystemInstruction,
            BuildPrompt(request.Target, candidates),
            SourceSemanticPromptTemplateVersion,
            evidence,
            ResponseSchema);

        AiModelResult aiResult;
        try
        {
            aiResult = await aiProvider.GenerateStructuredAsync(aiRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Failure(
                "The source recommendation model failed before returning a result.",
                new AiFailure("provider_error", "The source recommendation model failed.", true));
        }

        if (!aiResult.Succeeded || !aiResult.StructuredJson.HasValue)
        {
            var failure = aiResult.Failure ?? new AiFailure(
                "invalid_response",
                "The source recommendation model returned no structured result.",
                true);
            return new SourceSemanticRerankResult([], failure.Message, failure);
        }

        try
        {
            return ParseResult(aiResult.StructuredJson.Value, candidateIds);
        }
        catch (JsonException)
        {
            var failure = new AiFailure(
                "invalid_response",
                "The source recommendation model returned malformed structured JSON.",
                true);
            return new SourceSemanticRerankResult([], failure.Message, failure);
        }
    }

    private async Task<string> ResolveModelAsync(CancellationToken cancellationToken)
    {
        if (model is not null)
        {
            return model;
        }

        if (researchSettings is not null)
        {
            try
            {
                var settings = await researchSettings.GetAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(settings.GroundingModel))
                {
                    return settings.GroundingModel;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A settings read cannot make deterministic recommendations unavailable.
            }
        }

        return DefaultModel;
    }

    private static SourceSemanticRerankResult ParseResult(
        JsonElement root,
        IReadOnlySet<Guid> candidateIds)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("assessments", out var assessments) ||
            assessments.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Source semantic response must contain an assessments array.");
        }

        var parsed = new List<SourceSemanticAssessment>();
        var warnings = new List<string>();
        var seen = new HashSet<Guid>();

        foreach (var element in assessments.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("Ignored a source assessment because it was not an object.");
                continue;
            }

            if (!TryReadGuid(element, "candidateId", out var candidateId))
            {
                warnings.Add("Ignored a source assessment with a missing or invalid candidate ID.");
                continue;
            }

            if (!candidateIds.Contains(candidateId))
            {
                warnings.Add($"Ignored source assessment for unknown candidate '{candidateId:D}'.");
                continue;
            }

            if (!seen.Add(candidateId))
            {
                warnings.Add($"Ignored duplicate source assessment for candidate '{candidateId:D}'.");
                continue;
            }

            if (!TryReadEnum(element, "entityRelationship", out EntityRelationship relationship))
            {
                warnings.Add($"Ignored source assessment for candidate '{candidateId:D}' because its entity relationship was invalid.");
                continue;
            }

            if (!TryReadEnum(element, "relevance", out CandidateRelevance relevance))
            {
                warnings.Add($"Ignored source assessment for candidate '{candidateId:D}' because its relevance was invalid.");
                continue;
            }

            if (!element.TryGetProperty("recommended", out var recommendedElement) ||
                (recommendedElement.ValueKind != JsonValueKind.True && recommendedElement.ValueKind != JsonValueKind.False))
            {
                warnings.Add($"Ignored source assessment for candidate '{candidateId:D}' because its recommendation flag was invalid.");
                continue;
            }

            parsed.Add(new SourceSemanticAssessment(
                candidateId,
                relationship,
                relevance,
                recommendedElement.GetBoolean(),
                ReadPurposes(element, candidateId, warnings),
                ReadShortString(element, "rationale", MaxRationaleCharacters, warnings, candidateId)));
        }

        return new SourceSemanticRerankResult(
            parsed,
            warnings.Count == 0 ? null : string.Join(" ", warnings));
    }

    private static IReadOnlyList<string> ReadPurposes(
        JsonElement assessment,
        Guid candidateId,
        ICollection<string> warnings)
    {
        if (!assessment.TryGetProperty("purposes", out var purposes) || purposes.ValueKind != JsonValueKind.Array)
        {
            warnings.Add($"Source assessment for candidate '{candidateId:D}' omitted purposes.");
            return [];
        }

        var values = new List<string>();
        foreach (var purpose in purposes.EnumerateArray())
        {
            if (purpose.ValueKind != JsonValueKind.String)
            {
                warnings.Add($"Ignored a non-text purpose for candidate '{candidateId:D}'.");
                continue;
            }

            var value = purpose.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static bool TryReadGuid(JsonElement parent, string propertyName, out Guid value)
    {
        value = Guid.Empty;
        return parent.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               Guid.TryParse(property.GetString(), out value);
    }

    private static bool TryReadEnum<T>(JsonElement parent, string propertyName, out T value)
        where T : struct, Enum
    {
        value = default;
        return parent.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               Enum.TryParse(property.GetString(), true, out value) &&
               Enum.IsDefined(value);
    }

    private static string? ReadShortString(
        JsonElement parent,
        string propertyName,
        int maxCharacters,
        ICollection<string> warnings,
        Guid candidateId)
    {
        if (!parent.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            warnings.Add($"Ignored non-text rationale for candidate '{candidateId:D}'.");
            return null;
        }

        var text = property.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (text.Length <= maxCharacters)
        {
            return text;
        }

        warnings.Add($"Shortened an overlong rationale for candidate '{candidateId:D}'.");
        return text[..(maxCharacters - 1)].TrimEnd() + "…";
    }

    private static AiEvidenceItem ToEvidenceItem(SourceSemanticCandidate candidate) =>
        new(
            candidate.CandidateId.ToString("D"),
            candidate.SourceKind.ToString(),
            Truncate(candidate.Title ?? candidate.Domain),
            SafeUrl(candidate.Url),
            Truncate(candidate.Snippet ?? string.Empty));

    private static string BuildPrompt(
        ResolvedResearchEntity target,
        IReadOnlyList<SourceSemanticCandidate> candidates)
    {
        var input = new
        {
            target = new
            {
                target.TemporaryId,
                target.DisplayName,
                target.LegalName,
                target.Country,
                target.Website,
                target.OfficialDomain,
                entityType = target.EntityType.ToString()
            },
            candidates = candidates.Select(candidate => new
            {
                candidateId = candidate.CandidateId.ToString("D"),
                url = SafeUrl(candidate.Url),
                candidate.Domain,
                candidate.Title,
                candidate.Snippet,
                sourceKind = candidate.SourceKind.ToString(),
                candidate.DeterministicScore,
                candidate.DeterministicReasons
            })
        };

        return "Assess each supplied source candidate against the resolved research target. " +
               "Return only the structured JSON response. Use candidateId values exactly as supplied; " +
               "do not invent IDs. Classify whether each source is the same entity, a parent, subsidiary, " +
               "affiliate, different entity, or uncertain. Mark high relevance only when the source is useful " +
               "for the target. Recommend strong same-entity sources and keep rationales short and user-facing. " +
               "Do not provide hidden reasoning or unsupported facts.\n\n" +
               JsonSerializer.Serialize(input, JsonOptions);
    }

    private static string SafeUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
         uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            ? uri.ToString()
            : string.Empty;

    private static string Truncate(string value) =>
        value.Length <= MaxTextCharacters ? value : value[..MaxTextCharacters];

    private static SourceSemanticRerankResult InvalidRequest(string message) =>
        Failure(message, new AiFailure("invalid_request", message, false));

    private static SourceSemanticRerankResult Failure(string warning, AiFailure failure) =>
        new([], warning, failure);

    private static JsonElement BuildResponseSchema()
    {
        using var document = JsonDocument.Parse("""
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["assessments"],
              "properties": {
                "assessments": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "required": ["candidateId", "entityRelationship", "relevance", "recommended", "purposes"],
                    "properties": {
                      "candidateId": {"type": "string"},
                      "entityRelationship": {
                        "type": "string",
                        "enum": ["SameEntity", "Parent", "Subsidiary", "Affiliate", "DifferentEntity", "Uncertain"]
                      },
                      "relevance": {"type": "string", "enum": ["High", "Medium", "Low"]},
                      "recommended": {"type": "boolean"},
                      "purposes": {"type": "array", "items": {"type": "string"}},
                      "rationale": {"type": ["string", "null"]}
                    }
                  }
                }
              }
            }
            """);
        return document.RootElement.Clone();
    }

    private const string SystemInstruction =
        "You are RAVEN's source relevance classifier. Use only the supplied target and candidate metadata. " +
        "Never use outside knowledge as a factual source. Return JSON matching the schema. Keep rationale concise, " +
        "never reveal private chain-of-thought, and never alter candidate IDs.";
}
