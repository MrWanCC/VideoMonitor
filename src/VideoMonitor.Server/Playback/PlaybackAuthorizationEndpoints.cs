using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Server.Catalog;
using VideoMonitor.Server.Hosting;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Playback;

public static class PlaybackAuthorizationEndpoints
{
    private const string ValidationCode = "CATALOG_VALIDATION_FAILED";
    private const string ValidationMessage = "Playback request validation failed.";
    private const string UnavailableCode = "MEDIA_UNAVAILABLE";
    private const string UnavailableMessage = "Playback media is unavailable.";
    private const string RejectedMessage = "Playback ticket rejected.";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IEndpointRouteBuilder MapPlaybackAuthorizationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/api/v1/playback/streams/ensure",
            HandleEnsureAsync);
        endpoints.MapPost(
            "/api/v1/media/hooks/on-play",
            HandleOnPlayAsync);
        return endpoints;
    }

    public static async Task<IResult> HandleEnsureAsync(
        HttpRequest request,
        ServerReadinessState readiness,
        IPlaybackStreamService service)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(service);
        if (!readiness.IsReady)
        {
            return Results.Json(
                new CatalogErrorDto(UnavailableCode, UnavailableMessage),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        EnsurePlaybackStreamRequest? payload;
        try
        {
            payload = await request.ReadFromJsonAsync<EnsurePlaybackStreamRequest>(
                    SerializerOptions,
                    request.HttpContext.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return ValidationResult();
        }
        catch (NotSupportedException)
        {
            return ValidationResult();
        }

        if (payload is null)
        {
            return ValidationResult();
        }

        var result = await service
            .EnsureAsync(payload, request.HttpContext.RequestAborted)
            .ConfigureAwait(false);
        return ToHttpResult(result);
    }

    public static async Task<IResult> HandleOnPlayAsync(
        HttpContext context,
        IZlmHookTrustPolicy trust,
        IPlaybackTicketValidator validator)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(trust);
        ArgumentNullException.ThrowIfNull(validator);

        if (!trust.IsTrusted(context.Connection.RemoteIpAddress))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        OnPlayPayload? payload;
        try
        {
            payload = await JsonSerializer.DeserializeAsync<OnPlayPayload>(
                    context.Request.Body,
                    SerializerOptions,
                    context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return RejectedResult();
        }
        catch (NotSupportedException)
        {
            return RejectedResult();
        }

        if (payload is null
            || string.IsNullOrWhiteSpace(payload.Vhost)
            || string.IsNullOrWhiteSpace(payload.App)
            || string.IsNullOrWhiteSpace(payload.Stream))
        {
            return RejectedResult();
        }

        var ticket = TryGetTicket(payload.Params);
        PlaybackTicketValidationResult validation;
        try
        {
            validation = await validator.ValidateAsync(
                ticket,
                payload.Vhost,
                payload.App,
                payload.Stream,
                DateTimeOffset.UtcNow,
                context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return RejectedResult();
        }

        return validation.IsValid
            ? Results.Json(new { code = 0, msg = "success" })
            : RejectedResult();
    }

    private static string? TryGetTicket(string? parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters))
        {
            return null;
        }

        try
        {
            var query = QueryHelpers.ParseQuery(parameters);
            return query.TryGetValue("ticket", out var values)
                && values.Count == 1
                ? values[0]
                : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static IResult ValidationResult() =>
        Results.Json(
            new CatalogErrorDto(ValidationCode, ValidationMessage),
            statusCode: StatusCodes.Status400BadRequest);

    private static IResult RejectedResult() =>
        Results.Json(new { code = 1, msg = RejectedMessage });

    private static IResult ToHttpResult(
        CatalogOperationResult<EnsurePlaybackStreamResponse> result) =>
        result.IsSuccess
            ? Results.Json(result.Value, statusCode: result.StatusCode)
            : Results.Json(result.Error, statusCode: result.StatusCode);

    private sealed record OnPlayPayload(
        [property: JsonPropertyName("vhost")] string? Vhost,
        [property: JsonPropertyName("app")] string? App,
        [property: JsonPropertyName("stream")] string? Stream,
        [property: JsonPropertyName("params")] string? Params);
}
