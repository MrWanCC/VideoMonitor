using VideoMonitor.Core.Mock;
using VideoMonitor.Core.Services;
using VideoMonitor.Wpf.Playback;

namespace VideoMonitor.Core.Tests.Playback;

public sealed class PlaybackCompositionTests
{
    [Fact]
    public void SelectDevice_SelectsOnlyWest401FirstPhysicalCamera()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);
        var selection = SingleCameraPlaybackComposition.SelectDevice(
            catalog,
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            Guid.Parse("60000000-0000-0000-0000-000000000001"));

        Assert.Equal(
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            selection.Device.Id);
        Assert.Equal("西401溜井 · 通道1", selection.Device.Name);
        Assert.Equal(
            Guid.Parse("60000000-0000-0000-0000-000000000001"),
            selection.Channel.Id);
        Assert.Single(selection.Device.Channels);
    }

    [Fact]
    public void SelectDevice_WhenDeviceDoesNotExist_Throws()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SingleCameraPlaybackComposition.SelectDevice(
                catalog,
                Guid.Parse("50000000-0000-0000-0000-000000000099"),
                Guid.Parse("60000000-0000-0000-0000-000000000001")));

        Assert.Contains("设备不存在", exception.Message);
    }

    [Fact]
    public void SelectDevice_WhenChannelDoesNotExist_Throws()
    {
        var data = MockDeviceData.Create();
        var catalog = new InMemoryDeviceCatalog(data.Groups, data.Devices);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SingleCameraPlaybackComposition.SelectDevice(
                catalog,
                Guid.Parse("50000000-0000-0000-0000-000000000001"),
                Guid.Parse("60000000-0000-0000-0000-000000000099")));

        Assert.Contains("通道不存在", exception.Message);
    }

    [Fact]
    public async Task PlaybackSessionDisposeAsyncUsesAsyncDiagnostics()
    {
        var diagnostics = new AsyncOnlyDiagnostics();
        var session = new PlaybackSession(
            new PlaybackSource(
                Guid.NewGuid(),
                "stream-async-dispose",
                new Uri("https://server/live/stream-async-dispose"),
                null,
                false),
            null,
            null,
            diagnostics: diagnostics);

        var disposeTask = session.DisposeAsync().AsTask();
        await diagnostics.AsyncDisposeStarted.Task;

        Assert.False(disposeTask.IsCompleted);
        Assert.Equal(0, diagnostics.SyncDisposeCalls);

        diagnostics.ReleaseAsyncDispose();
        await disposeTask;

        Assert.Equal(1, diagnostics.AsyncDisposeCalls);
    }

    [Fact]
    public void AppStartupFailureUsesAsyncVlcCleanup()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var sourcePath = Path.Combine(
            repositoryRoot,
            "src",
            "VideoMonitor.Wpf",
            "App.xaml.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("vlcPlaybackService?.Dispose();", source);
        Assert.Contains("await vlcPlaybackService.DisposeAsync();", source);
    }

    private sealed class AsyncOnlyDiagnostics : IDisposable, IAsyncDisposable
    {
        public TaskCompletionSource<object?> AsyncDisposeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<object?> release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SyncDisposeCalls { get; private set; }

        public int AsyncDisposeCalls { get; private set; }

        public void Dispose()
        {
            SyncDisposeCalls++;
            throw new InvalidOperationException("Synchronous diagnostics disposal was used.");
        }

        public ValueTask DisposeAsync()
        {
            AsyncDisposeCalls++;
            AsyncDisposeStarted.TrySetResult(null);
            return new ValueTask(release.Task);
        }

        public void ReleaseAsyncDispose() => release.TrySetResult(null);
    }
}
