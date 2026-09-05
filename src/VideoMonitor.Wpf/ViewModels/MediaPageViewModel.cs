namespace VideoMonitor.Wpf.ViewModels;

public sealed class MediaPageViewModel : IAsyncDisposable
{
    private readonly object lifecycleGate = new();
    private Task? disposalTask;
    private bool disposed;
    private bool activeRequested;
    private long lifecycleGeneration;

    public MediaPageViewModel(
        MediaSettingsViewModel settings,
        MediaDiagnosticsViewModel diagnostics)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Diagnostics = diagnostics
            ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public MediaSettingsViewModel Settings { get; }

    public MediaDiagnosticsViewModel Diagnostics { get; }

    public async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        long generation;
        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            activeRequested = true;
            generation = ++lifecycleGeneration;
        }

        try
        {
            await Settings.LoadAsync(cancellationToken);
        }
        finally
        {
            bool shouldStart;
            lock (lifecycleGate)
            {
                shouldStart = !disposed
                    && activeRequested
                    && generation == lifecycleGeneration;
            }

            if (shouldStart)
            {
                await Diagnostics.StartAsync(cancellationToken);
            }
        }
    }

    public async Task DeactivateAsync(CancellationToken cancellationToken = default)
    {
        lock (lifecycleGate)
        {
            activeRequested = false;
            lifecycleGeneration++;
        }

        try
        {
            await Diagnostics.StopAsync(cancellationToken);
        }
        finally
        {
            Settings.ClearTransientSecret();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycleGate)
        {
            disposalTask ??= DisposeCoreAsync();
            return new ValueTask(disposalTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        lock (lifecycleGate)
        {
            disposed = true;
            activeRequested = false;
            lifecycleGeneration++;
        }

        try
        {
            await Diagnostics.DisposeAsync();
        }
        finally
        {
            Settings.ClearTransientSecret();
        }
    }
}
