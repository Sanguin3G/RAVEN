using Raven.Api.Features.Research.Sources;

namespace Raven.Api.Features.Research.Coverage;

/// <summary>
/// The evidence objective for a research operation. These targets describe the
/// kind of public evidence RAVEN is seeking; they are deliberately not raw
/// CompanyProfile field paths.
/// </summary>
public enum ResearchTarget
{
    LegalIdentity,
    TaxRegistration,
    FoundedHistory,
    Industry,
    EmployeeScale,
    ProductsServices,
    Markets,
    Leadership,
    Locations
}

/// <summary>
/// A qualitative evidence state. It intentionally avoids fabricated numeric
/// confidence scores.
/// </summary>
public enum CoverageLevel
{
    Missing,
    Weak,
    Supported,
    Strong
}

/// <summary>
/// A persisted research-run intent. Targeted enrichment reuses the normal
/// run/candidate/acquisition workflow rather than introducing a parallel
/// research engine.
/// </summary>
public enum ResearchMode
{
    Initial,
    Refresh,
    Monitoring,
    TargetedEnrichment
}

/// <summary>
/// Explainable coverage for one research target. Source count is informational;
/// the level is derived from target-specific authority and usable evidence.
/// </summary>
public sealed record EvidenceCoverageItem(
    ResearchTarget Target,
    CoverageLevel Level,
    int SupportingSourceCount,
    SourceKind? StrongestSourceKind,
    IReadOnlyList<string> Reasons);

public sealed record EvidenceCoverageResponse(
    Guid CompanyId,
    Guid? ResearchRunId,
    IReadOnlyList<EvidenceCoverageItem> Items,
    bool BudgetExhausted = false);

public interface IEvidenceCoverageEvaluator
{
    EvidenceCoverageResponse Evaluate(
        Guid companyId,
        Guid? researchRunId,
        IReadOnlyCollection<SourceDocument> sources,
        IReadOnlyCollection<ResearchTarget>? requestedTargets = null,
        bool budgetExhausted = false);
}
