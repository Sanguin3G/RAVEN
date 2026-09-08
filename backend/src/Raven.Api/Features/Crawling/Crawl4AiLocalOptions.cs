namespace Raven.Api.Features.Crawling;

public sealed class Crawl4AiLocalOptions
{
    public const string SectionName = "Crawl4AI:Local";

    public string BaseUrl { get; init; } = "http://localhost:11235";

    public string HealthPath { get; init; } = "/health";
}
