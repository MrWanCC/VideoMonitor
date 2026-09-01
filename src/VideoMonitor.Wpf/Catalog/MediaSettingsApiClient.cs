using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;

namespace VideoMonitor.Wpf.Catalog;

public interface IMediaSettingsApiClient
{
    Task<MediaSettingsDto> GetAsync(
        Uri baseUri,
        CancellationToken cancellationToken = default);

    Task<MediaSettingsDto> UpdateAsync(
        Uri baseUri,
        UpdateMediaSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<MediaSettingsTestResult> TestAsync(
        Uri baseUri,
        TestMediaSettingsRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class MediaSettingsApiClient : IMediaSettingsApiClient
{
    private static readonly HashSet<string> ApprovedMediaCodes =
    [
        "MEDIA_SETTINGS_VALIDATION_FAILED",
        "MEDIA_SETTINGS_REVISION_CONFLICT",
        "MEDIA_SETTINGS_READ_FAILED",
        "MEDIA_SETTINGS_WRITE_FAILED",
        "CATALOG_UNAVAILABLE"
    ];

    private readonly HttpClient httpClient;

    public MediaSettingsApiClient(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<MediaSettingsDto> GetAsync(
        Uri baseUri,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(baseUri, "/api/v1/media/settings"));
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadSuccessAsync<MediaSettingsDto>(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MediaSettingsDto> UpdateAsync(
        Uri baseUri,
        UpdateMediaSettingsRequest requestModel,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateJsonRequest(
            HttpMethod.Put,
            new Uri(baseUri, "/api/v1/media/settings"),
            requestModel);
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadSuccessAsync<MediaSettingsDto>(response, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<MediaSettingsTestResult> TestAsync(
        Uri baseUri,
        TestMediaSettingsRequest requestModel,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            new Uri(baseUri, "/api/v1/media/settings/test"),
            requestModel);
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        return await ReadSuccessAsync<MediaSettingsTestResult>(response, cancellationToken)
            .ConfigureAwait(false);
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
            throw await FromMediaResponseAsync(
                    response,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<CatalogApiException> FromMediaResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content
                .ReadFromJsonAsync<CatalogErrorDto>(cancellationToken)
                .ConfigureAwait(false);
            return error is not null
                && !string.IsNullOrEmpty(error.Code)
                && ApprovedMediaCodes.Contains(error.Code)
                ? new CatalogApiException(error.Code, error.CurrentRevision)
                : new CatalogApiException("CATALOG_UNAVAILABLE");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException)
        {
            return new CatalogApiException("CATALOG_UNAVAILABLE");
        }
        catch (NotSupportedException)
        {
            return new CatalogApiException("CATALOG_UNAVAILABLE");
        }
        catch (InvalidOperationException)
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
