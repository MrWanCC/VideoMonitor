using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        StopCommand = new AsyncRelayCommand(ExecuteStopCommandAsync, CanStop);
    }

    public TestPreviewState State
    {
        get => state;
        private set
        {
            if (SetProperty(ref state, value))
            {
                StopCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public TestSessionDto? Session => session;

    public MediaPlayer? MediaPlayer => playbackSession?.MediaPlayer;

    public bool IsActive => session is not null || playbackSession is not null;

    public IAsyncRelayCommand StopCommand { get; }

    public async Task StartAsync(
        TestStreamStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        if (IsActive)
        {
            if (!await StopAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }
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
            SetSession(createdSession);
            var source = new TestPreviewSource(
                createdSession.ChannelId,
                createdSession.StreamId,
                createdSession.PlaybackUrl);
            playbackSession = playbackEngine.Start(source.ToPlaybackSource());
            OnPlaybackSessionChanged();
            State = TestPreviewState.Playing;
            StatusText = "测试视频播放中。";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (createdSession is not null)
            {
                await ReleaseCreatedSessionAsync(baseUri, createdSession)
                    .ConfigureAwait(false);
            }

            State = TestPreviewState.Failure;
            StatusText = "测试视频已取消。";
            throw;
        }
        catch (CatalogApiException exception)
        {
            if (createdSession is not null)
            {
                await ReleaseCreatedSessionAsync(baseUri, createdSession)
                    .ConfigureAwait(false);
            }

            State = TestPreviewState.Failure;
            StatusText = ToSafeFailureStatus(exception.Code);
        }
        catch
        {
            if (createdSession is not null)
            {
                await ReleaseCreatedSessionAsync(baseUri, createdSession)
                    .ConfigureAwait(false);
            }

            State = TestPreviewState.Failure;
            StatusText = "播放准备失败。";
        }
    }

    public async Task<bool> StopAsync(CancellationToken cancellationToken = default)
    {
        var currentSession = session;
        var currentPlayback = playbackSession;
        if (currentSession is null && currentPlayback is null)
        {
            State = TestPreviewState.Idle;
            StatusText = string.Empty;
            return true;
        }

        State = TestPreviewState.Stopping;
        var baseUri = baseUriProvider();
        try
        {
            if (currentPlayback is not null)
            {
                playbackEngine.Stop(currentPlayback);
                playbackSession = null;
                OnPlaybackSessionChanged();
            }

            if (currentSession is not null)
            {
                if (baseUri is null
                    || !await StopServerSessionAsync(baseUri, currentSession.SessionId)
                        .ConfigureAwait(false))
                {
                    State = TestPreviewState.Failure;
                    StatusText = "测试视频清理失败。";
                    return false;
                }

                if (ReferenceEquals(session, currentSession))
                {
                    SetSession(null);
                }
            }

            State = TestPreviewState.Idle;
            StatusText = string.Empty;
            return true;
        }
        catch
        {
            State = TestPreviewState.Failure;
            StatusText = "测试视频清理失败。";
            return false;
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
    }

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

    private bool CanStop() =>
        IsActive
        && State is not TestPreviewState.Starting
        && State is not TestPreviewState.Stopping;

    private async Task ExecuteStopCommandAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task ReleaseCreatedSessionAsync(
        Uri? baseUri,
        TestSessionDto createdSession)
    {
        if (baseUri is not null
            && await StopServerSessionAsync(baseUri, createdSession.SessionId)
                .ConfigureAwait(false))
        {
            if (ReferenceEquals(session, createdSession))
            {
                SetSession(null);
            }

            return;
        }

        SetSession(createdSession);
    }

    private async Task<bool> StopServerSessionAsync(Uri baseUri, Guid sessionId)
    {
        try
        {
            await apiClient.StopAsync(baseUri, sessionId).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SetSession(TestSessionDto? value)
    {
        session = value;
        OnPropertyChanged(nameof(Session));
        OnPropertyChanged(nameof(IsActive));
        StopCommand.NotifyCanExecuteChanged();
    }

    private void OnPlaybackSessionChanged()
    {
        OnPropertyChanged(nameof(MediaPlayer));
        OnPropertyChanged(nameof(IsActive));
        StopCommand.NotifyCanExecuteChanged();
    }

    private static string ToSafeFailureStatus(string code) => code switch
    {
        "InvalidDraft"
            or "MediaServerUnavailable"
            or "AuthFailed"
            or "ConnectFailed"
            or "MediaRegistrationTimeout"
            or "PlaybackPreparationFailed"
            or "CatalogUnavailable"
            or "IdentityConflict"
            or "SessionNotFound"
            or "CATALOG_UNAVAILABLE"
            or "CATALOG_VALIDATION_FAILED"
            => $"测试视频失败：{code}",
        _ => "播放准备失败。"
    };
}
