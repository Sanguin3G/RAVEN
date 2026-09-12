using System.Text.Json;
using Raven.Api.Features.Ai;

namespace Raven.Api.Features.Research.Identity;

/// <summary>One bounded, evidence-free call that describes topology only.</summary>
public sealed class GeminiKnowledgeIdentityResolver(IAiModelProvider aiProvider) : IIdentityKnowledgeResolver
{
    private const string Model = "gemini-3.5-flash-lite";
    private const string Template = "identity-topology-v2";

    public async Task<IdentityTopologyResponse> ResolveAsync(IdentityResolutionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return Failure("invalid_request", "A company name is required.");
        var modelResult = await aiProvider.GenerateStructuredAsync(new AiModelRequest(Model, SystemInstruction, Prompt(request), Template,
            new AiEvidencePayload(new Dictionary<string, string?> { ["name"] = Text(request.Name), ["legalName"] = Text(request.LegalName), ["website"] = Text(request.Website), ["country"] = Text(request.Country), ["registrationNumber"] = Text(request.RegistrationNumber), ["headquarters"] = Text(request.Headquarters) }, []), Schema), cancellationToken);
        if (!modelResult.Succeeded || !modelResult.StructuredJson.HasValue)
            return Failure(modelResult.Failure?.Code ?? "invalid_response", "RAVEN couldn't confidently resolve this organization right now.", modelResult);
        try
        {
            var raw = JsonSerializer.Deserialize<Dto>(modelResult.StructuredJson.Value.GetRawText(), JsonOptions);
            if (raw is null || !TryInterpretation(raw.Interpretation, out var interpretation)) return Failure("invalid_response", "RAVEN couldn't confidently resolve this organization right now.", modelResult);
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entities = new List<IdentityOption>();
            foreach (var item in raw.Candidates ?? [])
            {
                if (entities.Count == 7 || string.IsNullOrWhiteSpace(item.TemporaryId) || string.IsNullOrWhiteSpace(item.DisplayName) || !ids.Add(item.TemporaryId.Trim()) ||
                    !TryEntity(item.EntityType, out var type) || !TryRelationship(item.RelationshipToQuery, out var relation) || !TryConfidence(item.Confidence, out var confidence)) continue;
                entities.Add(new IdentityOption(item.TemporaryId.Trim(), Bound(item.DisplayName, 160), Text(item.LegalName), Text(item.Country), Text(item.Region), Domain(item.OfficialDomain), type, Text(item.ParentTemporaryId), relation, confidence, Text(item.ShortDescription, 240)));
            }
            var hints = (raw.RequestedHints ?? []).Select(ParseHint).Where(x => x.HasValue).Select(x => x!.Value).Distinct().Take(3).ToArray();
            return new IdentityTopologyResponse(interpretation, entities, hints, Text(raw.Message, 400), null, modelResult.Model, Template);
        }
        catch (JsonException) { return Failure("invalid_response", "RAVEN couldn't confidently resolve this organization right now.", modelResult); }
    }

    private static IdentityTopologyResponse Failure(string code, string message, AiModelResult? result = null) => new(IdentityQueryInterpretation.Unknown, [], [IdentityHintKind.Country, IdentityHintKind.Website], message, message, result?.Model, Template, new AiFailureInfo(code, message, result?.Failure?.Retryable ?? false, result?.Failure?.HttpStatus));
    private static string Prompt(IdentityResolutionRequest x) => $"""Describe company identity topology from these hints. You identify possible organizations and relationships; never choose user intent or return workflow status. Use existing knowledge only: no browsing, search, crawl, tools, or sources. A generic umbrella/group name with meaningful family members is CorporateFamilyShorthand even if its parent is canonical; return parent then up to six relevant children. Only use SpecificEntity for one specifically named organization. NameCollision is for unrelated plausible matches; Unknown when unsafe. Do not repeat the query as an entity without recognized context. Domains/legal names are optional navigation hints, not evidence. Return JSON only. Name: {Text(x.Name)} Country: {Text(x.Country) ?? "null"} Legal name: {Text(x.LegalName) ?? "null"} Website: {Text(x.Website) ?? "null"}""";
    private static string? Text(string? x, int max = 160) => string.IsNullOrWhiteSpace(x) ? null : Bound(x, max);
    private static string Bound(string x, int max) { var t = x.Replace('\0', ' ').Trim(); return t.Length <= max ? t : t[..max]; }
    private static string? Domain(string? x) { if (string.IsNullOrWhiteSpace(x)) return null; var v = x.Trim(); return Uri.TryCreate(v.Contains("://", StringComparison.Ordinal) ? v : $"https://{v}", UriKind.Absolute, out var u) && u.Scheme is "http" or "https" ? u.Host.TrimEnd('.').ToLowerInvariant() : null; }
    private static bool TryInterpretation(string? x, out IdentityQueryInterpretation v) => Enum.TryParse(x, true, out v);
    private static bool TryEntity(string? x, out IdentityEntityType v) => Enum.TryParse(x, true, out v);
    private static bool TryRelationship(string? x, out IdentityRelationshipToQuery v) => Enum.TryParse(x, true, out v);
    private static bool TryConfidence(string? x, out IdentityConfidence v) => Enum.TryParse(x, true, out v);
    private static IdentityHintKind? ParseHint(string? x) => Enum.TryParse<IdentityHintKind>(x, true, out var v) ? v : null;

    private sealed class Dto { public string? Interpretation { get; set; } public List<Item>? Candidates { get; set; } public List<string>? RequestedHints { get; set; } public string? Message { get; set; } }
    private sealed class Item { public string? TemporaryId { get; set; } public string? DisplayName { get; set; } public string? LegalName { get; set; } public string? Country { get; set; } public string? Region { get; set; } public string? OfficialDomain { get; set; } public string? EntityType { get; set; } public string? ParentTemporaryId { get; set; } public string? RelationshipToQuery { get; set; } public string? Confidence { get; set; } public string? ShortDescription { get; set; } }
    private const string SystemInstruction = "RAVEN uses your output as non-evidentiary identity navigation. Describe topology only. Never decide what the user intended; return only structured JSON and no hidden reasoning.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private static readonly JsonElement Schema = JsonDocument.Parse("""{"type":"object","properties":{"interpretation":{"type":"string","enum":["SpecificEntity","CorporateFamilyShorthand","NameCollision","Unknown"]},"candidates":{"type":"array","maxItems":7,"items":{"type":"object","properties":{"temporaryId":{"type":"string"},"displayName":{"type":"string"},"entityType":{"type":"string","enum":["ParentGroup","Company","Subsidiary","Affiliate","Brand","Unknown"]},"relationshipToQuery":{"type":"string","enum":["Exact","Alias","Parent","Subsidiary","SimilarName","Possible"]},"confidence":{"type":"string","enum":["High","Medium","Low"]}}}},"requestedHints":{"type":"array","maxItems":3,"items":{"type":"string","enum":["Country","Website","LegalName","RegistrationNumber","Headquarters","Region"]}},"message":{"type":"string"}},"required":["interpretation","candidates","requestedHints"]}""").RootElement.Clone();
}
