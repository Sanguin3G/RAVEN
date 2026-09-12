using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Raven.Api.Features.Research.Events;

/// <summary>
/// Keeps telemetry useful without allowing credentials, headers, cookies, model
/// reasoning, or unbounded provider/document bodies into persisted events.
/// </summary>
public static partial class ResearchEventSanitizer
{
    public const int DefaultMaxSummaryCharacters = 2_000;
    public const int DefaultMaxMetadataCharacters = 4_000;
    private const int MaxMetadataValueCharacters = 1_024;

    private const string Redacted = "[REDACTED]";

    public static ResearchEvent Sanitize(
        ResearchEvent researchEvent,
        int maxSummaryCharacters = DefaultMaxSummaryCharacters,
        int maxMetadataCharacters = DefaultMaxMetadataCharacters)
    {
        ArgumentNullException.ThrowIfNull(researchEvent);

        return researchEvent with
        {
            Operation = ResearchEventSemantics.NormalizeOperation(researchEvent.Operation),
            Provider = SanitizeIdentifier(researchEvent.Provider),
            Model = SanitizeIdentifier(researchEvent.Model),
            ToolName = SanitizeIdentifier(researchEvent.ToolName),
            InputSummary = SanitizeSummary(researchEvent.InputSummary, maxSummaryCharacters),
            OutputSummary = SanitizeSummary(researchEvent.OutputSummary, maxSummaryCharacters),
            ExternalRequestId = SanitizeIdentifier(researchEvent.ExternalRequestId),
            PromptTemplateVersion = SanitizeIdentifier(researchEvent.PromptTemplateVersion),
            InputHash = SanitizeIdentifier(researchEvent.InputHash),
            ErrorCode = SanitizeIdentifier(researchEvent.ErrorCode),
            ErrorMessage = SanitizeSummary(researchEvent.ErrorMessage, maxSummaryCharacters),
            MetadataJson = SanitizeMetadataJson(researchEvent.MetadataJson, maxMetadataCharacters)
        };
    }

