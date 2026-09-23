using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Chat;

namespace Raven.Api.Features.ManagedResearch;

/// <summary>Only chat-originated jobs cross into conversation state.</summary>
public sealed class ManagedResearchChatBridge(
    RavenDbContext db,
    IResearchContextAttachmentService attachments,
    ICompanyChatService chat,
    ILogger<ManagedResearchChatBridge> logger)
{
    public async Task<Guid> PrepareAsync(Guid companyId, Guid conversationId, string question, CancellationToken ct)
    {
        var conversation = await db.ChatConversations.SingleOrDefaultAsync(
            item => item.Id == conversationId && item.CompanyId == companyId, ct);
        if (conversation is null || !await db.CompanyProfileVersions.AnyAsync(
                item => item.Id == conversation.ProfileVersionId && item.CompanyId == companyId, ct))
            throw new InvalidOperationException("An accepted-profile chat conversation is required.");

        var attached = await db.ResearchContextAttachments.CountAsync(
            item => item.CompanyId == companyId && item.ConversationId == conversationId, ct);
        var running = await db.ManagedResearchJobs.CountAsync(item => item.CompanyId == companyId &&
            item.ConversationId == conversationId && item.AnswerInChat &&
            (item.Status == ManagedResearchJobStatus.Queued || item.Status == ManagedResearchJobStatus.Researching), ct);
        if (attached + running >= 5)
            throw new InvalidOperationException("Remove an attached Investigation before starting another Chat Deep Research job.");

        var user = new ChatMessage
        {
            ConversationId = conversationId,
            Role = ChatMessageRole.User,
            Content = question,
            Status = ChatMessageStatus.Completed
        };
        db.ChatMessages.Add(user);
        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        // The job store uses this same scoped DbContext, saving both rows together.
        return user.Id;
    }

    public async Task CompleteAsync(ManagedResearchJob job, CancellationToken ct)
    {
        if (!job.AnswerInChat || job.Status != ManagedResearchJobStatus.Completed ||
            job.ConversationId is not { } conversationId ||
            job.ChatMessageId is not { } userMessageId ||
            job.InvestigationId is not { } investigationId) return;

        try
        {
            await attachments.AttachAsync(job.CompanyId, investigationId,
                new AttachResearchContextRequest(conversationId), ct);
            await chat.AnswerManagedResearchAsync(job.CompanyId, conversationId,
                userMessageId, job.Id, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Chat answer failed after managed research job {JobId}", job.Id);
            try
            {
                var failed = await db.ChatMessages.SingleOrDefaultAsync(
                    item => item.ManagedResearchJobId == job.Id, CancellationToken.None);
                if (failed is null)
                {
                    db.ChatMessages.Add(new ChatMessage { ConversationId = conversationId,
                        ManagedResearchJobId = job.Id, Role = ChatMessageRole.Assistant,
                        Content = "Ask RAVEN could not answer from the completed Investigation. You can ask again.",
                        Status = ChatMessageStatus.Failed });
                }
                else if (failed.Status == ChatMessageStatus.Pending)
                {
                    failed.Content = "Ask RAVEN could not answer from the completed Investigation. You can ask again.";
                    failed.Status = ChatMessageStatus.Failed;
                }
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception persistenceError)
            {
                logger.LogError(persistenceError, "Could not persist failed Chat answer for managed job {JobId}", job.Id);
            }
        }
    }
}
