using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Server.Catalog;
using VideoMonitor.Server.Hosting;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Tests.Media;

public sealed class TestStreamApiTests
{
    private static readonly Guid SessionId =
        Guid.Parse("93000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task PostReturnsSafeSessionWithoutSensitiveFields()
    {
        var service = new RecordingService
        {
            StartResult = new CatalogOperationResult<TestSessionDto>(
                true,
                new TestSessionDto(
                    SessionId,
                    null,
                    null,
                    "videomonitor-test",
                    "test_0123456789abcdef0123456789abcdef",
                    new Uri("rtsp://playback.example/live"),
                    DateTimeOffset.UtcNow.AddMinutes(2)),
                200,
                null)
        };
        var request = CreateRequest(new TestStreamStartRequest(
            null,
            null,
            new CameraDeviceDraftDto(
                "10.0.0.5", 554, "admin", "secret", 1, StreamType.Main, TransportMode.Auto),
            DateTimeOffset.UtcNow));

        var result = await TestStreamEndpoints.HandleStartAsync(
            request.Request,
            ReadyState(),
            service);
        var response = await ExecuteAsync(result);

        Assert.Equal(200, response.StatusCode);
        Assert.Contains("test_0123456789abcdef0123456789abcdef", response.Body);
        Assert.DoesNotContain("secret", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProxyKey", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SourceUri", response.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OriginUrl", response.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteUsesExactSessionId()
    {
        var service = new RecordingService
        {
            StopResult = new CatalogOperationResult<object?>(true, null, 200, null)
        };
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        context.Request.RouteValues["sessionId"] = SessionId.ToString();

        var result = await TestStreamEndpoints.HandleStopAsync(
            context.Request,
            ReadyState(),
            service);
        var response = await ExecuteAsync(result);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(SessionId, service.LastStoppedSessionId);
    }

    private static DefaultHttpContext CreateRequest(TestStreamStartRequest payload)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(payload));
        return context;
    }

    private static ServerReadinessState ReadyState()
    {
        var state = new ServerReadinessState();
        state.MarkDatabaseReady();
        state.MarkSecretProtectionReady();
        return state;
    }

    private static async Task<(int StatusCode, string Body)> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        return ((int)context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    private sealed class RecordingService : ITestStreamService
    {
        public CatalogOperationResult<TestSessionDto> StartResult { get; init; } =
            new(false, null, 500, new CatalogErrorDto("error", "error"));

        public CatalogOperationResult<object?> StopResult { get; init; } =
            new(false, null, 500, new CatalogErrorDto("error", "error"));

        public Guid? LastStoppedSessionId { get; private set; }

        public Task<CatalogOperationResult<TestSessionDto>> StartAsync(
            TestStreamStartRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(StartResult);

        public Task<CatalogOperationResult<object?>> StopAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            LastStoppedSessionId = sessionId;
            return Task.FromResult(StopResult);
        }
    }
}
