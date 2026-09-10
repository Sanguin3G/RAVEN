using Microsoft.AspNetCore.Http.HttpResults;

namespace Raven.Api.Features.Companies;

public static class CompanyEndpoints
{
    public static IEndpointRouteBuilder MapCompanyEndpoints(this IEndpointRouteBuilder app)
    {
        var companies = app.MapGroup("/api/companies").WithTags("Companies");

        companies.MapGet("/", GetCompaniesAsync)
            .WithName("GetCompanies")
            .WithSummary("List companies")
            .Produces<CompanyResponse[]>(StatusCodes.Status200OK);

        companies.MapGet("/{id:guid}", GetCompanyByIdAsync)
            .WithName("GetCompanyById")
            .WithSummary("Get a company by ID")
            .Produces<CompanyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        companies.MapPost("/matches", FindCompanyMatchesAsync)
            .WithName("FindCompanyMatches")
            .WithSummary("Find likely existing company identities")
            .WithDescription("Matches a submitted identity using registration number, website host, legal name, and display name without preventing duplicate creation.")
            .Produces<CompanyMatchResponse[]>(StatusCodes.Status200OK)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        companies.MapPost("/", CreateCompanyAsync)
            .WithName("CreateCompany")
            .WithSummary("Create a company")
            .Produces<CompanyResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<Ok<CompanyResponse[]>> GetCompaniesAsync(
        ICompanyService companies,
        CancellationToken cancellationToken)
    {
        var result = await companies.ListAsync(cancellationToken);
        return TypedResults.Ok(result.ToArray());
    }

    private static async Task<Results<Ok<CompanyResponse>, NotFound>> GetCompanyByIdAsync(
        Guid id,
        ICompanyService companies,
        CancellationToken cancellationToken)
    {
        var company = await companies.GetByIdAsync(id, cancellationToken);
        return company is null ? TypedResults.NotFound() : TypedResults.Ok(company);
    }

    private static async Task<Results<Created<CompanyResponse>, ValidationProblem>> CreateCompanyAsync(
        CreateCompanyRequest request,
        ICompanyService companies,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [nameof(CreateCompanyRequest.Name)] = ["Name is required."]
                });
        }

        var company = await companies.CreateAsync(
            request.Name,
            request.Website,
            request.Country,
            request.LegalName,
            request.RegistrationNumber,
            request.Headquarters,
            cancellationToken);
        return TypedResults.Created($"/api/companies/{company.Id}", company);
    }

    private static async Task<Results<Ok<CompanyMatchResponse[]>, ValidationProblem>> FindCompanyMatchesAsync(
        CompanyMatchRequest request,
        ICompanyService companies,
        CancellationToken cancellationToken)
    {
        if (!HasIdentityValue(request))
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["identity"] = ["At least one company identity field is required."]
                });
        }

        var matches = await companies.FindMatchesAsync(request, cancellationToken);
        return TypedResults.Ok(matches.ToArray());
    }

    private static bool HasIdentityValue(CompanyMatchRequest request) =>
        !string.IsNullOrWhiteSpace(request.Name) ||
        !string.IsNullOrWhiteSpace(request.Website) ||
        !string.IsNullOrWhiteSpace(request.Country) ||
        !string.IsNullOrWhiteSpace(request.LegalName) ||
        !string.IsNullOrWhiteSpace(request.RegistrationNumber) ||
        !string.IsNullOrWhiteSpace(request.Headquarters);
}
