namespace Raven.Api.Features.Crawling;

public interface ICrawlerStatusProbe
{
    Task<CrawlerStatusResponse> CheckAsync(CancellationToken cancellationToken);
}
