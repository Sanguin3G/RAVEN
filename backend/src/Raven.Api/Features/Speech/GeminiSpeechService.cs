using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Ai;
using Raven.Api.Features.ProviderCredentials;

namespace Raven.Api.Features.Speech;

public sealed class GeminiSpeechService(
    HttpClient http,
    IOptions<GeminiOptions> options,
    IProviderCredentialResolver? credentials = null)
{
    private const string LiveModel = "gemini-3.5-transcribe-live";
    private const string TtsModel = "gemini-3.8-flash-lite-tts";
    private static readonly HashSet<string> Voices = ["Kore", "Puck", "Charon", "Fenrir", "Aoede", "Leda", "Orus", "Zephyr"];

    public async Task<string> CreateLiveTokenAsync(string? language, CancellationToken ct)
    {
        var (configured, apiKey) = await GetOptionsAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var body = new
        {
            uses = 1,
            expireTime = now.AddMinutes(30),
            newSessionExpireTime = now.AddMinutes(1),
            liveConnectConstraints = new
            {
                model = $"models/{LiveModel}",
                config = new
                {
                    responseModalities = new[] { "TEXT" },
                    inputAudioTranscription = new { languageCodes = string.IsNullOrWhiteSpace(language) ? Array.Empty<string>() : new[] { language } }
                }
            }
        };
        using var response = await SendAsync(HttpMethod.Post, "auth_tokens", apiKey, body, ct);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var name = document.RootElement.TryGetProperty("name", out var value) ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(name)) throw new GeminiSpeechException(502, "Gemini did not provide a temporary speech token.");
        return name;
    }

    public async Task<GeminiSpeechAudioResponse> SynthesizeAsync(string text, string? voice, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4_000)
            throw new GeminiSpeechException(400, "Read-aloud text must contain 1 to 4,000 characters.");
        var selectedVoice = string.IsNullOrWhiteSpace(voice) ? "Kore" : voice.Trim();
        if (!Voices.Contains(selectedVoice)) throw new GeminiSpeechException(400, "Choose a supported Gemini voice.");
        var (configured, apiKey) = await GetOptionsAsync(ct);
        var body = new
        {
            model = TtsModel,
            input = new[] { new { type = "user_input", content = new[] { new { type = "text", text } } } },
            response_format = new { type = "audio" },
            generation_config = new { speech_config = new[] { new { voice = selectedVoice } } }
        };
        using var response = await SendAsync(HttpMethod.Post, "interactions", apiKey, body, ct);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var audio = document.RootElement.TryGetProperty("steps", out var steps) && steps.ValueKind == JsonValueKind.Array
            ? steps.EnumerateArray().SelectMany(step => step.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
                ? content.EnumerateArray() : []).LastOrDefault(part => part.TryGetProperty("type", out var type) && type.GetString() == "audio")
            : default;
        var data = audio.ValueKind == JsonValueKind.Object && audio.TryGetProperty("data", out var audioData) ? audioData.GetString() : null;
        if (string.IsNullOrWhiteSpace(data)) throw new GeminiSpeechException(502, "Gemini did not return speech audio.");
        return new GeminiSpeechAudioResponse(data, "audio/wav");
    }

    private async Task<(GeminiOptions Options, string ApiKey)> GetOptionsAsync(CancellationToken cancellationToken)
    {
        var configured = options.Value;
        var resolved = credentials is null
            ? null
            : await credentials.ResolveAsync(ProviderCredentialDefinitions.Gemini, cancellationToken);
        var apiKey = resolved?.Value ?? configured.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey)) throw new GeminiSpeechException(503, "Gemini speech is not configured on this server.");
        return (configured, apiKey);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string apiKey, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, $"{options.Value.ApiVersion}/{path}") { Content = JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        try
        {
            var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return response;
            var status = response.StatusCode switch
            {
                HttpStatusCode.TooManyRequests => 429,
                HttpStatusCode.ServiceUnavailable => 503,
                _ => 502
            };
            response.Dispose();
            throw new GeminiSpeechException(status, status == 429 ? "Gemini speech is rate limited. Try again later." : status == 503 ? "Gemini speech is temporarily busy." : "Gemini speech could not complete the request.");
        }
        catch (GeminiSpeechException) { throw; }
        catch (HttpRequestException) { throw new GeminiSpeechException(502, "Could not reach Gemini speech services."); }
    }
}

public sealed record GeminiLiveTokenResponse(string Token);
public sealed record GeminiSpeechRequest(string Text, string? Voice);
public sealed record GeminiSpeechAudioResponse(string AudioBase64, string MimeType);
public sealed class GeminiSpeechException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
