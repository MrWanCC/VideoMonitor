using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Catalog;
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

    [Fact]
    public void SameKindRoots_RenderAsSeparateRootSections()
    {
        var rootA = Guid.NewGuid();
        var rootB = Guid.NewGuid();
        var childA = Guid.NewGuid();
        var childB = Guid.NewGuid();
        var readModel = new MutableReadModelStub(
        [
            new DeviceGroupDto(rootA, "Root A", null, 0, true, MonitorGroupType.Chute, 1),
            new DeviceGroupDto(rootB, "Root B", null, 1, true, MonitorGroupType.Chute, 1),
            new DeviceGroupDto(childA, "401", rootA, 0, true, null, 1),
            new DeviceGroupDto(childB, "501", rootB, 0, true, null, 1)
        ]);
        var groups = MonitorCatalogProjection.CreateGroups(readModel);
        var viewModel = new MonitorViewModel(new MonitorSwitchService(groups), readModel);

        Assert.Equal(2, viewModel.TreeSections.Count);
        Assert.Equal(
            new[] { rootA, rootB },
            viewModel.TreeSections.Select(item => item.ItemId!.Value).ToArray());
        Assert.Equal("401", viewModel.TreeSections[0].Children.Single().Name);
        Assert.Equal("501", viewModel.TreeSections[1].Children.Single().Name);
    }

    [Fact]
    public void DuplicateRootNames_RemainIndependentByGuid()
    {
        var rootA = Guid.NewGuid();
        var rootB = Guid.NewGuid();
        var readModel = new MutableReadModelStub(
        [
            new DeviceGroupDto(rootA, "一号区域", null, 0, true, MonitorGroupType.Chute, 1),
            new DeviceGroupDto(rootB, "一号区域", null, 1, true, MonitorGroupType.Chute, 1),
            new DeviceGroupDto(Guid.NewGuid(), "401", rootA, 0, true, null, 1),
            new DeviceGroupDto(Guid.NewGuid(), "501", rootB, 0, true, null, 1)
        ]);
        var groups = MonitorCatalogProjection.CreateGroups(readModel);
        var viewModel = new MonitorViewModel(new MonitorSwitchService(groups), readModel);

        Assert.Equal(2, viewModel.TreeSections.Count);
        Assert.Equal(
            new[] { rootA, rootB },
            viewModel.TreeSections.Select(item => item.ItemId!.Value).ToArray());
        Assert.All(viewModel.TreeSections, item => Assert.Equal("一号区域", item.Name));
    }

    [Fact]
    public void DuplicateChildNames_SelectByGuid()
    {
        var rootId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var readModel = new MutableReadModelStub(
        [
            new DeviceGroupDto(rootId, "Chute Root", null, 0, true, MonitorGroupType.Chute, 1),
            new DeviceGroupDto(firstId, "同名分组", rootId, 0, true, null, 1),
            new DeviceGroupDto(secondId, "同名分组", rootId, 1, true, null, 1)
        ]);
        var groups = MonitorCatalogProjection.CreateGroups(readModel);
        var service = new MonitorSwitchService(groups);
        var viewModel = new MonitorViewModel(service, readModel);
        var secondItem = viewModel.TreeSections
            .SelectMany(section => section.Children)
            .Single(item => item.ItemId == secondId);

        viewModel.SelectGroupCommand.Execute(secondItem);

        Assert.Equal(secondId, service.SelectedChuteGroupId);
    }

    [Fact]
    public void CatalogChange_RenamePreservesSelectedGuid()
    {
        var rootId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var initial = new[]
        {
            new DeviceGroupDto(rootId, "Chute Root", null, 0, true, MonitorGroupType.Chute, 1),
            new DeviceGroupDto(firstId, "A", rootId, 0, true, null, 1),
            new DeviceGroupDto(secondId, "B", rootId, 1, true, null, 1)
        };
        var readModel = new MutableReadModelStub(initial);
        var groups = MonitorCatalogProjection.CreateGroups(readModel);
        var service = new MonitorSwitchService(groups);
        var viewModel = new MonitorViewModel(service, readModel);
        var selected = viewModel.TreeSections.SelectMany(section => section.Children)
            .Single(item => item.ItemId == secondId);
        viewModel.SelectGroupCommand.Execute(selected);

        readModel.Replace(
        [
            initial[0],
            initial[1],
            new DeviceGroupDto(secondId, "B renamed", rootId, 1, true, null, 2)
        ]);
        readModel.RaiseChanged();

        var renamed = viewModel.TreeSections.SelectMany(section => section.Children)
            .Single(item => item.ItemId == secondId);
        Assert.True(renamed.IsSelected);
        Assert.Equal(secondId, service.SelectedChuteGroupId);
        Assert.Equal("B renamed", viewModel.CurrentChuteName);
    }

    [Fact]
    public void CatalogChange_DeletedSelectedGroupFallsBackByGuid()
    {
        var rootId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var initial = new[]
        {
            new DeviceGroupDto(rootId, "Chute Root", null, 0, true, MonitorGroupType.Chute, 1),
            new DeviceGroupDto(firstId, "A", rootId, 0, true, null, 1),
            new DeviceGroupDto(secondId, "B", rootId, 1, true, null, 1)
        };
        var readModel = new MutableReadModelStub(initial);
        var groups = MonitorCatalogProjection.CreateGroups(readModel);
        var service = new MonitorSwitchService(groups);
        var viewModel = new MonitorViewModel(service, readModel);
        var selected = viewModel.TreeSections.SelectMany(section => section.Children)
            .Single(item => item.ItemId == secondId);
        viewModel.SelectGroupCommand.Execute(selected);

        readModel.Replace(initial.Take(2).ToArray());
        readModel.RaiseChanged();

        Assert.Equal(firstId, service.SelectedChuteGroupId);
        var fallback = viewModel.TreeSections.SelectMany(section => section.Children)
            .Single(item => item.ItemId == firstId);
        Assert.True(fallback.IsSelected);
    }

    [Fact]
    public void CatalogChange_PreservesRootExpansionByRootGuid()
    {
        var rootA = Guid.NewGuid();
        var rootB = Guid.NewGuid();
        var initial = new[]
        {
            new DeviceGroupDto(rootA, "同名 Root", null, 0, true, MonitorGroupType.Chute, 1),
            new DeviceGroupDto(rootB, "同名 Root", null, 1, true, MonitorGroupType.Chute, 1),
            new DeviceGroupDto(Guid.NewGuid(), "A", rootA, 0, true, null, 1),
            new DeviceGroupDto(Guid.NewGuid(), "B", rootB, 0, true, null, 1)
        };
        var readModel = new MutableReadModelStub(initial);
        var groups = MonitorCatalogProjection.CreateGroups(readModel);
        var viewModel = new MonitorViewModel(new MonitorSwitchService(groups), readModel);
        viewModel.TreeSections.Single(item => item.ItemId == rootA).IsExpanded = false;
        viewModel.TreeSections.Single(item => item.ItemId == rootB).IsExpanded = true;

        readModel.RaiseChanged();

        Assert.False(viewModel.TreeSections.Single(item => item.ItemId == rootA).IsExpanded);
        Assert.True(viewModel.TreeSections.Single(item => item.ItemId == rootB).IsExpanded);
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

    private sealed class MutableReadModelStub : IDeviceCatalogReadModel
    {
        private IReadOnlyList<DeviceGroupDto> groups;

        public MutableReadModelStub(IReadOnlyList<DeviceGroupDto> groups) => this.groups = groups;

        public event EventHandler? Changed;

        public IReadOnlyList<DeviceGroupDto> GetGroups() => groups;

        public IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId) => [];

        public CameraDeviceDto? GetDevice(Guid deviceId) => null;

        public void Replace(IReadOnlyList<DeviceGroupDto> next) => groups = next;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
