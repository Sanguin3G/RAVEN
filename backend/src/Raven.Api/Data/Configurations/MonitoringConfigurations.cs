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
