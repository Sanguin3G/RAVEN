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

public sealed class BriefingGenerationJobConfiguration : IEntityTypeConfiguration<Raven.Api.Features.Research.Briefings.BriefingGenerationJob>
{
    public void Configure(EntityTypeBuilder<Raven.Api.Features.Research.Briefings.BriefingGenerationJob> entity)
    {
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Operation).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(item => item.RequestJson).HasMaxLength(100_000).IsRequired();
        entity.Property(item => item.Error).HasMaxLength(2_000);
        entity.HasIndex(item => new { item.CompanyId, item.CreatedAt });
        entity.HasIndex(item => new { item.Status, item.CreatedAt });
        entity.HasOne<Company>().WithMany().HasForeignKey(item => item.CompanyId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Raven.Api.Features.Research.Briefings.ResearchBriefing>().WithMany().HasForeignKey(item => item.BriefingId).OnDelete(DeleteBehavior.SetNull);
    }
}
