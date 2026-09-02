using VideoMonitor.Core.Media;
using Microsoft.Extensions.Logging;
using VideoMonitor.Core.Models;
using VideoMonitor.Server.Catalog;
using VideoMonitor.Server.Media;
using VideoMonitor.Server.Playback;

namespace VideoMonitor.Server.Tests.Media;

public sealed class TestStreamServiceTests
{
    private static readonly Guid DeviceId =
        Guid.Parse("91000000-0000-0000-0000-000000000001");
    private static readonly Guid ChannelId =
        Guid.Parse("92000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task NewDraftStartsWithoutCatalogWrite()
    {
        var fixture = new ServiceFixture();

        var result = await fixture.Service.StartAsync(Request(null, null, ""));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, fixture.Recorder.Calls);
        Assert.Equal(0, fixture.Proxy.StopCalls);
    }

    [Fact]
    public async Task ExistingEditUsesSavedPasswordWhenDraftIsBlank()
    {
        var fixture = new ServiceFixture();

        var result = await fixture.Service.StartAsync(
            Request(DeviceId, ChannelId, " "));

        Assert.True(result.IsSuccess);
        Assert.Equal(DeviceId, fixture.Resolver.LastRequest!.ExistingDeviceId);
        Assert.Equal(1, fixture.Recorder.Calls);
    }

    [Fact]
    public async Task SuccessfulExistingTestUpdatesObservedAtUtc()
    {
        var fixture = new ServiceFixture();

        await fixture.Service.StartAsync(Request(DeviceId, ChannelId, "secret"));

        Assert.Equal(1, fixture.Recorder.Calls);
        Assert.Equal(SourceObservation.Reachable, fixture.Recorder.Observation);
        Assert.Equal(Now, fixture.Recorder.ObservedAtUtc);
    }

    [Fact]
    public async Task FailedExistingTestUpdatesObservation()
    {
        var fixture = new ServiceFixture
        {
            Proxy = new FakeProxy
            {
                Failure = new TestStreamOperationException(
                    TestStreamErrorCode.ConnectFailed,
                    "safe")
            }
        };
        fixture.RebuildService();

        var result = await fixture.Service.StartAsync(
            Request(DeviceId, ChannelId, "secret"));

        Assert.False(result.IsSuccess);
        Assert.Equal(SourceObservation.ConnectFailed, fixture.Recorder.Observation);
        Assert.Equal("ConnectFailed", fixture.Recorder.ErrorCode);
    }

    [Fact]
    public async Task NewDraftDoesNotCreateFormalObservation()
    {
        var fixture = new ServiceFixture();

        await fixture.Service.StartAsync(Request(null, null, "secret"));

        Assert.Equal(0, fixture.Recorder.Calls);
    }

