using VideoMonitor.Client.Models;

namespace VideoMonitor.Client.Mock;

public static class MockMonitorData
{
    public static IReadOnlyList<MonitorGroup> CreateGroups()
    {
        return
        [
            CreateThreeChannelGroup("2#主溜井", MonitorGroupType.UnloadingStation),
            CreateThreeChannelGroup("3#主溜井", MonitorGroupType.UnloadingStation),
            CreateThreeChannelGroup("备用1", MonitorGroupType.Shaft, useShortChannelNames: true),
            CreateThreeChannelGroup("备用2", MonitorGroupType.Shaft, useShortChannelNames: true),
            CreateThreeChannelGroup("西401溜井", MonitorGroupType.Shaft),
            CreateThreeChannelGroup("西402溜井", MonitorGroupType.Shaft),
            CreateThreeChannelGroup("西403溜井", MonitorGroupType.Shaft),
            CreateTunnelGroup("Z-1#巷"),
            CreateTunnelGroup("Z-2#巷"),
            CreateTunnelGroup("Z-3#巷"),
            CreateTunnelGroup("F-1#巷"),
            CreateTunnelGroup("F-2#巷")
        ];
    }

    private static MonitorGroup CreateThreeChannelGroup(
        string groupName,
        MonitorGroupType type,
        bool useShortChannelNames = false)
    {
        var cameras = Enumerable.Range(1, 3)
            .Select(channel => new CameraInfo(
                useShortChannelNames ? $"通道{channel}" : $"{groupName}-通道{channel}",
                groupName,
                channel))
            .ToArray();

        return new MonitorGroup(groupName, type, cameras);
    }

    private static MonitorGroup CreateTunnelGroup(string groupName)
    {
        return new MonitorGroup(
            groupName,
            MonitorGroupType.Tunnel,
            [new CameraInfo(groupName, groupName, 1)]);
    }
}
