using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Raven.Api.Data;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Profiles.Persistence;
using Raven.Api.Features.Research;
using Raven.Api.Features.ManagedResearch;
using Raven.Api.Features.Research.Briefings;

namespace Raven.Api.Features.Chat;

public sealed partial class CompanyChatService
{
    public Task<SendChatMessageResponse> SendMessageAsync(Guid companyId, Guid conversationId, CreateChatMessageRequest request, CancellationToken cancellationToken)
        => SendMessageCoreAsync(companyId, conversationId, request, null, cancellationToken);

    public Task<SendChatMessageResponse> SendMessageStreamAsync(Guid companyId, Guid conversationId, CreateChatMessageRequest request, IChatProgressReporter progressReporter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progressReporter);
        return SendMessageCoreAsync(companyId, conversationId, request, progressReporter, cancellationToken);
    }

    public async Task AnswerManagedResearchAsync(Guid companyId, Guid conversationId, Guid userMessageId, Guid jobId, CancellationToken cancellationToken)
    {
        var conversation = await dbContext.ChatConversations.SingleOrDefaultAsync(
            item => item.Id == conversationId && item.CompanyId == companyId, cancellationToken);
        var user = await dbContext.ChatMessages.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == userMessageId && item.ConversationId == conversationId && item.Role == ChatMessageRole.User,
            cancellationToken);
        if (conversation is null || user is null) throw new InvalidOperationException("The originating chat turn is unavailable.");
        var existingAssistant = await dbContext.ChatMessages.SingleOrDefaultAsync(
            item => item.ManagedResearchJobId == jobId, cancellationToken);
        if (existingAssistant?.Status is ChatMessageStatus.Completed or ChatMessageStatus.Failed) return;

        var profile = await LoadProfileAsync(conversation.ProfileVersionId, companyId, cancellationToken)
            ?? throw new InvalidOperationException("The pinned accepted profile is unavailable.");
        var company = await dbContext.Companies.AsNoTracking().SingleAsync(item => item.Id == companyId, cancellationToken);
        var contexts = await LoadResearchContextsAsync(companyId, conversationId, cancellationToken);
        var jobInvestigationId = await dbContext.ManagedResearchJobs.AsNoTracking()
            .Where(item => item.Id == jobId && item.CompanyId == companyId && item.ConversationId == conversationId && item.AnswerInChat)
            .Select(item => item.InvestigationId).SingleAsync(cancellationToken);
        if (jobInvestigationId is null || contexts.Investigations.All(item => item.Id != jobInvestigationId.Value))
            throw new InvalidOperationException("The completed Investigation is not attached to this chat.");
        var history = await dbContext.ChatMessages.AsNoTracking()
            .Where(item => item.ConversationId == conversationId && item.Status == ChatMessageStatus.Completed && item.Id != userMessageId)
            .ToListAsync(cancellationToken);
        var assistant = existingAssistant ?? new ChatMessage
        {
            ConversationId = conversationId,
            ManagedResearchJobId = jobId,
            Role = ChatMessageRole.Assistant,
            Content = string.Empty,
            Status = ChatMessageStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        if (existingAssistant is null)
        {
            dbContext.ChatMessages.Add(assistant);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(chatResearchOptions.Value.TurnDeadlineSeconds));
            var completion = await agentFactory.Create().RunAsync(new ChatAgentRequest(
                companyId, conversationId, company, profile, history.OrderByDescending(item => item.CreatedAt)
                    .Take(10).OrderBy(item => item.CreatedAt).ToArray(),
                user.Content, conversation.WebSearchEnabled, assistant.Id, null, contexts.Investigations, jobInvestigationId, contexts.Briefings), deadline.Token);
            if (completion.Result.Status == ChatAnswerStatus.Answered &&
                !(completion.Result.CitedInvestigationIds?.Contains(jobInvestigationId.Value) ?? false))
                throw new InvalidOperationException("The automatic answer did not cite its Investigation.");
            var validated = await ValidateResultAsync(completion.Result, profile, companyId, conversationId,
                completion.WebEvidenceDrafts ?? [], contexts.Investigations, contexts.Briefings, cancellationToken);
            assistant.Content = validated.Answer;
            assistant.Status = ChatMessageStatus.Completed;
            assistant.AnswerStatus = validated.Status;
            assistant.FollowUpQuestion = validated.FollowUpQuestion;
            assistant.AiProvider = ChatText.Bound(completion.Provider, 100);
            assistant.AiModel = ChatText.Bound(completion.Model, 200);
            foreach (var citation in validated.Citations)
                dbContext.ChatCitations.Add(new ChatCitation { ChatMessageId = assistant.Id,
                    SourceDocumentId = citation.SourceDocumentId, InvestigationId = citation.InvestigationId,
                    SavedResearchArtifactId = citation.SavedResearchArtifactId,
                    BriefingVersionId = citation.BriefingVersionId,
                    FieldPath = citation.FieldPath, Origin = citation.Origin });
            var snapshots = PersistWebEvidenceSnapshots(assistant.Id, completion.WebEvidenceDrafts ?? []);
            foreach (var candidateId in validated.WebCitationCandidateIds)
                dbContext.ChatCitations.Add(new ChatCitation { ChatMessageId = assistant.Id,
                    WebEvidenceSnapshotId = snapshots[candidateId].Id, Origin = ChatCitationOrigin.Web,
                    Excerpt = ChatText.Bound((completion.WebEvidenceDrafts ?? []).First(item => item.CandidateId == candidateId).ContentExcerpt, 2_000) });
            PersistToolExecutions(assistant.Id, completion.ToolExecutions);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            assistant.Content = "Ask RAVEN could not answer from the completed Investigation. You can ask again.";
            assistant.Status = ChatMessageStatus.Failed;
            logger.LogWarning(exception, "Could not answer from managed research job {JobId}", jobId);
        }
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<SendChatMessageResponse> SendMessageCoreAsync(Guid companyId, Guid conversationId, CreateChatMessageRequest request, IChatProgressReporter? progressReporter, CancellationToken cancellationToken)
    {
        var question = ChatText.NormalizeQuestion(request?.Question);
        if (question.Length is < 1 or > 2_000)
        {
            throw Problem(StatusCodes.Status400BadRequest, "validation_failed", "Invalid question", "Question must contain between 1 and 2,000 characters.");
        }

        var conversation = await dbContext.ChatConversations
            .SingleOrDefaultAsync(item => item.Id == conversationId && item.CompanyId == companyId, cancellationToken);
        if (conversation is null)
        {
            throw Problem(StatusCodes.Status404NotFound, "conversation_not_found", "Conversation not found", "The conversation does not belong to this company.");
        }

        if (await dbContext.ChatMessages.AnyAsync(item => item.ConversationId == conversationId && item.Role == ChatMessageRole.Assistant && item.Status == ChatMessageStatus.Pending, cancellationToken))
        {
            throw Problem(StatusCodes.Status409Conflict, "message_in_progress", "Message in progress", "Wait for the current Ask RAVEN response to finish.");
        }

        await ReportProgressAsync(progressReporter, ChatProgressStage.Analyzing, "Analyzing the question", cancellationToken);
        var company = await dbContext.Companies.AsNoTracking().SingleAsync(item => item.Id == companyId, cancellationToken);
        await ReportProgressAsync(progressReporter, ChatProgressStage.CheckingProfile, "Checking the accepted profile", cancellationToken);
        var profile = await LoadProfileAsync(conversation.ProfileVersionId, companyId, cancellationToken);
        if (profile is null)
        {
            throw Problem(StatusCodes.Status409Conflict, "profile_required", "Accepted profile required", "The pinned profile is no longer available.");
        }

        var previousMessages = await dbContext.ChatMessages.AsNoTracking()
            .Where(item => item.ConversationId == conversationId && item.Status == ChatMessageStatus.Completed)
            .ToListAsync(cancellationToken);
        var recentMessages = previousMessages
            .OrderByDescending(item => item.CreatedAt)
            .Take(10)
            .OrderBy(item => item.CreatedAt)
            .ToArray();
        var contexts = await LoadResearchContextsAsync(companyId, conversationId, cancellationToken);

        var userMessage = new ChatMessage
        {
            ConversationId = conversationId,
            Role = ChatMessageRole.User,
            Content = question,
            Status = ChatMessageStatus.Completed
        };
        var assistant = new ChatMessage
        {
            ConversationId = conversationId,
            Role = ChatMessageRole.Assistant,
            Content = string.Empty,
            Status = ChatMessageStatus.Pending
        };
        dbContext.ChatMessages.Add(userMessage);
        dbContext.ChatMessages.Add(assistant);
        if (conversation.Title is null)
        {
            conversation.Title = question.Length <= 72 ? question : $"{question[..71].TrimEnd()}…";
        }
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(chatResearchOptions.Value.TurnDeadlineSeconds));
            var completion = (contexts.Investigations.Count == 0 && contexts.Briefings.Count == 0 ? ChatConversationPolicy.TryRespond(question) : null) ?? await agentFactory.Create().RunAsync(
                new ChatAgentRequest(companyId, conversationId, company, profile, recentMessages, question, conversation.WebSearchEnabled, assistant.Id, progressReporter, contexts.Investigations, null, contexts.Briefings),
                deadline.Token);
            await ReportProgressAsync(progressReporter, ChatProgressStage.Composing, "Saving the grounded answer", cancellationToken);
            var validated = await ValidateResultAsync(completion.Result, profile, companyId, conversationId, completion.WebEvidenceDrafts ?? [], contexts.Investigations, contexts.Briefings, cancellationToken);

            assistant.Activity = null;
            assistant.Content = validated.Answer;
            assistant.Status = ChatMessageStatus.Completed;
            assistant.AnswerStatus = validated.Status;
            assistant.FollowUpQuestion = validated.FollowUpQuestion;
            assistant.AiProvider = ChatText.Bound(completion.Provider, 100);
            assistant.AiModel = ChatText.Bound(completion.Model, 200);
            foreach (var citation in validated.Citations)
            {
                dbContext.ChatCitations.Add(new ChatCitation
                {
                    ChatMessageId = assistant.Id,
                    SourceDocumentId = citation.SourceDocumentId,
                    InvestigationId = citation.InvestigationId,
                    SavedResearchArtifactId = citation.SavedResearchArtifactId,
                    BriefingVersionId = citation.BriefingVersionId,
                    FieldPath = citation.FieldPath,
                    Origin = citation.Origin,
                    Excerpt = citation.Excerpt
                });
            }
            var snapshots = PersistWebEvidenceSnapshots(assistant.Id, completion.WebEvidenceDrafts ?? []);
            foreach (var candidateId in validated.WebCitationCandidateIds)
            {
                if (!snapshots.TryGetValue(candidateId, out var snapshot))
                {
                    throw Problem(StatusCodes.Status502BadGateway, "ai_invalid_response", "Invalid AI response", "The chat provider cited Web evidence that was not retrieved in this turn.");
                }
                dbContext.ChatCitations.Add(new ChatCitation
                {
                    ChatMessageId = assistant.Id,
                    WebEvidenceSnapshotId = snapshot.Id,
                    Origin = ChatCitationOrigin.Web,
                    Excerpt = ChatText.Bound((completion.WebEvidenceDrafts ?? []).First(item => item.CandidateId == candidateId).ContentExcerpt, 2_000)
                });
            }
            PersistToolExecutions(assistant.Id, completion.ToolExecutions);
            conversation.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            return new SendChatMessageResponse(
                conversationId,
                assistant.Id,
                companyId,
                profile.Version,
                validated.Status,
                assistant.Content,
                validated.CitationResponses.Concat(validated.WebCitationCandidateIds.Select(candidateId => ToCitationResponse(new ChatCitation { Origin = ChatCitationOrigin.Web, WebEvidenceSnapshotId = snapshots[candidateId].Id, WebEvidenceSnapshot = snapshots[candidateId] }))).ToArray(),
                snapshots.Values.OrderBy(snapshot => snapshot.SearchRank).Select(ToWebEvidenceSnapshotResponse).ToArray(),
                completion.ToolExecutions.Select(ToToolResponse).ToArray(),
                validated.FollowUpQuestion);
        }
        catch (OperationCanceledException)
        {
            assistant.Activity = null;
            assistant.Content = cancellationToken.IsCancellationRequested
                ? "Ask RAVEN response was cancelled."
                : "Ask RAVEN could not complete this response within the time limit.";
            assistant.Status = ChatMessageStatus.Failed;
            await SaveFailureAsync(assistant, conversation, CancellationToken.None);
            if (cancellationToken.IsCancellationRequested) throw;
            throw Problem(StatusCodes.Status504GatewayTimeout, "chat_timeout", "Chat timed out", "Ask RAVEN could not finish within the request deadline.");
        }
        catch (ChatProblemException failure)
        {
            logger.LogWarning("Company chat failed with {Code}", failure.Code);
            assistant.Content = "Ask RAVEN could not complete this response.";
            assistant.Status = ChatMessageStatus.Failed;
            await SaveFailureAsync(assistant, conversation, cancellationToken);
            throw;
        }
    }

    private static Task ReportProgressAsync(IChatProgressReporter? progressReporter, ChatProgressStage stage, string message, CancellationToken cancellationToken) =>
        progressReporter?.ReportAsync(new ChatProgressEvent(stage, message), cancellationToken) ?? Task.CompletedTask;
}
