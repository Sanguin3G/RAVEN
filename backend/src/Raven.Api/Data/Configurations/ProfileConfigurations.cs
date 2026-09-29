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
