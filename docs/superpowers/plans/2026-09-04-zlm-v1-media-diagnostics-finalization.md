# ZLM V1 Media Diagnostics Finalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

## Project governance

VideoMonitor project governance selects superpowers:executing-plans for plan execution.

Subagents are prohibited. All implementation, tests, review, and verification are performed by the current Luna agent. Do not invoke superpowers:subagent-driven-development, dispatch parallel agents, or delegate work.

**Goal:** Complete Safe Media Diagnostics on top of the existing MediaRuntimeRegistry, without creating a second runtime authority, exposing sensitive media data, or changing playback and ZLM behavior.

**Architecture:** Add one stateless Server projection over the existing runtime snapshot, a bounded signal that wakes the existing serialized reconciler, and a ticket-free formal ensure boundary shared by playback and diagnostics retry. Expose safe DTOs through three HTTP operations and consume them through a bounded WPF polling ViewModel embedded in the existing Media page.

**Tech Stack:** .NET 8, ASP.NET Core minimal APIs, System.Threading.Channels, the existing SQLite Catalog, CommunityToolkit.Mvvm, WPF/XAML, xUnit, and the existing HttpClient/CatalogOperationResult conventions.

**Spec:** docs/superpowers/specs/2026-09-04-zlm-v1-media-diagnostics-finalization-design.md

## Global Constraints

- MediaRuntimeRegistry remains the single runtime truth.
- Diagnostics never persists runtime state and never creates a runtime SQLite table.
- ActiveStreamCount is the number of Ready streams.
- Aggregate ViewerCount is the sum of all projected stream viewer counts.
- FaultCount is the number of Faulted streams.
- FreshnessSeconds defaults to 90; stale state is derived during projection.
- Idle and streams without an observation timestamp are not immediately stale.
- Refresh only signals the existing bounded recovery channel and never starts parallel reconciliation.
- Retry accepts the complete DeviceId + ChannelId + StreamType identity.
- Retry never constructs RTSP, reads Camera credentials, calls ZLMediaKit directly, creates a playback ticket, or returns a playback URL.
- Existing GET /api/v1/media/runtime remains registered and backward compatible.
- Diagnostics responses never expose originUrl, SourceUri, credential-bearing RTSP URLs, Camera Password, ZLM Secret, ProxyKey, signing key, PlaybackTicket, PlaybackUrl, connection strings, or admin URLs.
- The WPF formal path continues to use Server DTOs and ClientCatalogCache; it never receives Camera credentials.
- No ZLM parameter, VLC parameter, Camera setting, SQLite schema, or MediaMTX adapter is added or changed.
- No new .Result, .Wait(), or GetAwaiter().GetResult() is introduced.
- The current Debug warning baseline is 15 existing warnings: CA1416 = 8 and CS8602 = 7. No new warning is acceptable.
- Current test baseline is Core 613, Server 198, Solution 811; new tests may increase totals.
- Camera-dependent validation is outside this implementation plan; no FIELD PASS may be claimed from automated tests.
- Each implementation task ends with focused tests, related regressions, git diff --check, safety scans, one commit, and a push to release/zlm-v1-finalize; stop for Sol review before the next task.

## Current implementation facts

Re-read these files before coding each affected task:

- src/VideoMonitor.Core/Media/MediaRuntimeContracts.cs defines MediaRuntimeSnapshot, MediaStreamRuntimeInfo, MediaServerHealth, StreamRuntimeState, SourceObservation, ViewerCount, and StreamOwnership.
- src/VideoMonitor.Server/Media/IStreamManager.cs exposes EnsureStreamAsync, cleanup, and GetSnapshot().
- src/VideoMonitor.Server/Media/StreamManager.cs owns formal identity, source binding, readiness, ownership, and cleanup.
- src/VideoMonitor.Server/Media/MediaRuntimeRegistry.cs records runtime facts.
- src/VideoMonitor.Server/Media/MediaRuntimeEndpoints.cs maps GET /api/v1/media/runtime.
- src/VideoMonitor.Server/Media/MediaReconcilerHostedService.cs performs startup reconciliation and the serialized recovery loop; TriggerRecovery writes to a bounded channel.
- src/VideoMonitor.Server/Media/MediaEventProcessor.cs consumes bounded hook events.
- src/VideoMonitor.Server/Playback/PlaybackStreamService.cs currently combines formal ensure, readiness validation, ticket issuance, and playback URL construction.
- src/VideoMonitor.Wpf/Catalog/CatalogApiClient.cs owns central HttpClient conventions and the current playback ensure request style.
- src/VideoMonitor.Wpf/Configuration/ApplicationCatalogComposition.cs creates the central HTTP client, ClientCatalogCache, ServerConnectionCoordinator, ServerStatusViewModel, and formal playback provider.
- src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml is currently the Media Settings page and receives MediaSettingsViewModel through MainWindow.xaml.

## File Structure / Responsibility Map

### Create

- src/VideoMonitor.Core/Media/MediaDiagnosticsDtos.cs — safe snapshot and per-stream DTO records only.
- src/VideoMonitor.Server/Media/MediaDiagnosticsOptions.cs — validated MediaDiagnostics:FreshnessSeconds configuration.
- src/VideoMonitor.Server/Media/MediaDiagnosticsService.cs — stateless projection, counts, stale calculation, and ticket-free retry delegation.
- src/VideoMonitor.Server/Media/IMediaReconcileSignal.cs — bounded recovery signal boundary; it does not expose a channel.
- src/VideoMonitor.Server/Media/MediaDiagnosticsEndpoints.cs — diagnostics routes and safe HTTP mapping.
- src/VideoMonitor.Server/Playback/IFormalStreamEnsureService.cs — ticket-free formal ensure contract and safe result types.
- src/VideoMonitor.Server/Playback/FormalStreamEnsureService.cs — one implementation of Catalog validation, source resolution, media settings, StreamManager ensure, Ready verification, and safe failure mapping.
- src/VideoMonitor.Wpf/Catalog/MediaDiagnosticsApiClient.cs — safe HTTP client for diagnostics GET, refresh, and retry.
- src/VideoMonitor.Wpf/ViewModels/MediaDiagnosticsStreamRowViewModel.cs — one stable-ID keyed presentation row.
- src/VideoMonitor.Wpf/ViewModels/MediaDiagnosticsViewModel.cs — summary, non-overlapping polling, refresh, retry, cancellation, and safe unavailable state.
- src/VideoMonitor.Wpf/ViewModels/MediaPageViewModel.cs — minimal coordinator for existing Media Settings and diagnostics lifecycles.
- tests/VideoMonitor.Server.Tests/Media/MediaDiagnosticsServiceTests.cs — projection, counts, stale, security, and retry tests.
- tests/VideoMonitor.Server.Tests/Media/MediaDiagnosticsApiTests.cs — route, status, serialization, and signal tests.
- tests/VideoMonitor.Core.Tests/Catalog/MediaDiagnosticsApiClientTests.cs — client request and safe response tests.
- tests/VideoMonitor.Core.Tests/ViewModels/MediaDiagnosticsViewModelTests.cs — polling, command, stale, cancellation, and unavailable tests.
- tests/VideoMonitor.Core.Tests/Views/MediaDiagnosticsViewStructureTests.cs — minimal Media page binding assertions.

