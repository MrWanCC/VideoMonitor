using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Services;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.Services;

public sealed class DeviceCatalogPersistenceIntegrationTests
{
    [Fact]
    public async Task DeviceManagementChangesPersistThroughRealCatalogCoordinatorAndJsonStore()
    {
        using var directory = new TemporaryDirectory();
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var store = new JsonDeviceCatalogStore(directory.PathOf("device-catalog.json"));
        await using var coordinator = new DeviceCatalogPersistenceCoordinator(catalog, store);
        var viewModel = new DeviceManagementViewModel(catalog);
        var root = viewModel.Groups.Single(group => group.Name == "溜井监控");

        viewModel.BeginAddGroupCommand.Execute(root);
        viewModel.EditingGroupName = "集成测试分组";
        viewModel.CommitGroupEditCommand.Execute(null);
        await coordinator.FlushAsync();

        var added = (await store.LoadAsync())!.Groups.Single(
            group => group.Name == "集成测试分组");

        viewModel.BeginRenameGroupCommand.Execute(added);
        viewModel.EditingGroupName = "集成测试分组-已修改";
        viewModel.CommitGroupEditCommand.Execute(null);
        await coordinator.FlushAsync();

        var renamed = (await store.LoadAsync())!.Groups.Single(
            group => group.Name == "集成测试分组-已修改");
        viewModel.DeleteGroupCommand.Execute(renamed);
        viewModel.ConfirmDialogCommand.Execute(null);
        await coordinator.FlushAsync();

        var finalSnapshot = await store.LoadAsync();
        Assert.NotNull(finalSnapshot);
        Assert.DoesNotContain(
            finalSnapshot.Groups,
            group => group.Id == added.Id);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"VideoMonitor.DeviceCatalogIntegration.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string PathOf(string fileName) => System.IO.Path.Combine(Path, fileName);

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
