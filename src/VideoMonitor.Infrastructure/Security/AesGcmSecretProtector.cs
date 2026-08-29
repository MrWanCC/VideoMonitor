using System.Security.Cryptography;
using System.Text;

namespace VideoMonitor.Infrastructure.Security;

public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const string Prefix = "aesgcm:v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private readonly IMasterKeyProvider masterKeyProvider;

    public AesGcmSecretProtector(IMasterKeyProvider masterKeyProvider)
    {
        this.masterKeyProvider = masterKeyProvider ??
            throw new ArgumentNullException(nameof(masterKeyProvider));
    }

    public async Task<string> ProtectAsync(
        string plaintext,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ValidatePurpose(purpose);

        if (plaintext.Length == 0)
        {
            return string.Empty;
        }

        var key = await masterKeyProvider.GetOrCreateAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            ValidateKeyForProtection(key);

            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var ciphertext = new byte[plaintextBytes.Length];
            var tag = new byte[TagSize];
            var associatedData = Encoding.UTF8.GetBytes(
                $"VideoMonitor|{purpose}");
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Encrypt(
                    nonce,
                    plaintextBytes,
                    ciphertext,
                    tag,
                    associatedData);

                return Prefix + string.Join(
                    ':',
                    Convert.ToBase64String(nonce),
                    Convert.ToBase64String(tag),
                    Convert.ToBase64String(ciphertext));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(plaintextBytes);
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(tag);
                CryptographicOperations.ZeroMemory(associatedData);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task<string> UnprotectAsync(
        string protectedValue,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(protectedValue);
        ValidatePurpose(purpose);

        if (protectedValue.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            var parts = protectedValue.Split(':', StringSplitOptions.None);
            if (parts.Length != 5 ||
                parts[0] != "aesgcm" ||
                parts[1] != "v1")
            {
                throw CreateDecryptionFailure();
            }

            var nonce = Convert.FromBase64String(parts[2]);
            var tag = Convert.FromBase64String(parts[3]);
            var ciphertext = Convert.FromBase64String(parts[4]);
            try
            {
                if (nonce.Length != NonceSize || tag.Length != TagSize)
                {
                    throw CreateDecryptionFailure();
                }

                var key = await masterKeyProvider
                    .GetOrCreateAsync(cancellationToken)
                    .ConfigureAwait(false);
                try
                {
                    if (key.Length != KeySize)
                    {
                        throw CreateDecryptionFailure();
                    }

                    var plaintextBytes = new byte[ciphertext.Length];
                    var associatedData = Encoding.UTF8.GetBytes(
                        $"VideoMonitor|{purpose}");
                    try
                    {
                        using var aes = new AesGcm(key, TagSize);
                        aes.Decrypt(
                            nonce,
                            ciphertext,
                            tag,
                            plaintextBytes,
                            associatedData);

                        return Encoding.UTF8.GetString(plaintextBytes);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(plaintextBytes);
                        CryptographicOperations.ZeroMemory(associatedData);
                    }
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(key);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(tag);
                CryptographicOperations.ZeroMemory(ciphertext);
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (FormatException exception)
        {
            throw CreateDecryptionFailure(exception);
        }
        catch (CryptographicException exception)
        {
            throw CreateDecryptionFailure(exception);
        }
        catch (ArgumentException exception)
        {
            throw CreateDecryptionFailure(exception);
        }
    }

    private static void ValidatePurpose(string purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            throw new ArgumentException(
                "Secret purpose 不能为空。",
                nameof(purpose));
        }
    }

    private static void ValidateKeyForProtection(byte[] key)
    {
        if (key.Length != KeySize)
        {
            throw new InvalidDataException("Master Key 长度无效。");
        }
    }

    private static InvalidDataException CreateDecryptionFailure(
        Exception? innerException = null) =>
        new("敏感数据解密失败。", innerException);
}
