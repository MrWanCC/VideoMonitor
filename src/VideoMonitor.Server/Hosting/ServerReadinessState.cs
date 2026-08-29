namespace VideoMonitor.Server.Hosting;

public sealed class ServerReadinessState
{
    private int databaseReady;
    private int secretProtectionReady;

    public bool DatabaseReady => Volatile.Read(ref databaseReady) == 1;

    public bool SecretProtectionReady => Volatile.Read(ref secretProtectionReady) == 1;

    public bool IsReady => DatabaseReady && SecretProtectionReady;

    public void MarkDatabaseReady()
    {
        Volatile.Write(ref databaseReady, 1);
    }

    public void MarkSecretProtectionReady()
    {
        Volatile.Write(ref secretProtectionReady, 1);
    }
}
