using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Server.Catalog;

namespace VideoMonitor.Server.Media;

public interface IMediaSettingsService
{
    Task<MediaSettingsDto> GetAsync(CancellationToken cancellationToken = default);

    Task<CatalogOperationResult<MediaSettingsDto>> UpdateAsync(
        UpdateMediaSettingsRequest request,
        CancellationToken cancellationToken = default);

    Task<CatalogOperationResult<MediaSettingsTestResult>> TestAsync(
        TestMediaSettingsRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class MediaSettingsService : IMediaSettingsService
{
    private const string ValidationCode = "MEDIA_SETTINGS_VALIDATION_FAILED";
    private const string RevisionConflictCode = "MEDIA_SETTINGS_REVISION_CONFLICT";
    private const string ReadFailedCode = "MEDIA_SETTINGS_READ_FAILED";
    private const string WriteFailedCode = "MEDIA_SETTINGS_WRITE_FAILED";

    private readonly IMediaSettingsRepository repository;
    private readonly IMediaSettingsProbe probe;

    public MediaSettingsService(
        IMediaSettingsRepository repository,
        IMediaSettingsProbe probe)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.probe = probe ?? throw new ArgumentNullException(nameof(probe));
    }

    public Task<MediaSettingsDto> GetAsync(
        CancellationToken cancellationToken = default) =>
        repository.GetAsync(cancellationToken);

    public async Task<CatalogOperationResult<MediaSettingsDto>> UpdateAsync(
        UpdateMediaSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidUpdate(request))
        {
            return Failure<MediaSettingsDto>(
                StatusCodes.Status400BadRequest,
                ValidationCode,
                "Media settings request validation failed.");
        }

        try
        {
            var result = await repository.UpdateAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return result.Status switch
            {
                CatalogRepositoryStatus.Success => Success(
                    result.Value!,
                    StatusCodes.Status200OK),
                CatalogRepositoryStatus.RevisionConflict =>
                    Failure<MediaSettingsDto>(
                        StatusCodes.Status409Conflict,
                        RevisionConflictCode,
                        "Media settings revision conflict.",
                        result.CurrentRevision),
                _ => Failure<MediaSettingsDto>(
                    StatusCodes.Status500InternalServerError,
                    WriteFailedCode,
                    "Media settings write failed.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Failure<MediaSettingsDto>(
                StatusCodes.Status500InternalServerError,
                WriteFailedCode,
                "Media settings write failed.");
        }
    }

    public async Task<CatalogOperationResult<MediaSettingsTestResult>> TestAsync(
        TestMediaSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Failure<MediaSettingsTestResult>(
                StatusCodes.Status400BadRequest,
                ValidationCode,
                "Media settings request validation failed.");
        }

        var result = await probe.TestAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return Success(result, StatusCodes.Status200OK);
    }

    private static bool IsValidUpdate(UpdateMediaSettingsRequest? request) =>
        request is not null
        && IsValidZlmApiBaseUrl(request.ZlmApiBaseUrl)
        && IsValidPlaybackBaseUrl(request.PlaybackBaseUrl)
        && request.ExpectedRevision > 0
        && request.NoReaderGraceSeconds > 0
        && !string.IsNullOrWhiteSpace(request.Vhost)
        && !string.IsNullOrWhiteSpace(request.FormalApp)
        && !string.IsNullOrWhiteSpace(request.TestApp);

    private static bool IsValidZlmApiBaseUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrWhiteSpace(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsValidPlaybackBaseUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo);

    private static CatalogOperationResult<T> Success<T>(T value, int statusCode) =>
        new(true, value, statusCode, null);

    private static CatalogOperationResult<T> Failure<T>(
        int statusCode,
        string code,
        string message,
        long? currentRevision = null) =>
        new(
            false,
            default,
            statusCode,
            new CatalogErrorDto(code, message, currentRevision));
}
