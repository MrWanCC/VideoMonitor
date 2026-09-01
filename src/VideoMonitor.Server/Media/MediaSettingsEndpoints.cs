using System.Text.Json;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Server.Catalog;
using VideoMonitor.Server.Hosting;

namespace VideoMonitor.Server.Media;

public static class MediaSettingsEndpoints
{
    private const string ValidationCode = "MEDIA_SETTINGS_VALIDATION_FAILED";
    private const string ValidationMessage = "Media settings request validation failed.";
    private const string UnavailableCode = "CATALOG_UNAVAILABLE";
    private const string UnavailableMessage = "Catalog service is unavailable.";

    public static IEndpointRouteBuilder MapMediaSettingsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1/media");

        api.MapGet("/settings", async (
            HttpRequest request,
            ServerReadinessState readiness,
            IMediaSettingsService service) =>
        {
            var unavailable = RequireReady(readiness);
            if (unavailable is not null)
            {
                return unavailable;
            }

            try
            {
                return Results.Json(
                    await service.GetAsync(request.HttpContext.RequestAborted)
                        .ConfigureAwait(false),
                    statusCode: StatusCodes.Status200OK);
            }
            catch (OperationCanceledException)
                when (request.HttpContext.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return Results.Json(
                    new CatalogErrorDto(
                        "MEDIA_SETTINGS_READ_FAILED",
                        "Media settings read failed."),
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        api.MapPut("/settings", async (
            HttpRequest request,
            ServerReadinessState readiness,
            IMediaSettingsService service) =>
        {
            var unavailable = RequireReady(readiness);
            if (unavailable is not null)
            {
                return unavailable;
            }

            var parsed = await ReadRequestAsync<UpdateMediaSettingsRequest>(request)
                .ConfigureAwait(false);
            if (parsed.Error is not null)
            {
                return parsed.Error;
            }

            return ToHttpResult(await service.UpdateAsync(
                    parsed.Value!,
                    request.HttpContext.RequestAborted)
                .ConfigureAwait(false));
        });

        api.MapPost("/settings/test", async (
            HttpRequest request,
            ServerReadinessState readiness,
            IMediaSettingsService service) =>
        {
            var unavailable = RequireReady(readiness);
            if (unavailable is not null)
            {
                return unavailable;
            }

            var parsed = await ReadRequestAsync<TestMediaSettingsRequest>(request)
                .ConfigureAwait(false);
            if (parsed.Error is not null)
            {
                return parsed.Error;
            }

            return ToHttpResult(await service.TestAsync(
                    parsed.Value!,
                    request.HttpContext.RequestAborted)
                .ConfigureAwait(false));
        });

        return endpoints;
    }

    private static IResult? RequireReady(ServerReadinessState readiness) =>
        readiness.IsReady
            ? null
            : Results.Json(
                new CatalogErrorDto(UnavailableCode, UnavailableMessage),
                statusCode: StatusCodes.Status503ServiceUnavailable);

    private static async Task<(T? Value, IResult? Error)> ReadRequestAsync<T>(
        HttpRequest request)
    {
        if (!request.HasJsonContentType())
        {
            return (default, ValidationResult());
        }

        try
        {
            return (
                await request.ReadFromJsonAsync<T>(request.HttpContext.RequestAborted)
                    .ConfigureAwait(false),
                null);
        }
        catch (JsonException)
        {
            return (default, ValidationResult());
        }
        catch (NotSupportedException)
        {
            return (default, ValidationResult());
        }
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

        return Results.Json(result.Value, statusCode: result.StatusCode);
    }
}
