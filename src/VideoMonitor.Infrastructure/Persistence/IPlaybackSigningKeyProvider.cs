namespace VideoMonitor.Infrastructure.Persistence;

public interface IPlaybackSigningKeyProvider
{
    Task<byte[]> GetOrCreateAsync(
        CancellationToken cancellationToken = default);
}
