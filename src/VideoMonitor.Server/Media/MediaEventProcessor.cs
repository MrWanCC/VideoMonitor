using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using VideoMonitor.Core.Media;

namespace VideoMonitor.Server.Media;

public sealed class MediaEventProcessor : IHostedService, IDisposable
{
    private readonly Channel<MediaHookEvent> events = Channel.CreateBounded<MediaHookEvent>(
        new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly IStreamManager streamManager;
    private readonly MediaReconcilerHostedService? reconciler;
    private readonly object lifecycleSync = new();
    private CancellationTokenSource? hostedCancellation;
    private Task? processorTask;
    private int enqueuedCount;

    public MediaEventProcessor(
        IStreamManager streamManager,
        MediaReconcilerHostedService? reconciler = null)
    {
        this.streamManager = streamManager ?? throw new ArgumentNullException(nameof(streamManager));
        this.reconciler = reconciler;
    }

    public int EnqueuedCount => Volatile.Read(ref enqueuedCount);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (lifecycleSync)
        {
            if (processorTask is not null)
            {
                return Task.CompletedTask;
            }

            hostedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            processorTask = ProcessAsync(hostedCancellation.Token);
            return Task.CompletedTask;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? task;
        lock (lifecycleSync)
        {
            hostedCancellation?.Cancel();
            task = processorTask;
        }

        if (task is null)
        {
            return;
        }

        try
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
        lock (lifecycleSync)
        {
            hostedCancellation?.Dispose();
            hostedCancellation = null;
            processorTask = null;
        }
    }

    internal bool TryEnqueue(MediaHookEvent mediaEvent)
    {
        if (!events.Writer.TryWrite(mediaEvent))
        {
            return false;
        }

        Interlocked.Increment(ref enqueuedCount);
        return true;
    }

    public void Dispose()
    {
        hostedCancellation?.Cancel();
        hostedCancellation?.Dispose();
        hostedCancellation = null;
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        await foreach (var mediaEvent in events.Reader.ReadAllAsync(cancellationToken))
        {
            if (mediaEvent.Kind == MediaHookKind.NoneReader
                && mediaEvent.Key is MediaStreamKey key)
            {
                if (reconciler is not null)
                {
                    await reconciler
                        .WaitForNoReaderGraceAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                await streamManager
                    .CleanupOwnedStreamIfEligibleAsync(key, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}

internal enum MediaHookKind
{
    StreamChanged,
    NoneReader
}

internal sealed record MediaHookEvent(
    MediaHookKind Kind,
    string Schema,
    string Vhost,
    string App,
    string Stream,
    MediaStreamKey? Key);
