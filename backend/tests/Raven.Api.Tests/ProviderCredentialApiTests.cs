using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Raven.Api.Data;
using Raven.Api.Features.Auth;
using Raven.Api.Features.ProviderCredentials;

namespace Raven.Api.Tests;

public sealed class ProviderCredentialApiTests(ProviderCredentialApiFactory factory) : IClassFixture<ProviderCredentialApiFactory>
{
    [Fact]
    public void Missing_master_key_disables_encrypted_workspace_storage()
    {
        var configuration = new ConfigurationBuilder().Build();
        using var vault = new ProviderCredentialVault(configuration);

        Assert.False(vault.CanPersist);
        Assert.Throws<ProviderCredentialStorageUnavailableException>(() => vault.Encrypt(ProviderCredentialDefinitions.Brave, "must-not-be-stored"));
    }

    [Fact]
    public async Task Admin_override_is_encrypted_live_and_removed_to_reveal_deployment_fallback()
    {
        var client = await AdminClientAsync();
        const string workspaceSecret = "workspace-brave-secret";

        var replace = await SendJsonWithAntiforgeryAsync(client, HttpMethod.Put, "/api/admin/provider-credentials/brave", new { apiKey = workspaceSecret });
        Assert.Equal(HttpStatusCode.OK, replace.StatusCode);
        Assert.DoesNotContain(workspaceSecret, await replace.Content.ReadAsStringAsync());

        using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RavenDbContext>();
            var saved = await db.ProviderCredentials.SingleAsync(item => item.Provider == ProviderCredentialDefinitions.Brave);
            Assert.DoesNotContain(workspaceSecret, Encoding.UTF8.GetString(saved.Ciphertext));
            Assert.Equal(12, saved.Nonce.Length);
            Assert.Equal(16, saved.AuthenticationTag.Length);

            var resolved = await scope.ServiceProvider.GetRequiredService<IProviderCredentialResolver>()
                .ResolveAsync(ProviderCredentialDefinitions.Brave);
            Assert.Equal(workspaceSecret, resolved.Value);
            Assert.Equal("workspace", resolved.Source);
        }

        var statusResponse = await client.GetAsync("/api/admin/provider-credentials");
        var statusJson = await statusResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        Assert.DoesNotContain(workspaceSecret, statusJson);
        using (var document = JsonDocument.Parse(statusJson))
        {
            var brave = Assert.Single(document.RootElement.GetProperty("credentials").EnumerateArray(),
                item => item.GetProperty("provider").GetString() == ProviderCredentialDefinitions.Brave);
            Assert.True(brave.GetProperty("configured").GetBoolean());
            Assert.Equal("workspace", brave.GetProperty("source").GetString());
        }

