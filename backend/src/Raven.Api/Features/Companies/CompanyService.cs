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
        string? legalName,
        string? registrationNumber,
        string? headquarters,
        CancellationToken cancellationToken)
    {
        var company = new Company
        {
            Name = name.Trim(),
            LegalName = NormalizeOptionalValue(legalName),
            RegistrationNumber = NormalizeOptionalValue(registrationNumber),
            Website = NormalizeOptionalValue(website),
            Country = NormalizeOptionalValue(country),
            Headquarters = NormalizeOptionalValue(headquarters)
        };

        dbContext.Companies.Add(company);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToResponse(company);
    }

    public async Task<IReadOnlyList<CompanyMatchResponse>> FindMatchesAsync(
        CompanyMatchRequest request,
        CancellationToken cancellationToken)
    {
        var identity = CompanyIdentityNormalizer.From(request);
        if (!identity.HasMatchableValue)
        {
            return [];
        }

        var companies = await dbContext.Companies
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return companies
            .Select(company => Match(company, identity))
            .Where(match => match is not null)
            .Select(match => match!)
            .OrderByDescending(match => MatchStrengthRank(match.MatchStrength))
            .ThenBy(match => match.Company.Name)
            .ThenBy(match => match.Company.Id)
            .ToList();
    }

    private static CompanyMatchResponse? Match(Company company, CompanyIdentity identity)
    {
        var companyRegistration = CompanyIdentityNormalizer.NormalizeRegistration(company.RegistrationNumber);
        if (identity.RegistrationNumber is not null && identity.RegistrationNumber == companyRegistration)
        {
            return new CompanyMatchResponse(
                ToResponse(company),
                CompanyMatchStrength.Exact,
                "Exact registration or tax ID match.");
        }

        var companyWebsiteHost = CompanyIdentityNormalizer.NormalizeWebsiteHost(company.Website);
        if (identity.WebsiteHost is not null && identity.WebsiteHost == companyWebsiteHost)
        {
            return new CompanyMatchResponse(
                ToResponse(company),
                CompanyMatchStrength.VeryStrong,
                "Exact website host match.");
        }

        var companyLegalName = CompanyIdentityNormalizer.NormalizeName(company.LegalName);
        if (identity.LegalName is not null && identity.LegalName == companyLegalName)
        {
            if (identity.Country is null || identity.Country == CompanyIdentityNormalizer.NormalizeName(company.Country))
            {
                return new CompanyMatchResponse(
                    ToResponse(company),
                    identity.Country is null ? CompanyMatchStrength.Weak : CompanyMatchStrength.Strong,
                    identity.Country is null
                        ? "Normalized legal name match; country was not supplied."
                        : "Normalized legal name and country match.");
            }
        }

        var companyName = CompanyIdentityNormalizer.NormalizeName(company.Name);
        if (identity.Name is not null && identity.Name == companyName &&
            (identity.Country is null || identity.Country == CompanyIdentityNormalizer.NormalizeName(company.Country)))
        {
            return new CompanyMatchResponse(
                ToResponse(company),
                CompanyMatchStrength.Weak,
                identity.Country is null
                    ? "Normalized display name match; country was not supplied."
                    : "Normalized display name and country match.");
        }

        return null;
    }

    private static int MatchStrengthRank(CompanyMatchStrength strength) => strength switch
    {
        CompanyMatchStrength.Exact => 4,
        CompanyMatchStrength.VeryStrong => 3,
        CompanyMatchStrength.Strong => 2,
        CompanyMatchStrength.Weak => 1,
        _ => 0
    };

    private static CompanyResponse ToResponse(Company company) =>
        new(
            company.Id,
            company.Name,
            company.Website,
            company.Country,
            company.CreatedAt,
            company.UpdatedAt,
            company.LegalName,
            company.RegistrationNumber,
            company.Headquarters,
            company.LastResearchedAt,
            company.ArchivedAt);

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
