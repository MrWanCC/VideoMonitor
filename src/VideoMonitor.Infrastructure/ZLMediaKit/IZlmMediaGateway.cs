using System.Text.Json;

namespace VideoMonitor.Infrastructure.ZLMediaKit;

public interface IZlmMediaGateway
{
    Task<ZlmApiResponse<IReadOnlyList<ZlmMediaEvidence>>> GetMediaListAsync(
        string vhost,
        string app,
        string? stream,
        CancellationToken cancellationToken = default);

    Task<ZlmApiResponse<ZlmAddStreamProxyData>> AddStreamProxyAsync(
        string vhost,
        string app,
        string stream,
        Uri sourceUri,
        CancellationToken cancellationToken = default);

    Task<ZlmApiResponse<ZlmDeleteStreamProxyData>> DeleteStreamProxyAsync(
        string proxyKey,
        CancellationToken cancellationToken = default);

    Task<ZlmApiResponse<JsonElement>> CloseExactStreamAsync(
        string schema,
        string vhost,
        string app,
        string stream,
        CancellationToken cancellationToken = default);
}
