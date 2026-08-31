using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;

namespace VideoMonitor.Wpf.Catalog;

public sealed class LegacyDeviceCatalogCommandService : IDeviceCatalogCommandService
{
    private readonly IDeviceCatalog catalog;

    public LegacyDeviceCatalogCommandService(IDeviceCatalog catalog)
    {
        this.catalog = catalog
            ?? throw new ArgumentNullException(nameof(catalog));
    }

    public bool CanWrite => true;

    public event EventHandler? AvailabilityChanged
    {
        add { }
        remove { }
    }

    public Task<DeviceGroupDto> CreateGroupAsync(
        CreateGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        catalog.AddGroup(new DeviceGroup
        {
            Id = request.Id,
            Name = request.Name,
            ParentId = request.ParentId,
            Sort = request.Sort,
            Enabled = request.Enabled,
            Kind = request.Kind
        });
        return Task.FromResult(MapGroup(catalog.GetGroups().Single(group => group.Id == request.Id)));
    }

    public Task<DeviceGroupDto> UpdateGroupAsync(
        Guid id,
        UpdateGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var group = catalog.GetGroups().SingleOrDefault(item => item.Id == id)
            ?? throw new CatalogApiException("GROUP_NOT_FOUND");
        group.Name = request.Name;
        group.ParentId = request.ParentId;
        group.Sort = request.Sort;
        group.Enabled = request.Enabled;
        group.Kind = request.Kind;
        catalog.UpdateGroup(group);
        return Task.FromResult(MapGroup(group));
    }

    public Task DeleteGroupAsync(
        Guid id,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!catalog.DeleteGroup(id))
        {
            throw new CatalogApiException("GROUP_NOT_FOUND");
        }

        return Task.CompletedTask;
    }

    public Task<CameraDeviceDto> CreateDeviceAsync(
        CreateDeviceRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var device = new CameraDevice
        {
            Id = request.Id,
            GroupId = request.GroupId,
            Name = request.Name,
            IpAddress = request.IpAddress,
            SdkPort = request.SdkPort,
            RtspPort = request.RtspPort,
            Username = request.Username,
            Password = request.Password,
            Manufacturer = request.Manufacturer,
            Model = request.Model,
            TransportMode = request.TransportMode,
            Enabled = request.Enabled,
            Remark = request.Remark
        };
        foreach (var channel in request.Channels)
        {
            device.Channels.Add(MapChannel(channel, device.Id, null));
        }

        catalog.AddDevice(device);
        return Task.FromResult(MapDevice(device));
    }

    public Task<CameraDeviceDto> UpdateDeviceAsync(
        Guid id,
        UpdateDeviceRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var device = catalog.GetDevice(id)
            ?? throw new CatalogApiException("DEVICE_NOT_FOUND");
        device.GroupId = request.GroupId;
        device.Name = request.Name;
        device.IpAddress = request.IpAddress;
        device.SdkPort = request.SdkPort;
        device.RtspPort = request.RtspPort;
        device.Username = request.Username;
        if (request.NewPassword is not null)
        {
            device.Password = request.NewPassword;
        }

        device.Manufacturer = request.Manufacturer;
        device.Model = request.Model;
        device.TransportMode = request.TransportMode;
        device.Enabled = request.Enabled;
        device.Remark = request.Remark;

        var existingChannels = device.Channels.ToDictionary(channel => channel.Id);
        device.Channels.Clear();
        foreach (var channel in request.Channels)
        {
            existingChannels.TryGetValue(channel.Id, out var existing);
            device.Channels.Add(MapChannel(channel, device.Id, existing));
        }

        catalog.UpdateDevice(device);
        return Task.FromResult(MapDevice(device));
    }

    public Task DeleteDeviceAsync(
        Guid id,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!catalog.DeleteDevice(id))
        {
            throw new CatalogApiException("DEVICE_NOT_FOUND");
        }

        return Task.CompletedTask;
    }

    private static DeviceGroupDto MapGroup(DeviceGroup group) => new(
        group.Id,
        group.Name,
        group.ParentId,
        group.Sort,
        group.Enabled,
        group.Kind,
        group.Revision);

    private static CameraDeviceDto MapDevice(CameraDevice device) => new(
        device.Id,
        device.GroupId,
        device.Name,
        device.IpAddress,
        device.SdkPort,
        device.RtspPort,
        device.Username,
        !string.IsNullOrEmpty(device.Password),
        device.Manufacturer,
        device.Model,
        device.TransportMode,
        device.Enabled,
        device.Remark,
        device.Revision,
        device.Channels.Select(channel => new CameraChannelDto(
            channel.Id,
            channel.DeviceId,
            channel.ChannelNo,
            channel.ChannelName,
            channel.StreamType,
            channel.Enabled)).ToArray());

    private static CameraChannel MapChannel(
        CameraChannelInput input,
        Guid deviceId,
        CameraChannel? existing) => new()
    {
        Id = input.Id,
        DeviceId = deviceId,
        ChannelNo = input.ChannelNo,
        ChannelName = input.ChannelName,
        StreamType = input.StreamType,
        StreamId = existing?.StreamId ?? string.Empty,
        Enabled = input.Enabled
    };
}
