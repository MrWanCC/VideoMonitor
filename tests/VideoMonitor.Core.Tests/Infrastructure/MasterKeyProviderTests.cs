using System.Security.Cryptography;
using VideoMonitor.Infrastructure.Paths;
using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Core.Tests.Infrastructure;

public sealed class MasterKeyProviderTests
{
    [Fact]
    public async Task GetOrCreateAsync_CreatesProtected32ByteKeyAndReusesIt()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var paths = CreatePaths(root);
            new ServerStorageLayout(paths).EnsureCreated();
            var machineProtector = new XorMachineSecretProtector();

            var firstProvider = new MasterKeyProvider(paths, machineProtector);
            var first = await firstProvider.GetOrCreateAsync();

            Assert.Equal(32, first.Length);
            Assert.True(File.Exists(paths.MasterKeyPath));
            var protectedBytes = await File.ReadAllBytesAsync(paths.MasterKeyPath);
            Assert.False(first.SequenceEqual(protectedBytes));

            var secondProvider = new MasterKeyProvider(paths, machineProtector);
            var second = await secondProvider.GetOrCreateAsync();

            Assert.Equal(first, second);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetOrCreateAsync_RejectsInvalidExistingKey()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var paths = CreatePaths(root);
            new ServerStorageLayout(paths).EnsureCreated();
            var machineProtector = new XorMachineSecretProtector();
            var protectedInvalidKey = machineProtector.Protect(new byte[8]);
            await File.WriteAllBytesAsync(paths.MasterKeyPath, protectedInvalidKey);

            var provider = new MasterKeyProvider(paths, machineProtector);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => provider.GetOrCreateAsync());

            Assert.Equal(
                protectedInvalidKey,
                await File.ReadAllBytesAsync(paths.MasterKeyPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetOrCreateAsync_ReturnsClones()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var paths = CreatePaths(root);
            new ServerStorageLayout(paths).EnsureCreated();
            var provider = new MasterKeyProvider(
                paths,
                new XorMachineSecretProtector());

            var first = await provider.GetOrCreateAsync();
            var original = first.ToArray();
            first[0] ^= 0xFF;

            var second = await provider.GetOrCreateAsync();

            Assert.Equal(original, second);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void DpapiMachineSecretProtector_RoundTripsOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var protector = new DpapiMachineSecretProtector();
        var plaintext = RandomNumberGenerator.GetBytes(32);

        var protectedBytes = protector.Protect(plaintext);
        var roundTrip = protector.Unprotect(protectedBytes);

        Assert.False(plaintext.SequenceEqual(protectedBytes));
        Assert.True(plaintext.SequenceEqual(roundTrip));
    }

    [Fact]
    public void UnsupportedMachineSecretProtector_RejectsProtection()
    {
        var protector = new UnsupportedMachineSecretProtector();

        Assert.Throws<PlatformNotSupportedException>(
            () => protector.Protect(new byte[32]));
        Assert.Throws<PlatformNotSupportedException>(
            () => protector.Unprotect(new byte[32]));
    }

    private static IAppPathProvider CreatePaths(string root) =>
        new DefaultAppPathProvider(
            new ServerStorageOptions { RootPath = root });

    private sealed class XorMachineSecretProtector : IMachineSecretProtector
    {
        private const byte Mask = 0xA7;

        public byte[] Protect(byte[] plaintext) =>
            plaintext.Select(value => (byte)(value ^ Mask)).ToArray();

        public byte[] Unprotect(byte[] protectedData) =>
            protectedData.Select(value => (byte)(value ^ Mask)).ToArray();
    }
}
