using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Services;

public static class DeviceCatalogSnapshotFactory
{
    public static DeviceCatalogSnapshot Create(IDeviceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var groups = catalog.GetGroups()
            .Select(CloneGroup)
            .ToArray();
        var devices = groups
            .SelectMany(group => catalog.GetDevices(group.Id))
            .Select(CloneDevice)
            .ToArray();

        return new DeviceCatalogSnapshot
        {
            SchemaVersion = DeviceCatalogSnapshot.CurrentSchemaVersion,
            Groups = groups,
            Devices = devices
        };
    }

    private static DeviceGroup CloneGroup(DeviceGroup group) => new()
    {
        Id = group.Id,
        Name = group.Name,
        ParentId = group.ParentId,
        Sort = group.Sort,
        Enabled = group.Enabled
    };

    private static CameraDevice CloneDevice(CameraDevice device)
    {
        var clone = new CameraDevice
        {
            Id = device.Id,
            Name = device.Name,
            GroupId = device.GroupId,
            IpAddress = device.IpAddress,
            SdkPort = device.SdkPort,
            RtspPort = device.RtspPort,
            Username = device.Username,
            Password = device.Password,
            Manufacturer = device.Manufacturer,
            Model = device.Model,
            TransportMode = device.TransportMode,
            Status = device.Status,
            Enabled = device.Enabled,
            Remark = device.Remark
        };

        foreach (var channel in device.Channels)
        {
            clone.Channels.Add(new CameraChannel
            {
                Id = channel.Id,
                DeviceId = channel.DeviceId,
                ChannelNo = channel.ChannelNo,
                ChannelName = channel.ChannelName,
                StreamType = channel.StreamType,
                StreamId = channel.StreamId,
                Enabled = channel.Enabled
            });
        }

        return clone;
    }
}
