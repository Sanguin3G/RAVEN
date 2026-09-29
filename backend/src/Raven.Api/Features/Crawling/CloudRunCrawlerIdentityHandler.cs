using Microsoft.Extensions.Options;

namespace Raven.Api.Features.Crawling;

/// <summary>Adds Cloud Run IAM identity without replacing Crawl4AI's own Authorization token.</summary>
public sealed class CloudRunCrawlerIdentityHandler(
    IOptions<Crawl4AiLocalOptions> options,
    IHttpClientFactory clients) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var audience = options.Value.CloudRunAudience;
        if (!string.IsNullOrWhiteSpace(audience))
        {
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Get,
                "http://metadata.google.internal/computeMetadata/v1/instance/service-accounts/default/identity" +
                $"?audience={Uri.EscapeDataString(audience.TrimEnd('/'))}&format=full");
            tokenRequest.Headers.Add("Metadata-Flavor", "Google");

            using var response = await clients.CreateClient("CloudRunMetadata")
                .SendAsync(tokenRequest, cancellationToken);
            response.EnsureSuccessStatusCode();
            var token = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            if (token.Length == 0)
            {
                throw new HttpRequestException("Cloud Run metadata returned an empty identity token.");
            }

            request.Headers.TryAddWithoutValidation("X-Serverless-Authorization", $"Bearer {token}");
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
