using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Ai;
using Raven.Api.Data;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Search;
using Raven.Api.Features.Search.Exa;

namespace Raven.Api.Features.ProviderCredentials;

public sealed class ProviderCredentialManagementService(
    RavenDbContext db,
    IProviderCredentialResolver resolver,
    IProviderCredentialVault vault,
    ProviderCredentialConnectionTester connectionTester)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ProviderCredentialAdministrationResponse> GetAsync(CancellationToken cancellationToken)
    {
        var statuses = await resolver.GetStatusesAsync(cancellationToken);
        return new ProviderCredentialAdministrationResponse(statuses, vault.CanPersist);
    }

    public async Task ReplaceAsync(string provider, ReplaceProviderCredentialRequest request, CancellationToken cancellationToken)
    {
        var normalized = NormalizeProvider(provider);
        var plaintext = normalized == ProviderCredentialDefinitions.Crawl4Ai
            ? BuildCrawl4AiPayload(request)
            : BuildApiKey(request);
        var encrypted = vault.Encrypt(normalized, plaintext);
        var entity = await db.ProviderCredentials.SingleOrDefaultAsync(item => item.Provider == normalized, cancellationToken);
        if (entity is null)
        {
            entity = new ProviderCredentialEntity { Provider = normalized, Ciphertext = [], Nonce = [], AuthenticationTag = [] };
            db.ProviderCredentials.Add(entity);
        }

        entity.Ciphertext = encrypted.Ciphertext;
        entity.Nonce = encrypted.Nonce;
        entity.AuthenticationTag = encrypted.AuthenticationTag;
        entity.Version = 1;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveOverrideAsync(string provider, CancellationToken cancellationToken)
    {
        var normalized = NormalizeProvider(provider);
        var entity = await db.ProviderCredentials.SingleOrDefaultAsync(item => item.Provider == normalized, cancellationToken);
        if (entity is null)
        {
            return false;
        }

        db.ProviderCredentials.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<ProviderCredentialOperationResponse> TestAsync(string provider, CancellationToken cancellationToken) =>
        connectionTester.TestAsync(NormalizeProvider(provider), cancellationToken);

    private static string NormalizeProvider(string provider)
    {
        if (!ProviderCredentialDefinitions.IsSupported(provider))
        {
            throw new ArgumentException("Choose a supported provider.", nameof(provider));
        }

        return ProviderCredentialDefinitions.Normalize(provider);
    }

    private static string BuildApiKey(ReplaceProviderCredentialRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            throw new ArgumentException("Enter a new provider key.", nameof(request));
        }
        if (request.ApiKey.Length > 4_096)
        {
            throw new ArgumentException("Provider keys must be 4,096 characters or fewer.", nameof(request));
        }

        if (!string.IsNullOrWhiteSpace(request.Endpoint) || !string.IsNullOrWhiteSpace(request.Token))
        {
            throw new ArgumentException("Endpoint and token fields are only used for Crawl4AI.", nameof(request));
        }

        return request.ApiKey.Trim();
    }

    private static string BuildCrawl4AiPayload(ReplaceProviderCredentialRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Endpoint) || string.IsNullOrWhiteSpace(request.Token))
        {
            throw new ArgumentException("Enter both the Crawl4AI endpoint and token.", nameof(request));
        }
        if (request.Endpoint.Length > 2_048 || request.Token.Length > 4_096)
        {
            throw new ArgumentException("Crawl4AI endpoint and token exceed the supported length.", nameof(request));
        }

        if (!Uri.TryCreate(request.Endpoint.Trim(), UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment)
        )
        {
            throw new ArgumentException("Crawl4AI endpoint must be an absolute HTTP(S) URL without credentials, query, or fragment.", nameof(request));
        }

        return JsonSerializer.Serialize(
            new Crawl4AiCredentialPayload(endpoint.ToString().TrimEnd('/'), request.Token.Trim()),
            JsonOptions);
    }
}

