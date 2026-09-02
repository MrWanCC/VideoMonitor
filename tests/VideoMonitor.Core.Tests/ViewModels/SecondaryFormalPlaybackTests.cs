using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Playback;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class SecondaryFormalPlaybackTests
{
    [Fact]
    public async Task SecondaryMonitorViewModelDoesNotStartFormalPlaybackBeforeViewActivation()
    {
        var fixture = CreateFormalFixture();
        await using var viewModel = fixture.ViewModel;

        Assert.Equal(0, fixture.Provider.PrepareCount);

        await viewModel.ActivatePlaybackAsync();

        Assert.Equal(1, fixture.Provider.PrepareCount);
    }

    [Fact]
    public async Task SecondaryDeactivationStopsFormalPlaybackAndReactivationStartsOnce()
    {
        var fixture = CreateFormalFixture();
        await using var viewModel = fixture.ViewModel;

        await viewModel.ActivatePlaybackAsync();
        Assert.Equal(1, fixture.Provider.PrepareCount);

        await viewModel.DeactivatePlaybackAsync();

        Assert.All(
            viewModel.Tiles,
            tile => Assert.Equal(PlaybackState.Placeholder, tile.PlaybackState));

        await viewModel.ActivatePlaybackAsync();

        Assert.Equal(2, fixture.Provider.PrepareCount);
    }

    [Fact]
    public async Task CatalogChangeWhileInactiveDoesNotStartSecondaryPlayback()
    {
        var fixture = CreateFormalFixture();
        await using var viewModel = fixture.ViewModel;

        fixture.ReadModel.RaiseChanged();

        Assert.Equal(0, fixture.Provider.PrepareCount);
    }

    [Fact]
    public void LateWrapperFailureCannotOverwriteNewIdentity()
    {
        var source = ReadSource("SecondaryMonitorViewModel.cs");
        var wrapper = ExtractFormalPlaybackWrapper(source);

        Assert.DoesNotContain("tile.ShowError(", wrapper);
    }

    [Fact]
    public void CatalogRefreshPreservesSelectionByGuid()
    {
        var rootId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var readModel = new MutableReadModel(
        [
            new DeviceGroupDto(rootId, "Unloading", null, 0, true, MonitorGroupType.UnloadingStation, 1),
            new DeviceGroupDto(firstId, "A", rootId, 0, true, null, 1),
            new DeviceGroupDto(secondId, "B", rootId, 1, true, null, 1)
        ]);
        var groups = MonitorCatalogProjection.CreateGroups(readModel);
        var switchService = new MonitorSwitchService(groups);
        var viewModel = new SecondaryMonitorViewModel(switchService, readModel);

        viewModel.SelectGroupCommand.Execute(secondId);
        readModel.Replace(
        [
            new DeviceGroupDto(rootId, "Unloading renamed", null, 0, true, MonitorGroupType.UnloadingStation, 2),
            new DeviceGroupDto(firstId, "A renamed", rootId, 0, true, null, 2),
            new DeviceGroupDto(secondId, "B renamed", rootId, 1, true, null, 2)
        ]);
        readModel.RaiseChanged();

        Assert.Equal(secondId, viewModel.SelectedGroupId);
        Assert.Equal("B renamed", viewModel.CurrentGroupName);
    }

    private static (
        SecondaryMonitorViewModel ViewModel,
        CountingProvider Provider,
        MutableReadModel ReadModel) CreateFormalFixture()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var provider = new CountingProvider(deviceId, channelId);
        var readModel = new MutableReadModel(
        [
            new DeviceGroupDto(
                rootId,
                "Unloading",
                null,
                0,
                true,
                MonitorGroupType.UnloadingStation,
                1),
            new DeviceGroupDto(childId, "Child", rootId, 0, true, null, 1)
        ],
        [new CameraDeviceDto(
            deviceId,
            childId,
            "Camera",
            "192.0.2.10",
            8000,
            554,
            "safe-user",
            true,
            "Maker",
            "Model",
            TransportMode.Tcp,
            true,
            "",
            1,
            [new CameraChannelDto(
                channelId,
                deviceId,
                1,
                "Main",
                StreamType.Main,
                true)])]);

        return (
            new SecondaryMonitorViewModel(
                new MonitorSwitchService(Array.Empty<MonitorGroup>()),
                readModel,
                tile => new FormalPlaybackCoordinator(
                    provider,
                    (source, _) => new PlaybackSession(
                        new PlaybackSource(
                            source.ChannelId,
                            source.StreamId,
                            source.PlaybackUrl,
                            null,
                            false),
                        null,
                        null),
                    session => session.Dispose(),
                    tile,
                    new ImmediateDispatcher())),
            provider,
            readModel);
    }

    private sealed class CountingProvider : IFormalPlaybackSourceProvider
    {
        private readonly Guid deviceId;
        private readonly Guid channelId;

        public CountingProvider(Guid deviceId, Guid channelId)
        {
            this.deviceId = deviceId;
            this.channelId = channelId;
        }

        public int PrepareCount { get; private set; }

        public Task<FormalPlaybackSource> PrepareAsync(
            Guid deviceId,
            Guid channelId,
            StreamType streamType,
            CancellationToken cancellationToken = default)
        {
            PrepareCount++;
            return Task.FromResult(new FormalPlaybackSource(
                this.deviceId,
                this.channelId,
                "formal-stream",
                new Uri("https://server-b/live/formal-stream"),
                DateTimeOffset.UtcNow.AddMinutes(1)));
        }

        public Task ReleaseAsync(
            FormalPlaybackSource source,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(
            Action action,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class MutableReadModel : IDeviceCatalogReadModel
    {
        private IReadOnlyList<DeviceGroupDto> groups;
        private readonly IReadOnlyList<CameraDeviceDto> devices;

        public MutableReadModel(
            IReadOnlyList<DeviceGroupDto> groups,
            IReadOnlyList<CameraDeviceDto>? devices = null)
        {
            this.groups = groups;
            this.devices = devices ?? [];
        }

        public event EventHandler? Changed;

        public IReadOnlyList<DeviceGroupDto> GetGroups() => groups;

        public IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId) =>
            devices.Where(device => device.GroupId == groupId).ToArray();

        public CameraDeviceDto? GetDevice(Guid deviceId) =>
            devices.SingleOrDefault(device => device.Id == deviceId);

        public void Replace(IReadOnlyList<DeviceGroupDto> next) => groups = next;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string ReadSource(string fileName) =>
        File.ReadAllText(Path.Combine(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..")),
            "src",
            "VideoMonitor.Wpf",
            "ViewModels",
            fileName));

    private static string ExtractFormalPlaybackWrapper(string source)
    {
        const string startMarker = "private async Task StartFormalPlaybackAsync";
        const string endMarker = "private async Task StopFormalPlaybackAsync";
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}
