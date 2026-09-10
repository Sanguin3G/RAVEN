using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Search;

namespace Raven.Api.Features.Crawling;

public sealed class Crawl4AiLocalProvider(HttpClient httpClient, IOptions<Crawl4AiLocalOptions> options) : ICrawlerProvider
{
    public const string ProviderId = "crawl4ai-local";
    public string Id => ProviderId;

    public async Task<CrawlResult> CrawlAsync(CrawlRequest request, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var requestedUri) || requestedUri.Scheme is not ("http" or "https"))
        {
            return Failure(request.Url, "Only absolute HTTP(S) URLs can be crawled.");
        }

        var configured = options.Value;
        if (string.IsNullOrWhiteSpace(configured.ApiToken))
        {
            throw new ProviderException(Id, "Crawl4AI Local is not configured. Set CRAWL4AI_API_TOKEN.", ProviderFailureKind.Configuration);
        }

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, configured.CrawlPath);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configured.ApiToken);
            message.Content = new StringContent(
                JsonSerializer.Serialize(new { urls = new[] { request.Url }, browser_config = new { }, crawler_config = new { } }),
                Encoding.UTF8,
                "application/json");
            using var response = await httpClient.SendAsync(message, cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ProviderException(Id, "Crawl4AI Local rejected the configured API token.", ProviderFailureKind.Authentication);
            }

            if (!response.IsSuccessStatusCode)
            {
                return Failure(request.Url, await ReadErrorAsync(response, cancellationToken));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return ReadResult(request.Url, document.RootElement);
        }
        catch (ProviderException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return Failure(request.Url, "Crawl4AI Local could not be reached.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(request.Url, "Crawl4AI Local timed out.");
        }
        catch (JsonException)
        {
            return Failure(request.Url, "Crawl4AI Local returned an invalid response.");
        }
    }

    private static CrawlResult ReadResult(string requestedUrl, JsonElement root)
    {
        if (!root.TryGetProperty("success", out var rootSuccess) || !rootSuccess.GetBoolean() ||
            !root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
        {
            return Failure(requestedUrl, ReadString(root, "error") ?? "Crawl4AI Local did not return a crawl result.");
        }

        var result = results[0];
        if (!result.TryGetProperty("success", out var success) || !success.GetBoolean())
        {
            return Failure(requestedUrl, ReadString(result, "error_message") ?? ReadString(result, "error") ?? "Crawl4AI Local could not crawl the URL.");
        }

        var markdown = ReadMarkdown(result);
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return Failure(requestedUrl, "Crawl4AI Local returned no readable content.");
        }

        var title = result.TryGetProperty("metadata", out var metadata) ? ReadString(metadata, "title") : null;
        return new CrawlResult(
            ProviderId,
            requestedUrl,
            ReadString(result, "url") ?? requestedUrl,
            title,
            markdown.Trim(),
            true,
            null,
            DateTimeOffset.UtcNow);
    }

    private static string? ReadMarkdown(JsonElement result)
    {
        if (!result.TryGetProperty("markdown", out var markdown))
        {
            return null;
        }

        return markdown.ValueKind switch
        {
            JsonValueKind.String => markdown.GetString(),
            JsonValueKind.Object => FirstNonEmpty(
                ReadString(markdown, "fit_markdown"),
                ReadString(markdown, "raw_markdown")),
            _ => null
        };
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(body) ? $"Crawl4AI Local returned {(int)response.StatusCode}." : $"Crawl4AI Local returned {(int)response.StatusCode}: {body[..Math.Min(body.Length, 500)]}";
    }

    private static CrawlResult Failure(string requestedUrl, string error) =>
        new(ProviderId, requestedUrl, null, null, null, false, error, DateTimeOffset.UtcNow);
}
