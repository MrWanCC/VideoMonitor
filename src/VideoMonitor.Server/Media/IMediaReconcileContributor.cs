namespace VideoMonitor.Server.Media;

public interface IMediaReconcileContributor
{
    Task ReconcileAsync(CancellationToken cancellationToken = default);
}
