using System.IO;
using VideoMonitor.Core.Catalog;

namespace VideoMonitor.Wpf.Catalog;

public sealed class ClientCatalogCache : IDeviceCatalogReadModel
{
    private readonly IUiDispatcher dispatcher;
    private CatalogSnapshotDto snapshot;

    public ClientCatalogCache(
        CatalogSnapshotDto initial,
        IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ValidateSnapshot(initial);
        this.dispatcher = dispatcher
            ?? throw new ArgumentNullException(nameof(dispatcher));
        snapshot = initial;
    }

    public CatalogSnapshotDto Snapshot => snapshot;

    public event EventHandler? Changed;

    public Task ReplaceAsync(
        CatalogSnapshotDto next,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(next);
        ValidateSnapshot(next);

        return dispatcher.InvokeAsync(
            () => ApplyPreparedSnapshotOnUiThread(next),
            cancellationToken);
    }

    internal CatalogSnapshotDto PrepareSnapshot(CatalogSnapshotDto next)
    {
        ValidateSnapshot(next);
        return next;
    }

    internal bool ApplyPreparedSnapshotOnUiThread(CatalogSnapshotDto next)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (SnapshotsEqual(snapshot, next))
        {
            return false;
        }

        snapshot = next;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public IReadOnlyList<DeviceGroupDto> GetGroups() =>
        Snapshot.Groups;

    public IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId) =>
        Snapshot.Devices
            .Where(device => device.GroupId == groupId)
            .ToArray();

    public CameraDeviceDto? GetDevice(Guid deviceId) =>
        Snapshot.Devices.SingleOrDefault(device => device.Id == deviceId);

    private static bool SnapshotsEqual(
        CatalogSnapshotDto left,
        CatalogSnapshotDto right) =>
        left.Groups.SequenceEqual(right.Groups)
        && left.Devices.Count == right.Devices.Count
        && left.Devices.Zip(right.Devices).All(
            pair => DevicesEqual(pair.First, pair.Second));

    private static bool DevicesEqual(
        CameraDeviceDto left,
        CameraDeviceDto right) =>
        left.Id == right.Id
        && left.GroupId == right.GroupId
        && left.Name == right.Name
        && left.IpAddress == right.IpAddress
        && left.SdkPort == right.SdkPort
        && left.RtspPort == right.RtspPort
        && left.Username == right.Username
        && left.HasPassword == right.HasPassword
        && left.Manufacturer == right.Manufacturer
        && left.Model == right.Model
        && left.TransportMode == right.TransportMode
        && left.Enabled == right.Enabled
        && left.Remark == right.Remark
        && left.Revision == right.Revision
        && left.Channels.SequenceEqual(right.Channels);

    private static void ValidateSnapshot(CatalogSnapshotDto snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Groups is null
            || snapshot.Devices is null
            || snapshot.Groups.Any(group => group is null)
            || snapshot.Devices.Any(
                device => device is null
                    || device.Channels is null
                    || device.Channels.Any(channel => channel is null)))
        {
            throw new InvalidDataException("Catalog snapshot is invalid.");
        }
    }
}
