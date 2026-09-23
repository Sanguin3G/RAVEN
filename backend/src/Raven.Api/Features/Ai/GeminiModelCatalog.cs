using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Raven.Api.Features.Ai;

/// <summary>A RAVEN-approved Gemini generation model and its project availability.</summary>
public sealed record GeminiModelOptionResponse(string Id, string DisplayName, string Description, string Availability);

/// <summary>Safe model choices discovered for the configured Google AI project.</summary>
public sealed record GeminiModelCatalogResponse(bool ProjectAvailabilityVerified, string Message, IReadOnlyList<GeminiModelOptionResponse> Models);

public interface IGeminiModelCatalog
{
    Task<GeminiModelCatalogResponse> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Intersects Google's project model listing with the application's narrow
/// structured-generation compatibility catalog. This is metadata discovery only.
/// </summary>
public sealed class GeminiModelCatalog(HttpClient httpClient, IOptions<GeminiOptions> options) : IGeminiModelCatalog
{
    private static readonly GeminiModelOptionResponse[] CompatibilityCatalog =
    [
        new("gemini-3.5-flash-lite", "Gemini 3.5 Flash-Lite", "Fast · high-throughput · good for identity and interactive tasks", "Unverified"),
        new("gemini-3.5-flash", "Gemini 3.5 Flash", "Balanced capability and throughput for structured tasks", "Unverified"),
        new("gemini-3.8-flash", "Gemini 3.8 Flash", "Higher capability for synthesis and complex analysis", "Unverified")
    ];

    public async Task<GeminiModelCatalogResponse> GetAsync(CancellationToken cancellationToken = default)
    {
        var configured = options.Value;
        if (string.IsNullOrWhiteSpace(configured.ApiKey))
        {
            return Unverified("No Gemini API key is configured; project model availability cannot be checked.");
        }

        try
        {
            var baseUri = new Uri(configured.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            var discoveredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? pageToken = null;
            var listingComplete = false;

            // Google documents a maximum of 1,000 models per page. Continue only
            // when needed, and bound pagination so the Settings request stays cheap.
            for (var pageNumber = 0; pageNumber < 10; pageNumber++)
            {
                var query = "pageSize=1000" + (string.IsNullOrWhiteSpace(pageToken)
                    ? string.Empty
                    : $"&pageToken={Uri.EscapeDataString(pageToken)}");
                var modelsUri = new Uri(baseUri, $"{configured.ApiVersion.Trim('/')}/models?{query}");
                using var request = new HttpRequestMessage(HttpMethod.Get, modelsUri);
                request.Headers.Add("x-goog-api-key", configured.ApiKey);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return Unverified("Google AI project availability could not be checked. RAVEN's compatible models are still shown.");
                }

                var page = await response.Content.ReadFromJsonAsync<ModelsPage>(cancellationToken);
                foreach (var model in page?.Models ?? [])
                {
                    if (model.SupportedGenerationMethods?.Contains("generateContent", StringComparer.OrdinalIgnoreCase) != true)
                    {
                        continue;
                    }

                    foreach (var rawId in new[] { model.BaseModelId, model.Name })
                    {
                        var modelId = NormalizeModelId(rawId);
                        if (modelId is not null)
                        {
                            discoveredIds.Add(modelId);
                        }
                    }
                }

                if (CompatibilityCatalog.All(model => discoveredIds.Contains(model.Id)) ||
                    string.IsNullOrWhiteSpace(page?.NextPageToken))
                {
                    listingComplete = true;
                    break;
                }

                pageToken = page.NextPageToken;
            }

            if (!listingComplete)
            {
                return Unverified("Google's model list has more pages than RAVEN checked. Compatible model availability is unverified; try again later.");
            }

            var discovered = CompatibilityCatalog.Select(model => model with
            {
                Availability = discoveredIds.Contains(model.Id) ? "Available" : "Unavailable"
            }).ToArray();
            var availableCount = discovered.Count(model => model.Availability == "Available");
            var message = availableCount > 0
                ? "Availability checked against the configured Google AI project."
                : "Google returned no RAVEN-supported Gemini models for this API key/project. Check that the key belongs to the intended Google AI project and that Gemini API access is enabled.";
            return new GeminiModelCatalogResponse(true, message, discovered);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unverified("Google AI project availability could not be checked. RAVEN's compatible models are still shown.");
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException)
        {
            return Unverified("Google AI project availability could not be checked. RAVEN's compatible models are still shown.");
        }
    }

    private static GeminiModelCatalogResponse Unverified(string message) =>
        new(false, message, CompatibilityCatalog);

    private static string? NormalizeModelId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? normalized["models/".Length..]
            : normalized;
    }

    private sealed record ModelsPage(
        [property: JsonPropertyName("models")] ModelInfo[]? Models,
        [property: JsonPropertyName("nextPageToken")] string? NextPageToken);

    private sealed record ModelInfo(
        [property: JsonPropertyName("baseModelId")] string? BaseModelId,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("supportedGenerationMethods")] string[]? SupportedGenerationMethods);
}
