using VideoMonitor.Core.Media;

namespace VideoMonitor.Wpf.Catalog;

public sealed class MediaRuntimeStatusStore
{
    private MediaRuntimeSnapshot snapshot = new(
        MediaServerHealth.Unconfigured,
        []);

    public MediaRuntimeSnapshot Snapshot => snapshot;

    public event EventHandler? Changed;

    public void Apply(MediaDiagnosticsSnapshotDto source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var next = new MediaRuntimeSnapshot(
            source.ServerHealth,
            source.Streams
                .Select(stream => new MediaStreamRuntimeInfo(
                    new MediaStreamKey(
                        stream.DeviceId,
                        stream.ChannelId,
                        stream.StreamType),
                    stream.RuntimeState,
                    stream.SourceObservation,
                    new ViewerCount(stream.ViewerCount),
                    stream.Ownership,
                    stream.StartedAtUtc,
                    stream.ObservedAtUtc,
                    stream.LastSuccessUtc,
                    stream.SafeLastErrorCode,
                    stream.SafeLastErrorMessage,
                    stream.IsStale))
                .ToArray());

        Replace(next);
    }

    public void MarkUnavailable()
    {
        Replace(new MediaRuntimeSnapshot(
            MediaServerHealth.Unavailable,
            []));
    }

    public void ClearEvidence()
    {
        Replace(new MediaRuntimeSnapshot(
            MediaServerHealth.Unconfigured,
            []));
    }

    private void Replace(MediaRuntimeSnapshot next)
    {
        if (snapshot.ServerHealth == next.ServerHealth
            && snapshot.Streams.SequenceEqual(next.Streams))
        {
            return;
        }

        snapshot = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
