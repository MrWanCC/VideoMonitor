using System.Windows.Threading;

namespace VideoMonitor.Wpf.Catalog;

public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher
            ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public Task InvokeAsync(
        Action action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher
            .InvokeAsync(action, DispatcherPriority.Normal, cancellationToken)
            .Task;
    }
}
