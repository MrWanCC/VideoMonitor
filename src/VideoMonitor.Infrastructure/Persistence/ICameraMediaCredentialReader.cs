namespace VideoMonitor.Infrastructure.Persistence;

public interface ICameraMediaCredentialReader
{
    Task<CameraMediaCredential> ReadAsync(
        Guid deviceId,
        Guid channelId,
        CancellationToken cancellationToken = default);
}
