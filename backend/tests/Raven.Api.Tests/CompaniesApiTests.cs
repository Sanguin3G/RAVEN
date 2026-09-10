using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Raven.Api.Features.Companies;

namespace Raven.Api.Tests;

public sealed class CompaniesApiTests(RavenApiFactory factory) : IClassFixture<RavenApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Create_list_and_get_company_returns_a_persistent_company_contract()
    {
        var client = factory.CreateClient();
        var request = new CreateCompanyRequest(
            "FPT Software",
            "https://fptsoftware.com",
            "Vietnam",
            "FPT Software Company Limited",
            "0101248141",
            "Hanoi, Vietnam");

        var createResponse = await client.PostAsJsonAsync("/api/companies", request);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CompanyResponse>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("FPT Software", created.Name);
        Assert.Equal("FPT Software Company Limited", created.LegalName);
        Assert.Equal("0101248141", created.RegistrationNumber);
        Assert.Equal("Hanoi, Vietnam", created.Headquarters);
        Assert.Null(created.LastResearchedAt);
        Assert.Equal($"/api/companies/{created.Id}", createResponse.Headers.Location?.OriginalString);

        var getResponse = await client.GetFromJsonAsync<CompanyResponse>($"/api/companies/{created.Id}");
        Assert.NotNull(getResponse);
        Assert.Equal(created.Id, getResponse.Id);

        var companies = await client.GetFromJsonAsync<CompanyResponse[]>("/api/companies");
        Assert.NotNull(companies);
        Assert.Contains(companies, company => company.Id == created.Id);
    }

    [Fact]
    public async Task Company_matches_are_ranked_and_normalize_identity_values()
    {
        var client = factory.CreateClient();
        var websiteCompany = await CreateCompanyAsync(client, new CreateCompanyRequest(
            "Website Match Company",
            "https://www.website-match.example/about",
            "Canada"));
        var registrationCompany = await CreateCompanyAsync(client, new CreateCompanyRequest(
            "Registration Match Company",
            null,
            "Vietnam",
            null,
            "010-123-4567"));
        await CreateCompanyAsync(client, new CreateCompanyRequest(
            "Registration Match Company",
            null,
            "Vietnam"));
        var legalNameCompany = await CreateCompanyAsync(client, new CreateCompanyRequest(
            "Legal Name Match Company",
            null,
            "Vietnam",
            "Cong ty Co phan Phu Quoc"));
        var nameOnlyCompany = await CreateCompanyAsync(client, new CreateCompanyRequest(
            "Name Only Match Company",
            null,
            "Singapore"));

        var websiteMatches = await FindMatchesAsync(client, new CompanyMatchRequest(
            null,
            "website-match.example/contact",
            null));
        var registrationMatches = await FindMatchesAsync(client, new CompanyMatchRequest(
            null,
            null,
            null,
            null,
            "010 123 4567"));
        var legalNameMatches = await FindMatchesAsync(client, new CompanyMatchRequest(
            null,
            null,
            "vietnam",
            "C\u00D4NG TY C\u1ED4 PH\u1EA6N PH\u00DA QU\u1ED0C"));
        var nameOnlyMatches = await FindMatchesAsync(client, new CompanyMatchRequest(
            "name-only match company",
            null,
            null));

        var websiteMatch = Assert.Single(websiteMatches);
        Assert.Equal(websiteCompany.Id, websiteMatch.Company.Id);
        Assert.Equal(CompanyMatchStrength.VeryStrong, websiteMatch.MatchStrength);
        Assert.Contains("website host", websiteMatch.MatchReason, StringComparison.OrdinalIgnoreCase);

        var registrationMatch = Assert.Single(registrationMatches);
        Assert.Equal(registrationCompany.Id, registrationMatch.Company.Id);
        Assert.Equal(CompanyMatchStrength.Exact, registrationMatch.MatchStrength);
        Assert.Contains("registration", registrationMatch.MatchReason, StringComparison.OrdinalIgnoreCase);

        var legalNameMatch = Assert.Single(legalNameMatches);
        Assert.Equal(legalNameCompany.Id, legalNameMatch.Company.Id);
        Assert.Equal(CompanyMatchStrength.Strong, legalNameMatch.MatchStrength);
        Assert.Contains("legal name and country", legalNameMatch.MatchReason, StringComparison.OrdinalIgnoreCase);

        var nameOnlyMatch = Assert.Single(nameOnlyMatches);
        Assert.Equal(nameOnlyCompany.Id, nameOnlyMatch.Company.Id);
        Assert.Equal(CompanyMatchStrength.Weak, nameOnlyMatch.MatchStrength);
        Assert.Null(nameOnlyMatch.Company.LastResearchedAt);
    }

    [Fact]
    public async Task Create_allows_an_explicit_duplicate_identity()
    {
        var client = factory.CreateClient();
        var request = new CreateCompanyRequest(
            "Explicit Duplicate Company",
            "https://duplicate.example",
            "Vietnam",
            "Explicit Duplicate Company Limited",
            "9999999999");

        var first = await CreateCompanyAsync(client, request);
        var second = await CreateCompanyAsync(client, request);

        Assert.NotEqual(first.Id, second.Id);
        var matches = await FindMatchesAsync(client, new CompanyMatchRequest(
            null,
            "duplicate.example",
            null));
        Assert.Equal(2, matches.Count(match => match.Company.Name == request.Name));
    }

    [Fact]
    public async Task Create_company_without_a_name_returns_bad_request()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/companies", new CreateCompanyRequest(" ", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_missing_company_returns_not_found()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_company_with_malformed_json_returns_bad_request()
    {
        var client = factory.CreateClient();
        using var content = new StringContent("{", System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/companies", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Company_matches_without_identity_fields_returns_bad_request()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/companies/matches",
            new CompanyMatchRequest(null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<CompanyResponse> CreateCompanyAsync(HttpClient client, CreateCompanyRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/companies", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CompanyResponse>())!;
    }

    private static async Task<CompanyMatchResponse[]> FindMatchesAsync(HttpClient client, CompanyMatchRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/companies/matches", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CompanyMatchResponse[]>(JsonOptions))!;
    }
}
