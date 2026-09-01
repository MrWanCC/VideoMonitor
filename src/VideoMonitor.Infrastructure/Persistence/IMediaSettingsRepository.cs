using VideoMonitor.Core.Media;

namespace VideoMonitor.Infrastructure.Persistence;

public interface IMediaSettingsRepository
{
    Task<MediaSettingsDto> GetAsync(CancellationToken cancellationToken = default);

    Task<MediaSettingsStorageRecord> ReadStorageAsync(
        CancellationToken cancellationToken = default);

    Task<CatalogRepositoryResult<MediaSettingsDto>> UpdateAsync(
        UpdateMediaSettingsRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record MediaSettingsStorageRecord(
    string ZlmApiBaseUrl,
    string PlaybackBaseUrl,
    string Vhost,
    string FormalApp,
    string TestApp,
    string ZlmSecretCiphertext,
    int NoReaderGraceSeconds,
    long Revision);
