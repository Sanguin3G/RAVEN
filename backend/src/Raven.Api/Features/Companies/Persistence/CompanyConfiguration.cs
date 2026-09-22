using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Raven.Api.Features.Companies.Persistence;

public sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> entity)
    {
        entity.HasKey(company => company.Id);
        entity.Property(company => company.Name).HasMaxLength(300).IsRequired();
        entity.Property(company => company.LegalName).HasMaxLength(500);
        entity.Property(company => company.RegistrationNumber).HasMaxLength(150);
        entity.Property(company => company.Website).HasMaxLength(2_048);
        entity.Property(company => company.Headquarters).HasMaxLength(1_000);
        entity.HasIndex(company => company.ArchivedAt);
        entity.HasIndex(company => company.Name);
    }
}
