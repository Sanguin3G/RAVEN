namespace Raven.Api.Features.Research;

public interface IResearchCompanyService
{
    Task<ResearchRunResponse?> ResearchAsync(Guid companyId, CancellationToken cancellationToken);
    Task<ResearchRunResponse?> DiscoverAsync(
        Guid companyId,
        DiscoverResearchRequest? request,
        CancellationToken cancellationToken);

    Task<ResearchRunResponse?> AcquireAsync(
        Guid researchRunId,
        AcquireResearchCandidatesRequest request,
        CancellationToken cancellationToken);

    Task<ResearchRunResponse?> GetRunAsync(Guid researchRunId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ResearchRunResponse>> ListRunsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ResearchCandidateResponse>?> ListCandidatesAsync(
        Guid researchRunId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ResearchIdentityCandidateResponse>?> ListIdentityCandidatesAsync(
        Guid researchRunId,
        CancellationToken cancellationToken);

    Task<ResearchRunResponse?> SelectIdentityAsync(
        Guid researchRunId,
        SelectResearchIdentityRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SourceDocumentResponse>?> ListSourcesAsync(Guid companyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SourceDocumentResponse>?> ListRunSourcesAsync(
        Guid researchRunId,
        CancellationToken cancellationToken);

    Task<SourceDocumentDetailResponse?> GetSourceAsync(Guid sourceId, CancellationToken cancellationToken);
}
