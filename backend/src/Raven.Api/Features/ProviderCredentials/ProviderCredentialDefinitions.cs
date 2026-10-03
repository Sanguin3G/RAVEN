namespace Raven.Api.Features.ProviderCredentials;

public static class ProviderCredentialDefinitions
{
    public const string Brave = "brave";
    public const string Exa = "exa";
    public const string Gemini = "gemini";
    public const string GoogleMaps = "google-maps";
    public const string Crawl4Ai = "crawl4ai";

    public static readonly IReadOnlyList<string> SupportedProviders =
        [Brave, Exa, Gemini, GoogleMaps, Crawl4Ai];

    public static bool IsSupported(string? provider) =>
        SupportedProviders.Contains(provider, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string provider) =>
        SupportedProviders.First(value => string.Equals(value, provider, StringComparison.OrdinalIgnoreCase));
}

public sealed record ProviderCredentialStatusResponse(string Provider, bool Configured, string Source);

public sealed record ProviderCredentialAdministrationResponse(
    IReadOnlyList<ProviderCredentialStatusResponse> Credentials,
    bool WorkspaceOverridesAvailable);

public sealed record ResolvedProviderCredential(string Provider, string? Value, string Source)
{
    public bool Configured => !string.IsNullOrWhiteSpace(Value);
}

public sealed record Crawl4AiConnectionSettings(string Endpoint, string Token);

public sealed record ReplaceProviderCredentialRequest(string? ApiKey, string? Endpoint, string? Token);

public sealed record ProviderCredentialOperationResponse(bool Succeeded, string Message);

public sealed record Crawl4AiCredentialPayload(string Endpoint, string Token);
