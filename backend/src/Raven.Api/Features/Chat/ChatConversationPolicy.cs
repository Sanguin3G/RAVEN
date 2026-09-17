namespace Raven.Api.Features.Chat;

/// <summary>
/// Handles bounded, non-factual product conversation without spending an AI
/// call or pretending that a greeting needs company evidence. Company facts
/// still go through the citation-gated profile agent.
/// </summary>
public static class ChatConversationPolicy
{
    public static ChatAgentCompletion? TryRespond(string question)
    {
        var normalized = string.Join(' ', question.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized is "hi" or "hello" or "hey" or "good morning" or "good afternoon")
        {
            return Complete(ChatAnswerStatus.Conversational, "Hello. Ask me about this company's accepted profile, supporting sources, or changes over time.");
        }

        if (normalized is "thanks" or "thank you" or "thank you!" or "thanks!")
        {
            return Complete(ChatAnswerStatus.Conversational, "You're welcome. I can help interpret the accepted profile and point you to its supporting sources.");
        }

        if (normalized.Contains("what can you do", StringComparison.Ordinal) || normalized.Contains("help", StringComparison.Ordinal))
        {
            return Complete(ChatAnswerStatus.Guidance, "I answer company questions from the accepted profile and its stored evidence. I can also help you find supporting sources and explain when more research is needed.");
        }

        if (normalized.Contains("research", StringComparison.Ordinal) &&
            (normalized.Contains("deep", StringComparison.Ordinal) || normalized.Contains("more", StringComparison.Ordinal)))
        {
            return Complete(ChatAnswerStatus.Guidance, "Saved investigations are reference material. This profile chat does not silently start additional research; use Investigations to review saved results.");
        }

        if (normalized.Contains("where", StringComparison.Ordinal) && normalized.Contains("source", StringComparison.Ordinal))
        {
            return Complete(ChatAnswerStatus.Guidance, "Citations appear beneath grounded answers. You can also open the Sources tab to review the stored evidence behind this company's profile.");
        }

        return null;
    }

    private static ChatAgentCompletion Complete(ChatAnswerStatus status, string answer) =>
        new(new ChatAgentResult(status, answer, [], null), "raven", "conversation-policy", []);
}
