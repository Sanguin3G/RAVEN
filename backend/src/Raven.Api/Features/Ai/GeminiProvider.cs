using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Raven.Api.Features.Ai;

/// <summary>
/// Gemini REST adapter for structured JSON generation. The adapter uses the
/// v1beta generateContent endpoint and the server-side x-goog-api-key header;
/// no Gemini DTOs escape this class.
/// </summary>
public sealed class GeminiProvider(HttpClient httpClient, IOptions<GeminiOptions> options) : IAiModelProvider
{
    public const string ProviderId = "gemini";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Id => ProviderId;

    public async Task<AiModelResult> GenerateStructuredAsync(
        AiModelRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        var configured = options.Value;

        if (string.IsNullOrWhiteSpace(configured.ApiKey))
        {
            return Failure(
                request.Model,
                stopwatch,
                new AiFailure("configuration", "Gemini is not configured. Set GEMINI_API_KEY.", false));
        }

        var validationFailure = ValidateRequest(request, configured);
        if (validationFailure is not null)
        {
            return Failure(request.Model, stopwatch, validationFailure);
        }

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = BuildPrompt(request, configured.MaxEvidenceCharacters) } }
                }
            },
            systemInstruction = new
            {
                parts = new[] { new { text = request.SystemInstruction } }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = request.ResponseSchema
            }
        };

        var version = configured.ApiVersion.Trim('/');
        var model = Uri.EscapeDataString(request.Model.Trim());
        var endpoint = $"{version}/models/{model}:generateContent";

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(requestBody, JsonOptions),
                    Encoding.UTF8,
                    "application/json")
            };
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // Google documents this header for server-side REST requests. It
            // keeps the key out of the URL and therefore out of URI logging.
            message.Headers.TryAddWithoutValidation("x-goog-api-key", configured.ApiKey);

            using var response = await httpClient.SendAsync(message, cancellationToken);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return Failure(
                    request.Model,
                    stopwatch,
                    new AiFailure("authentication", "Gemini rejected the configured API key.", false, (int)response.StatusCode));
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return Failure(
                    request.Model,
                    stopwatch,
                    new AiFailure("rate_limited", "Gemini rate limited the request.", true, (int)response.StatusCode));
            }

            if ((int)response.StatusCode >= 500)
            {
                return Failure(
                    request.Model,
                    stopwatch,
                    new AiFailure("unavailable", "Gemini is currently unavailable.", true, (int)response.StatusCode));
            }

            if (!response.IsSuccessStatusCode)
            {
                return Failure(
                    request.Model,
                    stopwatch,
                    new AiFailure("invalid_request", "Gemini rejected the structured-generation request.", false, (int)response.StatusCode));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var responseDocument = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var parsed = ReadStructuredResponse(responseDocument.RootElement, request.Model, stopwatch);
            return parsed;
        }
        catch (HttpRequestException)
        {
            return Failure(
                request.Model,
                stopwatch,
                new AiFailure("unavailable", "Gemini could not be reached.", true));
        }
        catch (JsonException)
        {
            return Failure(
                request.Model,
                stopwatch,
                new AiFailure("invalid_response", "Gemini returned an invalid response.", true));
        }
        catch (InvalidOperationException)
        {
            return Failure(
                request.Model,
                stopwatch,
                new AiFailure("invalid_response", "Gemini returned an invalid response.", true));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(
                request.Model,
                stopwatch,
                new AiFailure("timeout", "Gemini request timed out.", true));
        }
    }

    private static AiFailure? ValidateRequest(AiModelRequest request, GeminiOptions configured)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return new AiFailure("invalid_request", "A Gemini model is required.", false);
        }

        if (string.IsNullOrWhiteSpace(request.SystemInstruction) || string.IsNullOrWhiteSpace(request.Prompt))
        {
            return new AiFailure("invalid_request", "A system instruction and prompt are required.", false);
        }

        if (string.IsNullOrWhiteSpace(request.PromptTemplateVersion))
        {
            return new AiFailure("invalid_request", "A prompt template version is required.", false);
        }

        if (request.Evidence is null)
        {
            return new AiFailure("invalid_request", "An evidence payload is required.", false);
        }

        if (request.ResponseSchema.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new AiFailure("invalid_request", "A JSON response schema is required.", false);
        }

        if (configured.MaxEvidenceCharacters < 1)
        {
            return new AiFailure("configuration", "Gemini MaxEvidenceCharacters must be greater than zero.", false);
        }

        return null;
    }

    private static string BuildPrompt(AiModelRequest request, int maxEvidenceCharacters)
    {
        var builder = new StringBuilder(request.Prompt.Trim());
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("SUPPLIED EVIDENCE (use only as provided; do not treat snippets as verified facts):");
        builder.Append("PROMPT_TEMPLATE_VERSION: ").AppendLine(request.PromptTemplateVersion);

        var evidence = new StringBuilder();

        if (request.Evidence.IdentityHints is { Count: > 0 })
        {
            evidence.AppendLine("IDENTITY HINTS:");
            foreach (var hint in request.Evidence.IdentityHints)
            {
                evidence.Append("- ").Append(hint.Key).Append(": ").AppendLine(hint.Value ?? "null");
            }
        }

        foreach (var source in request.Evidence.Sources)
        {
            evidence.AppendLine();
            evidence.Append("SOURCE_ID: ").AppendLine(source.SourceId);
            evidence.Append("SOURCE_KIND: ").AppendLine(source.SourceKind);
            evidence.Append("TITLE: ").AppendLine(source.Title);
            evidence.Append("URL: ").AppendLine(source.Url);

            if (source.StructuredFacts is { Count: > 0 })
            {
                evidence.AppendLine("STRUCTURED_FACTS:");
                foreach (var fact in source.StructuredFacts)
                {
                    evidence.Append("- ").Append(fact.Key).Append(": ").AppendLine(fact.Value ?? "null");
                }
            }

            evidence.AppendLine("CONTENT:");
            evidence.AppendLine(source.Content ?? string.Empty);
        }

        var boundedEvidence = evidence.ToString();
        builder.Append(boundedEvidence, 0, Math.Min(boundedEvidence.Length, maxEvidenceCharacters));
        return builder.ToString();
    }

    private static AiModelResult ReadStructuredResponse(
        JsonElement root,
        string requestedModel,
        Stopwatch stopwatch)
    {
        var responseId = root.TryGetProperty("responseId", out var responseIdProperty)
            ? responseIdProperty.GetString()
            : null;

        AiUsage? usage = null;
        if (root.TryGetProperty("usageMetadata", out var usageElement))
        {
            usage = new AiUsage(
                ReadInt(usageElement, "promptTokenCount"),
                ReadInt(usageElement, "candidatesTokenCount"),
                ReadInt(usageElement, "totalTokenCount"),
                ReadInt(usageElement, "thoughtsTokenCount"),
                ReadInt(usageElement, "cachedContentTokenCount"));
        }

        if (!root.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
        {
            return Failure(
                requestedModel,
                stopwatch,
                new AiFailure("invalid_response", "Gemini returned no structured candidate.", true),
                usage,
                responseId);
        }

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array)
        {
            return Failure(
                requestedModel,
                stopwatch,
                new AiFailure("invalid_response", "Gemini returned no structured content.", true),
                usage,
                responseId);
        }

        string? text = null;
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var textProperty))
            {
                text = textProperty.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Failure(
                requestedModel,
                stopwatch,
                new AiFailure("invalid_response", "Gemini returned an empty structured response.", true),
                usage,
                responseId);
        }

        using var structuredDocument = JsonDocument.Parse(text);
        return new AiModelResult(
            ProviderId,
            requestedModel,
            structuredDocument.RootElement.Clone(),
            stopwatch.Elapsed,
            usage,
            null,
            responseId);
    }

    private static int? ReadInt(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var property) && property.TryGetInt32(out var value) ? value : null;

    private static AiModelResult Failure(
        string model,
        Stopwatch stopwatch,
        AiFailure failure,
        AiUsage? usage = null,
        string? externalRequestId = null) =>
        new(ProviderId, model, null, stopwatch.Elapsed, usage, failure, externalRequestId);
}