        var remove = await SendJsonWithAntiforgeryAsync(client, HttpMethod.Delete, "/api/admin/provider-credentials/brave", new { });
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        using (var scope = factory.Services.CreateAsyncScope())
        {
            var resolved = await scope.ServiceProvider.GetRequiredService<IProviderCredentialResolver>()
                .ResolveAsync(ProviderCredentialDefinitions.Brave);
            Assert.Equal("brave-deployment-secret", resolved.Value);
            Assert.Equal("environment", resolved.Source);
        }
    }

    [Fact]
    public async Task Credential_routes_are_admin_only_and_crawl_endpoint_is_validated()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/provider-credentials")).StatusCode);

        var admin = await AdminClientAsync();
        var invalidEndpoint = await SendJsonWithAntiforgeryAsync(admin, HttpMethod.Put, "/api/admin/provider-credentials/crawl4ai", new
        {
            endpoint = "file:///private/path",
            token = "crawl-new-token"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidEndpoint.StatusCode);

        var createResearcher = await SendJsonWithAntiforgeryAsync(admin, HttpMethod.Post, "/api/admin/users", new
        {
            displayName = "Credential Researcher",
            email = "credential-researcher@example.invalid",
            temporaryPassword = "Raven-Researcher-Pass9!"
        });
        Assert.Equal(HttpStatusCode.Created, createResearcher.StatusCode);
        var researcher = await createResearcher.Content.ReadFromJsonAsync<WorkspaceUserResponse>();
        Assert.NotNull(researcher);

        var researcherClient = factory.CreateClient();
        var login = await SendJsonWithAntiforgeryAsync(researcherClient, HttpMethod.Post, "/api/auth/login", new LoginRequest
        {
            Email = researcher.Email,
            Password = "Raven-Researcher-Pass9!"
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await researcherClient.GetAsync("/api/admin/provider-credentials")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await SendJsonWithAntiforgeryAsync(researcherClient, HttpMethod.Put, "/api/admin/provider-credentials/brave", new { apiKey = "should-not-save" })).StatusCode);

        var runtimeResponse = await researcherClient.GetAsync("/api/runtime-config");
        var runtimeJson = await runtimeResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, runtimeResponse.StatusCode);
        Assert.Contains("maps-browser-key", runtimeJson);
        Assert.DoesNotContain("brave-deployment-secret", runtimeJson);
        Assert.DoesNotContain("exa-deployment-secret", runtimeJson);
        Assert.DoesNotContain("gemini-deployment-secret", runtimeJson);
        Assert.DoesNotContain("crawl-deployment-token", runtimeJson);
        using var runtime = JsonDocument.Parse(runtimeJson);
        Assert.True(runtime.RootElement.GetProperty("demoMode").GetBoolean());
        Assert.Equal(new[] { "demoMode", "googleMapsEmbedApiKey" }, runtime.RootElement.EnumerateObject()
            .Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = factory.CreateClient();
        var login = await SendJsonWithAntiforgeryAsync(client, HttpMethod.Post, "/api/auth/login", new LoginRequest
        {
            Email = ProviderCredentialApiFactory.AdminEmail,
            Password = ProviderCredentialApiFactory.AdminPassword
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }

    private static async Task<HttpResponseMessage> SendJsonWithAntiforgeryAsync(HttpClient client, HttpMethod method, string path, object body)
    {
        var csrf = await client.GetFromJsonAsync<AntiforgeryTokenResponse>("/api/auth/csrf");
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-RAVEN-CSRF", csrf!.RequestToken);
        return await client.SendAsync(request);
    }
}

public sealed class ProviderCredentialApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "credential-admin@example.invalid";
    public const string AdminPassword = "Raven-Credentials-Pass9!";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"raven-provider-credentials-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Raven", $"Data Source={databasePath};Pooling=False");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RAVEN_BOOTSTRAP_ADMIN_EMAIL"] = AdminEmail,
            ["RAVEN_BOOTSTRAP_ADMIN_PASSWORD"] = AdminPassword,
            ["RAVEN_CREDENTIAL_MASTER_KEY"] = Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray()),
            ["RAVEN_DEMO_MODE"] = "true",
            ["Security:ValidateApiAntiforgery"] = "true",
            ["BRAVE_SEARCH_API_KEY"] = "brave-deployment-secret",
            ["EXA_API_KEY"] = "exa-deployment-secret",
            ["GEMINI_API_KEY"] = "gemini-deployment-secret",
            ["GOOGLE_MAPS_EMBED_API_KEY"] = "maps-browser-key",
            ["CRAWL4AI_LOCAL_BASE_URL"] = "http://localhost:11235",
            ["CRAWL4AI_API_TOKEN"] = "crawl-deployment-token",
            ["Providers:Brave:ApiKey"] = null,
            ["Providers:Exa:ApiKey"] = null,
            ["Providers:Gemini:ApiKey"] = null,
            ["GoogleMaps:EmbedApiKey"] = null,
            ["Crawl4AI:Local:ApiToken"] = null
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            if (File.Exists(path)) File.Delete(path);
    }
}
