using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;

namespace VideoMonitor.Core.Tests.Services;

public sealed class DeviceCatalogTests
{
    [Fact]
    public void Constructor_PreservesStableDeviceAndChannelObjects()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var device = data.Devices.Single(item => item.Name == "西401溜井 · 通道1");

        Assert.Same(device, catalog.GetDevice(device.Id));
        Assert.Same(device.Channels[0], catalog.GetDevice(device.Id)!.Channels[0]);
        Assert.Equal(3, catalog.GetDevices(device.GroupId).Count);
    }

    [Fact]
    public void UpdateDevice_UpdatesExistingCatalogObjectAndRaisesChanged()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var original = data.Devices.Single(item => item.Name == "西401溜井 · 通道1");
        var updated = Clone(original);
        updated.IpAddress = "192.0.2.20";
        var changed = 0;
        catalog.Changed += (_, _) => changed++;

        catalog.UpdateDevice(updated);

        Assert.Same(original, catalog.GetDevice(original.Id));
        Assert.Equal("192.0.2.20", catalog.GetDevice(original.Id)!.IpAddress);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void DeleteDevice_RemovesOnlyTheRequestedCatalogObject()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var deleted = data.Devices.Single(item => item.Name == "西401溜井 · 通道1");

        Assert.True(catalog.DeleteDevice(deleted.Id));

        Assert.Null(catalog.GetDevice(deleted.Id));
        Assert.Equal(2, catalog.GetDevices(deleted.GroupId).Count);
    }

    private static CameraDevice Clone(CameraDevice source)
    {
        var clone = new CameraDevice
        {
            Id = source.Id,
            Name = source.Name,
            GroupId = source.GroupId,
            IpAddress = source.IpAddress,
            SdkPort = source.SdkPort,
            RtspPort = source.RtspPort,
            Username = source.Username,
            Password = source.Password,
            Manufacturer = source.Manufacturer,
            Model = source.Model,
            TransportMode = source.TransportMode,
            Status = source.Status,
            Enabled = source.Enabled,
            Remark = source.Remark
        };
        foreach (var channel in source.Channels)
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
