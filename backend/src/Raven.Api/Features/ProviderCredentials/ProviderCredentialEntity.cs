namespace Raven.Api.Features.ProviderCredentials;

/// <summary>Encrypted, workspace-scoped override for one supported provider.</summary>
public sealed class ProviderCredentialEntity
{
    public required string Provider { get; set; }

    public required byte[] Ciphertext { get; set; }

    public required byte[] Nonce { get; set; }

    public required byte[] AuthenticationTag { get; set; }

    public int Version { get; set; } = 1;

    public DateTimeOffset UpdatedAt { get; set; }
}
