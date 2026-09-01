using System.Text.Json;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.ZLMediaKit;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Tests.Media;

public sealed class TestStreamProxyControllerTests
{
    private static readonly ResolvedTestCameraSource Source = new(
        new Uri("rtsp://10.0.0.5:554/Streaming/Channels/101"),
        null,
        null,
        1,
        StreamType.Main);

    [Fact]
    public async Task StartUsesConfiguredTestAppAndRandomTestGuid()
    {
        var gateway = new RecordingGateway(registerOnAdd: true);
        var streamIds = new Queue<string>(new[]
        {
            "test_0123456789abcdef0123456789abcdef"
        });
        var controller = new TestStreamProxyController(
            gateway,
            new FixedRuntimeSettingsProvider(),
            maxAttempts: 2,
            maxRegistrationPolls: 2,
            delayAsync: static (_, _) => Task.CompletedTask,
            streamIdFactory: () => streamIds.Dequeue());

        var handle = await controller.StartAsync(Source);

        Assert.Equal("configured-vhost", handle.Vhost);
        Assert.Equal("videomonitor-test", handle.App);
        Assert.Matches("^test_[0-9a-f]{32}$", handle.StreamId);
        Assert.Equal("proxy-key", handle.ProxyKey);
        Assert.Equal(("configured-vhost", "videomonitor-test", handle.StreamId), gateway.AddedIdentity);
    }

    [Fact]
    public async Task CollisionRegeneratesWithoutDeletingExistingStream()
    {
        var gateway = new RecordingGateway(registerOnAdd: true);
        gateway.ExistingStreams.Add("test_0123456789abcdef0123456789abcdef");
        var streamIds = new Queue<string>(new[]
        {
            "test_0123456789abcdef0123456789abcdef",
            "test_fedcba9876543210fedcba9876543210"
        });
        var controller = new TestStreamProxyController(
            gateway,
            new FixedRuntimeSettingsProvider(),
            maxAttempts: 2,
            maxRegistrationPolls: 1,
            delayAsync: static (_, _) => Task.CompletedTask,
            streamIdFactory: () => streamIds.Dequeue());

        var handle = await controller.StartAsync(Source);

        Assert.Equal("test_fedcba9876543210fedcba9876543210", handle.StreamId);
        Assert.Equal(0, gateway.DeleteStreamProxyCalls);
        Assert.Equal(1, gateway.AddStreamProxyCalls);
    }

    [Fact]
    public async Task RegistrationMustBeObservedBeforeSuccess()
    {
        var gateway = new RecordingGateway(registerOnAdd: false);
        var streamIds = new Queue<string>(new[]
        {
            "test_0123456789abcdef0123456789abcdef"
        });
        var controller = new TestStreamProxyController(
            gateway,
            new FixedRuntimeSettingsProvider(),
            maxAttempts: 1,
            maxRegistrationPolls: 2,
            delayAsync: static (_, _) => Task.CompletedTask,
            streamIdFactory: () => streamIds.Dequeue());

        var error = await Assert.ThrowsAsync<TestStreamOperationException>(
            () => controller.StartAsync(Source));

        Assert.Equal(TestStreamErrorCode.MediaRegistrationTimeout, error.Code);
        Assert.Equal(1, gateway.DeleteStreamProxyCalls);
        Assert.Equal("proxy-key", gateway.DeletedProxyKey);
    }

    [Fact]
    public async Task CancellationAfterAddCleansExactProxyKey()
    {
        using var cancellation = new CancellationTokenSource();
        var gateway = new RecordingGateway(registerOnAdd: false)
        {
            CancelAfterAdd = cancellation,
            ProxyKey = "test-proxy-key"
        };
        var controller = new TestStreamProxyController(
            gateway,
            new FixedRuntimeSettingsProvider(),
            maxAttempts: 1,
            maxRegistrationPolls: 2,
            delayAsync: static (_, _) => Task.CompletedTask,
            streamIdFactory: () => "test_0123456789abcdef0123456789abcdef");

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => controller.StartAsync(Source, cancellation.Token));

