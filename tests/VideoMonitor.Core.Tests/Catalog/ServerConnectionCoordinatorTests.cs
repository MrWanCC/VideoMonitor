using VideoMonitor.Core.Catalog;
using VideoMonitor.Core.Models;
using VideoMonitor.Wpf.Catalog;
using VideoMonitor.Wpf.Configuration;

namespace VideoMonitor.Core.Tests.Catalog;

public sealed class ServerConnectionCoordinatorTests
{
    private static readonly Uri ServerA = new("https://server-a");
    private static readonly Uri ServerB = new("https://server-b");

    [Fact]
    public async Task UnconfiguredRun_RemainsAliveUntilCancellation()
    {
        var fixture = new ConnectionFixture();
        using var cancellation = new CancellationTokenSource();

        var run = fixture.Coordinator.RunAsync(cancellation.Token);

        Assert.Equal(
            ServerConnectionState.Unconfigured,
            fixture.Coordinator.Status.State);
        Assert.False(run.IsCompleted);
        Assert.Empty(fixture.Api.ReadyCalls);
        Assert.Empty(fixture.Api.CatalogCalls);

        cancellation.Cancel();
        await run;
    }

    [Fact]
    public async Task NoConfiguration_IsUnconfiguredAndDoesNotCallApi()
    {
        var fixture = new ConnectionFixture();
        using var cancellation = new CancellationTokenSource();
        var run = fixture.Coordinator.RunAsync(cancellation.Token);

        Assert.Equal(
            new ServerConnectionStatus(
                null,
                ServerConnectionState.Unconfigured,
                null,
                false),
            fixture.Coordinator.Status);
        Assert.Empty(fixture.Api.ReadyCalls);
        Assert.Empty(fixture.Api.CatalogCalls);
        Assert.False(run.IsCompleted);

        cancellation.Cancel();
        await run;
    }

    [Fact]
    public async Task ConcurrentRefreshFromServerA_CannotOverwriteSuccessfulSwitchToServerB()
    {
        var snapshotA1 = Snapshot("A1");
        var snapshotA2 = Snapshot("A2");
        var snapshotB = Snapshot("B");
        var fixture = await ConnectionFixture.ConnectedToAsync(ServerA, snapshotA1);
        var aRefreshEntered = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var bSwitchStarted = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseARefresh = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Api.ReadyHandler = (uri, _) =>
        {
            if (uri == ServerB)
            {
                bSwitchStarted.TrySetResult(null);
            }

            return Task.CompletedTask;
        };
        fixture.Api.CatalogHandler = async (uri, _) =>
        {
            if (uri == ServerA)
            {
                aRefreshEntered.TrySetResult(null);
                await releaseARefresh.Task;
                return snapshotA2;
            }

            return snapshotB;
        };
        var bCommitted = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Coordinator.StatusChanged += (_, _) =>
        {
            if (fixture.Coordinator.Status.BaseUri == ServerB
                && fixture.Coordinator.Status.State == ServerConnectionState.Connected)
            {
                bCommitted.TrySetResult(null);
            }
        };

        var refresh = fixture.Coordinator.RefreshNowAsync();
        await aRefreshEntered.Task;
        var switchTask = fixture.Coordinator.SwitchServerAsync(ServerB, () => false);
        await bSwitchStarted.Task;
        await bCommitted.Task;
        releaseARefresh.SetResult(null);
        await Task.WhenAll(refresh, switchTask);

        Assert.Equal(ServerB, fixture.Coordinator.Status.BaseUri);
        Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
        Assert.Same(snapshotB, fixture.Cache.Snapshot);
        Assert.Equal(ServerB.ToString(), fixture.Settings.Settings.Server.BaseUrl);
    }

    [Fact]
    public async Task PeriodicRefreshFromServerA_CannotOverwriteSuccessfulSwitchToServerB()
    {
        var snapshotA1 = Snapshot("A1");
        var snapshotA2 = Snapshot("A2");
        var snapshotB = Snapshot("B");
        var fixture = new ConnectionFixture();
        fixture.Settings.Settings = SettingsFor(ServerA);
        var aCatalogCall = 0;
        var periodicAEntered = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseARefresh = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Api.CatalogHandler = async (uri, _) =>
        {
            if (uri == ServerA)
            {
                aCatalogCall++;
                if (aCatalogCall == 2)
                {
                    periodicAEntered.TrySetResult(null);
                    await releaseARefresh.Task;
                    return snapshotA2;
                }

                return snapshotA1;
            }

            return snapshotB;
        };
        fixture.Clock.DelayHandler = (_, _) => Task.CompletedTask;
        using var cancellation = new CancellationTokenSource();
        var run = fixture.Coordinator.RunAsync(cancellation.Token);
        await periodicAEntered.Task;

        var switchTask = fixture.Coordinator.SwitchServerAsync(ServerB, () => false);
        await switchTask;
        releaseARefresh.SetResult(null);
        fixture.Clock.DelayHandler = (_, _) =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };
        await run;

