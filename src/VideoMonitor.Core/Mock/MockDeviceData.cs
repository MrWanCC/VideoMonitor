using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Mock;

public static class MockDeviceData
{
    public static MockDeviceDataSet Create()
    {
        var unloading = CreateGroup("10000000-0000-0000-0000-000000000001", "卸矿站监控", null, 1);
        var chute = CreateGroup("10000000-0000-0000-0000-000000000002", "溜井监控", null, 2);
        var tunnel = CreateGroup("10000000-0000-0000-0000-000000000003", "巷道监控", null, 3);

        var groups = new List<DeviceGroup>
        {
            unloading,
            chute,
            tunnel,
            CreateGroup("20000000-0000-0000-0000-000000000001", "2#主溜井", unloading.Id, 1),
            CreateGroup("20000000-0000-0000-0000-000000000002", "3#主溜井", unloading.Id, 2),
            CreateGroup("30000000-0000-0000-0000-000000000001", "备用1", chute.Id, 1),
            CreateGroup("30000000-0000-0000-0000-000000000002", "备用2", chute.Id, 2),
            CreateGroup("30000000-0000-0000-0000-000000000003", "西401溜井", chute.Id, 3),
            CreateGroup("30000000-0000-0000-0000-000000000004", "西402溜井", chute.Id, 4),
            CreateGroup("30000000-0000-0000-0000-000000000005", "西403溜井", chute.Id, 5),
            CreateGroup("40000000-0000-0000-0000-000000000001", "Z-1#巷", tunnel.Id, 1),
            CreateGroup("40000000-0000-0000-0000-000000000002", "Z-2#巷", tunnel.Id, 2),
            CreateGroup("40000000-0000-0000-0000-000000000003", "Z-3#巷", tunnel.Id, 3),
            CreateGroup("40000000-0000-0000-0000-000000000004", "F-1#巷", tunnel.Id, 4),
            CreateGroup("40000000-0000-0000-0000-000000000005", "F-2#巷", tunnel.Id, 5)
        };

        var west401 = groups.Single(group => group.Name == "西401溜井");
        var devices = Enumerable.Range(1, 3)
            .Select(index => CreateWest401Device(west401.Id, index))
            .ToArray();

        return new MockDeviceDataSet(groups, devices);
    }

    private static DeviceGroup CreateGroup(string id, string name, Guid? parentId, int sort) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        ParentId = parentId,
        Sort = sort,
        Enabled = true
    };

    private static CameraDevice CreateWest401Device(Guid groupId, int index)
    {
        var deviceId = Guid.Parse($"50000000-0000-0000-0000-{index:000000000000}");
        var device = new CameraDevice
        {
            Id = deviceId,
            Name = $"西401溜井 · 通道{index}",
            GroupId = groupId,
            IpAddress = $"192.168.17.{4 + index}",
            SdkPort = 8000,
            RtspPort = 554,
            Username = "admin",
            Password = "mock-password",
            Manufacturer = "海康威视",
            Model = "IPC",
            TransportMode = index switch
            {
                2 => TransportMode.Tcp,
                3 => TransportMode.Udp,
                _ => TransportMode.Auto
            },
            Status = CameraStatus.Online,
            Enabled = true
        };

        device.Channels.Add(new CameraChannel
        {
            Id = Guid.Parse($"60000000-0000-0000-0000-{index:000000000000}"),
            DeviceId = deviceId,
            ChannelNo = 1,
            ChannelName = "通道1",
            StreamType = index == 3 ? StreamType.Sub : StreamType.Main,
            StreamId = $"west401-camera-{index}",
            Enabled = true
        });

        return device;
    }
}
