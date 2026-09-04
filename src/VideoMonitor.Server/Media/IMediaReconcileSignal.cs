namespace VideoMonitor.Server.Media;

public enum ReconcileSignalResult
{
    Accepted,
    Unavailable
}

public interface IMediaReconcileSignal
{
    ReconcileSignalResult TryRequestRecovery();
}
