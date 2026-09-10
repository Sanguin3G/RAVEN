using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Raven.Api.Features.Search;

public sealed class BraveSearchProvider(HttpClient httpClient, IOptions<BraveSearchOptions> options) : ISearchProvider
{
    public const string ProviderId = "brave";
    public string Id => ProviderId;

    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new ArgumentException("A search query is required.", nameof(request));
        }

        var configured = options.Value;
        if (string.IsNullOrWhiteSpace(configured.ApiKey))
        {
            throw new ProviderException(Id, "Brave Search is not configured. Set BRAVE_SEARCH_API_KEY.", ProviderFailureKind.Configuration);
        }

        var query = $"/res/v1/web/search?q={Uri.EscapeDataString(request.Query)}&count={Math.Clamp(request.MaxResults, 1, 20)}";
        // The Company identity form intentionally accepts a human-readable country
        // (for example, "Vietnam"). Brave accepts only ISO 3166-1 alpha-2 values
        // here, so never forward arbitrary user text as an external API parameter.
        var countryCode = NormalizeCountryCode(request.Country);
        if (countryCode is not null)
        {
            query += $"&country={countryCode}";
        }

        if (!string.IsNullOrWhiteSpace(request.Language))
        {
            query += $"&search_lang={Uri.EscapeDataString(request.Language)}";
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, query);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            message.Headers.Add("X-Subscription-Token", configured.ApiKey);
            using var response = await httpClient.SendAsync(message, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new ProviderException(Id, "Brave Search rejected the configured API key.", ProviderFailureKind.Authentication);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new ProviderException(Id, "Brave Search rate limited the request.", ProviderFailureKind.RateLimited);
            }

            if ((int)response.StatusCode >= 500)
            {
                throw new ProviderException(Id, $"Brave Search returned {(int)response.StatusCode}.", ProviderFailureKind.Unavailable);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderException(Id, $"Brave Search returned {(int)response.StatusCode}.", ProviderFailureKind.InvalidResponse);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var results = ReadResults(document.RootElement);
            return new SearchResponse(Id, results);
        }
        catch (ProviderException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new ProviderException(Id, "Brave Search could not be reached.", ProviderFailureKind.Unavailable, exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderException(Id, "Brave Search timed out.", ProviderFailureKind.Timeout, exception);
        }
        catch (JsonException exception)
        {
            throw new ProviderException(Id, "Brave Search returned an invalid response.", ProviderFailureKind.InvalidResponse, exception);
        }
    }

    private static string? NormalizeCountryCode(string? country)
    {
        var value = country?.Trim();
        return value is { Length: 2 } && value.All(char.IsAsciiLetter)
            ? value.ToUpperInvariant()
            : null;
    }

    private static IReadOnlyList<SearchResult> ReadResults(JsonElement root)
    {
        if (!root.TryGetProperty("web", out var web) || !web.TryGetProperty("results", out var rawResults) || rawResults.ValueKind != JsonValueKind.Array)
        {
            throw new ProviderException(ProviderId, "Brave Search response did not contain web results.", ProviderFailureKind.InvalidResponse);
        }

        var results = new List<SearchResult>();
        var rank = 1;
        foreach (var rawResult in rawResults.EnumerateArray())
        {
            if (!rawResult.TryGetProperty("url", out var urlProperty) || string.IsNullOrWhiteSpace(urlProperty.GetString()))
            {
                continue;
            }

            var title = rawResult.TryGetProperty("title", out var titleProperty) ? titleProperty.GetString() : null;
            var description = rawResult.TryGetProperty("description", out var descriptionProperty) ? descriptionProperty.GetString() : null;
            results.Add(new SearchResult(title ?? urlProperty.GetString()!, urlProperty.GetString()!, description, rank++));
        }

        return results;
    }
}
