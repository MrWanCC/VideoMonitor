namespace VideoMonitor.Infrastructure.Persistence;

public interface IMediaRuntimeSettingsProvider
{
    Task<MediaRuntimeSettings> GetAsync(
        CancellationToken cancellationToken = default);
}
