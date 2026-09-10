using System.Text.Json;

namespace Raven.Api.Features.Ai;

/// <summary>
/// Neutral boundary for a model that can return a structured JSON result.
/// Domain and workflow code should depend on this contract rather than on a
/// provider SDK or provider-specific request/response types.
/// </summary>
public interface IAiModelProvider
{
    string Id { get; }

    Task<AiModelResult> GenerateStructuredAsync(
        AiModelRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request for a bounded, structured generation operation.
/// </summary>
public sealed record AiModelRequest(
    string Model,
    string SystemInstruction,
    string Prompt,
    string PromptTemplateVersion,
    AiEvidencePayload Evidence,
    JsonElement ResponseSchema);

/// <summary>
/// Evidence and hints supplied to a model. The provider bounds the serialized
/// representation before sending it to an external service.
/// </summary>
public sealed record AiEvidencePayload(
    IReadOnlyDictionary<string, string?>? IdentityHints,
    IReadOnlyList<AiEvidenceItem> Sources)
{
    public static AiEvidencePayload Empty { get; } = new(null, []);
}

/// <summary>
/// A source-shaped evidence item without coupling the AI boundary to the
/// SourceDocument persistence model.
/// </summary>
public sealed record AiEvidenceItem(
    string SourceId,
    string SourceKind,
    string Title,
    string Url,
    string Content,
    IReadOnlyDictionary<string, string?>? StructuredFacts = null);

public sealed record AiModelResult(
    string Provider,
    string Model,
    JsonElement? StructuredJson,
    TimeSpan Duration,
    AiUsage? Usage = null,
    AiFailure? Failure = null,
    string? ExternalRequestId = null)
{
    public bool Succeeded => Failure is null && StructuredJson.HasValue;
}

/// <summary>
/// Token usage returned by the provider, when the provider exposes it.
/// </summary>
public sealed record AiUsage(
    int? PromptTokens = null,
    int? OutputTokens = null,
    int? TotalTokens = null,
    int? ThinkingTokens = null,
    int? CachedInputTokens = null);

/// <summary>
/// Sanitized failure data safe to persist in an execution event or return to a
/// caller. It intentionally does not carry provider response bodies.
/// </summary>
public sealed record AiFailure(
    string Code,
    string Message,
    bool Retryable,
    int? HttpStatus = null);
