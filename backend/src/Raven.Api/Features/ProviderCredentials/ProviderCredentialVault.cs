using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Raven.Api.Features.ProviderCredentials;

public sealed record EncryptedProviderCredential(byte[] Ciphertext, byte[] Nonce, byte[] AuthenticationTag);

public interface IProviderCredentialVault
{
    bool CanPersist { get; }

    EncryptedProviderCredential Encrypt(string provider, string plaintext);

    bool TryDecrypt(string provider, ProviderCredentialEntity credential, out string plaintext);
}

/// <summary>Encrypts provider overrides with a deployment-managed 256-bit key.</summary>
public sealed class ProviderCredentialVault : IProviderCredentialVault, IDisposable
{
    private const int KeyLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private readonly byte[]? masterKey;

    public ProviderCredentialVault(IConfiguration configuration)
    {
        var encodedKey = configuration["RAVEN_CREDENTIAL_MASTER_KEY"];
        if (string.IsNullOrWhiteSpace(encodedKey))
        {
            return;
        }

        try
        {
            var decoded = Convert.FromBase64String(encodedKey.Trim());
            if (decoded.Length == KeyLength)
            {
                masterKey = decoded;
            }
            else
            {
                CryptographicOperations.ZeroMemory(decoded);
            }
        }
        catch (FormatException)
        {
            // Invalid deployment configuration disables persistence. It never
            // permits a plaintext fallback or causes startup to store secrets.
        }
    }

    public bool CanPersist => masterKey is not null;

    public EncryptedProviderCredential Encrypt(string provider, string plaintext)
    {
        var key = masterKey ?? throw new ProviderCredentialStorageUnavailableException();
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var tag = new byte[TagLength];
        var value = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[value.Length];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(nonce, value, ciphertext, tag, Encoding.UTF8.GetBytes(provider));
            return new EncryptedProviderCredential(ciphertext, nonce, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(value);
        }
    }

    public bool TryDecrypt(string provider, ProviderCredentialEntity credential, out string plaintext)
    {
        plaintext = string.Empty;
        var key = masterKey;
        if (key is null || credential.Version != 1 || credential.Nonce.Length != NonceLength || credential.AuthenticationTag.Length != TagLength)
        {
            return false;
        }

        var decrypted = new byte[credential.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(credential.Nonce, credential.Ciphertext, credential.AuthenticationTag, decrypted, Encoding.UTF8.GetBytes(provider));
            plaintext = Encoding.UTF8.GetString(decrypted);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decrypted);
        }
    }

    public void Dispose()
    {
        if (masterKey is not null)
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }
}

public sealed class ProviderCredentialStorageUnavailableException()
    : Exception("Workspace credential storage is unavailable because RAVEN_CREDENTIAL_MASTER_KEY is not configured with a valid 32-byte Base64 key.");
