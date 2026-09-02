using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Playback;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class FormalMonitorPlaybackTests
{
    [Fact]
    public void LateWrapperFailureCannotOverwriteNewIdentity()
    {
        var source = ReadSource("MonitorViewModel.cs");
        var wrapper = ExtractFormalPlaybackWrapper(source);

        Assert.DoesNotContain("tile.ShowError(", wrapper);
    }

    [Fact]
    public void ProjectionUsesCatalogDtoIdsOnly()
    {
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var channelId = Guid.NewGuid();
        var provider = new CapturingProvider(deviceId, channelId);
        var readModel = new CatalogReadModel(
            [
                new DeviceGroupDto(rootId, "Chute", null, 0, true, MonitorGroupType.Chute, 1),
                new DeviceGroupDto(childId, "Child", rootId, 0, true, null, 1)
            ],
            [Device(deviceId, childId, channelId)]);

        var viewModel = new MonitorViewModel(
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
                new ImmediateDispatcher()));

        var tile = Assert.Single(viewModel.MainTiles.Where(item => item.CurrentDeviceId == deviceId));
        Assert.Equal(deviceId, tile.CurrentDeviceId);
        Assert.Equal(channelId, tile.CurrentChannelId);
        Assert.Equal(deviceId, provider.LastDeviceId);
        Assert.Equal(channelId, provider.LastChannelId);
    }

    [Fact]
    public async Task ChangedIdentityCannotBeOverwrittenByOlderCompletion()
    {
        var rootId = Guid.NewGuid();
        var oldGroupId = Guid.NewGuid();
        var newGroupId = Guid.NewGuid();
        var oldDeviceId = Guid.NewGuid();
        var newDeviceId = Guid.NewGuid();
        var oldChannelId = Guid.NewGuid();
        var newChannelId = Guid.NewGuid();
        var provider = new SwitchingProvider(oldDeviceId, newDeviceId);
        var readModel = new CatalogReadModel(
            [
                new DeviceGroupDto(
                    rootId,
                    "Chute Root",
                    null,
                    0,
                    true,
                    MonitorGroupType.Chute,
                    1),
                new DeviceGroupDto(oldGroupId, "Old", rootId, 0, true, null, 1),
                new DeviceGroupDto(newGroupId, "New", rootId, 1, true, null, 1)
            ],
            [
                Device(oldDeviceId, oldGroupId, oldChannelId),
                Device(newDeviceId, newGroupId, newChannelId)
            ]);
        var switchService = new MonitorSwitchService(Array.Empty<MonitorGroup>());
        await using var viewModel = new MonitorViewModel(
            switchService,
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
                new ImmediateDispatcher()));

        await provider.OldPrepareStarted.Task;
        switchService.SwitchChuteGroup(newGroupId);
        var tile = viewModel.MainTiles[0];
        Assert.Equal(newDeviceId, tile.CurrentDeviceId);
        Assert.Equal(newChannelId, tile.CurrentChannelId);
        provider.ReleaseOldPrepare();
        await provider.NewPrepareStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(20);

        Assert.Equal(newDeviceId, tile.CurrentDeviceId);
        Assert.Equal(newChannelId, tile.CurrentChannelId);
        Assert.Equal(StreamType.Main, tile.CurrentStreamType);
    }

    private static CameraDeviceDto Device(Guid deviceId, Guid groupId, Guid channelId) =>
        new(
            deviceId,
            groupId,
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
            [new CameraChannelDto(channelId, deviceId, 1, "Main", StreamType.Main, true)]);

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

    private sealed class CapturingProvider : IFormalPlaybackSourceProvider
    {
        private readonly Guid deviceId;
        private readonly Guid channelId;

        public CapturingProvider(Guid deviceId, Guid channelId)
        {
            this.deviceId = deviceId;
            this.channelId = channelId;
        }

        public Guid? LastDeviceId { get; private set; }

        public Guid? LastChannelId { get; private set; }

        public Task<FormalPlaybackSource> PrepareAsync(
            Guid deviceId,
            Guid channelId,
            StreamType streamType,
            CancellationToken cancellationToken = default)
        {
            LastDeviceId = deviceId;
            LastChannelId = channelId;
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

    private sealed class SwitchingProvider : IFormalPlaybackSourceProvider
    {
        private readonly Guid oldDeviceId;
        private readonly Guid newDeviceId;
        private readonly TaskCompletionSource<FormalPlaybackSource> oldPrepare =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SwitchingProvider(Guid oldDeviceId, Guid newDeviceId)
        {
            this.oldDeviceId = oldDeviceId;
            this.newDeviceId = newDeviceId;
        }

        public TaskCompletionSource<bool> OldPrepareStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> NewPrepareStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<FormalPlaybackSource> PrepareAsync(
            Guid deviceId,
            Guid channelId,
            StreamType streamType,
            CancellationToken cancellationToken = default)
        {
            if (deviceId == oldDeviceId)
            {
                OldPrepareStarted.TrySetResult(true);
                return CompleteOldPrepareAsync(channelId);
            }

            Assert.Equal(newDeviceId, deviceId);
            NewPrepareStarted.TrySetResult(true);
            return Task.FromResult(Source(deviceId, channelId));
        }

        public Task ReleaseAsync(
            FormalPlaybackSource source,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void ReleaseOldPrepare() =>
            oldPrepare.TrySetResult(Source(oldDeviceId, Guid.Empty));

        private async Task<FormalPlaybackSource> CompleteOldPrepareAsync(Guid channelId)
        {
            var source = await oldPrepare.Task.ConfigureAwait(false);
            return source with { ChannelId = channelId };
        }

        private static FormalPlaybackSource Source(Guid deviceId, Guid channelId) => new(
            deviceId,
            channelId,
            "stream-" + deviceId.ToString("N"),
            new Uri("https://server-b/live/" + deviceId.ToString("N")),
            DateTimeOffset.UtcNow.AddMinutes(1));
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

    private sealed class CatalogReadModel : IDeviceCatalogReadModel
    {
        public CatalogReadModel(
            IReadOnlyList<DeviceGroupDto> groups,
            IReadOnlyList<CameraDeviceDto> devices)
        {
            Groups = groups;
            Devices = devices;
        }

        public IReadOnlyList<DeviceGroupDto> Groups { get; }

        public IReadOnlyList<CameraDeviceDto> Devices { get; }

        public event EventHandler? Changed;

        public IReadOnlyList<DeviceGroupDto> GetGroups() => Groups;

        public IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId) =>
            Devices.Where(device => device.GroupId == groupId).ToArray();

        public CameraDeviceDto? GetDevice(Guid deviceId) =>
            Devices.SingleOrDefault(device => device.Id == deviceId);

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
