using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Server.Catalog;
using VideoMonitor.Server.Hosting;
using VideoMonitor.Server.Playback;

namespace VideoMonitor.Server.Media;

public static class MediaDiagnosticsEndpoints
{
    private const string UnavailableCode = "MEDIA_DIAGNOSTICS_UNAVAILABLE";
    private const string UnavailableMessage = "Media diagnostics are unavailable.";
    private const string ValidationCode = "CATALOG_VALIDATION_FAILED";
    private const string ValidationMessage = "Media diagnostics request is invalid.";
    private const string RetryFailureCode = "MEDIA_DIAGNOSTICS_RETRY_FAILED";
    private const string RetryFailureMessage = "Media diagnostics retry failed.";
    private const string StreamNotFoundMessage = "Media stream identity was not found.";
    private const string StreamNotFaultedMessage = "Media stream is not faulted.";
    private const string IdentityNotFoundMessage = "Playback identity was not found.";
    private const string IdentityConflictMessage = "Playback media identity is unavailable.";

    public static IEndpointRouteBuilder MapMediaDiagnosticsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1/media");

        api.MapGet("/diagnostics", HandleGetAsync);
        api.MapPost("/diagnostics/refresh", HandleRefresh);
        api.MapPost(
            "/diagnostics/streams/{deviceId}/{channelId}/{streamType}/retry",
            HandleRetryAsync);

        return endpoints;
    }

    public static async Task<IResult> HandleGetAsync(
        HttpContext context,
        ServerReadinessState readiness,
        MediaDiagnosticsService service)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(service);

        if (!readiness.IsReady)
        {
            return UnavailableResult();
        }

        try
        {
            var snapshot = await service
                .GetAsync(context.RequestAborted)
                .ConfigureAwait(false);
            return Results.Json(snapshot, statusCode: StatusCodes.Status200OK);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return UnavailableResult();
        }
    }

    public static IResult HandleRefresh(
        ServerReadinessState readiness,
        IMediaReconcileSignal reconcileSignal)
    {
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(reconcileSignal);

        if (!readiness.IsReady)
        {
            return UnavailableResult();
        }

        try
        {
            return reconcileSignal.TryRequestRecovery() == ReconcileSignalResult.Accepted
                ? Results.Accepted()
                : UnavailableResult();
        }
        catch
        {
            return UnavailableResult();
        }
    }

    public static async Task<IResult> HandleRetryAsync(
        HttpContext context,
        ServerReadinessState readiness,
        MediaDiagnosticsService service)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(service);

        if (!readiness.IsReady)
        {
            return UnavailableResult();
        }

        if (!TryReadIdentity(context, out var key))
        {
            return ValidationResult();
        }

        try
        {
            var result = await service
                .RetryFaultedAsync(key, context.RequestAborted)
                .ConfigureAwait(false);
            return ToRetryResult(result);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return RetryFailureResult();
        }
    }

    private static bool TryReadIdentity(
        HttpContext context,
        out MediaStreamKey key)
    {
        key = default;
        var values = context.Request.RouteValues;
        if (!Guid.TryParse(GetRouteValue(values, "deviceId"), out var deviceId)
            || !Guid.TryParse(GetRouteValue(values, "channelId"), out var channelId)
            || !Enum.TryParse<StreamType>(
                GetRouteValue(values, "streamType"),
                ignoreCase: true,
                out var streamType)
            || !Enum.IsDefined(streamType))
        {
            return false;
        }

        key = new MediaStreamKey(deviceId, channelId, streamType);
        return true;
    }

    private static string? GetRouteValue(
        RouteValueDictionary values,
        string name) =>
        values.TryGetValue(name, out var value)
            ? Convert.ToString(value)
            : null;

    private static IResult ToRetryResult(
        CatalogOperationResult<FormalStreamEnsureResult> result)
    {
        if (result.IsSuccess)
        {
            return Results.Accepted();
        }

        var code = result.Error?.Code;
        return code switch
        {
            "MEDIA_STREAM_NOT_FOUND" => ErrorResult(
                StatusCodes.Status404NotFound,
                code,
                StreamNotFoundMessage),
            "PLAYBACK_DEVICE_NOT_FOUND" or "PLAYBACK_CHANNEL_NOT_FOUND" =>
                ErrorResult(
                    StatusCodes.Status404NotFound,
                    code,
                    IdentityNotFoundMessage),
            "MEDIA_STREAM_NOT_FAULTED" => ErrorResult(
                StatusCodes.Status409Conflict,
                code,
                StreamNotFaultedMessage),
            "MediaStreamIdentityConflict" => ErrorResult(
                StatusCodes.Status409Conflict,
                code,
                IdentityConflictMessage),
            _ => RetryFailureResult()
        };
    }

    private static IResult ValidationResult() =>
        ErrorResult(
            StatusCodes.Status400BadRequest,
            ValidationCode,
            ValidationMessage);

    private static IResult UnavailableResult() =>
        ErrorResult(
            StatusCodes.Status503ServiceUnavailable,
            UnavailableCode,
            UnavailableMessage);

    private static IResult RetryFailureResult() =>
        ErrorResult(
            StatusCodes.Status503ServiceUnavailable,
            RetryFailureCode,
            RetryFailureMessage);

    private static IResult ErrorResult(
        int statusCode,
        string code,
        string message) =>
        Results.Json(
            new CatalogErrorDto(code, message),
            statusCode: statusCode);
}
