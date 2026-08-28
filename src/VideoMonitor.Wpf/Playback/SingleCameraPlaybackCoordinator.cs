using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Wpf.Playback;

public sealed class SingleCameraPlaybackCoordinator : IAsyncDisposable
{
    private readonly IPlaybackSourceProvider sourceProvider;
    private readonly IPlaybackEngine playbackEngine;
    private PlaybackSource? currentSource;
    private int disposed;

    public SingleCameraPlaybackCoordinator(
        IPlaybackSourceProvider sourceProvider,
        IPlaybackEngine playbackEngine)
    {
        this.sourceProvider = sourceProvider
            ?? throw new ArgumentNullException(nameof(sourceProvider));
        this.playbackEngine = playbackEngine
            ?? throw new ArgumentNullException(nameof(playbackEngine));
    }

    public PlaybackSession? CurrentSession { get; private set; }

    public async Task StartAsync(
        CameraDevice device,
        CameraChannel channel,
        VideoTileViewModel tile,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(tile);

        if (CurrentSession is not null)
        {
            return;
        }

        tile.ShowLoading();
        try
        {
            currentSource = await sourceProvider
                .PrepareAsync(device, channel, cancellationToken)
                .ConfigureAwait(true);
            CurrentSession = playbackEngine.Start(currentSource);
            tile.ShowPlaying(CurrentSession);
        }
        catch (PlaybackSourceException exception)
        {
            tile.ShowError(exception.Title, exception.Detail);
        }
        catch (PlaybackEngineException)
        {
            await ReleaseCurrentSourceAsync(CancellationToken.None).ConfigureAwait(true);
            tile.ShowError("播放失败", "LibVLC播放失败");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        if (CurrentSession is not null)
        {
            playbackEngine.Stop(CurrentSession);
            CurrentSession = null;
        }

        await ReleaseCurrentSourceAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ReleaseCurrentSourceAsync(CancellationToken cancellationToken)
    {
        if (currentSource is null)
        {
            return;
        }

        var source = currentSource;
        currentSource = null;
        await sourceProvider.ReleaseAsync(source, cancellationToken).ConfigureAwait(false);
    }
}
