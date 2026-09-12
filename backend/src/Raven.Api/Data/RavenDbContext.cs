using Microsoft.EntityFrameworkCore;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Sources;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Settings;
using Raven.Api.Features.Profiles.Changes;
using Raven.Api.Features.Monitoring;
using Raven.Api.Features.DeepResearch;
using Raven.Api.Features.Research.SavedArtifacts;
using Raven.Api.Features.Research.Coverage;

namespace Raven.Api.Data;

public sealed class RavenDbContext(DbContextOptions<RavenDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<ResearchRun> ResearchRuns => Set<ResearchRun>();
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();
    public DbSet<ResearchCandidate> ResearchCandidates => Set<ResearchCandidate>();
    public DbSet<ResearchEvent> ResearchEvents => Set<ResearchEvent>();
    public DbSet<CompanyProfileVersion> CompanyProfileVersions => Set<CompanyProfileVersion>();
    public DbSet<CompanyProfileCandidate> CompanyProfileCandidates => Set<CompanyProfileCandidate>();
    public DbSet<ProfileEvidence> ProfileEvidences => Set<ProfileEvidence>();
    public DbSet<ResearchSettingsEntity> ResearchSettings => Set<ResearchSettingsEntity>();
    public DbSet<ProfileChange> ProfileChanges => Set<ProfileChange>();
    public DbSet<ResearchIdentityCandidate> ResearchIdentityCandidates => Set<ResearchIdentityCandidate>();
    public DbSet<CompanyMonitoringSetting> CompanyMonitoringSettings => Set<CompanyMonitoringSetting>();
    public DbSet<DeepResearchRun> DeepResearchRuns => Set<DeepResearchRun>();
    public DbSet<DeepResearchActivityRecord> DeepResearchActivities => Set<DeepResearchActivityRecord>();
    public DbSet<SavedResearchArtifact> SavedResearchArtifacts => Set<SavedResearchArtifact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(company => company.Id);
            entity.Property(company => company.Name).HasMaxLength(300).IsRequired();
            entity.Property(company => company.LegalName).HasMaxLength(500);
            entity.Property(company => company.RegistrationNumber).HasMaxLength(150);
            entity.Property(company => company.Website).HasMaxLength(2_048);
            entity.Property(company => company.Headquarters).HasMaxLength(1_000);
            entity.HasIndex(company => company.ArchivedAt);
            entity.HasIndex(company => company.Name);
        });

        modelBuilder.Entity<ResearchRun>(entity =>
        {
            entity.HasKey(researchRun => researchRun.Id);
            entity.Property(researchRun => researchRun.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(researchRun => researchRun.Stage).HasConversion<string>().HasMaxLength(48).IsRequired();
            entity.Property(researchRun => researchRun.GroundingMode).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(researchRun => researchRun.Mode).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(researchRun => researchRun.ResearchTargetsJson).HasMaxLength(2_000).IsRequired();
            entity.Property(researchRun => researchRun.RequestedSearchProvider).HasMaxLength(100).IsRequired();
            entity.Property(researchRun => researchRun.ActualSearchProvider).HasMaxLength(100);
            entity.Property(researchRun => researchRun.RequestedCrawlerProvider).HasMaxLength(100).IsRequired();
            entity.Property(researchRun => researchRun.ActualCrawlerProvider).HasMaxLength(100);
            entity.Property(researchRun => researchRun.ResearchHint).HasMaxLength(2_000);
            entity.Property(researchRun => researchRun.Error).HasMaxLength(4_000);
            entity.HasIndex(researchRun => new { researchRun.CompanyId, researchRun.StartedAt });
            entity.HasOne(researchRun => researchRun.Company)
                .WithMany(company => company.ResearchRuns)
                .HasForeignKey(researchRun => researchRun.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ResearchIdentityCandidate>(entity =>
        {
            entity.HasKey(candidate => candidate.Id);
            entity.Property(candidate => candidate.TemporaryId).HasMaxLength(200).IsRequired();
            entity.Property(candidate => candidate.DisplayName).HasMaxLength(500).IsRequired();
            entity.Property(candidate => candidate.LegalName).HasMaxLength(500);
            entity.Property(candidate => candidate.Country).HasMaxLength(200);
            entity.Property(candidate => candidate.Website).HasMaxLength(2_048);
            entity.Property(candidate => candidate.OfficialDomain).HasMaxLength(253);
            entity.Property(candidate => candidate.EntityType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(candidate => candidate.Confidence).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(candidate => candidate.RelationshipHint).HasMaxLength(500);
            entity.Property(candidate => candidate.Rationale).HasMaxLength(500);
            entity.Property(candidate => candidate.SupportingCandidateIdsJson).HasMaxLength(4_000).IsRequired();
            entity.HasIndex(candidate => new { candidate.ResearchRunId, candidate.TemporaryId }).IsUnique();
            entity.HasOne<ResearchRun>()
                .WithMany()
                .HasForeignKey(candidate => candidate.ResearchRunId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CompanyMonitoringSetting>(entity =>
        {
            entity.HasKey(setting => setting.CompanyId);
            entity.Property(setting => setting.Cadence).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(setting => setting.LastRunStatus).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(setting => new { setting.Enabled, setting.NextRunAt });
            entity.HasIndex(setting => setting.ClaimExpiresAt);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(setting => setting.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeepResearchRun>(entity =>
        {
            entity.HasKey(run => run.Id);
            entity.Property(run => run.Question).HasMaxLength(4_000).IsRequired();
            entity.Property(run => run.Model).HasMaxLength(200).IsRequired();
            entity.Property(run => run.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(run => run.ResultMarkdown).HasMaxLength(40_000);
            entity.Property(run => run.Error).HasMaxLength(4_000);
            entity.HasIndex(run => new { run.CompanyId, run.CreatedAt });
            entity.HasOne<Company>().WithMany().HasForeignKey(run => run.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeepResearchActivityRecord>(entity =>
        {
            entity.HasKey(activity => activity.Id);
            entity.Property(activity => activity.Type).HasMaxLength(32).IsRequired();
            entity.Property(activity => activity.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(activity => activity.Label).HasMaxLength(120).IsRequired();
            entity.Property(activity => activity.Detail).HasMaxLength(280);
            entity.Property(activity => activity.Provider).HasMaxLength(100);
            entity.Property(activity => activity.SourceDocumentIdsJson).HasMaxLength(1_000).IsRequired();
            entity.HasIndex(activity => new { activity.DeepResearchRunId, activity.Sequence }).IsUnique();
            entity.HasOne<DeepResearchRun>().WithMany().HasForeignKey(activity => activity.DeepResearchRunId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SavedResearchArtifact>(entity =>
        {
            entity.HasKey(artifact => artifact.Id);
            entity.Property(artifact => artifact.Title).HasMaxLength(500).IsRequired();
            entity.Property(artifact => artifact.Question).HasMaxLength(4_000).IsRequired();
            entity.Property(artifact => artifact.Summary).HasMaxLength(100_000).IsRequired();
            entity.Property(artifact => artifact.Model).HasMaxLength(200);
            entity.Property(artifact => artifact.ResearchType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(artifact => artifact.SourceDocumentIdsJson).HasMaxLength(20_000).IsRequired();
            entity.Ignore(artifact => artifact.SourceDocumentIds);
            entity.Ignore(artifact => artifact.Result);
            entity.HasIndex(artifact => new { artifact.CompanyId, artifact.CreatedAt });
            entity.HasOne<Company>().WithMany().HasForeignKey(artifact => artifact.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ResearchSettingsEntity>(entity =>
        {
            entity.HasKey(settings => settings.Id);
            entity.Property(settings => settings.Id).HasMaxLength(64);
            entity.Property(settings => settings.GroundingMode).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(settings => settings.ProfileModel).HasMaxLength(200).IsRequired();
            entity.Property(settings => settings.GroundingModel).HasMaxLength(200).IsRequired();
            entity.Property(settings => settings.DeepResearchModel).HasMaxLength(200).IsRequired();
            // The Day-5.5 migration rewrites the persisted legacy value, but
            // accepting it at the model boundary keeps an interrupted upgrade
            // readable instead of resetting a user's provider priorities.
            entity.Property(settings => settings.ProviderPreset)
                .HasConversion(
                    preset => preset.ToString(),
                    value => string.Equals(value, "Balanced", StringComparison.OrdinalIgnoreCase)
                        ? ProviderPreset.Resilient
                        : Enum.Parse<ProviderPreset>(value, ignoreCase: true))
                .HasMaxLength(32)
                .IsRequired();
            entity.PrimitiveCollection(settings => settings.SearchProviderPriority).HasMaxLength(100);
            entity.PrimitiveCollection(settings => settings.CrawlerProviderPriority).HasMaxLength(100);
        });

        modelBuilder.Entity<ProfileChange>(entity =>
        {
            entity.HasKey(change => change.Id);
            entity.Property(change => change.FieldPath).HasMaxLength(300).IsRequired();
            entity.Property(change => change.ItemKey).HasMaxLength(500);
            entity.Property(change => change.ChangeType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(change => change.OldValueJson).HasMaxLength(16_000);
            entity.Property(change => change.NewValueJson).HasMaxLength(16_000);
            entity.HasIndex(change => new { change.CompanyId, change.NewProfileVersionId });
            entity.HasIndex(change => new { change.CompanyId, change.DetectedAt });
        });

        modelBuilder.Entity<ResearchCandidate>(entity =>
        {
            entity.HasKey(candidate => candidate.Id);
            entity.Property(candidate => candidate.Url).HasMaxLength(2_048).IsRequired();
            entity.Property(candidate => candidate.NormalizedUrl).HasMaxLength(2_048).IsRequired();
            entity.Property(candidate => candidate.Domain).HasMaxLength(253).IsRequired();
            entity.Property(candidate => candidate.Title).HasMaxLength(500);
            entity.Property(candidate => candidate.Snippet).HasMaxLength(4_000);
            entity.Property(candidate => candidate.SourceKind).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(candidate => candidate.RecommendationReasonsJson).HasMaxLength(4_000);
            entity.Property(candidate => candidate.AcquisitionStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(candidate => candidate.AcquisitionError).HasMaxLength(2_000);
            entity.Property(candidate => candidate.IconUrl).HasMaxLength(2_048);
            entity.HasIndex(candidate => new { candidate.ResearchRunId, candidate.NormalizedUrl }).IsUnique();
            entity.HasOne(candidate => candidate.ResearchRun)
                .WithMany(run => run.Candidates)
                .HasForeignKey(candidate => candidate.ResearchRunId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ResearchEvent>(entity =>
        {
            entity.HasKey(researchEvent => researchEvent.Id);
            entity.Property(researchEvent => researchEvent.Stage).HasConversion<string>().HasMaxLength(48);
            entity.Property(researchEvent => researchEvent.Category).HasConversion<string>().HasMaxLength(48).IsRequired();
            entity.Property(researchEvent => researchEvent.Operation).HasMaxLength(100).HasDefaultValue(ResearchEvent.LegacyOperation).IsRequired();
            entity.Property(researchEvent => researchEvent.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(researchEvent => researchEvent.Provider).HasMaxLength(100);
            entity.Property(researchEvent => researchEvent.Model).HasMaxLength(200);
            entity.Property(researchEvent => researchEvent.ToolName).HasMaxLength(200);
            entity.Property(researchEvent => researchEvent.InputSummary).HasMaxLength(2_000);
            entity.Property(researchEvent => researchEvent.OutputSummary).HasMaxLength(2_000);
            entity.Property(researchEvent => researchEvent.ExternalRequestId).HasMaxLength(500);
            entity.Property(researchEvent => researchEvent.PromptTemplateVersion).HasMaxLength(100);
            entity.Property(researchEvent => researchEvent.InputHash).HasMaxLength(128);
            entity.Property(researchEvent => researchEvent.ErrorCode).HasMaxLength(100);
            entity.Property(researchEvent => researchEvent.ErrorMessage).HasMaxLength(2_000);
            entity.Property(researchEvent => researchEvent.MetadataJson).HasMaxLength(4_000);
            entity.HasIndex(researchEvent => new { researchEvent.ResearchRunId, researchEvent.Sequence });
        });

        modelBuilder.Entity<CompanyProfileVersion>(entity =>
        {
            entity.HasKey(profile => profile.Id);
            entity.Property(profile => profile.CompanyId).IsRequired();
            entity.Property(profile => profile.ResearchRunId).IsRequired();
            entity.Property(profile => profile.AiProvider).HasMaxLength(100);
            entity.Property(profile => profile.AiModel).HasMaxLength(200);
            entity.Property(profile => profile.PromptTemplateVersion).HasMaxLength(100);
            entity.Property(profile => profile.ProfileJson).IsRequired();
            entity.HasIndex(profile => new { profile.CompanyId, profile.Version }).IsUnique();
            entity.HasIndex(profile => new { profile.CompanyId, profile.ConfirmedAt });
            entity.Ignore(profile => profile.DisplayName);
            entity.Ignore(profile => profile.LegalName);
            entity.Ignore(profile => profile.Website);
            entity.Ignore(profile => profile.Country);
            entity.Ignore(profile => profile.Headquarters);
            entity.Ignore(profile => profile.RegistrationNumberOrTaxId);
            entity.Ignore(profile => profile.FoundedYear);
            entity.Ignore(profile => profile.PrimaryIndustry);
            entity.Ignore(profile => profile.SecondaryIndustries);
            entity.Ignore(profile => profile.CompanySize);
            entity.Ignore(profile => profile.EmployeeCount);
            entity.Ignore(profile => profile.EmployeeCountRange);
            entity.Ignore(profile => profile.Summary);
            entity.Ignore(profile => profile.ProductsServices);
            entity.Ignore(profile => profile.Markets);
            entity.Ignore(profile => profile.Leadership);
            entity.Ignore(profile => profile.Locations);
            entity.Ignore(profile => profile.PublicLinks);
            entity.Ignore(profile => profile.Evidence);
        });

        modelBuilder.Entity<CompanyProfileCandidate>(entity =>
        {
            entity.HasKey(profile => profile.Id);
            entity.Property(profile => profile.CompanyId).IsRequired();
            entity.Property(profile => profile.ResearchRunId).IsRequired();
            entity.Property(profile => profile.AiProvider).HasMaxLength(100);
            entity.Property(profile => profile.AiModel).HasMaxLength(200);
            entity.Property(profile => profile.PromptTemplateVersion).HasMaxLength(100);
            entity.Property(profile => profile.CandidateJson).IsRequired();
            entity.HasIndex(profile => new { profile.ResearchRunId, profile.GeneratedAt });
            entity.Ignore(profile => profile.DisplayName);
            entity.Ignore(profile => profile.LegalName);
            entity.Ignore(profile => profile.Website);
            entity.Ignore(profile => profile.Country);
            entity.Ignore(profile => profile.Headquarters);
            entity.Ignore(profile => profile.RegistrationNumberOrTaxId);
            entity.Ignore(profile => profile.FoundedYear);
            entity.Ignore(profile => profile.PrimaryIndustry);
            entity.Ignore(profile => profile.SecondaryIndustries);
            entity.Ignore(profile => profile.CompanySize);
            entity.Ignore(profile => profile.EmployeeCount);
            entity.Ignore(profile => profile.EmployeeCountRange);
            entity.Ignore(profile => profile.Summary);
            entity.Ignore(profile => profile.ProductsServices);
            entity.Ignore(profile => profile.Markets);
            entity.Ignore(profile => profile.Leadership);
            entity.Ignore(profile => profile.Locations);
            entity.Ignore(profile => profile.PublicLinks);
            entity.Ignore(profile => profile.Evidence);
            entity.Ignore(profile => profile.ValidationWarnings);
        });

        modelBuilder.Entity<ProfileEvidence>(entity =>
        {
            entity.HasKey(evidence => evidence.Id);
            entity.Property(evidence => evidence.FieldPath).HasMaxLength(300).IsRequired();
            entity.Property(evidence => evidence.SourceDocumentIdsJson).HasMaxLength(8_000).IsRequired();
            entity.HasIndex(evidence => new { evidence.CompanyProfileVersionId, evidence.FieldPath });
            entity.Ignore(evidence => evidence.SourceDocumentIds);
        });

        modelBuilder.Entity<SourceDocument>(entity =>
        {
            entity.HasKey(sourceDocument => sourceDocument.Id);
            entity.Property(sourceDocument => sourceDocument.Url).HasMaxLength(2_048).IsRequired();
            entity.Property(sourceDocument => sourceDocument.NormalizedUrl).HasMaxLength(2_048).IsRequired();
            entity.Property(sourceDocument => sourceDocument.Title).HasMaxLength(500);
            entity.Property(sourceDocument => sourceDocument.SourceDomain).HasMaxLength(253);
            // The migration supplies a one-time default for pre-Day-3 rows; all
            // new documents must persist their classifier-selected kind.
            entity.Property(sourceDocument => sourceDocument.SourceKind).HasConversion<string>().HasMaxLength(32).ValueGeneratedNever().IsRequired();
            entity.Property(sourceDocument => sourceDocument.IconUrl).HasMaxLength(2_048);
            entity.Property(sourceDocument => sourceDocument.StructuredFactsJson).HasMaxLength(16_000);
            entity.Property(sourceDocument => sourceDocument.Content).IsRequired();
            entity.Property(sourceDocument => sourceDocument.ContentHash).HasMaxLength(64).IsRequired();
            entity.Property(sourceDocument => sourceDocument.CrawlerProvider).HasMaxLength(100).IsRequired();
            entity.HasIndex(sourceDocument => new { sourceDocument.CompanyId, sourceDocument.NormalizedUrl });
            entity.HasIndex(sourceDocument => new { sourceDocument.CompanyId, sourceDocument.ContentHash });
            entity.HasOne(sourceDocument => sourceDocument.Company)
                .WithMany(company => company.SourceDocuments)
                .HasForeignKey(sourceDocument => sourceDocument.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(sourceDocument => sourceDocument.ResearchRun)
                .WithMany(researchRun => researchRun.SourceDocuments)
                .HasForeignKey(sourceDocument => sourceDocument.ResearchRunId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
