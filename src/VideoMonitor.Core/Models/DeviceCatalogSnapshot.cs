namespace VideoMonitor.Core.Models;

public sealed class DeviceCatalogSnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; }

    public IReadOnlyList<DeviceGroup> Groups { get; init; } = [];

    public IReadOnlyList<CameraDevice> Devices { get; init; } = [];
}
