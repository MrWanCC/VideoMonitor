using System.Security.Cryptography;
using VideoMonitor.Infrastructure.Paths;

namespace VideoMonitor.Infrastructure.Security;

public sealed class MasterKeyProvider
    : IMasterKeyProvider
{
    private const int MasterKeyLength = 32;

    private readonly IAppPathProvider paths;
    private readonly IMachineSecretProtector machineProtector;
    private readonly SemaphoreSlim gate = new(1, 1);
    private byte[]? cachedKey;

    public MasterKeyProvider(
        IAppPathProvider paths,
        IMachineSecretProtector machineProtector)
    {
        this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
        this.machineProtector = machineProtector ??
            throw new ArgumentNullException(nameof(machineProtector));
    }

    public async Task<byte[]> GetOrCreateAsync(
        CancellationToken cancellationToken = default)
    {
        var cached = Volatile.Read(ref cachedKey);
        if (cached is not null)
        {
            return cached.ToArray();
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = cachedKey;
            if (cached is not null)
            {
                return cached.ToArray();
            }

            var key = await LoadOrCreateKeyAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                cachedKey = key.ToArray();
                return cachedKey.ToArray();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<byte[]> LoadOrCreateKeyAsync(
        CancellationToken cancellationToken)
    {
        if (File.Exists(paths.MasterKeyPath))
        {
            return await LoadExistingKeyAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        var key = RandomNumberGenerator.GetBytes(MasterKeyLength);
        try
        {
            var protectedBytes = machineProtector.Protect(key);
            try
            {
                try
                {
                    await WriteProtectedKeyAtomicallyAsync(
                        protectedBytes,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (IOException) when (File.Exists(paths.MasterKeyPath))
                {
                    CryptographicOperations.ZeroMemory(key);
                    return await LoadExistingKeyAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }

            return key.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private async Task<byte[]> LoadExistingKeyAsync(
        CancellationToken cancellationToken)
    {
        var protectedBytes = await File.ReadAllBytesAsync(
            paths.MasterKeyPath,
            cancellationToken).ConfigureAwait(false);
        try
        {
            var key = machineProtector.Unprotect(protectedBytes);
            ValidateKey(key);
            return key;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }

    private async Task WriteProtectedKeyAtomicallyAsync(
        byte[] protectedBytes,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(paths.MasterKeyPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException(
                "Master Key 文件路径必须包含目录。");
        }

        var tempPath = $"{paths.MasterKeyPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(
                    protectedBytes.AsMemory(),
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, paths.MasterKeyPath);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Do not mask an exception from the write or move operation.
            }
        }
    }

    private static void ValidateKey(byte[] key)
    {
        if (key.Length != MasterKeyLength)
        {
            throw new InvalidDataException(
                "Master Key 文件解密后长度无效。");
        }
    }
}
