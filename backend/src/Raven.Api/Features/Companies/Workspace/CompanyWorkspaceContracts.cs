using Raven.Api.Features.Profiles;
using Raven.Api.Features.Research.Coverage;

namespace Raven.Api.Features.Companies.Workspace;

/// <summary>
/// Deterministic health states used by the company workspace. Health is a
/// product signal, not a confidence score; unsupported profile facts remain
/// unknown.
/// </summary>
public enum CompanyHealthStatus
{
    Complete,
    Partial,
    Sparse,
    Unresearched,
    Stale,
    PendingUpdate,
    PossibleDuplicate,
    Archived
}

/// <summary>How two company records were deterministically grouped.</summary>
public enum DuplicateMatchType
{
    NameAndCountry,
    LegalNameAndCountry,
    WebsiteHost,
    RegistrationNumber
}

/// <summary>Review suggestions never contain mutation commands.</summary>
public enum WorkspaceReviewRecommendationKind
{
    PossibleDuplicate,
    PossibleAlias,
    SparseProfile,
    Unresearched,
    Stale,
    PendingUpdate
}

/// <summary>
/// Read-only input to workspace analysis. The integration layer can construct
/// this from EF entities without giving the review service a DbContext or a
/// mutation capability.
/// </summary>
public sealed record CompanyWorkspaceSnapshot(
    Company Company,
    CompanyProfileVersion? AcceptedProfile = null,
    IReadOnlyCollection<EvidenceCoverageItem>? AcceptedCoverage = null,
    int SourceDocumentCount = 0,
    int ResearchRunCount = 0,
    bool PendingUpdate = false,
    bool IsArchived = false)
{
    public IReadOnlyCollection<EvidenceCoverageItem> Coverage => AcceptedCoverage ?? [];

    public static CompanyWorkspaceSnapshot FromCompany(
        Company company,
        CompanyProfileVersion? acceptedProfile = null,
        IReadOnlyCollection<EvidenceCoverageItem>? acceptedCoverage = null,
        int sourceDocumentCount = 0,
        int researchRunCount = 0,
        bool pendingUpdate = false,
        bool isArchived = false)
    {
        ArgumentNullException.ThrowIfNull(company);
        return new(
            company,
            acceptedProfile,
            acceptedCoverage,
            sourceDocumentCount,
            researchRunCount,
            pendingUpdate,
            isArchived);
    }
}

public sealed class CompanyHealthOptions
{
    /// <summary>Profiles older than this are marked stale unless refreshed.</summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromDays(90);

    /// <summary>
    /// The minimum number of covered targets that makes a profile partial rather
    /// than sparse. The value is intentionally deterministic and configurable in
    /// code; it is not presented to users as a confidence score.
    /// </summary>
    public int PartialMinimumCoveredTargets { get; init; } = 3;

    public void Validate()
    {
        if (StaleAfter <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(StaleAfter), "StaleAfter must be greater than zero.");
        }

        if (PartialMinimumCoveredTargets < 1 || PartialMinimumCoveredTargets >= ResearchTargetCatalog.All.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PartialMinimumCoveredTargets),
                $"Partial coverage threshold must be between 1 and {ResearchTargetCatalog.All.Count - 1}.");
        }
    }
}

public static class ResearchTargetCatalog
{
    public static IReadOnlyList<ResearchTarget> All { get; } =
    [
        ResearchTarget.LegalIdentity,
        ResearchTarget.TaxRegistration,
        ResearchTarget.FoundedHistory,
        ResearchTarget.Industry,
        ResearchTarget.EmployeeScale,
        ResearchTarget.ProductsServices,
        ResearchTarget.Markets,
        ResearchTarget.Leadership,
        ResearchTarget.Locations
    ];
}

