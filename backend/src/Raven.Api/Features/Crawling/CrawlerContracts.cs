namespace Raven.Api.Features.Crawling;

public interface ICrawlerProvider
{
    string Id { get; }

    Task<CrawlResult> CrawlAsync(CrawlRequest request, CancellationToken cancellationToken = default);
}

public sealed record CrawlRequest(string Url);

public sealed record CrawlResult(
    string Provider,
    string RequestedUrl,
    string? FinalUrl,
    string? Title,
    string? Markdown,
    bool Success,
    string? Error,
    DateTimeOffset RetrievedAt);
