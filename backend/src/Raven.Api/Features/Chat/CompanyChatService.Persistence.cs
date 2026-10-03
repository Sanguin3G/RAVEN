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
            citations.Add(context.SavedResearchArtifactId is { } savedArtifactId
                ? new ChatCitation { SavedResearchArtifactId = savedArtifactId, Origin = ChatCitationOrigin.Investigation }
                : new ChatCitation { InvestigationId = investigationId, Origin = ChatCitationOrigin.Investigation });
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
}
