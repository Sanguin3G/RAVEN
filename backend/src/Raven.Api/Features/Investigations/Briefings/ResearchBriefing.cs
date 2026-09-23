using Raven.Api.Features.Research.SavedArtifacts;

namespace Raven.Api.Features.Research.Briefings;

public sealed class ResearchBriefing
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CompanyId { get; init; }
    public required string Title { get; set; }
    public required string Template { get; set; }
    public required string Objective { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ArchivedAt { get; set; }
}

public sealed class ResearchBriefingVersion
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BriefingId { get; init; }
    public int VersionNumber { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    public DateTimeOffset ResearchThrough { get; init; }
    public required string Title { get; init; }
    public required string Template { get; init; }
    public required string Objective { get; init; }
    public required string SectionsJson { get; init; }
    public required string SourcesJson { get; init; }
}

public sealed record BriefingSection(string Key, string Title, IReadOnlyList<string> Items, IReadOnlyList<Guid> SourceInvestigationIds);

public sealed record BriefingSourceSnapshot(
    Guid InvestigationId, InvestigationMaterialKind MaterialKind, Guid? MaterialId,
    string Title, string Origin, InvestigationPurpose Purpose, IReadOnlyList<string> Topics,
    DateTimeOffset MaterialUpdatedAt, string Summary, IReadOnlyList<ResearchClaim> Claims,
    IReadOnlyList<ResearchSourceLead> SourceLeads, IReadOnlyList<string> Uncertainties,
    string? RawMaterial);

public sealed record BriefingVersionResponse(
    Guid Id, int VersionNumber, DateTimeOffset GeneratedAt, DateTimeOffset ResearchThrough,
    string Title, string Template, string Objective, IReadOnlyList<BriefingSection> Sections,
    IReadOnlyList<BriefingSourceSnapshot> Sources);

public sealed record BriefingResponse(
    Guid Id, Guid CompanyId, string Title, string Template, string Objective,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ArchivedAt,
    BriefingVersionResponse CurrentVersion, int VersionCount, int NewerRelevantCount);

public sealed record BriefingListItemResponse(
    Guid Id, string Title, string Template, DateTimeOffset GeneratedAt,
    DateTimeOffset ResearchThrough, int VersionNumber, int SourceCount, int NewerRelevantCount);

public sealed record CreateBriefingRequest(
    string Title, string Template, string Objective, IReadOnlyList<Guid> InvestigationIds);

public sealed record UpdateBriefingRequest(
    IReadOnlyList<Guid> NewInvestigationIds, string? Title = null, string? Template = null,
    string? Objective = null);

public sealed record BriefingChangeResponse(
    int FromVersion, int ToVersion, IReadOnlyList<string> NewMaterial,
    IReadOnlyList<string> ChangedMaterial, IReadOnlyList<string> RemovedMaterial,
    IReadOnlyList<string> NewUncertainties);

public static class BriefingTemplates
{
    public static readonly IReadOnlyDictionary<string, string[]> Sections = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Financial & Performance"] = ["Key takeaways", "Key metrics", "Revenue / growth", "Profitability", "Funding / capital", "Recent developments", "Risks / uncertainties", "Open questions"],
        ["Talent & Hiring"] = ["Key takeaways", "Hiring signals", "Roles / capabilities", "Geographic activity", "Recent developments", "Uncertainties", "Open questions"],
        ["Business Model"] = ["Value proposition", "Customer segments", "Revenue model", "Channels", "Key partnerships", "Operating characteristics", "Recent changes", "Open questions"],
        ["Competitive Landscape"] = ["Key takeaways", "Competitors", "Differentiation", "Market position", "Recent developments", "Uncertainties", "Open questions"],
        ["Markets & Expansion"] = ["Key takeaways", "Markets", "Expansion signals", "Geographic activity", "Recent developments", "Uncertainties", "Open questions"],
        ["Partnerships"] = ["Key takeaways", "Partners", "Relationship signals", "Recent developments", "Uncertainties", "Open questions"],
        ["Regulatory"] = ["Key takeaways", "Applicable requirements", "Regulatory developments", "Risks / uncertainties", "Open questions"],
        ["Supply Chain"] = ["Key takeaways", "Suppliers", "Operating signals", "Recent developments", "Risks / uncertainties", "Open questions"],
        ["Custom"] = ["Key takeaways", "Findings", "Recent developments", "Uncertainties", "Open questions"]
    };

    public static readonly IReadOnlyDictionary<string, string[]> Topics = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Financial & Performance"] = ["Financial & Performance"],
        ["Talent & Hiring"] = ["Talent & Hiring", "Employee Scale"],
        ["Business Model"] = ["Business Model", "Products & Services", "Markets"],
        ["Competitive Landscape"] = ["Competitive Landscape", "Markets"],
        ["Markets & Expansion"] = ["Markets", "Locations"],
        ["Partnerships"] = ["Strategy & Partnerships"],
        ["Regulatory"] = ["Regulatory", "Tax / Registration"],
        ["Supply Chain"] = ["Supply Chain"],
        ["Custom"] = []
    };

    public static string DefaultObjective(string template)
    {
        if (!Sections.TryGetValue(template, out var sections))
        {
            throw new ArgumentException("Choose a supported Briefing template.", nameof(template));
        }

        return $"Synthesize the selected research into a {template} Briefing covering {string.Join(", ", sections)}. " +
               "Preserve uncertainty and cite only the selected Investigations.";
    }
}
