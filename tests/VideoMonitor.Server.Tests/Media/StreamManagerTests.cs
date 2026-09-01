using System.Security.Cryptography;
using System.Text;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Infrastructure.ZLMediaKit;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Tests.Media;

public sealed class StreamManagerTests
{
    [Fact]
    public async Task ConcurrentEnsureForSameKeyUsesOneAddProxy()
    {
        var fixture = new StreamManagerFixture();

        var results = await Task.WhenAll(
            fixture.Manager.EnsureStreamAsync(fixture.Request),
            fixture.Manager.EnsureStreamAsync(fixture.Request));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(1, fixture.Gateway.AddStreamProxyCalls);
        Assert.All(results, result => Assert.Equal(
            fixture.Request.Stream,
            result.Stream!.Stream));
    }

    [Fact]
    public async Task ReadyEvidenceRequiresRegistration()
    {
        var fixture = new StreamManagerFixture(registerAfterAdd: false);

        var result = await fixture.Manager.EnsureStreamAsync(fixture.Request);

        Assert.False(result.IsSuccess);
        Assert.DoesNotContain(
            fixture.Manager.GetSnapshot().Streams,
            stream => stream.RuntimeState == StreamRuntimeState.Ready);
    }

    [Fact]
    public async Task SuccessfulEnsureUpdatesObservedAtUtc()
    {
        var fixture = new StreamManagerFixture();

        var result = await fixture.Manager.EnsureStreamAsync(fixture.Request);
        var runtime = Assert.Single(fixture.Manager.GetSnapshot().Streams);

        Assert.True(result.IsSuccess);
        Assert.NotNull(runtime.ObservedAtUtc);
        Assert.NotNull(runtime.LastSuccessUtc);
        Assert.Equal(StreamRuntimeState.Ready, runtime.RuntimeState);
        Assert.Equal(SourceObservation.Reachable, runtime.SourceObservation);
    }

