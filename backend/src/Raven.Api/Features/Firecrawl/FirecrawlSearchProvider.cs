using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Search;

namespace Raven.Api.Features.Firecrawl;

/// <summary>
/// Firecrawl web-search adapter.
///
/// Firecrawl-specific request/response DTOs stay inside this adapter. The
/// research workflow receives only RAVEN's neutral <see cref="SearchResponse"/>
/// contract.
/// </summary>
public sealed class FirecrawlSearchProvider(
    HttpClient httpClient,
    IOptions<FirecrawlOptions> options) : ISearchProvider
{
    public const string ProviderId = "firecrawl-search";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Id => ProviderId;

    public async Task<SearchResponse> SearchAsync(
        SearchRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new ArgumentException("A search query is required.", nameof(request));
        }

        var configured = options.Value;
        EnsureConfigured(configured);

        var maxResults = Math.Clamp(request.MaxResults, 1, 20);
        var requestBody = new Dictionary<string, object?>
        {
            ["query"] = request.Query.Trim(),
            ["limit"] = maxResults,
            ["sources"] = new[] { "web" },
            ["timeout"] = ToTimeoutMilliseconds(configured.TimeoutSeconds)
        };

        var countryCode = NormalizeCountryCode(request.Country);
        if (countryCode is not null)
        {
            requestBody["country"] = countryCode;
        }

        try
        {
            using var message = CreateRequest(
                HttpMethod.Post,
                configured.SearchPath,
                configured.ApiKey!,
                requestBody);
            using var response = await httpClient.SendAsync(message, cancellationToken);

            ThrowForHttpFailure(response.StatusCode);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return new SearchResponse(
                Id,
                ReadResults(document.RootElement, maxResults, configured.MaxSnippetCharacters));
        }
        catch (ProviderException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new ProviderException(
                Id,
                "Firecrawl Search could not be reached.",
                ProviderFailureKind.Unavailable,
                exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderException(
                Id,
                "Firecrawl Search timed out.",
                ProviderFailureKind.Timeout,
                exception);
        }
        catch (JsonException exception)
        {
            throw new ProviderException(
                Id,
                "Firecrawl Search returned an invalid response.",
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
                $"Firecrawl Search is not configured. Set {FirecrawlOptions.ApiKeyEnvironmentVariable}.",
                ProviderFailureKind.Configuration);
        }
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string? path,
        string apiKey,
        object body)
    {
        var message = new HttpRequestMessage(
            method,
            string.IsNullOrWhiteSpace(path) ? "/v2/search" : path);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions),
            Encoding.UTF8,
            "application/json");
        return message;
    }

    private static void ThrowForHttpFailure(HttpStatusCode statusCode)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ProviderException(
                ProviderId,
                "Firecrawl Search rejected the configured API key.",
                ProviderFailureKind.Authentication);
        }

        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            throw new ProviderException(
                ProviderId,
                "Firecrawl Search rate limited the request.",
                ProviderFailureKind.RateLimited);
        }

        if (statusCode == HttpStatusCode.RequestTimeout)
        {
            throw new ProviderException(
                ProviderId,
                "Firecrawl Search timed out.",
                ProviderFailureKind.Timeout);
        }

        if (statusCode == HttpStatusCode.PaymentRequired)
        {
            throw new ProviderException(
                ProviderId,
                "Firecrawl Search account configuration does not allow this request.",
                ProviderFailureKind.Configuration);
        }

        if ((int)statusCode >= 500)
        {
            throw new ProviderException(
                ProviderId,
                $"Firecrawl Search returned {(int)statusCode}.",
                ProviderFailureKind.Unavailable);
        }

        if (!((int)statusCode is >= 200 and <= 299))
        {
            throw new ProviderException(
                ProviderId,
                $"Firecrawl Search rejected the request with {(int)statusCode}.",
                ProviderFailureKind.InvalidResponse);
        }
    }

    private static IReadOnlyList<SearchResult> ReadResults(
        JsonElement root,
        int maxResults,
        int configuredMaxSnippetCharacters)
    {
        if (!root.TryGetProperty("success", out var success) ||
            success.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("web", out var rawResults) ||
            rawResults.ValueKind != JsonValueKind.Array)
        {
            throw new ProviderException(
                ProviderId,
                "Firecrawl Search response did not contain web results.",
                ProviderFailureKind.InvalidResponse);
        }

        var maxSnippetCharacters = Math.Clamp(configuredMaxSnippetCharacters, 0, 2_000);
        var results = new List<SearchResult>(Math.Min(maxResults, rawResults.GetArrayLength()));
        var rank = 1;
        foreach (var rawResult in rawResults.EnumerateArray())
        {
            if (results.Count >= maxResults ||
                !TryReadHttpUrl(rawResult, "url", out var url))
            {
                continue;
            }

            var metadata = rawResult.TryGetProperty("metadata", out var metadataElement) &&
                           metadataElement.ValueKind == JsonValueKind.Object
                ? metadataElement
                : default;
            var title = ReadOptionalString(rawResult, "title") ??
                        ReadOptionalString(metadata, "title") ??
                        url;
            var snippet = ReadOptionalString(rawResult, "description") ??
                          ReadOptionalString(metadata, "description") ??
                          ReadOptionalString(rawResult, "markdown");

            results.Add(new SearchResult(
                title,
                url,
                TrimSnippet(snippet, maxSnippetCharacters),
                rank++));
        }

        return results;
    }

    private static string? NormalizeCountryCode(string? country)
    {
        var value = country?.Trim();
        return value is { Length: 2 } && value.All(char.IsAsciiLetter)
            ? value.ToUpperInvariant()
            : null;
    }

    private static int ToTimeoutMilliseconds(int timeoutSeconds) =>
        Math.Clamp(timeoutSeconds, 1, 300) * 1_000;

    private static string? TrimSnippet(string? value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value) || maxCharacters == 0)
        {
            return null;
        }

        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
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

    private static bool TryReadHttpUrl(JsonElement parent, string propertyName, out string value)
    {
        value = ReadOptionalString(parent, propertyName) ?? string.Empty;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               uri.Scheme is "http" or "https";
    }
}
