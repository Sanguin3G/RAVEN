using System.Net;
using System.Net.Http.Json;
using Raven.Api.Features.Companies;

namespace Raven.Api.Tests;

public sealed class CompaniesApiTests(RavenApiFactory factory) : IClassFixture<RavenApiFactory>
{
    [Fact]
    public async Task Create_list_and_get_company_returns_a_persistent_company_contract()
    {
        var client = factory.CreateClient();
        var request = new CreateCompanyRequest("FPT Software", "https://fptsoftware.com", "Vietnam");

        var createResponse = await client.PostAsJsonAsync("/api/companies", request);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CompanyResponse>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("FPT Software", created.Name);
        Assert.Equal($"/api/companies/{created.Id}", createResponse.Headers.Location?.OriginalString);

        var getResponse = await client.GetFromJsonAsync<CompanyResponse>($"/api/companies/{created.Id}");
        Assert.NotNull(getResponse);
        Assert.Equal(created.Id, getResponse.Id);

        var companies = await client.GetFromJsonAsync<CompanyResponse[]>("/api/companies");
        Assert.NotNull(companies);
        Assert.Contains(companies, company => company.Id == created.Id);
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
}
