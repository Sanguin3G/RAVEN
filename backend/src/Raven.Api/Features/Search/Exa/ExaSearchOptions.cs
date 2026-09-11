namespace Raven.Api.Features.Search.Exa;

/// <summary>
/// Configuration for the Exa web-search adapter.
/// </summary>
public sealed class ExaSearchOptions
{
    public const string SectionName = "Providers:Exa";

    public const string ApiKeyEnvironmentVariable = "EXA_API_KEY";

    public string BaseUrl { get; set; } = "https://api.exa.ai";

    public string SearchPath { get; set; } = "/search";

    public string SearchType { get; set; } = "auto";

    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of characters retained when a result contains a text or
    /// highlight snippet. Search results are discovery metadata, not evidence.
    /// </summary>
    public int MaxSnippetCharacters { get; set; } = 500;
}
