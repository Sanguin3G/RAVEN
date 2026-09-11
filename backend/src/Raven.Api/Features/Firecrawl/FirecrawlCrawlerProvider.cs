using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Crawling;
using Raven.Api.Features.Search;

namespace Raven.Api.Features.Firecrawl;

/// <summary>
/// Firecrawl single-page scrape adapter. A crawl provider is deliberately
/// limited to one requested URL here; domain crawling remains an explicit
/// research-planner concern.
/// </summary>
public sealed class FirecrawlCrawlerProvider(
    HttpClient httpClient,
    IOptions<FirecrawlOptions> options) : ICrawlerProvider
{
    public const string ProviderId = "firecrawl";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Id => ProviderId;

    public async Task<CrawlResult> CrawlAsync(
        CrawlRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var requestedUri) ||
            requestedUri.Scheme is not ("http" or "https"))
        {
            return Failure(request.Url, "Only absolute HTTP(S) URLs can be crawled.");
        }

        var configured = options.Value;
        EnsureConfigured(configured);

        var requestBody = new
        {
            url = request.Url,
            formats = new[] { "markdown" },
            onlyMainContent = true,
            removeBase64Images = true,
            blockAds = true,
            timeout = ToTimeoutMilliseconds(configured.TimeoutSeconds)
        };

        try
        {
            using var message = new HttpRequestMessage(
                HttpMethod.Post,
                string.IsNullOrWhiteSpace(configured.ScrapePath) ? "/v2/scrape" : configured.ScrapePath);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configured.ApiKey!);
            message.Content = new StringContent(
                JsonSerializer.Serialize(requestBody, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await httpClient.SendAsync(message, cancellationToken);
            ThrowForHttpFailure(response.StatusCode);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return ReadResult(request.Url, document.RootElement, configured.MaxMarkdownCharacters);
        }
        catch (ProviderException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new ProviderException(
                Id,
                "Firecrawl could not be reached.",
                ProviderFailureKind.Unavailable,
                exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderException(
                Id,
                "Firecrawl timed out.",
                ProviderFailureKind.Timeout,
                exception);
        }
        catch (JsonException exception)
        {
            throw new ProviderException(
                Id,
                "Firecrawl returned an invalid response.",
                ProviderFailureKind.InvalidResponse,
                exception);
        }
    }

    private static void EnsureConfigured(FirecrawlOptions configured)
    {
        if (string.IsNullOrWhiteSpace(configured.ApiKey))
        {
            throw new ProviderException(
                ProviderId,
                $"Firecrawl is not configured. Set {FirecrawlOptions.ApiKeyEnvironmentVariable}.",
                ProviderFailureKind.Configuration);
        }
    }

    private static void ThrowForHttpFailure(HttpStatusCode statusCode)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ProviderException(
                ProviderId,
                "Firecrawl rejected the configured API key.",
                ProviderFailureKind.Authentication);
        }

        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            throw new ProviderException(
                ProviderId,
                "Firecrawl rate limited the request.",
                ProviderFailureKind.RateLimited);
        }

        if (statusCode == HttpStatusCode.RequestTimeout)
        {
            throw new ProviderException(
                ProviderId,
                "Firecrawl timed out.",
                ProviderFailureKind.Timeout);
        }

        if (statusCode == HttpStatusCode.PaymentRequired)
        {
            throw new ProviderException(
                ProviderId,
                "Firecrawl account configuration does not allow this request.",
                ProviderFailureKind.Configuration);
        }

        if ((int)statusCode >= 500)
        {
            throw new ProviderException(
                ProviderId,
                $"Firecrawl returned {(int)statusCode}.",
                ProviderFailureKind.Unavailable);
        }

        if (!((int)statusCode is >= 200 and <= 299))
        {
            throw new ProviderException(
                ProviderId,
                $"Firecrawl rejected the request with {(int)statusCode}.",
                ProviderFailureKind.InvalidResponse);
        }
    }

    private static CrawlResult ReadResult(
        string requestedUrl,
        JsonElement root,
        int configuredMaxMarkdownCharacters)
    {
        if (!root.TryGetProperty("success", out var success) ||
            success.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
        {
            return Failure(requestedUrl, "Firecrawl did not return a successful scrape result.");
        }

        var markdown = ReadOptionalString(data, "markdown");
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return Failure(requestedUrl, "Firecrawl returned no readable markdown content.");
        }

        var metadata = data.TryGetProperty("metadata", out var metadataElement) &&
                       metadataElement.ValueKind == JsonValueKind.Object
            ? metadataElement
            : default;
        var title = ReadOptionalString(metadata, "title");
        var finalUrl = ReadOptionalString(metadata, "url") ??
                       ReadOptionalString(metadata, "sourceURL") ??
                       requestedUrl;

        return new CrawlResult(
            ProviderId,
            requestedUrl,
            finalUrl,
            title,
            TrimMarkdown(markdown, configuredMaxMarkdownCharacters),
            true,
            null,
            DateTimeOffset.UtcNow);
    }

    private static int ToTimeoutMilliseconds(int timeoutSeconds) =>
        Math.Clamp(timeoutSeconds, 1, 300) * 1_000;

    private static string? TrimMarkdown(string value, int configuredMaxCharacters)
    {
        var maxCharacters = Math.Clamp(configuredMaxCharacters, 1_000, 1_000_000);
        var normalized = value.Trim();
        if (normalized.Length <= maxCharacters)
        {
            return normalized;
        }

        var contentLength = Math.Max(0, maxCharacters - 1);
        return normalized[..contentLength].TrimEnd() + "…";
    }

    private static string? ReadOptionalString(JsonElement parent, string propertyName) =>
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static CrawlResult Failure(string requestedUrl, string error) =>
        new(
            ProviderId,
            requestedUrl,
            null,
            null,
            null,
            false,
            error,
            DateTimeOffset.UtcNow);
}
