using CommunityToolkit.Mvvm.ComponentModel;
using LibVLCSharp.Shared;
using VideoMonitor.Core.Media;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Playback;

namespace VideoMonitor.Wpf.ViewModels;

public enum TestPreviewState
{
    Idle,
    Starting,
    Playing,
    Stopping,
    Failure
}

public sealed class TestPreviewViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ITestStreamApiClient apiClient;
    private readonly IPlaybackEngine playbackEngine;
    private readonly Func<Uri?> baseUriProvider;
    private PlaybackSession? playbackSession;
    private TestSessionDto? session;
    private TestPreviewState state;
    private string statusText = string.Empty;
    private int disposed;

    public TestPreviewViewModel(
        ITestStreamApiClient apiClient,
        IPlaybackEngine playbackEngine,
        Func<Uri?> baseUriProvider)
    {
        this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        this.playbackEngine = playbackEngine
            ?? throw new ArgumentNullException(nameof(playbackEngine));
        this.baseUriProvider = baseUriProvider
            ?? throw new ArgumentNullException(nameof(baseUriProvider));
    }

    public TestPreviewState State
    {
        get => state;
        private set => SetProperty(ref state, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public TestSessionDto? Session => session;

    public MediaPlayer? MediaPlayer => playbackSession?.MediaPlayer;

    public bool IsActive => session is not null || playbackSession is not null;

    public async Task StartAsync(
        TestStreamStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        if (IsActive)
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
        }

        State = TestPreviewState.Starting;
        StatusText = "正在启动测试视频…";
        var baseUri = baseUriProvider();
        if (baseUri is null)
        {
            State = TestPreviewState.Failure;
            StatusText = "服务器不可用。";
            return;
        }

        TestSessionDto? createdSession = null;
        try
        {
            createdSession = await apiClient
                .StartAsync(baseUri, request, cancellationToken)
                .ConfigureAwait(false);
            session = createdSession;
            OnPropertyChanged(nameof(Session));
            OnPropertyChanged(nameof(IsActive));
            var source = new TestPreviewSource(
                createdSession.ChannelId,
                createdSession.StreamId,
                createdSession.PlaybackUrl);
            playbackSession = playbackEngine.Start(source.ToPlaybackSource());
            OnPropertyChanged(nameof(MediaPlayer));
            OnPropertyChanged(nameof(IsActive));
            State = TestPreviewState.Playing;
            StatusText = "测试视频播放中。";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (createdSession is not null)
            {
                await StopServerSessionAsync(baseUri, createdSession.SessionId)
                    .ConfigureAwait(false);
            }

            session = null;
            OnPropertyChanged(nameof(Session));
            OnPropertyChanged(nameof(IsActive));
            State = TestPreviewState.Failure;
            StatusText = "测试视频已取消。";
            throw;
        }
        catch
        {
            if (createdSession is not null)
            {
                await StopServerSessionAsync(baseUri, createdSession.SessionId)
                    .ConfigureAwait(false);
            }

            session = null;
            OnPropertyChanged(nameof(Session));
            OnPropertyChanged(nameof(IsActive));
            State = TestPreviewState.Failure;
            StatusText = "播放准备失败。";
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var currentSession = session;
        var currentPlayback = playbackSession;
        if (currentSession is null && currentPlayback is null)
        {
            State = TestPreviewState.Idle;
            StatusText = string.Empty;
            return;
        }

        State = TestPreviewState.Stopping;
        var baseUri = baseUriProvider();
        playbackSession = null;
        session = null;
        OnPropertyChanged(nameof(MediaPlayer));
        OnPropertyChanged(nameof(Session));
        OnPropertyChanged(nameof(IsActive));
        try
        {
            if (currentPlayback is not null)
            {
                playbackEngine.Stop(currentPlayback);
            }

            if (currentSession is not null && baseUri is not null)
            {
                await StopServerSessionAsync(baseUri, currentSession.SessionId)
                    .ConfigureAwait(false);
            }

            State = TestPreviewState.Idle;
            StatusText = string.Empty;
        }
        catch
        {
            State = TestPreviewState.Failure;
            StatusText = "测试视频清理失败。";
        }
    }

    public Task CloseAsync(CancellationToken cancellationToken = default) =>
        StopAsync(cancellationToken);

    public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
        StopAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        if (playbackEngine is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private async Task StopServerSessionAsync(Uri baseUri, Guid sessionId)
    {
        try
        {
            await apiClient.StopAsync(baseUri, sessionId).ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
