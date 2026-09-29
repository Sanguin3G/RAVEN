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

public sealed class ResearchContextAttachmentConfiguration : IEntityTypeConfiguration<ResearchContextAttachment>
{
    public void Configure(EntityTypeBuilder<ResearchContextAttachment> entity)
    {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.CompanyId).IsRequired();
            entity.Property(item => item.ConversationId).IsRequired();
            entity.HasIndex(item => new { item.CompanyId, item.ConversationId, item.InvestigationId })
                .IsUnique().HasFilter("\"InvestigationId\" IS NOT NULL");
            entity.HasIndex(item => new { item.CompanyId, item.ConversationId, item.SavedResearchArtifactId })
                .IsUnique().HasFilter("\"SavedResearchArtifactId\" IS NOT NULL");
            entity.HasIndex(item => new { item.CompanyId, item.ConversationId, item.BriefingId })
                .IsUnique().HasFilter("\"BriefingId\" IS NOT NULL");
            entity.ToTable(table => table.HasCheckConstraint(
                "CK_ResearchContextAttachments_ExactlyOneContext",
                "(\"InvestigationId\" IS NOT NULL AND \"SavedResearchArtifactId\" IS NULL AND \"BriefingId\" IS NULL AND \"BriefingVersionId\" IS NULL) OR (\"InvestigationId\" IS NULL AND \"SavedResearchArtifactId\" IS NOT NULL AND \"BriefingId\" IS NULL AND \"BriefingVersionId\" IS NULL) OR (\"InvestigationId\" IS NULL AND \"SavedResearchArtifactId\" IS NULL AND \"BriefingId\" IS NOT NULL AND \"BriefingVersionId\" IS NOT NULL)"));
            entity.HasOne<Company>().WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ManagedResearchInvestigation>().WithMany().HasForeignKey(item => item.InvestigationId)
                .IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SavedResearchArtifact>().WithMany().HasForeignKey(item => item.SavedResearchArtifactId)
                .IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Raven.Api.Features.Research.Briefings.ResearchBriefing>().WithMany().HasForeignKey(item => item.BriefingId)
                .IsRequired(false).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Raven.Api.Features.Research.Briefings.ResearchBriefingVersion>().WithMany().HasForeignKey(item => item.BriefingVersionId)
                .IsRequired(false).OnDelete(DeleteBehavior.Restrict);

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
