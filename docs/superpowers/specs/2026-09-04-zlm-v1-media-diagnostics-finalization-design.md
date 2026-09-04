# ZLM V1 Media Diagnostics Finalization Design

## 1. Status and scope

This document defines **VideoMonitor ZLM V1 Finalize-1 — Safe Media Diagnostics**.

The purpose is to complete the missing operator-facing diagnostics surface on top of the already merged Stage 5C media runtime. This is a projection and read-model design; it is not a second implementation of runtime state, a playback redesign, or a MediaMTX adapter.

Finalize-1 consumes the current ZLMediaKit, Server, and WPF boundaries and makes the runtime state observable without exposing credentials, origin evidence, management secrets, or playback authorization material.

## 2. Design context and current facts

The current implementation has the following authorities and boundaries:

- `MediaRuntimeRegistry` is the runtime authority.
- `MediaRuntimeSnapshot` and `MediaStreamRuntimeInfo` are the existing safe runtime contracts.
- `MediaRuntimeEndpoints` exposes `GET /api/v1/media/runtime`.
- `MediaReconcilerHostedService` performs one startup reconciliation and then serializes normal and recovery reconciliation in one loop.
- `MediaEventProcessor` receives bounded `on-stream-changed` and `on-stream-none-reader` signals.
- `StreamManager` owns formal stream identity, ownership proof, readiness verification, cleanup, and per-key single-flight.
- `PlaybackStreamService` owns the formal playback ensure boundary.
- `ServerStatusViewModel` already projects central Server connection state into the WPF shell.
- `ApplicationCatalogComposition` already creates the central WPF media composition and a shared formal playback engine.

The relevant source locations are:

- `src/VideoMonitor.Server/Media/MediaRuntimeRegistry.cs`
- `src/VideoMonitor.Server/Media/MediaRuntimeEndpoints.cs`
- `src/VideoMonitor.Server/Media/MediaReconcilerHostedService.cs`
- `src/VideoMonitor.Server/Media/MediaEventProcessor.cs`
- `src/VideoMonitor.Server/Media/StreamManager.cs`
- `src/VideoMonitor.Server/Playback/PlaybackStreamService.cs`
- `src/VideoMonitor.Core/Media/MediaRuntimeContracts.cs`
- `src/VideoMonitor.Wpf/ViewModels/ServerStatusViewModel.cs`
- `src/VideoMonitor.Wpf/Configuration/ApplicationCatalogComposition.cs`

The current runtime contract contains `MediaServerHealth`, `MediaStreamKey`, `StreamRuntimeState`, `SourceObservation`, `ViewerCount`, `StreamOwnership`, `StartedAtUtc`, `ObservedAtUtc`, `LastSuccessUtc`, `SafeLastErrorCode`, `SafeLastErrorMessage`, and `IsStale`. Finalize-1 reuses these facts and does not create another registry, runtime table, or persistence layer.

## 3. Goals

Finalize-1 will:

1. Provide a safe diagnostics snapshot for operators.
2. Provide fixed aggregate counts for server health, active streams, viewers, and faults.
3. Provide safe per-stream state using stable device and channel IDs plus `StreamType`.
4. Provide an explicit refresh request without starting parallel reconciliation.
5. Provide a retry operation for faulted formal streams.
6. Provide a WPF diagnostics surface in the existing Media page.
7. Poll with a bounded, non-overlapping client loop.
8. Derive stale state from observation age at projection time.
9. Keep the DTO, error, and polling concepts independent of the media server implementation so a later MediaMTX V2 can replace only the observation backend.

## 4. Non-goals

Finalize-1 does not include:

- Stop All or any destructive bulk operation.
- WebSocket, SignalR, or SSE transport.
- Prometheus, Grafana, or a metrics exporter.
- A historical status database or runtime SQLite persistence.
- A Camera ping subsystem.
- A second `MediaRuntimeRegistry`.
- ZLMediaKit parameter tuning or VLC parameter tuning.
- MediaMTX implementation or an adapter.
- A rewrite of the 4+3 playback architecture.
- A new Camera credential path.
- Playback URL, playback ticket, or ticket lifetime changes.

