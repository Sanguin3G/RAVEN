namespace Raven.Api.Features.Crawling;

/// <summary>Safe status only: never contains configuration values or secrets.</summary>
public sealed record ProviderConfigurationStatus(string Provider, bool Configured, bool? Available, string? SelectedModel);

public sealed record ProviderStatusResponse(
    ProviderConfigurationStatus Brave,
    ProviderConfigurationStatus Crawl4Ai,
    ProviderConfigurationStatus Gemini,
    ProviderConfigurationStatus Exa,
    ProviderConfigurationStatus Firecrawl,
    string DeepResearchModel);
