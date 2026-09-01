using VideoMonitor.Core.Media;
using VideoMonitor.Infrastructure.Persistence;

namespace VideoMonitor.Server.Playback;

public sealed class PlaybackUrlBuilder : IPlaybackUrlBuilder
{
    private readonly IMediaRuntimeSettingsProvider settingsProvider;

    public PlaybackUrlBuilder(IMediaRuntimeSettingsProvider settingsProvider)
    {
        this.settingsProvider = settingsProvider
            ?? throw new ArgumentNullException(nameof(settingsProvider));
    }

    public async Task<Uri> BuildAsync(
        PlaybackMediaIdentity media,
        PlaybackTicket ticket,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(ticket);

        if (!string.Equals(media.Vhost, ticket.Vhost, StringComparison.Ordinal)
            || !string.Equals(media.App, ticket.App, StringComparison.Ordinal)
            || !string.Equals(media.Stream, ticket.Stream, StringComparison.Ordinal))
        {
            throw new InvalidDataException("播放票据与媒体 identity 不匹配。");
        }

        var settings = await settingsProvider
            .GetAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!Uri.TryCreate(settings.PlaybackBaseUrl, UriKind.Absolute, out var baseUri)
            || !string.Equals(baseUri.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(baseUri.Host)
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new InvalidDataException("播放地址配置无效。");
        }

        var basePath = baseUri.AbsolutePath.TrimEnd('/');
        var path = basePath
            + "/"
            + Uri.EscapeDataString(media.App)
            + "/"
            + Uri.EscapeDataString(media.Stream);
        var builder = new UriBuilder(baseUri)
        {
            Path = path,
            Query = "ticket=" + Uri.EscapeDataString(ticket.Value)
        };
        return builder.Uri;
    }
}
