namespace VideoMonitor.Infrastructure.Security;

public interface IMasterKeyProvider
{
    Task<byte[]> GetOrCreateAsync(CancellationToken cancellationToken = default);
}
