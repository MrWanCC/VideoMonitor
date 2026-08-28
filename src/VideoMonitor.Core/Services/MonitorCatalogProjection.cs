using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Services;

public static class MonitorCatalogProjection
{
    public static IReadOnlyList<MonitorGroup> CreateGroups(IDeviceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var groups = catalog.GetGroups();
        return groups
            .Where(group => group.ParentId is not null)
            .OrderBy(group => GetRootSort(groups, group.ParentId!.Value))
            .ThenBy(group => group.Sort)
            .Select(group => CreateGroup(catalog, groups, group))
            .ToArray();
    }

    private static MonitorGroup CreateGroup(
        IDeviceCatalog catalog,
        IReadOnlyList<DeviceGroup> groups,
        DeviceGroup group)
    {
        var type = GetMonitorGroupType(groups, group);
        var cameras = catalog.GetDevices(group.Id)
            .Where(device => device.Enabled)
            .OrderBy(device => device.Name)
            .SelectMany(device => device.Channels
                .Where(channel => channel.Enabled)
                .OrderBy(channel => channel.ChannelNo)
                .Select(channel => (Device: device, Channel: channel)))
            .Select((entry, index) => new CameraInfo(
                type == MonitorGroupType.Tunnel ? group.Name : entry.Device.Name,
                group.Name,
                index + 1,
                entry.Device.Status,
                DefaultBitrate(index + 1),
                ToDisplayStreamType(entry.Channel.StreamType))
            {
                DeviceId = entry.Device.Id,
                ChannelId = entry.Channel.Id
            })
            .ToArray();

        return new MonitorGroup(group.Name, type, cameras)
        {
            GroupId = group.Id
        };
    }

    private static MonitorGroupType GetMonitorGroupType(
        IReadOnlyList<DeviceGroup> groups,
        DeviceGroup group)
    {
        var rootName = groups
            .Single(parent => parent.Id == group.ParentId)
            .Name;
        return rootName switch
        {
            "卸矿站监控" => MonitorGroupType.UnloadingStation,
            "溜井监控" => MonitorGroupType.Chute,
            "巷道监控" => MonitorGroupType.Tunnel,
            _ => throw new ArgumentException($"未知的监控分组分类：{rootName}。", nameof(groups))
        };
    }

    private static int GetRootSort(
        IReadOnlyList<DeviceGroup> groups,
        Guid rootId) => groups.Single(group => group.Id == rootId).Sort;

    private static string DefaultBitrate(int channelNo) => channelNo switch
    {
        1 => "3.9 Mbps",
        2 => "4.2 Mbps",
        3 => "4.5 Mbps",
        _ => "-- Mbps"
    };

    private static string ToDisplayStreamType(StreamType streamType) => streamType switch
    {
        StreamType.Main => "主码流",
        StreamType.Sub => "辅码流",
        _ => "--"
    };
}
