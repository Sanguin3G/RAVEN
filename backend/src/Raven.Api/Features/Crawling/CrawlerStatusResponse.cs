namespace Raven.Api.Features.Crawling;

/// <summary>Availability of the configured local Crawl4AI service.</summary>
public sealed record CrawlerStatusResponse(string Provider, bool Available);