        Assert.Equal(ServerB, fixture.Coordinator.Status.BaseUri);
        Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
        Assert.Same(snapshotB, fixture.Cache.Snapshot);
    }

    [Fact]
    public async Task InitialConnect_SucceedsOnlyAfterReadyAndCatalog()
    {
        var fixture = new ConnectionFixture();
        fixture.Settings.Settings = SettingsFor(ServerA);
        var snapshot = Snapshot("A");
        fixture.Api.Snapshot = snapshot;
        using var cancellation = new CancellationTokenSource();
        fixture.Clock.DelayHandler = (_, _) =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };

        await fixture.Coordinator.RunAsync(cancellation.Token);

        Assert.Equal([ServerA], fixture.Api.ReadyCalls);
        Assert.Equal([ServerA], fixture.Api.CatalogCalls);
        Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
        Assert.Equal(ServerA, fixture.Coordinator.Status.BaseUri);
        Assert.False(fixture.Coordinator.Status.IsStale);
        Assert.Same(snapshot, fixture.Cache.Snapshot);
    }

    [Fact]
    public async Task FirstConnectionFailure_LeavesEmptyNonStaleCache()
    {
        var fixture = new ConnectionFixture();
        fixture.Settings.Settings = SettingsFor(ServerA);
        fixture.Api.ReadyHandler = (_, _) =>
            Task.FromException(new CatalogApiException("CATALOG_UNAVAILABLE"));
        using var cancellation = new CancellationTokenSource();
        fixture.Clock.DelayHandler = (_, _) =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };
        var initial = fixture.Cache.Snapshot;

        await fixture.Coordinator.RunAsync(cancellation.Token);

        Assert.Equal(ServerConnectionState.Unavailable, fixture.Coordinator.Status.State);
        Assert.Equal(ServerA, fixture.Coordinator.Status.BaseUri);
        Assert.Null(fixture.Coordinator.Status.LastSuccessfulSyncUtc);
        Assert.False(fixture.Coordinator.Status.IsStale);
        Assert.Same(initial, fixture.Cache.Snapshot);
    }

    [Fact]
    public async Task SuccessfulRefresh_UpdatesLastSync()
    {
        var fixture = await ConnectionFixture.ConnectedToAsync(ServerA, Snapshot("A"));
        var refreshed = Snapshot("B");
        fixture.Api.Snapshot = refreshed;
        fixture.Clock.UtcNow = DateTimeOffset.Parse("2026-08-31T01:00:00Z");

        await fixture.Coordinator.RefreshNowAsync();

        Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
        Assert.Equal(fixture.Clock.UtcNow, fixture.Coordinator.Status.LastSuccessfulSyncUtc);
        Assert.False(fixture.Coordinator.Status.IsStale);
        Assert.Same(refreshed, fixture.Cache.Snapshot);
    }

    [Fact]
    public async Task FailedRefresh_PreservesStaleSnapshot()
    {
        var snapshot = Snapshot("A");
        var fixture = await ConnectionFixture.ConnectedToAsync(ServerA, snapshot);
        var lastSync = fixture.Coordinator.Status.LastSuccessfulSyncUtc;
        fixture.Api.CatalogHandler = (_, _) =>
            Task.FromException<CatalogSnapshotDto>(
                new CatalogApiException("CATALOG_UNAVAILABLE"));

        await fixture.Coordinator.RefreshNowAsync();

        Assert.Equal(ServerConnectionState.Unavailable, fixture.Coordinator.Status.State);
        Assert.Equal(ServerA, fixture.Coordinator.Status.BaseUri);
        Assert.Equal(lastSync, fixture.Coordinator.Status.LastSuccessfulSyncUtc);
        Assert.True(fixture.Coordinator.Status.IsStale);
        Assert.Same(snapshot, fixture.Cache.Snapshot);
    }

    [Fact]
    public async Task RefreshNowFailure_InterruptsPeriodicWaitAndUsesTwoSecondReconnectDelay()
    {
        var fixture = new ConnectionFixture();
        fixture.Settings.Settings = SettingsFor(ServerA);
        var recovered = Snapshot("A recovered");
        var catalogCall = 0;
        fixture.Api.CatalogHandler = (_, _) =>
        {
            catalogCall++;
            return catalogCall == 2
                ? Task.FromException<CatalogSnapshotDto>(
                    new CatalogApiException("CATALOG_UNAVAILABLE"))
                : Task.FromResult(recovered);
        };

        var periodicWaitEntered = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var periodicWait = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var reconnectDelayEntered = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var unavailableObserved = new TaskCompletionSource<ServerConnectionStatus>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Coordinator.StatusChanged += (_, _) =>
        {
            if (fixture.Coordinator.Status.State
                == ServerConnectionState.Unavailable)
            {
                unavailableObserved.TrySetResult(fixture.Coordinator.Status);
            }
        };
        using var cancellation = new CancellationTokenSource();
        var periodicWaitCount = 0;
        TimeSpan? reconnectDelay = null;
        fixture.Clock.DelayHandler = (delay, token) =>
        {
            if (delay == TimeSpan.FromSeconds(30))
            {
                periodicWaitCount++;
                if (periodicWaitCount == 1)
                {
                    periodicWaitEntered.TrySetResult(null);
                    return periodicWait.Task.WaitAsync(token);
                }

                cancellation.Cancel();
                return Task.CompletedTask;
            }

            if (delay == TimeSpan.FromSeconds(2))
            {
                reconnectDelay = delay;
                reconnectDelayEntered.TrySetResult(null);
            }

            return Task.CompletedTask;
        };

        var run = fixture.Coordinator.RunAsync(cancellation.Token);
        try
        {
            await periodicWaitEntered.Task;

            Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
            Assert.Equal(ServerA, fixture.Coordinator.Status.BaseUri);

            await fixture.Coordinator.RefreshNowAsync();

            var unavailable = await unavailableObserved.Task;
            Assert.Equal(ServerConnectionState.Unavailable, unavailable.State);
            Assert.Equal(ServerA, unavailable.BaseUri);
            Assert.True(unavailable.IsStale);
            Assert.False(periodicWait.Task.IsCompleted);

            await reconnectDelayEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(TimeSpan.FromSeconds(2), reconnectDelay);

            await run;

            Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
            Assert.Equal(ServerA, fixture.Coordinator.Status.BaseUri);
            Assert.False(fixture.Coordinator.Status.IsStale);
            Assert.Same(recovered, fixture.Cache.Snapshot);
            Assert.Equal([ServerA, ServerA, ServerA], fixture.Api.CatalogCalls);
            Assert.Equal(
                [
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(30)
                ],
                fixture.Clock.Delays);
        }
        finally
        {
            cancellation.Cancel();
            await run;
        }
    }

    [Fact]
    public async Task Reconnect_Uses2_5_10_15_15BaseSchedule()
    {
        var fixture = new ConnectionFixture();
        fixture.Settings.Settings = SettingsFor(ServerA);
        fixture.Api.ReadyHandler = (_, _) =>
            Task.FromException(new CatalogApiException("CATALOG_UNAVAILABLE"));
        fixture.Clock.JitterUnit = 0.5;
        using var cancellation = new CancellationTokenSource();
        fixture.Clock.DelayHandler = (_, _) =>
        {
            if (fixture.Clock.Delays.Count == 5)
            {
                cancellation.Cancel();
            }

            return Task.CompletedTask;
        };

        await fixture.Coordinator.RunAsync(cancellation.Token);

        Assert.Equal(
            [
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(15)
            ],
            fixture.Clock.Delays);
    }

    [Fact]
    public async Task Delay_UsesBoundedDeterministicJitter()
    {
        var fixture = new ConnectionFixture();
        fixture.Settings.Settings = SettingsFor(ServerA);
        fixture.Api.ReadyHandler = (_, _) =>
            Task.FromException(new CatalogApiException("CATALOG_UNAVAILABLE"));
        fixture.Clock.JitterUnits.Enqueue(0.0);
        fixture.Clock.JitterUnits.Enqueue(0.5);
        fixture.Clock.JitterUnits.Enqueue(0.999999);
        using var cancellation = new CancellationTokenSource();
        fixture.Clock.DelayHandler = (_, _) =>
        {
            if (fixture.Clock.Delays.Count == 3)
            {
                cancellation.Cancel();
            }

            return Task.CompletedTask;
        };

        await fixture.Coordinator.RunAsync(cancellation.Token);

        Assert.Equal(
            TimeSpan.FromTicks((long)(TimeSpan.FromSeconds(2).Ticks * 0.8)),
            fixture.Clock.Delays[0]);
        Assert.Equal(TimeSpan.FromSeconds(5), fixture.Clock.Delays[1]);
        Assert.InRange(
            fixture.Clock.Delays[2],
            TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(12));
    }

    [Fact]
    public async Task ConnectedPeriodicRefresh_Uses30SecondBase()
    {
        var fixture = new ConnectionFixture();
        fixture.Settings.Settings = SettingsFor(ServerA);
        var first = Snapshot("A");
        var second = Snapshot("B");
        fixture.Api.Snapshot = first;
        var catalogCall = 0;
        fixture.Api.CatalogHandler = (_, _) =>
        {
            catalogCall++;
            return Task.FromResult(catalogCall == 1 ? first : second);
        };
        fixture.Clock.JitterUnit = 0.5;
        using var cancellation = new CancellationTokenSource();
        fixture.Clock.DelayHandler = (_, _) =>
        {
            if (fixture.Clock.Delays.Count == 2)
            {
                cancellation.Cancel();
            }

            return Task.CompletedTask;
        };

        await fixture.Coordinator.RunAsync(cancellation.Token);

        Assert.Equal(
            [TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30)],
            fixture.Clock.Delays);
        Assert.Equal(2, fixture.Api.CatalogCalls.Count);
        Assert.Same(second, fixture.Cache.Snapshot);
        Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
    }

    [Fact]
    public async Task Refresh_IsSingleFlight()
    {
        var fixture = await ConnectionFixture.ConnectedToAsync(ServerA, Snapshot("A"));
        fixture.Api.ResetCounters();
        var entered = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Api.CatalogHandler = async (_, _) =>
        {
            entered.SetResult(null);
            await release.Task;
            return Snapshot("B");
        };

        var first = fixture.Coordinator.RefreshNowAsync();
        await entered.Task;
        var second = fixture.Coordinator.RefreshNowAsync();

        await second;
        release.SetResult(null);
        await first;

        Assert.Single(fixture.Api.CatalogCalls);
        Assert.Equal(1, fixture.Api.MaxConcurrentCatalogRequests);
    }

    [Fact]
    public async Task MutationRefresh_WaitsForActiveRefreshAndPerformsFreshSecondGet()
    {
        var snapshotA = Snapshot("A");
        var snapshotB = Snapshot("B");
        var fixture = await ConnectionFixture.ConnectedToAsync(ServerA, snapshotA);
        fixture.Api.ResetCounters();
        var firstGetEntered = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstGet = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var catalogCall = 0;
        fixture.Api.CatalogHandler = async (_, _) =>
        {
            if (Interlocked.Increment(ref catalogCall) == 1)
            {
                firstGetEntered.TrySetResult(null);
                await releaseFirstGet.Task;
                return snapshotA;
            }

            return snapshotB;
        };

        var ordinaryRefresh = fixture.Coordinator.RefreshNowAsync();
        await firstGetEntered.Task;
        var mutationRefresh = fixture.Coordinator.RefreshAfterMutationAsync(ServerA);

        await Task.Delay(50);
        Assert.False(mutationRefresh.IsCompleted);
        Assert.Single(fixture.Api.CatalogCalls);
        Assert.Equal(1, fixture.Api.MaxConcurrentCatalogRequests);

        releaseFirstGet.SetResult(null);
        Assert.True(await mutationRefresh);
        await ordinaryRefresh;

        Assert.Equal(2, fixture.Api.CatalogCalls.Count);
        Assert.Same(snapshotB, fixture.Cache.Snapshot);
        Assert.Equal(1, fixture.Api.MaxConcurrentCatalogRequests);
    }

    [Fact]
    public async Task MutationRefresh_DoesNotConfirmAgainstSwitchedEndpoint()
    {
        var snapshotA = Snapshot("A");
        var snapshotB = Snapshot("B");
        var fixture = await ConnectionFixture.ConnectedToAsync(ServerA, snapshotA);
        fixture.Api.Snapshot = snapshotB;
        await fixture.Coordinator.SwitchServerAsync(ServerB, () => false);
        fixture.Api.ResetCounters();

        var confirmed = await fixture.Coordinator.RefreshAfterMutationAsync(ServerA);

        Assert.False(confirmed);
        Assert.Empty(fixture.Api.CatalogCalls);
        Assert.Equal(ServerB, fixture.Coordinator.Status.BaseUri);
        Assert.Same(snapshotB, fixture.Cache.Snapshot);
    }

    [Fact]
    public async Task RefreshNow_WhenUnconfigured_DoesNotCallApi()
    {
        var fixture = new ConnectionFixture();

        await fixture.Coordinator.RefreshNowAsync();

        Assert.Empty(fixture.Api.ReadyCalls);
        Assert.Empty(fixture.Api.CatalogCalls);
        Assert.Equal(ServerConnectionState.Unconfigured, fixture.Coordinator.Status.State);
    }

    [Fact]
    public async Task SuccessfulSwitchFromUnconfigured_WakesExistingRunLoop()
    {
        var fixture = new ConnectionFixture();
        using var cancellation = new CancellationTokenSource();
        var run = fixture.Coordinator.RunAsync(cancellation.Token);
        Assert.Equal(ServerConnectionState.Unconfigured, fixture.Coordinator.Status.State);
        Assert.False(run.IsCompleted);

        var switched = Snapshot("B");
        var refreshed = Snapshot("B refreshed");
        fixture.Api.Snapshot = switched;
        await fixture.Coordinator.SwitchServerAsync(ServerB, () => false);
        fixture.Api.ResetCounters();
        fixture.Api.CatalogHandler = (_, _) => Task.FromResult(refreshed);
        fixture.Clock.DelayHandler = (_, _) =>
        {
            if (fixture.Clock.Delays.Count == 2)
            {
                cancellation.Cancel();
            }

            return Task.CompletedTask;
        };

        await run;

        Assert.NotEmpty(fixture.Api.CatalogCalls);
        Assert.All(fixture.Api.CatalogCalls, uri => Assert.Equal(ServerB, uri));
        Assert.Equal(ServerB, fixture.Coordinator.Status.BaseUri);
        Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
        Assert.Same(refreshed, fixture.Cache.Snapshot);
    }

    [Fact]
    public async Task InvalidConfiguredBaseUrl_WaitsForLaterValidSwitch()
    {
        var fixture = new ConnectionFixture();
        fixture.Settings.Settings = new ClientSettings(
            new ClientServerSettings("not a server uri"));
        using var cancellation = new CancellationTokenSource();
        var run = fixture.Coordinator.RunAsync(cancellation.Token);

        Assert.Equal(ServerConnectionState.Unavailable, fixture.Coordinator.Status.State);
        Assert.False(run.IsCompleted);
        Assert.Empty(fixture.Api.ReadyCalls);
        Assert.Empty(fixture.Api.CatalogCalls);

        var switched = Snapshot("B");
        var refreshed = Snapshot("B refreshed");
        fixture.Api.Snapshot = switched;
        await fixture.Coordinator.SwitchServerAsync(ServerB, () => false);
        fixture.Api.ResetCounters();
        fixture.Api.CatalogHandler = (_, _) => Task.FromResult(refreshed);
        fixture.Clock.DelayHandler = (_, _) =>
        {
            if (fixture.Clock.Delays.Count == 2)
            {
                cancellation.Cancel();
            }

            return Task.CompletedTask;
        };

        await run;

        Assert.NotEmpty(fixture.Api.CatalogCalls);
        Assert.All(fixture.Api.CatalogCalls, uri => Assert.Equal(ServerB, uri));
        Assert.Equal(ServerB, fixture.Coordinator.Status.BaseUri);
        Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
        Assert.Same(refreshed, fixture.Cache.Snapshot);
    }

    [Fact]
    public async Task FailedServerSwitch_KeepsServerA()
    {
        var snapshotA = Snapshot("A");
        var fixture = await ConnectionFixture.ConnectedToAsync(ServerA, snapshotA);
        fixture.Api.ReadyHandler = (_, _) =>
            Task.FromException(new CatalogApiException("CATALOG_UNAVAILABLE"));

        await Assert.ThrowsAsync<CatalogApiException>(
            () => fixture.Coordinator.SwitchServerAsync(ServerB, () => false));

        Assert.Equal(ServerA, fixture.Coordinator.Status.BaseUri);
        Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
        Assert.Same(snapshotA, fixture.Cache.Snapshot);
    }

    [Fact]
    public async Task SettingsSaveFailure_KeepsServerA()
    {
        var snapshotA = Snapshot("A");
        var fixture = await ConnectionFixture.ConnectedToAsync(ServerA, snapshotA);
        var snapshotB = Snapshot("B");
        fixture.Api.Snapshot = snapshotB;
        fixture.Settings.SaveHandler = (_, _) =>
            Task.FromException(new IOException("test settings failure"));

        await Assert.ThrowsAsync<IOException>(
            () => fixture.Coordinator.SwitchServerAsync(ServerB, () => false));

        Assert.Equal(ServerA, fixture.Coordinator.Status.BaseUri);
        Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
        Assert.Same(snapshotA, fixture.Cache.Snapshot);
        Assert.Equal(2, fixture.Settings.SaveCount);
    }

    [Fact]
    public async Task UnsavedDraft_BlocksSwitchBeforeSave()
    {
        var snapshotA = Snapshot("A");
        var fixture = await ConnectionFixture.ConnectedToAsync(ServerA, snapshotA);
        fixture.Api.Snapshot = Snapshot("B");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Coordinator.SwitchServerAsync(ServerB, () => true));

        Assert.Equal("Unsaved Catalog edits block a Server switch.", exception.Message);
        Assert.Equal(1, fixture.Settings.SaveCount);
        Assert.Equal(ServerA, fixture.Coordinator.Status.BaseUri);
        Assert.Same(snapshotA, fixture.Cache.Snapshot);
    }

    [Fact]
    public async Task SuccessfulSwitch_PersistsExactlyOnce()
    {
        var fixture = await ConnectionFixture.ConnectedToAsync(ServerA, Snapshot("A"));
        var snapshotB = Snapshot("B");
        fixture.Api.Snapshot = snapshotB;
        fixture.Settings.SaveCount = 0;

        await fixture.Coordinator.SwitchServerAsync(ServerB, () => false);

        Assert.Equal(1, fixture.Settings.SaveCount);
        Assert.Equal(ServerB, fixture.Coordinator.Status.BaseUri);
        Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
        Assert.Same(snapshotB, fixture.Cache.Snapshot);
    }

    [Fact]
    public async Task CacheChanged_SeesServerBStateDuringSuccessfulSwitch()
    {
        var fixture = await ConnectionFixture.ConnectedToAsync(ServerA, Snapshot("A"));
        fixture.Api.Snapshot = Snapshot("B");
        ServerConnectionStatus? observedStatus = null;
        fixture.Cache.Changed += (_, _) => observedStatus = fixture.Coordinator.Status;

        await fixture.Coordinator.SwitchServerAsync(ServerB, () => false);

        Assert.NotNull(observedStatus);
        Assert.Equal(ServerB, observedStatus!.BaseUri);
        Assert.Equal(ServerConnectionState.Connected, observedStatus.State);
    }

    [Fact]
    public async Task StatusChangedHandler_SeesServerBSnapshotDuringSuccessfulSwitch()
    {
        var fixture = await ConnectionFixture.ConnectedToAsync(ServerA, Snapshot("A"));
        var snapshotB = Snapshot("B");
        fixture.Api.Snapshot = snapshotB;
        CatalogSnapshotDto? observedSnapshot = null;
        fixture.Coordinator.StatusChanged += (_, _) =>
            observedSnapshot = fixture.Cache.Snapshot;

        await fixture.Coordinator.SwitchServerAsync(ServerB, () => false);

        Assert.Same(snapshotB, observedSnapshot);
        Assert.Equal(ServerB, fixture.Coordinator.Status.BaseUri);
    }

    [Fact]
    public async Task InvalidSnapshotB_IsRejectedBeforeSettingsSave()
    {
        var snapshotA = Snapshot("A");
        var fixture = await ConnectionFixture.ConnectedToAsync(ServerA, snapshotA);
        fixture.Api.Snapshot = new CatalogSnapshotDto(null!, []);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.Coordinator.SwitchServerAsync(ServerB, () => false));

        Assert.Equal(1, fixture.Settings.SaveCount);
        Assert.Equal(ServerA, fixture.Coordinator.Status.BaseUri);
        Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
        Assert.Same(snapshotA, fixture.Cache.Snapshot);
    }

    [Fact]
    public async Task CallerCancellationBeforeCommit_DoesNotSwitch()
    {
        var snapshotA = Snapshot("A");
        var fixture = await ConnectionFixture.ConnectedToAsync(ServerA, snapshotA);
        fixture.Api.Snapshot = Snapshot("B");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Coordinator.SwitchServerAsync(
                ServerB,
                () => false,
                cancellation.Token));

        Assert.Equal(1, fixture.Settings.SaveCount);
        Assert.Equal(ServerA, fixture.Coordinator.Status.BaseUri);
        Assert.Same(snapshotA, fixture.Cache.Snapshot);
    }

    [Fact]
    public async Task CallerCancellationAfterSettingsCommit_StillCompletesMemoryCommit()
    {
        var fixture = await ConnectionFixture.ConnectedToAsync(ServerA, Snapshot("A"));
        var snapshotB = Snapshot("B");
        fixture.Api.Snapshot = snapshotB;
        using var cancellation = new CancellationTokenSource();
        fixture.Settings.SaveHandler = (_, _) =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };

        await fixture.Coordinator.SwitchServerAsync(
            ServerB,
            () => false,
            cancellation.Token);

        Assert.Equal(ServerB, fixture.Coordinator.Status.BaseUri);
        Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
        Assert.Same(snapshotB, fixture.Cache.Snapshot);
    }

    [Fact]
    public async Task AcceptedServerBFailure_ReconnectsOnlyToB()
    {
        var snapshotB = Snapshot("B");
        var fixture = await ConnectionFixture.ConnectedToAsync(ServerB, snapshotB);
        var refreshedB = Snapshot("B refreshed");
        var catalogCall = 0;
        fixture.Api.CatalogHandler = (_, _) =>
        {
            catalogCall++;
            return catalogCall == 2
                ? Task.FromException<CatalogSnapshotDto>(
                    new CatalogApiException("CATALOG_UNAVAILABLE"))
                : Task.FromResult(catalogCall == 1 ? snapshotB : refreshedB);
        };
        using var cancellation = new CancellationTokenSource();
        fixture.Clock.JitterUnit = 0.5;
        fixture.Clock.DelayHandler = (_, _) =>
        {
            if (fixture.Clock.Delays.Count == 3)
            {
                cancellation.Cancel();
            }

            return Task.CompletedTask;
        };

        await fixture.Coordinator.RunAsync(cancellation.Token);

        Assert.NotEmpty(fixture.Api.ReadyCalls);
        Assert.All(fixture.Api.ReadyCalls, uri => Assert.Equal(ServerB, uri));
        Assert.NotEmpty(fixture.Api.CatalogCalls);
        Assert.All(fixture.Api.CatalogCalls, uri => Assert.Equal(ServerB, uri));
        Assert.Equal(ServerB, fixture.Coordinator.Status.BaseUri);
        Assert.Same(refreshedB, fixture.Cache.Snapshot);
        Assert.Equal(
            [
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(30)
            ],
            fixture.Clock.Delays);
    }

    [Fact]
    public async Task RunCancellation_StopsDelayLoop()
    {
        var fixture = new ConnectionFixture();
        fixture.Settings.Settings = SettingsFor(ServerA);
        fixture.Api.Snapshot = Snapshot("A");
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Clock.DelayHandler = (_, _) =>
        {
            entered.SetResult(null);
            return release.Task;
        };

        var run = fixture.Coordinator.RunAsync(cancellation.Token);
        await entered.Task;
        cancellation.Cancel();
        release.SetResult(null);
        await run;

        Assert.Single(fixture.Api.CatalogCalls);
    }

    [Fact]
    public async Task Dispose_StopsRunLoop()
    {
        var fixture = new ConnectionFixture();
        fixture.Settings.Settings = SettingsFor(ServerA);
        fixture.Api.Snapshot = Snapshot("A");
        var entered = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Clock.DelayHandler = (_, cancellationToken) =>
        {
            entered.SetResult(null);
            cancellationToken.Register(() => release.TrySetResult(null));
            return release.Task;
        };

        _ = fixture.Coordinator.RunAsync(CancellationToken.None);
        await entered.Task;
        await fixture.Coordinator.DisposeAsync();

        Assert.Single(fixture.Api.CatalogCalls);
    }

    [Fact]
    public async Task DisposeDuringManualRefresh_CancelsAndAwaitsRefreshSafely()
    {
        var fixture = new ConnectionFixture();
        fixture.Settings.Settings = SettingsFor(ServerA);
        var initialSnapshot = Snapshot("A");
        var postShutdownSnapshot = Snapshot("must not commit");
        var catalogCall = 0;
        var refreshEntered = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var shutdownObserved = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefresh = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRefreshCleanup = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshCleanup = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Api.CatalogHandler = async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref catalogCall) == 1)
            {
                return initialSnapshot;
            }

            refreshEntered.TrySetResult(null);
            try
            {
                await releaseRefresh.Task.WaitAsync(cancellationToken);
                return postShutdownSnapshot;
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                shutdownObserved.TrySetResult(null);
                throw;
            }
            finally
            {
                await releaseRefreshCleanup.Task;
                refreshCleanup.TrySetResult(null);
            }
        };

        var periodicWaitEntered = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var periodicWait = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var periodicWaitCount = 0;
        fixture.Clock.DelayHandler = (delay, cancellationToken) =>
        {
            if (delay == TimeSpan.FromSeconds(30)
                && Interlocked.Increment(ref periodicWaitCount) == 1)
            {
                periodicWaitEntered.TrySetResult(null);
                return periodicWait.Task.WaitAsync(cancellationToken);
            }

            return Task.CompletedTask;
        };

        using var cancellation = new CancellationTokenSource();
        var run = fixture.Coordinator.RunAsync(cancellation.Token);
        await periodicWaitEntered.Task;
        var statusBeforeDispose = fixture.Coordinator.Status;
        var cacheBeforeDispose = fixture.Cache.Snapshot;
        var refresh = fixture.Coordinator.RefreshNowAsync(CancellationToken.None);
        await refreshEntered.Task;

        var dispose = fixture.Coordinator.DisposeAsync().AsTask();
        var disposeCompletedBeforeRefreshCleanup = false;
        var disposeObservation = dispose.ContinueWith(
            _ => disposeCompletedBeforeRefreshCleanup =
                !refreshCleanup.Task.IsCompleted,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try
        {
            await shutdownObserved.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.False(dispose.IsCompleted);
            Assert.False(refreshCleanup.Task.IsCompleted);

            releaseRefreshCleanup.TrySetResult(null);
            await Task.WhenAll(refresh, dispose, disposeObservation);
            await run;

            Assert.False(disposeCompletedBeforeRefreshCleanup);
            Assert.Equal(statusBeforeDispose, fixture.Coordinator.Status);
            Assert.Same(cacheBeforeDispose, fixture.Cache.Snapshot);
            Assert.False(periodicWait.Task.IsCompleted);
        }
        finally
        {
            releaseRefreshCleanup.TrySetResult(null);
            releaseRefresh.TrySetResult(null);
            await Task.WhenAll(refresh, dispose, disposeObservation);
            await run;
        }
    }

    [Fact]
    public async Task RefreshNowAfterDispose_IsRejected()
    {
        var fixture = await ConnectionFixture.ConnectedToAsync(
            ServerA,
            Snapshot("A"));
        var statusBeforeDispose = fixture.Coordinator.Status;
        var cacheBeforeDispose = fixture.Cache.Snapshot;
        var catalogCallsBeforeDispose = fixture.Api.CatalogCalls.Count;

        await fixture.Coordinator.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => fixture.Coordinator.RefreshNowAsync());

        Assert.Equal(statusBeforeDispose, fixture.Coordinator.Status);
        Assert.Same(cacheBeforeDispose, fixture.Cache.Snapshot);
        Assert.Equal(catalogCallsBeforeDispose, fixture.Api.CatalogCalls.Count);
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

    private sealed class ConnectionFixture
    {
        public ConnectionFixture()
        {
            Settings = new FakeClientSettingsStore();
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

        public static async Task<ConnectionFixture> ConnectedToAsync(
            Uri uri,
            CatalogSnapshotDto snapshot)
        {
            var fixture = new ConnectionFixture
            {
                Settings =
                {
                    Settings = ClientSettings.Empty
                }
            };
            fixture.Api.Snapshot = snapshot;

            await fixture.Coordinator.SwitchServerAsync(uri, () => false);

            return fixture;
        }
    }

    private sealed class FakeCatalogApi : ICatalogConnectionClient
    {
        private int currentCatalogRequests;

        public CatalogSnapshotDto Snapshot { get; set; } = new([], []);

        public Func<Uri, CancellationToken, Task>? ReadyHandler { get; set; }

        public Func<Uri, CancellationToken, Task<CatalogSnapshotDto>>?
            CatalogHandler { get; set; }

        public List<Uri> ReadyCalls { get; } = [];

        public List<Uri> CatalogCalls { get; } = [];

        public int MaxConcurrentCatalogRequests { get; private set; }

        public Task CheckReadyAsync(
            Uri baseUri,
            CancellationToken cancellationToken = default)
        {
            ReadyCalls.Add(baseUri);
            return ReadyHandler?.Invoke(baseUri, cancellationToken)
                ?? Task.CompletedTask;
        }

        public async Task<CatalogSnapshotDto> GetCatalogAsync(
            Uri baseUri,
            CancellationToken cancellationToken = default)
        {
            CatalogCalls.Add(baseUri);
            var concurrent = Interlocked.Increment(ref currentCatalogRequests);
            MaxConcurrentCatalogRequests = Math.Max(
                MaxConcurrentCatalogRequests,
                concurrent);
            try
            {
                return CatalogHandler is null
                    ? Snapshot
                    : await CatalogHandler(baseUri, cancellationToken)
                        .ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref currentCatalogRequests);
            }
        }

        public void ResetCounters()
        {
            ReadyCalls.Clear();
            CatalogCalls.Clear();
            MaxConcurrentCatalogRequests = 0;
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
                await SaveHandler(settings, cancellationToken).ConfigureAwait(false);
                return;
            }

            Settings = settings;
        }
    }

    private sealed class FakeConnectionClock : IClientConnectionClock
    {
        public DateTimeOffset UtcNow { get; set; } =
            DateTimeOffset.Parse("2026-08-31T00:00:00Z");

        public double JitterUnit { get; set; } = 0.5;

        public Queue<double> JitterUnits { get; } = [];

        public List<TimeSpan> Delays { get; } = [];

        public Func<TimeSpan, CancellationToken, Task>? DelayHandler { get; set; }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return DelayHandler?.Invoke(delay, cancellationToken)
                ?? Task.CompletedTask;
        }

        public double NextJitterUnit() =>
            JitterUnits.Count > 0 ? JitterUnits.Dequeue() : JitterUnit;
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
}
