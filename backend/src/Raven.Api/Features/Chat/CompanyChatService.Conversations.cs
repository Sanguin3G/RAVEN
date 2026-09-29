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
    public async Task<IReadOnlyList<ChatConversationSummary>> ListConversationsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var summaries = await dbContext.ChatConversations.AsNoTracking()
            .Where(item => item.CompanyId == companyId)
            .Select(item => new ChatConversationSummary(item.Id, item.Title, item.ProfileVersionId,
                item.ProfileVersion.Version, item.Messages.Count, item.WebSearchEnabled, item.CreatedAt, item.UpdatedAt))
            .ToListAsync(cancellationToken);
        // SQLite cannot order DateTimeOffset values server-side. Only summaries are materialized.
        return summaries.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Id).Take(20).ToArray();
    }

    public async Task<bool> DeleteConversationAsync(Guid companyId, Guid conversationId, CancellationToken cancellationToken)
    {
        var exists = await dbContext.ChatConversations.AsNoTracking()
            .AnyAsync(item => item.Id == conversationId && item.CompanyId == companyId, cancellationToken);
        if (!exists) return false;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.ResearchContextAttachments
            .Where(item => item.CompanyId == companyId && item.ConversationId == conversationId)
            .ExecuteDeleteAsync(cancellationToken);

        var messageIds = await dbContext.ChatMessages.AsNoTracking()
            .Where(item => item.ConversationId == conversationId)
            .Select(item => item.Id).ToArrayAsync(cancellationToken);
        if (messageIds.Length > 0)
        {
            await dbContext.ChatCitations.Where(item => messageIds.Contains(item.ChatMessageId)).ExecuteDeleteAsync(cancellationToken);
            await dbContext.ChatToolExecutions.Where(item => messageIds.Contains(item.ChatMessageId)).ExecuteDeleteAsync(cancellationToken);
            await dbContext.ChatWebEvidenceSnapshots.Where(item => messageIds.Contains(item.ChatMessageId)).ExecuteDeleteAsync(cancellationToken);
            await dbContext.ChatMessages.Where(item => item.ConversationId == conversationId).ExecuteDeleteAsync(cancellationToken);
        }

        await dbContext.ChatConversations.Where(item => item.Id == conversationId && item.CompanyId == companyId)
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

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
            Title = null,
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
            .Include(item => item.Messages).ThenInclude(item => item.Citations).ThenInclude(item => item.SavedResearchArtifact)
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
}
