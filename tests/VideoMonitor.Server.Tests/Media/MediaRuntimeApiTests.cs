using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Media;
using VideoMonitor.Core.Models;
using VideoMonitor.Server.Media;

namespace VideoMonitor.Server.Tests.Media;

public sealed class MediaRuntimeApiTests
{
    [Fact]
    public async Task ReadyRuntimeEndpoint_ReturnsSnapshotWithSafeRuntimeFields()
    {
        var key = CreateKey();
        var snapshot = new MediaRuntimeSnapshot(
            MediaServerHealth.Healthy,
            new[]
            {
                new MediaStreamRuntimeInfo(
                    key,
                    StreamRuntimeState.Ready,
                    SourceObservation.Reachable,
                    new ViewerCount(0),
                    StreamOwnership.OwnedCurrentProcess,
                    DateTimeOffset.Parse("2026-09-03T01:02:03Z"),
                    DateTimeOffset.Parse("2026-09-03T01:02:04Z"),
                    DateTimeOffset.Parse("2026-09-03T01:02:04Z"),
                    "MEDIA_OK",
                    "safe runtime state",
                    false)
            });

        using var baseFactory = new TestServerFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamManager>();
                services.AddSingleton<IStreamManager>(
                    new FixedStreamManager(snapshot));
            }));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/media/runtime");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("serverHealth", body, StringComparison.Ordinal);
        Assert.Contains("streams", body, StringComparison.Ordinal);
        Assert.Contains("runtimeState", body, StringComparison.Ordinal);
        Assert.Contains("sourceObservation", body, StringComparison.Ordinal);
        Assert.Contains("viewerCount", body, StringComparison.Ordinal);
        Assert.Contains("ownership", body, StringComparison.Ordinal);
        Assert.Contains("startedAtUtc", body, StringComparison.Ordinal);
        Assert.Contains("observedAtUtc", body, StringComparison.Ordinal);
        Assert.Contains("lastSuccessUtc", body, StringComparison.Ordinal);
        Assert.Contains("safeLastErrorCode", body, StringComparison.Ordinal);
        Assert.Contains("safeLastErrorMessage", body, StringComparison.Ordinal);
        Assert.Contains("isStale", body, StringComparison.Ordinal);
        Assert.Contains(key.DeviceId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        await AssertSafeRuntimeBodyAsync(response);
    }

    [Fact]
    public async Task RuntimeEndpoint_IsActuallyRegistered()
    {
        using var factory = new TestServerFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/media/runtime");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NotReadyRuntimeEndpoint_ReturnsServiceUnavailable()
    {
        using var factory = new TestServerFactory(failMachineProtection: true);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/media/runtime");
        var error = await response.Content.ReadFromJsonAsync<CatalogErrorDto>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("CATALOG_UNAVAILABLE", error.Code);
        Assert.Equal("Catalog service is unavailable.", error.Message);
    }

    [Fact]
    public async Task RuntimeEndpoint_DoesNotExposeSensitiveFields()
    {
        var snapshot = new MediaRuntimeSnapshot(
            MediaServerHealth.Healthy,
            new[]
            {
                new MediaStreamRuntimeInfo(
                    CreateKey(),
                    StreamRuntimeState.Ready,
                    SourceObservation.Reachable,
                    new ViewerCount(1),
                    StreamOwnership.OwnedCurrentProcess,
                    null,
                    null,
                    null,
                    null,
                    null,
                    false)
            });

        using var baseFactory = new TestServerFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStreamManager>();
                services.AddSingleton<IStreamManager>(
                    new FixedStreamManager(snapshot));
            }));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/media/runtime");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertSafeRuntimeBodyAsync(response);
    }

    private static MediaStreamKey CreateKey() =>
        new(
            Guid.Parse("91000000-0000-0000-0000-000000000001"),
            Guid.Parse("92000000-0000-0000-0000-000000000001"),
            StreamType.Main);

    private static async Task AssertSafeRuntimeBodyAsync(
        HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        foreach (var forbidden in new[]
                 {
                     "proxyKey",
                     "originUrl",
                     "sourceUri",
                     "password",
                     "secret",
                     "signingKey"
                 })
        {
            Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class FixedStreamManager : IStreamManager
    {
        private readonly MediaRuntimeSnapshot snapshot;

        public FixedStreamManager(MediaRuntimeSnapshot snapshot) =>
            this.snapshot = snapshot;

        public Task<StreamEnsureResult> EnsureStreamAsync(
            MediaStreamRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new StreamEnsureResult(false, null, "NOT_USED"));

        public Task CleanupOwnedStreamIfEligibleAsync(
            MediaStreamKey key,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public MediaRuntimeSnapshot GetSnapshot() => snapshot;
    }
}
