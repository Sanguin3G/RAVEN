namespace Raven.Api.Features.Firecrawl;

/// <summary>
/// Configuration for the Firecrawl REST adapters.
///
/// The API key is intentionally kept in server-side configuration. It is never
/// included in a result, log entry, or frontend response.
/// </summary>
public sealed class FirecrawlOptions
{
    public const string SectionName = "Providers:Firecrawl";

    public const string ApiKeyEnvironmentVariable = "FIRECRAWL_API_KEY";

    public string BaseUrl { get; set; } = "https://api.firecrawl.dev";

    public string SearchPath { get; set; } = "/v2/search";

    public string ScrapePath { get; set; } = "/v2/scrape";

    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum characters retained from a Firecrawl search description.
    /// Search output is discovery metadata, not accepted evidence.
    /// </summary>
    public int MaxSnippetCharacters { get; set; } = 500;

    /// <summary>
    /// Maximum characters retained from a scraped markdown document at the
    /// provider boundary. The original URL and retrieval metadata are retained
    /// by the caller separately.
    /// </summary>
    public int MaxMarkdownCharacters { get; set; } = 250_000;
}
