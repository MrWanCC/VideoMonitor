using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;

namespace VideoMonitor.Infrastructure.Persistence;

public interface ICentralCatalogRepository
{
    Task<CatalogSnapshotDto> GetCatalogAsync(
        CancellationToken cancellationToken = default);

    Task<DeviceGroupDto?> GetGroupAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<CameraDeviceDto?> GetDeviceAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<CatalogRepositoryResult<DeviceGroupDto>> CreateGroupAsync(
        DeviceGroup group,
        CancellationToken cancellationToken = default);

    Task<CatalogRepositoryResult<CameraDeviceDto>> CreateDeviceAsync(
        CameraDevice device,
        CancellationToken cancellationToken = default);
}
