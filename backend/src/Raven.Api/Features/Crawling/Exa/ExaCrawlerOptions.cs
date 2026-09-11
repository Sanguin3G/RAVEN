namespace Raven.Api.Features.Crawling.Exa;

/// <summary>
/// Configuration for Exa's single-page Contents API adapter.
/// </summary>
public sealed class ExaCrawlerOptions
{
    public const string SectionName = "Providers:Exa";

    public const string ApiKeyEnvironmentVariable = "EXA_API_KEY";

    public string BaseUrl { get; set; } = "https://api.exa.ai";

    public string ContentsPath { get; set; } = "/contents";

    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum text retained from a Contents response. The adapter clamps this
    /// value so a provider response cannot turn into unbounded evidence input.
    /// </summary>
    public int MaxTextCharacters { get; set; } = 50_000;
}
