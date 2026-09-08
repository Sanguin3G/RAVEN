using Microsoft.Extensions.Options;

namespace Raven.Api.Features.Crawling;

public sealed class Crawl4AiLocalStatusProbe(
    HttpClient httpClient,
    IOptions<Crawl4AiLocalOptions> options) : ICrawlerStatusProbe
{
    public async Task<CrawlerStatusResponse> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, options.Value.HealthPath);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            return new CrawlerStatusResponse("crawl4ai-local", response.IsSuccessStatusCode);
        }
        catch (HttpRequestException)
        {
            return new CrawlerStatusResponse("crawl4ai-local", false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CrawlerStatusResponse("crawl4ai-local", false);
        }
    }
}
