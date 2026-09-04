using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Server.Catalog;
using VideoMonitor.Server.Playback;

namespace VideoMonitor.Server.Media;

public sealed class MediaDiagnosticsService
{
    private readonly IStreamManager streamManager;
    private readonly IFormalStreamEnsureService formalEnsureService;
    private readonly TimeProvider timeProvider;
    private readonly int freshnessSeconds;

    public MediaDiagnosticsService(
        IStreamManager streamManager,
        IFormalStreamEnsureService formalEnsureService,
        IOptions<MediaDiagnosticsOptions> options,
        TimeProvider? timeProvider = null)
    {
        this.streamManager = streamManager
            ?? throw new ArgumentNullException(nameof(streamManager));
        this.formalEnsureService = formalEnsureService
            ?? throw new ArgumentNullException(nameof(formalEnsureService));
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

    public async Task<CatalogOperationResult<FormalStreamEnsureResult>> RetryFaultedAsync(
        MediaStreamKey key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var runtime = streamManager.GetSnapshot().Streams
            .FirstOrDefault(stream => stream.Key == key);
        if (runtime is null)
        {
            return Failure<FormalStreamEnsureResult>(
                StatusCodes.Status404NotFound,
                "MEDIA_STREAM_NOT_FOUND",
                "Media stream identity was not found.");
        }

        if (runtime.RuntimeState != StreamRuntimeState.Faulted)
        {
            return Failure<FormalStreamEnsureResult>(
                StatusCodes.Status409Conflict,
                "MEDIA_STREAM_NOT_FAULTED",
                "Media stream is not faulted.");
        }

        return await formalEnsureService
            .EnsureAsync(
                new FormalStreamEnsureRequest(
                    key.DeviceId,
                    key.ChannelId,
                    key.StreamType),
                cancellationToken)
            .ConfigureAwait(false);
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

    private static CatalogOperationResult<T> Failure<T>(
        int statusCode,
        string code,
        string message) =>
        new(
            false,
            default,
            statusCode,
            new CatalogErrorDto(code, message));
}
