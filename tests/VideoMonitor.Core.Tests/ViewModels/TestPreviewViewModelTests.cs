using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Playback;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class TestPreviewViewModelTests
{
    private static readonly TestStreamStartRequest Request = new(
        null,
        null,
        new CameraDeviceDraftDto(
            "10.0.0.5", 554, "admin", "secret", 1, StreamType.Main, TransportMode.Auto),
        DateTimeOffset.UtcNow);

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
        Assert.Contains("AuthFailed", fixture.ViewModel.StatusText);
        Assert.DoesNotContain("secret", fixture.ViewModel.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rtsp://", fixture.ViewModel.StatusText, StringComparison.OrdinalIgnoreCase);
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
}