public sealed record CompanyHealthAssessment(
    Guid CompanyId,
    CompanyHealthStatus Status,
    CompanyHealthStatus BaseStatus,
    IReadOnlyList<CompanyHealthStatus> Flags,
    bool HasAcceptedProfile,
    int CoveredTargetCount,
    int RequiredTargetCount,
    int SourceDocumentCount,
    int ResearchRunCount,
    DateTimeOffset? LastResearchedAt,
    IReadOnlyList<ResearchTarget> MissingTargets,
    IReadOnlyList<string> Reasons);

public sealed record CompanyWorkspaceIdentity(
    Guid CompanyId,
    string Name,
    string? LegalName,
    string? Website,
    string? Country,
    string? RegistrationNumber);

public sealed record CompanyDuplicateGroup(
    string GroupId,
    DuplicateMatchType StrongestMatch,
    IReadOnlyList<DuplicateMatchType> MatchTypes,
    IReadOnlyList<CompanyWorkspaceIdentity> Members,
    string Rationale);

public sealed record WorkspaceReviewRequest(
    IReadOnlyCollection<CompanyWorkspaceSnapshot> Companies,
    bool IncludeArchived = false,
    DateTimeOffset? AsOf = null,
    bool IncludeAiSuggestions = true);

public sealed record WorkspaceReviewCompany(
    Guid CompanyId,
    string Name,
    CompanyHealthAssessment Health);

public sealed record WorkspaceReviewRecommendation(
    WorkspaceReviewRecommendationKind Kind,
    Guid CompanyId,
    string Title,
    string Summary,
    IReadOnlyList<Guid> RelatedCompanyIds,
    IReadOnlyList<string> Reasons,
    CompanyHealthStatus? HealthStatus = null,
    bool IsAiGenerated = false);

public sealed record WorkspaceReviewResponse(
    IReadOnlyList<WorkspaceReviewCompany> Companies,
    IReadOnlyList<CompanyDuplicateGroup> DuplicateGroups,
    IReadOnlyList<WorkspaceReviewRecommendation> Recommendations,
    bool AiUsed = false,
    string? AiWarning = null);

/// <summary>Read-only grouping seam; implementations must not mutate companies.</summary>
public interface ICompanyDuplicateGroupingService
{
    IReadOnlyList<CompanyDuplicateGroup> Group(
        IReadOnlyCollection<CompanyWorkspaceSnapshot> companies);
}

/// <summary>Read-only health evaluator used by list and workspace review callers.</summary>
public interface ICompanyHealthEvaluator
{
    CompanyHealthAssessment Evaluate(
        CompanyWorkspaceSnapshot snapshot,
        bool possibleDuplicate = false,
        DateTimeOffset? asOf = null);
}

/// <summary>
/// Optional AI boundary. It may propose review cards, but has no operation for
/// archive, delete, merge, or identity mutation.
/// </summary>
public interface IWorkspaceReviewAiProvider
{
    Task<WorkspaceReviewAiResult> ReviewAsync(
        WorkspaceReviewAiRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record WorkspaceReviewAiRequest(
    IReadOnlyList<WorkspaceReviewCompany> Companies,
    IReadOnlyList<CompanyDuplicateGroup> DuplicateGroups,
    IReadOnlyList<WorkspaceReviewRecommendation> DeterministicRecommendations);

public sealed record WorkspaceReviewAiSuggestion(
    WorkspaceReviewRecommendationKind Kind,
    Guid CompanyId,
    string Title,
    string Summary,
    IReadOnlyList<Guid>? RelatedCompanyIds = null,
    IReadOnlyList<string>? Reasons = null);

public sealed record WorkspaceReviewAiResult(
    IReadOnlyList<WorkspaceReviewAiSuggestion> Suggestions,
    string? Warning = null);

public interface ICompanyWorkspaceReviewService
{
    WorkspaceReviewResponse Review(WorkspaceReviewRequest request);

    Task<WorkspaceReviewResponse> ReviewAsync(
        WorkspaceReviewRequest request,
        CancellationToken cancellationToken = default);
}