### Modify

- src/VideoMonitor.Server/Media/MediaReconcilerHostedService.cs — implement IMediaReconcileSignal without changing the serialized loop.
- src/VideoMonitor.Server/Playback/PlaybackStreamService.cs — consume the ticket-free boundary, retaining ticket and URL work here.
- src/VideoMonitor.Server/Program.cs — register options/services and map diagnostics routes.
- src/VideoMonitor.Wpf/Configuration/ApplicationCatalogComposition.cs — create the diagnostics client/ViewModel only in central mode.
- src/VideoMonitor.Wpf/ViewModels/MainViewModel.cs — expose the central Media page coordinator.
- src/VideoMonitor.Wpf/App.xaml.cs — construct the coordinator and await its async shutdown.
- src/VideoMonitor.Wpf/MainWindow.xaml — bind the existing Media page to the coordinator.
- src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml — retain settings and add the compact diagnostics surface.
- src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml.cs — activate/deactivate the page asynchronously.
- Existing focused tests only when a regression assertion requires the new boundary.

### Explicitly do not modify

- src/VideoMonitor.Server/Media/StreamManager.cs lifecycle logic.
- src/VideoMonitor.Server/Media/MediaRuntimeRegistry.cs storage semantics or stale timer behavior.
- src/VideoMonitor.Infrastructure/ZLMediaKit/* and ZLM configuration.
- src/VideoMonitor.Infrastructure/Persistence/SqliteDatabaseInitializer.cs schema or persistence.
- src/VideoMonitor.Server/Media/MediaEventProcessor.cs event semantics.
- src/VideoMonitor.Server/Playback/PlaybackAuthorizationEndpoints.cs ticket rules.
- src/VideoMonitor.Wpf/Playback/FormalPlaybackCoordinator.cs and VlcPlaybackService.cs.
- Camera configuration, SQLite data, MediaMTX, VLC options, and deployment artifacts.

---

### Task 1: Safe diagnostics contracts, projection, counts, and stale state

**Files:**

- Create: src/VideoMonitor.Core/Media/MediaDiagnosticsDtos.cs
- Create: src/VideoMonitor.Server/Media/MediaDiagnosticsOptions.cs
- Create: src/VideoMonitor.Server/Media/MediaDiagnosticsService.cs
- Test: tests/VideoMonitor.Server.Tests/Media/MediaDiagnosticsServiceTests.cs

**Interfaces:**

- Consumes: MediaRuntimeSnapshot, TimeProvider, and IOptions<MediaDiagnosticsOptions>.
- Produces: Task<MediaDiagnosticsSnapshotDto> GetAsync(CancellationToken) and deterministic MediaDiagnosticsSnapshotDto Project(MediaRuntimeSnapshot, DateTimeOffset).

- [ ] **Step 1: Write the failing tests.**

~~~csharp
[Fact]
public void DiagnosticsProjectionContainsSafeCounts()
{
    var result = CreateService().Project(
        Snapshot(
            Ready(viewers: 2),
            Ready(viewers: 1),
            Starting(viewers: 0),
            Faulted(viewers: 0),
            Idle(viewers: 4)),
        Now);

    Assert.Equal(2, result.ActiveStreamCount);
    Assert.Equal(7, result.ViewerCount);
    Assert.Equal(1, result.FaultCount);
}

[Fact]
public void OldReadyObservationBecomesStale()
{
    var result = CreateService().Project(
        Snapshot(Ready(observedAtUtc: Now.AddSeconds(-91))),
        Now);

    Assert.True(Assert.Single(result.Streams).IsStale);
}

[Fact]
public void IdleNeverBecomesStale()
{
    var result = CreateService().Project(
        Snapshot(Idle(observedAtUtc: Now.AddDays(-30))),
        Now);

    Assert.False(Assert.Single(result.Streams).IsStale);
}

[Fact]
public void StartingWithoutObservationIsNotImmediatelyStale()
{
    var result = CreateService().Project(
        Snapshot(Starting(observedAtUtc: null)),
        Now);

    Assert.False(Assert.Single(result.Streams).IsStale);
}

[Fact]
public void FutureObservationIsNotStale()
{
    var result = CreateService().Project(
        Snapshot(Ready(observedAtUtc: Now.AddMinutes(1))),
        Now);

    Assert.False(Assert.Single(result.Streams).IsStale);
}

[Fact]
public void SafeProjectionContainsNoSecretFields()
{
    var json = JsonSerializer.Serialize(
        CreateService().Project(Snapshot(Ready()), Now));

    Assert.DoesNotContain("originUrl", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("SourceUri", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("ZlmSecret", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("ProxyKey", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("PlaybackTicket", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("PlaybackUrl", json, StringComparison.OrdinalIgnoreCase);
}
~~~

- [ ] **Step 2: Run the focused filter and verify RED.**

Run:

~~~powershell
dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaDiagnosticsServiceTests"
~~~

Expected RED: compile/discovery failure because the diagnostics DTOs, options, service, and projection seam do not exist. Capture the exact failure before production code is written.

- [ ] **Step 3: Add the minimal contracts and projection.**

Use these exact public shapes:

~~~csharp
public sealed record MediaDiagnosticsSnapshotDto(
    MediaServerHealth ServerHealth,
    int ActiveStreamCount,
    int ViewerCount,
    int FaultCount,
    IReadOnlyList<MediaStreamDiagnosticsDto> Streams);

public sealed record MediaStreamDiagnosticsDto(
    Guid DeviceId,
    Guid ChannelId,
    StreamType StreamType,
    StreamRuntimeState RuntimeState,
    int ViewerCount,
    StreamOwnership Ownership,
    DateTimeOffset? StartedAtUtc,
    SourceObservation SourceObservation,
    DateTimeOffset? ObservedAtUtc,
    DateTimeOffset? LastSuccessUtc,
    string? SafeLastErrorCode,
    string? SafeLastErrorMessage,
    bool IsStale);

public sealed class MediaDiagnosticsOptions
{
    public const int DefaultFreshnessSeconds = 90;
    public int FreshnessSeconds { get; set; } = DefaultFreshnessSeconds;
}

public sealed class MediaDiagnosticsService
{
    public Task<MediaDiagnosticsSnapshotDto> GetAsync(
        CancellationToken cancellationToken = default);

    public MediaDiagnosticsSnapshotDto Project(
        MediaRuntimeSnapshot snapshot,
        DateTimeOffset nowUtc);
}
~~~

Project must flatten MediaStreamRuntimeInfo.Key, copy only safe fields, calculate counts exactly, and derive stale only for Ready, Starting, and Faulted observations older than the positive configured interval. Null and future observations are not stale; Idle is never stale. Inject TimeProvider so tests use a fixed clock and no real sleep. No timer is added and the registry is not mutated.

- [ ] **Step 4: Run GREEN for the projection tests.**

~~~powershell
dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaDiagnosticsServiceTests"
~~~

- [ ] **Step 5: Run related runtime regressions.**

~~~
dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaRuntimeApiTests|FullyQualifiedName~MediaRuntimeRegistryTests"
~~~

Expected: all focused tests pass and the existing runtime endpoint remains green.

- [ ] **Step 6: Scan the projection scope and safety invariants.**

~~~powershell
git diff --check
git diff --name-only
rg -n "\.Result\b|\.Wait\(|GetAwaiter\(\)\.GetResult\(\)" src
rg -n "originUrl|SourceUri|ProxyKey|ZlmSecret|PlaybackTicket|PlaybackUrl|Data Source=|rtsp://" src\VideoMonitor.Core\Media\MediaDiagnosticsDtos.cs src\VideoMonitor.Server\Media\MediaDiagnosticsOptions.cs src\VideoMonitor.Server\Media\MediaDiagnosticsService.cs
~~~

Manually confirm matches are only field/type names and no values or raw exception text exist.

- [ ] **Step 7: Commit and push the isolated task.**

~~~powershell
git add src/VideoMonitor.Core/Media/MediaDiagnosticsDtos.cs src/VideoMonitor.Server/Media/MediaDiagnosticsOptions.cs src/VideoMonitor.Server/Media/MediaDiagnosticsService.cs tests/VideoMonitor.Server.Tests/Media/MediaDiagnosticsServiceTests.cs
git commit -m "feat: add safe media diagnostics projection"
git push origin release/zlm-v1-finalize
~~~

Stop for Sol review.

### Task 2: Bounded reconcile signal and refresh operation

**Files:**

- Create: src/VideoMonitor.Server/Media/IMediaReconcileSignal.cs
- Modify: src/VideoMonitor.Server/Media/MediaReconcilerHostedService.cs
- Test: tests/VideoMonitor.Server.Tests/Media/MediaReconcileSignalTests.cs

**Interfaces:**

~~~csharp
public enum ReconcileSignalResult
{
    Accepted,
    Unavailable
}

public interface IMediaReconcileSignal
{
    ReconcileSignalResult TryRequestRecovery();
}
~~~

- [ ] **Step 1: Write the failing signal tests.**

~~~csharp
[Fact]
public void RefreshOnlySignalsReconciler()
{
    var reconciler = CreateReconciler();

    var result = ((IMediaReconcileSignal)reconciler).TryRequestRecovery();

    Assert.Equal(ReconcileSignalResult.Accepted, result);
    Assert.Equal(0, reconciler.ReconcileCallCount);
}

[Fact]
public void RepeatedRefreshIsCoalesced()
{
    var reconciler = CreateReconciler();

    Assert.Equal(ReconcileSignalResult.Accepted, Signal(reconciler));
    Assert.Equal(ReconcileSignalResult.Accepted, Signal(reconciler));
    Assert.Equal(1, reconciler.PendingRecoveryCount);
}

[Fact]
public async Task RepeatedRefreshDoesNotCreateParallelReconcile()
{
    var reconciler = CreateBlockingReconciler();

    Signal(reconciler);
    Signal(reconciler);
    await reconciler.WaitForOneReconcileAsync();

    Assert.Equal(1, reconciler.MaximumConcurrentReconcileCalls);
}
~~~

- [ ] **Step 2: Run RED.**

~~~powershell
dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaReconcileSignalTests"
~~~

Expected RED: missing signal contract or missing public signal result. The test must not create a separate reconciliation task.

- [ ] **Step 3: Expose the existing bounded signal without changing the loop.**

~~~csharp
public ReconcileSignalResult TryRequestRecovery()
{
    return recoverySignals.Writer.TryWrite(true)
        ? ReconcileSignalResult.Accepted
        : ReconcileSignalResult.Unavailable;
}

public void TriggerRecovery() => TryRequestRecovery();
~~~

Preserve channel capacity 1, single-reader recovery, Task.WhenAny, startup reconciliation, and backoff. Do not expose the channel.

- [ ] **Step 4: Run GREEN and hosted-service regressions.**

~~~powershell
dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaReconcileSignalTests"
~~~

- [ ] **Step 5: Run related hosted-service and hook regressions.**

~~~
dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaReconcilerHostedServiceTests|FullyQualifiedName~MediaHookTests"
~~~

- [ ] **Step 6: Scan the serialized-path invariants.**

~~~powershell
git diff --check
git diff --name-only
rg -n "Task\.Run|ReconcileAsync\(|CreateUnbounded|\.Result\b|\.Wait\(|GetAwaiter\(\)\.GetResult\(\)" src\VideoMonitor.Server\Media
~~~

Confirm the only reconciliation call remains in the serialized hosted-service path.

- [ ] **Step 7: Commit and push the isolated task.**

~~~powershell
git add src/VideoMonitor.Server/Media/IMediaReconcileSignal.cs src/VideoMonitor.Server/Media/MediaReconcilerHostedService.cs tests/VideoMonitor.Server.Tests/Media/MediaReconcileSignalTests.cs
git commit -m "feat: add coalesced media refresh signal"
git push origin release/zlm-v1-finalize
~~~

Stop for Sol review.

### Task 3: Ticket-free formal ensure boundary and faulted retry

**Files:**

Exactly 8 files are in the Task 3 boundary:

- Create: src/VideoMonitor.Server/Playback/IFormalStreamEnsureService.cs
- Create: src/VideoMonitor.Server/Playback/FormalStreamEnsureService.cs
- Modify: src/VideoMonitor.Server/Playback/PlaybackStreamService.cs
- Modify: src/VideoMonitor.Server/Media/MediaDiagnosticsService.cs
- Modify: src/VideoMonitor.Server/Program.cs
- Create test: tests/VideoMonitor.Server.Tests/Playback/FormalStreamEnsureServiceTests.cs
- Modify regression test: tests/VideoMonitor.Server.Tests/Playback/PlaybackAuthorizationTests.cs
- Modify regression test: tests/VideoMonitor.Server.Tests/Media/MediaDiagnosticsServiceTests.cs

**Interfaces:**

~~~csharp
public sealed record FormalStreamEnsureRequest(
    Guid DeviceId,
    Guid ChannelId,
    StreamType StreamType);

public sealed record FormalStreamEnsureResult(
    Guid DeviceId,
    Guid ChannelId,
    StreamType StreamType,
    PlaybackMediaIdentity MediaIdentity,
    StreamRuntimeState RuntimeState);

public interface IFormalStreamEnsureService
{
    Task<CatalogOperationResult<FormalStreamEnsureResult>> EnsureAsync(
        FormalStreamEnsureRequest request,
        CancellationToken cancellationToken = default);
}
~~~

`PlaybackMediaIdentity` is the existing internal value containing only `Vhost`, `App`, and `Stream`. `FormalStreamEnsureService` must populate it directly from the ensured stream (`ensured.Stream.Vhost`, `ensured.Stream.App`, and `ensured.Stream.Stream`); `FormalStreamEnsureResult` must not contain `PlaybackUrl`, `PlaybackTicket`, or ticket expiry data.

**DI registration:** Task 3 must add the formal ensure registration in `Program.cs` before the existing playback registration:

~~~csharp
builder.Services.AddSingleton<IFormalStreamEnsureService, FormalStreamEnsureService>();
builder.Services.AddSingleton<IPlaybackStreamService, PlaybackStreamService>();
~~~

The exact existing registration syntax may be preserved, but the ordering and resolvable dependency graph are mandatory. Task 4 must not defer, repeat, or redesign this registration.

**Retry responsibility:** `MediaDiagnosticsService` owns the diagnostics retry decision. It must find the exact `MediaStreamKey` from `IStreamManager.GetSnapshot()`; a missing identity returns a safe not-found result and does not call the formal ensure boundary. An existing non-`Faulted` stream returns `MEDIA_STREAM_NOT_FAULTED` and does not call the boundary. Only an exact currently `Faulted` key may be converted to `FormalStreamEnsureRequest` and passed to `IFormalStreamEnsureService.EnsureAsync`. This service must not call ZLMediaKit, read Camera credentials, construct an RTSP URI, issue a playback ticket, or build or return a playback URL.

The formal ensure boundary is deliberately ticket-free. Its input and result remain the `FormalStreamEnsureRequest` and `FormalStreamEnsureResult` records above: Catalog validation, channel/source/media-settings validation, `MediaStreamRequest`, `IStreamManager.EnsureStreamAsync`, and `Ready` verification belong inside the boundary; ticket issuance and playback URL construction do not. `PlaybackStreamService` retains those playback responsibilities after a successful ensure.

- [ ] **Step 1: Add the existing-playback regression before extraction.**

~~~csharp
[Fact]
public async Task FormalPlaybackStillEnsuresThenIssuesTicketAndBuildsUrl()
{
    var fixture = CreatePlaybackFixture();

    var result = await fixture.Service.EnsureAsync(fixture.Request);

    Assert.True(result.IsSuccess);
    Assert.Equal(1, fixture.StreamManager.EnsureCalls);
    Assert.Equal(1, fixture.TicketIssuer.IssueCalls);
    Assert.Equal(1, fixture.UrlBuilder.BuildCalls);
    Assert.Equal(fixture.TicketIssuer.LastIssuedIdentity.Stream, result.Value?.StreamId);
    Assert.NotNull(result.Value?.PlaybackUrl);
}
~~~

The playback test double must record the `PlaybackMediaIdentity` passed to `IPlaybackTicketIssuer`; the assertion above verifies that the response `StreamId` is the same `MediaIdentity.Stream` used for ticket issuance, rather than a separately reconstructed value.

- [ ] **Step 2: Add failing ticket-free retry tests.**

~~~csharp
[Fact]
public async Task FaultedRetryUsesFormalEnsureBoundary()
{
    var fixture = CreateDiagnosticsRetryFixture(StreamRuntimeState.Faulted);

    var result = await fixture.Diagnostics.RetryFaultedAsync(fixture.Key);

    Assert.True(result.IsSuccess);
    Assert.Equal(fixture.Key, fixture.EnsureService.LastRequestKey);
}

[Fact]
public async Task FaultedRetryDoesNotIssuePlaybackTicket()
{
    var fixture = CreateDiagnosticsRetryFixture(StreamRuntimeState.Faulted);

    await fixture.Diagnostics.RetryFaultedAsync(fixture.Key);

    Assert.Equal(0, fixture.TicketIssuer.IssueCalls);
}

[Fact]
public async Task NonFaultedRetryIsRejected()
{
    var fixture = CreateDiagnosticsRetryFixture(StreamRuntimeState.Ready);

    var result = await fixture.Diagnostics.RetryFaultedAsync(fixture.Key);

    Assert.False(result.IsSuccess);
    Assert.Equal("MEDIA_STREAM_NOT_FAULTED", result.Error?.Code);
}

[Fact]
public async Task FaultedRetryDoesNotReturnPlaybackUrl()
{
    var fixture = CreateDiagnosticsRetryFixture(StreamRuntimeState.Faulted);

    var result = await fixture.Diagnostics.RetryFaultedAsync(fixture.Key);
    var serialized = JsonSerializer.Serialize(result);

    Assert.DoesNotContain("PlaybackUrl", serialized,
        StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("PlaybackTicket", serialized,
        StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task UnknownRuntimeIdentityIsRejected()
{
    var fixture = CreateDiagnosticsRetryFixture(StreamRuntimeState.Faulted);

    var result = await fixture.Diagnostics.RetryFaultedAsync(
        new MediaStreamKey(Guid.NewGuid(), Guid.NewGuid(), StreamType.Main));

    Assert.False(result.IsSuccess);
    Assert.Equal(0, fixture.EnsureService.CallCount);
}

[Fact]
public async Task RetryDoesNotCallZlmDirectly()
{
    var fixture = CreateDiagnosticsRetryFixture(StreamRuntimeState.Faulted);

    await fixture.Diagnostics.RetryFaultedAsync(fixture.Key);

    Assert.Equal(0, fixture.ZlmClient.CallCount);
}
~~~

The retry tests belong in `MediaDiagnosticsServiceTests`, while `FormalPlaybackStillEnsuresThenIssuesTicketAndBuildsUrl` remains the `PlaybackAuthorizationTests` regression proving that the playback service still performs ticket issuance and URL construction after the shared ticket-free ensure succeeds.

Task 3 must also preserve the existing `PlaybackFailureLogsSafeDiagnosticsWithoutSensitiveValues` regression. If the safe logging responsibility moves with the extracted boundary, move only the test ownership as needed; keep assertions for the safe failure code, safe stage/operation context, exception type only, and absence of `Exception.Message`, RTSP URI, password, and secret.

The Task 3 verification must confirm the production DI graph before the task is complete: `Program.cs` registers `IFormalStreamEnsureService` to `FormalStreamEnsureService` as a singleton before the existing `IPlaybackStreamService` registration. Add a focused composition/registration test when the existing Server test structure supports it; otherwise verify that resolving `IPlaybackStreamService` resolves the complete formal-ensure dependency chain during the Server regression. This registration belongs to Task 3 and must not be deferred to Task 4.

The diagnostics retry result is a safe result type with no URL or ticket properties. Serialize it in the test and assert that those names are absent.

- [ ] **Step 3: Run RED.**

~~~powershell
dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~FormalStreamEnsureServiceTests|FullyQualifiedName~MediaDiagnosticsServiceTests|FullyQualifiedName~PlaybackAuthorizationTests"
~~~

Expected RED: the ticket-free service and diagnostics retry seam do not exist. Preserve the existing playback regression as the pre-extraction behavior check.

- [ ] **Step 4: Extract the minimal shared boundary.**

Move only this sequence into FormalStreamEnsureService:

~~~text
validate stable IDs and StreamType
→ read Catalog device and channel
→ validate relation and enabled state
→ build MediaStreamKey
→ resolve source through ICameraSourceResolver
→ read media runtime settings
→ build formal MediaStreamRequest
→ call IStreamManager.EnsureStreamAsync
→ verify Ready from IStreamManager.GetSnapshot()
→ return safe FormalStreamEnsureResult
~~~

PlaybackStreamService then uses `FormalStreamEnsureResult.MediaIdentity` (without re-reading media settings or reconstructing identity) to issue the ticket and build the playback URL. Its returned `StreamId` must equal `MediaIdentity.Stream`. Diagnostics retry checks the exact key's current Faulted state, may receive the internal result, and returns only accepted/safe-error data; it never serializes that result, calls ZLM, or reads credentials itself. Preserve existing playback status and error mappings.

- [ ] **Step 5: Run GREEN and playback regressions.**

~~~powershell
dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~FormalStreamEnsureServiceTests|FullyQualifiedName~MediaDiagnosticsServiceTests|FullyQualifiedName~PlaybackAuthorizationTests"
dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~StreamManagerTests|FullyQualifiedName~SourceBindingVerifierTests"
~~~

- [ ] **Step 6: Scan security and the ticket-free boundary.**

~~~powershell
git diff --check
rg -n "PlaybackTicket|PlaybackUrl|TicketExpiresUtc|SourceUri|originUrl|Password|ZlmSecret|ProxyKey|\.Result\b|\.Wait\(|GetAwaiter\(\)\.GetResult\(\)" src\VideoMonitor.Server\Playback
~~~

Review every match; no diagnostics result or log may carry sensitive values.

- [ ] **Step 7: Commit and push the isolated task.**

~~~powershell
git add src/VideoMonitor.Server/Playback/IFormalStreamEnsureService.cs src/VideoMonitor.Server/Playback/FormalStreamEnsureService.cs src/VideoMonitor.Server/Playback/PlaybackStreamService.cs src/VideoMonitor.Server/Media/MediaDiagnosticsService.cs tests/VideoMonitor.Server.Tests/Playback/FormalStreamEnsureServiceTests.cs tests/VideoMonitor.Server.Tests/Playback/PlaybackAuthorizationTests.cs tests/VideoMonitor.Server.Tests/Media/MediaDiagnosticsServiceTests.cs
git commit -m "refactor: extract ticket-free formal ensure boundary"
git push origin release/zlm-v1-finalize
~~~

Stop for Sol review.

### Task 4: Diagnostics HTTP API

**Files:**

- Create: src/VideoMonitor.Server/Media/MediaDiagnosticsEndpoints.cs
- Modify: src/VideoMonitor.Server/Program.cs
- Modify: src/VideoMonitor.Server/Media/MediaDiagnosticsService.cs only if a minimal endpoint-facing interface adjustment is proven necessary after Task 3; Task 4 must not add or duplicate retry business logic.
- Test: tests/VideoMonitor.Server.Tests/Media/MediaDiagnosticsApiTests.cs

**Interfaces and routes:**

~~~text
GET  /api/v1/media/runtime
GET  /api/v1/media/diagnostics
POST /api/v1/media/diagnostics/refresh
POST /api/v1/media/diagnostics/streams/{deviceId}/{channelId}/{streamType}/retry
~~~

- [ ] **Step 1: Write failing endpoint tests.**

~~~csharp
[Fact]
public async Task DiagnosticsApiReturnsSafeDto()
{
    var response = await Client.GetAsync("/api/v1/media/diagnostics");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var json = await response.Content.ReadAsStringAsync();
    Assert.Contains("ActiveStreamCount", json, StringComparison.Ordinal);
    Assert.DoesNotContain("originUrl", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("PlaybackTicket", json, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task RuntimeEndpointRemainsBackwardCompatible()
{
    var response = await Client.GetAsync("/api/v1/media/runtime");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}

[Fact]
public async Task DiagnosticsApiDoesNotExposeStopAll()
{
    var response = await Client.PostAsync(
        "/api/v1/media/diagnostics/stop-all", content: null);

    Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
}

[Fact]
public async Task DiagnosticsApiDoesNotExposePlaybackTicket()
{
    var response = await Client.GetAsync("/api/v1/media/diagnostics");
    var json = await response.Content.ReadAsStringAsync();

    Assert.DoesNotContain("PlaybackTicket", json,
        StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("PlaybackUrl", json,
        StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task DiagnosticsApiRejectsNonFaultedRetry()
{
    var response = await Client.PostAsync(
        $"/api/v1/media/diagnostics/streams/{ReadyDeviceId}/{ReadyChannelId}/main/retry",
        content: null);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
}

[Fact]
public async Task DiagnosticsApiReturnsUnavailableSafely()
{
    var fixture = CreateUnavailableServerFixture();

    var response = await fixture.Client.GetAsync("/api/v1/media/diagnostics");
    var body = await response.Content.ReadAsStringAsync();

    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    Assert.DoesNotContain("Exception", body,
        StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("SourceUri", body,
        StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task RefreshReturns202()
{
    var response = await Client.PostAsync(
        "/api/v1/media/diagnostics/refresh", content: null);

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
}
~~~

- [ ] **Step 2: Run RED.**

~~~powershell
dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaDiagnosticsApiTests"
~~~

Expected RED: missing diagnostics route registration and missing safe HTTP mappings.

- [ ] **Step 3: Implement route mapping.**

Map readiness failure to 503 MEDIA_DIAGNOSTICS_UNAVAILABLE. Call only IMediaReconcileSignal.TryRequestRecovery() for refresh and return 202 for an accepted/coalesced signal. Parse GUIDs and enum values without name-based identity. The Task 3 `MediaDiagnosticsService` owns the exact-key/current-state retry decision and formal-ensure call; this task only maps its result to 202 for accepted retry, 404, 409 `MEDIA_STREAM_NOT_FAULTED`, or safe 503. The retry response must never serialize `FormalStreamEnsureResult`, tickets, URLs, source/origin, proxy, secrets, or raw exceptions.

Register only `MediaDiagnosticsOptions`, `MediaDiagnosticsService`, `IMediaReconcileSignal`, and the endpoint dependencies in Program.cs. The `IFormalStreamEnsureService` → `FormalStreamEnsureService` registration is owned by Task 3 and must not be repeated or redesigned here. Map the new routes beside existing runtime, hook, playback, and test-stream endpoints. Do not remove GET /api/v1/media/runtime.

- [ ] **Step 4: Run GREEN for the diagnostics API tests.**

~~~powershell
dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaDiagnosticsApiTests"
~~~

- [ ] **Step 5: Run related API, hook, and playback regressions.**

~~~powershell
dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaRuntimeApiTests|FullyQualifiedName~PlaybackAuthorizationTests|FullyQualifiedName~MediaHookTests"
~~~

- [ ] **Step 6: Scan endpoint scope and serialization safety.**

~~~
git diff --check
rg -n "Map(Get|Post)|/api/v1/media/(runtime|diagnostics)|StopAll|stop-all" src\VideoMonitor.Server
rg -n "originUrl|SourceUri|ProxyKey|ZlmSecret|SigningKey|PlaybackTicket|PlaybackUrl|Data Source=|rtsp://|Exception\.Message" src\VideoMonitor.Server\Media\MediaDiagnosticsEndpoints.cs src\VideoMonitor.Server\Media\MediaDiagnosticsService.cs
~~~

- [ ] **Step 7: Commit and push the isolated task.**

~~~powershell
git add src/VideoMonitor.Server/Media/MediaDiagnosticsEndpoints.cs src/VideoMonitor.Server/Media/MediaDiagnosticsService.cs src/VideoMonitor.Server/Program.cs tests/VideoMonitor.Server.Tests/Media/MediaDiagnosticsApiTests.cs
git commit -m "feat: expose safe media diagnostics endpoints"
git push origin release/zlm-v1-finalize
~~~

Stop for Sol review.

### Task 5: WPF diagnostics API client

**Files:**

- Create: src/VideoMonitor.Wpf/Catalog/MediaDiagnosticsApiClient.cs
- Test: tests/VideoMonitor.Core.Tests/Catalog/MediaDiagnosticsApiClientTests.cs
- Modify: src/VideoMonitor.Wpf/Catalog/CatalogApiClient.cs only if a shared safe response parser is necessary.

**Interfaces:**

~~~csharp
public interface IMediaDiagnosticsApiClient
{
    Task<MediaDiagnosticsSnapshotDto> GetDiagnosticsAsync(
        Uri baseUri,
        CancellationToken cancellationToken = default);

    Task RequestRefreshAsync(
        Uri baseUri,
        CancellationToken cancellationToken = default);

    Task RetryFaultedAsync(
        Uri baseUri,
        Guid deviceId,
        Guid channelId,
        StreamType streamType,
        CancellationToken cancellationToken = default);
}
~~~

- [ ] **Step 1: Write failing client tests.**

~~~csharp
[Fact]
public async Task MediaDiagnosticsApiClientReadsSafeDto()
{
    var handler = new RecordingHandler(JsonSnapshot());
    var client = new MediaDiagnosticsApiClient(new HttpClient(handler));

    var result = await client.GetDiagnosticsAsync(BaseUri);

    Assert.Equal(1, result.ActiveStreamCount);
    Assert.Equal("GET", handler.LastMethod);
    Assert.Equal("/api/v1/media/diagnostics", handler.LastPath);
}

[Fact]
public async Task RefreshUsesCorrectEndpoint()
{
    var handler = new RecordingHandler(HttpStatusCode.Accepted);
    var client = new MediaDiagnosticsApiClient(new HttpClient(handler));

    await client.RequestRefreshAsync(BaseUri);

    Assert.Equal("POST", handler.LastMethod);
    Assert.Equal("/api/v1/media/diagnostics/refresh", handler.LastPath);
}

[Fact]
public async Task RetryUsesCompleteStableIdentity()
{
    var handler = new RecordingHandler(HttpStatusCode.Accepted);
    var client = new MediaDiagnosticsApiClient(new HttpClient(handler));

    await client.RetryFaultedAsync(BaseUri, DeviceId, ChannelId, StreamType.Main);

    Assert.Contains(DeviceId.ToString(), handler.LastPath,
        StringComparison.OrdinalIgnoreCase);
    Assert.Contains(ChannelId.ToString(), handler.LastPath,
        StringComparison.OrdinalIgnoreCase);
    Assert.Contains("main", handler.LastPath,
        StringComparison.OrdinalIgnoreCase);
}

[Fact]
public async Task ClientDoesNotExposeRawErrorBody()
{
    var handler = new RecordingHandler(
        HttpStatusCode.InternalServerError,
        "Password=secret; rtsp://user:secret@camera");
    var client = new MediaDiagnosticsApiClient(new HttpClient(handler));

    var exception = await Assert.ThrowsAsync<CatalogApiException>(
        () => client.GetDiagnosticsAsync(BaseUri));

    Assert.DoesNotContain("secret", exception.Message,
        StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("rtsp://", exception.Message,
        StringComparison.OrdinalIgnoreCase);
}
~~~

- [ ] **Step 2: Run RED.**

~~~powershell
dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~MediaDiagnosticsApiClientTests"
~~~

Expected RED: missing client interface/class and route methods.

- [ ] **Step 3: Implement safe HTTP behavior.**

Use ResponseHeadersRead, caller cancellation, and configured absolute HTTP/HTTPS base URI. GET deserializes only the diagnostics DTO; refresh accepts the expected 202/safe failure status; retry sends only the three identity values. Error parsing preserves safe code/status and discards raw body text from exception messages.

- [ ] **Step 4: Run GREEN for the diagnostics client tests.**

~~~powershell
dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~MediaDiagnosticsApiClientTests"
~~~

- [ ] **Step 5: Run related HTTP client regressions.**

~~~powershell
dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~RemotePlaybackSourceProviderTests|FullyQualifiedName~MediaSettingsApiClientTests"
~~~

- [ ] **Step 6: Scan client safety and async behavior.**

~~~
git diff --check
rg -n "Password|password_ciphertext|ZlmSecret|secret|originUrl|SourceUri|rtsp://|ProxyKey|PlaybackTicket|PlaybackUrl|SigningKey|Data Source=" src\VideoMonitor.Wpf\Catalog\MediaDiagnosticsApiClient.cs tests\VideoMonitor.Core.Tests\Catalog\MediaDiagnosticsApiClientTests.cs
~~~

Review that test-only secret literals never enter production messages or logs.

- [ ] **Step 7: Commit and push the isolated task.**

~~~powershell
git add src/VideoMonitor.Wpf/Catalog/MediaDiagnosticsApiClient.cs tests/VideoMonitor.Core.Tests/Catalog/MediaDiagnosticsApiClientTests.cs
git commit -m "feat: add media diagnostics api client"
git push origin release/zlm-v1-finalize
~~~

Stop for Sol review.

### Task 6: WPF ViewModel polling, Media page surface, and composition

**Files:**

- Create: src/VideoMonitor.Wpf/ViewModels/MediaDiagnosticsStreamRowViewModel.cs
- Create: src/VideoMonitor.Wpf/ViewModels/MediaDiagnosticsViewModel.cs
- Create: src/VideoMonitor.Wpf/ViewModels/MediaPageViewModel.cs
- Modify: src/VideoMonitor.Wpf/Configuration/ApplicationCatalogComposition.cs
- Modify: src/VideoMonitor.Wpf/ViewModels/MainViewModel.cs
- Modify: src/VideoMonitor.Wpf/App.xaml.cs
- Modify: src/VideoMonitor.Wpf/MainWindow.xaml
- Modify: src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml
- Modify: src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml.cs
- Test: tests/VideoMonitor.Core.Tests/ViewModels/MediaDiagnosticsViewModelTests.cs
- Test: tests/VideoMonitor.Core.Tests/Views/MediaDiagnosticsViewStructureTests.cs
- Modify: tests/VideoMonitor.Core.Tests/Composition/ApplicationCatalogCompositionTests.cs only for central/local composition assertions.

**Interfaces:**

~~~csharp
public sealed class MediaDiagnosticsViewModel : ObservableObject, IAsyncDisposable
{
    public MediaServerHealth ServerHealth { get; }
    public int ActiveStreamCount { get; }
    public int ViewerCount { get; }
    public int FaultCount { get; }
    public bool IsBusy { get; }
    public bool IsUnavailable { get; }
    public IReadOnlyList<MediaDiagnosticsStreamRowViewModel> Streams { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand<MediaDiagnosticsStreamRowViewModel> RetryCommand { get; }
    public Task StartAsync(CancellationToken cancellationToken = default);
    public Task StopAsync(CancellationToken cancellationToken = default);
    public ValueTask DisposeAsync();
}

public sealed class MediaPageViewModel : IAsyncDisposable
{
    public MediaSettingsViewModel Settings { get; }
    public MediaDiagnosticsViewModel Diagnostics { get; }
    public Task ActivateAsync(CancellationToken cancellationToken = default);
    public Task DeactivateAsync(CancellationToken cancellationToken = default);
    public ValueTask DisposeAsync();
}
~~~

- [ ] **Step 1: Write failing ViewModel tests.**

Use a controllable delay/poll trigger; do not sleep five seconds.

~~~csharp
[Fact]
public async Task MediaDiagnosticsViewModelInitialRefresh()
{
    var fixture = CreateFixture();

    await fixture.ViewModel.StartAsync();

    Assert.Equal(1, fixture.Api.GetCalls);
    Assert.Equal(2, fixture.ViewModel.ActiveStreamCount);
}

[Fact]
public async Task MediaDiagnosticsViewModelPollDoesNotOverlap()
{
    var fixture = CreateBlockingFixture();
    await fixture.ViewModel.StartAsync();

    fixture.Clock.ReleaseFirstPoll();
    fixture.Clock.TriggerNextPoll();
    fixture.Clock.TriggerNextPoll();

    Assert.Equal(1, fixture.Api.MaximumConcurrentGetCalls);
}

[Fact]
public async Task MediaDiagnosticsViewModelShowsStale()
{
    var fixture = CreateFixture(
        snapshot: SnapshotWithStaleStream());
    await fixture.ViewModel.StartAsync();

    Assert.True(Assert.Single(fixture.ViewModel.Streams).IsStale);
}

[Fact]
public async Task RetryOnlyEnabledForFaulted()
{
    var fixture = CreateFixture(
        snapshot: SnapshotWithReadyAndFaultedStreams());
    await fixture.ViewModel.StartAsync();

    Assert.False(fixture.ViewModel.RetryCommand.CanExecute(fixture.ReadyRow));
    Assert.True(fixture.ViewModel.RetryCommand.CanExecute(fixture.FaultedRow));
}

[Fact]
public async Task DisposeOrStopCancelsPolling()
{
    var fixture = CreateFixture();
    await fixture.ViewModel.StartAsync();

    await fixture.ViewModel.StopAsync();

    Assert.True(fixture.Clock.PollCancellationObserved);
}

[Fact]
public async Task ServerUnavailableDoesNotCreateMessageLoop()
{
    var fixture = CreateUnavailableFixture();

    await fixture.ViewModel.StartAsync();

    Assert.True(fixture.ViewModel.IsUnavailable);
    Assert.Equal(1, fixture.Api.GetCalls);
    Assert.Equal(0, fixture.MessageBoxCalls);
}
~~~

- [ ] **Step 2: Add failing UI and composition tests.**

~~~csharp
[Fact]
public void MediaViewContainsDiagnosticsSummaryAndRefresh()
{
    var xaml = ReadProjectFile("Views/Pages/MediaView.xaml");

    Assert.Contains("ActiveStreamCount", xaml, StringComparison.Ordinal);
    Assert.Contains("ViewerCount", xaml, StringComparison.Ordinal);
    Assert.Contains("FaultCount", xaml, StringComparison.Ordinal);
    Assert.Contains("RefreshCommand", xaml, StringComparison.Ordinal);
    Assert.Contains("RetryCommand", xaml, StringComparison.Ordinal);
}

[Fact]
public async Task CentralCompositionProvidesDiagnosticsButLocalModeDoesNot()
{
    var central = await CreateCentralCompositionAsync();
    var local = await CreateSingleCameraCompositionAsync();

    Assert.NotNull(central.MediaDiagnosticsApiClient);
    Assert.Null(local.MediaDiagnosticsApiClient);
}
~~~

- [ ] **Step 3: Run RED.**

~~~powershell
dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~MediaDiagnosticsViewModelTests|FullyQualifiedName~MediaDiagnosticsViewStructureTests|FullyQualifiedName~ApplicationCatalogCompositionTests"
~~~

Expected RED: missing ViewModel, page bindings, and composition property.

- [ ] **Step 4: Implement async-safe ViewModel and page lifecycle.**

Implement these rules:

1. StartAsync performs one immediate GET and starts one five-second polling task.
2. A single async gate prevents overlapping GETs. A tick arriving during an active GET is skipped, not queued.
3. Refresh posts the signal and then performs a later GET; it never runs reconciliation locally.
4. Retry is executable only for a row currently Faulted, sends all stable identity fields, then refreshes.
5. Unavailable errors update safe state once and never create a repeated MessageBox loop.
6. StopAsync cancels and awaits polling. DisposeAsync is idempotent and never blocks with sync-over-async.
7. Rows retain stable DeviceId + ChannelId + StreamType identity across snapshots.
8. MediaPageViewModel calls existing Media Settings load/secret-clear behavior and diagnostics start/stop without changing secret semantics.

- [ ] **Step 5: Add the compact UI and central composition hookup.**

Retain all existing Media Settings fields and buttons. Add summary bindings for ServerHealth, ActiveStreamCount, ViewerCount, FaultCount, and RefreshCommand; add a safe stream list with display name, channel, type, state, viewers, safe error, stale state, and conditional Retry. Do not add Stop All or source/credential fields.

Expose diagnostics only from formal central ApplicationCatalogComposition. Keep local SingleCameraTest composition unchanged. Change MainWindow.xaml to bind the existing Media page to the coordinator while retaining the capability-gated navigation.

- [ ] **Step 6: Run GREEN and WPF/playback regressions.**

~~~powershell
dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~MediaDiagnosticsViewModelTests|FullyQualifiedName~MediaDiagnosticsViewStructureTests|FullyQualifiedName~ApplicationCatalogCompositionTests"
dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~MediaViewPasswordBindingTests|FullyQualifiedName~VideoTilePlaybackLifecycleTests|FullyQualifiedName~FormalPlaybackCompositionTests"
~~~

- [ ] **Step 7: Scan, commit, and push.**

~~~powershell
git diff --check
rg -n "\.Result\b|\.Wait\(|GetAwaiter\(\)\.GetResult\(\)" src\VideoMonitor.Wpf
rg -n "Password|password_ciphertext|ZlmSecret|secret|originUrl|SourceUri|rtsp://|ProxyKey|PlaybackTicket|PlaybackUrl|SigningKey|Data Source=" src\VideoMonitor.Wpf\Catalog\MediaDiagnosticsApiClient.cs src\VideoMonitor.Wpf\ViewModels\MediaDiagnosticsViewModel.cs src\VideoMonitor.Wpf\ViewModels\MediaDiagnosticsStreamRowViewModel.cs src\VideoMonitor.Wpf\ViewModels\MediaPageViewModel.cs src\VideoMonitor.Wpf\Views\Pages\MediaView.xaml
~~~

Expected new diagnostics code has no sync-over-async and no sensitive values.

~~~powershell
git add src/VideoMonitor.Wpf/Catalog/MediaDiagnosticsApiClient.cs src/VideoMonitor.Wpf/ViewModels/MediaDiagnosticsStreamRowViewModel.cs src/VideoMonitor.Wpf/ViewModels/MediaDiagnosticsViewModel.cs src/VideoMonitor.Wpf/ViewModels/MediaPageViewModel.cs src/VideoMonitor.Wpf/Configuration/ApplicationCatalogComposition.cs src/VideoMonitor.Wpf/ViewModels/MainViewModel.cs src/VideoMonitor.Wpf/App.xaml.cs src/VideoMonitor.Wpf/MainWindow.xaml src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml.cs tests/VideoMonitor.Core.Tests/Catalog/MediaDiagnosticsApiClientTests.cs tests/VideoMonitor.Core.Tests/ViewModels/MediaDiagnosticsViewModelTests.cs tests/VideoMonitor.Core.Tests/Views/MediaDiagnosticsViewStructureTests.cs tests/VideoMonitor.Core.Tests/Composition/ApplicationCatalogCompositionTests.cs
git commit -m "feat: add WPF media diagnostics surface"
git push origin release/zlm-v1-finalize
~~~

Stop for Sol review.

## Final Regression / Security / Completion Gate

Execute only after Sol has approved all six task commits. Do not create a merge commit or update master as part of this plan.

- [ ] **Step 1: Verify final changed-file scope.**

~~~powershell
git status --short
git diff --name-only 452dbad504be5408b01807b5bf7eeffe1a7ed30d..HEAD
git diff --check 452dbad504be5408b01807b5bf7eeffe1a7ed30d..HEAD
~~~

The scope may contain only the diagnostics contracts, Server service/endpoints/signal, ticket-free ensure boundary, WPF client/ViewModel/page/composition hookup, and corresponding tests. It must not contain Camera, SQLite schema, ZLM configuration, VLC option, MediaMTX, or unrelated cleanup.

- [ ] **Step 2: Run focused Finalize-1 verification.**

~~~powershell
dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaDiagnosticsServiceTests|FullyQualifiedName~MediaDiagnosticsApiTests|FullyQualifiedName~MediaReconcileSignalTests|FullyQualifiedName~FormalStreamEnsureServiceTests"
dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~MediaDiagnosticsApiClientTests|FullyQualifiedName~MediaDiagnosticsViewModelTests|FullyQualifiedName~MediaDiagnosticsViewStructureTests"
~~~

Use fresh output for counts and failures.

- [ ] **Step 3: Run all required tests and builds.**

~~~powershell
dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj
dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj
dotnet test .\VideoMonitor.sln -c Debug
dotnet build .\VideoMonitor.sln -c Debug
dotnet build .\VideoMonitor.sln -c Debug -t:Rebuild
~~~

Expected: zero test failures and zero build errors. Compare warnings with the 15-warning baseline: CA1416 = 8 and CS8602 = 7. Any additional warning is a gate failure.

- [ ] **Step 4: Run security and architecture scans.**

~~~powershell
rg -n "\.Result\b|\.Wait\(|GetAwaiter\(\)\.GetResult\(\)" src
rg -n "CreateUnbounded|CreateBounded" src
rg -n "originUrl|SourceUri|ProxyKey|ZlmSecret|SigningKey|PlaybackTicket|PlaybackUrl|Data Source=|rtsp://" src/VideoMonitor.Core/Media src/VideoMonitor.Server/Media src/VideoMonitor.Server/Playback src/VideoMonitor.Wpf/Catalog src/VideoMonitor.Wpf/ViewModels src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml
rg -n "StopAll|stop-all|Stop All" src/VideoMonitor.Core/Media src/VideoMonitor.Server/Media src/VideoMonitor.Wpf/Catalog src/VideoMonitor.Wpf/ViewModels src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml
~~~

Review field-name matches manually. Diagnostics must contain no secret value, raw exception, origin evidence, ticket, or URL. Channels must remain bounded. The three known old WPF disposal hits remain a separate Finalize-2 item and are not changed by this plan.

- [ ] **Step 5: Verify invariants.**

~~~text
MediaRuntimeRegistry remains the only runtime truth.
Diagnostics has no runtime persistence.
Ready/viewer/fault counts use the fixed definitions.
Idle, null-observation, and future-observation stale behavior is correct.
Refresh is bounded, coalesced, and serialized.
Fault retry uses the ticket-free formal ensure boundary.
Normal formal playback still issues its ticket and URL exactly once.
GET /api/v1/media/runtime remains available.
WPF diagnostics polling has one in-flight GET and cancellable shutdown.
No diagnostics route exposes Stop All or sensitive fields.
No ZLM/VLC tuning, Camera change, SQLite migration, or MediaMTX code was added.
~~~

- [ ] **Step 6: Record final verification without merging.**

~~~powershell
git status --short
git log --oneline --decorate -8
~~~

The release branch must be clean. Pushes are limited to origin/release/zlm-v1-finalize; this plan never pushes or merges master. Hardware acceptance remains a separate field gate and is not implied by automated results.

## Execution order and review boundaries

~~~text
Task 1 → Sol review
Task 2 → Sol review
Task 3 → Sol review
Task 4 → Sol review
Task 5 → Sol review
Task 6 → Sol review
Final Regression / Security / Completion Gate
~~~

No Task 7B/7C, MediaMTX adapter, ZLM tuning, VLC tuning, Camera configuration, or unrelated deployment work begins under this plan. After the final gate, stop and wait for a separate integration decision.
