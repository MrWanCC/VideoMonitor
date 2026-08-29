using System.Threading.Channels;
using VideoMonitor.Core.Models;

namespace VideoMonitor.Core.Services;

public sealed class DeviceCatalogPersistenceCoordinator : IAsyncDisposable
{
    private readonly IDeviceCatalog catalog;
    private readonly IDeviceCatalogStore store;
    private readonly Channel<QueueItem> queue =
        Channel.CreateUnbounded<QueueItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    private readonly object lifecycleGate = new();
    private readonly Task processingTask;
    private Task? completionTask;
    private bool acceptingChanges = true;
    private int failureReported;
    private int failureNotificationSent;
    private Exception? lastPersistenceException;

    public DeviceCatalogPersistenceCoordinator(
        IDeviceCatalog catalog,
        IDeviceCatalogStore store)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        catalog.Changed += OnCatalogChanged;
        processingTask = ProcessQueueAsync();
    }

    public event EventHandler? PersistenceFailed;

    public Exception? LastPersistenceException =>
        Volatile.Read(ref lastPersistenceException);

    public async Task FlushAsync()
    {
        Task task;
        var enqueueFailed = false;
        lock (lifecycleGate)
        {
            if (completionTask is not null)
            {
                task = completionTask;
            }
            else
            {
                var barrier = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                enqueueFailed = !queue.Writer.TryWrite(new QueueItem.Barrier(barrier));
                task = barrier.Task;
            }
        }

        if (enqueueFailed)
        {
            RecordFailure();
            throw LastPersistenceException
                ?? new InvalidOperationException("设备目录自动保存失败。");
        }

        await task.ConfigureAwait(false);

        var failure = LastPersistenceException;
        if (failure is not null)
        {
            Interlocked.Exchange(ref failureReported, 1);
            throw failure;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            Task task;
            lock (lifecycleGate)
            {
                task = CompleteQueueNoLock();
            }

            await task.ConfigureAwait(false);

            var failure = LastPersistenceException;
            if (failure is not null
                && Interlocked.Exchange(ref failureReported, 1) == 0)
            {
                throw failure;
            }
        }
        finally
        {
            lock (lifecycleGate)
            {
                catalog.Changed -= OnCatalogChanged;
                acceptingChanges = false;
            }
        }
    }

    private Task CompleteQueueNoLock()
    {
        if (completionTask is not null)
        {
            return completionTask;
        }

        acceptingChanges = false;
        catalog.Changed -= OnCatalogChanged;
        queue.Writer.TryComplete();
        completionTask = processingTask;
        return completionTask;
    }

    private void OnCatalogChanged(object? sender, EventArgs args)
    {
        try
        {
            DeviceCatalogSnapshot snapshot;
            lock (lifecycleGate)
            {
                if (!acceptingChanges)
                {
                    return;
                }

                snapshot = DeviceCatalogSnapshotFactory.Create(catalog);
                if (queue.Writer.TryWrite(new QueueItem.Save(snapshot)))
                {
                    return;
                }
            }

            RecordFailure();
        }
        catch (Exception)
        {
            RecordFailure();
        }
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var item in queue.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (item is QueueItem.Barrier barrier)
            {
                barrier.Completion.TrySetResult();
                continue;
            }

            var snapshot = ((QueueItem.Save)item).Snapshot;
            try
            {
                await store.SaveAsync(snapshot, CancellationToken.None)
                    .ConfigureAwait(false);
                Volatile.Write(ref lastPersistenceException, null);
                Interlocked.Exchange(ref failureNotificationSent, 0);
            }
            catch (Exception)
            {
                RecordFailure();
            }
        }
    }

    private void RecordFailure()
    {
        Volatile.Write(
            ref lastPersistenceException,
            new InvalidOperationException("设备目录自动保存失败。"));
        Interlocked.Exchange(ref failureReported, 0);

        if (Interlocked.Exchange(ref failureNotificationSent, 1) != 0)
        {
            return;
        }

        try
        {
            PersistenceFailed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // Persistence notifications must not terminate the single consumer.
        }
    }

    private abstract record QueueItem
    {
        private QueueItem()
        {
        }

        public sealed record Save(DeviceCatalogSnapshot Snapshot) : QueueItem;

        public sealed record Barrier(TaskCompletionSource Completion) : QueueItem;
    }
}
