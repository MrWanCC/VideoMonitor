using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Server.Catalog;
using VideoMonitor.Server.Media;
using VideoMonitor.Server.Playback;

namespace VideoMonitor.Server.Tests.Media;

public sealed class MediaDiagnosticsApiTests
{
    private static readonly Guid DeviceId =
        Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid ChannelId =
        Guid.Parse("b1000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task DiagnosticsApiReturnsSafeDto()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/media/diagnostics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("activeStreamCount", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("originUrl", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PlaybackTicket", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RuntimeEndpointRemainsBackwardCompatible()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/media/runtime");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DiagnosticsApiDoesNotExposeStopAll()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/api/v1/media/diagnostics/stop-all",
            content: null);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DiagnosticsApiDoesNotExposePlaybackTicket()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/media/diagnostics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("PlaybackTicket", body,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiagnosticsApiDoesNotExposePlaybackUrl()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/media/diagnostics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("PlaybackUrl", body,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshReturns202()
    {
        var signal = new RecordingSignal(ReconcileSignalResult.Accepted);
        using var baseFactory = new TestServerFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMediaReconcileSignal>();
                services.AddSingleton<IMediaReconcileSignal>(signal);
            }));
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/api/v1/media/diagnostics/refresh",
            content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, signal.CallCount);
    }

    [Fact]
    public async Task RefreshUnavailableReturns503Safely()
    {
        var signal = new RecordingSignal(ReconcileSignalResult.Unavailable);
        using var baseFactory = new TestServerFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMediaReconcileSignal>();
                services.AddSingleton<IMediaReconcileSignal>(signal);
            }));
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/api/v1/media/diagnostics/refresh",
            content: null);
        var error = await response.Content.ReadFromJsonAsync<CatalogErrorDto>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("MEDIA_DIAGNOSTICS_UNAVAILABLE", error.Code);
        Assert.Equal("Media diagnostics are unavailable.", error.Message);
    }

    [Fact]
    public void RefreshSignalUsesExistingReconcilerSingleton()
    {
        using var factory = new TestServerFactory();

        var reconciler = factory.Services
            .GetRequiredService<MediaReconcilerHostedService>();
        var signal = factory.Services
            .GetRequiredService<IMediaReconcileSignal>();

        Assert.Same(reconciler, signal);
    }

    [Fact]
    public async Task DiagnosticsUnavailableReturns503Safely()
    {
        using var factory = new TestServerFactory(failMachineProtection: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/media/diagnostics");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("MEDIA_DIAGNOSTICS_UNAVAILABLE", body,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SourceUri", body,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FaultedRetryReturns202WithoutBodyPayload()
    {
        var key = CreateKey();
        var formal = new RecordingFormalEnsureService(Success(key));
        using var baseFactory = new TestServerFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamManager>();
                services.AddSingleton<IStreamManager>(
                    new FixedStreamManager(Snapshot(key, StreamRuntimeState.Faulted)));
                services.RemoveAll<IFormalStreamEnsureService>();
                services.AddSingleton<IFormalStreamEnsureService>(formal);
            }));
        using var client = factory.CreateClient();

        var response = await client.PostAsync(RetryPath(key), content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(body);
        Assert.DoesNotContain("MediaIdentity", body,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, formal.CallCount);
    }

    [Fact]
    public async Task NonFaultedRetryReturns409()
    {
        var key = CreateKey();
        var formal = new RecordingFormalEnsureService(Success(key));
        using var baseFactory = new TestServerFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamManager>();
                services.AddSingleton<IStreamManager>(
                    new FixedStreamManager(Snapshot(key, StreamRuntimeState.Ready)));
                services.RemoveAll<IFormalStreamEnsureService>();
                services.AddSingleton<IFormalStreamEnsureService>(formal);
            }));
        using var client = factory.CreateClient();

        var response = await client.PostAsync(RetryPath(key), content: null);
        var error = await response.Content.ReadFromJsonAsync<CatalogErrorDto>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("MEDIA_STREAM_NOT_FAULTED", error.Code);
        Assert.Equal(0, formal.CallCount);
    }

    [Fact]
    public async Task UnknownRuntimeIdentityReturns404()
    {
        var key = CreateKey();
        using var baseFactory = new TestServerFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamManager>();
                services.AddSingleton<IStreamManager>(
                    new FixedStreamManager(
                        new MediaRuntimeSnapshot(
                            MediaServerHealth.Healthy,
                            Array.Empty<MediaStreamRuntimeInfo>())));
            }));
        using var client = factory.CreateClient();

        var response = await client.PostAsync(RetryPath(key), content: null);
        var error = await response.Content.ReadFromJsonAsync<CatalogErrorDto>();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("MEDIA_STREAM_NOT_FOUND", error.Code);
    }

    [Fact]
    public async Task InvalidRetryIdentityReturns400()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/api/v1/media/diagnostics/streams/not-a-guid/"
            + $"{ChannelId}/main/retry",
            content: null);
        var error = await response.Content.ReadFromJsonAsync<CatalogErrorDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("CATALOG_VALIDATION_FAILED", error.Code);
    }

    [Fact]
    public async Task InvalidStreamTypeReturns400()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/v1/media/diagnostics/streams/{DeviceId}/{ChannelId}/unknown/retry",
            content: null);
        var error = await response.Content.ReadFromJsonAsync<CatalogErrorDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("CATALOG_VALIDATION_FAILED", error.Code);
    }

    [Fact]
    public async Task RetryUnavailableReturns503Safely()
    {
        var key = CreateKey();
        var formal = new RecordingFormalEnsureService(
            new CatalogOperationResult<FormalStreamEnsureResult>(
                false,
                null,
                503,
                new CatalogErrorDto("MEDIA_UNAVAILABLE", "safe failure")));
        using var baseFactory = new TestServerFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamManager>();
                services.AddSingleton<IStreamManager>(
                    new FixedStreamManager(Snapshot(key, StreamRuntimeState.Faulted)));
                services.RemoveAll<IFormalStreamEnsureService>();
                services.AddSingleton<IFormalStreamEnsureService>(formal);
            }));
        using var client = factory.CreateClient();

        var response = await client.PostAsync(RetryPath(key), content: null);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("MEDIA_DIAGNOSTICS_RETRY_FAILED", body,
            StringComparison.Ordinal);
        Assert.DoesNotContain("FormalStreamEnsureResult", body,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("safe failure", body,
            StringComparison.OrdinalIgnoreCase);
    }

    private static MediaStreamKey CreateKey() =>
        new(DeviceId, ChannelId, StreamType.Main);

    private static string RetryPath(MediaStreamKey key) =>
        $"/api/v1/media/diagnostics/streams/{key.DeviceId}/{key.ChannelId}/main/retry";

    private static MediaRuntimeSnapshot Snapshot(
        MediaStreamKey key,
        StreamRuntimeState state) =>
        new(
            MediaServerHealth.Healthy,
            new[]
            {
                new MediaStreamRuntimeInfo(
                    key,
                    state,
                    SourceObservation.Reachable,
                    new ViewerCount(0),
                    StreamOwnership.OwnedCurrentProcess,
                    null,
                    null,
                    null,
                    null,
                    null,
                    false)
            });

    private static CatalogOperationResult<FormalStreamEnsureResult> Success(
        MediaStreamKey key) =>
        new(
            true,
            new FormalStreamEnsureResult(
                key.DeviceId,
                key.ChannelId,
                key.StreamType,
                new PlaybackMediaIdentity(
                    "__defaultVhost__",
                    "videomonitor",
                    "vm_test"),
                StreamRuntimeState.Ready),
            200,
            null);

    private sealed class FixedStreamManager : IStreamManager
    {
        private readonly MediaRuntimeSnapshot snapshot;

        public FixedStreamManager(MediaRuntimeSnapshot snapshot) =>
            this.snapshot = snapshot;

        public Task<StreamEnsureResult> EnsureStreamAsync(
            MediaStreamRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new StreamEnsureResult(false, null, "NOT_USED"));

        public Task CleanupOwnedStreamIfEligibleAsync(
            MediaStreamKey key,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public MediaRuntimeSnapshot GetSnapshot() => snapshot;
    }

    private sealed class RecordingFormalEnsureService : IFormalStreamEnsureService
    {
        private readonly CatalogOperationResult<FormalStreamEnsureResult> result;

        public RecordingFormalEnsureService(
            CatalogOperationResult<FormalStreamEnsureResult> result) =>
            this.result = result;

        public int CallCount { get; private set; }

        public Task<CatalogOperationResult<FormalStreamEnsureResult>> EnsureAsync(
            FormalStreamEnsureRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingSignal : IMediaReconcileSignal
    {
        private readonly ReconcileSignalResult result;

        public RecordingSignal(ReconcileSignalResult result) =>
            this.result = result;

        public int CallCount { get; private set; }

        public ReconcileSignalResult TryRequestRecovery()
        {
            CallCount++;
            return result;
        }
    }
}
