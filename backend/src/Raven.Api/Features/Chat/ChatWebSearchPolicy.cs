namespace Raven.Api.Features.Chat;

/// <summary>Small deterministic guardrail for questions where a profile-only answer is likely stale. Enabling Web Search remains a permission, not a requirement for ordinary company questions.</summary>
public sealed class ChatWebSearchPolicy
{
    private static readonly string[] ExplicitWebSignals = ["search the web", "search web", "tìm trên web", "tìm web", "tra cứu web", "google", "nguồn mới"];
    private static readonly string[] FreshnessSignals = ["latest", "recent", "today", "current", "currently", "newest", "recently", "gần đây", "mới nhất", "hôm nay", "hiện tại", "vừa"];

    public ChatWebSearchExpectation Evaluate(string question, bool webSearchEnabled)
    {
        var normalized = ChatText.NormalizeQuestion(question).ToLowerInvariant();
        var explicitRequest = ExplicitWebSignals.Any(signal => normalized.Contains(signal, StringComparison.Ordinal));
        var freshnessSensitive = FreshnessSignals.Any(signal => normalized.Contains(signal, StringComparison.Ordinal));
        if (!webSearchEnabled && (explicitRequest || freshnessSensitive))
            return new(ChatWebSearchExpectationKind.Unavailable, "Web Search is off for this conversation. Enable it to verify current public information.");
        return webSearchEnabled && (explicitRequest || freshnessSensitive)
            ? new(ChatWebSearchExpectationKind.Required, "This question explicitly asks for current or web-verified information. Search and read public evidence before a factual final answer.")
            : new(ChatWebSearchExpectationKind.Optional, "Use accepted profile evidence first. Web Search is optional for this question.");
    }
}

public enum ChatWebSearchExpectationKind { Optional, Required, Unavailable }
public sealed record ChatWebSearchExpectation(ChatWebSearchExpectationKind Kind, string Instruction);