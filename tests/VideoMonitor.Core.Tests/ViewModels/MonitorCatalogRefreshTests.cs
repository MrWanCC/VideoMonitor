using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class MonitorCatalogRefreshTests
{
    [Fact]
    public void CatalogChange_RefreshesMonitorDisplayWithoutReplacingDeviceAssociation()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var groups = MonitorCatalogProjection.CreateGroups(catalog);
        var switchService = new MonitorSwitchService(
            groups.Single(group => group.Name == "西401溜井"),
            groups.Single(group => group.Name == "Z-1#巷"),
            groups.Single(group => group.Name == "2#主溜井"));
        var monitor = new MonitorViewModel(switchService, groups, catalog);
        var device = data.Devices.Single(item => item.Name == "西401溜井 · 通道1");
        var updated = Clone(device);
        updated.IpAddress = "192.0.2.20";

        catalog.UpdateDevice(updated);

        Assert.Equal("192.0.2.20", monitor.MainTiles[0].IpAddress);
        Assert.Equal(device.Id, groups.Single(item => item.Name == "西401溜井").Cameras[0].DeviceId);
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
