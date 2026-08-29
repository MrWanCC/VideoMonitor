using System.Text.Encodings.Web;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;

namespace VideoMonitor.Infrastructure.Persistence;

public sealed class JsonDeviceCatalogStore : IDeviceCatalogStore
{
    private const int BufferSize = 4096;
    private const string ProtectedPasswordPrefix = "dpapi:v1:";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly string filePath;
    private readonly DataProtectionScope protectionScope;
    private readonly SemaphoreSlim saveGate = new(1, 1);

    public JsonDeviceCatalogStore()
        : this(DefaultFilePath, DataProtectionScope.LocalMachine)
    {
    }

    public JsonDeviceCatalogStore(string filePath)
        : this(filePath, DataProtectionScope.LocalMachine)
    {
    }

    public JsonDeviceCatalogStore(
        string filePath,
        DataProtectionScope protectionScope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        this.filePath = Path.GetFullPath(filePath);
        this.protectionScope = protectionScope;
    }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "VideoMonitor",
        "data",
        "device-catalog.json");

    public async Task<DeviceCatalogSnapshot?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var persistedSnapshot = await JsonSerializer.DeserializeAsync<PersistedDeviceCatalogSnapshot>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            if (persistedSnapshot is null)
            {
                throw new InvalidDataException("设备目录文件为空。");
            }

            ValidateSchemaVersion(persistedSnapshot.SchemaVersion);
            if (persistedSnapshot.Groups is null || persistedSnapshot.Devices is null)
            {
                throw new InvalidDataException("设备目录 Groups 或 Devices 不能为空。");
            }

            return FromPersistedSnapshot(persistedSnapshot);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("设备目录 JSON 格式无效。", exception);
        }
    }

    public async Task SaveAsync(
        DeviceCatalogSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);
        cancellationToken.ThrowIfCancellationRequested();

        await saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var persistedSnapshot = ToPersistedSnapshot(snapshot);
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("设备目录文件路径无效。");
            }

            // The installer must create %ProgramData%\VideoMonitor and grant
            // this application directory only the required Modify access.
            Directory.CreateDirectory(directory);
            var temporaryPath = filePath + ".tmp";
            Exception? saveException = null;
            try
            {
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    BufferSize,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        persistedSnapshot,
                        SerializerOptions,
                        cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(filePath))
                {
                    File.Replace(
                        temporaryPath,
                        filePath,
                        filePath + ".bak",
                        ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporaryPath, filePath);
                }
            }
            catch (Exception exception)
            {
                saveException = exception;
                throw;
            }
            finally
            {
                TryDeleteTemporaryFile(temporaryPath, saveException is not null);
            }
        }
        finally
        {
            saveGate.Release();
        }
    }

    private static void TryDeleteTemporaryFile(
        string temporaryPath,
        bool preserveOriginalException)
    {
        if (!File.Exists(temporaryPath))
        {
            return;
        }

        try
        {
            File.Delete(temporaryPath);
        }
        catch (IOException) when (preserveOriginalException)
        {
        }
        catch (UnauthorizedAccessException) when (preserveOriginalException)
        {
        }
    }

    private static void ValidateSnapshot(DeviceCatalogSnapshot snapshot)
    {
        ValidateSchemaVersion(snapshot.SchemaVersion);

        if (snapshot.Groups is null)
        {
            throw new InvalidDataException("设备目录 Groups 不能为空。");
        }

        if (snapshot.Devices is null)
        {
            throw new InvalidDataException("设备目录 Devices 不能为空。");
        }
    }

    private static void ValidateSchemaVersion(int schemaVersion)
    {
        if (schemaVersion != DeviceCatalogSnapshot.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"不支持的设备目录 SchemaVersion：{schemaVersion}，当前仅支持版本 {DeviceCatalogSnapshot.CurrentSchemaVersion}。");
        }
    }

    private PersistedDeviceCatalogSnapshot ToPersistedSnapshot(
        DeviceCatalogSnapshot snapshot) => new()
        {
            SchemaVersion = snapshot.SchemaVersion,
            Groups = snapshot.Groups
                .Select(CloneGroup)
                .ToArray(),
            Devices = snapshot.Devices
                .Select(ToPersistedDevice)
                .ToArray()
        };

    private DeviceCatalogSnapshot FromPersistedSnapshot(
        PersistedDeviceCatalogSnapshot persistedSnapshot) => new()
        {
            SchemaVersion = persistedSnapshot.SchemaVersion,
            Groups = persistedSnapshot.Groups!
                .Select(CloneGroup)
                .ToArray(),
            Devices = persistedSnapshot.Devices!
                .Select(FromPersistedDevice)
                .ToArray()
        };

    private PersistedCameraDevice ToPersistedDevice(CameraDevice device)
    {
        var persistedDevice = new PersistedCameraDevice
        {
            Id = device.Id,
            Name = device.Name,
            GroupId = device.GroupId,
            IpAddress = device.IpAddress,
            SdkPort = device.SdkPort,
            RtspPort = device.RtspPort,
            Username = device.Username,
            Password = ProtectPassword(device.Password),
            Manufacturer = device.Manufacturer,
            Model = device.Model,
            TransportMode = device.TransportMode,
            Enabled = device.Enabled,
            Remark = device.Remark
        };
        persistedDevice.Channels = device.Channels
            .Select(ToPersistedChannel)
            .ToList();
        return persistedDevice;
    }

    private CameraDevice FromPersistedDevice(PersistedCameraDevice device)
    {
        var result = new CameraDevice
        {
            Id = device.Id,
            Name = device.Name,
            GroupId = device.GroupId,
            IpAddress = device.IpAddress,
            SdkPort = device.SdkPort,
            RtspPort = device.RtspPort,
            Username = device.Username,
            Password = UnprotectPassword(device.Password),
            Manufacturer = device.Manufacturer,
            Model = device.Model,
            TransportMode = device.TransportMode,
            Status = CameraStatus.Unknown,
            Enabled = device.Enabled,
            Remark = device.Remark
        };
        foreach (var channel in device.Channels ?? [])
        {
            result.Channels.Add(FromPersistedChannel(channel));
        }

        return result;
    }

    private string ProtectPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return string.Empty;
        }

        try
        {
            var protectedBytes = ProtectedData.Protect(
                StrictUtf8.GetBytes(password),
                optionalEntropy: null,
                protectionScope);
            return ProtectedPasswordPrefix + Convert.ToBase64String(protectedBytes);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("设备密码保护失败。", exception);
        }
    }

    private string UnprotectPassword(string? value)
    {
        if (value is null)
        {
            throw new InvalidDataException("设备密码字段缺失。");
        }

        if (value.Length == 0)
        {
            return string.Empty;
        }

        if (!value.StartsWith("dpapi:", StringComparison.Ordinal))
        {
            throw new InvalidDataException("设备密码必须使用 DPAPI 保护格式。");
        }

        var separatorIndex = value.IndexOf(':', "dpapi:".Length);
        if (separatorIndex < 0)
        {
            throw new InvalidDataException("设备密码保护格式无效。");
        }

        var format = value[..separatorIndex];
        if (!string.Equals(format, "dpapi:v1", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"不支持的设备密码保护格式：{format}。");
        }

        var base64 = value[(separatorIndex + 1)..];
        if (base64.Length == 0)
        {
            throw new InvalidDataException("设备密码密文无效。");
        }

        byte[] protectedBytes;
        try
        {
            protectedBytes = Convert.FromBase64String(base64);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("设备密码密文无效。", exception);
        }

        try
        {
            var plainBytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                protectionScope);
            return StrictUtf8.GetString(plainBytes);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("设备密码解密失败。", exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("设备密码解密结果无效。", exception);
        }
    }

    private static DeviceGroup CloneGroup(DeviceGroup group) => new()
    {
        Id = group.Id,
        Name = group.Name,
        ParentId = group.ParentId,
        Sort = group.Sort,
        Enabled = group.Enabled
    };

    private static PersistedCameraChannel ToPersistedChannel(CameraChannel channel) => new()
    {
        Id = channel.Id,
        DeviceId = channel.DeviceId,
        ChannelNo = channel.ChannelNo,
        ChannelName = channel.ChannelName,
        StreamType = channel.StreamType,
        Enabled = channel.Enabled
    };

    private static CameraChannel FromPersistedChannel(PersistedCameraChannel channel) => new()
    {
        Id = channel.Id,
        DeviceId = channel.DeviceId,
        ChannelNo = channel.ChannelNo,
        ChannelName = channel.ChannelName,
        StreamType = channel.StreamType,
        Enabled = channel.Enabled
    };

    private sealed class PersistedDeviceCatalogSnapshot
    {
        public int SchemaVersion { get; set; }

        public IReadOnlyList<DeviceGroup>? Groups { get; set; }

        public IReadOnlyList<PersistedCameraDevice>? Devices { get; set; }
    }

    private sealed class PersistedCameraDevice
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public Guid GroupId { get; set; }

        public string IpAddress { get; set; } = string.Empty;

        public int SdkPort { get; set; } = 8000;

        public int RtspPort { get; set; } = 554;

        public string Username { get; set; } = string.Empty;

        public string? Password { get; set; }

        public string Manufacturer { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;

        public TransportMode TransportMode { get; set; } = TransportMode.Auto;

        public bool Enabled { get; set; } = true;

        public string Remark { get; set; } = string.Empty;

        public List<PersistedCameraChannel>? Channels { get; set; } = [];
    }

    private sealed class PersistedCameraChannel
    {
        public Guid Id { get; set; }

        public Guid DeviceId { get; set; }

        public int ChannelNo { get; set; } = 1;

        public string ChannelName { get; set; } = string.Empty;

        public StreamType StreamType { get; set; } = StreamType.Main;

        public bool Enabled { get; set; } = true;
    }
}
