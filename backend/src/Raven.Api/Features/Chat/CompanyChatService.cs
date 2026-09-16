using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Profiles.Persistence;
using Raven.Api.Features.Research;

namespace Raven.Api.Features.Chat;

public sealed class CompanyChatService(
    RavenDbContext dbContext,
    ICompanyProfilePersistenceService profiles,
    ICompanyChatAgentFactory agentFactory,
    ILogger<CompanyChatService> logger) : ICompanyChatService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task<ChatConversationResponse> CreateConversationAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var companyExists = await dbContext.Companies.AnyAsync(company => company.Id == companyId, cancellationToken);
        if (!companyExists)
        {
            throw Problem(StatusCodes.Status404NotFound, "company_not_found", "Company not found", "The company does not exist.");
        }

        var profile = await profiles.GetCurrentProfileAsync(companyId, cancellationToken);
        if (profile is null)
        {
            throw Problem(StatusCodes.Status409Conflict, "profile_required", "Accepted profile required", "Accept a company profile before starting a conversation.");
        }

        var now = DateTimeOffset.UtcNow;
        var conversation = new ChatConversation
        {
            CompanyId = companyId,
            ProfileVersionId = profile.Id,
            Title = ChatText.Bound($"Ask RAVEN — {profile.DisplayName ?? "Company"}", 200),
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.ChatConversations.Add(conversation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToConversationResponse(conversation, profile.Version, []);
    }

    public async Task<ChatConversationResponse> GetConversationAsync(Guid companyId, Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await dbContext.ChatConversations
            .Include(item => item.Messages).ThenInclude(item => item.Citations).ThenInclude(item => item.SourceDocument)
            .Include(item => item.Messages).ThenInclude(item => item.ToolExecutions)
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == conversationId && item.CompanyId == companyId, cancellationToken);
        if (conversation is null)
        {
            throw Problem(StatusCodes.Status404NotFound, "conversation_not_found", "Conversation not found", "The conversation does not belong to this company.");
        }

        var profile = await dbContext.CompanyProfileVersions.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == conversation.ProfileVersionId && item.CompanyId == companyId, cancellationToken);
        if (profile is null)
        {
            throw Problem(StatusCodes.Status409Conflict, "profile_required", "Accepted profile required", "The pinned profile is no longer available.");
        }

        return ToConversationResponse(conversation, profile.Version, conversation.Messages.OrderBy(item => item.CreatedAt).ToArray());
    }

    public async Task<SendChatMessageResponse> SendMessageAsync(Guid companyId, Guid conversationId, CreateChatMessageRequest request, CancellationToken cancellationToken)
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

        var company = await dbContext.Companies.AsNoTracking().SingleAsync(item => item.Id == companyId, cancellationToken);
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
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(90));
            var completion = ChatConversationPolicy.TryRespond(question) ?? await agentFactory.Create().RunAsync(
                new ChatAgentRequest(companyId, conversationId, company, profile, recentMessages, question),
                deadline.Token);
            var validated = await ValidateResultAsync(completion.Result, profile, companyId, cancellationToken);

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
                    FieldPath = citation.FieldPath,
                    Origin = citation.Origin,
                    Excerpt = citation.Excerpt
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
                validated.CitationResponses,
                completion.ToolExecutions.Select(ToToolResponse).ToArray(),
                validated.FollowUpQuestion);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            assistant.Content = "Ask RAVEN could not complete this response within the time limit.";
            assistant.Status = ChatMessageStatus.Failed;
            await SaveFailureAsync(assistant, conversation, cancellationToken);
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

    private async Task<CompanyProfileVersion?> LoadProfileAsync(Guid profileId, Guid companyId, CancellationToken cancellationToken)
    {
        var persisted = await dbContext.CompanyProfileVersions.AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.Id == profileId && profile.CompanyId == companyId, cancellationToken);
        if (persisted is null) return null;
        try
        {
            var profile = JsonSerializer.Deserialize<CompanyProfileVersion>(persisted.ProfileJson, JsonOptions);
            if (profile is null || profile.Id != persisted.Id || profile.CompanyId != persisted.CompanyId || profile.Version != persisted.Version) return null;
            profile.ProfileJson = persisted.ProfileJson;
            var evidence = await dbContext.ProfileEvidences.AsNoTracking()
                .Where(item => item.CompanyProfileVersionId == profileId)
                .ToListAsync(cancellationToken);
            profile.Evidence.Clear();
            foreach (var row in evidence)
            {
                var ids = JsonSerializer.Deserialize<Guid[]>(row.SourceDocumentIdsJson, JsonOptions) ?? [];
                var item = new ProfileEvidence { Id = row.Id, CompanyProfileVersionId = profileId, FieldPath = row.FieldPath, SourceDocumentIdsJson = row.SourceDocumentIdsJson };
                foreach (var id in ids) item.SourceDocumentIds.Add(id);
                profile.Evidence.Add(item);
            }
            return profile;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<ValidatedChatResult> ValidateResultAsync(ChatAgentResult result, CompanyProfileVersion profile, Guid companyId, CancellationToken cancellationToken)
    {
        var answer = ChatText.Bound(result.Answer, 20_000);
        if (answer.Length == 0)
        {
            throw Problem(StatusCodes.Status502BadGateway, "ai_invalid_response", "Invalid AI response", "The chat provider returned an empty answer.");
        }

        if (result.Status == ChatAnswerStatus.Answered && result.CitedSourceDocumentIds.Count == 0)
        {
            throw Problem(StatusCodes.Status502BadGateway, "ai_invalid_response", "Invalid AI response", "The chat provider returned an answer without evidence.");
        }

        var profileIds = profile.Evidence.SelectMany(item => item.SourceDocumentIds).ToHashSet();
        if (result.CitedSourceDocumentIds.Any(id => !profileIds.Contains(id)))
        {
            throw Problem(StatusCodes.Status502BadGateway, "ai_invalid_response", "Invalid AI response", "The chat provider returned an unsupported citation.");
        }

        var sources = await dbContext.SourceDocuments.AsNoTracking()
            .Where(source => source.CompanyId == companyId && result.CitedSourceDocumentIds.Contains(source.Id))
            .ToDictionaryAsync(source => source.Id, cancellationToken);
        var citations = new List<ChatCitation>();
        var responses = new List<ChatCitationResponse>();
        foreach (var sourceId in result.CitedSourceDocumentIds.Distinct())
        {
            if (!sources.TryGetValue(sourceId, out var source))
            {
                throw Problem(StatusCodes.Status502BadGateway, "ai_invalid_response", "Invalid AI response", "The chat provider returned a citation outside the company scope.");
            }
            var evidence = profile.Evidence.First(item => item.SourceDocumentIds.Contains(sourceId));
            var citation = new ChatCitation
            {
                SourceDocumentId = sourceId,
                FieldPath = ChatText.Bound(evidence.FieldPath, 300),
                Origin = ChatCitationOrigin.Profile
            };
            citations.Add(citation);
            responses.Add(ToCitationResponse(citation, source));
        }
        return new ValidatedChatResult(answer, result.Status, ChatText.Bound(result.FollowUpQuestion, 1_000), citations, responses);
    }

    private async Task SaveFailureAsync(ChatMessage assistant, ChatConversation conversation, CancellationToken cancellationToken)
    {
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateException or OperationCanceledException)
        {
            logger.LogError(exception, "Could not persist failed chat message {MessageId}", assistant.Id);
        }
    }

    private void PersistToolExecutions(Guid assistantMessageId, IReadOnlyList<ChatToolExecution> executions)
    {
        foreach (var execution in executions)
        {
            dbContext.ChatToolExecutions.Add(new ChatToolExecution
            {
                ChatMessageId = assistantMessageId,
                Tool = ChatText.Bound(execution.Tool, 200),
                Provider = ChatText.Bound(execution.Provider, 100),
                Status = ChatText.Bound(execution.Status, 32),
                DurationMs = execution.DurationMs,
                InputSummary = ChatText.Bound(execution.InputSummary, 2_000),
                OutputSummary = ChatText.Bound(execution.OutputSummary, 2_000),
                ErrorCode = ChatText.Bound(execution.ErrorCode, 100),
                CreatedAt = execution.CreatedAt
            });
        }
    }

    private static ChatConversationResponse ToConversationResponse(ChatConversation conversation, int version, IReadOnlyList<ChatMessage> messages) =>
        new(conversation.Id, conversation.CompanyId, conversation.ProfileVersionId, version, conversation.Title, conversation.CreatedAt, conversation.UpdatedAt, messages.Select(ToMessageResponse).ToArray());

    private static ChatHistoryMessage ToMessageResponse(ChatMessage message) =>
        new(message.Id, message.Role, message.Content, message.Status, message.AnswerStatus, message.FollowUpQuestion, message.Citations.Select(item => ToCitationResponse(item, item.SourceDocument)).ToArray(), message.ToolExecutions.Select(ToToolResponse).ToArray(), message.CreatedAt);

    private static ChatCitationResponse ToCitationResponse(ChatCitation item, SourceDocument source) =>
        new(item.Origin, item.SourceDocumentId, item.FieldPath, source.Title ?? source.Url, source.Url, source.RetrievedAt);

    private static ChatToolExecutionResponse ToToolResponse(ChatToolExecution item) =>
        new(item.Tool, item.Provider, item.Status, item.DurationMs, item.ErrorCode);

    private static ChatProblemException Problem(int status, string code, string title, string detail) =>
        new(status, code, title, detail);

    private sealed record ValidatedChatResult(
        string Answer,
        ChatAnswerStatus Status,
        string? FollowUpQuestion,
        IReadOnlyList<ChatCitation> Citations,
        IReadOnlyList<ChatCitationResponse> CitationResponses);
}
