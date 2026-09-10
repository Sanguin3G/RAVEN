using Raven.Api.Features.Research.Events;

namespace Raven.Api.Tests;

public sealed class ResearchEventSanitizerTests
{
    [Fact]
    public void SanitizeSummary_redacts_keys_headers_bearer_tokens_and_cookies()
    {
        const string apiKey = "brave-key-that-must-not-survive";
        const string bearer = "eyJhbGciOiJIUzI1NiJ9.secret.payload";
        const string cookie = "session=private-cookie-value";

        var result = ResearchEventSanitizer.SanitizeSummary(
            $"api_key={apiKey}; Authorization: Bearer {bearer}; Cookie: {cookie}");

        Assert.NotNull(result);
        Assert.DoesNotContain(apiKey, result, StringComparison.Ordinal);
        Assert.DoesNotContain(bearer, result, StringComparison.Ordinal);
        Assert.DoesNotContain(cookie, result, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeSummary_bounds_large_document_or_provider_body()
    {
        var result = ResearchEventSanitizer.SanitizeSummary(new string('x', 20_000));

        Assert.NotNull(result);
        Assert.Equal(ResearchEventSanitizer.DefaultMaxSummaryCharacters, result.Length);
        Assert.EndsWith("…", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeMetadataJson_preserves_shape_but_redacts_sensitive_properties_and_bounds_values()
    {
        var metadata = $$"""{"provider":"brave","apiKey":"{{Guid.NewGuid()}}","nested":{"access_token":"private"},"body":"{{new string('x', 20_000)}}"}""";

        var result = ResearchEventSanitizer.SanitizeMetadataJson(metadata, 1_000);

        Assert.NotNull(result);
        Assert.True(result.Length <= 1_000 || result.Contains("\"truncated\":true", StringComparison.Ordinal));
        Assert.DoesNotContain("private", result, StringComparison.Ordinal);
        using var document = System.Text.Json.JsonDocument.Parse(result);
        Assert.Equal("[REDACTED]", document.RootElement.GetProperty("apiKey").GetString());
        Assert.Equal("[REDACTED]", document.RootElement.GetProperty("nested").GetProperty("access_token").GetString());
    }

    [Fact]
    public async Task SanitizingWriter_sanitizes_before_delegating_to_storage_boundary()
    {
        var inner = new CapturingWriter();
        var writer = new SanitizingResearchEventWriter(inner);
        var secret = "gemini-secret-that-must-not-survive";

        await writer.WriteAsync(new ResearchEvent
        {
            Category = ResearchEventCategory.AiRequested,
            Status = ResearchEventStatus.Working,
            InputSummary = $"apiKey: {secret}",
            OutputSummary = new string('x', 10_000)
        });

        var captured = Assert.Single(inner.Events);
        Assert.DoesNotContain(secret, captured.InputSummary, StringComparison.Ordinal);
        Assert.True(captured.OutputSummary!.Length <= ResearchEventSanitizer.DefaultMaxSummaryCharacters);
    }

    [Fact]
    public void Event_has_conversation_seam_but_does_not_model_hidden_reasoning()
    {
        var eventPropertyNames = typeof(ResearchEvent).GetProperties().Select(property => property.Name).ToArray();

        Assert.Contains(nameof(ResearchEvent.ConversationId), eventPropertyNames);
        Assert.DoesNotContain(eventPropertyNames, name =>
            name.Contains("chain", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("thought", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Reasoning", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CapturingWriter : IResearchEventWriter
    {
        public List<ResearchEvent> Events { get; } = [];

        public Task WriteAsync(ResearchEvent researchEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(researchEvent);
            return Task.CompletedTask;
        }
    }
}
