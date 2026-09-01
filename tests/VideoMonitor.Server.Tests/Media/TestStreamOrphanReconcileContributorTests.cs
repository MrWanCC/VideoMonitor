using System.Text.Json;
using VideoMonitor.Core.Media;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.ZLMediaKit;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Tests.Media;

public sealed class TestStreamOrphanReconcileContributorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RestartOrphanRequiresConfiguredVhostTestAppGuidOriginAndAge()
    {
        const string stream = "test_0123456789abcdef0123456789abcdef";
        var gateway = new RecordingGateway
        {
            Evidence = new[]
            {
                new ZlmMediaEvidence(
                    "rtsp",
                    "configured-vhost",
                    "videomonitor-test",
                    stream,
                    4,
                    "rtmp_push",
                    null,
                    Now.AddMinutes(-3).ToUnixTimeSeconds(),
                    null,
                    0)
            }
        };
        var contributor = new TestStreamOrphanReconcileContributor(
            gateway,
            new FixedSettingsProvider(),
            new TestSessionRegistry(() => Now),
            () => Now);

        await contributor.ReconcileAsync();

        Assert.Equal(1, gateway.CloseCalls);
        Assert.Equal(("rtsp", "configured-vhost", "videomonitor-test", stream), gateway.ClosedIdentity);
    }

    [Fact]
    public async Task NonMatchingEvidenceIsUntouched()
    {
        var gateway = new RecordingGateway
        {
            Evidence = new[]
            {
                Evidence("other-vhost", "videomonitor-test", "test_0123456789abcdef0123456789abcdef"),
                Evidence("configured-vhost", "other-app", "test_0123456789abcdef0123456789abcdef"),
                Evidence("configured-vhost", "videomonitor-test", "test_not-a-guid"),
                Evidence("configured-vhost", "videomonitor-test", "test_fedcba9876543210fedcba9876543210", 1),
                Evidence("configured-vhost", "videomonitor-test", "test_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", 4, ageMinutes: 1)
            }
        };
        var contributor = new TestStreamOrphanReconcileContributor(
            gateway,
            new FixedSettingsProvider(),
            new TestSessionRegistry(() => Now),
            () => Now);

        await contributor.ReconcileAsync();

        Assert.Equal(0, gateway.CloseCalls);
    }

    [Fact]
    public async Task ExpiredCleanupFailureRetainsCurrentProcessHandle()
    {
        var currentTime = Now;
        var registry = new TestSessionRegistry(() => currentTime);
        var session = registry.Add(
            new TestStreamProxyHandle(
                "configured-vhost",
                "videomonitor-test",
                "test_0123456789abcdef0123456789abcdef",
                "test-proxy-key",
                Now),
            null,
            null,
            new Uri("rtsp://playback.example/live"));
        currentTime = Now.AddMinutes(2).AddTicks(1);
        var gateway = new RecordingGateway
        {
            DeleteResults = new Queue<bool>(new[] { false, true })
        };
        var contributor = new TestStreamOrphanReconcileContributor(
            gateway,
            new FixedSettingsProvider(),
            registry,
            () => currentTime);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => contributor.ReconcileAsync());
        Assert.True(registry.TryGet(session.SessionId, out _));

        await contributor.ReconcileAsync();

        Assert.Equal(2, gateway.DeleteCalls);
        Assert.Equal("test-proxy-key", gateway.DeletedProxyKey);
        Assert.False(registry.TryGet(session.SessionId, out _));
    }

    private static ZlmMediaEvidence Evidence(
        string vhost,
        string app,
        string stream,
        int originType = 4,
        int ageMinutes = 3) => new(
            "rtsp",
            vhost,
            app,
            stream,
            originType,
            "rtmp_push",
            null,
            Now.AddMinutes(-ageMinutes).ToUnixTimeSeconds(),
            null,
            0);

    private sealed class FixedSettingsProvider : IMediaRuntimeSettingsProvider
    {
        public Task<MediaRuntimeSettings> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaRuntimeSettings(
                "http://zlm.example",
                "rtsp://playback.example",
                "configured-vhost",
                "videomonitor",
                "videomonitor-test",
                string.Empty,
                30,
                1));
    }

    private sealed class RecordingGateway : IZlmMediaGateway
    {
        public IReadOnlyList<ZlmMediaEvidence> Evidence { get; init; } = [];

        public int CloseCalls { get; private set; }

        public (string Schema, string Vhost, string App, string Stream)? ClosedIdentity { get; private set; }

        public Queue<bool> DeleteResults { get; init; } = new();

        public int DeleteCalls { get; private set; }

        public string? DeletedProxyKey { get; private set; }

        public Task<ZlmApiResponse<IReadOnlyList<ZlmMediaEvidence>>> GetMediaListAsync(
            string vhost,
            string app,
            string? stream,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ZlmApiResponse<IReadOnlyList<ZlmMediaEvidence>>(
                true, 0, string.Empty, Evidence));

        public Task<ZlmApiResponse<ZlmAddStreamProxyData>> AddStreamProxyAsync(
            string vhost,
            string app,
            string stream,
            Uri sourceUri,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ZlmApiResponse<ZlmDeleteStreamProxyData>> DeleteStreamProxyAsync(
            string proxyKey,
            CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            DeletedProxyKey = proxyKey;
            var success = DeleteResults.Count == 0 || DeleteResults.Dequeue();
            return Task.FromResult(new ZlmApiResponse<ZlmDeleteStreamProxyData>(
                true,
                0,
                string.Empty,
                new ZlmDeleteStreamProxyData { Flag = success }));
        }

        public Task<ZlmApiResponse<JsonElement>> CloseExactStreamAsync(
            string schema,
            string vhost,
            string app,
            string stream,
            CancellationToken cancellationToken = default)
        {
            CloseCalls++;
            ClosedIdentity = (schema, vhost, app, stream);
            return Task.FromResult(new ZlmApiResponse<JsonElement>(true, 0, string.Empty, default));
        }
    }
}
