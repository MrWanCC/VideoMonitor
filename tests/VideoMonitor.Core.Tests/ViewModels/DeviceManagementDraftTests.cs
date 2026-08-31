using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class DeviceManagementDraftTests
{
    [Fact]
    public void ExistingPassword_IsNeverLoadedIntoDraft()
    {
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(ExistingDevice()),
            new FakeCatalogCommandService());

        Assert.Empty(viewModel.EditDraft.Password);
    }

    [Fact]
    public async Task BlankPassword_MapsToNoPasswordChange()
    {
        var commands = new FakeCatalogCommandService();
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(ExistingDevice()),
            commands);
        viewModel.EditDraft.Password = string.Empty;

        await viewModel.SaveDeviceCommand.ExecuteAsync(null);

        Assert.Null(commands.LastUpdate!.NewPassword);
    }

    [Fact]
    public async Task Conflict_RetainsDraft()
    {
        var commands = new FakeCatalogCommandService
        {
            NextFailure = new CatalogApiException("DEVICE_REVISION_CONFLICT", 9)
        };
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(ExistingDevice()),
            commands);
        viewModel.EditDraft.Name = "Unsubmitted";

        await viewModel.SaveDeviceCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasUnsavedDraft);
        Assert.Equal("Unsubmitted", viewModel.EditDraft.Name);
        Assert.Equal("DEVICE_REVISION_CONFLICT", viewModel.OperationErrorCode);
        Assert.False(viewModel.LastOperationSucceeded);
    }

    [Fact]
    public async Task AmbiguousUpdate_SetsSafeErrorAndRetainsDraft()
    {
        var commands = new FakeCatalogCommandService
        {
            NextFailure = new CatalogMutationUncertainException(
                "update-device",
                Guid.NewGuid())
        };
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(ExistingDevice()),
            commands);
        viewModel.EditDraft.Password = "new-secret";

        await viewModel.SaveDeviceCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasUnsavedDraft);
        Assert.True(viewModel.HasOperationError);
        Assert.False(viewModel.LastOperationSucceeded);
        Assert.Equal("CATALOG_MUTATION_UNCERTAIN", viewModel.OperationErrorCode);
        Assert.DoesNotContain("new-secret", viewModel.OperationError ?? string.Empty);
    }

    [Fact]
    public async Task CancelEdit_DoesNotWriteAndClearsDraft()
    {
        var commands = new FakeCatalogCommandService();
        var viewModel = new DeviceManagementViewModel(
            new DeviceReadModelStub(ExistingDevice()),
            commands);
        viewModel.EditDraft.Name = "Unsubmitted";

        viewModel.CancelEditCommand.Execute(null);
        await Task.CompletedTask;

        Assert.Equal(0, commands.WriteCount);
        Assert.True(viewModel.HasUnsavedDraft);
        Assert.Equal("Unsubmitted", viewModel.EditDraft.Name);
    }

    private static CameraDeviceDto ExistingDevice() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Camera",
            "192.0.2.10",
            8000,
            554,
            "user",
            true,
            "Maker",
            "Model",
            TransportMode.Tcp,
            true,
            "remark",
            8,
            []);

    private sealed class DeviceReadModelStub : IDeviceCatalogReadModel
    {
        private readonly CameraDeviceDto device;

        public DeviceReadModelStub(CameraDeviceDto device) => this.device = device;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public IReadOnlyList<DeviceGroupDto> GetGroups() => [];

        public IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId) => [device];

        public CameraDeviceDto? GetDevice(Guid deviceId) =>
            device.Id == deviceId ? device : null;
    }

    private sealed class FakeCatalogCommandService : IDeviceCatalogCommandService
    {
        public UpdateDeviceRequest? LastUpdate { get; private set; }

        public Exception? NextFailure { get; init; }

        public int WriteCount { get; private set; }

        public bool CanWrite => true;

        public event EventHandler? AvailabilityChanged
        {
            add { }
            remove { }
        }

        public Task<DeviceGroupDto> CreateGroupAsync(
            CreateGroupRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<DeviceGroupDto>(null!);

        public Task<DeviceGroupDto> UpdateGroupAsync(
            Guid id,
            UpdateGroupRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<DeviceGroupDto>(null!);

        public Task DeleteGroupAsync(
            Guid id,
            long expectedRevision,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<CameraDeviceDto> CreateDeviceAsync(
            CreateDeviceRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CameraDeviceDto>(null!);

        public Task<CameraDeviceDto> UpdateDeviceAsync(
            Guid id,
            UpdateDeviceRequest request,
            CancellationToken cancellationToken = default)
        {
            WriteCount++;
            LastUpdate = request;
            return NextFailure is null
                ? Task.FromResult<CameraDeviceDto>(null!)
                : Task.FromException<CameraDeviceDto>(NextFailure);
        }

        public Task DeleteDeviceAsync(
            Guid id,
            long expectedRevision,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
