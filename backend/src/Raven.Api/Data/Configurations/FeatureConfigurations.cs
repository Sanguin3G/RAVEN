using Microsoft.EntityFrameworkCore;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Companies.Workspace;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Sources;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Research.Intelligence;
using Raven.Api.Features.Research.Identity;
using Raven.Api.Features.Profiles;
using Raven.Api.Features.Settings;
using Raven.Api.Features.Profiles.Changes;
using Raven.Api.Features.Monitoring;
using Raven.Api.Features.DeepResearch;
using Raven.Api.Features.Research.SavedArtifacts;
using Raven.Api.Features.Research.Coverage;
using Raven.Api.Features.Research.Organization;
using Raven.Api.Features.Research.ExternalImport;
using Raven.Api.Features.Chat;
using Raven.Api.Features.ManagedResearch;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raven.Api.Data.Configurations;

public sealed class ManagedResearchJobConfiguration : IEntityTypeConfiguration<ManagedResearchJob>
{
    public void Configure(EntityTypeBuilder<ManagedResearchJob> entity)
    {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Objective).HasMaxLength(4_000).IsRequired();
            entity.Property(item => item.ProviderQuery).HasMaxLength(12_000).IsRequired();
            entity.Property(item => item.Effort).HasMaxLength(32).IsRequired();
            entity.Property(item => item.Purpose).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(item => item.Provider).HasMaxLength(100);
            entity.Property(item => item.ProviderRunId).HasMaxLength(300);
            entity.Property(item => item.ProviderRunStatus).HasConversion<string>().HasMaxLength(32);
            entity.Property(item => item.ResultJson).HasMaxLength(200_000);
            entity.Property(item => item.Error).HasMaxLength(4_000);
            entity.HasIndex(item => new { item.CompanyId, item.CreatedAt });
            entity.HasIndex(item => new { item.Status, item.CreatedAt });
            entity.Property(item => item.AnswerInChat).HasDefaultValue(false);
            entity.HasOne<Company>().WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Restrict);
        
    }
}

public sealed class ManagedResearchInvestigationConfiguration : IEntityTypeConfiguration<ManagedResearchInvestigation>
{
    public void Configure(EntityTypeBuilder<ManagedResearchInvestigation> entity)
    {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Origin).HasMaxLength(32).IsRequired();
            entity.Property(item => item.Objective).HasMaxLength(4_000).IsRequired();
            entity.Property(item => item.Summary).HasMaxLength(100_000).IsRequired();
            entity.Property(item => item.ResultJson).HasMaxLength(200_000).IsRequired();
            entity.HasIndex(item => item.JobId).IsUnique();
            entity.HasIndex(item => new { item.CompanyId, item.CompletedAt });
            entity.HasOne<Company>().WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Restrict);
        
    }
}

public sealed class ExternalResearchAnalysisJobConfiguration : IEntityTypeConfiguration<ExternalResearchAnalysisJob>
{
    public void Configure(EntityTypeBuilder<ExternalResearchAnalysisJob> entity)
    {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Question).HasMaxLength(4_000).IsRequired();
            entity.Property(item => item.RawResponse).HasMaxLength(200_000).IsRequired();
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(item => item.ResultJson).HasMaxLength(200_000);
            entity.Property(item => item.Error).HasMaxLength(4_000);
            entity.HasIndex(item => new { item.CompanyId, item.CreatedAt });
            entity.HasIndex(item => new { item.Status, item.CreatedAt });
            entity.HasOne<Company>().WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Restrict);
        
    }
}

public sealed class WorkspaceResearchReviewStateConfiguration : IEntityTypeConfiguration<WorkspaceResearchReviewState>
{
    public void Configure(EntityTypeBuilder<WorkspaceResearchReviewState> entity)
    {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ReviewKey).HasMaxLength(600).IsRequired();
            entity.Property(item => item.Method).HasMaxLength(64).IsRequired();
            entity.Property(item => item.TopicKey).HasMaxLength(500).IsRequired();
            entity.HasIndex(item => item.ReviewKey).IsUnique();
            entity.HasIndex(item => new { item.CompanyId, item.AcknowledgedThrough });
            entity.HasOne<Company>().WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Cascade);
        
    }
}

