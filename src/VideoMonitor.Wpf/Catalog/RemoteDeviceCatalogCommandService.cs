using VideoMonitor.Core.Catalog;

namespace VideoMonitor.Wpf.Catalog;

public sealed class RemoteDeviceCatalogCommandService : IDeviceCatalogCommandService, IDisposable
{
    private readonly IDeviceCatalogReadModel catalog;
    private readonly CatalogApiClient apiClient;
    private readonly ServerConnectionCoordinator coordinator;
    private bool disposed;

    public RemoteDeviceCatalogCommandService(
        IDeviceCatalogReadModel catalog,
        CatalogApiClient apiClient,
        ServerConnectionCoordinator coordinator)
    {
        this.catalog = catalog
            ?? throw new ArgumentNullException(nameof(catalog));
        this.apiClient = apiClient
            ?? throw new ArgumentNullException(nameof(apiClient));
        this.coordinator = coordinator
            ?? throw new ArgumentNullException(nameof(coordinator));
        this.coordinator.StatusChanged += OnCoordinatorStatusChanged;
    }

    public bool CanWrite =>
        !disposed
        && coordinator.Status.State == ServerConnectionState.Connected
        && coordinator.Status.BaseUri is not null;

    public event EventHandler? AvailabilityChanged;

    public async Task<DeviceGroupDto> CreateGroupAsync(
        CreateGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        var endpoint = GetConnectedEndpoint();
        try
        {
            var result = await apiClient.CreateGroupAsync(
                    endpoint,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            await RefreshAndRequireConnectionAsync(
                    "create-group",
                    request.Id,
                    cancellationToken)
                .ConfigureAwait(false);
            return result;
        }
        catch (CatalogApiException exception) when (IsAmbiguous(exception))
        {
            if (!await TryRefreshForUncertaintyAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new CatalogMutationUncertainException("create-group", request.Id, exception);
            }

            var confirmed = catalog.GetGroups().SingleOrDefault(group => group.Id == request.Id);
            if (confirmed is not null)
            {
                return confirmed;
            }

            throw new CatalogMutationUncertainException(
                "create-group",
                request.Id,
                exception);
        }
    }

    public async Task<DeviceGroupDto> UpdateGroupAsync(
        Guid id,
        UpdateGroupRequest request,
        CancellationToken cancellationToken = default)
    {
        var endpoint = GetConnectedEndpoint();
        try
        {
            var result = await apiClient.UpdateGroupAsync(
                    endpoint,
                    id,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            await RefreshAndRequireConnectionAsync(
                    "update-group",
                    id,
                    cancellationToken)
                .ConfigureAwait(false);
            return result;
        }
        catch (CatalogApiException exception) when (IsAmbiguous(exception))
        {
            await TryRefreshForUncertaintyAsync(cancellationToken).ConfigureAwait(false);
            throw new CatalogMutationUncertainException("update-group", id, exception);
        }
    }

    public async Task DeleteGroupAsync(
        Guid id,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var endpoint = GetConnectedEndpoint();
        try
        {
            await apiClient.DeleteGroupAsync(
                    endpoint,
                    id,
                    expectedRevision,
                    cancellationToken)
                .ConfigureAwait(false);
            await RefreshAndRequireConnectionAsync(
                    "delete-group",
                    id,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CatalogApiException exception) when (IsAmbiguous(exception))
        {
            if (await TryRefreshForUncertaintyAsync(cancellationToken).ConfigureAwait(false)
                && !catalog.GetGroups().Any(group => group.Id == id))
            {
                return;
            }

            throw new CatalogMutationUncertainException("delete-group", id, exception);
        }
    }

    public async Task<CameraDeviceDto> CreateDeviceAsync(
        CreateDeviceRequest request,
        CancellationToken cancellationToken = default)
    {
        var endpoint = GetConnectedEndpoint();
        try
        {
            var result = await apiClient.CreateDeviceAsync(
                    endpoint,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            await RefreshAndRequireConnectionAsync(
                    "create-device",
                    request.Id,
                    cancellationToken)
                .ConfigureAwait(false);
            return result;
        }
        catch (CatalogApiException exception) when (IsAmbiguous(exception))
        {
            if (!await TryRefreshForUncertaintyAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new CatalogMutationUncertainException("create-device", request.Id, exception);
            }

            var confirmed = catalog.GetDevice(request.Id);
            if (confirmed is not null)
            {
                return confirmed;
            }

            throw new CatalogMutationUncertainException(
                "create-device",
                request.Id,
                exception);
        }
    }

    public async Task<CameraDeviceDto> UpdateDeviceAsync(
        Guid id,
        UpdateDeviceRequest request,
        CancellationToken cancellationToken = default)
    {
        var endpoint = GetConnectedEndpoint();
        try
        {
            var result = await apiClient.UpdateDeviceAsync(
                    endpoint,
                    id,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            await RefreshAndRequireConnectionAsync(
                    "update-device",
                    id,
                    cancellationToken)
                .ConfigureAwait(false);
            return result;
        }
        catch (CatalogApiException exception) when (IsAmbiguous(exception))
        {
            await TryRefreshForUncertaintyAsync(cancellationToken).ConfigureAwait(false);
            throw new CatalogMutationUncertainException("update-device", id, exception);
        }
    }

    public async Task DeleteDeviceAsync(
        Guid id,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var endpoint = GetConnectedEndpoint();
        try
        {
            await apiClient.DeleteDeviceAsync(
                    endpoint,
                    id,
                    expectedRevision,
                    cancellationToken)
                .ConfigureAwait(false);
            await RefreshAndRequireConnectionAsync(
                    "delete-device",
                    id,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CatalogApiException exception) when (IsAmbiguous(exception))
        {
            if (await TryRefreshForUncertaintyAsync(cancellationToken).ConfigureAwait(false)
                && catalog.GetDevice(id) is null)
            {
                return;
            }

            throw new CatalogMutationUncertainException("delete-device", id, exception);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        coordinator.StatusChanged -= OnCoordinatorStatusChanged;
    }

    private Uri GetConnectedEndpoint()
    {
        if (!CanWrite || coordinator.Status.BaseUri is not { } endpoint)
        {
            throw new CatalogApiException("CATALOG_UNAVAILABLE");
        }

        return endpoint;
    }

    private async Task RefreshAndRequireConnectionAsync(
        string operation,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        await coordinator.RefreshNowAsync(cancellationToken).ConfigureAwait(false);
        if (!CanWrite)
        {
            throw new CatalogMutationUncertainException(operation, entityId);
        }
    }

    private async Task<bool> TryRefreshForUncertaintyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await coordinator.RefreshNowAsync(cancellationToken).ConfigureAwait(false);
            return CanWrite;
        }
        catch (CatalogApiException exception) when (IsAmbiguous(exception))
        {
            return false;
        }
    }

    private void OnCoordinatorStatusChanged(object? sender, EventArgs e) =>
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);

    private static bool IsAmbiguous(CatalogApiException exception) =>
        exception.Code == "CATALOG_UNAVAILABLE";
}
