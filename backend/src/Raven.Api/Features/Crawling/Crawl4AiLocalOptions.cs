namespace Raven.Api.Features.Crawling;

public sealed class Crawl4AiLocalOptions
{
    public const string SectionName = "Crawl4AI:Local";

    public string BaseUrl { get; set; } = "http://localhost:11235";

    public string HealthPath { get; set; } = "/health";

    public string CrawlPath { get; set; } = "/crawl";

    public string? ApiToken { get; set; }

    public int TimeoutSeconds { get; set; } = 60;
}
