using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Raven.Api.Features.Search.Exa;

/// <summary>
/// Exa REST adapter. Provider-specific request and response shapes stay inside
/// this class; the research workflow consumes only the neutral search contract.
/// </summary>
public sealed class ExaSearchProvider(HttpClient httpClient, IOptions<ExaSearchOptions> options) : ISearchProvider
{
    public const string ProviderId = "exa";

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
        if (string.IsNullOrWhiteSpace(configured.ApiKey))
        {
            throw new ProviderException(
                Id,
                $"Exa Search is not configured. Set {ExaSearchOptions.ApiKeyEnvironmentVariable}.",
                ProviderFailureKind.Configuration);
        }

        var requestBody = new Dictionary<string, object?>
        {
            ["query"] = request.Query.Trim(),
            ["type"] = string.IsNullOrWhiteSpace(configured.SearchType) ? "auto" : configured.SearchType.Trim(),
            ["numResults"] = Math.Clamp(request.MaxResults, 1, 20),
            // Highlights provide useful discovery context without turning a
            // search call into a full document acquisition operation.
            ["contents"] = new { highlights = true }
        };
        var countryCode = NormalizeCountryCode(request.Country);
        if (countryCode is not null)
        {
            // Exa's documented location hint is an ISO country code. Human
            // readable country names are already represented in RAVEN's query
            // text and are deliberately not sent as an invalid API value.
            requestBody["userLocation"] = countryCode;
        }

        try
        {
            using var message = new HttpRequestMessage(
                HttpMethod.Post,
                string.IsNullOrWhiteSpace(configured.SearchPath) ? "/search" : configured.SearchPath);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // Exa documents x-api-key for server-side requests. It is kept in a
            // header, never in the URL or any result returned to the browser.
            message.Headers.TryAddWithoutValidation("x-api-key", configured.ApiKey);
            message.Content = new StringContent(
                JsonSerializer.Serialize(requestBody, JsonOptions),
                Encoding.UTF8,
                "application/json");

            using var response = await httpClient.SendAsync(message, cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ProviderException(
                    Id,
                    "Exa Search rejected the configured API key.",
                    ProviderFailureKind.Authentication);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new ProviderException(
                    Id,
                    "Exa Search rate limited the request.",
                    ProviderFailureKind.RateLimited);
            }

            if (response.StatusCode == HttpStatusCode.RequestTimeout)
            {
                throw new ProviderException(
                    Id,
                    "Exa Search timed out.",
                    ProviderFailureKind.Timeout);
            }

            if ((int)response.StatusCode >= 500)
            {
                throw new ProviderException(
                    Id,
                    $"Exa Search returned {(int)response.StatusCode}.",
                    ProviderFailureKind.Unavailable);
            }

            if (!response.IsSuccessStatusCode)
            {
                // The neutral failure taxonomy has no separate BadRequest kind;
                // Exa 4xx request/validation failures are invalid provider
                // responses and must not silently trigger a fallback as if Exa
                // were temporarily unavailable.
                throw new ProviderException(
                    Id,
                    $"Exa Search rejected the request with {(int)response.StatusCode}.",
                    ProviderFailureKind.InvalidResponse);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return new SearchResponse(Id, ReadResults(document.RootElement, configured.MaxSnippetCharacters));
        }
        catch (ProviderException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new ProviderException(
                Id,
                "Exa Search could not be reached.",
                ProviderFailureKind.Unavailable,
                exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderException(
                Id,
                "Exa Search timed out.",
                ProviderFailureKind.Timeout,
                exception);
        }
        catch (JsonException exception)
        {
            throw new ProviderException(
                Id,
                "Exa Search returned an invalid response.",
                ProviderFailureKind.InvalidResponse,
                exception);
        }
    }

    private static string? NormalizeCountryCode(string? country)
    {
        var value = country?.Trim();
        return value is { Length: 2 } && value.All(char.IsAsciiLetter)
            ? value.ToUpperInvariant()
            : null;
    }

    private static IReadOnlyList<SearchResult> ReadResults(JsonElement root, int configuredMaxSnippetCharacters)
    {
        if (!root.TryGetProperty("results", out var rawResults) || rawResults.ValueKind != JsonValueKind.Array)
        {
            throw new ProviderException(
                ProviderId,
                "Exa Search response did not contain results.",
                ProviderFailureKind.InvalidResponse);
        }

        var maxSnippetCharacters = Math.Clamp(configuredMaxSnippetCharacters, 0, 2_000);
        var results = new List<SearchResult>();
        var rank = 1;

        foreach (var rawResult in rawResults.EnumerateArray())
        {
            if (!TryReadString(rawResult, "url", out var url))
            {
                continue;
            }

            var title = ReadOptionalString(rawResult, "title") ?? url;
            var snippet = ReadSnippet(rawResult, maxSnippetCharacters);
            results.Add(new SearchResult(title, url, snippet, rank++));
        }

        return results;
    }

    private static string? ReadSnippet(JsonElement result, int maxCharacters)
    {
        var snippet = ReadOptionalString(result, "summary") ?? ReadOptionalString(result, "snippet");

        if (string.IsNullOrWhiteSpace(snippet) &&
            result.TryGetProperty("highlights", out var highlights) &&
            highlights.ValueKind == JsonValueKind.Array)
        {
            snippet = string.Join(" ", highlights.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        snippet ??= ReadOptionalString(result, "text");
        return TrimSnippet(snippet, maxCharacters);
    }

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
        parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryReadString(JsonElement parent, string propertyName, out string value)
    {
        value = ReadOptionalString(parent, propertyName) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}