public sealed class ResearchRunConfiguration : IEntityTypeConfiguration<ResearchRun>
{
    public void Configure(EntityTypeBuilder<ResearchRun> entity)
    {
            entity.HasKey(researchRun => researchRun.Id);
            entity.Property(researchRun => researchRun.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(researchRun => researchRun.Stage).HasConversion<string>().HasMaxLength(48).IsRequired();
            entity.Property(researchRun => researchRun.GroundingMode).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(researchRun => researchRun.Mode).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(researchRun => researchRun.SourceInvestigationKind).HasConversion<string>().HasMaxLength(16);
            entity.Property(researchRun => researchRun.ResearchTargetsJson).HasMaxLength(2_000).IsRequired();
            entity.Property(researchRun => researchRun.RequestedSearchProvider).HasMaxLength(100).IsRequired();
            entity.Property(researchRun => researchRun.ActualSearchProvider).HasMaxLength(100);
            entity.Property(researchRun => researchRun.RequestedCrawlerProvider).HasMaxLength(100).IsRequired();
            entity.Property(researchRun => researchRun.ActualCrawlerProvider).HasMaxLength(100);
            entity.Property(researchRun => researchRun.ResearchHint).HasMaxLength(2_000);
            entity.Property(researchRun => researchRun.ResolvedIdentitySnapshotJson)
                .HasMaxLength(ResolvedIdentitySnapshot.MaximumSerializedLength);
            entity.Property(researchRun => researchRun.Error).HasMaxLength(4_000);
            entity.HasIndex(researchRun => new { researchRun.CompanyId, researchRun.StartedAt });
            entity.HasOne(researchRun => researchRun.Company)
                .WithMany(company => company.ResearchRuns)
                .HasForeignKey(researchRun => researchRun.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        
    }
}

public sealed class ResearchContextAttachmentConfiguration : IEntityTypeConfiguration<ResearchContextAttachment>
{
    public void Configure(EntityTypeBuilder<ResearchContextAttachment> entity)
    {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.CompanyId).IsRequired();
            entity.Property(item => item.ConversationId).IsRequired();
            entity.Property(item => item.InvestigationId).IsRequired();
            entity.HasIndex(item => new { item.CompanyId, item.ConversationId, item.InvestigationId }).IsUnique();
            entity.HasOne<Company>().WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ManagedResearchInvestigation>().WithMany().HasForeignKey(item => item.InvestigationId).OnDelete(DeleteBehavior.Restrict);
        
    }
}

public sealed class ResearchIdentityCandidateConfiguration : IEntityTypeConfiguration<ResearchIdentityCandidate>
{
    public void Configure(EntityTypeBuilder<ResearchIdentityCandidate> entity)
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
        
    }
}

public sealed class CompanyMonitoringSettingConfiguration : IEntityTypeConfiguration<CompanyMonitoringSetting>
{
    public void Configure(EntityTypeBuilder<CompanyMonitoringSetting> entity)
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
        
    }
}

public sealed class DeepResearchRunConfiguration : IEntityTypeConfiguration<DeepResearchRun>
{
    public void Configure(EntityTypeBuilder<DeepResearchRun> entity)
    {
            entity.HasKey(run => run.Id);
            entity.Property(run => run.Question).HasMaxLength(4_000).IsRequired();
            entity.Property(run => run.Model).HasMaxLength(200).IsRequired();
            entity.Property(run => run.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(run => run.ResultMarkdown).HasMaxLength(40_000);
            entity.Property(run => run.Error).HasMaxLength(4_000);
            entity.HasIndex(run => new { run.CompanyId, run.CreatedAt });
            entity.HasOne<Company>().WithMany().HasForeignKey(run => run.CompanyId).OnDelete(DeleteBehavior.Restrict);
        
    }
}

public sealed class DeepResearchActivityRecordConfiguration : IEntityTypeConfiguration<DeepResearchActivityRecord>
{
    public void Configure(EntityTypeBuilder<DeepResearchActivityRecord> entity)
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
        
    }
}