        Assert.Equal(1, gateway.DeleteStreamProxyCalls);
        Assert.Equal("test-proxy-key", gateway.DeletedProxyKey);
    }

    [Fact]
    public async Task RegistrationPollingCancellationCleansExactProxyKey()
    {
        using var cancellation = new CancellationTokenSource();
        var gateway = new RecordingGateway(registerOnAdd: false)
        {
            ThrowCancellationOnMediaListCall = 2,
            Cancellation = cancellation,
            ProxyKey = "test-proxy-key"
        };
        var controller = new TestStreamProxyController(
            gateway,
            new FixedRuntimeSettingsProvider(),
            maxAttempts: 1,
            maxRegistrationPolls: 2,
            delayAsync: static (_, _) => Task.CompletedTask,
            streamIdFactory: () => "test_0123456789abcdef0123456789abcdef");

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => controller.StartAsync(Source, cancellation.Token));

        Assert.Equal(1, gateway.DeleteStreamProxyCalls);
        Assert.Equal("test-proxy-key", gateway.DeletedProxyKey);
    }

    private sealed class FixedRuntimeSettingsProvider : IMediaRuntimeSettingsProvider
    {
        public Task<MediaRuntimeSettings> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaRuntimeSettings(
                "http://zlm.example",
                "rtsp://playback.example",
                "configured-vhost",
                "videomonitor",
                "videomonitor-test",
                "secret-not-used-by-test",
                30,
                1));
    }

    private sealed class RecordingGateway : IZlmMediaGateway
    {
        private readonly bool registerOnAdd;

        public RecordingGateway(bool registerOnAdd)
        {
            this.registerOnAdd = registerOnAdd;
        }

        public HashSet<string> ExistingStreams { get; } = [];

        public int AddStreamProxyCalls { get; private set; }

        public int DeleteStreamProxyCalls { get; private set; }

        public string? DeletedProxyKey { get; private set; }

        public string ProxyKey { get; init; } = "proxy-key";

        public CancellationTokenSource? CancelAfterAdd { get; init; }

        public int ThrowCancellationOnMediaListCall { get; init; }

        public CancellationTokenSource? Cancellation { get; init; }

        private int mediaListCalls;

        public (string Vhost, string App, string Stream)? AddedIdentity { get; private set; }

        public Task<ZlmApiResponse<IReadOnlyList<ZlmMediaEvidence>>> GetMediaListAsync(
            string vhost,
            string app,
            string? stream,
            CancellationToken cancellationToken = default)
        {
            mediaListCalls++;
            if (mediaListCalls == ThrowCancellationOnMediaListCall)
            {
                Cancellation?.Cancel();
                throw new OperationCanceledException(Cancellation?.Token ?? cancellationToken);
            }

            var evidence = stream is not null && ExistingStreams.Contains(stream)
                ? new[]
                {
                    new ZlmMediaEvidence(
                        "rtsp", vhost, app, stream, 4, "rtsp_pull", null, 1, 1, 0)
                }
                : Array.Empty<ZlmMediaEvidence>();
            return Task.FromResult(new ZlmApiResponse<IReadOnlyList<ZlmMediaEvidence>>(
                true,
                0,
                string.Empty,
                evidence));
        }

        public Task<ZlmApiResponse<ZlmAddStreamProxyData>> AddStreamProxyAsync(
            string vhost,
            string app,
            string stream,
            Uri sourceUri,
            CancellationToken cancellationToken = default)
        {
            AddStreamProxyCalls++;
            AddedIdentity = (vhost, app, stream);
            if (registerOnAdd)
            {
                ExistingStreams.Add(stream);
            }

            CancelAfterAdd?.Cancel();

            return Task.FromResult(new ZlmApiResponse<ZlmAddStreamProxyData>(
                true,
                0,
                string.Empty,
                new ZlmAddStreamProxyData { Key = ProxyKey }));
        }

        public Task<ZlmApiResponse<ZlmDeleteStreamProxyData>> DeleteStreamProxyAsync(
            string proxyKey,
            CancellationToken cancellationToken = default)
        {
            DeleteStreamProxyCalls++;
            DeletedProxyKey = proxyKey;
            return Task.FromResult(new ZlmApiResponse<ZlmDeleteStreamProxyData>(
                true,
                0,
                string.Empty,
                new ZlmDeleteStreamProxyData { Flag = true }));
        }

        public Task<ZlmApiResponse<JsonElement>> CloseExactStreamAsync(
            string schema,
            string vhost,
            string app,
            string stream,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ZlmApiResponse<JsonElement>(
                true,
                0,
                string.Empty,
                default));
    }
}
