using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Profiles.Persistence;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Sources;

namespace Raven.Api.Tests;

public sealed class CompanyProfilePersistenceServiceTests : IDisposable
{
    private static readonly Guid CompanyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid RunId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid FirstCandidateId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid SecondCandidateId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly RavenDbContext dbContext;

    public CompanyProfilePersistenceServiceTests()
    {
        connection.Open();
        dbContext = CreateContext();
        dbContext.Database.EnsureCreated();
        SeedCompanyRunAndSource();
    }

    [Fact]
    public async Task Candidate_is_reviewable_but_no_profile_exists_before_confirmation()
    {
        var service = new CompanyProfilePersistenceService(dbContext);

        var saved = await service.SaveCandidateAsync(Candidate(FirstCandidateId, "Example Co"));

        Assert.NotNull(saved);
        Assert.Equal("Example Co", saved!.DisplayName);
        Assert.NotNull(await service.GetCandidateAsync(FirstCandidateId));
        Assert.Null(await service.GetCurrentProfileAsync(CompanyId));
        Assert.Empty(await dbContext.CompanyProfileVersions.ToListAsync());
    }

    [Fact]
    public async Task Confirmation_allocates_an_immutable_per_company_version()
    {
        var service = new CompanyProfilePersistenceService(dbContext);

        await service.SaveCandidateAsync(Candidate(FirstCandidateId, "First snapshot"));
        var first = await service.ConfirmCandidateAsync(FirstCandidateId);

        await service.SaveCandidateAsync(Candidate(SecondCandidateId, "Second snapshot"));
        var second = await service.ConfirmCandidateAsync(SecondCandidateId);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1, first!.Version);
        Assert.Equal(2, second!.Version);
        Assert.Equal(2, await dbContext.CompanyProfileVersions.CountAsync());
        Assert.Contains("First snapshot", (await dbContext.CompanyProfileVersions
            .SingleAsync(profile => profile.Version == 1)).ProfileJson);
    }

    [Fact]
    public async Task Confirmation_persists_and_reopens_profile_evidence()
    {
        var service = new CompanyProfilePersistenceService(dbContext);
        var candidate = Candidate(FirstCandidateId, "Evidence-backed company");

        await service.SaveCandidateAsync(candidate);
        var confirmed = await service.ConfirmCandidateAsync(FirstCandidateId);

        Assert.NotNull(confirmed);
        var evidenceRow = await dbContext.ProfileEvidences.SingleAsync();
        Assert.Equal(confirmed!.Id, evidenceRow.CompanyProfileVersionId);
        Assert.Equal("[\"33333333-3333-3333-3333-333333333333\"]", evidenceRow.SourceDocumentIdsJson);

        await using var reopenedContext = CreateContext();
        var reopened = await new CompanyProfilePersistenceService(reopenedContext)
            .GetCurrentProfileAsync(CompanyId);

        Assert.NotNull(reopened);
        var evidence = Assert.Single(reopened!.Evidence);
        Assert.Equal("displayName", evidence.FieldPath);
        Assert.Equal([SourceId], evidence.SourceDocumentIds);
        Assert.Equal("Evidence-backed company", reopened.DisplayName);
    }

    [Fact]
    public async Task Foreign_company_or_research_run_candidate_cannot_be_saved_or_confirmed()
    {
        var service = new CompanyProfilePersistenceService(dbContext);

        var foreignCompany = Candidate(FirstCandidateId, "Foreign", companyId: Guid.NewGuid());
        Assert.Null(await service.SaveCandidateAsync(foreignCompany));

        var foreignRunId = Guid.NewGuid();
        var maliciousCandidate = Candidate(SecondCandidateId, "Foreign run", researchRunId: foreignRunId);
        dbContext.CompanyProfileCandidates.Add(new CompanyProfileCandidate
        {
            Id = maliciousCandidate.Id,
            CompanyId = maliciousCandidate.CompanyId,
            ResearchRunId = maliciousCandidate.ResearchRunId,
            GeneratedAt = maliciousCandidate.GeneratedAt,
            CandidateJson = JsonSerializer.Serialize(maliciousCandidate)
        });
        await dbContext.SaveChangesAsync();

        Assert.Null(await service.ConfirmCandidateAsync(SecondCandidateId));
        Assert.Empty(await dbContext.CompanyProfileVersions.ToListAsync());
    }

    [Fact]
    public async Task Malformed_persisted_candidate_or_profile_is_treated_as_absent()
    {
        var candidate = Candidate(FirstCandidateId, "Malformed");
        dbContext.CompanyProfileCandidates.Add(new CompanyProfileCandidate
        {
            Id = candidate.Id,
            CompanyId = candidate.CompanyId,
            ResearchRunId = candidate.ResearchRunId,
            GeneratedAt = candidate.GeneratedAt,
            CandidateJson = "{ not valid json"
        });
        await dbContext.SaveChangesAsync();

        var service = new CompanyProfilePersistenceService(dbContext);
        Assert.Null(await service.GetCandidateAsync(FirstCandidateId));
        Assert.Null(await service.ConfirmCandidateAsync(FirstCandidateId));

        dbContext.CompanyProfileVersions.Add(new CompanyProfileVersion
        {
            Id = Guid.NewGuid(),
            CompanyId = CompanyId,
            ResearchRunId = RunId,
            Version = 1,
            GeneratedAt = candidate.GeneratedAt,
            ConfirmedAt = candidate.GeneratedAt.AddHours(1),
            ProfileJson = "{ not valid json"
        });
        await dbContext.SaveChangesAsync();

        Assert.Null(await service.GetCurrentProfileAsync(CompanyId));
    }

    public void Dispose()
    {
        dbContext.Dispose();
        connection.Dispose();
    }

    private RavenDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<RavenDbContext>()
            .UseSqlite(connection)
            .Options);

    private void SeedCompanyRunAndSource()
    {
        var company = new Company
        {
            Id = CompanyId,
            Name = "Example Co",
            Country = "Vietnam"
        };
        var run = new ResearchRun
        {
            Id = RunId,
            CompanyId = CompanyId,
            Company = company,
            RequestedSearchProvider = "fake-search",
            RequestedCrawlerProvider = "fake-crawler",
            Stage = ResearchStage.EvidenceReady
        };
        var source = new SourceDocument
        {
            Id = SourceId,
            CompanyId = CompanyId,
            Company = company,
            ResearchRunId = RunId,
            ResearchRun = run,
            Url = "https://example.com/about",
            NormalizedUrl = "https://example.com/about",
            Title = "Example Co About",
            SourceDomain = "example.com",
            SourceKind = SourceKind.OfficialWebsite,
            RetrievedAt = DateTimeOffset.UtcNow,
            Content = "Example Co public evidence.",
            ContentHash = "example-hash",
            CrawlerProvider = "fake-crawler"
        };

        dbContext.Companies.Add(company);
        dbContext.ResearchRuns.Add(run);
        dbContext.SourceDocuments.Add(source);
        dbContext.SaveChanges();
    }

    private static CompanyProfileCandidate Candidate(
        Guid id,
        string displayName,
        Guid? companyId = null,
        Guid? researchRunId = null)
    {
        var candidate = new CompanyProfileCandidate
        {
            Id = id,
            CompanyId = companyId ?? CompanyId,
            ResearchRunId = researchRunId ?? RunId,
            GeneratedAt = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero),
            AiProvider = "fake",
            AiModel = "fake-model",
            PromptTemplateVersion = "test-v1",
            DisplayName = displayName,
            PrimaryIndustry = "Technology"
        };
        candidate.Evidence.Add(new ProfileEvidence
        {
            FieldPath = "displayName",
            SourceDocumentIds = { SourceId }
        });
        return candidate;
    }
}
