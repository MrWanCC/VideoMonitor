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
    private readonly IUiDispatcher dispatcher;
    private PlaybackSession? playbackSession;
    private TestSessionDto? session;
    private TestPreviewState state;
    private string statusText = string.Empty;
    private int disposed;

    public TestPreviewViewModel(
        ITestStreamApiClient apiClient,
        IPlaybackEngine playbackEngine,
        Func<Uri?> baseUriProvider,
        IUiDispatcher dispatcher)
    {
        this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        this.playbackEngine = playbackEngine
            ?? throw new ArgumentNullException(nameof(playbackEngine));
        this.baseUriProvider = baseUriProvider
            ?? throw new ArgumentNullException(nameof(baseUriProvider));
        this.dispatcher = dispatcher
            ?? throw new ArgumentNullException(nameof(dispatcher));
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        if (await ReadOnUiAsync(() => IsActive))
        {
            if (!await StopAsync(cancellationToken))
            {
                return;
            }
        }

        Uri? baseUri = null;
        await RunOnUiAsync(() =>
        {
            State = TestPreviewState.Starting;
            StatusText = "正在启动测试视频…";
            baseUri = baseUriProvider();
        });
        if (baseUri is null)
        {
            await RunOnUiAsync(() =>
            {
                State = TestPreviewState.Failure;
                StatusText = "服务器不可用。";
            });
            return;
        }

        TestSessionDto? createdSession = null;
        try
        {
            createdSession = await apiClient
                .StartAsync(baseUri, request, cancellationToken);
            await RunOnUiAsync(() =>
            {
                SetSession(createdSession);
                var source = new TestPreviewSource(
                    createdSession.ChannelId,
                    createdSession.StreamId,
                    createdSession.PlaybackUrl);
                playbackSession = playbackEngine.Start(source.ToPlaybackSource());
                OnPlaybackSessionChanged();
                State = TestPreviewState.Playing;
                StatusText = "测试视频播放中。";
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (createdSession is not null)
            {
                await ReleaseCreatedSessionAsync(baseUri, createdSession);
            }

            await RunOnUiAsync(() =>
            {
                State = TestPreviewState.Failure;
                StatusText = "测试视频已取消。";
            });
            throw;
        }
        catch (CatalogApiException exception)
        {
            if (createdSession is not null)
            {
                await ReleaseCreatedSessionAsync(baseUri, createdSession);
            }

            await RunOnUiAsync(() =>
            {
                State = TestPreviewState.Failure;
                StatusText = ToSafeFailureStatus(exception.Code);
            });
        }
        catch
        {
            if (createdSession is not null)
            {
                await ReleaseCreatedSessionAsync(baseUri, createdSession);
            }

            await RunOnUiAsync(() =>
            {
                State = TestPreviewState.Failure;
                StatusText = "播放准备失败。";
            });
        }
    }

    public async Task<bool> StopAsync(CancellationToken cancellationToken = default)
    {
        StopContext? context = null;
        try
        {
            await RunOnUiAsync(() =>
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
                if (currentPlayback is not null)
                {
                    playbackEngine.Stop(currentPlayback);
                    playbackSession = null;
                    OnPlaybackSessionChanged();
                }

                context = new StopContext(currentSession, baseUri);
            });
            if (context is null)
            {
                return true;
            }

            if (context.Session is not null)
            {
                if (context.BaseUri is null
                    || !await StopServerSessionAsync(
                        context.BaseUri,
                        context.Session.SessionId))
                {
                    await RunOnUiAsync(() =>
                    {
                        State = TestPreviewState.Failure;
                        StatusText = "测试视频清理失败。";
                    });
                    return false;
                }

                await RunOnUiAsync(() =>
                {
                    if (ReferenceEquals(session, context.Session))
                    {
                        SetSession(null);
                    }
                });
            }

            await RunOnUiAsync(() =>
            {
                State = TestPreviewState.Idle;
                StatusText = string.Empty;
            });
            return true;
        }
        catch
        {
            await RunOnUiAsync(() =>
            {
                State = TestPreviewState.Failure;
                StatusText = "测试视频清理失败。";
            });
            return false;
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken);
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        await StopAsync();
        if (playbackEngine is IDisposable disposable)
        {
            await RunOnUiAsync(disposable.Dispose);
        }
    }

    private bool CanStop() =>
        IsActive
        && State is not TestPreviewState.Starting
        && State is not TestPreviewState.Stopping;

    private async Task ExecuteStopCommandAsync()
    {
        await StopAsync();
    }

    private async Task ReleaseCreatedSessionAsync(
        Uri? baseUri,
        TestSessionDto createdSession)
    {
        var stopped = baseUri is not null
            && await StopServerSessionAsync(baseUri, createdSession.SessionId);
        await RunOnUiAsync(() =>
        {
            if (stopped && ReferenceEquals(session, createdSession))
            {
                SetSession(null);
                return;
            }

            SetSession(createdSession);
        });
    }

    private async Task<bool> StopServerSessionAsync(Uri baseUri, Guid sessionId)
    {
        try
        {
            await apiClient.StopAsync(baseUri, sessionId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private Task RunOnUiAsync(Action action) =>
        dispatcher.InvokeAsync(action, CancellationToken.None);

    private async Task<T> ReadOnUiAsync<T>(Func<T> read)
    {
        T value = default!;
        await RunOnUiAsync(() => value = read());
        return value;
    }

    private sealed record StopContext(
        TestSessionDto? Session,
        Uri? BaseUri);

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
        "MediaServerUnavailable" or "CATALOG_UNAVAILABLE"
            => "无法连接流媒体服务。",
        "AuthFailed"
            => "摄像头用户名或密码验证失败。",
        "ConnectFailed"
            => "无法连接摄像头。",
        "MediaRegistrationTimeout"
            => "视频流注册超时。",
        "PlaybackPreparationFailed"
            => "视频播放准备失败。",
        "IdentityConflict"
            => "视频流标识冲突，请重试。",
        _ => "测试视频启动失败。"
    };
}
