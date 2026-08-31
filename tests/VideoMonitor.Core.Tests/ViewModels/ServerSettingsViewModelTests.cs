using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Configuration;
using VideoMonitor.Wpf.ViewModels;

namespace VideoMonitor.Core.Tests.ViewModels;

public sealed class ServerSettingsViewModelTests
{
    [Fact]
    public async Task InitialBaseUrl_LoadsFromSettings()
    {
        await using var fixture = ConnectionFixture.Create();
        var viewModel = fixture.CreateSettingsViewModel();

        Assert.Equal("https://server-a/", viewModel.BaseUrl);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task InvalidUri_DoesNotProbe()
    {
        await using var fixture = await ConnectionFixture.CreateConnectedAsync();
        var viewModel = fixture.CreateSettingsViewModel();
        viewModel.BaseUrl = "server-b";

        await viewModel.TestConnectionCommand.ExecuteAsync(null);

        Assert.Empty(fixture.Api.ReadyCalls);
        Assert.False(viewModel.IsTestSuccessful);
    }

    [Fact]
    public async Task UnsupportedScheme_DoesNotProbe()
    {
        await using var fixture = await ConnectionFixture.CreateConnectedAsync();
        var viewModel = fixture.CreateSettingsViewModel();
        viewModel.BaseUrl = "file:///server-b";

        await viewModel.TestConnectionCommand.ExecuteAsync(null);

        Assert.Empty(fixture.Api.ReadyCalls);
        Assert.False(viewModel.IsTestSuccessful);
    }

    [Fact]
    public async Task TestConnection_DoesNotSwitchEndpoint()
    {
        await using var fixture = await ConnectionFixture.CreateConnectedAsync();
        var viewModel = fixture.CreateSettingsViewModel();
        viewModel.BaseUrl = "https://server-b";

        await viewModel.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal(1, fixture.Api.ReadyCalls.Count(uri => uri == new Uri("https://server-b")));
        Assert.Equal(1, fixture.Api.CatalogCalls.Count(uri => uri == new Uri("https://server-b")));
        Assert.Equal(new Uri("https://server-a"), fixture.Coordinator.Status.BaseUri);
        Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
        Assert.True(viewModel.IsTestSuccessful);
    }

    [Fact]
    public async Task TestConnection_DoesNotSaveSettings()
    {
        await using var fixture = await ConnectionFixture.CreateConnectedAsync();
        var viewModel = fixture.CreateSettingsViewModel();
        viewModel.BaseUrl = "https://server-b";

        await viewModel.TestConnectionCommand.ExecuteAsync(null);

        Assert.Equal(0, fixture.Settings.SaveCount);
        Assert.Equal("https://server-a/", fixture.Settings.Settings.Server.BaseUrl);
    }

    [Fact]
    public async Task SuccessfulTest_IsClearedWhenBaseUrlChanges()
    {
        await using var fixture = await ConnectionFixture.CreateConnectedAsync();
        var viewModel = fixture.CreateSettingsViewModel();
        viewModel.BaseUrl = "https://server-b";
        await viewModel.TestConnectionCommand.ExecuteAsync(null);

        viewModel.BaseUrl = "https://server-c";

        Assert.False(viewModel.IsTestSuccessful);
        Assert.Equal(string.Empty, viewModel.TestResultText);
    }

    [Fact]
    public async Task TestFailure_ShowsSafeError()
    {
        await using var fixture = await ConnectionFixture.CreateConnectedAsync();
        fixture.Api.ReadyHandler = (_, _) =>
            Task.FromException(new CatalogApiException("CATALOG_UNAVAILABLE"));
        var viewModel = fixture.CreateSettingsViewModel();
        viewModel.BaseUrl = "https://server-b";

        await viewModel.TestConnectionCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsTestSuccessful);
        Assert.Contains("CATALOG_UNAVAILABLE", viewModel.TestResultText);
        Assert.DoesNotContain("System.", viewModel.TestResultText);
    }

