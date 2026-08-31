using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Configuration;

namespace VideoMonitor.Core.Tests.Catalog;

public sealed class DeviceCatalogCommandServiceTests
{
    private static readonly Uri ServerUri = new("https://server-b/");

    [Fact]
    public async Task RemoteUpdate_PerformsOneWriteThenRefreshesCatalog()
    {
        var device = ExistingDevice("Before");
        var updated = ExistingDevice("After", device.Id, device.Revision + 1);
        var initial = new CatalogSnapshotDto([], [device]);
        var refreshed = new CatalogSnapshotDto([], [updated]);
        var handler = new CatalogHttpHandler(initial, refreshed)
        {
            UpdateResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent(updated)
            }
        };
        await using var fixture = await CommandFixture.ConnectAsync(handler, initial);
        using var httpClient = new HttpClient(handler);
        var apiClient = new CatalogApiClient(httpClient);
        var commands = new RemoteDeviceCatalogCommandService(
            fixture.Cache,
            apiClient,
            fixture.Coordinator);

        var result = await commands.UpdateDeviceAsync(
            device.Id,
            new UpdateDeviceRequest(
                device.GroupId,
                updated.Name,
                updated.IpAddress,
                updated.SdkPort,
                updated.RtspPort,
                updated.Username,
                "new-secret",
                updated.Manufacturer,
                updated.Model,
                updated.TransportMode,
                updated.Enabled,
                updated.Remark,
                device.Revision,
                []));