    [Fact]
    public async Task EmptyNewPasswordIsAllowed()
    {
        var fixture = new ServiceFixture();

        var result = await fixture.Service.StartAsync(Request(null, null, ""));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task MediaServerUnavailableIsMapped()
    {
        var fixture = new ServiceFixture
        {
            Proxy = new FakeProxy
            {
                Failure = new TestStreamOperationException(
                    TestStreamErrorCode.MediaServerUnavailable,
                    "internal")
            }
        };
        fixture.RebuildService();

        var result = await fixture.Service.StartAsync(Request(null, null, ""));

        Assert.False(result.IsSuccess);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal("MediaServerUnavailable", result.Error!.Code);
        Assert.DoesNotContain("internal", result.Error.Message);
    }

    [Fact]
    public async Task AuthFailedRequiresEvidence()
    {
        var fixture = new ServiceFixture
        {
            Proxy = new FakeProxy
            {
                Failure = new TestStreamOperationException(
                    TestStreamErrorCode.AuthFailed,
                    "internal")
            }
        };
        fixture.RebuildService();

        var result = await fixture.Service.StartAsync(Request(null, null, ""));

        Assert.False(result.IsSuccess);
        Assert.Equal("AuthFailed", result.Error!.Code);
    }

    [Fact]
    public async Task ConnectFailedIsSafeWithoutAuthEvidence()
    {
        var fixture = new ServiceFixture
        {
            Proxy = new FakeProxy
            {
                Failure = new TestStreamOperationException(
                    TestStreamErrorCode.ConnectFailed,
                    "internal")
            }
        };
        fixture.RebuildService();

        var result = await fixture.Service.StartAsync(Request(null, null, ""));

        Assert.False(result.IsSuccess);
        Assert.Equal("ConnectFailed", result.Error!.Code);
    }

    [Fact]
    public async Task MediaRegistrationTimeoutIsMapped()
    {
        var fixture = new ServiceFixture
        {
            Proxy = new FakeProxy
            {
                Failure = new TestStreamOperationException(
                    TestStreamErrorCode.MediaRegistrationTimeout,
                    "internal")
            }
        };
        fixture.RebuildService();

        var result = await fixture.Service.StartAsync(Request(null, null, ""));

        Assert.False(result.IsSuccess);
        Assert.Equal("MediaRegistrationTimeout", result.Error!.Code);
    }

    [Fact]
    public async Task FailureLogsSafeDiagnosticsWithoutSensitiveValues()
    {
        var fixture = new ServiceFixture
        {
            Proxy = new FakeProxy
            {
                Failure = new TestStreamOperationException(
                    TestStreamErrorCode.MediaServerUnavailable,
                    "camera password and rtsp://secret must not be logged")
            }
        };
        fixture.RebuildService();

        var result = await fixture.Service.StartAsync(
            Request(DeviceId, ChannelId, "secret"));

        Assert.False(result.IsSuccess);
        var message = Assert.Single(fixture.Logger.Messages);
        Assert.Contains("Test stream failed safely.", message);
        Assert.Contains("FailureCode=MediaServerUnavailable", message);
        Assert.Contains("Stage=AddProxy", message);
        Assert.Contains(DeviceId.ToString(), message);
        Assert.Contains(ChannelId.ToString(), message);
        Assert.Contains("StreamType=Main", message);
        Assert.Contains("ExceptionType=TestStreamOperationException", message);
        Assert.Null(fixture.Logger.Exceptions.Single());
        Assert.DoesNotContain("secret", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rtsp://", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlaybackPreparationFailedIsMapped()
    {
        var fixture = new ServiceFixture
        {
            UrlBuilder = new FakeUrlBuilder { Failure = true }
        };
        fixture.RebuildService();

        var result = await fixture.Service.StartAsync(Request(null, null, ""));

        Assert.False(result.IsSuccess);
        Assert.Equal("PlaybackPreparationFailed", result.Error!.Code);
        Assert.Equal(1, fixture.Proxy.StopCalls);
    }

    [Fact]
    public async Task StopFailureRetainsExactSessionForRetry()
    {
        var fixture = new ServiceFixture();
        var start = await fixture.Service.StartAsync(Request(DeviceId, ChannelId, "secret"));
        var sessionId = start.Value!.SessionId;
        fixture.Proxy.StopFailure = true;

        var first = await fixture.Service.StopAsync(sessionId);

        Assert.False(first.IsSuccess);
        Assert.True(fixture.Registry.TryGet(sessionId, out var retained));
        Assert.Equal("proxy", retained!.Handle.ProxyKey);

        fixture.Proxy.StopFailure = false;
        var second = await fixture.Service.StopAsync(sessionId);

        Assert.True(second.IsSuccess);
        Assert.False(fixture.Registry.TryGet(sessionId, out _));
        Assert.Equal(new[] { "proxy", "proxy" }, fixture.Proxy.StoppedProxyKeys);
    }

    [Fact]
    public async Task PlaybackPreparationCleanupFailureRetainsPendingHandle()
    {
        var fixture = new ServiceFixture
        {
            UrlBuilder = new FakeUrlBuilder { Failure = true },
            Proxy = new FakeProxy { StopFailure = true }
        };
        fixture.RebuildService();

        var result = await fixture.Service.StartAsync(Request(null, null, ""));

        Assert.False(result.IsSuccess);
        Assert.Equal("PlaybackPreparationFailed", result.Error!.Code);
        var pending = Assert.Single(fixture.Registry.GetPendingCleanup());
        Assert.Equal("vhost", pending.Vhost);
        Assert.Equal("videomonitor-test", pending.App);
        Assert.Equal("test_0123456789abcdef0123456789abcdef", pending.StreamId);
        Assert.Equal("proxy", pending.ProxyKey);
    }

    [Fact]
    public async Task InvalidExistingRelationDoesNotCreateFormalObservation()
    {
        var fixture = new ServiceFixture();
        fixture.Resolver.Failure = new TestStreamOperationException(
            TestStreamErrorCode.CatalogUnavailable,
            "relation is invalid");
        fixture.RebuildService();

        var result = await fixture.Service.StartAsync(Request(DeviceId, ChannelId, "secret"));

        Assert.False(result.IsSuccess);
        Assert.Equal(0, fixture.Recorder.Calls);
    }

    private static TestStreamStartRequest Request(
        Guid? deviceId,
        Guid? channelId,
        string password) =>
        new(
            deviceId,
            channelId,
            new CameraDeviceDraftDto(
                "10.0.0.5", 554, "admin", password, 1, StreamType.Main, TransportMode.Auto),
            Now);

    private sealed class ServiceFixture
    {
        public FakeResolver Resolver { get; } = new();

        public FakeProxy Proxy { get; set; } = new();

        public TestSessionRegistry Registry { get; } = new(() => Now);

        public FakeUrlBuilder UrlBuilder { get; set; } = new();

        public RecordingObservationRecorder Recorder { get; } = new();

        public RecordingLogger Logger { get; } = new();

        public TestStreamService Service { get; private set; } = null!;

        public ServiceFixture() => RebuildService();

        public void RebuildService()
        {
            Service = new TestStreamService(
                Resolver,
                Proxy,
                new FakeTicketIssuer(),
                UrlBuilder,
                Registry,
                Recorder,
                () => Now,
                Logger);
        }
    }

    private sealed class FakeResolver : ITestCameraSourceResolver
    {
        public TestStreamStartRequest? LastRequest { get; private set; }

        public TestStreamOperationException? Failure { get; set; }

        public Task<ResolvedTestCameraSource> ResolveAsync(
            TestStreamStartRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            LastRequest = request;
            return Task.FromResult(new ResolvedTestCameraSource(
                new Uri("rtsp://10.0.0.5:554/Streaming/Channels/101"),
                request.ExistingDeviceId,
                request.ExistingChannelId,
                request.Draft.ChannelNo,
                request.Draft.StreamType));
        }
    }

    private sealed class FakeProxy : ITestStreamProxyController
    {
        public TestStreamOperationException? Failure { get; init; }

        public bool StopFailure { get; set; }

        public int StopCalls { get; private set; }

        public List<string> StoppedProxyKeys { get; } = [];

        public Task<TestStreamProxyHandle> StartAsync(
            ResolvedTestCameraSource source,
            CancellationToken cancellationToken = default) =>
            Failure is not null
                ? throw Failure
                : Task.FromResult(new TestStreamProxyHandle(
                    "vhost", "videomonitor-test", "test_0123456789abcdef0123456789abcdef", "proxy", Now));

        public Task StopAsync(
            TestStreamProxyHandle handle,
            CancellationToken cancellationToken = default)
        {
            StopCalls++;
            StoppedProxyKeys.Add(handle.ProxyKey);
            if (StopFailure)
            {
                throw new TestStreamOperationException(
                    TestStreamErrorCode.MediaServerUnavailable,
                    "cleanup failed");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeTicketIssuer : IPlaybackTicketIssuer
    {
        public Task<PlaybackTicket> IssueAsync(
            PlaybackMediaIdentity media,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlaybackTicket(
                "ticket", media.Vhost, media.App, media.Stream, Now.AddMinutes(1)));
    }

    private sealed class FakeUrlBuilder : IPlaybackUrlBuilder
    {
        public bool Failure { get; init; }

        public Task<Uri> BuildAsync(
            PlaybackMediaIdentity media,
            PlaybackTicket ticket,
            CancellationToken cancellationToken = default) =>
            Failure
                ? throw new InvalidDataException("playback secret")
                : Task.FromResult(new Uri("rtsp://playback.example/live"));
    }

    private sealed class RecordingObservationRecorder : IMediaObservationRecorder
    {
        public int Calls { get; private set; }

        public SourceObservation Observation { get; private set; }

        public DateTimeOffset ObservedAtUtc { get; private set; }

        public string? ErrorCode { get; private set; }

        public void Record(
            MediaStreamKey key,
            SourceObservation observation,
            DateTimeOffset observedAtUtc,
            string? safeErrorCode,
            string? safeErrorMessage)
        {
            Calls++;
            Observation = observation;
            ObservedAtUtc = observedAtUtc;
            ErrorCode = safeErrorCode;
        }
    }

    private sealed class RecordingLogger : ILogger<TestStreamService>
    {
        public List<string> Messages { get; } = [];

        public List<Exception?> Exceptions { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }
}