    [Fact]
    public async Task TestThenSave_PerformsIndependentProbeAgain()
    {
        await using var fixture = await ConnectionFixture.CreateConnectedAsync();
        var viewModel = fixture.CreateSettingsViewModel();
        viewModel.BaseUrl = "https://server-b";

        await viewModel.TestConnectionCommand.ExecuteAsync(null);
        Assert.Equal(1, fixture.Api.ReadyCalls.Count(uri => uri == new Uri("https://server-b")));
        Assert.Equal(1, fixture.Api.CatalogCalls.Count(uri => uri == new Uri("https://server-b")));
        Assert.Equal(new Uri("https://server-a"), fixture.Coordinator.Status.BaseUri);
        Assert.Equal(0, fixture.Settings.SaveCount);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(2, fixture.Api.ReadyCalls.Count(uri => uri == new Uri("https://server-b")));
        Assert.Equal(2, fixture.Api.CatalogCalls.Count(uri => uri == new Uri("https://server-b")));
        Assert.Equal(1, fixture.Settings.SaveCount);
        Assert.Equal(new Uri("https://server-b"), fixture.Coordinator.Status.BaseUri);
        Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
    }

    [Fact]
    public async Task Save_WithUnsavedDraft_DoesNotProbeOrSwitch()
    {
        await using var fixture = await ConnectionFixture.CreateConnectedAsync();
        var viewModel = fixture.CreateSettingsViewModel(() => true);
        viewModel.BaseUrl = "https://server-b";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Empty(fixture.Api.ReadyCalls);
        Assert.Empty(fixture.Api.CatalogCalls);
        Assert.Equal(0, fixture.Settings.SaveCount);
        Assert.Equal(new Uri("https://server-a"), fixture.Coordinator.Status.BaseUri);
        Assert.True(viewModel.HasSaveError);
        Assert.Contains("未保存", viewModel.SaveError);
    }

