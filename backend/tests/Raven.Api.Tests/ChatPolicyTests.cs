using Raven.Api.Features.Chat;

namespace Raven.Api.Tests;

public sealed class ChatPolicyTests
{
    [Fact]
    public void Question_normalization_removes_control_characters_and_collapses_whitespace()
    {
        var result = ChatText.NormalizeQuestion("  Công ty\n\t này\r là ai?  ");

        Assert.Equal("Công ty này là ai?", result);
    }

    [Fact]
    public void Profile_chat_statuses_are_explicit_and_not_field_taxonomy()
    {
        Assert.NotEqual(ChatAnswerStatus.Answered, ChatAnswerStatus.UnsupportedScope);
        Assert.NotEqual(ChatAnswerStatus.Answered, ChatAnswerStatus.InsufficientEvidence);
        Assert.NotEqual(ChatAnswerStatus.ClarificationRequired, ChatAnswerStatus.UnsupportedScope);
    }
}
