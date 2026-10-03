using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Raven.Api.Features.Auth;

namespace Raven.Api.Tests;

public sealed class AuthenticationApiTests(RavenIdentityApiFactory factory) : IClassFixture<RavenIdentityApiFactory>
{
    [Fact]
    public async Task Anonymous_requests_are_rejected_and_health_remains_public()
    {
        var client = factory.CreateClient();

        var protectedResponse = await client.GetAsync("/api/companies");
        var healthResponse = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);
    }

    [Fact]
    public async Task Login_and_logout_issue_and_clear_an_authenticated_cookie()
    {
        var client = factory.CreateClient();
        var login = await LoginAsync(client, RavenIdentityApiFactory.AdminEmail, RavenIdentityApiFactory.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var current = await client.GetFromJsonAsync<AuthUserResponse>("/api/auth/me");
        Assert.NotNull(current);
        Assert.Contains("Admin", current.Roles);

        var logout = await PostWithAntiforgeryAsync(client, "/api/auth/logout");
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/companies")).StatusCode);
    }

    [Fact]
    public async Task Login_requires_an_antiforgery_token()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = RavenIdentityApiFactory.AdminEmail,
            Password = RavenIdentityApiFactory.AdminPassword
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Researcher_can_use_workspace_api_but_cannot_manage_users_or_merge_companies()
    {
        var adminClient = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(adminClient, RavenIdentityApiFactory.AdminEmail, RavenIdentityApiFactory.AdminPassword)).StatusCode);

        var createResponse = await PostJsonWithAntiforgeryAsync(adminClient, "/api/admin/users", new
        {
            displayName = "Workspace Researcher",
            email = "researcher@example.invalid",
            temporaryPassword = "Raven-Researcher-Pass9!"
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<WorkspaceUserResponse>();
        Assert.NotNull(created);
        Assert.Equal("Researcher", created.Role);

        var researcherClient = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(researcherClient, created.Email, "Raven-Researcher-Pass9!")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await researcherClient.GetAsync("/api/companies")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await researcherClient.GetAsync("/api/settings/research")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await researcherClient.GetAsync($"/api/companies/{Guid.NewGuid()}/chat/conversations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await researcherClient.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await researcherClient.PostAsJsonAsync("/api/companies/merge/preview", new
        {
            canonicalCompanyId = Guid.NewGuid(),
            duplicateCompanyId = Guid.NewGuid()
        })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await researcherClient.DeleteAsync($"/api/companies/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Last_enabled_admin_cannot_be_disabled_or_demoted()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, RavenIdentityApiFactory.AdminEmail, RavenIdentityApiFactory.AdminPassword)).StatusCode);
        var users = await client.GetFromJsonAsync<WorkspaceUserResponse[]>("/api/admin/users");
        var admin = Assert.Single(users!, user => user.Role == "Admin");
        var token = await GetAntiforgeryTokenAsync(client);

        var roleResponse = await PutJsonAsync(client, $"/api/admin/users/{admin.Id}/role", new { role = "Researcher" }, token);
        var enabledResponse = await PutJsonAsync(client, $"/api/admin/users/{admin.Id}/enabled", new { isEnabled = false }, token);
        var mergeResponse = await SendJsonAsync(client, HttpMethod.Post, "/api/companies/merge/preview", new
        {
            canonicalCompanyId = Guid.NewGuid(),
            duplicateCompanyId = Guid.NewGuid()
        }, token);
        var deleteResponse = await SendJsonAsync(client, HttpMethod.Delete, $"/api/companies/{Guid.NewGuid()}", new { confirm = true }, token);

        Assert.Equal(HttpStatusCode.Conflict, roleResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, enabledResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, mergeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_creates_an_admin_once_and_does_not_overwrite_existing_credentials()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            configuration["RAVEN_BOOTSTRAP_ADMIN_PASSWORD"] = "Raven-Replacement-Pass9!";
            await scope.ServiceProvider.GetRequiredService<IdentityBootstrapper>().InitializeAsync();
        }

        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client, RavenIdentityApiFactory.AdminEmail, RavenIdentityApiFactory.AdminPassword)).StatusCode);
        var users = await client.GetFromJsonAsync<WorkspaceUserResponse[]>("/api/admin/users");
        Assert.Contains(users!, user => user.Email == RavenIdentityApiFactory.AdminEmail && user.Role == "Admin");

        var freshClient = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(freshClient, RavenIdentityApiFactory.AdminEmail, "Raven-Replacement-Pass9!")).StatusCode);
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        await PostJsonWithAntiforgeryAsync(client, "/api/auth/login", new LoginRequest { Email = email, Password = password });

    private static async Task<HttpResponseMessage> PostWithAntiforgeryAsync(HttpClient client, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-RAVEN-CSRF", await GetAntiforgeryTokenAsync(client));
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostJsonWithAntiforgeryAsync(HttpClient client, string path, object body) =>
        await SendJsonWithAntiforgeryAsync(client, HttpMethod.Post, path, body);

    private static async Task<HttpResponseMessage> PutJsonAsync(HttpClient client, string path, object body, string token) =>
        await SendJsonAsync(client, HttpMethod.Put, path, body, token);

    private static async Task<HttpResponseMessage> SendJsonWithAntiforgeryAsync(HttpClient client, HttpMethod method, string path, object body) =>
        await SendJsonAsync(client, method, path, body, await GetAntiforgeryTokenAsync(client));

    private static async Task<HttpResponseMessage> SendJsonAsync(HttpClient client, HttpMethod method, string path, object body, string token)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-RAVEN-CSRF", token);
        return await client.SendAsync(request);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<AntiforgeryTokenResponse>("/api/auth/csrf");
        return response!.RequestToken;
    }
}
