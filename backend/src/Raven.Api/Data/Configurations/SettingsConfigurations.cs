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
            // The compatibility migration rewrites the persisted legacy value, but
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
