using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Server.Catalog;
using VideoMonitor.Server.Hosting;

namespace VideoMonitor.Server.Media;

public static class TestStreamEndpoints
{
    private const string UnavailableCode = "MEDIA_UNAVAILABLE";
    private const string UnavailableMessage = "Media service is unavailable.";
    private const string ValidationCode = "CATALOG_VALIDATION_FAILED";
    private const string ValidationMessage = "Test stream request validation failed.";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IEndpointRouteBuilder MapTestStreamEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/test-streams", HandleStartAsync);
        endpoints.MapDelete("/api/v1/test-streams/{sessionId}", HandleStopAsync);
        return endpoints;
    }

    public static async Task<IResult> HandleStartAsync(
        HttpRequest request,
        ServerReadinessState readiness,
        ITestStreamService service)
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

        TestStreamStartRequest? payload;
        try
        {
            payload = await request.ReadFromJsonAsync<TestStreamStartRequest>(
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

        if (payload is null || payload.Draft is null)
        {
            return ValidationResult();
        }

        return ToHttpResult(await service.StartAsync(
                payload,
                request.HttpContext.RequestAborted)
            .ConfigureAwait(false));
    }

    public static async Task<IResult> HandleStopAsync(
        HttpRequest request,
        ServerReadinessState readiness,
        ITestStreamService service)
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

        if (!Guid.TryParse(
                request.RouteValues["sessionId"]?.ToString(),
                out var sessionId))
        {
            return ValidationResult();
        }

        return ToHttpResult(await service.StopAsync(
                sessionId,
                request.HttpContext.RequestAborted)
            .ConfigureAwait(false));
    }

    private static IResult ValidationResult() =>
        Results.Json(
            new CatalogErrorDto(ValidationCode, ValidationMessage),
            statusCode: StatusCodes.Status400BadRequest);

    private static IResult ToHttpResult<T>(CatalogOperationResult<T> result)
    {
        if (!result.IsSuccess)
        {
            return Results.Json(result.Error, statusCode: result.StatusCode);
        }

        return result.StatusCode == StatusCodes.Status204NoContent
            ? Results.NoContent()
            : Results.Json(result.Value, statusCode: result.StatusCode);
    }
}
