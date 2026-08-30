using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Services;

public sealed class InMemoryDeviceCatalog : IDeviceCatalog
{
    private readonly List<DeviceGroup> groups;
    private readonly List<CameraDevice> devices;

    public InMemoryDeviceCatalog(
        IEnumerable<DeviceGroup> groups,
        IEnumerable<CameraDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(devices);

        this.groups = groups.ToList();
        this.devices = devices.ToList();
        ValidateInitialData();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<DeviceGroup> GetGroups() => groups.ToArray();

    public IReadOnlyList<CameraDevice> GetDevices(Guid groupId) => devices
        .Where(device => device.GroupId == groupId)
        .ToArray();

    public CameraDevice? GetDevice(Guid deviceId) => devices
        .SingleOrDefault(device => device.Id == deviceId);

    public void AddGroup(DeviceGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        ValidateGroup(group);
        if (groups.Any(item => item.Id == group.Id))
        {
            throw new InvalidOperationException("设备分组ID已存在。");
        }

        groups.Add(group);
        OnChanged();
    }

    public void UpdateGroup(DeviceGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        ValidateGroup(group);
        var existing = FindGroup(group.Id);
        if (existing is null)
        {
            throw new KeyNotFoundException("设备分组不存在。");
        }

        existing.Name = group.Name;
        existing.Revision = group.Revision;
        existing.ParentId = group.ParentId;
        existing.Sort = group.Sort;
        existing.Enabled = group.Enabled;
        OnChanged();
    }

    public bool DeleteGroup(Guid groupId)
    {
        var group = FindGroup(groupId);
        if (group is null
            || group.ParentId is null
            || groups.Any(item => item.ParentId == groupId)
            || devices.Any(device => device.GroupId == groupId))
        {
            return false;
        }

        groups.Remove(group);
        OnChanged();
        return true;
    }

    public void AddDevice(CameraDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        ValidateDevice(device);
        if (devices.Any(item => item.Id == device.Id))
        {
            throw new InvalidOperationException("设备ID已存在。");
        }

        devices.Add(device);
        OnChanged();
    }

    public void UpdateDevice(CameraDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        ValidateDevice(device);
        var existing = GetDevice(device.Id);
        if (existing is null)
        {
            throw new KeyNotFoundException("设备不存在。");
        }

        if (!ReferenceEquals(existing, device))
        {
            CopyDevice(existing, device);
        }

        OnChanged();
    }

    public bool DeleteDevice(Guid deviceId)
    {
        var device = GetDevice(deviceId);
        if (device is null)
        {
            return false;
        }

        devices.Remove(device);
        OnChanged();
        return true;
    }

    private DeviceGroup? FindGroup(Guid groupId) => groups
        .SingleOrDefault(group => group.Id == groupId);

    private void ValidateInitialData()
    {
        if (groups.Any(group => group.Id == Guid.Empty)
            || groups.Select(group => group.Id).Distinct().Count() != groups.Count)
        {
            throw new ArgumentException("设备分组必须具有唯一的稳定ID。", nameof(groups));
        }

        if (devices.Select(device => device.Id).Distinct().Count() != devices.Count)
        {
            throw new ArgumentException("设备必须具有唯一的稳定ID。", nameof(devices));
        }

        var channels = devices.SelectMany(device => device.Channels);
        if (channels.Select(channel => channel.Id).Distinct().Count() != channels.Count())
        {
            throw new ArgumentException("设备通道必须具有全局唯一的稳定ID。", nameof(devices));
        }

        foreach (var group in groups)
        {
            ValidateGroup(group);
        }

        foreach (var device in devices)
        {
            ValidateDevice(device);
        }
    }

    private void ValidateGroup(DeviceGroup group)
    {
        if (group.Id == Guid.Empty)
        {
            throw new ArgumentException("设备分组必须具有稳定ID。", nameof(group));
        }

        if (group.ParentId is { } parentId && FindGroup(parentId) is null)
        {
            throw new ArgumentException("设备分组的父分组不存在。", nameof(group));
        }
    }

    private void ValidateDevice(CameraDevice device)
    {
        if (device.Id == Guid.Empty)
        {
            throw new ArgumentException("设备必须具有稳定ID。", nameof(device));
        }

        if (FindGroup(device.GroupId) is null)
        {
            throw new ArgumentException("设备所属分组不存在。", nameof(device));
        }

        var channelIds = new HashSet<Guid>();
        foreach (var channel in device.Channels)
        {
            if (channel.Id == Guid.Empty
                || channel.DeviceId != device.Id
                || !channelIds.Add(channel.Id))
            {
                throw new ArgumentException("设备通道必须具有唯一且匹配设备的稳定ID。", nameof(device));
            }

            if (devices.Any(existing => existing.Id != device.Id
                && existing.Channels.Any(existingChannel => existingChannel.Id == channel.Id)))
            {
                throw new ArgumentException("设备通道必须具有全局唯一的稳定ID。", nameof(device));
            }
        }
    }

    private static void CopyDevice(CameraDevice target, CameraDevice source)
    {
        target.Revision = source.Revision;
        target.Name = source.Name;
        target.GroupId = source.GroupId;
        target.IpAddress = source.IpAddress;
        target.SdkPort = source.SdkPort;
        target.RtspPort = source.RtspPort;
        target.Username = source.Username;
        target.Password = source.Password;
        target.Manufacturer = source.Manufacturer;
        target.Model = source.Model;
        target.TransportMode = source.TransportMode;
        target.Status = source.Status;
        target.Enabled = source.Enabled;
        target.Remark = source.Remark;

        var sourceChannels = source.Channels.ToArray();
        var sourceIds = sourceChannels.Select(channel => channel.Id).ToHashSet();
        target.Channels.RemoveAll(channel => !sourceIds.Contains(channel.Id));
        foreach (var sourceChannel in sourceChannels)
        {
            var targetChannel = target.Channels
                .FirstOrDefault(channel => channel.Id == sourceChannel.Id);
            if (targetChannel is null)
            {
                targetChannel = new CameraChannel { Id = sourceChannel.Id };
                target.Channels.Add(targetChannel);
            }

            targetChannel.DeviceId = sourceChannel.DeviceId;
            targetChannel.ChannelNo = sourceChannel.ChannelNo;
            targetChannel.ChannelName = sourceChannel.ChannelName;
            targetChannel.StreamType = sourceChannel.StreamType;
            targetChannel.StreamId = sourceChannel.StreamId;
            targetChannel.Enabled = sourceChannel.Enabled;
        }
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
