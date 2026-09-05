namespace VideoMonitor.Wpf.ViewModels;

public sealed class MediaPageViewModel : IAsyncDisposable
{
    private readonly object lifecycleGate = new();
    private Task? disposalTask;
    private bool disposed;

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
        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
        }

        try
        {
            await Settings.LoadAsync(cancellationToken);
        }
        finally
        {
            await Diagnostics.StartAsync(cancellationToken);
        }
    }

    public async Task DeactivateAsync(CancellationToken cancellationToken = default)
    {
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
