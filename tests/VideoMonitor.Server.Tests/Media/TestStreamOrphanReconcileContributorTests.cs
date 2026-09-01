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

        await contributor.ReconcileAsync();
        Assert.True(registry.TryGet(session.SessionId, out _));

        await contributor.ReconcileAsync();

        Assert.Equal(2, gateway.DeleteCalls);
        Assert.Equal("test-proxy-key", gateway.DeletedProxyKey);
        Assert.False(registry.TryGet(session.SessionId, out _));
    }

    [Fact]
    public async Task PendingCleanupRetriesExactProxyAndRemovesOnlyAfterSuccess()
    {
        var registry = new TestSessionRegistry(() => Now);
        var handle = new TestStreamProxyHandle(
            "configured-vhost",
            "videomonitor-test",
            "test_0123456789abcdef0123456789abcdef",
            "pending-proxy-key",
            Now);
        registry.RegisterPendingCleanup(handle);
        var gateway = new RecordingGateway
        {
            DeleteResults = new Queue<bool>(new[] { false, true })
        };
        var contributor = new TestStreamOrphanReconcileContributor(
            gateway,
            new FixedSettingsProvider(),
            registry,
            () => Now);

        await contributor.ReconcileAsync();
        Assert.Single(registry.GetPendingCleanup());

        await contributor.ReconcileAsync();
        Assert.Empty(registry.GetPendingCleanup());
        Assert.Equal(new[] { "pending-proxy-key", "pending-proxy-key" }, gateway.DeletedProxyKeys);
    }

    [Fact]
    public async Task OneExpiredCleanupFailureDoesNotBlockOtherExpiredSessions()
    {
        var currentTime = Now;
        var registry = new TestSessionRegistry(() => currentTime);
        var first = registry.Add(
            new TestStreamProxyHandle(
                "configured-vhost",
                "videomonitor-test",
                "test_0123456789abcdef0123456789abcdef",
                "expired-a",
                Now),
            null,
            null,
            new Uri("rtsp://playback.example/a"));
        var second = registry.Add(
            new TestStreamProxyHandle(
                "configured-vhost",
                "videomonitor-test",
                "test_fedcba9876543210fedcba9876543210",
                "expired-b",
                Now),
            null,
            null,
            new Uri("rtsp://playback.example/b"));
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

        await contributor.ReconcileAsync();

        Assert.True(registry.TryGet(first.SessionId, out _));
        Assert.False(registry.TryGet(second.SessionId, out _));
        Assert.Equal(new[] { "expired-a", "expired-b" }, gateway.DeletedProxyKeys);
    }

    [Fact]
    public async Task ExpiredCleanupFailureDoesNotBlockSafeRestartOrphanScan()
    {
        var currentTime = Now;
        var registry = new TestSessionRegistry(() => currentTime);
        var expired = registry.Add(
            new TestStreamProxyHandle(
                "configured-vhost",
                "videomonitor-test",
                "test_0123456789abcdef0123456789abcdef",
                "expired-proxy-key",
                Now),
            null,
            null,
            new Uri("rtsp://playback.example/live"));
        currentTime = Now.AddMinutes(2).AddTicks(1);
        const string orphan = "test_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var gateway = new RecordingGateway
        {
            DeleteResults = new Queue<bool>(new[] { false }),
            Evidence = new[]
            {
                new ZlmMediaEvidence(
                    "rtsp",
                    "configured-vhost",
                    "videomonitor-test",
                    orphan,
                    4,
                    "rtsp_pull",
                    null,
                    Now.AddMinutes(-3).ToUnixTimeSeconds(),
                    null,
                    0)
            }
        };
        var contributor = new TestStreamOrphanReconcileContributor(
            gateway,
            new FixedSettingsProvider(),
            registry,
            () => currentTime);

        await contributor.ReconcileAsync();

        Assert.True(registry.TryGet(expired.SessionId, out _));
        Assert.Equal(1, gateway.CloseCalls);
        Assert.Equal(("rtsp", "configured-vhost", "videomonitor-test", orphan), gateway.ClosedIdentity);
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

        public List<string> DeletedProxyKeys { get; } = [];

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
            DeletedProxyKeys.Add(proxyKey);
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
