using Microsoft.EntityFrameworkCore;
using Raven.Api.Features.Companies;
using Raven.Api.Features.Research;

namespace Raven.Api.Data;

public sealed class RavenDbContext(DbContextOptions<RavenDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<ResearchRun> ResearchRuns => Set<ResearchRun>();
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(company => company.Id);
            entity.Property(company => company.Name).HasMaxLength(300).IsRequired();
            entity.Property(company => company.Website).HasMaxLength(2_048);
            entity.HasIndex(company => company.Name);
        });

        modelBuilder.Entity<ResearchRun>(entity =>
        {
            entity.HasKey(researchRun => researchRun.Id);
            entity.Property(researchRun => researchRun.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(researchRun => researchRun.RequestedSearchProvider).HasMaxLength(100).IsRequired();
            entity.Property(researchRun => researchRun.ActualSearchProvider).HasMaxLength(100);
            entity.Property(researchRun => researchRun.RequestedCrawlerProvider).HasMaxLength(100).IsRequired();
            entity.Property(researchRun => researchRun.ActualCrawlerProvider).HasMaxLength(100);
            entity.Property(researchRun => researchRun.Error).HasMaxLength(4_000);
            entity.HasIndex(researchRun => new { researchRun.CompanyId, researchRun.StartedAt });
            entity.HasOne(researchRun => researchRun.Company)
                .WithMany(company => company.ResearchRuns)
                .HasForeignKey(researchRun => researchRun.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SourceDocument>(entity =>
        {
            entity.HasKey(sourceDocument => sourceDocument.Id);
            entity.Property(sourceDocument => sourceDocument.Url).HasMaxLength(2_048).IsRequired();
            entity.Property(sourceDocument => sourceDocument.NormalizedUrl).HasMaxLength(2_048).IsRequired();
            entity.Property(sourceDocument => sourceDocument.Title).HasMaxLength(500);
            entity.Property(sourceDocument => sourceDocument.SourceDomain).HasMaxLength(253);
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
