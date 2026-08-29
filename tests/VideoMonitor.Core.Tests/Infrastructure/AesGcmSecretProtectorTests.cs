using System.Security.Cryptography;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class AesGcmSecretProtectorTests
{
    [Fact]
    public async Task ProtectAsync_RoundTripsWithoutPlaintextLeak()
    {
        var provider = new StubMasterKeyProvider(
            Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        var protector = new AesGcmSecretProtector(provider);

        var cipher = await protector.ProtectAsync(
            "camera-secret",
            "camera-password:11111111111111111111111111111111");

        Assert.StartsWith("aesgcm:v1:", cipher, StringComparison.Ordinal);
        Assert.DoesNotContain("camera-secret", cipher, StringComparison.Ordinal);
        var parts = cipher.Split(':');
        Assert.Equal(5, parts.Length);
        Assert.Equal("aesgcm", parts[0]);
        Assert.Equal("v1", parts[1]);
        Assert.Equal(12, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(16, Convert.FromBase64String(parts[3]).Length);

        var plain = await protector.UnprotectAsync(
            cipher,
            "camera-password:11111111111111111111111111111111");

        Assert.Equal("camera-secret", plain);
    }

    [Fact]
    public async Task ProtectAsync_UsesFreshNonce()
    {
        var provider = new StubMasterKeyProvider(new byte[32]);
        var protector = new AesGcmSecretProtector(provider);

        var first = await protector.ProtectAsync("same", "purpose");
        var second = await protector.ProtectAsync("same", "purpose");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task UnprotectAsync_RejectsWrongPurpose()
    {
        var provider = new StubMasterKeyProvider(new byte[32]);
        var protector = new AesGcmSecretProtector(provider);
        var cipher = await protector.ProtectAsync("secret", "camera-password:a");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => protector.UnprotectAsync(cipher, "camera-password:b"));
    }

    [Fact]
    public async Task EmptyValueRoundTrips()
    {
        var protector = new AesGcmSecretProtector(
            new StubMasterKeyProvider(new byte[32]));

        var cipher = await protector.ProtectAsync(string.Empty, "purpose");
        var plain = await protector.UnprotectAsync(string.Empty, "purpose");

        Assert.Equal(string.Empty, cipher);
        Assert.Equal(string.Empty, plain);
    }

    [Fact]
    public async Task UnprotectAsync_RejectsUnsupportedVersion()
    {
        var protector = new AesGcmSecretProtector(
            new StubMasterKeyProvider(new byte[32]));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => protector.UnprotectAsync(
                "aesgcm:v2:AA==:AA==:AA==",
                "purpose"));
    }

    [Fact]
    public async Task UnprotectAsync_RejectsMalformedBase64()
    {
        var protector = new AesGcmSecretProtector(
            new StubMasterKeyProvider(new byte[32]));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => protector.UnprotectAsync(
                "aesgcm:v1:not-base64:AA==:AA==",
                "purpose"));
    }

    [Fact]
    public async Task UnprotectAsync_RejectsInvalidNonceSize()
    {
        var protector = new AesGcmSecretProtector(
            new StubMasterKeyProvider(new byte[32]));
        var nonce = Convert.ToBase64String(new byte[8]);
        var tag = Convert.ToBase64String(new byte[16]);
        var ciphertext = Convert.ToBase64String(new byte[1]);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => protector.UnprotectAsync(
                $"aesgcm:v1:{nonce}:{tag}:{ciphertext}",
                "purpose"));
    }

    [Fact]
    public async Task UnprotectAsync_RejectsInvalidTagSize()
    {
        var protector = new AesGcmSecretProtector(
            new StubMasterKeyProvider(new byte[32]));
        var nonce = Convert.ToBase64String(new byte[12]);
        var tag = Convert.ToBase64String(new byte[8]);
        var ciphertext = Convert.ToBase64String(new byte[1]);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => protector.UnprotectAsync(
                $"aesgcm:v1:{nonce}:{tag}:{ciphertext}",
                "purpose"));
    }

    [Fact]
    public async Task UnprotectAsync_RejectsTamperedCiphertext()
    {
        var protector = new AesGcmSecretProtector(
            new StubMasterKeyProvider(new byte[32]));
        string cipher = await protector.ProtectAsync("secret", "purpose");
        string[] parts = cipher.Split(':');
        var ciphertext = Convert.FromBase64String(parts[4]);
        ciphertext[0] ^= 0xFF;
        parts[4] = Convert.ToBase64String(ciphertext);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => protector.UnprotectAsync(string.Join(':', parts), "purpose"));
    }

    [Fact]
    public async Task UnprotectAsync_RejectsTamperedTag()
    {
        var protector = new AesGcmSecretProtector(
            new StubMasterKeyProvider(new byte[32]));
        string cipher = await protector.ProtectAsync("secret", "purpose");
        string[] parts = cipher.Split(':');
        var tag = Convert.FromBase64String(parts[3]);
        tag[0] ^= 0xFF;
        parts[3] = Convert.ToBase64String(tag);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => protector.UnprotectAsync(string.Join(':', parts), "purpose"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ProtectAsync_RejectsInvalidPurpose(string? purpose)
    {
        var protector = new AesGcmSecretProtector(
            new StubMasterKeyProvider(new byte[32]));

        await Assert.ThrowsAsync<ArgumentException>(
            () => protector.ProtectAsync("secret", purpose!));
    }

    [Fact]
    public async Task ProtectAsync_RejectsInvalidMasterKeyLength()
    {
        var protector = new AesGcmSecretProtector(
            new StubMasterKeyProvider(new byte[8]));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => protector.ProtectAsync("secret", "purpose"));
    }

    private sealed class StubMasterKeyProvider : IMasterKeyProvider
    {
        private readonly byte[] key;

        public StubMasterKeyProvider(byte[] key)
        {
            this.key = key.ToArray();
        }

        public Task<byte[]> GetOrCreateAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(key.ToArray());
    }
}
