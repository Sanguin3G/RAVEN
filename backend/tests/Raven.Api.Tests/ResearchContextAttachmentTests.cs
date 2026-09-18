using Raven.Api.Features.ManagedResearch;

namespace Raven.Api.Tests;

public sealed class ResearchContextAttachmentTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ConversationId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid InvestigationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Attach_is_idempotent_and_list_remove_are_explicit()
    {
        var investigationStore = new FakeInvestigationStore(new ManagedResearchInvestigation
        {
            Id = InvestigationId,
            JobId = Guid.NewGuid(),
            CompanyId = CompanyId,
            Origin = "ManagedAi",
            Objective = "Research the Japan expansion",
            Summary = "The company opened a new Japan office.",
            ResultJson = "{\"summary\":\"The company opened a new Japan office.\"}",
            CompletedAt = Now.AddMinutes(-5)
        });
        var attachmentStore = new FakeAttachmentStore();
        var service = new ResearchContextAttachmentService(
            attachmentStore,
            investigationStore,
            new FixedClock(Now));

        var first = await service.AttachAsync(
            CompanyId,
            InvestigationId,
            new AttachResearchContextRequest(ConversationId));
        var second = await service.AttachAsync(
            CompanyId,
            InvestigationId,
            new AttachResearchContextRequest(ConversationId));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Research the Japan expansion", first.Objective);
        Assert.Equal(Now, first.AttachedAt);
        Assert.Single(await service.ListAsync(CompanyId, ConversationId));

        Assert.True(await service.RemoveAsync(CompanyId, ConversationId, InvestigationId));
        Assert.False(await service.RemoveAsync(CompanyId, ConversationId, InvestigationId));
        Assert.Empty(await service.ListAsync(CompanyId, ConversationId));
    }

    [Fact]
    public async Task Attach_rejects_an_investigation_from_another_company()
    {
        var attachmentStore = new FakeAttachmentStore();
        var service = new ResearchContextAttachmentService(
            attachmentStore,
            new FakeInvestigationStore(new ManagedResearchInvestigation
            {
                Id = InvestigationId,
                JobId = Guid.NewGuid(),
                CompanyId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Origin = "ManagedAi",
                Objective = "Other company",
                Summary = "Not available here",
                ResultJson = "{}",
                CompletedAt = Now
            }),
            new FixedClock(Now));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.AttachAsync(
            CompanyId,
            InvestigationId,
            new AttachResearchContextRequest(ConversationId)));
        Assert.Empty(await service.ListAsync(CompanyId, ConversationId));
    }

    private sealed class FixedClock(DateTimeOffset value) : IManagedResearchClock
    {
        public DateTimeOffset UtcNow { get; } = value;
    }

    private sealed class FakeInvestigationStore(ManagedResearchInvestigation investigation) : IManagedResearchInvestigationStore
    {
        public Task SaveAsync(ManagedResearchInvestigation item, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<ManagedResearchInvestigation?> GetAsync(Guid companyId, Guid investigationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ManagedResearchInvestigation?>(investigation.CompanyId == companyId && investigation.Id == investigationId ? investigation : null);

        public Task<IReadOnlyList<ManagedResearchInvestigation>> ListForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ManagedResearchInvestigation>>(investigation.CompanyId == companyId ? [investigation] : []);
    }

    private sealed class FakeAttachmentStore : IResearchContextAttachmentStore
    {
        private readonly List<ResearchContextAttachment> rows = [];

        public Task<ResearchContextAttachment?> GetAsync(Guid companyId, Guid conversationId, Guid investigationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(rows.SingleOrDefault(item => item.CompanyId == companyId && item.ConversationId == conversationId && item.InvestigationId == investigationId));

        public Task AddAsync(ResearchContextAttachment attachment, CancellationToken cancellationToken = default)
        {
            rows.Add(attachment);
            return Task.CompletedTask;
        }

        public Task<bool> RemoveAsync(Guid companyId, Guid conversationId, Guid investigationId, CancellationToken cancellationToken = default)
        {
            var row = rows.SingleOrDefault(item => item.CompanyId == companyId && item.ConversationId == conversationId && item.InvestigationId == investigationId);
            if (row is null) return Task.FromResult(false);
            rows.Remove(row);
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<ResearchContextAttachment>> ListAsync(Guid companyId, Guid conversationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResearchContextAttachment>>(rows.Where(item => item.CompanyId == companyId && item.ConversationId == conversationId).ToArray());
    }
}
