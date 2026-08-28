using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Mock;

public static class MockMonitorData
{
    public static IReadOnlyList<MonitorGroup> CreateGroups()
    {
        return
        [
            CreateThreeChannelGroup("2#主溜井", MonitorGroupType.UnloadingStation),
            CreateThreeChannelGroup("3#主溜井", MonitorGroupType.UnloadingStation),
            CreateThreeChannelGroup("备用1", MonitorGroupType.Chute, shortNames: true),
            CreateThreeChannelGroup("备用2", MonitorGroupType.Chute, shortNames: true),
            CreateThreeChannelGroup("西401溜井", MonitorGroupType.Chute),
            CreateThreeChannelGroup("西402溜井", MonitorGroupType.Chute),
            CreateThreeChannelGroup("西403溜井", MonitorGroupType.Chute),
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
        bool shortNames = false)
    {
        var cameras = Enumerable.Range(1, 3)
            .Select(channel => new CameraInfo(
                shortNames ? $"通道{channel}" : $"{groupName}-通道{channel}",
                groupName,
                channel,
                CameraStatus.Online,
                $"{3.6 + channel * 0.3:0.0} Mbps",
                channel == 3 ? "辅码流" : "主码流"))
            .ToArray();

        return new MonitorGroup(groupName, type, cameras);
    }

    private static MonitorGroup CreateTunnelGroup(string groupName)
    {
        return new MonitorGroup(
            groupName,
            MonitorGroupType.Tunnel,
            [new CameraInfo(groupName, groupName, 1, CameraStatus.Online, "3.8 Mbps", "主码流")]);
    }
}
