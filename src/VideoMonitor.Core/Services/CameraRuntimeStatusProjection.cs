using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Services;

public static class CameraRuntimeStatusProjection
{
    public static CameraStatus Project(
        MediaServerHealth serverHealth,
        MediaStreamKey key,
        MediaStreamRuntimeInfo? runtime)
    {
        if (serverHealth != MediaServerHealth.Healthy
            || runtime is null
            || runtime.Key != key
            || runtime.IsStale)
        {
            return CameraStatus.Unknown;
        }

        if (runtime.SourceObservation == SourceObservation.AuthFailed)
        {
            return CameraStatus.Warning;
        }

        if (runtime.RuntimeState == StreamRuntimeState.Faulted)
        {
            return runtime.SourceObservation == SourceObservation.ConnectFailed
                ? CameraStatus.Offline
                : CameraStatus.Warning;
        }

        return runtime.RuntimeState == StreamRuntimeState.Ready
            && runtime.SourceObservation == SourceObservation.Reachable
            ? CameraStatus.Online
            : CameraStatus.Unknown;
    }
}
