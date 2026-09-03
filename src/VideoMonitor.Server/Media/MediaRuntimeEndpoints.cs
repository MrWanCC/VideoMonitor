using VideoMonitor.Core.Catalog;
using VideoMonitor.Server.Hosting;

namespace VideoMonitor.Server.Media;

public static class MediaRuntimeEndpoints
{
    private const string UnavailableCode = "CATALOG_UNAVAILABLE";
    private const string UnavailableMessage = "Catalog service is unavailable.";

    public static IEndpointRouteBuilder MapMediaRuntimeEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1/media");

        api.MapGet("/runtime", (
            ServerReadinessState readiness,
            IStreamManager streamManager) =>
        {
            if (!readiness.IsReady)
            {
                return Results.Json(
                    new CatalogErrorDto(UnavailableCode, UnavailableMessage),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Json(
                streamManager.GetSnapshot(),
                statusCode: StatusCodes.Status200OK);
        });

        return endpoints;
    }
}
