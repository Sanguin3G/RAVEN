using System.Text;
using System.Text.Json;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Settings;

namespace Raven.Api.Features.Research.Intelligence;

/// <summary>
/// Resolves an identity using the neutral structured-AI boundary. The model
/// receives bounded candidate metadata only; it does not receive crawled page
/// content and it cannot mutate a Company or a ResearchRun.
/// </summary>
public sealed class GeminiCompanyIdentityResolver : ICompanyIdentityResolver
{
    public const string DefaultModel = "gemini-3.5-flash-lite";
    public const string DefaultPromptTemplateVersion = "company-identity-grounding-v1";

    private const int MaxCandidates = 40;
    private const int MaxFieldCharacters = 600;
    private const int MaxRationaleCharacters = 500;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonElement ResponseSchema = CreateResponseSchema();

    private readonly IAiModelProvider modelProvider;
    private readonly string? model;
    private readonly string promptTemplateVersion;
    private readonly IResearchSettingsService? researchSettings;

    public GeminiCompanyIdentityResolver(
        IAiModelProvider modelProvider,
        string? model = null,
        string? promptTemplateVersion = null,
        IResearchSettingsService? researchSettings = null)
    {
        this.modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
        this.model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        this.promptTemplateVersion = string.IsNullOrWhiteSpace(promptTemplateVersion)
            ? DefaultPromptTemplateVersion
            : promptTemplateVersion.Trim();
        this.researchSettings = researchSettings;
    }

    public async Task<IdentityResolutionResult> ResolveAsync(
        IdentityResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Candidates is null || request.Candidates.Count == 0)
        {
            return InvalidRequest("At least one discovery candidate is required for identity grounding.");
        }

        var candidates = request.Candidates.Take(MaxCandidates).ToArray();
        var selectedModel = await ResolveModelAsync(cancellationToken);
        var aiRequest = new AiModelRequest(
            selectedModel,
            SystemInstruction,
            BuildPrompt(request.Identity, candidates),
            promptTemplateVersion,
            BuildEvidence(request.Identity, candidates),
            ResponseSchema);

        AiModelResult modelResult;
        try
        {
            modelResult = await modelProvider.GenerateStructuredAsync(aiRequest, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(
                new AiFailure("timeout", "Company identity grounding timed out.", true),
                "Company identity grounding timed out; deterministic research can continue.");
        }
        catch (Exception)
        {
            return Failure(
                new AiFailure("provider_error", "Company identity grounding could not be completed.", true),
                "Company identity grounding failed; deterministic research can continue.");
        }

        if (modelResult.Failure is not null)
        {
            return Failure(
                modelResult.Failure,
                "Company identity grounding was unavailable; deterministic research can continue.");
        }

        if (!modelResult.StructuredJson.HasValue ||
            modelResult.StructuredJson.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return Failure(
                new AiFailure("invalid_response", "The grounding model returned no structured result.", true),
                "Company identity grounding returned no usable result; deterministic research can continue.");
        }

        try
        {
            var response = JsonSerializer.Deserialize<GroundingResponseDto>(
                modelResult.StructuredJson.Value.GetRawText(),
                JsonOptions);

            return ValidateResponse(response, candidates);
        }
        catch (JsonException)
        {
            return Failure(
                new AiFailure("invalid_response", "The grounding model returned malformed structured data.", false),
                "Company identity grounding returned malformed data; deterministic research can continue.");
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
                // Settings must not make deterministic fallback research unavailable.
            }
        }

