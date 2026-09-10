namespace Raven.Api.Features.Research;

public interface IResearchCompanyService
{
    Task<ResearchRunResponse?> ResearchAsync(Guid companyId, CancellationToken cancellationToken);
    Task<ResearchRunResponse?> GetRunAsync(Guid researchRunId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ResearchRunResponse>> ListRunsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SourceDocumentResponse>?> ListSourcesAsync(Guid companyId, CancellationToken cancellationToken);
    Task<SourceDocumentDetailResponse?> GetSourceAsync(Guid sourceId, CancellationToken cancellationToken);
}
