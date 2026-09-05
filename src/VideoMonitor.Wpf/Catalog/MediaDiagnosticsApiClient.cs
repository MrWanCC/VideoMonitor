using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;

namespace VideoMonitor.Wpf.Catalog;

public interface IMediaDiagnosticsApiClient
{
    Task<MediaDiagnosticsSnapshotDto> GetDiagnosticsAsync(
        Uri baseUri,
        CancellationToken cancellationToken = default);

    Task RequestRefreshAsync(
        Uri baseUri,
        CancellationToken cancellationToken = default);

    Task RetryFaultedAsync(
        Uri baseUri,
        Guid deviceId,
        Guid channelId,
        StreamType streamType,
        CancellationToken cancellationToken = default);
}

public sealed class MediaDiagnosticsApiClient : IMediaDiagnosticsApiClient
{
    private static readonly HashSet<string> SafeErrorCodes =
    [
        "MEDIA_DIAGNOSTICS_UNAVAILABLE",
        "MEDIA_DIAGNOSTICS_RETRY_FAILED",
        "MEDIA_STREAM_NOT_FOUND",
        "MEDIA_STREAM_NOT_FAULTED",
        "CATALOG_VALIDATION_FAILED",
        "PLAYBACK_DEVICE_NOT_FOUND",
        "PLAYBACK_CHANNEL_NOT_FOUND",
        "MediaStreamIdentityConflict"
    ];

    private readonly HttpClient httpClient;

    public MediaDiagnosticsApiClient(HttpClient httpClient)
    {
        this.httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<MediaDiagnosticsSnapshotDto> GetDiagnosticsAsync(
        Uri baseUri,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            CreateUri(baseUri, "/api/v1/media/diagnostics"));
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw await ReadErrorAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }

        return await ReadSuccessAsync<MediaDiagnosticsSnapshotDto>(
                response,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task RequestRefreshAsync(
        Uri baseUri,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            CreateUri(baseUri, "/api/v1/media/diagnostics/refresh"));
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Accepted)
        {
            throw await ReadErrorAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task RetryFaultedAsync(
        Uri baseUri,
        Guid deviceId,
        Guid channelId,
        StreamType streamType,
        CancellationToken cancellationToken = default)
    {
        var streamTypeSegment = streamType switch
        {
            StreamType.Main => "main",
            StreamType.Sub => "sub",
            _ => throw new CatalogApiException("CATALOG_UNAVAILABLE")
        };
        var path =
            $"/api/v1/media/diagnostics/streams/"
            + $"{Uri.EscapeDataString(deviceId.ToString())}/"
            + $"{Uri.EscapeDataString(channelId.ToString())}/"
            + $"{streamTypeSegment}/retry";

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            CreateUri(baseUri, path));
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Accepted)
        {
            throw await ReadErrorAsync(response, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CatalogApiException("CATALOG_UNAVAILABLE");
        }
        catch (HttpRequestException)
        {
            throw new CatalogApiException("CATALOG_UNAVAILABLE");
        }
    }

    private static async Task<CatalogApiException> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content
                .ReadFromJsonAsync<CatalogErrorDto>(cancellationToken)
                .ConfigureAwait(false);

            return error is not null
                && !string.IsNullOrWhiteSpace(error.Code)
                && SafeErrorCodes.Contains(error.Code)
                ? new CatalogApiException(error.Code, error.CurrentRevision)
                : new CatalogApiException("CATALOG_UNAVAILABLE");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new CatalogApiException("CATALOG_UNAVAILABLE");
        }
    }

    private static async Task<T> ReadSuccessAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var value = await response.Content
                .ReadFromJsonAsync<T>(cancellationToken)
                .ConfigureAwait(false);
            return value ?? throw new CatalogApiException("CATALOG_UNAVAILABLE");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CatalogApiException)
        {
            throw;
        }
        catch
        {
            throw new CatalogApiException("CATALOG_UNAVAILABLE");
        }
    }

    private static Uri CreateUri(Uri baseUri, string path)
    {
        if (!baseUri.IsAbsoluteUri
            || (baseUri.Scheme != Uri.UriSchemeHttp
                && baseUri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(baseUri.Host)
            || !string.IsNullOrEmpty(baseUri.UserInfo))
        {
            throw new CatalogApiException("CATALOG_UNAVAILABLE");
        }

        try
        {
            return new Uri(baseUri, path);
        }
        catch (UriFormatException)
        {
            throw new CatalogApiException("CATALOG_UNAVAILABLE");
        }
    }
}
