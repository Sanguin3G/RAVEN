using System.Text.Json;
using Raven.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Raven.Api.Features.ProviderCredentials;

public interface IProviderCredentialResolver
{
    Task<ResolvedProviderCredential> ResolveAsync(string provider, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderCredentialStatusResponse>> GetStatusesAsync(CancellationToken cancellationToken = default);

    Task<Crawl4AiConnectionSettings?> ResolveCrawl4AiAsync(CancellationToken cancellationToken = default);
}

/// <summary>Resolves live encrypted workspace overrides before deployment configuration.</summary>
public sealed class ProviderCredentialResolver(
    RavenDbContext db,
    IProviderCredentialVault vault,
    IConfiguration configuration,
    ILogger<ProviderCredentialResolver> logger) : IProviderCredentialResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ResolvedProviderCredential> ResolveAsync(string provider, CancellationToken cancellationToken = default)
    {
        if (!ProviderCredentialDefinitions.IsSupported(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider), "Unsupported provider credential.");
        }

        var normalized = ProviderCredentialDefinitions.Normalize(provider);
        var workspace = await db.ProviderCredentials.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Provider == normalized, cancellationToken);
        if (workspace is not null && vault.TryDecrypt(normalized, workspace, out var decrypted) && IsValidPayload(normalized, decrypted))
        {
            return new ResolvedProviderCredential(normalized, decrypted, "workspace");
        }

        if (workspace is not null)
        {
            logger.LogWarning("An encrypted credential override for {Provider} could not be opened; using deployment configuration if available.", normalized);
        }

        var fallback = ReadDeploymentValue(normalized);
        return new ResolvedProviderCredential(normalized, fallback, string.IsNullOrWhiteSpace(fallback) ? "missing" : "environment");
    }

    public async Task<IReadOnlyList<ProviderCredentialStatusResponse>> GetStatusesAsync(CancellationToken cancellationToken = default)
    {
        var statuses = new List<ProviderCredentialStatusResponse>(ProviderCredentialDefinitions.SupportedProviders.Count);
        foreach (var provider in ProviderCredentialDefinitions.SupportedProviders)
        {
            var resolved = await ResolveAsync(provider, cancellationToken);
            statuses.Add(new ProviderCredentialStatusResponse(provider, resolved.Configured, resolved.Source));
        }

        return statuses;
    }

    public async Task<Crawl4AiConnectionSettings?> ResolveCrawl4AiAsync(CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(ProviderCredentialDefinitions.Crawl4Ai, cancellationToken);
        if (!resolved.Configured)
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Crawl4AiCredentialPayload>(resolved.Value!, JsonOptions);
            return payload is null ? null : new Crawl4AiConnectionSettings(payload.Endpoint, payload.Token);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string? ReadDeploymentValue(string provider) => provider switch
    {
        ProviderCredentialDefinitions.Brave => FirstConfigured("BRAVE_SEARCH_API_KEY", "Providers:Brave:ApiKey"),
        ProviderCredentialDefinitions.Exa => FirstConfigured("EXA_API_KEY", "Providers:Exa:ApiKey"),
        ProviderCredentialDefinitions.Gemini => FirstConfigured("GEMINI_API_KEY", "Providers:Gemini:ApiKey"),
        ProviderCredentialDefinitions.GoogleMaps => FirstConfigured("GOOGLE_MAPS_EMBED_API_KEY", "GoogleMaps:EmbedApiKey"),
        ProviderCredentialDefinitions.Crawl4Ai => ReadDeploymentCrawlConfiguration(),
        _ => null
    };

    private string? ReadDeploymentCrawlConfiguration()
    {
        var token = FirstConfigured("CRAWL4AI_API_TOKEN", "Crawl4AI:Local:ApiToken");
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var endpoint = FirstConfigured("CRAWL4AI_LOCAL_BASE_URL", "Crawl4AI:Local:BaseUrl") ?? "http://localhost:11235";
        return JsonSerializer.Serialize(new Crawl4AiCredentialPayload(endpoint.TrimEnd('/'), token), JsonOptions);
    }

    private string? FirstConfigured(params string[] names)
    {
        foreach (var name in names)
        {
            var value = configuration[name];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static bool IsValidPayload(string provider, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (provider != ProviderCredentialDefinitions.Crawl4Ai)
        {
            return true;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Crawl4AiCredentialPayload>(value, JsonOptions);
            return payload is not null && Uri.TryCreate(payload.Endpoint, UriKind.Absolute, out var uri) &&
                   uri.Scheme is "http" or "https" && !string.IsNullOrWhiteSpace(payload.Token);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
