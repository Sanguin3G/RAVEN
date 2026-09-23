using System.Text.Json;
using Raven.Api.Features.ManagedResearch;

namespace Raven.Api.Features.Research.SavedArtifacts;

public enum InvestigationPurpose { GeneralResearch, ProfileImprovement }
public enum InvestigationMaterialKind { Saved, Managed }
public enum InvestigationStatus { Running, Ready, Done, Failed }

public static class InvestigationTopics
{
    public static readonly string[] All =
    [
        "Legal Identity", "Tax / Registration", "Founded / History", "Industry", "Employee Scale",
        "Products & Services", "Markets", "Leadership", "Locations", "Financial & Performance",
        "Talent & Hiring", "Business Model", "Competitive Landscape", "Strategy & Partnerships",
        "Regulatory", "Supply Chain"
    ];

    private static readonly (string Topic, string[] Terms)[] Rules =
    [
        ("Legal Identity", ["legal", "identity", "incorporat"]),
        ("Tax / Registration", ["tax", "registr", "business number"]),
        ("Founded / History", ["found", "history", "establish"]),
        ("Industry", ["industry", "sector"]),
        ("Employee Scale", ["headcount", "employee count", "workforce size"]),
        ("Products & Services", ["product", "service", "offering"]),
        ("Markets", ["market", "expansion", "geograph"]),
        ("Leadership", ["leadership", "executive", "ceo"]),
        ("Locations", ["location", "office", "headquarter"]),
        ("Financial & Performance", ["revenue", "profit", "margin", "funding", "financial"]),
        ("Talent & Hiring", ["hiring", "recruit", "talent", "job opening"]),
        ("Business Model", ["business model", "customer segment", "revenue model", "channel"]),
        ("Competitive Landscape", ["competitor", "competition", "competitive"]),
        ("Strategy & Partnerships", ["strategy", "strategic", "partner", "alliance"]),
        ("Regulatory", ["regulat", "compliance", "legislation"]),
        ("Supply Chain", ["supply chain", "supplier", "procurement"])
    ];

    public static string[] Suggest(string text) => Rules
        .Where(rule => rule.Terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)))
        .Select(rule => rule.Topic).ToArray();

    public static string[] Parse(string? json)
    {
        try { return (JsonSerializer.Deserialize<string[]>(json ?? "[]") ?? []).Where(All.Contains).Distinct().ToArray(); }
        catch (JsonException) { return []; }
    }
}

public sealed class InvestigationReviewState
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CompanyId { get; init; }
    public InvestigationMaterialKind MaterialKind { get; init; }
    public Guid MaterialId { get; init; }
    public DateTimeOffset? DoneThrough { get; set; }
    public DateTimeOffset? DoneAt { get; set; }
    public DateTimeOffset? ReopenedAt { get; set; }
    public Guid? AppliedProfileVersionId { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }

    public bool IsDone(DateTimeOffset materialUpdatedAt) => DoneThrough >= materialUpdatedAt &&
        (ReopenedAt is null || DoneAt > ReopenedAt);
}

public sealed record InvestigationResponse(
    Guid Id, Guid CompanyId, InvestigationMaterialKind MaterialKind, Guid? MaterialId,
    string Title, string Objective, string Summary, string Origin, InvestigationPurpose Purpose,
    IReadOnlyList<string> Topics, InvestigationStatus Status, DateTimeOffset MaterialUpdatedAt,
    DateTimeOffset? DoneThrough, DateTimeOffset? DoneAt, Guid? AppliedProfileVersionId,
    DateTimeOffset? AppliedAt, bool ProfileImprovementLocked, string? Provider,
    IReadOnlyList<ResearchClaim> Claims, IReadOnlyList<ResearchSourceLead> SourceLeads,
    IReadOnlyList<string> Uncertainties, string? RawMaterial, string? RawResponse,
    IReadOnlyList<Guid> BriefingIds);

public static class InvestigationPurposeMapping
{
    public static InvestigationPurpose FromManaged(ManagedResearchPurpose purpose) =>
        purpose == ManagedResearchPurpose.ProfileImprovement
            ? InvestigationPurpose.ProfileImprovement : InvestigationPurpose.GeneralResearch;
}