## 5. Architecture and data flow

The intended data flow is:

```text
ZLMediaKit
    ↓ hooks and observations
StreamManager / MediaReconcilerHostedService
    ↓ runtime facts
MediaRuntimeRegistry
    ↓ MediaRuntimeSnapshot
MediaDiagnosticsService
    ↓ safe projection
MediaDiagnosticsSnapshotDto
    ↓ HTTP API
MediaDiagnosticsApiClient
    ↓ safe DTOs
MediaDiagnosticsViewModel
    ↓ existing catalog name lookup and WPF binding
MediaView
```

`MediaDiagnosticsService` is a stateless projection service. It reads a runtime snapshot and current time, computes counts and stale flags, and returns a new DTO. It does not retain mutable stream state between calls and does not write to SQLite.

`MediaRuntimeRegistry` remains the only runtime truth. It records observations and lifecycle facts. A background timer must not mutate registry stale flags. Staleness is a property of a diagnostics response at the time it is projected.

## 6. DTO design

The public diagnostics contract is intentionally flattened. It does not expose a `MediaStreamKey` object, origin descriptor, or internal stream entry.

```csharp
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
```

The DTO carries only stable IDs and safe runtime facts. WPF resolves display names through its existing `ClientCatalogCache`; the Server does not expand the response with `CameraDevice`, `CameraChannel`, or credential-bearing domain objects.

## 7. Count semantics

The aggregate fields have fixed meanings:

- `ActiveStreamCount` is the number of streams whose `RuntimeState` is `Ready`.
- `ViewerCount` is the sum of every stream's `ViewerCount` value.
- `FaultCount` is the number of streams whose `RuntimeState` is `Faulted`.
- `Starting` is not active.
- `Idle` is not a fault.
- A stream is counted once even if it has multiple viewers.

These definitions are part of the contract and must not be changed by a client or future implementation without a versioned design decision.

## 8. Stale semantics

The Server owns a `MediaDiagnosticsOptions` configuration section:

```text
MediaDiagnostics:
  FreshnessSeconds: 90
```

`FreshnessSeconds` defaults to `90` and must be a positive integer. It is Server configuration, not a `media_settings` column, because freshness is an observation policy rather than media connection configuration.

For every diagnostics projection:

- `Idle` is never stale.
- `Ready`, `Starting`, and `Faulted` are stale only when `ObservedAtUtc` has a value and `UtcNow - ObservedAtUtc` is greater than `FreshnessSeconds`.
- A missing `ObservedAtUtc` is not immediately stale. This includes a newly `Starting` stream with no observation yet.
- A future observation timestamp is not stale; the age is treated as zero until wall-clock time catches up.
- The registry keeps observation facts. The diagnostics service computes `IsStale` for the response.

This policy tolerates the normal 30-second reconciliation interval while still identifying a stream that has missed multiple observation opportunities. The time source is injectable for deterministic tests; production uses UTC.

## 9. API design

### 9.1 Existing runtime endpoint

`GET /api/v1/media/runtime` remains registered and backward compatible. Finalize-1 does not remove or rename it and does not change its existing safe-field boundary.

### 9.2 Diagnostics snapshot

`GET /api/v1/media/diagnostics`

The endpoint returns `200` with `MediaDiagnosticsSnapshotDto` when the Server is ready. It returns `503` with the existing safe error DTO conventions and code `MEDIA_DIAGNOSTICS_UNAVAILABLE` when the diagnostics service cannot provide a usable runtime snapshot.

The response must not contain playback or source material. In particular, it must not contain `PlaybackUrl`, `PlaybackTicket`, `TicketExpiresUtc`, `originUrl`, `SourceUri`, `ProxyKey`, connection strings, admin URLs, Camera Password, ZLM Secret, or signing keys.

