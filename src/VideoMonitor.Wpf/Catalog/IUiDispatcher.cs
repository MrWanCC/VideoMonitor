namespace VideoMonitor.Wpf.Catalog;

public interface IUiDispatcher
{
    Task InvokeAsync(
        Action action,
        CancellationToken cancellationToken = default);
}
