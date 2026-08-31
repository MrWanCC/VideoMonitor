using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;

namespace VideoMonitor.Core.Tests.Services;

public sealed class MonitorCatalogProjectionTests
{
    [Fact]
    public void EmptyCatalog_ProducesFourMainAndThreeSecondaryNullSlots()
    {
        var groups = MonitorCatalogProjection.CreateGroups(new ReadModelStub());

        var layout = new MonitorSwitchService(groups).CurrentLayout;

        Assert.Equal(4, layout.MainSlots.Count);
        Assert.Equal(3, layout.SecondarySlots.Count);
        Assert.All(layout.MainSlots, slot => Assert.Null(slot));
        Assert.All(layout.SecondarySlots, slot => Assert.Null(slot));
    }

    [Fact]
    public void Projection_UsesRootKind_NotRootName()
    {
        var root = Group(Guid.NewGuid(), "任意名字A", null, 4, true, MonitorGroupType.Chute);
        var child = Group(Guid.NewGuid(), "任意业务组", root.Id, 7, true, null);
        var catalog = new ReadModelStub(root, child);

        var projected = Assert.Single(
            MonitorCatalogProjection.CreateGroups(catalog));

        Assert.Equal(MonitorGroupType.Chute, projected.Type);
        Assert.Equal(root.Id, projected.RootGroupId);
        Assert.Equal(root.Name, projected.RootName);
        Assert.Equal(root.Sort, projected.RootSort);
        Assert.Equal(child.Sort, projected.Sort);
    }

    [Fact]
    public void UnclassifiedRoot_IsExcludedEvenWhenNameLooksLegacy()
    {
        var root = Group(Guid.NewGuid(), "溜井监控", null, 0, true, null);
        var child = Group(Guid.NewGuid(), "Child", root.Id, 0, true, null);

        var projected = MonitorCatalogProjection.CreateGroups(
            new ReadModelStub(root, child));

        Assert.Empty(projected);
    }

    [Fact]
    public void MalformedChildParent_IsExcludedWithoutThrowing()
    {
        var root = Group(Guid.NewGuid(), "Root", null, 0, true, MonitorGroupType.Chute);
        var child = Group(Guid.NewGuid(), "Orphan", Guid.NewGuid(), 0, true, null);

        var projected = MonitorCatalogProjection.CreateGroups(
            new ReadModelStub(root, child));

        Assert.Empty(projected);
    }

    [Fact]
    public void NestedChild_IsExcluded()
    {
        var root = Group(Guid.NewGuid(), "Root", null, 0, true, MonitorGroupType.Chute);
        var child = Group(Guid.NewGuid(), "Child 1", root.Id, 0, true, null);
        var nested = Group(Guid.NewGuid(), "Child 2", child.Id, 0, true, null);

        var projected = MonitorCatalogProjection.CreateGroups(
            new ReadModelStub(root, child, nested));

        var onlyGroup = Assert.Single(projected);
        Assert.Equal(child.Id, onlyGroup.GroupId);
    }

    [Fact]
    public void DisabledRootChildDeviceAndChannel_AreExcluded()
    {
        var disabledRoot = Group(Guid.NewGuid(), "Disabled root", null, 0, false, MonitorGroupType.Chute);
        var disabledRootChild = Group(Guid.NewGuid(), "Hidden child", disabledRoot.Id, 0, true, null);
        var root = Group(Guid.NewGuid(), "Root", null, 1, true, MonitorGroupType.Chute);
        var disabledChild = Group(Guid.NewGuid(), "Disabled child", root.Id, 0, false, null);
        var child = Group(Guid.NewGuid(), "Child", root.Id, 1, true, null);
        var disabledChannel = Channel(Guid.NewGuid(), Guid.NewGuid(), 1, false);
        var disabledDevice = Device(
            Guid.NewGuid(),
            child.Id,
            "Disabled device",
            false,
            Channel(Guid.NewGuid(), Guid.NewGuid(), 1, true));
        var enabledDevice = Device(
            Guid.NewGuid(),
            child.Id,
            "Enabled device",
            true,
            disabledChannel with { DeviceId = Guid.NewGuid() });
        var catalog = new ReadModelStub(
            [disabledRoot, disabledRootChild, root, disabledChild, child],
            [disabledDevice, enabledDevice]);

        var projected = MonitorCatalogProjection.CreateGroups(catalog);

        var onlyGroup = Assert.Single(projected);
        Assert.Equal(child.Id, onlyGroup.GroupId);
        Assert.Empty(onlyGroup.Cameras);
    }

