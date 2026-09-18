using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Raven.Api.Features.ManagedResearch;

/// <summary>Configuration for the Exa Agent HTTP adapter.</summary>
public sealed class ExaAgentOptions
{
    public const string SectionName = "Providers:Exa";
    public const string ApiKeyEnvironmentVariable = "EXA_API_KEY";

    public string BaseUrl { get; set; } = "https://api.exa.ai";
    public string AgentRunsPath { get; set; } = "/agent/runs";
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
    public int PollIntervalMilliseconds { get; set; } = 2_000;
}

/// <summary>Safe provider failure categories for managed research jobs.</summary>
public enum ManagedResearchFailureKind
{
    Configuration,
    Authentication,
    RateLimited,
    Timeout,
    Unavailable,
    InvalidResponse
}

/// <summary>Provider failure that deliberately excludes credentials and raw response bodies.</summary>
public sealed class ManagedResearchProviderException : Exception
{
    public ManagedResearchProviderException(
        string message,
        ManagedResearchFailureKind kind,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public ManagedResearchFailureKind Kind { get; }
}

/// <summary>
/// Exa Agent adapter. The application sees only the provider-neutral client
/// contract; Exa's run/output envelope remains inside this class.
/// </summary>
public sealed class ExaAgentClient(
    HttpClient httpClient,
    IOptions<ExaAgentOptions> options) : IManagedResearchAgentClient
{
    public const string ProviderId = "exa-agent";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ManagedResearchProviderRun> CreateAsync(
        ManagedResearchAgentCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new ArgumentException("A managed research query is required.", nameof(request));
        }

        var configured = options.Value;
        EnsureConfigured(configured);
        var body = new
        {
            query = ManagedResearchText.Bound(request.Query, ManagedResearchLimits.MaxQueryLength),
            effort = ToProviderEffort(request.Effort),
            outputSchema = OutputSchema
        };

        using var message = CreateRequest(HttpMethod.Post, configured.AgentRunsPath, configured);
        message.Content = new StringContent(
            JsonSerializer.Serialize(body, JsonOptions),
            Encoding.UTF8,
            "application/json");
        return await SendAsync(message, configured, httpClient, cancellationToken);
    }

    public Task<ManagedResearchProviderRun> GetAsync(
        string providerRunId,
        CancellationToken cancellationToken = default)
    {
        var configured = options.Value;
        EnsureConfigured(configured);
        var path = JoinPath(configured.AgentRunsPath, Uri.EscapeDataString(RequireRunId(providerRunId)));
        return SendAsync(CreateRequest(HttpMethod.Get, path, configured), configured, httpClient, cancellationToken);
    }

    public Task<ManagedResearchProviderRun> CancelAsync(
        string providerRunId,
        CancellationToken cancellationToken = default)
    {
        var configured = options.Value;
        EnsureConfigured(configured);
        var path = JoinPath(configured.AgentRunsPath, Uri.EscapeDataString(RequireRunId(providerRunId)), "cancel");
        return SendAsync(CreateRequest(HttpMethod.Post, path, configured), configured, httpClient, cancellationToken);
    }