        Assert.Equal("After", result.Name);
        Assert.Equal(1, handler.Requests.Count(request =>
            request.Method == HttpMethod.Put
            && request.RequestUri!.AbsolutePath == $"/api/v1/devices/{device.Id}"));
        Assert.Equal(2, handler.CatalogRequestCount);
        Assert.Equal("After", fixture.Cache.GetDevice(device.Id)!.Name);
    }

    [Fact]
    public async Task AmbiguousUpdate_RefreshesButRemainsUncertainWithoutSuccessResponse()
    {
        var device = ExistingDevice("Before");
        var initial = new CatalogSnapshotDto([], [device]);
        var handler = new CatalogHttpHandler(initial, initial)
        {
            UpdateResponse = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = JsonContent("{}")
            }
        };
        await using var fixture = await CommandFixture.ConnectAsync(handler, initial);
        using var httpClient = new HttpClient(handler);
        var apiClient = new CatalogApiClient(httpClient);
        var commands = new RemoteDeviceCatalogCommandService(
            fixture.Cache,
            apiClient,
            fixture.Coordinator);

        var exception = await Assert.ThrowsAsync<CatalogMutationUncertainException>(() =>
            commands.UpdateDeviceAsync(
                device.Id,
                new UpdateDeviceRequest(
                    device.GroupId,
                    "After",
                    device.IpAddress,
                    device.SdkPort,
                    device.RtspPort,
                    device.Username,
                    "new-secret",
                    device.Manufacturer,
                    device.Model,
                    device.TransportMode,
                    device.Enabled,
                    device.Remark,
                    device.Revision,
                    [])));

        Assert.Equal("update-device", exception.Operation);
        Assert.DoesNotContain("new-secret", exception.Message);
        Assert.Equal(2, handler.CatalogRequestCount);
        Assert.Equal("Before", fixture.Cache.GetDevice(device.Id)!.Name);
    }

    [Fact]
    public async Task AmbiguousCreate_ConfirmsKnownIdentityAfterRefresh()
    {
        var device = ExistingDevice("Created");
        var initial = new CatalogSnapshotDto([], []);
        var refreshed = new CatalogSnapshotDto([], [device]);
        var handler = new CatalogHttpHandler(initial, refreshed)
        {
            WriteResponse = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = JsonContent(new { })
            }
        };
        await using var fixture = await CommandFixture.ConnectAsync(handler, initial);
        using var httpClient = new HttpClient(handler);
        var commands = new RemoteDeviceCatalogCommandService(
            fixture.Cache,
            new CatalogApiClient(httpClient),
            fixture.Coordinator);

        var result = await commands.CreateDeviceAsync(
            new CreateDeviceRequest(
                device.Id,
                device.GroupId,
                device.Name,
                device.IpAddress,
                device.SdkPort,
                device.RtspPort,
                device.Username,
                "new-secret",
                device.Manufacturer,
                device.Model,
                device.TransportMode,
                device.Enabled,
                device.Remark,
                []));

        Assert.Equal(device.Id, result.Id);
        Assert.Equal(2, handler.CatalogRequestCount);
        Assert.NotNull(fixture.Cache.GetDevice(device.Id));
    }

    [Fact]
    public async Task AmbiguousDelete_ConfirmsMissingIdentityAfterRefresh()
    {
        var device = ExistingDevice("ToDelete");
        var initial = new CatalogSnapshotDto([], [device]);
        var refreshed = new CatalogSnapshotDto([], []);
        var handler = new CatalogHttpHandler(initial, refreshed)
        {
            WriteResponse = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = JsonContent(new { })
            }
        };
        await using var fixture = await CommandFixture.ConnectAsync(handler, initial);
        using var httpClient = new HttpClient(handler);
        var commands = new RemoteDeviceCatalogCommandService(
            fixture.Cache,
            new CatalogApiClient(httpClient),
            fixture.Coordinator);

        await commands.DeleteDeviceAsync(device.Id, device.Revision);

        Assert.Equal(2, handler.CatalogRequestCount);
        Assert.Null(fixture.Cache.GetDevice(device.Id));
    }

    [Fact]
    public async Task OfflineCommands_AreNotWritableAndDoNotCallApi()
    {
        var initial = new CatalogSnapshotDto([], []);
        var handler = new CatalogHttpHandler(initial, initial);
        await using var fixture = new CommandFixture(handler, initial);
        using var httpClient = new HttpClient(handler);
        var commands = new RemoteDeviceCatalogCommandService(
            fixture.Cache,
            new CatalogApiClient(httpClient),
            fixture.Coordinator);

        Assert.False(commands.CanWrite);
        await Assert.ThrowsAsync<CatalogApiException>(() =>
            commands.DeleteDeviceAsync(Guid.NewGuid(), 1));
        Assert.Empty(handler.Requests);
    }

    private static CameraDeviceDto ExistingDevice(
        string name,
        Guid? id = null,
        long revision = 8) =>
        new(
            id ?? Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            "192.0.2.10",
            8000,
            554,
            "user",
            true,
            "Maker",
            "Model",
            TransportMode.Tcp,
            true,
            "remark",
            revision,
            []);

    private static StringContent JsonContent<T>(T value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private sealed class CommandFixture : IAsyncDisposable
    {
        public CommandFixture(
            CatalogHttpHandler handler,
            CatalogSnapshotDto initial)
        {
            Settings = new MemorySettingsStore();
            Dispatcher = new InlineDispatcher();
            Cache = new ClientCatalogCache(initial, Dispatcher);
            Coordinator = new ServerConnectionCoordinator(
                Settings,
                new CatalogApiClient(new HttpClient(handler)),
                Cache,
                Dispatcher,
                new TestClock());
        }

        public MemorySettingsStore Settings { get; }

        public InlineDispatcher Dispatcher { get; }

        public ClientCatalogCache Cache { get; }

        public ServerConnectionCoordinator Coordinator { get; }

        public static async Task<CommandFixture> ConnectAsync(
            CatalogHttpHandler handler,
            CatalogSnapshotDto initial)
        {
            var fixture = new CommandFixture(handler, initial);
            await fixture.Coordinator.SwitchServerAsync(ServerUri, () => false);
            return fixture;
        }

        public ValueTask DisposeAsync() => Coordinator.DisposeAsync();
    }

    private sealed class CatalogHttpHandler : HttpMessageHandler
    {
        private readonly CatalogSnapshotDto initial;
        private readonly CatalogSnapshotDto refreshed;

        public CatalogHttpHandler(
            CatalogSnapshotDto initial,
            CatalogSnapshotDto refreshed)
        {
            this.initial = initial;
            this.refreshed = refreshed;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        public int CatalogRequestCount { get; private set; }

        public HttpResponseMessage? UpdateResponse { get; init; }

        public HttpResponseMessage? WriteResponse { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.RequestUri!.AbsolutePath == "/health/ready")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent(new { status = "ready" })
                });
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/catalog")
            {
                CatalogRequestCount++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent(CatalogRequestCount == 1 ? initial : refreshed)
                });
            }

            if (request.Method == HttpMethod.Put)
            {
                return Task.FromResult(UpdateResponse is null
                    ? new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent(refreshed.Devices.Single())
                    }
                    : UpdateResponse);
            }

            if (request.Method == HttpMethod.Post
                || request.Method == HttpMethod.Delete)
            {
                return Task.FromResult(WriteResponse
                    ?? new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class MemorySettingsStore : IClientSettingsStore
    {
        public ClientSettings Settings { get; private set; } = ClientSettings.Empty;

        public ClientSettings Load() => Settings;

        public Task SaveAsync(
            ClientSettings settings,
            CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(
            Action action,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class TestClock : IClientConnectionClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public double NextJitterUnit() => 0.5;

        public Task DelayAsync(
            TimeSpan delay,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