    [Fact]
    public async Task Save_PassesLiveDraftGuardToCoordinator()
    {
        await using var fixture = await ConnectionFixture.CreateConnectedAsync();
        var hasDraft = false;
        fixture.Api.CatalogHandler = (_, _) =>
        {
            hasDraft = true;
            return Task.FromResult(fixture.Api.Snapshot);
        };
        var viewModel = fixture.CreateSettingsViewModel(() => hasDraft);
        viewModel.BaseUrl = "https://server-b";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, fixture.Api.ReadyCalls.Count(uri => uri == new Uri("https://server-b")));
        Assert.Equal(1, fixture.Api.CatalogCalls.Count(uri => uri == new Uri("https://server-b")));
        Assert.Equal(0, fixture.Settings.SaveCount);
        Assert.Equal(new Uri("https://server-a"), fixture.Coordinator.Status.BaseUri);
        Assert.True(viewModel.HasSaveError);
    }

    [Fact]
    public async Task Save_SwitchesOnlyThroughCoordinator()
    {
        await using var fixture = await ConnectionFixture.CreateConnectedAsync();
        var viewModel = fixture.CreateSettingsViewModel();
        viewModel.BaseUrl = "https://server-b";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(new Uri("https://server-b"), fixture.Coordinator.Status.BaseUri);
        Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
        Assert.Equal(1, fixture.Settings.SaveCount);
    }

    [Fact]
    public async Task Save_SettingsPersistenceFailure_DoesNotReportSuccess()
    {
        await using var fixture = await ConnectionFixture.CreateConnectedAsync();
        fixture.Settings.SaveHandler = (_, _) =>
            Task.FromException(new InvalidOperationException("internal-settings-detail"));
        var viewModel = fixture.CreateSettingsViewModel();
        viewModel.BaseUrl = "https://server-b";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(new Uri("https://server-a"), fixture.Coordinator.Status.BaseUri);
        Assert.False(viewModel.IsTestSuccessful);
        Assert.True(viewModel.HasSaveError);
        Assert.DoesNotContain("internal-settings-detail", viewModel.SaveError);
    }

    [Fact]
    public async Task Save_ProbeFailure_DoesNotReportSuccess()
    {
        await using var fixture = await ConnectionFixture.CreateConnectedAsync();
        fixture.Api.ReadyHandler = (_, _) =>
            Task.FromException(new CatalogApiException("CATALOG_UNAVAILABLE"));
        var viewModel = fixture.CreateSettingsViewModel();
        viewModel.BaseUrl = "https://server-b";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(new Uri("https://server-a"), fixture.Coordinator.Status.BaseUri);
        Assert.False(viewModel.IsTestSuccessful);
        Assert.True(viewModel.HasSaveError);
    }

    [Fact]
    public async Task Save_SuccessUsesCoordinatorStatus()
    {
        await using var fixture = await ConnectionFixture.CreateConnectedAsync();
        var viewModel = fixture.CreateSettingsViewModel();
        viewModel.BaseUrl = "https://server-b";

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(new Uri("https://server-b"), fixture.Coordinator.Status.BaseUri);
        Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
        Assert.Contains("成功", viewModel.TestResultText);
    }

    [Fact]
    public async Task Unconfigured_MapsTo未配置()
    {
        await using var fixture = ConnectionFixture.Create();
        using var viewModel = new ServerStatusViewModel(fixture.Coordinator);

        Assert.Equal(ServerConnectionState.Unconfigured, viewModel.State);
        Assert.Equal("未配置", viewModel.StateText);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Connecting_MapsTo连接中()
    {
        await using var fixture = ConnectionFixture.Create();
        var readyStarted = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReady = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Settings.Settings = SettingsFor(new Uri("https://server-a"));
        fixture.Api.ReadyHandler = async (_, cancellationToken) =>
        {
            readyStarted.TrySetResult(null);
            await releaseReady.Task.WaitAsync(cancellationToken);
        };
        fixture.Clock.DelayHandler = (_, cancellationToken) =>
            Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        using var cancellation = new CancellationTokenSource();
        var run = fixture.Coordinator.RunAsync(cancellation.Token);
        await readyStarted.Task;
        using var viewModel = new ServerStatusViewModel(fixture.Coordinator);

        Assert.Equal(ServerConnectionState.Connecting, viewModel.State);
        Assert.Equal("连接中", viewModel.StateText);

        releaseReady.TrySetResult(null);
        cancellation.Cancel();
        await fixture.Coordinator.DisposeAsync();
        await run;
    }

    [Fact]
    public async Task Connected_MapsTo已连接()
    {
        await using var fixture = await ConnectionFixture.CreateConnectedAsync();
        using var viewModel = new ServerStatusViewModel(fixture.Coordinator);

        Assert.Equal(ServerConnectionState.Connected, viewModel.State);
        Assert.Equal("已连接", viewModel.StateText);
    }

    [Fact]
    public async Task NullLastSync_MapsToDoubleDash()
    {
        await using var fixture = ConnectionFixture.Create();
        using var viewModel = new ServerStatusViewModel(fixture.Coordinator);

        Assert.Equal("--", viewModel.LastSuccessfulSyncText);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task LastSync_UsesLocalTimeFormat()
    {
        await using var fixture = await ConnectionFixture.CreateConnectedAsync();
        using var viewModel = new ServerStatusViewModel(fixture.Coordinator);

        var expected = fixture.Coordinator.Status.LastSuccessfulSyncUtc!
            .Value
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss");

        Assert.Equal(expected, viewModel.LastSuccessfulSyncText);
    }

    [Fact]
    public async Task UnavailableAfterConnected_DoesNotRemainHealthy()
    {
        await using var fixture = await ConnectionFixture.CreateConnectedAsync();
        using var viewModel = new ServerStatusViewModel(fixture.Coordinator);
        fixture.Api.CatalogHandler = (_, _) =>
            Task.FromException<CatalogSnapshotDto>(
                new CatalogApiException("CATALOG_UNAVAILABLE"));

        await fixture.Coordinator.RefreshNowAsync();

        Assert.Equal(ServerConnectionState.Unavailable, viewModel.State);
        Assert.Equal("连接失败", viewModel.StateText);
        Assert.NotEqual("已连接", viewModel.StateText);
        Assert.NotEqual("系统运行正常", viewModel.StateText);
        Assert.NotEqual("安全运行中", viewModel.StateText);
    }

    private sealed class ConnectionFixture : IAsyncDisposable
    {
        private ConnectionFixture()
        {
            Settings = new FakeClientSettingsStore
            {
                Settings = SettingsFor(new Uri("https://server-a"))
            };
            Api = new FakeCatalogApi();
            Clock = new FakeConnectionClock();
            Dispatcher = new InlineDispatcher();
            Cache = new ClientCatalogCache(
                new CatalogSnapshotDto([], []),
                Dispatcher);
            Coordinator = new ServerConnectionCoordinator(
                Settings,
                Api,
                Cache,
                Dispatcher,
                Clock);
        }

        public FakeClientSettingsStore Settings { get; }
        public FakeCatalogApi Api { get; }
        public FakeConnectionClock Clock { get; }
        public InlineDispatcher Dispatcher { get; }
        public ClientCatalogCache Cache { get; }
        public ServerConnectionCoordinator Coordinator { get; }

        public static ConnectionFixture Create() => new();

        public static async Task<ConnectionFixture> CreateConnectedAsync()
        {
            var fixture = new ConnectionFixture();
            fixture.Api.Snapshot = Snapshot("Server A");
            await fixture.Coordinator.SwitchServerAsync(
                new Uri("https://server-a"),
                () => false);
            fixture.Api.ResetCounters();
            fixture.Settings.SaveCount = 0;
            return fixture;
        }

        public ServerSettingsViewModel CreateSettingsViewModel(
            Func<bool>? hasUnsavedDraft = null) =>
            new(
                Coordinator,
                Settings,
                hasUnsavedDraft ?? (() => false));

        public ValueTask DisposeAsync() => Coordinator.DisposeAsync();
    }

    private sealed class FakeCatalogApi : ICatalogConnectionClient
    {
        public CatalogSnapshotDto Snapshot { get; set; } = Snapshot("Server B");

        public Func<Uri, CancellationToken, Task>? ReadyHandler { get; set; }

        public Func<Uri, CancellationToken, Task<CatalogSnapshotDto>>?
            CatalogHandler { get; set; }

        public List<Uri> ReadyCalls { get; } = [];

        public List<Uri> CatalogCalls { get; } = [];

        public Task CheckReadyAsync(
            Uri baseUri,
            CancellationToken cancellationToken = default)
        {
            ReadyCalls.Add(baseUri);
            return ReadyHandler?.Invoke(baseUri, cancellationToken)
                ?? Task.CompletedTask;
        }

        public Task<CatalogSnapshotDto> GetCatalogAsync(
            Uri baseUri,
            CancellationToken cancellationToken = default)
        {
            CatalogCalls.Add(baseUri);
            return CatalogHandler?.Invoke(baseUri, cancellationToken)
                ?? Task.FromResult(Snapshot);
        }

        public void ResetCounters()
        {
            ReadyCalls.Clear();
            CatalogCalls.Clear();
        }
    }

    private sealed class FakeClientSettingsStore : IClientSettingsStore
    {
        public ClientSettings Settings { get; set; } = ClientSettings.Empty;

        public int SaveCount { get; set; }

        public Func<ClientSettings, CancellationToken, Task>? SaveHandler { get; set; }

        public ClientSettings Load() => Settings;

        public async Task SaveAsync(
            ClientSettings settings,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (SaveHandler is not null)
            {
                await SaveHandler(settings, cancellationToken);
                return;
            }

            Settings = settings;
        }
    }

    private sealed class FakeConnectionClock : IClientConnectionClock
    {
        public DateTimeOffset UtcNow { get; set; } =
            DateTimeOffset.Parse("2026-08-31T00:00:00Z");

        public Func<TimeSpan, CancellationToken, Task>? DelayHandler { get; set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            DelayHandler?.Invoke(delay, cancellationToken)
            ?? Task.CompletedTask;

        public double NextJitterUnit() => 0.5;
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

    private static ClientSettings SettingsFor(Uri uri) =>
        new(new ClientServerSettings(uri.ToString()));

    private static CatalogSnapshotDto Snapshot(string name) =>
        new(
            [new DeviceGroupDto(
                Guid.NewGuid(),
                name,
                null,
                0,
                true,
                MonitorGroupType.Chute,
                1)],
            []);
}
