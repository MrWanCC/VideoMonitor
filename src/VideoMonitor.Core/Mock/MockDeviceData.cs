using System.Security.Cryptography;
using System.Text;
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

        var devices = groups
            .Where(group => group.ParentId is not null)
            .SelectMany(CreateDevices)
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

    private static IEnumerable<CameraDevice> CreateDevices(DeviceGroup group)
    {
        var channelCount = group.Name is "Z-1#巷" or "Z-2#巷" or "Z-3#巷" or "F-1#巷" or "F-2#巷"
            ? 1
            : 3;
        return Enumerable.Range(1, channelCount)
            .Select(slotNo => CreateDevice(group, slotNo));
    }

    private static CameraDevice CreateDevice(DeviceGroup group, int slotNo)
    {
        var isWest401 = group.Name == "西401溜井";
        var (deviceId, channelId) = GetStableIds(group, slotNo);
        var device = new CameraDevice
        {
            Id = deviceId,
            Name = $"{group.Name} · 通道{slotNo}",
            GroupId = group.Id,
            IpAddress = isWest401
                ? $"192.168.17.{4 + slotNo}"
                : $"10.{group.Id.ToString()[0] - '0'}.0.{group.Sort * 10 + slotNo}",
            SdkPort = 8000,
            RtspPort = 554,
            Username = "admin",
            Password = "mock-password",
            Manufacturer = "海康威视",
            Model = "IPC",
            TransportMode = isWest401
                ? slotNo switch
                {
                    2 => TransportMode.Tcp,
                    3 => TransportMode.Udp,
                    _ => TransportMode.Auto
                }
                : TransportMode.Auto,
            Status = CameraStatus.Online,
            Enabled = true
        };

        device.Channels.Add(new CameraChannel
        {
            Id = channelId,
            DeviceId = deviceId,
            ChannelNo = 1,
            ChannelName = "通道1",
            StreamType = isWest401 && slotNo == 3 ? StreamType.Sub : StreamType.Main,
            StreamId = isWest401
                ? $"west401-camera-{slotNo}"
                : $"mock-{deviceId:N}",
            Enabled = true
        });

        return device;
    }

    private static (Guid DeviceId, Guid ChannelId) GetStableIds(
        DeviceGroup group,
        int slotNo)
    {
        if (group.Name == "西401溜井")
        {
            return (
                Guid.Parse($"50000000-0000-0000-0000-{slotNo:000000000000}"),
                Guid.Parse($"60000000-0000-0000-0000-{slotNo:000000000000}"));
        }

        return (
            CreateStableId("device", group.Id, slotNo),
            CreateStableId("channel", group.Id, slotNo));
    }

    private static Guid CreateStableId(string kind, Guid groupId, int channelNo)
    {
        var bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes($"videomonitor:{kind}:{groupId:N}:{channelNo}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
