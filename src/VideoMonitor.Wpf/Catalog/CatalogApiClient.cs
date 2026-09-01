using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;

namespace VideoMonitor.Wpf.Catalog;

public interface ICatalogConnectionClient
{
    Task CheckReadyAsync(
        Uri baseUri,
        CancellationToken cancellationToken = default);

    Task<CatalogSnapshotDto> GetCatalogAsync(
        Uri baseUri,
        CancellationToken cancellationToken = default);
}

public sealed class CatalogApiClient : ICatalogConnectionClient
{
    private readonly HttpClient httpClient;

    public CatalogApiClient(HttpClient httpClient)
    {
        this.httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task CheckReadyAsync(
        Uri baseUri,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(baseUri, "/health/ready"));
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CatalogSnapshotDto> GetCatalogAsync(
        Uri baseUri,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(baseUri, "/api/v1/catalog"));
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadSuccessAsync<CatalogSnapshotDto>(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<EnsurePlaybackStreamResponse> EnsurePlaybackStreamAsync(
        Uri baseUri,
        EnsurePlaybackStreamRequest requestModel,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, "/api/v1/playback/streams/ensure"),
            requestModel);
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsurePlaybackSuccessAsync(response, cancellationToken)
            .ConfigureAwait(false);
        return await ReadSuccessAsync<EnsurePlaybackStreamResponse>(
                response,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DeviceGroupDto> CreateGroupAsync(
        Uri baseUri,
        CreateGroupRequest requestModel,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, "/api/v1/device-groups"),
            requestModel);
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadSuccessAsync<DeviceGroupDto>(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DeviceGroupDto> UpdateGroupAsync(
        Uri baseUri,
        Guid id,
        UpdateGroupRequest requestModel,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateJsonRequest(
            HttpMethod.Put,
            new Uri(baseUri, $"/api/v1/device-groups/{id}"),
            requestModel);
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadSuccessAsync<DeviceGroupDto>(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteGroupAsync(
        Uri baseUri,
        Guid id,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri(
                baseUri,
                $"/api/v1/device-groups/{id}?expectedRevision={expectedRevision}"));
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CameraDeviceDto> CreateDeviceAsync(
        Uri baseUri,
        CreateDeviceRequest requestModel,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, "/api/v1/devices"),
            requestModel);
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadSuccessAsync<CameraDeviceDto>(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<CameraDeviceDto> UpdateDeviceAsync(
        Uri baseUri,
        Guid id,
        UpdateDeviceRequest requestModel,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateJsonRequest(
            HttpMethod.Put,
            new Uri(baseUri, $"/api/v1/devices/{id}"),
            requestModel);
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadSuccessAsync<CameraDeviceDto>(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeleteDeviceAsync(
        Uri baseUri,
        Guid id,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri(
                baseUri,
                $"/api/v1/devices/{id}?expectedRevision={expectedRevision}"));
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
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
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new CatalogApiException(
                "CATALOG_UNAVAILABLE",
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new CatalogApiException(
                "CATALOG_UNAVAILABLE",
                innerException: exception);
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await CatalogApiException.FromResponseAsync(
                    response,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task EnsurePlaybackSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        try
        {
            var error = await response.Content
                .ReadFromJsonAsync<CatalogErrorDto>(cancellationToken)
                .ConfigureAwait(false);
            if (error?.Code is "CATALOG_VALIDATION_FAILED"
                or "PLAYBACK_DEVICE_NOT_FOUND"
                or "PLAYBACK_CHANNEL_NOT_FOUND"
                or "MEDIA_UNAVAILABLE"
                or "MediaStreamIdentityConflict")
            {
                throw new CatalogApiException(error.Code, error.CurrentRevision);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CatalogApiException)
        {
            throw;
        }
        catch (JsonException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        throw new CatalogApiException("CATALOG_UNAVAILABLE");
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
        catch (OperationCanceledException exception)
        {
            throw new CatalogApiException(
                "CATALOG_UNAVAILABLE",
                innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new CatalogApiException(
                "CATALOG_UNAVAILABLE",
                innerException: exception);
        }
        catch (JsonException)
        {
            throw new CatalogApiException("CATALOG_UNAVAILABLE");
        }
        catch (NotSupportedException)
        {
            throw new CatalogApiException("CATALOG_UNAVAILABLE");
        }
        catch (InvalidOperationException)
        {
            throw new CatalogApiException("CATALOG_UNAVAILABLE");
        }
    }

    private static HttpRequestMessage CreateJsonRequest<T>(
        HttpMethod method,
        Uri uri,
        T value) =>
        new(method, uri)
        {
            Content = JsonContent.Create(value)
        };
}
