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
            ChatCitationOrigin.Investigation when item.SavedResearchArtifact is not null => new(
                item.Origin, null, null, null, item.SavedResearchArtifact.Title,
                $"/companies/{item.SavedResearchArtifact.CompanyId:D}?tab=investigations&research={item.SavedResearchArtifactId:D}",
                item.SavedResearchArtifact.CompletedAt ?? item.SavedResearchArtifact.CreatedAt, item.SavedResearchArtifactId),
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
}