/// <summary>Performs explicit, bounded connectivity checks without echoing provider data.</summary>
public sealed class ProviderCredentialConnectionTester(
    IProviderCredentialResolver resolver,
    ICrawlerStatusProbe crawlerStatusProbe,
    IOptions<BraveSearchOptions> braveOptions,
    IOptions<ExaSearchOptions> exaOptions,
    IOptions<GeminiOptions> geminiOptions,
    IHttpClientFactory clients)
{
    public async Task<ProviderCredentialOperationResponse> TestAsync(string provider, CancellationToken cancellationToken)
    {
        var resolved = await resolver.ResolveAsync(provider, cancellationToken);
        if (!resolved.Configured)
        {
            return new ProviderCredentialOperationResponse(false, "No workspace override or deployment credential is configured.");
        }

        if (provider == ProviderCredentialDefinitions.GoogleMaps)
        {
            return new ProviderCredentialOperationResponse(false, "Maps keys are browser-restricted. Verify this key by loading a company map in RAVEN.");
        }

        if (provider == ProviderCredentialDefinitions.Crawl4Ai)
        {
            var crawlerStatus = await crawlerStatusProbe.CheckAsync(cancellationToken);
            return crawlerStatus.Available
                ? new ProviderCredentialOperationResponse(true, "Connection succeeded.")
                : new ProviderCredentialOperationResponse(false, "Crawl4AI is unavailable or rejected its configured endpoint/token.");
        }

        try
        {
            using var client = clients.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(8);
            using var response = provider switch
            {
                ProviderCredentialDefinitions.Brave => await TestBraveAsync(client, resolved.Value!, braveOptions.Value, cancellationToken),
                ProviderCredentialDefinitions.Exa => await TestExaAsync(client, resolved.Value!, exaOptions.Value, cancellationToken),
                ProviderCredentialDefinitions.Gemini => await TestGeminiAsync(client, resolved.Value!, geminiOptions.Value, cancellationToken),
                _ => null
            };

            return response is not null && response.IsSuccessStatusCode
                ? new ProviderCredentialOperationResponse(true, "Connection succeeded.")
                : new ProviderCredentialOperationResponse(false, "The provider rejected the connection test. Check the credential and endpoint.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProviderCredentialOperationResponse(false, "The provider connection test timed out.");
        }
        catch (HttpRequestException)
        {
            return new ProviderCredentialOperationResponse(false, "The provider could not be reached.");
        }
        catch (JsonException)
        {
            return new ProviderCredentialOperationResponse(false, "The Crawl4AI configuration is invalid.");
        }
    }

    private static async Task<HttpResponseMessage> TestBraveAsync(HttpClient client, string key, BraveSearchOptions configured, CancellationToken cancellationToken)
    {
        var baseUri = new Uri(configured.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, "res/v1/web/search?q=RAVEN&count=1"));
        request.Headers.TryAddWithoutValidation("X-Subscription-Token", key);
        request.Headers.Accept.ParseAdd("application/json");
        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task<HttpResponseMessage> TestExaAsync(HttpClient client, string key, ExaSearchOptions configured, CancellationToken cancellationToken)
    {
        var baseUri = new Uri(configured.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var searchPath = string.IsNullOrWhiteSpace(configured.SearchPath) ? "search" : configured.SearchPath.TrimStart('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, searchPath));
        request.Headers.TryAddWithoutValidation("x-api-key", key);
        request.Content = JsonContent.Create(new { query = "RAVEN", type = "auto", numResults = 1 });
        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task<HttpResponseMessage> TestGeminiAsync(HttpClient client, string key, GeminiOptions configured, CancellationToken cancellationToken)
    {
        var baseUri = new Uri(configured.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, $"{configured.ApiVersion.Trim('/')}/models?pageSize=1"));
        request.Headers.TryAddWithoutValidation("x-goog-api-key", key);
        return await client.SendAsync(request, cancellationToken);
    }

}