    [Fact]
    public async Task AddProxySuccessWithoutMediaRegistrationFailsAndCleansOwnedProxy()
    {
        var fixture = new StreamManagerFixture(registerAfterAdd: false);

        var result = await fixture.Manager.EnsureStreamAsync(fixture.Request);
        var runtime = Assert.Single(fixture.Manager.GetSnapshot().Streams);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, fixture.Gateway.DeleteStreamProxyCalls);
        Assert.Equal(StreamRuntimeState.Idle, runtime.RuntimeState);
        Assert.Equal(StreamOwnership.NotOwned, runtime.Ownership);
        Assert.NotEqual(SourceObservation.Reachable, runtime.SourceObservation);
    }

    [Fact]
    public async Task UnavailableMediaServerUpdatesReconcilerHealthState()
    {
        var fixture = new StreamManagerFixture();
        fixture.Gateway.MediaListAvailable = false;

        var result = await fixture.Manager.EnsureStreamAsync(fixture.Request);

        Assert.False(result.IsSuccess);
        Assert.Equal(MediaServerHealth.Unavailable, fixture.ReconcilerHealthState.Health);
    }

    [Fact]
    public async Task FormalEnsureRejectsScopeDifferentFromRuntimeSettingsBeforeZlmCall()
    {
        foreach (var scope in new[]
                 {
                     (Vhost: "other-vhost", App: "videomonitor"),
                     (Vhost: "configured-vhost", App: "other-app")
                 })
        {
            var fixture = new StreamManagerFixture();
            var request = fixture.Request with
            {
                Vhost = scope.Vhost,
                App = scope.App
            };

            var result = await fixture.Manager.EnsureStreamAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal("MEDIA_STREAM_SCOPE_INVALID", result.FailureCode);
            Assert.Equal(0, fixture.Gateway.GetMediaListCalls);
            Assert.Equal(0, fixture.Gateway.AddStreamProxyCalls);
            Assert.Equal(0, fixture.Gateway.DeleteStreamProxyCalls);
            Assert.Null(fixture.Gateway.LastCloseIdentity);
        }
    }

    [Fact]
    public async Task CleanupCurrentProcessFailureRetainsOwnershipForRetry()
    {
        var fixture = new StreamManagerFixture();
        await fixture.Manager.EnsureStreamAsync(fixture.Request);
        fixture.Gateway.CurrentEvidence = fixture.CreateEvidence();
        fixture.Gateway.DeleteSucceeds = false;

        await fixture.Manager.CleanupOwnedStreamIfEligibleAsync(fixture.Request.CatalogKey!.Value);

        var failedRuntime = Assert.Single(fixture.Manager.GetSnapshot().Streams);
        Assert.Equal(StreamOwnership.OwnedCurrentProcess, failedRuntime.Ownership);
        Assert.Equal("MEDIA_STREAM_CLEANUP_FAILED", failedRuntime.SafeLastErrorCode);
        Assert.Equal(1, fixture.Gateway.DeleteStreamProxyCalls);

        fixture.Gateway.DeleteSucceeds = true;
        await fixture.Manager.CleanupOwnedStreamIfEligibleAsync(fixture.Request.CatalogKey!.Value);

        var retriedRuntime = Assert.Single(fixture.Manager.GetSnapshot().Streams);
        Assert.Equal(StreamOwnership.NotOwned, retriedRuntime.Ownership);
        Assert.Equal(2, fixture.Gateway.DeleteStreamProxyCalls);
        Assert.Equal(new[] { "proxy-key", "proxy-key" }, fixture.Gateway.DeletedProxyKeys);
    }

    [Fact]
    public async Task CleanupAdoptedFailureDoesNotMarkIdle()
    {
        var fixture = new StreamManagerFixture(registerAfterAdd: false);
        fixture.Gateway.CurrentEvidence = fixture.CreateEvidence();
        var ensured = await fixture.Manager.EnsureStreamAsync(fixture.Request);
        Assert.True(ensured.IsSuccess);
        fixture.Gateway.CloseSucceeds = false;

        await fixture.Manager.CleanupOwnedStreamIfEligibleAsync(fixture.Request.CatalogKey!.Value);

        var failedRuntime = Assert.Single(fixture.Manager.GetSnapshot().Streams);
        Assert.Equal(StreamOwnership.OwnedAdopted, failedRuntime.Ownership);
        Assert.Equal(StreamRuntimeState.Faulted, failedRuntime.RuntimeState);
        Assert.Equal("MEDIA_STREAM_CLEANUP_FAILED", failedRuntime.SafeLastErrorCode);

        fixture.Gateway.CloseSucceeds = true;
        await fixture.Manager.CleanupOwnedStreamIfEligibleAsync(fixture.Request.CatalogKey!.Value);

        var retriedRuntime = Assert.Single(fixture.Manager.GetSnapshot().Streams);
        Assert.Equal(StreamOwnership.NotOwned, retriedRuntime.Ownership);
        Assert.Equal(2, fixture.Gateway.CloseCalls);
        Assert.All(
            fixture.Gateway.ClosedIdentities,
            identity => Assert.Equal(
                ("rtsp", "configured-vhost", "videomonitor", fixture.Request.Stream),
                identity));
    }

    [Fact]
    public async Task AddProxyTimeoutCleanupFailureRetainsOwnershipForRetry()
    {
        var fixture = new StreamManagerFixture(registerAfterAdd: false);
        fixture.Gateway.DeleteSucceeds = false;

        var result = await fixture.Manager.EnsureStreamAsync(fixture.Request);

        Assert.False(result.IsSuccess);
        Assert.Equal("MEDIA_STREAM_CLEANUP_FAILED", result.FailureCode);
        var runtime = Assert.Single(fixture.Manager.GetSnapshot().Streams);
        Assert.Equal(StreamOwnership.OwnedCurrentProcess, runtime.Ownership);
        Assert.Equal(1, fixture.Gateway.DeleteStreamProxyCalls);

        fixture.Gateway.CurrentEvidence = fixture.CreateEvidence();
        fixture.Gateway.DeleteSucceeds = true;
        await fixture.Manager.CleanupOwnedStreamIfEligibleAsync(fixture.Request.CatalogKey!.Value);

        Assert.Equal(new[] { "proxy-key", "proxy-key" }, fixture.Gateway.DeletedProxyKeys);
        Assert.Equal(
            StreamOwnership.NotOwned,
            Assert.Single(fixture.Manager.GetSnapshot().Streams).Ownership);
    }

    [Fact]
    public async Task ReconcileMarksPreviouslyReadyMissingStreamIdle()
    {
        var fixture = new StreamManagerFixture();
        var ensured = await fixture.Manager.EnsureStreamAsync(fixture.Request);
        Assert.True(ensured.IsSuccess);
        fixture.Gateway.CurrentEvidence = null;

        await fixture.Manager.ReconcileAsync();

        var runtime = Assert.Single(fixture.Manager.GetSnapshot().Streams);
        Assert.Equal(StreamRuntimeState.Idle, runtime.RuntimeState);
        Assert.Equal(StreamOwnership.NotOwned, runtime.Ownership);
        Assert.Equal(new ViewerCount(0), runtime.ViewerCount);
        Assert.NotNull(runtime.ObservedAtUtc);
    }

    [Fact]
    public async Task ReconcileZeroReaderUsesConfiguredGraceBeforeCleanup()
    {
        var fixture = new StreamManagerFixture();
        var startedAt = fixture.Now;
        var ensured = await fixture.Manager.EnsureStreamAsync(fixture.Request);
        Assert.True(ensured.IsSuccess);

        await fixture.Manager.ReconcileAsync();
        Assert.Empty(fixture.Gateway.DeletedProxyKeys);

        fixture.Now = startedAt.AddSeconds(29);
        await fixture.Manager.ReconcileAsync();
        Assert.Empty(fixture.Gateway.DeletedProxyKeys);

        fixture.Now = startedAt.AddSeconds(30);
        await fixture.Manager.ReconcileAsync();

        Assert.Equal(new[] { "proxy-key" }, fixture.Gateway.DeletedProxyKeys);
        Assert.Equal(
            StreamOwnership.NotOwned,
            Assert.Single(fixture.Manager.GetSnapshot().Streams).Ownership);
    }

    [Fact]
    public async Task ReconcileReaderRecoveryDuringNoReaderGraceCancelsCleanup()
    {
        var fixture = new StreamManagerFixture();
        var startedAt = fixture.Now;
        var ensured = await fixture.Manager.EnsureStreamAsync(fixture.Request);
        Assert.True(ensured.IsSuccess);

        await fixture.Manager.ReconcileAsync();
        fixture.Now = startedAt.AddSeconds(10);
        fixture.Gateway.CurrentEvidence = fixture.CreateEvidence() with
        {
            TotalReaderCount = 1
        };
        await fixture.Manager.ReconcileAsync();

        fixture.Now = startedAt.AddSeconds(30);
        fixture.Gateway.CurrentEvidence = fixture.CreateEvidence();
        await fixture.Manager.ReconcileAsync();

        Assert.Empty(fixture.Gateway.DeletedProxyKeys);
    }

    [Fact]
    public async Task NotOwnedIdentityConflictFailsClosedWithoutDeleteOrAdd()
    {
        var fixture = new StreamManagerFixture(registerAfterAdd: false);
        fixture.Gateway.CurrentEvidence = fixture.CreateEvidence();
        fixture.Gateway.CurrentEvidence = fixture.Gateway.CurrentEvidence with
        {
            OriginUrl = "rtsp://another-camera.example/live"
        };

        var result = await fixture.Manager.EnsureStreamAsync(fixture.Request);

        Assert.False(result.IsSuccess);
        Assert.Equal("MediaStreamIdentityConflict", result.FailureCode);
        Assert.Equal(0, fixture.Gateway.DeleteStreamProxyCalls);
        Assert.Equal(0, fixture.Gateway.AddStreamProxyCalls);
    }

    [Fact]
    public async Task DifferentKeysProceedIndependently()
    {
        var keyA = new MediaStreamKey(
            Guid.Parse("7b000000-0000-0000-0000-000000000001"),
            Guid.Parse("7c000000-0000-0000-0000-000000000001"),
            StreamType.Main);
        var keyB = new MediaStreamKey(
            Guid.Parse("7b000000-0000-0000-0000-000000000002"),
            Guid.Parse("7c000000-0000-0000-0000-000000000002"),
            StreamType.Main);
        var sourceA = new Uri("rtsp://camera-a.example/live");
        var sourceB = new Uri("rtsp://camera-b.example/live");
        var gateway = new MultiKeyGateway(keyA, sourceA);
        var resolver = new MultiSourceResolver(
            new ResolvedCameraSource(keyA, sourceA, Fingerprint(sourceA)),
            new ResolvedCameraSource(keyB, sourceB, Fingerprint(sourceB)));
        var manager = new StreamManager(
            gateway,
            resolver,
            new FakeRuntimeSettingsProvider(),
            new MediaRuntimeRegistry(),
            new MediaStreamGate(),
            new MediaOwnershipClassifier(
                "configured-vhost",
                "videomonitor",
                _ => true),
            new SourceBindingVerifier(),
            (_, _) => Task.CompletedTask,
            maxRegistrationPolls: 1);

        var pendingA = manager.EnsureStreamAsync(Request(keyA, sourceA));
        await gateway.FirstKeyQueryStarted.Task;
        var resultB = await manager.EnsureStreamAsync(Request(keyB, sourceB));

        Assert.True(resultB.IsSuccess);
        gateway.ReleaseFirstKeyQuery.SetResult(null);
        var resultA = await pendingA;
        Assert.True(resultA.IsSuccess);
        Assert.Equal(1, gateway.AddCallsFor(keyA));
        Assert.Equal(1, gateway.AddCallsFor(keyB));
    }

    [Fact]
    public async Task EnsureQueriesBeforeAddingProxy()
    {
        var fixture = new StreamManagerFixture();

        var result = await fixture.Manager.EnsureStreamAsync(fixture.Request);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "get", "add", "get" }, fixture.Gateway.Calls);
    }

    [Fact]
    public async Task CleanupOwnedCurrentProcessUsesExactProxyKey()
    {
        var fixture = new StreamManagerFixture();
        await fixture.Manager.EnsureStreamAsync(fixture.Request);
        fixture.Gateway.CurrentEvidence = fixture.CreateEvidence();

        await fixture.Manager.CleanupOwnedStreamIfEligibleAsync(fixture.Request.CatalogKey!.Value);

        Assert.Equal(new[] { "proxy-key" }, fixture.Gateway.DeletedProxyKeys);
    }

    [Fact]
    public async Task CleanupDoesNotDeleteWhenSourceBindingNoLongerMatches()
    {
        var fixture = new StreamManagerFixture();
        await fixture.Manager.EnsureStreamAsync(fixture.Request);
        fixture.Gateway.CurrentEvidence = fixture.CreateEvidence() with
        {
            OriginUrl = "rtsp://another-camera.example/live"
        };

        await fixture.Manager.CleanupOwnedStreamIfEligibleAsync(fixture.Request.CatalogKey!.Value);

        Assert.Empty(fixture.Gateway.DeletedProxyKeys);
        Assert.Null(fixture.Gateway.LastCloseIdentity);
    }

    [Fact]
    public async Task CleanupAdoptedUsesExactCloseIdentity()
    {
        var fixture = new StreamManagerFixture(registerAfterAdd: false);
        fixture.Gateway.CurrentEvidence = fixture.CreateEvidence();
        var ensured = await fixture.Manager.EnsureStreamAsync(fixture.Request);

        Assert.True(ensured.IsSuccess);
        await fixture.Manager.CleanupOwnedStreamIfEligibleAsync(fixture.Request.CatalogKey!.Value);

        Assert.Equal(
            ("rtsp", "configured-vhost", "videomonitor", fixture.Request.Stream),
            fixture.Gateway.LastCloseIdentity);
    }

    private sealed class StreamManagerFixture
    {
        private readonly MediaStreamKey key = new(
            Guid.Parse("71000000-0000-0000-0000-000000000001"),
            Guid.Parse("72000000-0000-0000-0000-000000000001"),
            StreamType.Main);

        public StreamManagerFixture(bool registerAfterAdd = true)
        {
            SourceUri = new Uri("rtsp://camera.example/live");
            Gateway = new FakeGateway(this, registerAfterAdd);
            var resolver = new FakeSourceResolver(
                new ResolvedCameraSource(key, SourceUri, Fingerprint(SourceUri)));
            Manager = new StreamManager(
                Gateway,
                resolver,
                new FakeRuntimeSettingsProvider(),
                new MediaRuntimeRegistry(),
                new MediaStreamGate(),
                new MediaOwnershipClassifier(
                    "configured-vhost",
                    "videomonitor",
                    requestedKey => requestedKey == key),
                new SourceBindingVerifier(),
                (_, _) => Task.CompletedTask,
                maxRegistrationPolls: 2,
                reconcilerHealthState: ReconcilerHealthState,
                utcNow: () => Now);
        }

        public Uri SourceUri { get; }

        public DateTimeOffset Now { get; set; } = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

        public FakeGateway Gateway { get; }

        public MediaServerHealthState ReconcilerHealthState { get; } = new();

        public StreamManager Manager { get; }

        public MediaStreamRequest Request => new(
            MediaStreamNamespace.Formal,
            key,
            "configured-vhost",
            "videomonitor",
            MediaStreamIdGenerator.GenerateFormal(key),
            SourceUri);

        public ZlmMediaEvidence CreateEvidence() => new(
            "rtsp",
            "configured-vhost",
            "videomonitor",
            Request.Stream,
            4,
            "rtsp_pull",
            SourceUri.ToString(),
            1,
            1,
            0);

        private static string Fingerprint(Uri uri)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(uri.ToString()));
            return Convert.ToHexString(bytes);
        }
    }

    private static MediaStreamRequest Request(MediaStreamKey key, Uri sourceUri) => new(
        MediaStreamNamespace.Formal,
        key,
        "configured-vhost",
        "videomonitor",
        MediaStreamIdGenerator.GenerateFormal(key),
        sourceUri);

    private static string Fingerprint(Uri uri)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(uri.ToString()));
        return Convert.ToHexString(bytes);
    }

    private sealed class FakeRuntimeSettingsProvider : IMediaRuntimeSettingsProvider
    {
        public Task<MediaRuntimeSettings> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MediaRuntimeSettings(
                "http://127.0.0.1:8080",
                "",
                "configured-vhost",
                "videomonitor",
                "videomonitor-test",
                "",
                30,
                1));
    }

    private sealed class FakeSourceResolver : ICameraSourceResolver
    {
        private readonly ResolvedCameraSource source;

        public FakeSourceResolver(ResolvedCameraSource source)
        {
            this.source = source;
        }

        public Task<ResolvedCameraSource> ResolveAsync(
            MediaStreamKey key,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(source.Key, key);
            return Task.FromResult(source);
        }
    }

    private sealed class FakeGateway : IZlmMediaGateway
    {
        private readonly StreamManagerFixture fixture;
        private readonly bool registerAfterAdd;
        private readonly object sync = new();

        public FakeGateway(StreamManagerFixture fixture, bool registerAfterAdd)
        {
            this.fixture = fixture;
            this.registerAfterAdd = registerAfterAdd;
        }

        public int AddStreamProxyCalls { get; private set; }

        public int DeleteStreamProxyCalls { get; private set; }

        public int GetMediaListCalls { get; private set; }

        public List<string> Calls { get; } = [];

        public List<string> DeletedProxyKeys { get; } = [];

        public bool MediaListAvailable { get; set; } = true;

        public bool DeleteSucceeds { get; set; } = true;

        public bool CloseSucceeds { get; set; } = true;

        public int CloseCalls { get; private set; }

        public List<(string Schema, string Vhost, string App, string Stream)> ClosedIdentities { get; } = [];

        public (string Schema, string Vhost, string App, string Stream)? LastCloseIdentity { get; private set; }

        public ZlmMediaEvidence? CurrentEvidence { get; set; }

        public Task<ZlmApiResponse<IReadOnlyList<ZlmMediaEvidence>>> GetMediaListAsync(
            string vhost,
            string app,
            string? stream,
            CancellationToken cancellationToken = default)
        {
            if (!MediaListAvailable)
            {
                return Task.FromResult(new ZlmApiResponse<IReadOnlyList<ZlmMediaEvidence>>(
                    false,
                    -1,
                    string.Empty,
                    Array.Empty<ZlmMediaEvidence>()));
            }

            IReadOnlyList<ZlmMediaEvidence> result;
            lock (sync)
            {
                Calls.Add("get");
                GetMediaListCalls++;
                result = CurrentEvidence is null
                    ? Array.Empty<ZlmMediaEvidence>()
                    : new[] { CurrentEvidence };
            }

            return Task.FromResult(new ZlmApiResponse<IReadOnlyList<ZlmMediaEvidence>>(
                true,
                0,
                string.Empty,
                result));
        }

        public Task<ZlmApiResponse<ZlmAddStreamProxyData>> AddStreamProxyAsync(
            string vhost,
            string app,
            string stream,
            Uri sourceUri,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                Calls.Add("add");
                AddStreamProxyCalls++;
                if (registerAfterAdd)
                {
                    CurrentEvidence = fixture.CreateEvidence();
                }
            }

            return Task.FromResult(new ZlmApiResponse<ZlmAddStreamProxyData>(
                true,
                0,
                string.Empty,
                new ZlmAddStreamProxyData { Key = "proxy-key" }));
        }

        public Task<ZlmApiResponse<ZlmDeleteStreamProxyData>> DeleteStreamProxyAsync(
            string proxyKey,
            CancellationToken cancellationToken = default)
        {
            DeletedProxyKeys.Add(proxyKey);
            DeleteStreamProxyCalls++;
            return Task.FromResult(new ZlmApiResponse<ZlmDeleteStreamProxyData>(
                DeleteSucceeds,
                DeleteSucceeds ? 0 : -1,
                string.Empty,
                new ZlmDeleteStreamProxyData { Flag = DeleteSucceeds }));
        }

        public Task<ZlmApiResponse<System.Text.Json.JsonElement>> CloseExactStreamAsync(
            string schema,
            string vhost,
            string app,
            string stream,
            CancellationToken cancellationToken = default)
        {
            LastCloseIdentity = (schema, vhost, app, stream);
            ClosedIdentities.Add((schema, vhost, app, stream));
            CloseCalls++;
            return Task.FromResult(new ZlmApiResponse<System.Text.Json.JsonElement>(
                CloseSucceeds,
                CloseSucceeds ? 0 : -1,
                string.Empty,
                default));
        }
    }

    private sealed class MultiSourceResolver : ICameraSourceResolver
    {
        private readonly Dictionary<MediaStreamKey, ResolvedCameraSource> sources;

        public MultiSourceResolver(params ResolvedCameraSource[] sources)
        {
            this.sources = sources.ToDictionary(source => source.Key);
        }

        public Task<ResolvedCameraSource> ResolveAsync(
            MediaStreamKey key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(sources[key]);
    }

    private sealed class MultiKeyGateway : IZlmMediaGateway
    {
        private readonly MediaStreamKey firstKey;
        private readonly Uri firstSource;
        private readonly Dictionary<string, ZlmMediaEvidence> registered = [];
        private readonly Dictionary<MediaStreamKey, int> addCalls = [];

        public MultiKeyGateway(MediaStreamKey firstKey, Uri firstSource)
        {
            this.firstKey = firstKey;
            this.firstSource = firstSource;
        }

        public TaskCompletionSource<object?> FirstKeyQueryStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?> ReleaseFirstKeyQuery { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int AddCallsFor(MediaStreamKey key) => addCalls.GetValueOrDefault(key);

        public async Task<ZlmApiResponse<IReadOnlyList<ZlmMediaEvidence>>> GetMediaListAsync(
            string vhost,
            string app,
            string? stream,
            CancellationToken cancellationToken = default)
        {
            if (stream == firstKey.ToFormalStreamId()
                && !FirstKeyQueryStarted.Task.IsCompleted)
            {
                FirstKeyQueryStarted.SetResult(null);
                await ReleaseFirstKeyQuery.Task.WaitAsync(cancellationToken);
            }

            var result = stream is not null && registered.TryGetValue(stream, out var evidence)
                ? new[] { evidence }
                : Array.Empty<ZlmMediaEvidence>();
            return new ZlmApiResponse<IReadOnlyList<ZlmMediaEvidence>>(
                true,
                0,
                string.Empty,
                result);
        }

        public Task<ZlmApiResponse<ZlmAddStreamProxyData>> AddStreamProxyAsync(
            string vhost,
            string app,
            string stream,
            Uri sourceUri,
            CancellationToken cancellationToken = default)
        {
            var key = MediaStreamIdGenerator.TryParseFormal(stream, out var parsed)
                ? parsed
                : default;
            addCalls[key] = addCalls.GetValueOrDefault(key) + 1;
            registered[stream] = new ZlmMediaEvidence(
                "rtsp",
                vhost,
                app,
                stream,
                4,
                "rtsp_pull",
                sourceUri.ToString(),
                1,
                1,
                0);
            return Task.FromResult(new ZlmApiResponse<ZlmAddStreamProxyData>(
                true,
                0,
                string.Empty,
                new ZlmAddStreamProxyData { Key = "proxy-" + stream }));
        }

        public Task<ZlmApiResponse<ZlmDeleteStreamProxyData>> DeleteStreamProxyAsync(
            string proxyKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ZlmApiResponse<ZlmDeleteStreamProxyData>(
                true,
                0,
                string.Empty,
                new ZlmDeleteStreamProxyData()));

        public Task<ZlmApiResponse<System.Text.Json.JsonElement>> CloseExactStreamAsync(
            string schema,
            string vhost,
            string app,
            string stream,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ZlmApiResponse<System.Text.Json.JsonElement>(
                true,
                0,
                string.Empty,
                default));
    }
}
