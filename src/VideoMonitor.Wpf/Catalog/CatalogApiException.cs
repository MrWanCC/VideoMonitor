using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using VideoMonitor.Core.Catalog;

namespace VideoMonitor.Wpf.Catalog;

public sealed class CatalogApiException : Exception
{
    private const string SafeMessage = "Catalog API request failed.";

    private static readonly HashSet<string> ApprovedCodes =
    [
        "CATALOG_VALIDATION_FAILED",
        "DEVICE_NOT_FOUND",
        "GROUP_NOT_FOUND",
        "DEVICE_REVISION_CONFLICT",
        "GROUP_REVISION_CONFLICT",
        "GROUP_NOT_EMPTY",
        "CHANNEL_CONFLICT",
        "CATALOG_UNAVAILABLE",
        "CATALOG_READ_FAILED",
        "CATALOG_WRITE_FAILED"
    ];

    public CatalogApiException(
        string code,
        long? currentRevision = null,
        Exception? innerException = null)
        : base(SafeMessage, innerException)
    {
        Code = code;
        CurrentRevision = currentRevision;
    }

    public string Code { get; }

    public long? CurrentRevision { get; }

    public static async Task<CatalogApiException> FromResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var error = await response.Content
                .ReadFromJsonAsync<CatalogErrorDto>(cancellationToken)
                .ConfigureAwait(false);

            return error is not null
                && !string.IsNullOrEmpty(error.Code)
                && ApprovedCodes.Contains(error.Code)
                ? new CatalogApiException(error.Code, error.CurrentRevision)
                : Unavailable();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException)
        {
            return Unavailable();
        }
        catch (NotSupportedException)
        {
            return Unavailable();
        }
        catch (InvalidOperationException)
        {
            return Unavailable();
        }
    }

    private static CatalogApiException Unavailable() =>
        new("CATALOG_UNAVAILABLE");
}
