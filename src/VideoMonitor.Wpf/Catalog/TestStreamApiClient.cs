using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using VideoMonitor.Core.Media;

namespace VideoMonitor.Wpf.Catalog;

public interface ITestStreamApiClient
{
    Task<TestSessionDto> StartAsync(
        Uri baseUri,
        TestStreamStartRequest request,
        CancellationToken cancellationToken = default);

    Task StopAsync(
        Uri baseUri,
        Guid sessionId,
        CancellationToken cancellationToken = default);
}

public sealed class TestStreamApiClient : ITestStreamApiClient
{
    private readonly HttpClient httpClient;

    public TestStreamApiClient(HttpClient httpClient)
    {
        this.httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<TestSessionDto> StartAsync(
        Uri baseUri,
        TestStreamStartRequest requestModel,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(baseUri, "/api/v1/test-streams"))
        {
            Content = JsonContent.Create(requestModel)
        };
        using var response = await SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
        try
        {
            var value = await response.Content
                .ReadFromJsonAsync<TestSessionDto>(cancellationToken)
                .ConfigureAwait(false);
            return value ?? throw new CatalogApiException("CATALOG_UNAVAILABLE");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException)
        {
            throw new CatalogApiException("CATALOG_UNAVAILABLE");
        }
        catch (NotSupportedException)
        {
            throw new CatalogApiException("CATALOG_UNAVAILABLE");
        }
    }

    public async Task StopAsync(
        Uri baseUri,
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            new Uri(baseUri, $"/api/v1/test-streams/{sessionId}"));
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
            throw new CatalogApiException("CATALOG_UNAVAILABLE", innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new CatalogApiException("CATALOG_UNAVAILABLE", innerException: exception);
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
}
