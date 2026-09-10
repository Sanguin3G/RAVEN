namespace Raven.Api.Features.Companies;

public interface ICompanyService
{
    Task<IReadOnlyList<CompanyResponse>> ListAsync(CancellationToken cancellationToken);

    Task<CompanyResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<CompanyResponse> CreateAsync(
        string name,
        string? website,
        string? country,
        string? legalName,
        string? registrationNumber,
        string? headquarters,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CompanyMatchResponse>> FindMatchesAsync(
        CompanyMatchRequest request,
        CancellationToken cancellationToken);
}