    [Fact]
    public void CentralProjection_InitializesCameraStatusUnknown()
    {
        var root = Group(Guid.NewGuid(), "Root", null, 0, true, MonitorGroupType.Chute);
        var child = Group(Guid.NewGuid(), "Child", root.Id, 0, true, null);
        var deviceId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var catalog = new ReadModelStub(
            [root, child],
            [Device(
                deviceId,
                child.Id,
                "Camera",
                true,
                Channel(channelId, deviceId, 1, true))]);

        var camera = Assert.Single(
            Assert.Single(MonitorCatalogProjection.CreateGroups(catalog)).Cameras);

        Assert.Equal(CameraStatus.Unknown, camera.Status);
    }

    [Fact]
    public void DuplicateNames_RemainDistinctByGuid()
    {
        var root = Group(Guid.NewGuid(), "Root", null, 0, true, MonitorGroupType.Chute);
        var first = Group(Guid.NewGuid(), "Same name", root.Id, 0, true, null);
        var second = Group(Guid.NewGuid(), "Same name", root.Id, 1, true, null);

        var projected = MonitorCatalogProjection.CreateGroups(
            new ReadModelStub(root, first, second));

        Assert.Equal(2, projected.Count);
        Assert.NotEqual(projected[0].GroupId, projected[1].GroupId);
    }

    [Fact]
    public void SameKindRoots_AreNotMerged()
    {
        var firstRoot = Group(Guid.NewGuid(), "Root A", null, 0, true, MonitorGroupType.Chute);
        var firstChild = Group(Guid.NewGuid(), "Child A", firstRoot.Id, 0, true, null);
        var secondRoot = Group(Guid.NewGuid(), "Root B", null, 1, true, MonitorGroupType.Chute);
        var secondChild = Group(Guid.NewGuid(), "Child B", secondRoot.Id, 0, true, null);

        var projected = MonitorCatalogProjection.CreateGroups(
            new ReadModelStub(firstRoot, firstChild, secondRoot, secondChild));

        Assert.Equal(2, projected.Count);
        Assert.Equal(firstRoot.Id, projected[0].RootGroupId);
        Assert.Equal(secondRoot.Id, projected[1].RootGroupId);
    }

    [Fact]
    public void DefaultSelection_UsesRootSortChildSortAndGuid()
    {
        var root = Group(Guid.NewGuid(), "Root", null, 0, true, MonitorGroupType.Chute);
        var emptyDefault = Group(Guid.NewGuid(), "Empty default", root.Id, 0, true, null);
        var later = Group(Guid.NewGuid(), "Later", root.Id, 1, true, null);
        var laterDeviceId = Guid.NewGuid();
        var catalog = new ReadModelStub(
            [root, emptyDefault, later],
            [Device(
                laterDeviceId,
                later.Id,
                "Later camera",
                true,
                Channel(Guid.NewGuid(), laterDeviceId, 1, true))]);
        var groups = MonitorCatalogProjection.CreateGroups(catalog);

        var layout = new MonitorSwitchService(groups).CurrentLayout;

        Assert.All(layout.MainSlots.Take(3), slot => Assert.Null(slot));
    }

    [Fact]
    public void OneCameraChute_ProducesNullPadding()
    {
        var root = Group(Guid.NewGuid(), "Root", null, 0, true, MonitorGroupType.Chute);
        var child = Group(Guid.NewGuid(), "Child", root.Id, 0, true, null);
        var deviceId = Guid.NewGuid();
        var catalog = new ReadModelStub(
            [root, child],
            [Device(
                deviceId,
                child.Id,
                "Camera",
                true,
                Channel(Guid.NewGuid(), deviceId, 1, true))]);

        var groups = MonitorCatalogProjection.CreateGroups(catalog);
        var camera = Assert.Single(Assert.Single(groups).Cameras);
        var layout = new MonitorSwitchService(
                groups)
            .CurrentLayout;

        Assert.Same(camera, layout.MainSlots[0]);
        Assert.Null(layout.MainSlots[1]);
        Assert.Null(layout.MainSlots[2]);
    }

    [Fact]
    public void MissingTunnel_LeavesFourthMainSlotNull()
    {
        var root = Group(Guid.NewGuid(), "Root", null, 0, true, MonitorGroupType.Chute);
        var child = Group(Guid.NewGuid(), "Child", root.Id, 0, true, null);
        var groups = MonitorCatalogProjection.CreateGroups(
            new ReadModelStub(root, child));

        var layout = new MonitorSwitchService(groups).CurrentLayout;

        Assert.Null(layout.MainSlots[3]);
    }

