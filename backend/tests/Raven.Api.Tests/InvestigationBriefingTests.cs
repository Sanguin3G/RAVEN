using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Ai;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Profiles.Persistence;
using Raven.Api.Features.Research.Briefings;
using Raven.Api.Features.Research.SavedArtifacts;

namespace Raven.Api.Tests;

public sealed class InvestigationBriefingTests : IDisposable
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherCompanyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly RavenDbContext db;
    private readonly InvestigationService investigations;
    private readonly BriefingService briefings;
    private readonly SavedResearchArtifactService saved;
    private readonly FakeAi ai = new();

    public InvestigationBriefingTests()
    {
        connection.Open();
        db = new RavenDbContext(new DbContextOptionsBuilder<RavenDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        db.Companies.AddRange(new Company { Id = CompanyId, Name = "Example" }, new Company { Id = OtherCompanyId, Name = "Other" });
        db.SaveChanges();
        saved = new SavedResearchArtifactService(new EfSavedResearchArtifactStore(db), new EfSourceDocumentOwnershipReader(db), new FixedClock());
        investigations = new InvestigationService(db, saved, new CompanyProfilePersistenceService(db));
        briefings = new BriefingService(db, investigations, new BriefingGenerator(ai, new FixedModels()));
    }

    [Fact]
    public async Task Purpose_is_explicit_and_done_state_uses_a_material_watermark()
    {
        var artifact = await SaveAsync("Legal market research", InvestigationPurpose.GeneralResearch);
        var item = Assert.Single(await investigations.ListAsync(CompanyId, default));
        Assert.Equal(InvestigationPurpose.GeneralResearch, item.Purpose);
        Assert.Equal(InvestigationStatus.Ready, item.Status);

        var done = await investigations.MarkDoneAsync(CompanyId, artifact.Id, default);
        Assert.Equal(InvestigationStatus.Done, done!.Status);
        var state = Assert.Single(db.InvestigationReviewStates);
        Assert.Equal(item.MaterialUpdatedAt, state.DoneThrough);
        Assert.True(state.IsDone(item.MaterialUpdatedAt));
        Assert.False(state.IsDone(item.MaterialUpdatedAt.AddMinutes(1)));

        var reopened = await investigations.ReopenAsync(CompanyId, artifact.Id, default);
        Assert.Equal(InvestigationStatus.Ready, reopened!.Status);
        Assert.NotNull(state.DoneAt);
        Assert.NotNull(state.ReopenedAt);
    }

    [Fact]
    public async Task Briefing_versions_keep_selected_snapshots_and_reject_foreign_material()
    {
        var first = await SaveAsync("European hiring", InvestigationPurpose.GeneralResearch);
        var second = await SaveAsync("Technical roles", InvestigationPurpose.ProfileImprovement);
        var foreign = await SaveAsync("Foreign research", InvestigationPurpose.GeneralResearch, OtherCompanyId);
        await Assert.ThrowsAsync<ArgumentException>(() => briefings.CreateAsync(CompanyId,
            new CreateBriefingRequest("Hiring", "Talent & Hiring", "Hiring signals", [foreign.Id]), default));
        Assert.Empty(db.ResearchBriefings);

        var created = await briefings.CreateAsync(CompanyId,
            new CreateBriefingRequest("Hiring", "Talent & Hiring", "Hiring signals", [first.Id]), default);
        Assert.Equal(1, created.CurrentVersion.VersionNumber);
        Assert.Equal(first.Id, Assert.Single(created.CurrentVersion.Sources).InvestigationId);
        Assert.Equal(created.CurrentVersion.Sources[0].MaterialUpdatedAt, created.CurrentVersion.ResearchThrough);

        var updated = await briefings.UpdateAsync(CompanyId, created.Id,
            new UpdateBriefingRequest([second.Id]), default);
        Assert.NotNull(updated);
        Assert.Equal(2, updated!.CurrentVersion.VersionNumber);
        Assert.Equal(2, updated.CurrentVersion.Sources.Count);
        var old = await briefings.VersionAsync(CompanyId, created.Id, 1, default);
        Assert.Equal(first.Id, Assert.Single(old!.Sources).InvestigationId);
        Assert.Equal(2, db.ResearchBriefingVersions.Count());
        Assert.Empty(db.CompanyProfileVersions);
        Assert.Equal(2, ai.Calls);
    }

    [Fact]
    public async Task Newer_research_requires_company_topic_and_material_date()
    {
        var first = await SaveAsync("Hiring signals", InvestigationPurpose.GeneralResearch);
        var created = await briefings.CreateAsync(CompanyId,
            new CreateBriefingRequest("Hiring", "Talent & Hiring", "Hiring signals", [first.Id]), default);
        var newer = await saved.CreateAsync(new SavedResearchArtifactRequest(CompanyId, "Recent recruitment", "Who is hiring?", "New roles",
            SavedResearchType.Deep, CompletedAt: created.CurrentVersion.ResearchThrough.AddDays(1)));
        await saved.CreateAsync(new SavedResearchArtifactRequest(CompanyId, "Legal identity", "What is the legal name?", "Name",
            SavedResearchType.Deep, CompletedAt: created.CurrentVersion.ResearchThrough.AddDays(2)));

        var candidates = await briefings.NewerAsync(CompanyId, created.Id, default);
        Assert.Equal(newer.Id, Assert.Single(candidates!).Id);
    }

    private async Task<SavedResearchArtifact> SaveAsync(string title, InvestigationPurpose purpose, Guid? companyId = null)
    {
        return await saved.CreateAsync(new SavedResearchArtifactRequest(companyId ?? CompanyId, title, title, title,
            SavedResearchType.Deep, Purpose: purpose));
    }

    public void Dispose() { db.Dispose(); connection.Dispose(); }

    private sealed class FixedClock : ISavedResearchArtifactClock { public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-09-18T00:00:00Z"); }
    private sealed class FixedModels : IRuntimeModelPreferences
    {
        public RuntimeModelPreferenceResponse Current => new("gemini-3.5-flash-lite", "gemini-3.8-flash");
        public bool TryUpdate(UpdateRuntimeModelPreferencesRequest request, out RuntimeModelPreferenceResponse preferences) { preferences = Current; return false; }
    }
    private sealed class FakeAi : IAiModelProvider
    {
        public string Id => "fake";
        public int Calls { get; private set; }
        public Task<AiModelResult> GenerateStructuredAsync(AiModelRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            using var json = JsonDocument.Parse("""
                { "sections": [{ "title": "Key takeaways", "items": ["Hiring signal from selected material"], "sourceInvestigationIds": [] }] }
                """);
            return Task.FromResult(new AiModelResult(Id, request.Model, json.RootElement.Clone(), TimeSpan.Zero));
        }
    }
}
