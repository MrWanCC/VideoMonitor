using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;

namespace VideoMonitor.Wpf.Catalog;

public sealed class LegacyDeviceCatalogReadModel : IDeviceCatalogReadModel
{
    private readonly IDeviceCatalog catalog;

    public LegacyDeviceCatalogReadModel(IDeviceCatalog catalog)
    {
        this.catalog = catalog
            ?? throw new ArgumentNullException(nameof(catalog));
        this.catalog.Changed += OnCatalogChanged;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<DeviceGroupDto> GetGroups() =>
        catalog.GetGroups()
            .Select(MapGroup)
            .ToArray();

    public IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId) =>
        catalog.GetDevices(groupId)
            .Select(MapDevice)
            .ToArray();

    public CameraDeviceDto? GetDevice(Guid deviceId)
    {
        var device = catalog.GetDevice(deviceId);
        return device is null ? null : MapDevice(device);
    }

    private void OnCatalogChanged(object? sender, EventArgs e) =>
        Changed?.Invoke(this, e);

    private static DeviceGroupDto MapGroup(DeviceGroup group) => new(
        group.Id,
        group.Name,
        group.ParentId,
        group.Sort,
        group.Enabled,
        group.Kind,
        group.Revision);

    private static CameraDeviceDto MapDevice(CameraDevice device) => new(
        device.Id,
        device.GroupId,
        device.Name,
        device.IpAddress,
        device.SdkPort,
        device.RtspPort,
        device.Username,
        !string.IsNullOrEmpty(device.Password),
        device.Manufacturer,
        device.Model,
        device.TransportMode,
        device.Enabled,
        device.Remark,
        device.Revision,
        device.Channels
            .Select(MapChannel)
            .ToArray());

    private static CameraChannelDto MapChannel(CameraChannel channel) => new(
        channel.Id,
        channel.DeviceId,
        channel.ChannelNo,
        channel.ChannelName,
        channel.StreamType,
        channel.Enabled);
}
