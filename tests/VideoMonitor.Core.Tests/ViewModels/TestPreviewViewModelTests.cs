using System.ComponentModel;
using System.Threading;
using System.Windows.Threading;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Playback;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

[Collection("Wpf")]
public sealed class TestPreviewViewModelTests
{
    private static readonly SemaphoreSlim WpfGate = new(1, 1);

    private static readonly TestStreamStartRequest Request = new(
        null,
        null,
        new CameraDeviceDraftDto(
            "10.0.0.5", 554, "admin", "secret", 1, StreamType.Main, TransportMode.Auto),
        DateTimeOffset.UtcNow);

    [Fact]
    public async Task StartFailureReturnsToUiDispatcher()
    {
        await RunOnStaAsync(async () =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var api = new DeferredFailureApi();
            var viewModel = new TestPreviewViewModel(
                api,
                new FakePlaybackEngine(),
                () => new Uri("https://server/"));
            var propertyChangedOnUi = new List<bool>();
            viewModel.PropertyChanged += OnPropertyChanged;

            var startTask = viewModel.StartAsync(Request);
            await api.Started.Task;
            await Task.Run(() => api.Fail(new CatalogApiException("MediaServerUnavailable")));
            await startTask;

            Assert.Equal(TestPreviewState.Failure, viewModel.State);
            Assert.NotEmpty(propertyChangedOnUi);
            Assert.All(propertyChangedOnUi, Assert.True);
            Assert.Equal("无法连接流媒体服务。", viewModel.StatusText);

            void OnPropertyChanged(object? sender, PropertyChangedEventArgs args) =>
                propertyChangedOnUi.Add(dispatcher.CheckAccess());
        });
    }

    [Fact]
    public async Task StartSuccessUpdatesMediaPlayerOnUiThread()
    {
        await RunOnStaAsync(async () =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var api = new DeferredSuccessApi();
            var engine = new ThreadRecordingPlaybackEngine(dispatcher);
            var viewModel = new TestPreviewViewModel(
                api,
                engine,
                () => new Uri("https://server/"));
            var propertyChangedOnUi = new List<bool>();
            viewModel.PropertyChanged += (_, _) =>
                propertyChangedOnUi.Add(dispatcher.CheckAccess());

            var startTask = viewModel.StartAsync(Request);
            await api.Started.Task;
            await Task.Run(api.Complete);
            await startTask;

            Assert.Equal(TestPreviewState.Playing, viewModel.State);
            Assert.True(engine.StartedOnUi);
            Assert.NotEmpty(propertyChangedOnUi);
            Assert.All(propertyChangedOnUi, Assert.True);
        });
    }

    [Fact]
    public async Task StopUpdatesSessionOnUiThread()
    {
        await RunOnStaAsync(async () =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var api = new DeferredStopApi();
            var engine = new ThreadRecordingPlaybackEngine(dispatcher);
            var viewModel = new TestPreviewViewModel(
                api,
                engine,
                () => new Uri("https://server/"));
            await viewModel.StartAsync(Request);
            var propertyChangedOnUi = new List<bool>();
            viewModel.PropertyChanged += (_, _) =>
                propertyChangedOnUi.Add(dispatcher.CheckAccess());

            var stopTask = viewModel.StopAsync();
            await api.StopStarted.Task;
            await Task.Run(api.CompleteStop);
            await stopTask;

            Assert.Equal(TestPreviewState.Idle, viewModel.State);
            Assert.Null(viewModel.Session);
            Assert.True(engine.StoppedOnUi);
            Assert.NotEmpty(propertyChangedOnUi);
            Assert.All(propertyChangedOnUi, Assert.True);
        });
    }

    [Fact]
    public async Task CloseDoesNotCrossThread()
    {
        await RunOnStaAsync(async () =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var api = new DeferredStopApi();
            var viewModel = new TestPreviewViewModel(
                api,
                new ThreadRecordingPlaybackEngine(dispatcher),
                () => new Uri("https://server/"));
            await viewModel.StartAsync(Request);
            var propertyChangedOnUi = new List<bool>();
            viewModel.PropertyChanged += (_, _) =>
                propertyChangedOnUi.Add(dispatcher.CheckAccess());

            var closeTask = viewModel.CloseAsync();
            await api.StopStarted.Task;
            await Task.Run(api.CompleteStop);
            await closeTask;

            Assert.Equal(TestPreviewState.Idle, viewModel.State);
            Assert.NotEmpty(propertyChangedOnUi);
            Assert.All(propertyChangedOnUi, Assert.True);
        });
    }

    [Fact]
    public async Task DisposeDoesNotCrossThread()
    {
        await RunOnStaAsync(async () =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var api = new DeferredStopApi();
            var engine = new ThreadRecordingPlaybackEngine(dispatcher);
            var viewModel = new TestPreviewViewModel(
                api,
                engine,
                () => new Uri("https://server/"));
            await viewModel.StartAsync(Request);
            var propertyChangedOnUi = new List<bool>();
            viewModel.PropertyChanged += (_, _) =>
                propertyChangedOnUi.Add(dispatcher.CheckAccess());

            await using (viewModel)
            {
                var disposeTask = viewModel.DisposeAsync().AsTask();
                await api.StopStarted.Task;
                await Task.Run(api.CompleteStop);
                await disposeTask;
            }

            Assert.True(engine.DisposedOnUi);
            Assert.NotEmpty(propertyChangedOnUi);
            Assert.All(propertyChangedOnUi, Assert.True);
        });
    }

    [Fact]
    public async Task StopAndCloseReleaseSession()
    {
        var fixture = new Fixture();

        await fixture.ViewModel.StartAsync(Request);
        await fixture.ViewModel.CloseAsync();

        Assert.Equal(TestPreviewState.Idle, fixture.ViewModel.State);
        Assert.Equal(1, fixture.Api.StopCalls);
        Assert.Equal(1, fixture.Engine.StopCalls);
    }

    [Fact]
    public async Task StartFailureCleansCreatedServerSessionIfPlaybackEngineFails()
    {
        var fixture = new Fixture { Engine = new FakePlaybackEngine { ThrowOnStart = true } };
        fixture.RebuildViewModel();

        await fixture.ViewModel.StartAsync(Request);

        Assert.Equal(TestPreviewState.Failure, fixture.ViewModel.State);
        Assert.Equal(1, fixture.Api.StopCalls);
        Assert.Contains("播放", fixture.ViewModel.StatusText);
    }

    [Fact]
    public async Task SwitchingDraftStopsPreviousSession()
    {
        var fixture = new Fixture();

        await fixture.ViewModel.StartAsync(Request);
        await fixture.ViewModel.StartAsync(Request with
        {
            Draft = Request.Draft with { IpAddress = "10.0.0.6" }
        });

        Assert.Equal(1, fixture.Api.StopCalls);
        Assert.Equal(1, fixture.Engine.StopCalls);
        Assert.Equal(TestPreviewState.Playing, fixture.ViewModel.State);
    }

    [Fact]
    public async Task StopActionDoesNotRestartPreview()
    {
        var fixture = new Fixture();

        await fixture.ViewModel.StartAsync(Request);
        await fixture.ViewModel.StopCommand.ExecuteAsync(null);

        Assert.Equal(1, fixture.Api.StartCalls);
        Assert.Equal(1, fixture.Api.StopCalls);
        Assert.Equal(TestPreviewState.Idle, fixture.ViewModel.State);
    }

    [Fact]
    public async Task ServerStopFailureRetainsSessionForRetry()
    {
        var fixture = new Fixture();
        await fixture.ViewModel.StartAsync(Request);
        var sessionId = fixture.ViewModel.Session!.SessionId;
        fixture.Api.StopFailure = true;

        await fixture.ViewModel.StopAsync();

        Assert.Equal(TestPreviewState.Failure, fixture.ViewModel.State);
        Assert.Equal(sessionId, fixture.ViewModel.Session!.SessionId);
        Assert.Equal(1, fixture.Api.StopCalls);

        fixture.Api.StopFailure = false;
        await fixture.ViewModel.StopAsync();

        Assert.Equal(TestPreviewState.Idle, fixture.ViewModel.State);
        Assert.Null(fixture.ViewModel.Session);
        Assert.Equal(2, fixture.Api.StopCalls);
        Assert.Equal(new[] { sessionId, sessionId }, fixture.Api.StoppedSessionIds);
    }

    [Fact]
    public async Task SafeServerFailureIsVisibleWithoutCredentials()
    {
        var fixture = new Fixture
        {
            Api = new FakeApi { StartFailure = new CatalogApiException("AuthFailed") }
        };
        fixture.RebuildViewModel();

        await fixture.ViewModel.StartAsync(Request);

        Assert.Equal(TestPreviewState.Failure, fixture.ViewModel.State);
        Assert.Equal("摄像头用户名或密码验证失败。", fixture.ViewModel.StatusText);
        Assert.DoesNotContain("AuthFailed", fixture.ViewModel.StatusText);
        Assert.DoesNotContain("secret", fixture.ViewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rtsp://", fixture.ViewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("MediaServerUnavailable", "无法连接流媒体服务。")]
    [InlineData("AuthFailed", "摄像头用户名或密码验证失败。")]
    [InlineData("ConnectFailed", "无法连接摄像头。")]
    [InlineData("MediaRegistrationTimeout", "视频流注册超时。")]
    [InlineData("PlaybackPreparationFailed", "视频播放准备失败。")]
    [InlineData("IdentityConflict", "视频流标识冲突，请重试。")]
    [InlineData("UnexpectedInternalCode", "测试视频启动失败。")]
    public async Task ServerFailureUsesSafeChineseStatus(
        string code,
        string expectedStatus)
    {
        var fixture = new Fixture
        {
            Api = new FakeApi { StartFailure = new CatalogApiException(code) }
        };
        fixture.RebuildViewModel();

        await fixture.ViewModel.StartAsync(Request);

        Assert.Equal(TestPreviewState.Failure, fixture.ViewModel.State);
        Assert.Equal(expectedStatus, fixture.ViewModel.StatusText);
        Assert.DoesNotContain(code, fixture.ViewModel.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartFailureWithServerStopFailureRetainsSessionForRetry()
    {
        var fixture = new Fixture
        {
            Engine = new FakePlaybackEngine { ThrowOnStart = true },
            Api = new FakeApi { StopFailure = true }
        };
        fixture.RebuildViewModel();

        await fixture.ViewModel.StartAsync(Request);

        Assert.Equal(TestPreviewState.Failure, fixture.ViewModel.State);
        Assert.NotNull(fixture.ViewModel.Session);

        fixture.Api.StopFailure = false;
        await fixture.ViewModel.StopAsync();

        Assert.Null(fixture.ViewModel.Session);
        Assert.Equal(TestPreviewState.Idle, fixture.ViewModel.State);
    }

    private sealed class Fixture
    {
        public FakeApi Api { get; set; } = new();

        public FakePlaybackEngine Engine { get; set; } = new();

        public TestPreviewViewModel ViewModel { get; private set; } = null!;

        public Fixture() => RebuildViewModel();

        public void RebuildViewModel() => ViewModel = new TestPreviewViewModel(
            Api,
            Engine,
            () => new Uri("https://server/"));
    }

    private sealed class FakeApi : ITestStreamApiClient
    {
        private int nextSession;

        public int StopCalls { get; private set; }

        public int StartCalls { get; private set; }

        public bool StopFailure { get; set; }

        public CatalogApiException? StartFailure { get; init; }

        public List<Guid> StoppedSessionIds { get; } = [];

        public Task<TestSessionDto> StartAsync(
            Uri baseUri,
            TestStreamStartRequest request,
            CancellationToken cancellationToken = default)
        {
            StartCalls++;
            if (StartFailure is not null)
            {
                throw StartFailure;
            }

            var sessionId = new Guid(
                unchecked((int)(0x94000000u + (uint)nextSession++)), 0, 0, new byte[8]);
            return Task.FromResult(new TestSessionDto(
                sessionId,
                request.ExistingDeviceId,
                request.ExistingChannelId,
                "videomonitor-test",
                "test_0123456789abcdef0123456789abcdef",
                new Uri("rtsp://playback.example/live"),
                DateTimeOffset.UtcNow.AddMinutes(2)));
        }

        public Task StopAsync(
            Uri baseUri,
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            StopCalls++;
            StoppedSessionIds.Add(sessionId);
            if (StopFailure)
            {
                throw new CatalogApiException("MediaServerUnavailable");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakePlaybackEngine : IPlaybackEngine
    {
        public bool ThrowOnStart { get; init; }

        public int StopCalls { get; private set; }

        public PlaybackSession Start(PlaybackSource source)
        {
            if (ThrowOnStart)
            {
                throw new PlaybackEngineException("播放失败");
            }

            return new PlaybackSession(source, null, null);
        }

        public void Stop(PlaybackSession session)
        {
            StopCalls++;
            session.Dispose();
        }
    }

    private sealed class DeferredFailureApi : ITestStreamApiClient
    {
        private readonly TaskCompletionSource<TestSessionDto> failure =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TestSessionDto> StartAsync(
            Uri baseUri,
            TestStreamStartRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(null);
            return failure.Task;
        }

        public Task StopAsync(
            Uri baseUri,
            Guid sessionId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void Fail(Exception exception) => failure.TrySetException(exception);
    }

    private sealed class DeferredSuccessApi : ITestStreamApiClient
    {
        private readonly TaskCompletionSource<TestSessionDto> completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?> Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TestSessionDto> StartAsync(
            Uri baseUri,
            TestStreamStartRequest request,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(null);
            return completion.Task;
        }

        public Task StopAsync(
            Uri baseUri,
            Guid sessionId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void Complete() => completion.TrySetResult(new TestSessionDto(
            Guid.Parse("94000000-0000-0000-0000-000000000001"),
            null,
            null,
            "videomonitor-test",
            "test_0123456789abcdef0123456789abcdef",
            new Uri("rtsp://playback.example/live"),
            DateTimeOffset.UtcNow.AddMinutes(2)));
    }

    private sealed class DeferredStopApi : ITestStreamApiClient
    {
        private readonly TaskCompletionSource<object?> stopCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?> StopStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TestSessionDto> StartAsync(
            Uri baseUri,
            TestStreamStartRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new TestSessionDto(
                Guid.Parse("94000000-0000-0000-0000-000000000001"),
                null,
                null,
                "videomonitor-test",
                "test_0123456789abcdef0123456789abcdef",
                new Uri("rtsp://playback.example/live"),
                DateTimeOffset.UtcNow.AddMinutes(2)));

        public Task StopAsync(
            Uri baseUri,
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            StopStarted.TrySetResult(null);
            return stopCompletion.Task;
        }

        public void CompleteStop() => stopCompletion.TrySetResult(null);
    }

    private sealed class ThreadRecordingPlaybackEngine : IPlaybackEngine, IDisposable
    {
        private readonly Dispatcher dispatcher;

        public ThreadRecordingPlaybackEngine(Dispatcher dispatcher) =>
            this.dispatcher = dispatcher;

        public bool StartedOnUi { get; private set; }

        public bool StoppedOnUi { get; private set; }

        public bool DisposedOnUi { get; private set; }

        public PlaybackSession Start(PlaybackSource source)
        {
            StartedOnUi = dispatcher.CheckAccess();
            return new PlaybackSession(source, null, null);
        }

        public void Stop(PlaybackSession session)
        {
            StoppedOnUi = dispatcher.CheckAccess();
            session.Dispose();
        }

        public void Dispose() => DisposedOnUi = dispatcher.CheckAccess();
    }

    private static async Task RunOnStaAsync(Func<Task> action)
    {
        await WpfGate.WaitAsync();
        try
        {
            var completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                Exception? failure = null;
                try
                {
                    var dispatcher = Dispatcher.CurrentDispatcher;
                    SynchronizationContext.SetSynchronizationContext(
                        new DispatcherSynchronizationContext(dispatcher));
                    _ = RunAsync();
                    Dispatcher.Run();

                    if (failure is null)
                    {
                        completion.SetResult(null);
                    }
                    else
                    {
                        completion.SetException(failure);
                    }

                    async Task RunAsync()
                    {
                        try
                        {
                            await action();
                        }
                        catch (Exception exception)
                        {
                            failure = exception;
                        }
                        finally
                        {
                            dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
                        }
                    }
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            })
            {
                IsBackground = true,
            };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            await completion.Task;
        }
        finally
        {
            WpfGate.Release();
        }
    }
}