        return DefaultModel;
    }

    private IdentityResolutionResult ValidateResponse(
        GroundingResponseDto? response,
        IReadOnlyList<GroundingSourceCandidate> candidates)
    {
        if (response is null)
        {
            return Failure(
                new AiFailure("invalid_response", "The grounding model returned an empty result.", false),
                "Company identity grounding returned an empty result; deterministic research can continue.");
        }

        var candidateIds = candidates
            .Select(candidate => candidate.CandidateId)
            .ToHashSet();

        var entities = new List<ResolvedResearchEntity>();
        var temporaryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recommendedCount = 0;

        foreach (var entity in response.Entities ?? [])
        {
            if (string.IsNullOrWhiteSpace(entity.TemporaryId) ||
                string.IsNullOrWhiteSpace(entity.DisplayName))
            {
                return Failure(
                    new AiFailure("invalid_response", "A grounded entity was missing its identifier or display name.", false),
                    "Company identity grounding returned an incomplete entity; deterministic research can continue.");
            }

            var temporaryId = TrimAndBound(entity.TemporaryId, MaxFieldCharacters);
            if (!temporaryIds.Add(temporaryId))
            {
                return Failure(
                    new AiFailure("invalid_response", "The grounding model returned duplicate entity identifiers.", false),
                    "Company identity grounding returned duplicate entities; deterministic research can continue.");
            }

            if (!TryParseEntityType(entity.EntityType, out var entityType) ||
                !TryParseConfidence(entity.Confidence, out var confidence))
            {
                return Failure(
                    new AiFailure("invalid_response", "The grounding model returned an unsupported entity classification.", false),
                    "Company identity grounding returned an unsupported classification; deterministic research can continue.");
            }

            var supportingIds = new List<Guid>();
            foreach (var candidateIdText in entity.SupportingCandidateIds ?? [])
            {
                if (!Guid.TryParse(candidateIdText, out var candidateId) || !candidateIds.Contains(candidateId))
                {
                    return Failure(
                        new AiFailure("invalid_candidate_reference", "The grounding model referenced an unknown discovery candidate.", false),
                        "Company identity grounding returned an unknown source reference; deterministic research can continue.");
                }

                if (!supportingIds.Contains(candidateId))
                {
                    supportingIds.Add(candidateId);
                }
            }

            if (entity.Recommended)
            {
                recommendedCount++;
            }

            entities.Add(new ResolvedResearchEntity(
                temporaryId,
                TrimAndBound(entity.DisplayName, MaxFieldCharacters),
                TrimNullable(entity.LegalName),
                TrimNullable(entity.Country),
                TrimNullable(entity.Website),
                TrimNullable(entity.OfficialDomain),
                entityType,
                TrimNullable(entity.RelationshipHint),
                confidence,
                TrimNullable(entity.Rationale, MaxRationaleCharacters),
                supportingIds,
                entity.Recommended));
        }

        if (recommendedCount > 1)
        {
            return Failure(
                new AiFailure("invalid_response", "The grounding model returned multiple recommended entities.", false),
                "Company identity grounding returned multiple recommendations; deterministic research can continue.");
        }

        var recommendedTemporaryId = TrimNullable(response.RecommendedTemporaryId);
        string? warning = null;
        if (recommendedTemporaryId is not null &&
            !temporaryIds.Contains(recommendedTemporaryId))
        {
            // A model can still identify useful distinct organizations while
            // making an invalid top-level recommendation reference. Dropping
            // the entire grounded result would hide the FPT/Viettel-style
            // family choices from the user. Preserve the bounded candidates,
            // remove only the bad default, and require explicit review.
            recommendedTemporaryId = null;
            warning = "Grounding returned no usable default target; review the possible organizations before continuing.";
        }

        recommendedTemporaryId ??= entities.SingleOrDefault(entity => entity.Recommended)?.TemporaryId;

        if (response.Ambiguous && entities.Count == 0)
        {
            return Failure(
                new AiFailure("invalid_response", "The grounding model marked the identity ambiguous without candidates.", false),
                "Company identity grounding returned no candidate entities; deterministic research can continue.");
        }

        var requiresSelection = response.Ambiguous ||
                                entities.Select(entity => entity.OfficialDomain ?? entity.Website ?? entity.DisplayName)
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .Take(2)
                                    .Count() > 1;

        return new IdentityResolutionResult(
            requiresSelection,
            recommendedTemporaryId,
            entities,
            warning);
    }

    private static AiEvidencePayload BuildEvidence(
        ResearchIdentityInput identity,
        IReadOnlyList<GroundingSourceCandidate> candidates)
    {
        var hints = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = TrimNullable(identity.Name),
            ["legalName"] = TrimNullable(identity.LegalName),
            ["website"] = TrimNullable(identity.Website),
            ["country"] = TrimNullable(identity.Country),
            ["registrationNumber"] = TrimNullable(identity.RegistrationNumber),
            ["headquarters"] = TrimNullable(identity.Headquarters),
            ["researchHint"] = TrimNullable(identity.ResearchHint)
        };

        var sources = candidates.Select(candidate => new AiEvidenceItem(
            candidate.CandidateId.ToString("D"),
            candidate.SourceKind.ToString(),
            TrimAndBound(candidate.Title ?? candidate.Domain, MaxFieldCharacters),
            TrimAndBound(candidate.Url, MaxFieldCharacters),
            BuildCandidateContent(candidate))).ToArray();

        return new AiEvidencePayload(hints, sources);
    }

    private static string BuildPrompt(
        ResearchIdentityInput identity,
        IReadOnlyList<GroundingSourceCandidate> candidates)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Resolve the real-world organization intended by the supplied identity hints.");
        builder.AppendLine("Use only the supplied identity hints and discovery candidate metadata.");
        builder.AppendLine("Do not use outside knowledge, and do not treat a search snippet as verified fact.");
        builder.AppendLine("Classify each plausible entity as parent_group, company, subsidiary, affiliate, brand, or unknown.");
        builder.AppendLine("Set supportingCandidateIds only to candidate IDs supplied below.");
        builder.AppendLine("Return a short user-facing rationale, never private chain-of-thought.");
        builder.AppendLine("Return only JSON matching the response schema.");
        builder.AppendLine();
        builder.AppendLine("IDENTITY HINTS:");
        builder.Append("Name: ").AppendLine(TrimAndBound(identity.Name, MaxFieldCharacters));
        builder.Append("Legal name: ").AppendLine(TrimNullable(identity.LegalName) ?? "null");
        builder.Append("Website: ").AppendLine(TrimNullable(identity.Website) ?? "null");
        builder.Append("Country: ").AppendLine(TrimNullable(identity.Country) ?? "null");
        builder.Append("Registration/tax ID: ").AppendLine(TrimNullable(identity.RegistrationNumber) ?? "null");
        builder.Append("Headquarters: ").AppendLine(TrimNullable(identity.Headquarters) ?? "null");
        builder.Append("Research hint: ").AppendLine(TrimNullable(identity.ResearchHint) ?? "null");
        builder.AppendLine();
        builder.AppendLine($"DISCOVERY CANDIDATES (bounded to {candidates.Count}):");

        foreach (var candidate in candidates)
        {
            builder.Append("CandidateId: ").AppendLine(candidate.CandidateId.ToString("D"));
            builder.Append("Title: ").AppendLine(TrimNullable(candidate.Title) ?? "null");
            builder.Append("URL: ").AppendLine(TrimAndBound(candidate.Url, MaxFieldCharacters));
            builder.Append("Domain: ").AppendLine(TrimAndBound(candidate.Domain, MaxFieldCharacters));
            builder.Append("SourceKind: ").AppendLine(candidate.SourceKind.ToString());
            builder.Append("SearchRank: ").AppendLine(candidate.SearchRank.ToString());
            builder.Append("OfficialDomain: ").AppendLine(candidate.OfficialDomain.ToString());
            builder.Append("Recommendation reasons: ").AppendLine(
                TrimAndBound(string.Join("; ", candidate.RecommendationReasons ?? []), MaxFieldCharacters));
            builder.Append("Snippet: ").AppendLine(TrimNullable(candidate.Snippet) ?? "null");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildCandidateContent(GroundingSourceCandidate candidate) =>
        $"Domain: {TrimAndBound(candidate.Domain, MaxFieldCharacters)}\n" +
        $"Search rank: {candidate.SearchRank}\n" +
        $"Official domain indicator: {candidate.OfficialDomain}\n" +
        $"Recommendation reasons: {TrimAndBound(string.Join("; ", candidate.RecommendationReasons ?? []), MaxFieldCharacters)}\n" +
        $"Snippet: {TrimNullable(candidate.Snippet) ?? "null"}";

    private static IdentityResolutionResult InvalidRequest(string message) => Failure(
        new AiFailure("invalid_request", message, false),
        "Company identity grounding could not start; deterministic research can continue.");

    private static IdentityResolutionResult Failure(AiFailure failure, string warning) =>
        new(false, null, [], warning, failure);

    private static string? TrimNullable(string? value, int maxLength = MaxFieldCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return TrimAndBound(value, maxLength);
    }

    private static string TrimAndBound(string value, int maxLength)
    {
        var normalized = value.Replace('\0', ' ').Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static bool TryParseEntityType(string? raw, out GroundedEntityType value)
    {
        value = raw?.Trim().ToLowerInvariant() switch
        {
            "parentgroup" or "parent_group" or "parent group" => GroundedEntityType.ParentGroup,
            "company" => GroundedEntityType.Company,
            "subsidiary" => GroundedEntityType.Subsidiary,
            "affiliate" => GroundedEntityType.Affiliate,
            "brand" => GroundedEntityType.Brand,
            "unknown" => GroundedEntityType.Unknown,
            _ => default
        };

        return raw?.Trim().ToLowerInvariant() is
            "parentgroup" or "parent_group" or "parent group" or
            "company" or "subsidiary" or "affiliate" or "brand" or "unknown";
    }

    private static bool TryParseConfidence(string? raw, out GroundingConfidence value)
    {
        value = raw?.Trim().ToLowerInvariant() switch
        {
            "low" => GroundingConfidence.Low,
            "medium" => GroundingConfidence.Medium,
            "high" => GroundingConfidence.High,
            _ => default
        };

        return raw?.Trim().ToLowerInvariant() is "low" or "medium" or "high";
    }

    private static JsonElement CreateResponseSchema()
    {
        using var document = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "ambiguous": { "type": "boolean" },
                "recommendedTemporaryId": { "type": "string", "nullable": true },
                "entities": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "temporaryId": { "type": "string" },
                      "displayName": { "type": "string" },
                      "legalName": { "type": "string", "nullable": true },
                      "country": { "type": "string", "nullable": true },
                      "website": { "type": "string", "nullable": true },
                      "officialDomain": { "type": "string", "nullable": true },
                      "entityType": { "type": "string", "enum": ["parent_group", "company", "subsidiary", "affiliate", "brand", "unknown"] },
                      "relationshipHint": { "type": "string", "nullable": true },
                      "confidence": { "type": "string", "enum": ["low", "medium", "high"] },
                      "rationale": { "type": "string", "nullable": true },
                      "supportingCandidateIds": { "type": "array", "items": { "type": "string" } },
                      "recommended": { "type": "boolean" }
                    }
                  }
                }
              }
            }
            """);

        return document.RootElement.Clone();
    }

    private sealed class GroundingResponseDto
    {
        public bool Ambiguous { get; set; }

        public string? RecommendedTemporaryId { get; set; }

        public List<GroundedEntityDto>? Entities { get; set; }
    }

    private sealed class GroundedEntityDto
    {
        public string? TemporaryId { get; set; }

        public string? DisplayName { get; set; }

        public string? LegalName { get; set; }

        public string? Country { get; set; }

        public string? Website { get; set; }

        public string? OfficialDomain { get; set; }

        public string? EntityType { get; set; }

        public string? RelationshipHint { get; set; }

        public string? Confidence { get; set; }

        public string? Rationale { get; set; }

        public List<string>? SupportingCandidateIds { get; set; }

        public bool Recommended { get; set; }
    }

    private const string SystemInstruction = """
        You are RAVEN's company identity grounding assistant.
        Ground only against the supplied identity hints and bounded public-search metadata.
        Do not invent company facts, use unsupplied world knowledge, or return hidden reasoning.
        Return only the requested structured JSON. Rationale must be brief and user-facing.
        """;
}
