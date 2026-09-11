namespace Raven.Api.Features.Companies;

/// <summary>Explicit confirmation payload for permanently deleting a company.</summary>
public sealed record DeleteCompanyRequest(bool Confirm);

/// <summary>Identifies the canonical and duplicate records for a merge preview.</summary>
public sealed record CompanyMergePreviewRequest(
    Guid CanonicalCompanyId,
    Guid DuplicateCompanyId);

/// <summary>Explicit confirmation payload for a company merge.</summary>
public sealed record CompanyMergeConfirmRequest(
    Guid CanonicalCompanyId,
    Guid DuplicateCompanyId,
    bool Confirm);

/// <summary>The lifecycle action outcome used by the API layer.</summary>
public enum CompanyDeleteOutcome
{
    NotFound,
    ConfirmationRequired,
    Deleted
}

/// <summary>Result of a permanent company deletion.</summary>
public sealed record CompanyDeleteResult(
    CompanyDeleteOutcome Outcome,
    int RelatedRecordsDeleted = 0);

/// <summary>The lifecycle outcome of a merge confirmation.</summary>
public enum CompanyMergeOutcome
{
    NotFound,
    Invalid,
    ConfirmationRequired,
    Merged
}

/// <summary>Counts and warnings shown before a destructive merge.</summary>
public sealed record CompanyMergePreviewResponse(
    Guid CanonicalCompanyId,
    Guid DuplicateCompanyId,
    string CanonicalCompanyName,
    string DuplicateCompanyName,
    int ResearchRuns,
    int ResearchCandidates,
    int ResearchEvents,
    int IdentityCandidates,
    int SourceDocuments,
    int DuplicateSourceDocumentsToReuse,
    int DuplicateSourceDocumentsToMove,
    int ProfileCandidates,
    int ProfileVersions,
    int ProfileEvidenceRows,
    int ProfileChanges,
    int DeepResearchRuns,
    int DeepResearchActivities,
    int SavedInvestigations,
    bool HasCanonicalMonitoring,
    bool HasDuplicateMonitoring,
    IReadOnlyList<string> Warnings);

/// <summary>Result of a confirmed company merge.</summary>
public sealed record CompanyMergeResult(
    CompanyMergeOutcome Outcome,
    CompanyResponse? CanonicalCompany = null,
    CompanyMergePreviewResponse? Preview = null,
    string? Error = null);

/// <summary>Lifecycle operations for reversible cleanup and user-confirmed merges.</summary>
public interface ICompanyLifecycleService
{
    Task<CompanyResponse?> ArchiveAsync(Guid companyId, CancellationToken cancellationToken);

    Task<CompanyResponse?> RestoreAsync(Guid companyId, CancellationToken cancellationToken);

    Task<CompanyDeleteResult> DeleteAsync(
        Guid companyId,
        bool confirm,
        CancellationToken cancellationToken);

    Task<CompanyMergePreviewResponse?> PreviewMergeAsync(
        CompanyMergePreviewRequest request,
        CancellationToken cancellationToken);

    Task<CompanyMergeResult> ConfirmMergeAsync(
        CompanyMergeConfirmRequest request,
        CancellationToken cancellationToken);
}
