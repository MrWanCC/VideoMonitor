using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Services;

public interface IDeviceCatalogStore
{
    Task<DeviceCatalogSnapshot?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        DeviceCatalogSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
