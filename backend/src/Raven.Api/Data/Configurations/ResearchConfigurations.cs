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

public sealed class SourceDocumentConfiguration : IEntityTypeConfiguration<SourceDocument>
{
    public void Configure(EntityTypeBuilder<SourceDocument> entity)
    {
            entity.HasKey(sourceDocument => sourceDocument.Id);
            entity.Property(sourceDocument => sourceDocument.Url).HasMaxLength(2_048).IsRequired();
            entity.Property(sourceDocument => sourceDocument.NormalizedUrl).HasMaxLength(2_048).IsRequired();
            entity.Property(sourceDocument => sourceDocument.Title).HasMaxLength(500);
            entity.Property(sourceDocument => sourceDocument.SourceDomain).HasMaxLength(253);
            // The migration supplies a one-time default for older rows; all
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
