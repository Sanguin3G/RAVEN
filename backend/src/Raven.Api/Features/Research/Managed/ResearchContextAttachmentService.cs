using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Research.Briefings;

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

    public Task<ResearchContextAttachment?> GetBriefingAsync(
        Guid companyId,
        Guid conversationId,
        Guid briefingId,
        CancellationToken cancellationToken = default) =>
        db.ResearchContextAttachments.AsNoTracking().SingleOrDefaultAsync(
            item => item.CompanyId == companyId &&
                    item.ConversationId == conversationId &&
                    item.BriefingId == briefingId,
            cancellationToken);

    public async Task AddAsync(ResearchContextAttachment attachment, CancellationToken cancellationToken = default)
    {
        db.ResearchContextAttachments.Add(attachment);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(ResearchContextAttachment attachment, CancellationToken cancellationToken = default)
    {
        db.ResearchContextAttachments.Update(attachment);
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

    public async Task<bool> RemoveBriefingAsync(
        Guid companyId,
        Guid conversationId,
        Guid briefingId,
        CancellationToken cancellationToken = default)
    {
        var attachment = await db.ResearchContextAttachments.SingleOrDefaultAsync(
            item => item.CompanyId == companyId &&
                    item.ConversationId == conversationId &&
                    item.BriefingId == briefingId,
            cancellationToken);
        if (attachment is null) return false;

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
/// Coordinates attachment state with completed Investigations and immutable
/// Briefing versions. It has
/// no profile writer and does not alter the Chat agent contract.
/// </summary>
public sealed class ResearchContextAttachmentService(
    IResearchContextAttachmentStore attachments,
    IManagedResearchInvestigationStore investigations,
    IManagedResearchClock clock,
    RavenDbContext? db = null) : IResearchContextAttachmentService
{
    public async Task<IReadOnlyList<ResearchContextAttachmentResponse>> ListAsync(
        Guid companyId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(companyId, conversationId);
        await ValidateConversationAsync(companyId, conversationId, cancellationToken);
        var rows = await attachments.ListAsync(companyId, conversationId, cancellationToken);
        var responses = new List<ResearchContextAttachmentResponse>(rows.Count);
        foreach (var row in rows)
        {
            if (row.InvestigationId is { } investigationId)
            {
                var investigation = await investigations.GetAsync(companyId, investigationId, cancellationToken);
                if (investigation is not null) responses.Add(ToResponse(row, investigation));
                continue;
            }

            if (db is not null && row.BriefingId is { } briefingId && row.BriefingVersionId is { } versionId)
            {
                var briefing = await db.ResearchBriefings.AsNoTracking()
                    .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == briefingId, cancellationToken);
                var version = await db.ResearchBriefingVersions.AsNoTracking()
                    .SingleOrDefaultAsync(item => item.BriefingId == briefingId && item.Id == versionId, cancellationToken);
                if (briefing is not null && version is not null) responses.Add(ToResponse(row, briefing, version));
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
        await ValidateConversationAsync(companyId, request.ConversationId, cancellationToken);
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

        await EnsureCapacityAsync(companyId, request.ConversationId, request, cancellationToken);

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

    public async Task<ResearchContextAttachmentResponse> AttachBriefingAsync(
        Guid companyId,
        Guid briefingId,
        int versionNumber,
        AttachResearchContextRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateScope(companyId, request.ConversationId);
        await ValidateConversationAsync(companyId, request.ConversationId, cancellationToken);
        if (briefingId == Guid.Empty || versionNumber < 1)
            throw new ArgumentException("A valid Briefing and version are required.", nameof(briefingId));
        if (db is null) throw new InvalidOperationException("Briefing context requires the application database.");

        var briefing = await db.ResearchBriefings.AsNoTracking().SingleOrDefaultAsync(
            item => item.CompanyId == companyId && item.Id == briefingId && item.ArchivedAt == null,
            cancellationToken) ?? throw new KeyNotFoundException("The active Briefing does not belong to this company.");
        var version = await db.ResearchBriefingVersions.AsNoTracking().SingleOrDefaultAsync(
            item => item.BriefingId == briefingId && item.VersionNumber == versionNumber,
            cancellationToken) ?? throw new KeyNotFoundException("The Briefing version does not exist.");

        var existing = await attachments.GetBriefingAsync(companyId, request.ConversationId, briefingId, cancellationToken);
        if (existing is not null)
        {
            if (existing.BriefingVersionId != version.Id)
            {
                existing.BriefingVersionId = version.Id;
                existing.AttachedAt = clock.UtcNow;
                await attachments.UpdateAsync(existing, cancellationToken);
            }
            return ToResponse(existing, briefing, version);
        }

        await EnsureCapacityAsync(companyId, request.ConversationId, request, cancellationToken);
        var attachment = new ResearchContextAttachment
        {
            CompanyId = companyId,
            ConversationId = request.ConversationId,
            BriefingId = briefingId,
            BriefingVersionId = version.Id,
            AttachedAt = clock.UtcNow
        };
        await attachments.AddAsync(attachment, cancellationToken);
        return ToResponse(attachment, briefing, version);
    }

    public async Task<bool> RemoveAsync(
        Guid companyId,
        Guid conversationId,
        Guid investigationId,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(companyId, conversationId);
        await ValidateConversationAsync(companyId, conversationId, cancellationToken);
        if (investigationId == Guid.Empty)
        {
            throw new ArgumentException("An investigation ID is required.", nameof(investigationId));
        }

        return await attachments.RemoveAsync(companyId, conversationId, investigationId, cancellationToken);
    }

    public async Task<bool> RemoveBriefingAsync(
        Guid companyId,
        Guid conversationId,
        Guid briefingId,
        CancellationToken cancellationToken = default)
    {
        ValidateScope(companyId, conversationId);
        await ValidateConversationAsync(companyId, conversationId, cancellationToken);
        if (briefingId == Guid.Empty)
            throw new ArgumentException("A Briefing ID is required.", nameof(briefingId));
        return await attachments.RemoveBriefingAsync(companyId, conversationId, briefingId, cancellationToken);
    }

    private static ResearchContextAttachmentResponse ToResponse(
        ResearchContextAttachment attachment,
        ManagedResearchInvestigation investigation) =>
        new(
            attachment.Id,
            attachment.CompanyId,
            attachment.ConversationId,
            "Investigation",
            attachment.AttachedAt,
            InvestigationId: investigation.Id,
            Origin: investigation.Origin,
            Objective: investigation.Objective,
            Summary: investigation.Summary,
            CompletedAt: investigation.CompletedAt);

    private static ResearchContextAttachmentResponse ToResponse(
        ResearchContextAttachment attachment,
        ResearchBriefing briefing,
        ResearchBriefingVersion version) =>
        new(
            attachment.Id,
            attachment.CompanyId,
            attachment.ConversationId,
            "Briefing",
            attachment.AttachedAt,
            BriefingId: briefing.Id,
            BriefingVersionId: version.Id,
            BriefingVersionNumber: version.VersionNumber,
            Title: version.Title,
            Template: version.Template,
            ResearchThrough: version.ResearchThrough);

    private async Task EnsureCapacityAsync(Guid companyId, Guid conversationId, AttachResearchContextRequest request,
        CancellationToken cancellationToken)
    {
        var running = db is null ? 0 : await db.ManagedResearchJobs.CountAsync(item =>
            item.CompanyId == companyId && item.ConversationId == conversationId && item.AnswerInChat &&
            (item.Status == ManagedResearchJobStatus.Queued || item.Status == ManagedResearchJobStatus.Researching), cancellationToken);
        if ((await attachments.ListAsync(companyId, conversationId, cancellationToken)).Count + running >= 5)
            throw new ArgumentException("A chat can attach at most five research context items.", nameof(request));
    }

    private async Task ValidateConversationAsync(Guid companyId, Guid conversationId, CancellationToken cancellationToken)
    {
        if (db is not null && !await db.ChatConversations.AnyAsync(
                item => item.Id == conversationId && item.CompanyId == companyId, cancellationToken))
            throw new KeyNotFoundException("The conversation does not belong to this company.");
    }

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
