using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Services;

public static class MonitorCatalogProjection
{
    public static IReadOnlyList<MonitorGroup> CreateGroups(
        IDeviceCatalogReadModel catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var groups = catalog.GetGroups();
        var roots = groups
            .Where(IsFormalRoot)
            .ToDictionary(group => group.Id);

        return groups
            .Where(child => child.Enabled
                && child.ParentId is { } parentId
                && roots.ContainsKey(parentId))
            .Select(child => CreateGroup(catalog, roots[child.ParentId!.Value], child))
            .OrderBy(group => group.RootSort)
            .ThenBy(group => group.RootGroupId)
            .ThenBy(group => group.Sort)
            .ThenBy(group => group.GroupId)
            .ToArray();
    }

    // Compatibility overload for the pre-central local WPF path. Central callers
    // must use the password-safe IDeviceCatalogReadModel overload above.
    public static IReadOnlyList<MonitorGroup> CreateGroups(IDeviceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var groups = catalog.GetGroups();
        return groups
            .Where(group => group.ParentId is not null)
            .OrderBy(group => GetRootSort(groups, group.ParentId!.Value))
            .ThenBy(group => group.Sort)
            .Select(group => CreateLegacyGroup(catalog, groups, group))
            .ToArray();
    }

    private static MonitorGroup CreateGroup(
        IDeviceCatalogReadModel catalog,
        DeviceGroupDto root,
        DeviceGroupDto child)
    {
        var cameras = (catalog.GetDevices(child.Id) ?? [])
            .Where(device => device.Enabled)
            .OrderBy(device => device.Name, StringComparer.Ordinal)
            .ThenBy(device => device.Id)
            .SelectMany(device => (device.Channels ?? [])
                .Where(channel => channel.Enabled)
                .OrderBy(channel => channel.ChannelNo)
                .ThenBy(channel => channel.Id)
                .Select(channel => (Device: device, Channel: channel)))
            .Select((entry, index) => new CameraInfo(
                root.Kind == MonitorGroupType.Tunnel
                    ? child.Name
                    : entry.Device.Name,
                child.Name,
                index + 1,
                CameraStatus.Unknown,
                DefaultBitrate(index + 1),
                ToDisplayStreamType(entry.Channel.StreamType))
            {
                DeviceId = entry.Device.Id,
                ChannelId = entry.Channel.Id
            })
            .ToArray();

        return new MonitorGroup(child.Name, root.Kind!.Value, cameras)
        {
            GroupId = child.Id,
            RootGroupId = root.Id,
            RootName = root.Name,
            RootSort = root.Sort,
            Sort = child.Sort
        };
    }

    private static MonitorGroup CreateLegacyGroup(
        IDeviceCatalog catalog,
        IReadOnlyList<DeviceGroup> groups,
        DeviceGroup group)
    {
        var type = GetMonitorGroupType(groups, group);
        var root = groups.Single(parent => parent.Id == group.ParentId);
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
            GroupId = group.Id,
            RootGroupId = root.Id,
            RootName = root.Name,
            RootSort = root.Sort,
            Sort = group.Sort
        };
    }

    private static bool IsFormalRoot(DeviceGroupDto group) =>
        group.ParentId is null
        && group.Enabled
        && group.Kind is { } kind
        && Enum.IsDefined(kind);

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
