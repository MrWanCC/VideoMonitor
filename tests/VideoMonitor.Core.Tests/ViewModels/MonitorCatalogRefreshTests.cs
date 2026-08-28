using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class MonitorCatalogRefreshTests
{
    [Fact]
    public void CatalogChange_RebuildsMonitorTreeAfterAddingGroup()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var groups = MonitorCatalogProjection.CreateGroups(catalog);
        var switchService = new MonitorSwitchService(
            groups.Single(group => group.Name == "西401溜井"),
            groups.Single(group => group.Name == "Z-1#巷"),
            groups.Single(group => group.Name == "2#主溜井"));
        var monitor = new MonitorViewModel(switchService, groups, catalog);
        var root = data.Groups.Single(group => group.Name == "溜井监控");

        catalog.AddGroup(new DeviceGroup
        {
            Id = Guid.Parse("90000000-0000-0000-0000-000000000001"),
            Name = "新增溜井",
            ParentId = root.Id,
            Sort = 999,
            Enabled = true
        });

        var section = monitor.TreeSections.Single(item => item.Name == "溜井监控");
        Assert.Contains(section.Children, item => item.Name == "新增溜井");
    }

    [Fact]
    public void CatalogChange_RebuildsMonitorTreeAfterRenamingGroupAndPreservesSelection()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var groups = MonitorCatalogProjection.CreateGroups(catalog);
        var switchService = new MonitorSwitchService(
            groups.Single(group => group.Name == "西401溜井"),
            groups.Single(group => group.Name == "Z-1#巷"),
            groups.Single(group => group.Name == "2#主溜井"));
        var monitor = new MonitorViewModel(switchService, groups, catalog);
        var treeSections = monitor.TreeSections;
        var source = data.Groups.Single(group => group.Name == "西401溜井");

        catalog.UpdateGroup(new DeviceGroup
        {
            Id = source.Id,
            Name = "西401新名称",
            ParentId = source.ParentId,
            Sort = source.Sort,
            Enabled = source.Enabled
        });

        var item = monitor.TreeSections
            .SelectMany(section => section.Children)
            .Single(child => child.Name == "西401新名称");
        Assert.True(item.IsSelected);
        Assert.Equal("西401新名称", monitor.CurrentChuteName);
        Assert.Same(treeSections, monitor.TreeSections);
    }

    [Fact]
    public void CatalogChange_RemovesDeletedGroupFromMonitorTree()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var root = data.Groups.Single(group => group.Name == "溜井监控");
        var removable = new DeviceGroup
        {
            Id = Guid.Parse("90000000-0000-0000-0000-000000000002"),
            Name = "待删除溜井",
            ParentId = root.Id,
            Sort = 999,
            Enabled = true
        };
        catalog.AddGroup(removable);
        var groups = MonitorCatalogProjection.CreateGroups(catalog);
        var switchService = new MonitorSwitchService(
            groups.Single(group => group.Name == "西401溜井"),
            groups.Single(group => group.Name == "Z-1#巷"),
            groups.Single(group => group.Name == "2#主溜井"));
        var monitor = new MonitorViewModel(switchService, groups, catalog);

        Assert.True(catalog.DeleteGroup(removable.Id));

        var section = monitor.TreeSections.Single(item => item.Name == "溜井监控");
        Assert.DoesNotContain(section.Children, item => item.Name == "待删除溜井");
    }

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
