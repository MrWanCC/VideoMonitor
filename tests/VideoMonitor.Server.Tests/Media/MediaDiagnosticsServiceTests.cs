using System.Text.Json;
using Microsoft.Extensions.Options;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Server.Media;

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

    private static MediaDiagnosticsService CreateService(
        int freshnessSeconds = MediaDiagnosticsOptions.DefaultFreshnessSeconds)
    {
        return new MediaDiagnosticsService(
            new SnapshotStreamManager(Snapshot()),
            Options.Create(new MediaDiagnosticsOptions
            {
                FreshnessSeconds = freshnessSeconds
            }));
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