    [Fact]
    public void TwoCameraUnloading_ProducesThirdNull()
    {
        var root = Group(Guid.NewGuid(), "Root", null, 0, true, MonitorGroupType.UnloadingStation);
        var child = Group(Guid.NewGuid(), "Child", root.Id, 0, true, null);
        var firstDevice = Guid.NewGuid();
        var secondDevice = Guid.NewGuid();
        var catalog = new ReadModelStub(
            [root, child],
            [
                Device(firstDevice, child.Id, "Camera 1", true, Channel(Guid.NewGuid(), firstDevice, 1, true)),
                Device(secondDevice, child.Id, "Camera 2", true, Channel(Guid.NewGuid(), secondDevice, 2, true))
            ]);

        var layout = new MonitorSwitchService(
                MonitorCatalogProjection.CreateGroups(catalog))
            .CurrentLayout;

        Assert.NotNull(layout.SecondarySlots[0]);
        Assert.NotNull(layout.SecondarySlots[1]);
        Assert.Null(layout.SecondarySlots[2]);
    }

    [Fact]
    public void CreateGroups_ProjectsAllRequired3Plus1ChannelsFromCatalog()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);

        var groups = MonitorCatalogProjection.CreateGroups(catalog);

        Assert.Equal(12, groups.Count);
        Assert.Equal(3, groups.Single(group => group.Name == "备用1").Cameras.Count);
        var west401 = groups.Single(group => group.Name == "西401溜井");
        Assert.Equal(3, west401.Cameras.Count);
        Assert.Equal(new[] { 1, 2, 3 }, west401.Cameras.Select(camera => camera.ChannelNumber));
        Assert.Equal(3, groups.Single(group => group.Name == "2#主溜井").Cameras.Count);
        Assert.Single(groups.Single(group => group.Name == "Z-1#巷").Cameras);
    }

    [Fact]
    public void CreateGroups_PreservesStableSourceGroupId()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var sourceGroup = data.Groups.Single(group => group.Name == "西401溜井");

        var projectedGroup = MonitorCatalogProjection.CreateGroups(catalog)
            .Single(group => group.Name == sourceGroup.Name);

        Assert.Equal(sourceGroup.Id, projectedGroup.GroupId);
    }

    [Fact]
    public void CreateGroups_CarriesOnlyStableDeviceAndChannelAssociations()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var device = catalog.GetDevice(
            data.Devices.Single(item => item.Name == "西401溜井 · 通道1").Id)!;
        var channel = Assert.Single(device.Channels);

        var camera = MonitorCatalogProjection.CreateGroups(catalog)
            .Single(group => group.Name == "西401溜井")
            .Cameras
            .Single(item => item.DeviceId == device.Id);

        Assert.Equal(device.Id, camera.DeviceId);
        Assert.Equal(channel.Id, camera.ChannelId);
    }

    private static DeviceGroupDto Group(
        Guid id,
        string name,
        Guid? parentId,
        int sort,
        bool enabled,
        MonitorGroupType? kind) =>
        new(id, name, parentId, sort, enabled, kind, 1);

    private static CameraDeviceDto Device(
        Guid id,
        Guid groupId,
        string name,
        bool enabled,
        params CameraChannelDto[] channels) =>
        new(
            id,
            groupId,
            name,
            "192.0.2.10",
            8000,
            554,
            "user",
            false,
            "",
            "",
            TransportMode.Auto,
            enabled,
            "",
            1,
            channels);

    private static CameraChannelDto Channel(
        Guid id,
        Guid deviceId,
        int channelNo,
        bool enabled) =>
        new(id, deviceId, channelNo, "Channel", StreamType.Main, enabled);

    private sealed class ReadModelStub : IDeviceCatalogReadModel
    {
        private readonly IReadOnlyList<DeviceGroupDto> groups;
        private readonly IReadOnlyDictionary<Guid, IReadOnlyList<CameraDeviceDto>> devices;

        public ReadModelStub(params DeviceGroupDto[] groups)
            : this((IReadOnlyList<DeviceGroupDto>)groups, [])
        {
        }

        public ReadModelStub(
            IReadOnlyList<DeviceGroupDto> groups,
            IReadOnlyList<CameraDeviceDto> devices)
        {
            this.groups = groups;
            this.devices = devices
                .GroupBy(device => device.GroupId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<CameraDeviceDto>)group.ToArray());
        }

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<DeviceGroupDto> GetGroups() => groups;

        public IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId) =>
            devices.TryGetValue(groupId, out var result) ? result : [];

        public CameraDeviceDto? GetDevice(Guid deviceId) =>
            devices.Values.SelectMany(items => items)
                .FirstOrDefault(device => device.Id == deviceId);
    }
}
