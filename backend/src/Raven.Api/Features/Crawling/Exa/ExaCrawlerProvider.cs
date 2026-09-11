using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Search;

namespace Raven.Api.Features.Crawling.Exa;

/// <summary>
/// Exa REST adapter for retrieving one page through the Contents API.
/// Exa discovery remains a separate <see cref="Search.ISearchProvider"/>
/// capability; this adapter only acquires the requested page content.
/// </summary>
public sealed class ExaCrawlerProvider(
    HttpClient httpClient,
    IOptions<ExaCrawlerOptions> options) : ICrawlerProvider
{
    public const string ProviderId = "exa";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Id => ProviderId;

    public async Task<CrawlResult> CrawlAsync(
        CrawlRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Url) ||
            !Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var requestedUri) ||
            requestedUri.Scheme is not ("http" or "https"))
        {
            return Failure(request.Url, "Only absolute HTTP(S) URLs can be crawled.");
        }

        var requestedUrl = request.Url.Trim();
        var configured = options.Value;
        EnsureConfigured(configured);

        var requestBody = new
        {
            urls = new[] { requestedUrl },
            text = new { maxCharacters = BoundTextCharacters(configured.MaxTextCharacters) }
        };

        try
        {
            using var message = new HttpRequestMessage(
                HttpMethod.Post,
                string.IsNullOrWhiteSpace(configured.ContentsPath) ? "/contents" : configured.ContentsPath);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // Exa supports x-api-key for server-side requests. Never include it
            // in a URL, response object, or exception message.
            message.Headers.TryAddWithoutValidation("x-api-key", configured.ApiKey!);
            message.Content = new StringContent(
                JsonSerializer.Serialize(requestBody, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await httpClient.SendAsync(message, cancellationToken);
            ThrowForHttpFailure(response.StatusCode);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return ReadResult(requestedUrl, document.RootElement, configured.MaxTextCharacters);
        }
        catch (ProviderException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new ProviderException(
                Id,
                "Exa Contents could not be reached.",
                ProviderFailureKind.Unavailable,
                exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderException(
                Id,
                "Exa Contents timed out.",
                ProviderFailureKind.Timeout,
                exception);
        }
        catch (JsonException exception)
        {
            throw new ProviderException(
                Id,
                "Exa Contents returned an invalid response.",
                ProviderFailureKind.InvalidResponse,
                exception);
        }
    }

    private static void EnsureConfigured(ExaCrawlerOptions configured)
    {
        if (string.IsNullOrWhiteSpace(configured.ApiKey))
        {
            throw new ProviderException(
                ProviderId,
                $"Exa Contents is not configured. Set {ExaCrawlerOptions.ApiKeyEnvironmentVariable}.",
                ProviderFailureKind.Configuration);
        }
    }

    private static void ThrowForHttpFailure(HttpStatusCode statusCode)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ProviderException(
                ProviderId,
                "Exa Contents rejected the configured API key.",
                ProviderFailureKind.Authentication);
        }

        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            throw new ProviderException(
                ProviderId,
                "Exa Contents rate limited the request.",
                ProviderFailureKind.RateLimited);
        }

        if (statusCode == HttpStatusCode.RequestTimeout)
        {
            throw new ProviderException(
                ProviderId,
                "Exa Contents timed out.",
                ProviderFailureKind.Timeout);
        }

        if ((int)statusCode >= 500)
        {
            throw new ProviderException(
                ProviderId,
                $"Exa Contents returned {(int)statusCode}.",
                ProviderFailureKind.Unavailable);
        }

        if (!((int)statusCode is >= 200 and <= 299))
        {
            // Keep this aligned with ExaSearchProvider: non-retryable 4xx
            // request errors are invalid provider responses, not unavailability.
            throw new ProviderException(
                ProviderId,
                $"Exa Contents rejected the request with {(int)statusCode}.",
                ProviderFailureKind.InvalidResponse);
        }
    }

    private static CrawlResult ReadResult(
        string requestedUrl,
        JsonElement root,
        int configuredMaxTextCharacters)
    {
        if (!root.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            throw new ProviderException(
                ProviderId,
                "Exa Contents response did not contain results.",
                ProviderFailureKind.InvalidResponse);
        }

        var status = ReadTargetStatus(root, requestedUrl);
        if (status is not null && !string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(requestedUrl, "Exa Contents could not retrieve the requested URL.");
        }

        var result = FindResult(results, requestedUrl);
        if (result is null)
        {
            return Failure(requestedUrl, "Exa Contents returned no result for the requested URL.");
        }

        var text = ReadOptionalString(result.Value, "text");
        if (string.IsNullOrWhiteSpace(text))
        {
            return Failure(requestedUrl, "Exa Contents returned no readable text.");
        }

        var finalUrl = ReadOptionalString(result.Value, "url");
        var title = ReadOptionalString(result.Value, "title");

        return new CrawlResult(
            ProviderId,
            requestedUrl,
            string.IsNullOrWhiteSpace(finalUrl) ? requestedUrl : finalUrl.Trim(),
            string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            TrimText(text, configuredMaxTextCharacters),
            true,
            null,
            DateTimeOffset.UtcNow);
    }

    private static string? ReadTargetStatus(JsonElement root, string requestedUrl)
    {
        if (!root.TryGetProperty("statuses", out var statuses))
        {
            return null;
        }

        if (statuses.ValueKind != JsonValueKind.Array)
        {
            throw new ProviderException(
                ProviderId,
                "Exa Contents response contained invalid statuses.",
                ProviderFailureKind.InvalidResponse);
        }

        var statusCount = statuses.GetArrayLength();
        foreach (var item in statuses.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = ReadOptionalString(item, "id");
            var isTarget = !string.IsNullOrWhiteSpace(id) && UrlMatches(id, requestedUrl);
            var isSingleUnidentifiedStatus = statusCount == 1 && string.IsNullOrWhiteSpace(id);
            if (!isTarget && !isSingleUnidentifiedStatus)
            {
                continue;
            }

            if (!item.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String)
            {
                throw new ProviderException(
                    ProviderId,
                    "Exa Contents response contained an invalid URL status.",
                    ProviderFailureKind.InvalidResponse);
            }

            return status.GetString();
        }

        return null;
    }

    private static JsonElement? FindResult(JsonElement results, string requestedUrl)
    {
        JsonElement? onlyResult = null;
        foreach (var item in results.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            onlyResult ??= item;
            var id = ReadOptionalString(item, "id");
            var url = ReadOptionalString(item, "url");
            if ((id is not null && UrlMatches(id, requestedUrl)) ||
                (url is not null && UrlMatches(url, requestedUrl)))
            {
                return item;
            }
        }

        // Exactly one URL was submitted. If Exa only supplies a rewritten final
        // URL and omits the original id, the single result is still unambiguous.
        return results.GetArrayLength() == 1 ? onlyResult : null;
    }

    private static bool UrlMatches(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static int BoundTextCharacters(int value) => Math.Clamp(value, 1_000, 100_000);

    private static string TrimText(string value, int configuredMaxCharacters)
    {
        var normalized = value.Trim();
        var maxCharacters = BoundTextCharacters(configuredMaxCharacters);
        if (normalized.Length <= maxCharacters)
        {
            return normalized;
        }

        var contentLength = Math.Max(0, maxCharacters - 1);
        return normalized[..contentLength].TrimEnd() + "…";
    }

    private static string? ReadOptionalString(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
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
