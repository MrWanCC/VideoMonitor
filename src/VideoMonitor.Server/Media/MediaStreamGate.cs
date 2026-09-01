using System.Collections.Concurrent;
using VideoMonitor.Core.Media;

namespace VideoMonitor.Server.Media;

public sealed class MediaStreamGate
{
    private readonly ConcurrentDictionary<MediaStreamKey, SemaphoreSlim> gates = new();

    public async ValueTask<IAsyncDisposable> AcquireAsync(
        MediaStreamKey key,
        CancellationToken cancellationToken = default)
    {
        var gate = gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new GateLease(gate);
    }

    private sealed class GateLease : IAsyncDisposable
    {
        private readonly SemaphoreSlim gate;
        private int released;

        public GateLease(SemaphoreSlim gate)
        {
            this.gate = gate;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                gate.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