### 9.3 Refresh request

`POST /api/v1/media/diagnostics/refresh`

The endpoint signals the existing `MediaReconcilerHostedService` through a small reconcile-signal abstraction. It does not call `ReconcileAsync` directly and does not create a new reconciliation task.

The endpoint returns `202 Accepted` when a signal was queued or when an equivalent signal is already pending. Repeated requests may therefore be coalesced. It returns `503` with `MEDIA_DIAGNOSTICS_UNAVAILABLE` only when the reconciler signal boundary is unavailable or shutting down.

The response means that reconciliation was requested; it does not mean that a new snapshot has already been produced. The client obtains the result through a later diagnostics GET.

### 9.4 Faulted stream retry

`POST /api/v1/media/diagnostics/streams/{deviceId}/{channelId}/{streamType}/retry`

The route contains the complete `MediaStreamKey` identity. The service first reads the current runtime state for that exact key.

The HTTP semantics are:

- `202 Accepted` when a faulted stream retry has been handed to the existing formal ensure boundary.
- `404` with a safe identity-not-found error when the requested stream identity is not present in the current runtime/catalog context.
- `409` with `MEDIA_STREAM_NOT_FAULTED` when the stream exists but is not currently `Faulted`.
- `503` with `MEDIA_DIAGNOSTICS_RETRY_FAILED` or the existing safe media-unavailable mapping when the formal ensure boundary cannot accept the retry.

Retry must re-enter the existing formal stream path. It must not construct an RTSP URI, read Camera credentials, call ZLMediaKit directly, issue a playback ticket, return a playback URL, duplicate StreamManager lifecycle code, or guess a `StreamType` from a name.

The implementation may extract a small internal formal-ensure boundary from `PlaybackStreamService` if reusing the current service would issue an unnecessary playback ticket. That extraction must preserve one StreamManager lifecycle and must not become a broad playback refactor. The diagnostics API returns only accepted/safe-error information.

## 10. Refresh concurrency and lifecycle

The current `MediaReconcilerHostedService` serialized loop remains the sole reconciliation executor:

```text
startup ReconcileOnceAsync
    ↓
normal delay OR bounded recovery signal
    ↓
one ReconcileOnceAsync
    ↓
next delay OR coalesced recovery signal
```

The recovery signal remains bounded and coalesced. A rapid sequence of refresh requests produces at most one pending signal and never N parallel reconciliations. The existing startup reconciliation and recovery backoff remain unchanged.

The signal abstraction has one responsibility: request recovery and report whether recovery is available or already pending. It must not expose the channel itself to HTTP or WPF code.

## 11. WPF client design

`MediaDiagnosticsApiClient` uses the configured Server base URI and the existing `HttpClient` conventions. Its operations are:

- `GetDiagnosticsAsync` for the safe snapshot.
- `RequestRefreshAsync` for the `202` refresh request.
- `RetryFaultedAsync` with the complete `DeviceId`, `ChannelId`, and `StreamType` identity.

The client never accesses ZLMediaKit, constructs a Camera URL, stores a Secret, or accepts a credential-bearing URI. HTTP failures are converted to the existing safe catalog/media error model; raw response bodies are not shown to operators.

## 12. WPF ViewModel design

`MediaDiagnosticsViewModel` owns the presentation state for the existing Media page:

- `ServerHealth`.
- `ActiveStreamCount`.
- `ViewerCount`.
- `FaultCount`.
- A streams collection keyed by stable `DeviceId + ChannelId + StreamType`.
- Manual refresh command.
- Faulted-stream retry command.
- Polling lifecycle and cancellation state.

On page or ViewModel activation, it performs one immediate GET. While active, it requests one GET every five seconds. A per-ViewModel async gate ensures that at most one diagnostics GET is in flight. If a prior GET is still running when the next interval arrives, the new interval is skipped rather than queued.

Manual refresh POSTs the refresh signal and then obtains state through a subsequent GET. Manual refresh does not directly run reconciliation and does not wait for a server-side reconciliation task to finish.

