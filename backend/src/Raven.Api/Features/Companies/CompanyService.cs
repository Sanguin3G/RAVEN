using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;

namespace Raven.Api.Features.Companies;

public sealed class CompanyService(RavenDbContext dbContext) : ICompanyService
{
    public async Task<IReadOnlyList<CompanyResponse>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Companies
            .AsNoTracking()
            .OrderBy(company => company.Name)
            .Select(company => ToResponse(company))
            .ToListAsync(cancellationToken);

    public async Task<CompanyResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var company = await dbContext.Companies
            .AsNoTracking()
            .SingleOrDefaultAsync(company => company.Id == id, cancellationToken);

        return company is null ? null : ToResponse(company);
    }

    public async Task<CompanyResponse> CreateAsync(
        string name,
        string? website,
        string? country,
        CancellationToken cancellationToken)
    {
        var company = new Company
        {
            Name = name.Trim(),
            Website = NormalizeOptionalValue(website),
            Country = NormalizeOptionalValue(country)
        };

        dbContext.Companies.Add(company);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(company);
    }

    private static CompanyResponse ToResponse(Company company) =>
        new(
            company.Id,
            company.Name,
            company.Website,
            company.Country,
            company.CreatedAt,
            company.UpdatedAt);

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
