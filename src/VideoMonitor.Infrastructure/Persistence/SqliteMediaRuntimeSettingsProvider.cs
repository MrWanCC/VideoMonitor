using VideoMonitor.Infrastructure.Security;

namespace VideoMonitor.Infrastructure.Persistence;

public sealed class SqliteMediaRuntimeSettingsProvider : IMediaRuntimeSettingsProvider
{
    public const string MediaSecretPurpose = "media-settings:zlm-secret";

    private readonly IMediaSettingsRepository repository;
    private readonly ISecretProtector protector;

    public SqliteMediaRuntimeSettingsProvider(
        IMediaSettingsRepository repository,
        ISecretProtector protector)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public async Task<MediaRuntimeSettings> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var stored = await repository.ReadStorageAsync(cancellationToken)
            .ConfigureAwait(false);
        var secret = string.IsNullOrEmpty(stored.ZlmSecretCiphertext)
            ? string.Empty
            : await protector.UnprotectAsync(
                    stored.ZlmSecretCiphertext,
                    MediaSecretPurpose,
                    cancellationToken)
                .ConfigureAwait(false);

        return new MediaRuntimeSettings(
            stored.ZlmApiBaseUrl,
            stored.PlaybackBaseUrl,
            stored.Vhost,
            stored.FormalApp,
            stored.TestApp,
            secret,
            stored.NoReaderGraceSeconds,
            stored.Revision);
    }
}
