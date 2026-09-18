using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;

namespace Raven.Api.Features.ManagedResearch;

/// <summary>EF persistence for explicit research context attachments.</summary>
public sealed class EfResearchContextAttachmentStore(RavenDbContext db) : IResearchContextAttachmentStore
{
    public Task<ResearchContextAttachment?> GetAsync(
        Guid companyId,
        Guid conversationId,
        Guid investigationId,
        CancellationToken cancellationToken = default) =>
        db.ResearchContextAttachments.AsNoTracking().SingleOrDefaultAsync(
            item => item.CompanyId == companyId &&
                    item.ConversationId == conversationId &&
                    item.InvestigationId == investigationId,
            cancellationToken);

    public async Task AddAsync(ResearchContextAttachment attachment, CancellationToken cancellationToken = default)
    {
        db.ResearchContextAttachments.Add(attachment);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RemoveAsync(
        Guid companyId,
        Guid conversationId,
        Guid investigationId,
        CancellationToken cancellationToken = default)
    {
        var attachment = await db.ResearchContextAttachments.SingleOrDefaultAsync(
            item => item.CompanyId == companyId &&
                    item.ConversationId == conversationId &&
                    item.InvestigationId == investigationId,
            cancellationToken);
        if (attachment is null)
        {
            return false;
        }

        db.ResearchContextAttachments.Remove(attachment);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ResearchContextAttachment>> ListAsync(
        Guid companyId,
        Guid conversationId,
        CancellationToken cancellationToken = default) =>
        (await db.ResearchContextAttachments.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.ConversationId == conversationId)
            .ToListAsync(cancellationToken))
        .OrderByDescending(item => item.AttachedAt)
        .ToArray();
}

/// <summary>
/// Coordinates attachment state with completed investigation records. It has
/// no profile writer and does not alter the Chat agent contract.
/// </summary>
public sealed class ResearchContextAttachmentService(
    IResearchContextAttachmentStore attachments,
    IManagedResearchInvestigationStore investigations,
    IManagedResearchClock clock) : IResearchContextAttachmentService
{
    public async Task<IReadOnlyList<ResearchContextAttachmentResponse>> ListAsync(
        Guid companyId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(companyId, conversationId);
        var rows = await attachments.ListAsync(companyId, conversationId, cancellationToken);
        var responses = new List<ResearchContextAttachmentResponse>(rows.Count);
        foreach (var row in rows)
        {
            var investigation = await investigations.GetAsync(companyId, row.InvestigationId, cancellationToken);
            if (investigation is not null)
            {
                responses.Add(ToResponse(row, investigation));
            }
        }

        return responses;
    }

    public async Task<ResearchContextAttachmentResponse> AttachAsync(
        Guid companyId,
        Guid investigationId,
        AttachResearchContextRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateScope(companyId, request.ConversationId);
        if (investigationId == Guid.Empty)
        {
            throw new ArgumentException("An investigation ID is required.", nameof(investigationId));
        }

        var investigation = await investigations.GetAsync(companyId, investigationId, cancellationToken)
            ?? throw new KeyNotFoundException("The investigation does not belong to this company.");
        var existing = await attachments.GetAsync(
            companyId,
            request.ConversationId,
            investigationId,
            cancellationToken);
        if (existing is not null)
        {
            return ToResponse(existing, investigation);
        }

        var attachment = new ResearchContextAttachment
        {
            CompanyId = companyId,
            ConversationId = request.ConversationId,
            InvestigationId = investigationId,
            AttachedAt = clock.UtcNow
        };
        await attachments.AddAsync(attachment, cancellationToken);
        return ToResponse(attachment, investigation);
    }

    public async Task<bool> RemoveAsync(
        Guid companyId,
        Guid conversationId,
        Guid investigationId,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(companyId, conversationId);
        if (investigationId == Guid.Empty)
        {
            throw new ArgumentException("An investigation ID is required.", nameof(investigationId));
        }

        return await attachments.RemoveAsync(companyId, conversationId, investigationId, cancellationToken);
    }

    private static ResearchContextAttachmentResponse ToResponse(
        ResearchContextAttachment attachment,
        ManagedResearchInvestigation investigation) =>
        new(
            attachment.Id,
            attachment.CompanyId,
            attachment.ConversationId,
            attachment.InvestigationId,
            investigation.Origin,
            investigation.Objective,
            investigation.Summary,
            investigation.CompletedAt,
            attachment.AttachedAt);

    private static void ValidateScope(Guid companyId, Guid conversationId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("A company ID is required.", nameof(companyId));
        }

        if (conversationId == Guid.Empty)
        {
            throw new ArgumentException("A conversation ID is required.", nameof(conversationId));
        }
    }
}
