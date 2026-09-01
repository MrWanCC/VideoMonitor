using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;

namespace VideoMonitor.Wpf.Playback;

public sealed class RemotePlaybackSourceProvider : IFormalPlaybackSourceProvider
{
    private readonly CatalogApiClient apiClient;
    private readonly Func<Uri> baseUriProvider;

    public RemotePlaybackSourceProvider(
        CatalogApiClient apiClient,
        Func<Uri> baseUriProvider)
    {
        this.apiClient = apiClient
            ?? throw new ArgumentNullException(nameof(apiClient));
        this.baseUriProvider = baseUriProvider
            ?? throw new ArgumentNullException(nameof(baseUriProvider));
    }

    public async Task<FormalPlaybackSource> PrepareAsync(
        Guid deviceId,
        Guid channelId,
        StreamType streamType,
        CancellationToken cancellationToken = default)
    {
        var response = await apiClient
            .EnsurePlaybackStreamAsync(
                baseUriProvider(),
                new EnsurePlaybackStreamRequest(deviceId, channelId, streamType),
                cancellationToken)
            .ConfigureAwait(false);

        return new FormalPlaybackSource(
            deviceId,
            channelId,
            response.StreamId,
            response.PlaybackUrl,
            response.ExpiresAtUtc);
    }

    public Task ReleaseAsync(
        FormalPlaybackSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
