using Microsoft.EntityFrameworkCore;
using Raven.Api.Features.Companies;

namespace Raven.Api.Data;

public sealed class RavenDbContext(DbContextOptions<RavenDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Company>(entity =>
        {
            entity.HasKey(company => company.Id);
            entity.Property(company => company.Name).HasMaxLength(300).IsRequired();
            entity.Property(company => company.Website).HasMaxLength(2_048);
            entity.HasIndex(company => company.Name);
        });
    }
}
