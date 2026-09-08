namespace Raven.Api.Features.Companies;

public interface ICompanyService
{
    Task<IReadOnlyList<CompanyResponse>> ListAsync(CancellationToken cancellationToken);

    Task<CompanyResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<CompanyResponse> CreateAsync(
        string name,
        string? website,
        string? country,
        CancellationToken cancellationToken);
}