Retry is enabled only for a stream whose projected `RuntimeState` is `Faulted`. It sends the full stable identity, then refreshes the diagnostics projection. Non-faulted rows have retry disabled. Page shutdown and ViewModel disposal cancel the polling loop and await its termination without creating an unbounded message or error loop.

When the Server is unavailable, the ViewModel shows one safe unavailable state, preserves no sensitive response text, and does not create repeated MessageBox notifications.

## 13. WPF UI design

The diagnostics surface is added to the existing Media page; no large navigation system is introduced.

The summary area shows:

- 媒体服务 — mapped from `ServerHealth`.
- 活动流 — `ActiveStreamCount`.
- 观看者 — `ViewerCount`.
- 故障 — `FaultCount`.
- Manual refresh.

The stream list shows:

- Device display name resolved locally from `ClientCatalogCache`.
- Channel display name/number resolved locally.
- Stream type.
- Runtime state.
- Viewer count.
- Safe error text.
- Stale indication.
- Retry action only when faulted.

The Server response remains ID-based. A missing local display-name mapping is rendered as a safe unknown identity; it does not trigger a Camera lookup or a credential request.

## 14. Security boundary

The diagnostics response, retry response, WPF client, and WPF ViewModel must never expose:

- Camera Password.
- `originUrl` or `SourceUri`.
- Any credential-bearing RTSP URI.
- ZLM Secret.
- ProxyKey.
- Playback signing key.
- Playback ticket or playback URL.
- Connection string.
- ZLMediaKit admin URL.

`SafeLastErrorCode` and `SafeLastErrorMessage` must come from the existing safe error vocabulary. The implementation must not forward `Exception.Message`, a raw ZLMediaKit response, a Camera URL, or request/object dumps to the client.

Retry authorization is based on the complete stable key and current runtime state, not on a display name. Refresh is a bounded signal, not an administrative command surface.

## 15. Failure semantics

The design uses these safe diagnostics errors:

- `MEDIA_DIAGNOSTICS_UNAVAILABLE` — the safe diagnostics surface cannot read a usable runtime snapshot or accept a refresh signal.
- `MEDIA_DIAGNOSTICS_RETRY_FAILED` — a faulted retry could not be accepted by the formal ensure boundary.
- `MEDIA_STREAM_NOT_FAULTED` — retry was requested for a stream that is not faulted.
- Existing safe StreamManager, Catalog, and media error codes may be reused where their semantics match.

All errors returned to WPF are safe codes and safe user messages. The Server may log an internal exception type at its existing safe logging boundary, but no sensitive request values or raw credential-bearing URLs are logged.

## 16. Task 7A / 7B / 7C mapping

The current repository already contains the Task 7A foundation:

- Safe runtime contracts.
- Runtime registry.
- `GET /api/v1/media/runtime`.
- Runtime API safety tests.
- Central Server status projection.

Finalize-1 closes the remaining original Task 7 capability through the following logical split:

- Task 7B: diagnostics API client, diagnostics ViewModel, bounded polling, refresh, retry, and the Media diagnostics UI.
- Task 7C: freshness projection, stale semantics, and expired-state UI.

Task 7B and Task 7C share the DTO and do not create a second runtime authority. Together with the existing Task 7A foundation, they close the original Stage 5C “Minimal Media Diagnostics” requirement.

## 17. MediaMTX future compatibility

The following upper-layer concepts are media-server agnostic and must remain stable:

- Diagnostics DTO shape.
- Safe error presentation.
- WPF API client boundary.
- ViewModel polling and retry commands.
- Stale semantics.
- Stable stream identity.

A future MediaMTX V2 may replace the runtime observation source, gateway, and reconciliation backend. It must not require a second WPF diagnostics UI or a new credential path. No MediaMTX adapter is designed or implemented by Finalize-1.

## 18. Testing strategy and acceptance

