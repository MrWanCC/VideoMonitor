using VideoMonitor.Core.Catalog;

namespace VideoMonitor.Wpf.Catalog;

public interface IDeviceCatalogCommandService
{
    bool CanWrite { get; }

    event EventHandler? AvailabilityChanged;

    Task<DeviceGroupDto> CreateGroupAsync(
        CreateGroupRequest request,
        CancellationToken cancellationToken = default);

    Task<DeviceGroupDto> UpdateGroupAsync(
        Guid id,
        UpdateGroupRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteGroupAsync(
        Guid id,
        long expectedRevision,
        CancellationToken cancellationToken = default);

    Task<CameraDeviceDto> CreateDeviceAsync(
        CreateDeviceRequest request,
        CancellationToken cancellationToken = default);

    Task<CameraDeviceDto> UpdateDeviceAsync(
        Guid id,
        UpdateDeviceRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteDeviceAsync(
        Guid id,
        long expectedRevision,
        CancellationToken cancellationToken = default);
}
