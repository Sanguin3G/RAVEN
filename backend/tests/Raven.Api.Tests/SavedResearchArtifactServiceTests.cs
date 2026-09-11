using Raven.Api.Features.Research.SavedArtifacts;

namespace Raven.Api.Tests;

public sealed class SavedResearchArtifactServiceTests
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherCompanyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceOne = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SourceTwo = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid SourceFromOtherCompany = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateAsync_persists_a_company_scoped_artifact_and_derives_source_count()
    {
        var store = new InMemorySavedResearchArtifactStore();
        var service = CreateService(store, new SourceOwnershipReader(SourceOne, SourceTwo));
        var conversationId = Guid.NewGuid();
        var deepResearchRunId = Guid.NewGuid();

        var artifact = await service.CreateAsync(new SavedResearchArtifactRequest(
            CompanyId,
            "  Market outlook  ",
            "  What is the company's outlook? ",
            "  The company is expanding into two new markets.  ",
            SavedResearchType.Deep,
            "  gemini-3.8-flash  ",
            [SourceOne, SourceTwo, SourceOne],
            conversationId,
            deepResearchRunId));

        Assert.Equal(CompanyId, artifact.CompanyId);
        Assert.Equal(conversationId, artifact.ConversationId);
        Assert.Equal(deepResearchRunId, artifact.DeepResearchRunId);
        Assert.Equal("Market outlook", artifact.Title);
        Assert.Equal("What is the company's outlook?", artifact.Question);
        Assert.Equal("The company is expanding into two new markets.", artifact.Summary);
        Assert.Equal(artifact.Summary, artifact.Result);
        Assert.Equal(CreatedAt, artifact.CreatedAt);
        Assert.Equal(SavedResearchType.Deep, artifact.ResearchType);
        Assert.Equal("gemini-3.8-flash", artifact.Model);
        Assert.Equal(2, artifact.SourceCount);
        Assert.Equal([SourceOne, SourceTwo], artifact.SourceDocumentIds);

        var reloaded = await store.GetAsync(CompanyId, artifact.Id);
        Assert.NotNull(reloaded);
        Assert.NotSame(artifact, reloaded);
        Assert.Equal(artifact.SourceDocumentIds, reloaded!.SourceDocumentIds);

        artifact.SourceDocumentIds.Add(Guid.NewGuid());
        Assert.Equal(2, reloaded.SourceDocumentIds.Count);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_source_owned_by_another_company_and_does_not_save()
    {
        var store = new InMemorySavedResearchArtifactStore();
        var service = CreateService(store, new SourceOwnershipReader(SourceOne));

        var exception = await Assert.ThrowsAsync<SavedResearchArtifactValidationException>(() =>
            service.CreateAsync(ValidRequest([SourceOne, SourceFromOtherCompany])));

        Assert.Contains("not owned by the company", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await store.ListAsync(CompanyId));
    }

    [Fact]
    public async Task CreateAsync_reports_request_validation_errors_before_ownership_lookup()
    {
        var ownership = new SourceOwnershipReader();
        var service = CreateService(new InMemorySavedResearchArtifactStore(), ownership);
        var request = new SavedResearchArtifactRequest(
            Guid.Empty,
            " ",
            " ",
            " ",
            (SavedResearchType)999,
            SourceDocumentIds: [Guid.Empty],
            ConversationId: Guid.Empty,
            DeepResearchRunId: Guid.Empty);

        var exception = await Assert.ThrowsAsync<SavedResearchArtifactValidationException>(() =>
            service.CreateAsync(request));

        Assert.Contains("company ID", exception.Errors[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Title is required.", exception.Errors);
        Assert.Contains("Question is required.", exception.Errors);
        Assert.Contains("Summary is required.", exception.Errors);
        Assert.Contains("Research type is not supported.", exception.Errors);
        Assert.Contains("Source document IDs must be non-empty IDs.", exception.Errors);
        Assert.Contains("Conversation ID must be a non-empty ID when provided.", exception.Errors);
        Assert.Contains("Deep Research run ID must be a non-empty ID when provided.", exception.Errors);
        Assert.Equal(0, ownership.LookupCount);
    }

    [Fact]
    public async Task ListAsync_and_GetAsync_are_scoped_to_the_requested_company()
    {
        var store = new InMemorySavedResearchArtifactStore();
        var ownership = new SourceOwnershipReader(SourceOne, SourceTwo);
        var service = CreateService(store, ownership);
        var first = await service.CreateAsync(ValidRequest([SourceOne]));
        var second = await service.CreateAsync(ValidRequest([SourceTwo]) with { CompanyId = OtherCompanyId });

        var companyArtifacts = await service.ListAsync(CompanyId);

        var onlyArtifact = Assert.Single(companyArtifacts);
        Assert.Equal(first.Id, onlyArtifact.Id);
        Assert.Null(await service.GetAsync(CompanyId, second.Id));
        Assert.Equal(second.Id, (await service.GetAsync(OtherCompanyId, second.Id))!.Id);
    }

    [Fact]
    public async Task CreateAsync_treats_an_optional_blank_model_as_unknown()
    {
        var service = CreateService(
            new InMemorySavedResearchArtifactStore(),
            new SourceOwnershipReader());

        var artifact = await service.CreateAsync(ValidRequest() with { Model = "  " });

        Assert.Null(artifact.Model);
    }

    private static SavedResearchArtifactService CreateService(
        ISavedResearchArtifactStore store,
        SourceOwnershipReader ownership) =>
        new(store, ownership, new TestClock(CreatedAt));

    private static SavedResearchArtifactRequest ValidRequest(
        IReadOnlyList<Guid>? sourceDocumentIds = null) =>
        new(
            CompanyId,
            "Market outlook",
            "What is the company's outlook?",
            "The company is expanding.",
            SavedResearchType.Fast,
            "gemini-3.5-flash-lite",
            sourceDocumentIds);

    private sealed class TestClock(DateTimeOffset now) : ISavedResearchArtifactClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class SourceOwnershipReader(params Guid[] ownedSourceDocumentIds)
        : ISourceDocumentOwnershipReader
    {
        private readonly HashSet<Guid> owned = ownedSourceDocumentIds.ToHashSet();

        public int LookupCount { get; private set; }

        public Task<IReadOnlySet<Guid>> GetOwnedSourceDocumentIdsAsync(
            Guid companyId,
            IReadOnlyCollection<Guid> sourceDocumentIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LookupCount++;
            IReadOnlySet<Guid> result = sourceDocumentIds.Where(owned.Contains).ToHashSet();
            return Task.FromResult(result);
        }
    }
}
