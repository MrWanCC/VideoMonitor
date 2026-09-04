using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Server.Catalog;
using VideoMonitor.Server.Media;
using VideoMonitor.Server.Playback;

namespace VideoMonitor.Server.Tests.Media;

public sealed class MediaDiagnosticsServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DiagnosticsProjectionContainsSafeCounts()
    {
        var result = CreateService().Project(
            Snapshot(
                Stream(StreamRuntimeState.Ready, viewers: 2),
                Stream(StreamRuntimeState.Ready, viewers: 1),
                Stream(StreamRuntimeState.Starting),
                Stream(StreamRuntimeState.Faulted),
                Stream(StreamRuntimeState.Idle, viewers: 4)),
            Now);

        Assert.Equal(2, result.ActiveStreamCount);
        Assert.Equal(7, result.ViewerCount);
        Assert.Equal(1, result.FaultCount);
    }

    [Fact]
    public void OldReadyObservationBecomesStale()
    {
        var result = CreateService().Project(
            Snapshot(Stream(StreamRuntimeState.Ready,
                observedAtUtc: Now.AddSeconds(-91))),
            Now);

        Assert.True(Assert.Single(result.Streams).IsStale);
    }

    [Fact]
    public void IdleNeverBecomesStale()
    {
        var result = CreateService().Project(
            Snapshot(Stream(StreamRuntimeState.Idle,
                observedAtUtc: Now.AddDays(-30))),
            Now);

        Assert.False(Assert.Single(result.Streams).IsStale);
    }

    [Fact]
    public void StartingWithoutObservationIsNotImmediatelyStale()
    {
        var result = CreateService().Project(
            Snapshot(Stream(StreamRuntimeState.Starting,
                observedAtUtc: null)),
            Now);

        Assert.False(Assert.Single(result.Streams).IsStale);
    }

    [Fact]
    public void FutureObservationIsNotStale()
    {
        var result = CreateService().Project(
            Snapshot(Stream(StreamRuntimeState.Ready,
                observedAtUtc: Now.AddMinutes(1))),
            Now);

        Assert.False(Assert.Single(result.Streams).IsStale);
    }

    [Fact]
    public void ObservationExactlyAtFreshnessBoundaryIsNotStale()
    {
        var result = CreateService().Project(
            Snapshot(Stream(StreamRuntimeState.Ready,
                observedAtUtc: Now.AddSeconds(-90))),
            Now);

        Assert.False(Assert.Single(result.Streams).IsStale);
    }

    [Fact]
    public void InvalidFreshnessFallsBackToDefault()
    {
        var result = CreateService(freshnessSeconds: 0).Project(
            Snapshot(Stream(StreamRuntimeState.Ready,
                observedAtUtc: Now.AddSeconds(-91))),
            Now);

        Assert.True(Assert.Single(result.Streams).IsStale);
    }

    [Fact]
    public async Task GetAsyncUsesInjectedTimeProvider()
    {
        var service = new MediaDiagnosticsService(
            new SnapshotStreamManager(
                Snapshot(Stream(StreamRuntimeState.Ready,
                    observedAtUtc: Now.AddSeconds(-91)))),
            new RecordingFormalEnsureService(),
            Options.Create(new MediaDiagnosticsOptions()),
            new FixedTimeProvider(Now));

        var result = await service.GetAsync();

        Assert.True(Assert.Single(result.Streams).IsStale);
    }

    [Fact]
    public void SafeProjectionContainsNoSecretFields()
    {
        var result = CreateService().Project(
            Snapshot(Stream(StreamRuntimeState.Ready)),
            Now);
        var json = JsonSerializer.Serialize(result);

        foreach (var forbidden in new[]
                 {
                     "StreamId",
                     "MediaStreamKey",
                     "originUrl",
                     "SourceUri",
                     "Password",
                     "PasswordCiphertext",
                     "ZlmSecret",
                     "ProxyKey",
                     "PlaybackTicket",
                     "PlaybackUrl",
                     "SigningKey"
                 })
        {
            Assert.DoesNotContain(forbidden, json,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task FaultedRetryUsesFormalEnsureBoundary()
    {
        var fixture = CreateRetryFixture(StreamRuntimeState.Faulted);

        var result = await fixture.Service.RetryFaultedAsync(fixture.Key);

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.Key, fixture.EnsureService.LastRequestKey);
    }

    [Theory]
    [InlineData(StreamRuntimeState.Ready)]
    [InlineData(StreamRuntimeState.Starting)]
    [InlineData(StreamRuntimeState.Idle)]
    public async Task NonFaultedRetryIsRejected(StreamRuntimeState runtimeState)
    {
        var fixture = CreateRetryFixture(runtimeState);

        var result = await fixture.Service.RetryFaultedAsync(fixture.Key);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal("MEDIA_STREAM_NOT_FAULTED", result.Error?.Code);
        Assert.Equal(0, fixture.EnsureService.CallCount);
    }

    [Fact]
    public async Task UnknownRuntimeIdentityIsRejected()
    {
        var fixture = CreateRetryFixture(StreamRuntimeState.Faulted);
        var unknownKey = new MediaStreamKey(
            Guid.NewGuid(),
            Guid.NewGuid(),
            StreamType.Main);

        var result = await fixture.Service.RetryFaultedAsync(unknownKey);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Equal(0, fixture.EnsureService.CallCount);
    }

    [Fact]
    public async Task FaultedRetryDoesNotExposePlaybackUrl()
    {
        var fixture = CreateRetryFixture(StreamRuntimeState.Faulted);

        var result = await fixture.Service.RetryFaultedAsync(fixture.Key);
        var json = JsonSerializer.Serialize(result);

        Assert.DoesNotContain("PlaybackUrl", json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FaultedRetryDoesNotExposePlaybackTicket()
    {
        var fixture = CreateRetryFixture(StreamRuntimeState.Faulted);

        var result = await fixture.Service.RetryFaultedAsync(fixture.Key);
        var json = JsonSerializer.Serialize(result);

        Assert.DoesNotContain("PlaybackTicket", json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FaultedRetryDoesNotExposeTicketExpiry()
    {
        var fixture = CreateRetryFixture(StreamRuntimeState.Faulted);

        var result = await fixture.Service.RetryFaultedAsync(fixture.Key);
        var json = JsonSerializer.Serialize(result);

        Assert.DoesNotContain("TicketExpiresUtc", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExpiresAtUtc", json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnlyExactKeyCanBeRetried()
    {
        var fixture = CreateRetryFixture(StreamRuntimeState.Faulted);
        var sameDeviceDifferentChannel = new MediaStreamKey(
            fixture.Key.DeviceId,
            Guid.NewGuid(),
            fixture.Key.StreamType);

        var result = await fixture.Service.RetryFaultedAsync(sameDeviceDifferentChannel);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, fixture.EnsureService.CallCount);
    }

    private static MediaDiagnosticsService CreateService(
        int freshnessSeconds = MediaDiagnosticsOptions.DefaultFreshnessSeconds)
    {
        return new MediaDiagnosticsService(
            new SnapshotStreamManager(Snapshot()),
            new RecordingFormalEnsureService(),
            Options.Create(new MediaDiagnosticsOptions
            {
                FreshnessSeconds = freshnessSeconds
            }));
    }

    private static RetryFixture CreateRetryFixture(StreamRuntimeState runtimeState)
    {
        var key = new MediaStreamKey(
            Guid.Parse("77000000-0000-0000-0000-000000000001"),
            Guid.Parse("78000000-0000-0000-0000-000000000001"),
            StreamType.Main);
        var ensureService = new RecordingFormalEnsureService();
        var service = new MediaDiagnosticsService(
            new SnapshotStreamManager(Snapshot(Stream(runtimeState))),
            ensureService,
            Options.Create(new MediaDiagnosticsOptions()));

        return new RetryFixture(service, ensureService, key);
    }

    private static MediaRuntimeSnapshot Snapshot(
        params MediaStreamRuntimeInfo[] streams) =>
        new(MediaServerHealth.Healthy, streams);

    private static MediaStreamRuntimeInfo Stream(
        StreamRuntimeState state,
        int viewers = 0,
        DateTimeOffset? observedAtUtc = null,
        StreamType streamType = StreamType.Main) =>
        new(
            new MediaStreamKey(
                Guid.Parse("77000000-0000-0000-0000-000000000001"),
                Guid.Parse("78000000-0000-0000-0000-000000000001"),
                streamType),
            state,
            SourceObservation.Reachable,
            new ViewerCount(viewers),
            StreamOwnership.OwnedCurrentProcess,
            Now,
            observedAtUtc,
            observedAtUtc,
            null,
            null,
            true);

    private sealed class SnapshotStreamManager(MediaRuntimeSnapshot snapshot)
        : IStreamManager
    {
        public Task<StreamEnsureResult> EnsureStreamAsync(
            MediaStreamRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CleanupOwnedStreamIfEligibleAsync(
            MediaStreamKey key,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public MediaRuntimeSnapshot GetSnapshot() => snapshot;
    }

    private sealed record RetryFixture(
        MediaDiagnosticsService Service,
        RecordingFormalEnsureService EnsureService,
        MediaStreamKey Key);

    private sealed class RecordingFormalEnsureService : IFormalStreamEnsureService
    {
        public int CallCount { get; private set; }

        public MediaStreamKey? LastRequestKey { get; private set; }

        public Task<CatalogOperationResult<FormalStreamEnsureResult>> EnsureAsync(
            FormalStreamEnsureRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequestKey = new MediaStreamKey(
                request.DeviceId,
                request.ChannelId,
                request.StreamType);
            return Task.FromResult(
                new CatalogOperationResult<FormalStreamEnsureResult>(
                    true,
                    new FormalStreamEnsureResult(
                        request.DeviceId,
                        request.ChannelId,
                        request.StreamType,
                        new PlaybackMediaIdentity(
                            "vhost",
                            "videomonitor",
                            request.ChannelId.ToString("N")),
                        StreamRuntimeState.Ready),
                    StatusCodes.Status200OK,
                    null));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
