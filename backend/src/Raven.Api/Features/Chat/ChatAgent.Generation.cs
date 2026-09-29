using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Settings;

namespace Raven.Api.Features.Chat;

public sealed partial class CompanyChatAgent
{
    private async Task<ModelJsonResult> GenerateAsync(string model, JsonElement schema, string prompt, string promptVersion,
        Guid conversationId,
        int timeoutSeconds, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        AiModelResult result;
        using (executionContext.Push(null, conversationId))
            result = await aiProvider.GenerateStructuredAsync(new AiModelRequest(model, SystemInstruction, prompt, promptVersion, AiEvidencePayload.Empty, schema), deadline.Token);
        if (!result.Succeeded || result.StructuredJson is not { } json) throw ProviderFailure(result.Failure);
        return new(json, result.Provider, result.Model);
    }

    private static ChatAgentCompletion InsufficientEvidenceCompletion(ChatAgentRequest request, ModelJsonResult result,
        IReadOnlyList<ChatToolExecution> tools, ChatResearchState state)
    {
        var answer = VietnameseQuestionRegex().IsMatch(request.Question)
            ? "Tôi đã tìm được một số nguồn công khai nhưng chưa thể xác minh an toàn các trích dẫn để trả lời câu hỏi này. Bạn có thể thử lại hoặc thu hẹp phạm vi thông tin cần tìm."
            : "I found public sources but could not safely validate the citations needed to answer this question. Please try again or narrow the requested scope.";
        return new ChatAgentCompletion(
            new ChatAgentResult(ChatAnswerStatus.InsufficientEvidence, answer, [], null, [], [], []),
            result.Provider, result.Model, tools, state.WebEvidence);
    }
}
