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

public sealed class CompanyChatService(
    RavenDbContext dbContext,
    ICompanyProfilePersistenceService profiles,
    ICompanyChatAgentFactory agentFactory,
    IOptions<ChatResearchOptions> chatResearchOptions,
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
            WebSearchEnabled = false,
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
            .Include(item => item.Messages).ThenInclude(item => item.Citations).ThenInclude(item => item.WebEvidenceSnapshot)
            .Include(item => item.Messages).ThenInclude(item => item.Citations).ThenInclude(item => item.Investigation)
            .Include(item => item.Messages).ThenInclude(item => item.Citations).ThenInclude(item => item.BriefingVersion)
            .Include(item => item.Messages).ThenInclude(item => item.WebEvidenceSnapshots)
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

    public async Task<ChatConversationResponse> UpdateCapabilitiesAsync(
        Guid companyId,
        Guid conversationId,
        UpdateChatCapabilitiesRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var conversation = await dbContext.ChatConversations
            .SingleOrDefaultAsync(item => item.Id == conversationId && item.CompanyId == companyId, cancellationToken);
        if (conversation is null)
        {
            throw Problem(StatusCodes.Status404NotFound, "conversation_not_found", "Conversation not found", "The conversation does not belong to this company.");
        }

        if (conversation.WebSearchEnabled != request.WebSearchEnabled)
        {
            conversation.WebSearchEnabled = request.WebSearchEnabled;
            conversation.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return await GetConversationAsync(companyId, conversationId, cancellationToken);
    }

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

    private async Task<LoadedChatContexts> LoadResearchContextsAsync(Guid companyId, Guid conversationId, CancellationToken ct)
    {
        var attachments = await dbContext.ResearchContextAttachments.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.ConversationId == conversationId)
            .ToListAsync(ct);
        var selected = attachments.OrderByDescending(item => item.AttachedAt).Take(5).ToArray();
        var ids = selected.Where(item => item.InvestigationId.HasValue)
            .Select(item => item.InvestigationId!.Value).ToArray();
        var investigations = await dbContext.ManagedResearchInvestigations.AsNoTracking()
            .Where(item => item.CompanyId == companyId && ids.Contains(item.Id)).ToListAsync(ct);
        var versionIds = selected.Where(item => item.BriefingVersionId.HasValue)
            .Select(item => item.BriefingVersionId!.Value).ToArray();
        var versions = await dbContext.ResearchBriefingVersions.AsNoTracking()
            .Where(item => versionIds.Contains(item.Id)).ToListAsync(ct);
        var briefingIds = versions.Select(item => item.BriefingId).Distinct().ToArray();
        var ownedBriefingIds = await dbContext.ResearchBriefings.AsNoTracking()
            .Where(item => item.CompanyId == companyId && briefingIds.Contains(item.Id))
            .Select(item => item.Id).ToListAsync(ct);
        return new(
            investigations.Select(ToInvestigationContext).ToArray(),
            versions.Where(item => ownedBriefingIds.Contains(item.BriefingId)).Select(ToBriefingContext).ToArray());
    }

    private static ChatInvestigationContext ToInvestigationContext(ManagedResearchInvestigation item)
    {
        var objective = ChatText.Bound(item.Objective, 400);
        var summary = ChatText.Bound(item.Summary, 700);
        try
        {
            var result = JsonSerializer.Deserialize<ManagedResearchResult>(item.ResultJson, JsonOptions);
            if (result is not null)
            {
                var claims = string.Join('\n', result.Claims.Take(8).Select(claim =>
                    $"{claim.Topic}: {claim.Statement} [source leads: {string.Join(", ", claim.SupportingSourceUrls.Take(3))}]"));
                var sources = string.Join('\n', result.Sources.Take(8).Select(source => $"{source.Title}: {source.Url}"));
                var uncertainties = string.Join('\n', result.Uncertainties.Take(5));
                return new(item.Id, objective, summary, ChatText.Bound(
                    $"CLAIMS:\n{claims}\nSOURCE LEADS:\n{sources}\nUNCERTAINTIES:\n{uncertainties}", 3_000), item.CompletedAt);
            }
        }
        catch (JsonException) { /* Summary remains readable when old provider JSON is malformed. */ }
        return new(item.Id, objective, summary, string.Empty, item.CompletedAt);
    }

    private static ChatBriefingContext ToBriefingContext(ResearchBriefingVersion item)
    {
        var sections = JsonSerializer.Deserialize<BriefingSection[]>(item.SectionsJson, JsonOptions) ?? [];
        var sources = JsonSerializer.Deserialize<BriefingSourceSnapshot[]>(item.SourcesJson, JsonOptions) ?? [];
        var sectionText = string.Join("\n\n", sections.Select(section =>
            $"## {section.Title}\n{string.Join('\n', section.Items.Take(20).Select(value => $"- {value}"))}\nSOURCE INVESTIGATION IDS: {string.Join(", ", section.SourceInvestigationIds)}"));
        var sourceText = string.Join('\n', sources.Take(12).Select(source =>
            $"- {source.InvestigationId} | {source.Title} | topics={string.Join(", ", source.Topics)} | materialUpdatedAt={source.MaterialUpdatedAt:O}" +
            (source.Uncertainties.Count > 0 ? $" | uncertainties={string.Join("; ", source.Uncertainties.Take(5))}" : string.Empty)));
        var material = ChatText.Bound($"SECTIONS:\n{sectionText}\n\nSOURCE INVESTIGATION METADATA:\n{sourceText}", 20_000);
        return new(item.BriefingId, item.Id, item.VersionNumber, item.Title, item.Template,
            ChatText.Bound(item.Objective, 2_000), material, item.GeneratedAt, item.ResearchThrough);
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

    private async Task<ValidatedChatResult> ValidateResultAsync(ChatAgentResult result, CompanyProfileVersion profile, Guid companyId, Guid conversationId, IReadOnlyList<ChatWebEvidenceDraft> webEvidence, IReadOnlyList<ChatInvestigationContext> investigations, IReadOnlyList<ChatBriefingContext> briefings, CancellationToken cancellationToken)
    {
        var answer = ChatText.Bound(result.Answer, 20_000);
        if (answer.Length == 0)
        {
            throw Problem(StatusCodes.Status502BadGateway, "ai_invalid_response", "Invalid AI response", "The chat provider returned an empty answer.");
        }

        if (result.Status == ChatAnswerStatus.Answered && result.CitedSourceDocumentIds.Count == 0 && (result.CitedWebEvidenceCandidateIds?.Count ?? 0) == 0 && (result.CitedInvestigationIds?.Count ?? 0) == 0 && (result.CitedBriefingVersionIds?.Count ?? 0) == 0)
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
        foreach (var investigationId in (result.CitedInvestigationIds ?? []).Distinct())
        {
            var context = investigations.FirstOrDefault(item => item.Id == investigationId);
            if (context is null)
                throw Problem(StatusCodes.Status502BadGateway, "ai_invalid_response", "Invalid AI response", "The chat provider cited an unattached Investigation.");
            citations.Add(new ChatCitation { InvestigationId = investigationId, Origin = ChatCitationOrigin.Investigation });
            responses.Add(new ChatCitationResponse(ChatCitationOrigin.Investigation, null, null, null,
                context.Objective, $"/companies/{companyId:D}?tab=investigations&research={investigationId:D}",
                context.CompletedAt, investigationId));
        }
        foreach (var versionId in (result.CitedBriefingVersionIds ?? []).Distinct())
        {
            var context = briefings.FirstOrDefault(item => item.VersionId == versionId);
            if (context is null)
                throw Problem(StatusCodes.Status502BadGateway, "ai_invalid_response", "Invalid AI response", "The chat provider cited an unattached Briefing version.");
            citations.Add(new ChatCitation { BriefingVersionId = versionId, Origin = ChatCitationOrigin.Briefing });
            responses.Add(new ChatCitationResponse(ChatCitationOrigin.Briefing, null, null, null,
                $"{context.Title} · v{context.VersionNumber}",
                $"/companies/{companyId:D}?tab=briefings&briefing={context.BriefingId:D}&briefingVersion={context.VersionNumber}&conversation={conversationId:D}",
                context.GeneratedAt, BriefingId: context.BriefingId, BriefingVersionId: context.VersionId,
                BriefingVersionNumber: context.VersionNumber));
        }
        var webCitationIds = result.CitedWebEvidenceCandidateIds ?? [];
        if (webCitationIds.Any(id => webEvidence.All(draft => !string.Equals(draft.CandidateId, id, StringComparison.Ordinal)))) throw Problem(StatusCodes.Status502BadGateway, "ai_invalid_response", "Invalid AI response", "The chat provider returned an unsupported Web citation.");
        return new ValidatedChatResult(answer, result.Status, ChatText.Bound(result.FollowUpQuestion, 1_000), citations, responses, webCitationIds.Distinct(StringComparer.Ordinal).ToArray());
    }

    private Dictionary<string, ChatWebEvidenceSnapshot> PersistWebEvidenceSnapshots(Guid assistantMessageId, IReadOnlyList<ChatWebEvidenceDraft> drafts)
    {
        var snapshots = new Dictionary<string, ChatWebEvidenceSnapshot>(StringComparer.Ordinal);
        foreach (var draft in drafts.GroupBy(item => item.NormalizedUrl, StringComparer.Ordinal).Select(group => group.First()))
        {
            var snapshot = new ChatWebEvidenceSnapshot { ChatMessageId = assistantMessageId, Url = ChatText.Bound(draft.Url, 2_000), NormalizedUrl = ChatText.Bound(draft.NormalizedUrl, 2_000), Title = ChatText.Bound(draft.Title, 500), SearchSnippet = ChatText.Bound(draft.SearchSnippet, 2_000), ContentExcerpt = ChatText.Bound(draft.ContentExcerpt, 8_000), SearchProvider = ChatText.Bound(draft.SearchProvider, 100), CrawlerProvider = ChatText.Bound(draft.CrawlerProvider, 100), SearchRank = draft.SearchRank, RetrievedAt = draft.RetrievedAt };
            dbContext.ChatWebEvidenceSnapshots.Add(snapshot);
            snapshots[draft.CandidateId] = snapshot;
        }
        return snapshots;
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
        new(conversation.Id, conversation.CompanyId, conversation.ProfileVersionId, version, conversation.WebSearchEnabled, conversation.Title, conversation.CreatedAt, conversation.UpdatedAt, messages.Select(ToMessageResponse).ToArray());

    private static ChatHistoryMessage ToMessageResponse(ChatMessage message) =>
        new(
            message.Id,
            message.Role,
            message.Content,
            message.Status,
            message.AnswerStatus,
            message.FollowUpQuestion,
            message.Activity,
            message.Citations.Select(ToCitationResponse).ToArray(),
            message.WebEvidenceSnapshots.OrderBy(snapshot => snapshot.SearchRank).Select(ToWebEvidenceSnapshotResponse).ToArray(),
            message.ToolExecutions.Select(ToToolResponse).ToArray(),
            message.CreatedAt);

    private static ChatCitationResponse ToCitationResponse(ChatCitation item) =>
        item.Origin switch
        {
            ChatCitationOrigin.Profile when item.SourceDocument is not null => ToCitationResponse(item, item.SourceDocument),
            ChatCitationOrigin.Web when item.WebEvidenceSnapshot is not null => new(
                item.Origin,
                null,
                item.WebEvidenceSnapshotId,
                null,
                item.WebEvidenceSnapshot.Title ?? item.WebEvidenceSnapshot.Url,
                item.WebEvidenceSnapshot.Url,
                item.WebEvidenceSnapshot.RetrievedAt),
            ChatCitationOrigin.Investigation when item.Investigation is not null => new(
                item.Origin, null, null, null, item.Investigation.Objective,
                $"/companies/{item.Investigation.CompanyId:D}?tab=investigations&research={item.InvestigationId:D}",
                item.Investigation.CompletedAt, item.InvestigationId),
            ChatCitationOrigin.Briefing when item.BriefingVersion is not null => new(
                item.Origin, null, null, null, $"{item.BriefingVersion.Title} · v{item.BriefingVersion.VersionNumber}",
                $"/companies/{item.ChatMessage.Conversation.CompanyId:D}?tab=briefings&briefing={item.BriefingVersion.BriefingId:D}&briefingVersion={item.BriefingVersion.VersionNumber}&conversation={item.ChatMessage.ConversationId:D}",
                item.BriefingVersion.GeneratedAt, BriefingId: item.BriefingVersion.BriefingId,
                BriefingVersionId: item.BriefingVersionId, BriefingVersionNumber: item.BriefingVersion.VersionNumber),
            _ => throw new InvalidOperationException("Chat citation evidence reference is invalid.")
        };

    private static ChatCitationResponse ToCitationResponse(ChatCitation item, SourceDocument source) =>
        new(item.Origin, item.SourceDocumentId, null, item.FieldPath, source.Title ?? source.Url, source.Url, source.RetrievedAt);

    private static ChatWebEvidenceSnapshotResponse ToWebEvidenceSnapshotResponse(ChatWebEvidenceSnapshot snapshot) =>
        new(snapshot.Id, snapshot.Url, snapshot.Title, snapshot.SearchSnippet, snapshot.ContentExcerpt, snapshot.SearchProvider, snapshot.CrawlerProvider, snapshot.SearchRank, snapshot.RetrievedAt);

    private static ChatToolExecutionResponse ToToolResponse(ChatToolExecution item) =>
        new(item.Tool, item.Provider, item.Status, item.DurationMs, item.ErrorCode);

    private static ChatProblemException Problem(int status, string code, string title, string detail) =>
        new(status, code, title, detail);

    private sealed record ValidatedChatResult(
        string Answer,
        ChatAnswerStatus Status,
        string? FollowUpQuestion,
        IReadOnlyList<ChatCitation> Citations,
        IReadOnlyList<ChatCitationResponse> CitationResponses,
        IReadOnlyList<string> WebCitationCandidateIds);
    private sealed record LoadedChatContexts(IReadOnlyList<ChatInvestigationContext> Investigations,
        IReadOnlyList<ChatBriefingContext> Briefings);
}
