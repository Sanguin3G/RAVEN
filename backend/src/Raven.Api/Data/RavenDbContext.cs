using Microsoft.EntityFrameworkCore;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Research;
using Raven.Api.Features.Research.Sources;
using Raven.Api.Features.Research.Events;
using Raven.Api.Features.Profiles;

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
            entity.HasIndex(company => company.Name);
        });

        modelBuilder.Entity<ResearchRun>(entity =>
        {
            entity.HasKey(researchRun => researchRun.Id);
            entity.Property(researchRun => researchRun.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(researchRun => researchRun.Stage).HasConversion<string>().HasMaxLength(48).IsRequired();
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
