using System.Text.Json;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Settings;

namespace Raven.Api.Features.Research.Briefings;

/// <summary>Synthesizes only selected, persisted Investigation snapshots.</summary>
public sealed class BriefingGenerator(IAiModelProvider ai, IResearchSettingsService settings)
{
    private const string PromptVersion = "research-briefing-v1";
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "sections": { "type": "array", "items": { "type": "object",
              "properties": {
                "title": { "type": "string" },
                "items": { "type": "array", "items": { "type": "string" } },
                "sourceInvestigationIds": { "type": "array", "items": { "type": "string" } }
              }, "required": ["title", "items", "sourceInvestigationIds"] } }
          }, "required": ["sections"]
        }
        """).RootElement.Clone();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<BriefingSection>> GenerateAsync(
        string template, string objective, IReadOnlyList<BriefingSourceSnapshot> sources, CancellationToken ct)
    {
        var configured = await settings.GetAsync(ct);
        var titles = BriefingTemplates.Sections[template];
        var input = sources.Select(source => new
        {
            id = source.InvestigationId,
            source.Title,
            source.Origin,
            source.Purpose,
            source.MaterialUpdatedAt,
            source.Summary,
            claims = source.Claims.Take(30).Select(claim => new { claim.Field, claim.Statement }),
            uncertainties = source.Uncertainties.Take(30),
            rawMaterial = Bound(source.RawMaterial, 8_000)
        });
        var prompt = $"Template: {template}\nObjective: {objective}\nRequired sections: {string.Join(" | ", titles)}\nSelected Investigation material:\n{JsonSerializer.Serialize(input, JsonOptions)}";
        var result = await ai.GenerateStructuredAsync(new AiModelRequest(
            configured.DeepResearchModel,
            "Create a concise thematic Briefing only from the supplied Investigation material. Do not search, invent facts, resolve contradictions without support, or describe this as accepted Company Profile truth. Preserve uncertainty. Use exactly the requested sections. For each section cite only Investigation IDs supplied in the input. Empty sections are allowed when material is insufficient.",
            prompt, PromptVersion, AiEvidencePayload.Empty, Schema), ct);
        if (!result.Succeeded || result.StructuredJson is not { } json)
            throw new InvalidOperationException(result.Failure?.Message ?? "Briefing generation failed.");
        GeneratedEnvelope? generated;
        try { generated = JsonSerializer.Deserialize<GeneratedEnvelope>(json.GetRawText(), JsonOptions); }
        catch (JsonException) { throw new InvalidOperationException("Briefing generation returned invalid structured content."); }
        var allowedIds = sources.Select(source => source.InvestigationId).ToHashSet();
        var sections = titles.Select(title =>
        {
            var value = generated?.Sections?.FirstOrDefault(section => string.Equals(section.Title, title, StringComparison.OrdinalIgnoreCase));
            return new BriefingSection(title, title,
                (value?.Items ?? []).Where(item => !string.IsNullOrWhiteSpace(item)).Take(8).Select(item => Bound(item.Trim(), 800)!).ToArray(),
                (value?.SourceInvestigationIds ?? []).Where(allowedIds.Contains).Distinct().ToArray());
        }).ToArray();
        if (sections.All(section => section.Items.Count == 0))
            throw new InvalidOperationException("Briefing generation returned no usable findings.");
        return sections;
    }

    private static string? Bound(string? value, int length) => value is { Length: > 0 }
        ? value.Length <= length ? value : value[..length] : value;

    private sealed record GeneratedEnvelope(IReadOnlyList<GeneratedSection>? Sections);
    private sealed record GeneratedSection(string Title, IReadOnlyList<string>? Items, IReadOnlyList<Guid>? SourceInvestigationIds);
}
