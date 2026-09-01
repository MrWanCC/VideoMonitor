using VideoMonitor.Core.Media;

namespace VideoMonitor.Server.Media;

public sealed class MediaRuntimeRegistry : IMediaRuntimeStore, IMediaObservationRecorder
{
    private readonly object sync = new();
    private readonly Dictionary<MediaStreamKey, MediaStreamRuntimeState> states = new();
    private MediaServerHealth serverHealth = MediaServerHealth.Unconfigured;

    public MediaRuntimeSnapshot GetSnapshot()
    {
        lock (sync)
        {
            var streams = states
                .OrderBy(pair => pair.Key.DeviceId)
                .ThenBy(pair => pair.Key.ChannelId)
                .ThenBy(pair => pair.Key.StreamType)
                .Select(pair => ToInfo(pair.Key, pair.Value))
                .ToArray();
            return new MediaRuntimeSnapshot(serverHealth, streams);
        }
    }

    public void Record(
        MediaStreamKey key,
        SourceObservation observation,
        DateTimeOffset observedAtUtc,
        string? safeErrorCode,
        string? safeErrorMessage)
    {
        lock (sync)
        {
            var state = GetOrCreate(key);
            state.SourceObservation = observation;
            state.ObservedAtUtc = observedAtUtc;
            state.SafeLastErrorCode = safeErrorCode;
            state.SafeLastErrorMessage = safeErrorMessage;
            if (observation == SourceObservation.Reachable)
            {
                state.LastSuccessUtc = observedAtUtc;
            }
        }
    }

    internal void SetServerHealth(MediaServerHealth health)
    {
        lock (sync)
        {
            serverHealth = health;
        }
    }

    internal bool CurrentProcessOwnsProxy(
        MediaStreamKey key,
        out string? proxyKey)
    {
        lock (sync)
        {
            if (states.TryGetValue(key, out var state)
                && state.Ownership == StreamOwnership.OwnedCurrentProcess
                && !string.IsNullOrWhiteSpace(state.ProxyKey))
            {
                proxyKey = state.ProxyKey;
                return true;
            }

            proxyKey = null;
            return false;
        }
    }

    internal StreamOwnership GetOwnership(MediaStreamKey key)
    {
        lock (sync)
        {
            return states.TryGetValue(key, out var state)
                ? state.Ownership
                : StreamOwnership.NotOwned;
        }
    }

    internal void MarkStarting(MediaStreamKey key, DateTimeOffset startedAtUtc)
    {
        lock (sync)
        {
            var state = GetOrCreate(key);
            state.RuntimeState = StreamRuntimeState.Starting;
            state.StartedAtUtc = startedAtUtc;
            state.SafeLastErrorCode = null;
            state.SafeLastErrorMessage = null;
        }
    }

    internal void MarkReady(
        MediaStreamKey key,
        StreamOwnership ownership,
        int readerCount,
        DateTimeOffset observedAtUtc)
    {
        lock (sync)
        {
            var state = GetOrCreate(key);
            state.RuntimeState = StreamRuntimeState.Ready;
            state.SourceObservation = SourceObservation.Reachable;
            state.ViewerCount = new ViewerCount(readerCount);
            state.Ownership = ownership;
            state.ObservedAtUtc = observedAtUtc;
            state.LastSuccessUtc = observedAtUtc;
            state.SafeLastErrorCode = null;
            state.SafeLastErrorMessage = null;
            state.IsStale = false;
        }
    }

    internal void RememberCurrentProxy(MediaStreamKey key, string proxyKey)
    {
        lock (sync)
        {
            var state = GetOrCreate(key);
            state.Ownership = StreamOwnership.OwnedCurrentProcess;
            state.ProxyKey = proxyKey;
        }
    }

    internal void RememberAdopted(MediaStreamKey key)
    {
        lock (sync)
        {
            var state = GetOrCreate(key);
            state.Ownership = StreamOwnership.OwnedAdopted;
            state.ProxyKey = null;
        }
    }

    internal void MarkFaulted(
        MediaStreamKey key,
        SourceObservation observation,
        string safeErrorCode,
        string safeErrorMessage,
        DateTimeOffset observedAtUtc)
    {
        lock (sync)
        {
            var state = GetOrCreate(key);
            state.RuntimeState = StreamRuntimeState.Faulted;
            state.SourceObservation = observation;
            state.ObservedAtUtc = observedAtUtc;
            state.SafeLastErrorCode = safeErrorCode;
            state.SafeLastErrorMessage = safeErrorMessage;
            state.IsStale = false;
        }
    }

    internal void MarkIdle(MediaStreamKey key, DateTimeOffset observedAtUtc)
    {
        lock (sync)
        {
            var state = GetOrCreate(key);
            state.RuntimeState = StreamRuntimeState.Idle;
            state.Ownership = StreamOwnership.NotOwned;
            state.ProxyKey = null;
            state.ViewerCount = new ViewerCount(0);
            state.ObservedAtUtc = observedAtUtc;
            state.SafeLastErrorCode = null;
            state.SafeLastErrorMessage = null;
        }
    }

    private MediaStreamRuntimeState GetOrCreate(MediaStreamKey key)
    {
        if (!states.TryGetValue(key, out var state))
        {
            state = new MediaStreamRuntimeState();
            states.Add(key, state);
        }

        return state;
    }

    private static MediaStreamRuntimeInfo ToInfo(
        MediaStreamKey key,
        MediaStreamRuntimeState state) =>
        new(
            key,
            state.RuntimeState,
            state.SourceObservation,
            state.ViewerCount,
            state.Ownership,
            state.StartedAtUtc,
            state.ObservedAtUtc,
            state.LastSuccessUtc,
            state.SafeLastErrorCode,
            state.SafeLastErrorMessage,
            state.IsStale);
}
