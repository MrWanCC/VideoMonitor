using Microsoft.Extensions.Options;
using VideoMonitor.Core.Media;

namespace VideoMonitor.Server.Media;

public sealed class MediaDiagnosticsService
{
    private readonly IStreamManager streamManager;
    private readonly TimeProvider timeProvider;
    private readonly int freshnessSeconds;

    public MediaDiagnosticsService(
        IStreamManager streamManager,
        IOptions<MediaDiagnosticsOptions> options,
        TimeProvider? timeProvider = null)
    {
        this.streamManager = streamManager
            ?? throw new ArgumentNullException(nameof(streamManager));
        ArgumentNullException.ThrowIfNull(options);

        freshnessSeconds = options.Value.FreshnessSeconds > 0
            ? options.Value.FreshnessSeconds
            : MediaDiagnosticsOptions.DefaultFreshnessSeconds;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<MediaDiagnosticsSnapshotDto> GetAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = streamManager.GetSnapshot();
        var nowUtc = timeProvider.GetUtcNow();
        return Task.FromResult(Project(snapshot, nowUtc));
    }

    public MediaDiagnosticsSnapshotDto Project(
        MediaRuntimeSnapshot snapshot,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var streams = snapshot.Streams
            .Select(stream => new MediaStreamDiagnosticsDto(
                stream.Key.DeviceId,
                stream.Key.ChannelId,
                stream.Key.StreamType,
                stream.RuntimeState,
                stream.ViewerCount.Value,
                stream.Ownership,
                stream.StartedAtUtc,
                stream.SourceObservation,
                stream.ObservedAtUtc,
                stream.LastSuccessUtc,
                stream.SafeLastErrorCode,
                stream.SafeLastErrorMessage,
                IsStale(stream.RuntimeState, stream.ObservedAtUtc, nowUtc)))
            .ToArray();

        return new MediaDiagnosticsSnapshotDto(
            snapshot.ServerHealth,
            streams.Count(stream => stream.RuntimeState == StreamRuntimeState.Ready),
            streams.Sum(stream => stream.ViewerCount),
            streams.Count(stream => stream.RuntimeState == StreamRuntimeState.Faulted),
            streams);
    }

    private bool IsStale(
        StreamRuntimeState runtimeState,
        DateTimeOffset? observedAtUtc,
        DateTimeOffset nowUtc)
    {
        if (runtimeState == StreamRuntimeState.Idle
            || runtimeState is not (
                StreamRuntimeState.Ready
                or StreamRuntimeState.Starting
                or StreamRuntimeState.Faulted)
            || observedAtUtc is null
            || observedAtUtc.Value > nowUtc)
        {
            return false;
        }

        return nowUtc - observedAtUtc.Value
            > TimeSpan.FromSeconds(freshnessSeconds);
    }
}