    public static ResearchEventDraft Sanitize(
        ResearchEventDraft draft,
        int maxSummaryCharacters = DefaultMaxSummaryCharacters,
        int maxMetadataCharacters = DefaultMaxMetadataCharacters)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return draft with
        {
            Operation = ResearchEventSemantics.NormalizeOperation(draft.Operation),
            Provider = SanitizeIdentifier(draft.Provider),
            Model = SanitizeIdentifier(draft.Model),
            ToolName = SanitizeIdentifier(draft.ToolName),
            InputSummary = SanitizeSummary(draft.InputSummary, maxSummaryCharacters),
            OutputSummary = SanitizeSummary(draft.OutputSummary, maxSummaryCharacters),
            ExternalRequestId = SanitizeIdentifier(draft.ExternalRequestId),
            PromptTemplateVersion = SanitizeIdentifier(draft.PromptTemplateVersion),
            InputHash = SanitizeIdentifier(draft.InputHash),
            ErrorCode = SanitizeIdentifier(draft.ErrorCode),
            ErrorMessage = SanitizeSummary(draft.ErrorMessage, maxSummaryCharacters),
            MetadataJson = SanitizeMetadataJson(draft.MetadataJson, maxMetadataCharacters)
        };
    }

    public static ResearchEvent Create(
        ResearchEventDraft draft,
        long sequence,
        DateTimeOffset? timestamp = null,
        int maxSummaryCharacters = DefaultMaxSummaryCharacters,
        int maxMetadataCharacters = DefaultMaxMetadataCharacters)
    {
        var safeDraft = Sanitize(draft, maxSummaryCharacters, maxMetadataCharacters);
        return new ResearchEvent
        {
            Id = Guid.NewGuid(),
            ResearchRunId = safeDraft.ResearchRunId,
            ConversationId = safeDraft.ConversationId,
            Sequence = sequence,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            DurationMs = safeDraft.DurationMs,
            Stage = safeDraft.Stage,
            Category = safeDraft.Category,
            Operation = safeDraft.Operation,
            Status = safeDraft.Status,
            Provider = safeDraft.Provider,
            Model = safeDraft.Model,
            ToolName = safeDraft.ToolName,
            InputSummary = safeDraft.InputSummary,
            OutputSummary = safeDraft.OutputSummary,
            HttpStatus = safeDraft.HttpStatus,
            ExternalRequestId = safeDraft.ExternalRequestId,
            InputTokens = safeDraft.InputTokens,
            OutputTokens = safeDraft.OutputTokens,
            CachedTokens = safeDraft.CachedTokens,
            ThinkingTokens = safeDraft.ThinkingTokens,
            EstimatedCost = safeDraft.EstimatedCost,
            PromptTemplateVersion = safeDraft.PromptTemplateVersion,
            InputHash = safeDraft.InputHash,
            ErrorCode = safeDraft.ErrorCode,
            ErrorMessage = safeDraft.ErrorMessage,
            MetadataJson = safeDraft.MetadataJson
        };
    }

    public static string? SanitizeSummary(string? summary, int maxCharacters = DefaultMaxSummaryCharacters)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        var boundedLength = NormalizeLimit(maxCharacters, DefaultMaxSummaryCharacters);
        var oneLine = WhitespaceRegex().Replace(summary, " ").Trim();
        var redacted = RedactSensitiveValues(oneLine);
        return Truncate(redacted, boundedLength);
    }

    public static string? SanitizeMetadataJson(string? metadataJson, int maxCharacters = DefaultMaxMetadataCharacters)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        var boundedLength = NormalizeLimit(maxCharacters, DefaultMaxMetadataCharacters);
        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            var sanitized = SanitizeJsonElement(document.RootElement, boundedLength);
            var serialized = sanitized?.ToJsonString() ?? "null";
            if (serialized.Length <= boundedLength)
            {
                return serialized;
            }

            // Preserve valid JSON after bounding a large arbitrary metadata tree.
            return JsonSerializer.Serialize(new
            {
                truncated = true,
                preview = Truncate(RedactSensitiveValues(serialized), Math.Max(1, boundedLength - 32))
            });
        }
        catch (JsonException)
        {
            // Metadata is diagnostic, not a reason to fail a research operation.
            // Wrap malformed input so the persisted value remains valid JSON.
            return JsonSerializer.Serialize(new
            {
                raw = SanitizeSummary(metadataJson, Math.Max(1, boundedLength - 16))
            });
        }
    }

    private static JsonNode? SanitizeJsonElement(JsonElement element, int maxCharacters)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var result = new JsonObject();
                foreach (var property in element.EnumerateObject())
                {
                    result[property.Name] = IsSensitiveName(property.Name)
                        ? Redacted
                        : SanitizeJsonElement(property.Value, maxCharacters);
                }

                return result;
            }
            case JsonValueKind.Array:
            {
                var result = new JsonArray();
                foreach (var item in element.EnumerateArray())
                {
                    result.Add(SanitizeJsonElement(item, maxCharacters));
                }

                return result;
            }
            case JsonValueKind.String:
                return SanitizeSummary(
                    element.GetString(),
                    Math.Min(MaxMetadataValueCharacters, Math.Max(64, maxCharacters / 4)));
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                return JsonNode.Parse(element.GetRawText());
            case JsonValueKind.Null:
                return null;
            default:
                return null;
        }
    }

    private static string RedactSensitiveValues(string value)
    {
        var redacted = AuthorizationHeaderRegex().Replace(value, "$1" + Redacted);
        redacted = CookieHeaderRegex().Replace(redacted, "$1" + Redacted);
        redacted = SensitiveAssignmentRegex().Replace(redacted, "$1" + Redacted);
        redacted = BearerTokenRegex().Replace(redacted, "$1" + Redacted);
        return redacted;
    }

    private static bool IsSensitiveName(string name) =>
        SensitiveNameRegex().IsMatch(name);

    private static string? SanitizeIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Truncate(WhitespaceRegex().Replace(value, " ").Trim(), 256);
    }

    private static int NormalizeLimit(int requested, int fallback) =>
        requested > 0 ? Math.Min(requested, 16_384) : fallback;

    private static string Truncate(string value, int maxCharacters)
    {
        if (value.Length <= maxCharacters)
        {
            return value;
        }

        return maxCharacters == 1
            ? "…"
            : value[..(maxCharacters - 1)] + "…";
    }

    [GeneratedRegex(@"(?im)(\b(?:authorization|proxy-authorization)\s*:\s*)[^\r\n]*")]
    private static partial Regex AuthorizationHeaderRegex();

    [GeneratedRegex(@"(?im)(\b(?:cookie|set-cookie)\s*:\s*)[^\r\n]*")]
    private static partial Regex CookieHeaderRegex();

    [GeneratedRegex(@"(?im)(\b(?:api[-_ ]?key|access[-_ ]?token|refresh[-_ ]?token|id[-_ ]?token|password|secret|client[-_ ]?secret|cookie|set-cookie)\b\s*[:=]\s*)(?:[""']?)[^""'\s,;}]+(?:[""']?)")]
    private static partial Regex SensitiveAssignmentRegex();

    [GeneratedRegex(@"(?i)(\bbearer\s+)[A-Za-z0-9._~+/=-]+")]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"(?i)\b(?:api[-_ ]?key|access[-_ ]?token|refresh[-_ ]?token|id[-_ ]?token|authorization|proxy[-_ ]?authorization|password|secret|client[-_ ]?secret|cookie|set-cookie|chain[-_ ]?of[-_ ]?thought|reasoning|thoughts?)\b")]
    private static partial Regex SensitiveNameRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
