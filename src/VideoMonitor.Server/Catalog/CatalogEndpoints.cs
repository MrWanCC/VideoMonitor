using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Infrastructure.Persistence;
using VideoMonitor.Server.Hosting;

namespace VideoMonitor.Server.Catalog;

public static class CatalogEndpoints
{
    private const string ValidationCode = "CATALOG_VALIDATION_FAILED";
    private const string ValidationMessage = "Catalog request validation failed.";
    private const string UnavailableCode = "CATALOG_UNAVAILABLE";
    private const string UnavailableMessage = "Catalog service is unavailable.";

    public static IEndpointRouteBuilder MapCatalogEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");

        api.MapGet("/catalog", async (
            HttpRequest request,
            ServerReadinessState readiness,
            CatalogApplicationService service) =>
        {
            var unavailable = RequireReady(readiness);
            if (unavailable is not null)
            {
                return unavailable;
            }

            return ToHttpResult(await service.GetCatalogAsync(
                    request.HttpContext.RequestAborted)
                .ConfigureAwait(false));
        });

        api.MapGet("/device-groups", async (
            HttpRequest request,
            ServerReadinessState readiness,
            CatalogApplicationService service) =>
        {
            var unavailable = RequireReady(readiness);
            if (unavailable is not null)
            {
                return unavailable;
            }

            return ToHttpResult(await service.GetGroupsAsync(
                    request.HttpContext.RequestAborted)
                .ConfigureAwait(false));
        });

        api.MapPost("/device-groups", async (
            HttpRequest request,
            ServerReadinessState readiness,
            CatalogApplicationService service) =>
        {
            var unavailable = RequireReady(readiness);
            if (unavailable is not null)
            {
                return unavailable;
            }

            var parsed = await ReadRequestAsync<CreateGroupRequest>(request)
                .ConfigureAwait(false);
            if (parsed.Error is not null)
            {
                return parsed.Error;
            }

            return ToHttpResult(await service.CreateGroupAsync(
                    parsed.Value,
                    request.HttpContext.RequestAborted)
                .ConfigureAwait(false));
        });

        api.MapPut("/device-groups/{id}", async (
            HttpRequest request,
            ServerReadinessState readiness,
            CatalogApplicationService service) =>
        {
            var unavailable = RequireReady(readiness);
            if (unavailable is not null)
            {
                return unavailable;
            }

            if (!TryParseRouteId(request, out var id, out var idError))
            {
                return idError!;
            }

            var parsed = await ReadRequestAsync<UpdateGroupRequest>(request)
                .ConfigureAwait(false);
            if (parsed.Error is not null)
            {
                return parsed.Error;
            }

            return ToHttpResult(await service.UpdateGroupAsync(
                    id,
                    parsed.Value,
                    request.HttpContext.RequestAborted)
                .ConfigureAwait(false));
        });

        api.MapDelete("/device-groups/{id}", async (
            HttpRequest request,
            ServerReadinessState readiness,
            CatalogApplicationService service) =>
        {
            var unavailable = RequireReady(readiness);
            if (unavailable is not null)
            {
                return unavailable;
            }

            if (!TryParseRouteId(request, out var id, out var idError))
            {
                return idError!;
            }

            if (!TryParseExpectedRevision(request, out var expectedRevision))
            {
                return ValidationResult();
            }

            return ToHttpResult(await service.DeleteGroupAsync(
                    id,
                    expectedRevision,
                    request.HttpContext.RequestAborted)
                .ConfigureAwait(false));
        });

        api.MapGet("/devices", async (
            HttpRequest request,
            ServerReadinessState readiness,
            CatalogApplicationService service) =>
        {
            var unavailable = RequireReady(readiness);
            if (unavailable is not null)
            {
                return unavailable;
            }

            if (!TryParseOptionalGroupId(request, out var groupId))
            {
                return ValidationResult();
            }

            return ToHttpResult(await service.GetDevicesAsync(
                    groupId,
                    request.HttpContext.RequestAborted)
                .ConfigureAwait(false));
        });

        api.MapGet("/devices/{id}", async (
            HttpRequest request,
            ServerReadinessState readiness,
            CatalogApplicationService service) =>
        {
            var unavailable = RequireReady(readiness);
            if (unavailable is not null)
            {
                return unavailable;
            }

            if (!TryParseRouteId(request, out var id, out var idError))
            {
                return idError!;
            }

            return ToHttpResult(await service.GetDeviceAsync(
                    id,
                    request.HttpContext.RequestAborted)
                .ConfigureAwait(false));
        });

        api.MapPost("/devices", async (
            HttpRequest request,
            ServerReadinessState readiness,
            CatalogApplicationService service) =>
        {
            var unavailable = RequireReady(readiness);
            if (unavailable is not null)
            {
                return unavailable;
            }

            var parsed = await ReadRequestAsync<CreateDeviceRequest>(request)
                .ConfigureAwait(false);
            if (parsed.Error is not null)
            {
                return parsed.Error;
            }

            return ToHttpResult(await service.CreateDeviceAsync(
                    parsed.Value,
                    request.HttpContext.RequestAborted)
                .ConfigureAwait(false));
        });

        api.MapPut("/devices/{id}", async (
            HttpRequest request,
            ServerReadinessState readiness,
            CatalogApplicationService service) =>
        {
            var unavailable = RequireReady(readiness);
            if (unavailable is not null)
            {
                return unavailable;
            }

            if (!TryParseRouteId(request, out var id, out var idError))
            {
                return idError!;
            }

            var parsed = await ReadRequestAsync<UpdateDeviceRequest>(request)
                .ConfigureAwait(false);
            if (parsed.Error is not null)
            {
                return parsed.Error;
            }

            return ToHttpResult(await service.UpdateDeviceAsync(
                    id,
                    parsed.Value,
                    request.HttpContext.RequestAborted)
                .ConfigureAwait(false));
        });

        api.MapDelete("/devices/{id}", async (
            HttpRequest request,
            ServerReadinessState readiness,
            CatalogApplicationService service) =>
        {
            var unavailable = RequireReady(readiness);
            if (unavailable is not null)
            {
                return unavailable;
            }

            if (!TryParseRouteId(request, out var id, out var idError))
            {
                return idError!;
            }

            if (!TryParseExpectedRevision(request, out var expectedRevision))
            {
                return ValidationResult();
            }

            return ToHttpResult(await service.DeleteDeviceAsync(
                    id,
                    expectedRevision,
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

    private static bool TryParseRouteId(
        HttpRequest request,
        out Guid id,
        out IResult? error)
    {
        var rawId = request.RouteValues["id"]?.ToString();
        if (Guid.TryParse(rawId, out id))
        {
            error = null;
            return true;
        }

        error = ValidationResult();
        return false;
    }

    private static bool TryParseOptionalGroupId(
        HttpRequest request,
        out Guid? groupId)
    {
        if (!request.Query.TryGetValue("groupId", out var value))
        {
            groupId = null;
            return true;
        }

        if (value.Count == 1 && Guid.TryParse(value[0], out var parsed))
        {
            groupId = parsed;
            return true;
        }

        groupId = null;
        return false;
    }

    private static bool TryParseExpectedRevision(
        HttpRequest request,
        out long expectedRevision)
    {
        if (request.Query.TryGetValue("expectedRevision", out var value)
            && value.Count == 1
            && long.TryParse(value[0], out expectedRevision)
            && expectedRevision > 0)
        {
            return true;
        }

        expectedRevision = default;
        return false;
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
