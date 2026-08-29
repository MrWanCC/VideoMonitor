using System.Globalization;
using System.IO;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Wpf.Configuration;

internal static class DeviceCatalogRecoveryService
{
    private const int BufferSize = 4096;

    public static async Task<InMemoryDeviceCatalog> RecoverAsync(
        JsonDeviceCatalogStore formalStore,
        Func<DeviceCatalogSnapshot, InMemoryDeviceCatalog> catalogFactory,
        Exception formalLoadFailure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(formalStore);
        ArgumentNullException.ThrowIfNull(catalogFactory);
        ArgumentNullException.ThrowIfNull(formalLoadFailure);

        var formalPath = formalStore.FilePath;
        var backupPath = formalPath + ".bak";
        if (!File.Exists(backupPath))
        {
            if (formalLoadFailure is NotSupportedException)
            {
                throw new NotSupportedException(
                    "设备配置文件已损坏，且没有可用备份。",
                    formalLoadFailure);
            }

            throw new InvalidDataException(
                "设备配置文件已损坏，且没有可用备份。",
                formalLoadFailure);
        }

        try
        {
            var backupStore = new JsonDeviceCatalogStore(
                backupPath,
                formalStore.ProtectionScope);
            var backupSnapshot = await backupStore
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException("设备目录备份为空。");
            _ = catalogFactory(backupSnapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or NotSupportedException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException)
        {
            throw new InvalidDataException(
                "设备目录备份无效，无法恢复。",
                exception);
        }

        string corruptPath;
        try
        {
            corruptPath = await PreserveCorruptFileAsync(
                    formalPath,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "无法保留损坏的设备目录，未执行恢复。",
                exception);
        }

        try
        {
            await RestoreFormalFileAsync(
                    backupPath,
                    formalPath,
                    cancellationToken)
                .ConfigureAwait(false);

            var restoredSnapshot = await formalStore
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException(
                    "设备目录从备份恢复后无法重新加载。");
            return catalogFactory(restoredSnapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"设备目录从备份恢复失败，原损坏文件已保留为 {Path.GetFileName(corruptPath)}。",
                exception);
        }
    }

    private static async Task<string> PreserveCorruptFileAsync(
        string formalPath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(formalPath)
            ?? throw new InvalidOperationException("设备目录文件路径无效。");
        var fileName = Path.GetFileNameWithoutExtension(formalPath);
        var timestamp = DateTime.Now.ToString(
            "yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture);

        for (var suffix = 0; suffix < 10000; suffix++)
        {
            var suffixText = suffix == 0 ? string.Empty : $"-{suffix}";
            var candidate = Path.Combine(
                directory,
                $"{fileName}.corrupt-{timestamp}{suffixText}.json");
            var candidateCreated = false;
            try
            {
                await using var destination = new FileStream(
                    candidate,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
                candidateCreated = true;
                await using var source = new FileStream(
                    formalPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    BufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await source.CopyToAsync(destination, cancellationToken)
                    .ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
                destination.Flush(flushToDisk: true);
                return candidate;
            }
            catch (IOException) when (!candidateCreated && File.Exists(candidate))
            {
                continue;
            }
            catch
            {
                if (candidateCreated)
                {
                    TryDelete(candidate);
                }

                throw;
            }
        }

        throw new IOException("无法为损坏的设备目录创建唯一存档文件。");
    }

    private static async Task RestoreFormalFileAsync(
        string backupPath,
        string formalPath,
        CancellationToken cancellationToken)
    {
        var temporaryPath = formalPath + ".recovery.tmp";
        try
        {
            await using (var source = new FileStream(
                backupPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await source.CopyToAsync(destination, cancellationToken)
                    .ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
                destination.Flush(flushToDisk: true);
            }

            File.Replace(
                temporaryPath,
                formalPath,
                destinationBackupFileName: null,
                ignoreMetadataErrors: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Preserve the original recovery failure.
        }
    }
}
