using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Raven.Api.Features.ProviderCredentials;

namespace Raven.Api.Data.Configurations;

public sealed class ProviderCredentialConfiguration : IEntityTypeConfiguration<ProviderCredentialEntity>
{
    public void Configure(EntityTypeBuilder<ProviderCredentialEntity> entity)
    {
        entity.HasKey(credential => credential.Provider);
        entity.Property(credential => credential.Provider).HasMaxLength(32).IsRequired();
        entity.Property(credential => credential.Ciphertext).IsRequired();
        entity.Property(credential => credential.Nonce).HasMaxLength(12).IsRequired();
        entity.Property(credential => credential.AuthenticationTag).HasMaxLength(16).IsRequired();
        entity.Property(credential => credential.Version).IsRequired();
        entity.Property(credential => credential.UpdatedAt).IsRequired();
    }
}
