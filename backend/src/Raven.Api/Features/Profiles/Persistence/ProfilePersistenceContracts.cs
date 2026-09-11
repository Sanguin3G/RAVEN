namespace Raven.Api.Features.Profiles.Persistence;

/// <summary>
/// Stores model-generated profile candidates separately from accepted dossier
/// versions. Implementations must treat the candidate and its evidence as
/// server-owned data; callers never submit a full profile payload at confirm
/// time.
/// </summary>
public interface ICompanyProfilePersistenceService
{
    Task<CompanyProfileCandidate?> SaveCandidateAsync(
        CompanyProfileCandidate candidate,
        CancellationToken cancellationToken = default);

    Task<CompanyProfileCandidate?> GetCandidateAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default);

    Task<CompanyProfileVersion?> ConfirmCandidateAsync(
        Guid candidateId,
        CancellationToken cancellationToken = default);

    Task<CompanyProfileVersion?> GetCurrentProfileAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompanyProfileVersion>> ListProfileVersionsAsync(
        Guid companyId,
        CancellationToken cancellationToken = default);

    Task<CompanyProfileVersion?> GetProfileVersionAsync(
        Guid companyId,
        int version,
        CancellationToken cancellationToken = default);
}