public sealed class SavedResearchArtifactConfiguration : IEntityTypeConfiguration<SavedResearchArtifact>
{
    public void Configure(EntityTypeBuilder<SavedResearchArtifact> entity)
    {
            entity.HasKey(artifact => artifact.Id);
            entity.Property(artifact => artifact.Title).HasMaxLength(500).IsRequired();
            entity.Property(artifact => artifact.Question).HasMaxLength(4_000).IsRequired();
            entity.Property(artifact => artifact.Summary).HasMaxLength(100_000).IsRequired();
            entity.Property(artifact => artifact.RawResponse).HasMaxLength(200_000);
            entity.Property(artifact => artifact.Model).HasMaxLength(200);
            entity.Property(artifact => artifact.Origin).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(artifact => artifact.Purpose).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(artifact => artifact.TopicsJson).HasMaxLength(4_000).IsRequired();
            entity.Property(artifact => artifact.Provider).HasMaxLength(200);
            entity.Property(artifact => artifact.Objective).HasMaxLength(4_000);
            entity.Property(artifact => artifact.ManagedResearchJobId).HasMaxLength(200);
            entity.Property(artifact => artifact.ProviderMetadataJson).HasMaxLength(120_000).IsRequired();
            entity.Property(artifact => artifact.SourceLeadsJson).HasMaxLength(200_000).IsRequired();
            entity.Property(artifact => artifact.ClaimsJson).HasMaxLength(400_000).IsRequired();
            entity.Property(artifact => artifact.UncertaintiesJson).HasMaxLength(200_000).IsRequired();
            entity.Property(artifact => artifact.ResearchType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(artifact => artifact.SourceDocumentIdsJson).HasMaxLength(20_000).IsRequired();
            entity.Ignore(artifact => artifact.SourceDocumentIds);
            entity.Ignore(artifact => artifact.Result);
            entity.HasIndex(artifact => new { artifact.CompanyId, artifact.CreatedAt });
            entity.HasOne<Company>().WithMany().HasForeignKey(artifact => artifact.CompanyId).OnDelete(DeleteBehavior.Restrict);
        
    }
}

public sealed class InvestigationReviewStateConfiguration : IEntityTypeConfiguration<InvestigationReviewState>
{
    public void Configure(EntityTypeBuilder<InvestigationReviewState> entity)
    {
        entity.HasKey(item => item.Id);
        entity.Property(item => item.MaterialKind).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.HasIndex(item => new { item.CompanyId, item.MaterialKind, item.MaterialId }).IsUnique();
        entity.HasOne<Company>().WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<CompanyProfileVersion>().WithMany().HasForeignKey(item => item.AppliedProfileVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ResearchBriefingConfiguration : IEntityTypeConfiguration<Raven.Api.Features.Research.Briefings.ResearchBriefing>
{
    public void Configure(EntityTypeBuilder<Raven.Api.Features.Research.Briefings.ResearchBriefing> entity)
    {
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Title).HasMaxLength(200).IsRequired();
        entity.Property(item => item.Template).HasMaxLength(80).IsRequired();
        entity.Property(item => item.Objective).HasMaxLength(2_000).IsRequired();
        entity.HasIndex(item => new { item.CompanyId, item.UpdatedAt });
        entity.HasOne<Company>().WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ResearchBriefingVersionConfiguration : IEntityTypeConfiguration<Raven.Api.Features.Research.Briefings.ResearchBriefingVersion>
{
    public void Configure(EntityTypeBuilder<Raven.Api.Features.Research.Briefings.ResearchBriefingVersion> entity)
    {
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Title).HasMaxLength(200).IsRequired();
        entity.Property(item => item.Template).HasMaxLength(80).IsRequired();
        entity.Property(item => item.Objective).HasMaxLength(2_000).IsRequired();
        entity.Property(item => item.SectionsJson).HasMaxLength(200_000).IsRequired();
        entity.Property(item => item.SourcesJson).HasMaxLength(600_000).IsRequired();
        entity.HasIndex(item => new { item.BriefingId, item.VersionNumber }).IsUnique();
        entity.HasOne<Raven.Api.Features.Research.Briefings.ResearchBriefing>().WithMany()
            .HasForeignKey(item => item.BriefingId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InvestigationOrganizationRevisionConfiguration : IEntityTypeConfiguration<InvestigationOrganizationRevision>
{
    public void Configure(EntityTypeBuilder<InvestigationOrganizationRevision> entity)
    {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ExecutiveSummary).HasMaxLength(100_000).IsRequired();
            entity.Property(item => item.ThemesJson).HasMaxLength(200_000).IsRequired();
            entity.Property(item => item.EvidenceGapsJson).HasMaxLength(200_000).IsRequired();
            entity.Property(item => item.SuggestedFollowUpsJson).HasMaxLength(200_000).IsRequired();
            entity.Property(item => item.UncertaintiesJson).HasMaxLength(200_000).IsRequired();
            entity.HasIndex(item => new { item.SavedResearchArtifactId, item.Version }).IsUnique();
            entity.HasOne<SavedResearchArtifact>()
                .WithMany()
                .HasForeignKey(item => item.SavedResearchArtifactId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Company>()
                .WithMany()
                .HasForeignKey(item => item.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        
    }
}

public sealed class ResearchSettingsEntityConfiguration : IEntityTypeConfiguration<ResearchSettingsEntity>
{
    public void Configure(EntityTypeBuilder<ResearchSettingsEntity> entity)
    {
            entity.HasKey(settings => settings.Id);
            entity.Property(settings => settings.Id).HasMaxLength(64);
            entity.Property(settings => settings.GroundingMode).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(settings => settings.ProfileModel).HasMaxLength(200).IsRequired();
            entity.Property(settings => settings.GroundingModel).HasMaxLength(200).IsRequired();
            entity.Property(settings => settings.DeepResearchModel).HasMaxLength(200).IsRequired();
            entity.Property(settings => settings.ChatModel).HasMaxLength(200).IsRequired();
            entity.Property(settings => settings.ManagedResearchProvider).HasMaxLength(64).IsRequired();
            entity.Property(settings => settings.ManagedResearchDepth).HasConversion<string>().HasMaxLength(32).IsRequired();
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
            entity.PrimitiveCollection(settings => settings.CustomSearchProviderPriority).HasMaxLength(100);
            entity.PrimitiveCollection(settings => settings.CustomCrawlerProviderPriority).HasMaxLength(100);
        
    }
}

public sealed class ProfileChangeConfiguration : IEntityTypeConfiguration<ProfileChange>
{
    public void Configure(EntityTypeBuilder<ProfileChange> entity)
    {
            entity.HasKey(change => change.Id);
            entity.Property(change => change.FieldPath).HasMaxLength(300).IsRequired();
            entity.Property(change => change.ItemKey).HasMaxLength(500);
            entity.Property(change => change.ChangeType).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(change => change.OldValueJson).HasMaxLength(16_000);
            entity.Property(change => change.NewValueJson).HasMaxLength(16_000);
            entity.HasIndex(change => new { change.CompanyId, change.NewProfileVersionId });
            entity.HasIndex(change => new { change.CompanyId, change.DetectedAt });
        
    }
}

public sealed class ResearchCandidateConfiguration : IEntityTypeConfiguration<ResearchCandidate>
{
    public void Configure(EntityTypeBuilder<ResearchCandidate> entity)
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
        
    }
}

public sealed class ResearchEventConfiguration : IEntityTypeConfiguration<ResearchEvent>
{
    public void Configure(EntityTypeBuilder<ResearchEvent> entity)
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
        
    }
}

public sealed class CompanyProfileVersionConfiguration : IEntityTypeConfiguration<CompanyProfileVersion>
{
    public void Configure(EntityTypeBuilder<CompanyProfileVersion> entity)
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
        
    }
}

public sealed class CompanyProfileCandidateConfiguration : IEntityTypeConfiguration<CompanyProfileCandidate>
{
    public void Configure(EntityTypeBuilder<CompanyProfileCandidate> entity)
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
        
    }
}

public sealed class ProfileEvidenceConfiguration : IEntityTypeConfiguration<ProfileEvidence>
{
    public void Configure(EntityTypeBuilder<ProfileEvidence> entity)
    {
            entity.HasKey(evidence => evidence.Id);
            entity.Property(evidence => evidence.FieldPath).HasMaxLength(300).IsRequired();
            entity.Property(evidence => evidence.SourceDocumentIdsJson).HasMaxLength(8_000).IsRequired();
            entity.HasIndex(evidence => new { evidence.CompanyProfileVersionId, evidence.FieldPath });
            entity.Ignore(evidence => evidence.SourceDocumentIds);
        
    }
}

public sealed class SourceDocumentConfiguration : IEntityTypeConfiguration<SourceDocument>
{
    public void Configure(EntityTypeBuilder<SourceDocument> entity)
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
        
    }
}

public sealed class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
{
    public void Configure(EntityTypeBuilder<ChatConversation> entity)
    {
            entity.HasKey(conversation => conversation.Id);
            entity.Property(conversation => conversation.Title).HasMaxLength(200);
            entity.Property(conversation => conversation.WebSearchEnabled).HasDefaultValue(false);
            entity.HasIndex(conversation => new { conversation.CompanyId, conversation.UpdatedAt });
            entity.HasOne(conversation => conversation.Company)
                .WithMany()
                .HasForeignKey(conversation => conversation.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(conversation => conversation.ProfileVersion)
                .WithMany()
                .HasForeignKey(conversation => conversation.ProfileVersionId)
                .OnDelete(DeleteBehavior.Restrict);
        
    }
}

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> entity)
    {
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Role).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(message => message.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(message => message.WebLookupIncomplete).HasColumnName("WebLookupIncomplete");
            entity.Property(message => message.AnswerStatus).HasConversion<string>().HasMaxLength(32);
            entity.Property(message => message.Content).HasMaxLength(20_000).IsRequired();
            entity.Property(message => message.FollowUpQuestion).HasMaxLength(1_000);
            entity.Property(message => message.AiProvider).HasMaxLength(100);
            entity.Property(message => message.AiModel).HasMaxLength(200);
            entity.Property(message => message.Activity).HasMaxLength(100);
            entity.HasIndex(message => new { message.ConversationId, message.CreatedAt });
            entity.HasIndex(message => message.ManagedResearchJobId).IsUnique();
            entity.HasOne(message => message.Conversation)
                .WithMany(conversation => conversation.Messages)
                .HasForeignKey(message => message.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        
    }
}

public sealed class ChatWebEvidenceSnapshotConfiguration : IEntityTypeConfiguration<ChatWebEvidenceSnapshot>
{
    public void Configure(EntityTypeBuilder<ChatWebEvidenceSnapshot> entity)
    {
            entity.HasKey(snapshot => snapshot.Id);
            entity.Property(snapshot => snapshot.Url).HasMaxLength(2_000).IsRequired();
            entity.Property(snapshot => snapshot.NormalizedUrl).HasMaxLength(2_000).IsRequired();
            entity.Property(snapshot => snapshot.Title).HasMaxLength(500);
            entity.Property(snapshot => snapshot.SearchSnippet).HasMaxLength(2_000);
            entity.Property(snapshot => snapshot.ContentExcerpt).HasMaxLength(8_000).IsRequired();
            entity.Property(snapshot => snapshot.SearchProvider).HasMaxLength(100).IsRequired();
            entity.Property(snapshot => snapshot.CrawlerProvider).HasMaxLength(100);
            entity.HasIndex(snapshot => new { snapshot.ChatMessageId, snapshot.NormalizedUrl }).IsUnique();
            entity.HasOne(snapshot => snapshot.ChatMessage)
                .WithMany(message => message.WebEvidenceSnapshots)
                .HasForeignKey(snapshot => snapshot.ChatMessageId)
                .OnDelete(DeleteBehavior.Cascade);
        
    }
}

public sealed class ChatCitationConfiguration : IEntityTypeConfiguration<ChatCitation>
{
    public void Configure(EntityTypeBuilder<ChatCitation> entity)
    {
            entity.HasKey(citation => citation.Id);
            entity.Property(citation => citation.Origin).HasConversion<string>().HasMaxLength(16).IsRequired();
            entity.Property(citation => citation.FieldPath).HasMaxLength(300);
            entity.Property(citation => citation.Excerpt).HasMaxLength(1_000);
            entity.HasIndex(citation => new { citation.ChatMessageId, citation.SourceDocumentId }).IsUnique();
            entity.HasIndex(citation => new { citation.ChatMessageId, citation.WebEvidenceSnapshotId }).IsUnique();
            entity.HasIndex(citation => new { citation.ChatMessageId, citation.InvestigationId }).IsUnique();
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_ChatCitations_ExactlyOneEvidence",
                "(\"SourceDocumentId\" IS NOT NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NOT NULL AND \"InvestigationId\" IS NULL) OR (\"SourceDocumentId\" IS NULL AND \"WebEvidenceSnapshotId\" IS NULL AND \"InvestigationId\" IS NOT NULL)"));
            entity.HasOne(citation => citation.ChatMessage)
                .WithMany(message => message.Citations)
                .HasForeignKey(citation => citation.ChatMessageId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(citation => citation.SourceDocument)
                .WithMany()
                .HasForeignKey(citation => citation.SourceDocumentId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(citation => citation.WebEvidenceSnapshot)
                .WithMany()
                .HasForeignKey(citation => citation.WebEvidenceSnapshotId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(citation => citation.Investigation)
                .WithMany()
                .HasForeignKey(citation => citation.InvestigationId)
                .OnDelete(DeleteBehavior.Restrict);
        
    }
}

public sealed class ChatToolExecutionConfiguration : IEntityTypeConfiguration<ChatToolExecution>
{
    public void Configure(EntityTypeBuilder<ChatToolExecution> entity)
    {
            entity.HasKey(execution => execution.Id);
            entity.Property(execution => execution.Tool).HasMaxLength(200).IsRequired();
            entity.Property(execution => execution.Provider).HasMaxLength(100).IsRequired();
            entity.Property(execution => execution.Status).HasMaxLength(32).IsRequired();
            entity.Property(execution => execution.InputSummary).HasMaxLength(2_000);
            entity.Property(execution => execution.OutputSummary).HasMaxLength(2_000);
            entity.Property(execution => execution.ErrorCode).HasMaxLength(100);
            entity.HasIndex(execution => new { execution.ChatMessageId, execution.CreatedAt });
            entity.HasOne(execution => execution.ChatMessage)
                .WithMany(message => message.ToolExecutions)
                .HasForeignKey(execution => execution.ChatMessageId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(execution => execution.ResearchRun)
                .WithMany()
                .HasForeignKey(execution => execution.ResearchRunId)
                .OnDelete(DeleteBehavior.Restrict);
        
    }
}

