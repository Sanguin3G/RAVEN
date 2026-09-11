using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Raven.Api.Data;
using Raven.Api.Features.Companies;
using Raven.Api.Features.DeepResearch;
using Raven.Api.Features.Monitoring;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Profiles.Changes;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.SavedArtifacts;
using Raven.Api.Features.Research.Sources;

namespace Raven.Api.Tests;

public sealed class CompanyLifecycleServiceTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly RavenDbContext dbContext;

    public CompanyLifecycleServiceTests()
    {
        connection.Open();
        dbContext = CreateContext();
        dbContext.Database.EnsureCreated();
    }

    [Fact]
    public async Task Archive_and_restore_are_reversible_and_preserve_identity()
    {
        var company = NewCompany("Archive me");
        dbContext.Companies.Add(company);
        await dbContext.SaveChangesAsync();
        var service = new CompanyLifecycleService(dbContext);

        var archived = await service.ArchiveAsync(company.Id, CancellationToken.None);

        Assert.NotNull(archived);
        Assert.NotNull(archived!.ArchivedAt);
        Assert.Equal(company.Id, archived.Id);

        var restored = await service.RestoreAsync(company.Id, CancellationToken.None);

        Assert.NotNull(restored);
        Assert.Null(restored!.ArchivedAt);
        Assert.Equal(company.Name, restored.Name);
        Assert.Equal(company.Id, await dbContext.Companies.Select(item => item.Id).SingleAsync());
    }

    [Fact]
    public async Task Permanent_delete_removes_dependent_research_profile_monitoring_and_investigation_data()
    {
        var company = NewCompany("Delete me");
        var run = NewResearchRun(company);
        var source = NewSource(company, run, "https://delete.example/about", "delete-hash");
        var candidate = new CompanyProfileCandidate
        {
            CompanyId = company.Id,
            ResearchRunId = run.Id,
            DisplayName = company.Name,
            CandidateJson = "{}"
        };
        var profile = new CompanyProfileVersion
        {
            CompanyId = company.Id,
            ResearchRunId = run.Id,
            Version = 1,
            GeneratedAt = DateTimeOffset.UtcNow,
            ConfirmedAt = DateTimeOffset.UtcNow,
            ProfileJson = "{}"
        };
        dbContext.ProfileEvidences.Add(new ProfileEvidence
        {
            CompanyProfileCandidateId = candidate.Id,
            FieldPath = "displayName",
            SourceDocumentIds = { source.Id },
            SourceDocumentIdsJson = $"[\"{source.Id}\"]"
        });
        dbContext.ProfileEvidences.Add(new ProfileEvidence
        {
            CompanyProfileVersionId = profile.Id,
            FieldPath = "displayName",
            SourceDocumentIds = { source.Id },
            SourceDocumentIdsJson = $"[\"{source.Id}\"]"
        });
        var deepRun = new DeepResearchRun
        {
            CompanyId = company.Id,
            Question = "Delete test",
            Model = "test-model"
        };
        dbContext.DeepResearchActivities.Add(new DeepResearchActivityRecord
        {
            DeepResearchRunId = deepRun.Id,
            Sequence = 0,
            Type = "crawl",
            Status = DeepResearchActivityStatus.Completed,
            Label = "Read source",
            SourceDocumentIdsJson = $"[\"{source.Id}\"]"
        });
        var deleteArtifact = new SavedResearchArtifact
        {
            CompanyId = company.Id,
            Title = "Delete test",
            Question = "Delete test",
            Summary = "Delete test",
            CreatedAt = DateTimeOffset.UtcNow,
            ResearchType = SavedResearchType.Deep
        };
        deleteArtifact.SourceDocumentIds.Add(source.Id);
        dbContext.SavedResearchArtifacts.Add(deleteArtifact);
        dbContext.CompanyMonitoringSettings.Add(new CompanyMonitoringSetting
        {
            CompanyId = company.Id,
            NextRunAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        dbContext.ResearchCandidates.Add(new ResearchCandidate
        {
            ResearchRunId = run.Id,
            Url = source.Url,
            NormalizedUrl = source.NormalizedUrl,
            Domain = "delete.example",
            SourceKind = SourceKind.OfficialWebsite
        });
        dbContext.ResearchIdentityCandidates.Add(new ResearchIdentityCandidate
        {
            ResearchRunId = run.Id,
            TemporaryId = "delete-identity",
            DisplayName = company.Name,
            SupportingCandidateIdsJson = "[]"
        });
        dbContext.ResearchEvents.Add(new ResearchEvent
        {
            ResearchRunId = run.Id,
            Sequence = 0,
            Category = ResearchEventCategory.SourcePersisted,
            Status = ResearchEventStatus.Completed
        });
        dbContext.ProfileChanges.Add(new ProfileChange
        {
            CompanyId = company.Id,
            OldProfileVersionId = profile.Id,
            NewProfileVersionId = profile.Id,
            FieldPath = "displayName",
            ChangeType = ProfileChangeType.Changed
        });
        dbContext.Companies.Add(company);
        dbContext.ResearchRuns.Add(run);
        dbContext.SourceDocuments.Add(source);
        dbContext.CompanyProfileCandidates.Add(candidate);
        dbContext.CompanyProfileVersions.Add(profile);
        dbContext.DeepResearchRuns.Add(deepRun);
        await dbContext.SaveChangesAsync();

        var service = new CompanyLifecycleService(dbContext);
        var result = await service.DeleteAsync(company.Id, confirm: true, CancellationToken.None);

        Assert.Equal(CompanyDeleteOutcome.Deleted, result.Outcome);
        Assert.True(result.RelatedRecordsDeleted >= 11);
        Assert.Empty(await dbContext.Companies.ToListAsync());
        Assert.Empty(await dbContext.ResearchRuns.ToListAsync());
        Assert.Empty(await dbContext.SourceDocuments.ToListAsync());
        Assert.Empty(await dbContext.ResearchCandidates.ToListAsync());
        Assert.Empty(await dbContext.ResearchIdentityCandidates.ToListAsync());
        Assert.Empty(await dbContext.ResearchEvents.ToListAsync());
        Assert.Empty(await dbContext.CompanyProfileCandidates.ToListAsync());
        Assert.Empty(await dbContext.CompanyProfileVersions.ToListAsync());
        Assert.Empty(await dbContext.ProfileEvidences.ToListAsync());
        Assert.Empty(await dbContext.ProfileChanges.ToListAsync());
        Assert.Empty(await dbContext.DeepResearchRuns.ToListAsync());
        Assert.Empty(await dbContext.DeepResearchActivities.ToListAsync());
        Assert.Empty(await dbContext.SavedResearchArtifacts.ToListAsync());
        Assert.Empty(await dbContext.CompanyMonitoringSettings.ToListAsync());
    }

    [Fact]
    public async Task Merge_reassigns_related_data_deduplicates_sources_and_remaps_provenance()
    {
        var canonical = NewCompany("Canonical company");
        var duplicate = NewCompany("Duplicate company");
        var canonicalRun = NewResearchRun(canonical);
        var duplicateRun = NewResearchRun(duplicate);
        var canonicalSource = NewSource(canonical, canonicalRun, "https://merge.example/about", "shared-hash");
        var duplicateSource = NewSource(duplicate, duplicateRun, "https://merge.example/about", "shared-hash");
        var movedSource = NewSource(duplicate, duplicateRun, "https://merge.example/products", "products-hash");
        var canonicalProfile = new CompanyProfileVersion
        {
            CompanyId = canonical.Id,
            ResearchRunId = canonicalRun.Id,
            Version = 1,
            GeneratedAt = DateTimeOffset.UtcNow,
            ConfirmedAt = DateTimeOffset.UtcNow,
            ProfileJson = "{\"displayName\":\"Canonical\"}"
        };
        var duplicateProfile = new CompanyProfileVersion
        {
            CompanyId = duplicate.Id,
            ResearchRunId = duplicateRun.Id,
            Version = 1,
            GeneratedAt = DateTimeOffset.UtcNow,
            ConfirmedAt = DateTimeOffset.UtcNow,
            ProfileJson = "{}"
        };
        var evidence = new ProfileEvidence
        {
            CompanyProfileVersionId = duplicateProfile.Id,
            FieldPath = "summary",
            SourceDocumentIds = { duplicateSource.Id, movedSource.Id },
            SourceDocumentIdsJson = $"[\"{duplicateSource.Id}\",\"{movedSource.Id}\"]"
        };
        var artifact = new SavedResearchArtifact
        {
            CompanyId = duplicate.Id,
            Title = "Merge test",
            Question = "Merge test",
            Summary = "Merge test",
            CreatedAt = DateTimeOffset.UtcNow,
            ResearchType = SavedResearchType.Fast
        };
        artifact.SourceDocumentIds.Add(duplicateSource.Id);
        artifact.SourceDocumentIds.Add(movedSource.Id);

        dbContext.Companies.AddRange(canonical, duplicate);
        dbContext.ResearchRuns.AddRange(canonicalRun, duplicateRun);
        dbContext.SourceDocuments.AddRange(canonicalSource, duplicateSource, movedSource);
        dbContext.CompanyProfileVersions.AddRange(canonicalProfile, duplicateProfile);
        dbContext.ProfileEvidences.Add(evidence);
        dbContext.SavedResearchArtifacts.Add(artifact);
        await dbContext.SaveChangesAsync();
        dbContext.Entry(artifact).Property(item => item.SourceDocumentIdsJson).CurrentValue = $"[\"{duplicateSource.Id}\",\"{movedSource.Id}\"]";
        await dbContext.SaveChangesAsync();

        var service = new CompanyLifecycleService(dbContext);
        var preview = await service.PreviewMergeAsync(
            new CompanyMergePreviewRequest(canonical.Id, duplicate.Id),
            CancellationToken.None);

        Assert.NotNull(preview);
        Assert.Equal(2, preview!.SourceDocuments);
        Assert.Equal(1, preview.DuplicateSourceDocumentsToReuse);
        Assert.Equal(1, preview.DuplicateSourceDocumentsToMove);

        var result = await service.ConfirmMergeAsync(
            new CompanyMergeConfirmRequest(canonical.Id, duplicate.Id, Confirm: true),
            CancellationToken.None);

        Assert.Equal(CompanyMergeOutcome.Merged, result.Outcome);
        dbContext.ChangeTracker.Clear();
        Assert.Null(await dbContext.Companies.AsNoTracking().SingleOrDefaultAsync(item => item.Id == duplicate.Id));
        var mergedRun = await dbContext.ResearchRuns.AsNoTracking().SingleAsync(item => item.Id == duplicateRun.Id);
        Assert.Equal(canonical.Id, mergedRun.CompanyId);
        var mergedProfile = await dbContext.CompanyProfileVersions.AsNoTracking().SingleAsync(item => item.Id == duplicateProfile.Id);
        Assert.Equal(canonical.Id, mergedProfile.CompanyId);
        Assert.Equal(2, mergedProfile.Version);
        Assert.Equal(2, await dbContext.CompanyProfileVersions.CountAsync(item => item.CompanyId == canonical.Id));
        Assert.Equal(2, await dbContext.SourceDocuments.CountAsync(item => item.CompanyId == canonical.Id));
        Assert.Null(await dbContext.SourceDocuments.AsNoTracking().SingleOrDefaultAsync(item => item.Id == duplicateSource.Id));
        var rewrittenEvidence = await dbContext.ProfileEvidences.AsNoTracking().SingleAsync();
        Assert.Contains(canonicalSource.Id.ToString(), rewrittenEvidence.SourceDocumentIdsJson);
        Assert.DoesNotContain(duplicateSource.Id.ToString(), rewrittenEvidence.SourceDocumentIdsJson);
        var mergedArtifact = await dbContext.SavedResearchArtifacts.AsNoTracking().SingleAsync();
        Assert.Equal(canonical.Id, mergedArtifact.CompanyId);
        var rewrittenArtifact = mergedArtifact;
        Assert.Contains(canonicalSource.Id.ToString(), rewrittenArtifact.SourceDocumentIdsJson);
        Assert.DoesNotContain(duplicateSource.Id.ToString(), rewrittenArtifact.SourceDocumentIdsJson);
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

    private static Company NewCompany(string name) => new()
    {
        Name = name,
        Country = "Vietnam"
    };

    private static ResearchRun NewResearchRun(Company company) => new()
    {
        CompanyId = company.Id,
        Company = company,
        RequestedSearchProvider = "test-search",
        RequestedCrawlerProvider = "test-crawler"
    };

    private static SourceDocument NewSource(
        Company company,
        ResearchRun run,
        string url,
        string hash) => new()
    {
        CompanyId = company.Id,
        Company = company,
        ResearchRunId = run.Id,
        ResearchRun = run,
        Url = url,
        NormalizedUrl = url,
        SourceDomain = "merge.example",
        SourceKind = SourceKind.OfficialWebsite,
        RetrievedAt = DateTimeOffset.UtcNow,
        Content = $"Evidence for {url}",
        ContentHash = hash,
        CrawlerProvider = "test-crawler"
    };
}
