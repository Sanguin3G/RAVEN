using Microsoft.Extensions.Options;
using Raven.Api.Features.ProviderCredentials;

namespace Raven.Api.Features.Crawling;

public sealed class Crawl4AiLocalStatusProbe(
    HttpClient httpClient,
    IOptions<Crawl4AiLocalOptions> options,
    IProviderCredentialResolver? credentials = null) : ICrawlerStatusProbe
{
    public async Task<CrawlerStatusResponse> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            var configured = options.Value;
            var resolved = credentials is null
                ? null
                : await credentials.ResolveCrawl4AiAsync(cancellationToken);
            var connection = resolved ?? (string.IsNullOrWhiteSpace(configured.ApiToken)
                ? null
                : new Crawl4AiConnectionSettings(configured.BaseUrl, configured.ApiToken));
            if (connection is null || !Uri.TryCreate(connection.Endpoint, UriKind.Absolute, out var baseUri))
            {
                return new CrawlerStatusResponse("crawl4ai-local", false);
            }

            baseUri = new Uri(baseUri.ToString().TrimEnd('/') + "/", UriKind.Absolute);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, configured.HealthPath));
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", connection.Token);
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