    private static readonly object OutputSchema = new
    {
        type = "object",
        properties = new
        {
            summary = new { type = "string" },
            claims = new
            {
                type = "array",
                maxItems = ManagedResearchLimits.MaxClaims,
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        topic = new { type = "string" },
                        statement = new { type = "string" },
                        supportingSourceUrls = new { type = "array", items = new { type = "string", format = "uri" } },
                        providerConfidence = new { type = "number" },
                        notes = new { type = "string" }
                    },
                    required = new[] { "topic", "statement" }
                }
            },
            sources = new
            {
                type = "array",
                maxItems = ManagedResearchLimits.MaxSources,
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        title = new { type = "string" },
                        url = new { type = "string", format = "uri" },
                        publisher = new { type = "string" },
                        date = new { type = "string" },
                        supports = new { type = "string" }
                    },
                    required = new[] { "title", "url" }
                }
            },
            uncertainties = new { type = "array", items = new { type = "string" } }
        },
        required = new[] { "summary", "claims", "sources", "uncertainties" }
    };

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, ExaAgentOptions configured)
    {
        var message = new HttpRequestMessage(method, string.IsNullOrWhiteSpace(path) ? "/agent/runs" : path);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        // The managed-research contract uses Bearer authentication. The key is
        // never included in a URL, result, or exception text.
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configured.ApiKey);
        return message;
    }

    private static async Task<ManagedResearchProviderRun> SendAsync(
        HttpRequestMessage message,
        ExaAgentOptions configured,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendWithTimeoutAsync(message, configured, httpClient, cancellationToken);
            ThrowForHttpFailure(response.StatusCode);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return ReadRun(document.RootElement);
        }
        catch (ManagedResearchProviderException)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            throw new ManagedResearchProviderException(
                "Exa Agent could not be reached.",
                ManagedResearchFailureKind.Unavailable,
                exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ManagedResearchProviderException(
                "Exa Agent timed out.",
                ManagedResearchFailureKind.Timeout,
                exception);
        }
        catch (JsonException exception)
        {
            throw new ManagedResearchProviderException(
                "Exa Agent returned an invalid response.",
                ManagedResearchFailureKind.InvalidResponse,
                exception);
        }
    }

    private static async Task<HttpResponseMessage> SendWithTimeoutAsync(
        HttpRequestMessage message,
        ExaAgentOptions configured,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Clamp(configured.TimeoutSeconds, 1, 3_600)));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        return await httpClient.SendAsync(message, linked.Token);
    }

    private static ManagedResearchProviderRun ReadRun(JsonElement root)
    {
        var run = root;
        if (TryGetProperty(root, out var wrapped, "agent_run", "agentRun", "data") && wrapped.ValueKind == JsonValueKind.Object)
        {
            run = wrapped;
        }

        var id = ReadString(run, "id", "runId");
        var statusValue = ReadString(run, "status")?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(id) || statusValue is null)
        {
            throw new ManagedResearchProviderException(
                "Exa Agent response did not contain a valid run.",
                ManagedResearchFailureKind.InvalidResponse);
        }

        var status = statusValue switch
        {
            "queued" or "created" => ManagedResearchProviderRunStatus.Queued,
            "running" or "in_progress" => ManagedResearchProviderRunStatus.Running,
            "completed" => ManagedResearchProviderRunStatus.Completed,
            "failed" or "error" => ManagedResearchProviderRunStatus.Failed,
            "cancelled" or "canceled" => ManagedResearchProviderRunStatus.Cancelled,
            _ => throw new ManagedResearchProviderException(
                "Exa Agent returned an unknown run status.",
                ManagedResearchFailureKind.InvalidResponse)
        };

        var output = ReadOutput(run);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var requestId = ReadString(run, "requestId");
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            metadata["requestId"] = ManagedResearchText.Bound(requestId, 200);
        }

        var cost = ReadDecimal(run, "costDollars", "cost");
        return new ManagedResearchProviderRun(
            id.Trim(),
            status,
            ProviderId,
            output,
            ReadDate(run, "createdAt", "created_at"),
            ReadDate(run, "completedAt", "completed_at"),
            ManagedResearchText.Bound(ReadString(run, "error", "failedReason", "failureReason"), ManagedResearchLimits.MaxErrorLength) is { Length: > 0 } error ? error : null,
            ReadString(run, "stopReason", "stop_reason"),
            cost,
            metadata);
    }

    private static ManagedResearchProviderOutput? ReadOutput(JsonElement run)
    {
        if (!TryGetProperty(run, out var rawOutput, "output") || rawOutput.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var text = ReadString(rawOutput, "text", "content");
        JsonElement? structured = null;
        if (TryGetProperty(rawOutput, out var rawStructured, "structured", "json") && rawStructured.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            structured = rawStructured.Clone();
        }

        var citations = new List<ManagedResearchCitation>();
        if (TryGetProperty(rawOutput, out var grounding, "grounding") && grounding.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in grounding.EnumerateArray())
            {
                var fieldName = ReadString(field, "field", "path");
                if (!TryGetProperty(field, out var rawCitations, "citations", "sources") || rawCitations.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var citation in rawCitations.EnumerateArray())
                {
                    var url = ReadString(citation, "url", "sourceUrl", "link");
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        citations.Add(new ManagedResearchCitation(
                            url.Trim(),
                            ReadString(citation, "title", "name"),
                            fieldName));
                    }
                }
            }
        }

        return new ManagedResearchProviderOutput(text, structured, citations);
    }

    private static void EnsureConfigured(ExaAgentOptions configured)
    {
        if (string.IsNullOrWhiteSpace(configured.ApiKey))
        {
            throw new ManagedResearchProviderException(
                $"Managed AI Research is not configured. Set {ExaAgentOptions.ApiKeyEnvironmentVariable}.",
                ManagedResearchFailureKind.Configuration);
        }
    }

    private static void ThrowForHttpFailure(HttpStatusCode statusCode)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ManagedResearchProviderException(
                "Exa Agent rejected the configured API key.",
                ManagedResearchFailureKind.Authentication);
        }

        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            throw new ManagedResearchProviderException(
                "Exa Agent rate limited the request.",
                ManagedResearchFailureKind.RateLimited);
        }

        if (statusCode == HttpStatusCode.RequestTimeout)
        {
            throw new ManagedResearchProviderException(
                "Exa Agent timed out.",
                ManagedResearchFailureKind.Timeout);
        }

        if ((int)statusCode >= 500)
        {
            throw new ManagedResearchProviderException(
                $"Exa Agent returned {(int)statusCode}.",
                ManagedResearchFailureKind.Unavailable);
        }

        if (!((int)statusCode is >= 200 and <= 299))
        {
            throw new ManagedResearchProviderException(
                $"Exa Agent rejected the request with {(int)statusCode}.",
                ManagedResearchFailureKind.InvalidResponse);
        }
    }

    private static string RequireRunId(string providerRunId) =>
        string.IsNullOrWhiteSpace(providerRunId)
            ? throw new ArgumentException("A provider run ID is required.", nameof(providerRunId))
            : providerRunId.Trim();

    private static string ToProviderEffort(ManagedResearchEffort effort) => effort switch
    {
        ManagedResearchEffort.Auto => "auto",
        ManagedResearchEffort.Low => "low",
        ManagedResearchEffort.Medium => "medium",
        ManagedResearchEffort.High => "high",
        ManagedResearchEffort.XHigh => "xhigh",
        _ => throw new ArgumentOutOfRangeException(nameof(effort))
    };

    private static string JoinPath(params string[] segments) =>
        string.Join('/', segments.Select(segment => segment.Trim('/')).Where(segment => segment.Length > 0).Prepend(string.Empty));

    private static string? ReadString(JsonElement parent, params string[] names) =>
        TryGetProperty(parent, out var value, names) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static decimal? ReadDecimal(JsonElement parent, params string[] names)
    {
        if (!TryGetProperty(parent, out var value, names))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static DateTimeOffset? ReadDate(JsonElement parent, params string[] names) =>
        DateTimeOffset.TryParse(ReadString(parent, names), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;

    private static bool TryGetProperty(JsonElement parent, out JsonElement value, params string[] names)
    {
        foreach (var property in parent.EnumerateObject())
        {
            if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
