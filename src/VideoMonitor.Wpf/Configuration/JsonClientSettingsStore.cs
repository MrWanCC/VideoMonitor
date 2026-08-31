using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VideoMonitor.Wpf.Configuration;

public sealed class JsonClientSettingsStore : IClientSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string filePath;
    private readonly SemaphoreSlim saveGate = new(1, 1);

    public JsonClientSettingsStore(string? injectedRoot = null)
    {
        filePath = ClientSettingsPathProvider.GetPath(injectedRoot);
    }

    public ClientSettings Load()
    {
        if (!File.Exists(filePath))
        {
            return ClientSettings.Empty;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<ClientSettings>(
                File.ReadAllText(filePath));
            return settings is not null && settings.Server is not null
                ? settings
                : throw new InvalidDataException("Client settings file is invalid.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Client settings file is invalid.",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new InvalidDataException(
                "Client settings file is invalid.",
                exception);
        }
    }

    public async Task SaveAsync(
        ClientSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var directory = Path.GetDirectoryName(filePath)!;
        var temporaryPath = Path.Combine(directory, "client-settings.tmp");
        try
        {
            Directory.CreateDirectory(directory);

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        settings,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(filePath))
            {
                File.Replace(
                    temporaryPath,
                    filePath,
                    destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, filePath);
            }
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // Best-effort cleanup must not mask the original failure.
            }

            throw;
        }
        finally
        {
            saveGate.Release();
        }
    }
}