The implementation must use TDD and preserve the existing Core, Server, and solution test suites.

### Server and Core projection tests

The tests must cover:

- `DiagnosticsProjectionContainsSafeCounts`.
- `OldReadyObservationBecomesStale`.
- `IdleNeverBecomesStale`.
- `StartingWithoutObservationIsNotImmediatelyStale`.
- `SafeProjectionContainsNoSecretFields`.
- `FaultedRetryUsesFormalEnsureBoundary`.
- `NonFaultedRetryIsRejected`.
- `RefreshOnlySignalsReconciler`.
- `RepeatedRefreshDoesNotCreateParallelReconcile`.

### API tests

The tests must cover:

- `DiagnosticsApiReturnsSafeDto`.
- `DiagnosticsApiDoesNotExposeStopAll`.
- `DiagnosticsApiDoesNotExposePlaybackTicket`.
- `DiagnosticsApiRejectsNonFaultedRetry`.
- `DiagnosticsApiReturnsUnavailableSafely`.

### WPF tests

The tests must cover:

- `MediaDiagnosticsApiClientReadsSafeDto`.
- `MediaDiagnosticsViewModelInitialRefresh`.
- `MediaDiagnosticsViewModelPollDoesNotOverlap`.
- `MediaDiagnosticsViewModelShowsStale`.
- `RetryOnlyEnabledForFaulted`.
- `DisposeCancelsPolling`.
- `ServerUnavailableDoesNotCreateMessageLoop`.

The completed implementation must run the focused diagnostics tests, all Core tests, all Server tests, all solution tests, build/rebuild, secret scans, `git diff --check`, and the changed-file scan.

## 19. Expected implementation boundary

The likely implementation boundary is:

- `src/VideoMonitor.Core/Media/MediaDiagnosticsDtos.cs`
- `src/VideoMonitor.Server/Media/MediaDiagnosticsService.cs`
- `src/VideoMonitor.Server/Media/MediaDiagnosticsEndpoints.cs`
- `src/VideoMonitor.Server/Media/MediaDiagnosticsOptions.cs`
- A small reconcile-signal abstraction if the existing hosted service needs one.
- `src/VideoMonitor.Wpf/Catalog/MediaDiagnosticsApiClient.cs`
- `src/VideoMonitor.Wpf/ViewModels/MediaDiagnosticsViewModel.cs`
- The existing `MediaView.xaml` and only the necessary composition hookup.
- Corresponding focused tests.

The implementation should avoid changes to `StreamManager` core lifecycle, the ZLM gateway, SQLite schema, `FormalPlaybackCoordinator`, and `VlcPlaybackService`. If implementation requires a broad change to any of those boundaries, the design must be re-reviewed before coding continues.

## 20. Explicit invariants

1. `MediaRuntimeRegistry` remains the single runtime truth.
2. Diagnostics never persists runtime state.
3. The WPF formal path never receives Camera credentials.
4. Diagnostics retry never constructs RTSP or calls ZLMediaKit directly.
5. Refresh never starts parallel reconciliation.
6. No diagnostics API exposes ticket, Secret, or origin evidence.
7. Existing `GET /api/v1/media/runtime` remains backward compatible.
8. No ZLM or VLC performance tuning is part of Finalize-1.
9. No Camera change or SQLite migration is required.
10. MediaMTX V2 remains a later architecture replacement.

## 21. Self-review checklist

Before implementation planning, review this spec against the current repository and confirm:

- The design projects the current runtime rather than creating another state store.
- Retry uses the formal ensure boundary rather than copying StreamManager or handling credentials.
- Refresh only signals the serialized reconciler and coalesces repeated requests.
- Stale state is derived from observation timestamps and is not maintained by a timer in the registry.
- Sensitive fields and raw error details are excluded from every diagnostics response.
- The existing central WPF composition and 4+3 playback lifecycle remain intact.
- No MediaMTX implementation, ZLM tuning, VLC tuning, Camera configuration, or SQLite migration has entered this scope.
