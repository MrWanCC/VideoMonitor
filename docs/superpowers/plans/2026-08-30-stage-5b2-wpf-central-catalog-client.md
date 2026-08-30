# Stage 5B-2 WPF Central Catalog Client Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the WPF client use `VideoMonitor.Server` as the formal Catalog authority, add V3 two-level group semantics, preserve a password-safe in-memory client snapshot with resilient connection handling, and keep the existing fixed 4+3 monitor layout usable when the Catalog is empty, partial, stale, or offline.

**Architecture:** Extend the existing Server/Core Catalog contract with `group_kind` while reusing `MonitorGroupType`, then put all WPF central communication behind `CatalogApiClient`, `ServerConnectionCoordinator`, `ClientCatalogCache`, and password-safe read/write abstractions. Formal mode never falls back to JSON; `SingleCameraTest.Enabled=true` keeps the local JSON path through compatibility adapters. Monitor, Secondary Monitor, and Device Management consume DTO/read-model data and use Guid identity only.

**Tech Stack:** .NET SDK 8.0.424, C# 12, ASP.NET Core `net8.0`, WPF `net8.0-windows`, Microsoft.Data.Sqlite 8.x, CommunityToolkit.Mvvm 8.4.2, xUnit 2.5.3.

**Spec:** `docs/superpowers/specs/2026-08-30-stage-5b2-wpf-central-catalog-client-design.md`

## Global Constraints

- The approved spec and this plan must enter the formal Git baseline before implementation starts.
- Every Task follows its own TDD loop: add a failing test, confirm RED for the intended missing behavior, add the smallest implementation, run focused GREEN tests, run the affected suite, commit, and stop.
- Each Task commit is reviewed by Sol before the next Task starts.
- Server SQLite is the formal Catalog authority. Server unavailability never falls back to an editable JSON Catalog.
- JSON and Core `CameraDevice` paths are used only when `SingleCameraTest.Enabled=true`.
- Do not implement a Legacy JSON Migration subsystem or reintroduce local JSON as a formal Catalog source.
- Reuse only the existing `MonitorGroupType` values: `UnloadingStation`, `Chute`, and `Tunnel`. Do not create or rename a parallel group-kind enum.
- The hierarchy is exactly `Root Category -> Business Child Group -> CameraDevice`. A Child cannot contain a Child, and a Device cannot attach directly to a Root.
- A new Root requires a Kind. A historical V2 unclassified Root may receive a valid Kind once; after assignment the Kind is immutable.
- Central cache/read-model types contain neither `Password` nor `PasswordCiphertext`; they expose only `HasPassword`.
- The central Catalog describes configuration, not health. Runtime status starts as `CameraStatus.Unknown`.
- Names are display text only. Selection, switching, recovery, and references use stable Guid identity.
- The fixed layout remains Main = three Chute slots plus one Tunnel slot, and Secondary = three UnloadingStation slots. Missing slots become `null` and render as “未配置”; incomplete data is valid.
- Do not introduce a dynamic `LayoutProfile`.
- Preserve `Revision + expectedRevision + HTTP 409`; do not add edit locks, automatic merge, or last-write-wins behavior.
- Communication code supports absolute HTTP and HTTPS URIs. Local development and controlled debugging may use HTTP; production requires HTTPS. TLS certificate validation bypass is never allowed.
- Formal client settings path is `C:\ProgramData\VideoMonitor\Client\client-settings.json`.
- Development data paths are `D:\Work\VideoMonitor\.devdata\client\` and `D:\Work\VideoMonitor\.devdata\server\`; `.devdata/` must be ignored by Git.
- Cloud-ready means seams only. Do not implement login, RBAC, JWT, tenant/site identity, Edge Agent, cloud database, or cloud video relay.
- Do not enter Stage 5C.

## File Structure

Core:

Modify:

- `src/VideoMonitor.Core/Models/DeviceGroup.cs`
- `src/VideoMonitor.Core/Catalog/DeviceGroupDto.cs`
- `src/VideoMonitor.Core/Catalog/CatalogRequests.cs`
- `src/VideoMonitor.Core/Models/MonitorGroup.cs`
- `src/VideoMonitor.Core/Services/MonitorCatalogProjection.cs`
- `src/VideoMonitor.Core/Services/MonitorLayoutSnapshot.cs`
- `src/VideoMonitor.Core/Services/MonitorSwitchService.cs`

Create:

- `src/VideoMonitor.Core/Catalog/IDeviceCatalogReadModel.cs`

Server / Infrastructure:

Modify:

- `src/VideoMonitor.Infrastructure/Persistence/SqliteDatabaseInitializer.cs`
- `src/VideoMonitor.Infrastructure/Persistence/SqliteCentralCatalogRepository.cs`
- `src/VideoMonitor.Server/Catalog/CatalogApplicationService.cs`

`CatalogEndpoints.cs` is changed only if an existing DTO signature requires it for compilation; endpoint behavior and error contracts remain those approved in Stage 5B-1.

WPF Catalog:

Create:

- `src/VideoMonitor.Wpf/Catalog/CatalogApiException.cs`
- `src/VideoMonitor.Wpf/Catalog/CatalogApiClient.cs`
- `src/VideoMonitor.Wpf/Catalog/IDeviceCatalogCommandService.cs`
- `src/VideoMonitor.Wpf/Catalog/ClientCatalogCache.cs`
- `src/VideoMonitor.Wpf/Catalog/IUiDispatcher.cs`
- `src/VideoMonitor.Wpf/Catalog/WpfUiDispatcher.cs`
- `src/VideoMonitor.Wpf/Catalog/LegacyDeviceCatalogReadModel.cs`
- `src/VideoMonitor.Wpf/Catalog/LegacyDeviceCatalogCommandService.cs`
- `src/VideoMonitor.Wpf/Catalog/RemoteDeviceCatalogCommandService.cs`
- `src/VideoMonitor.Wpf/Catalog/ServerConnectionState.cs`
- `src/VideoMonitor.Wpf/Catalog/IClientConnectionClock.cs`
- `src/VideoMonitor.Wpf/Catalog/SystemClientConnectionClock.cs`
- `src/VideoMonitor.Wpf/Catalog/ServerConnectionCoordinator.cs`

WPF configuration:

Create:

- `src/VideoMonitor.Wpf/Configuration/ClientSettings.cs`
- `src/VideoMonitor.Wpf/Configuration/IClientSettingsStore.cs`
- `src/VideoMonitor.Wpf/Configuration/ClientSettingsPathProvider.cs`
- `src/VideoMonitor.Wpf/Configuration/JsonClientSettingsStore.cs`

Modify:

- `.gitignore`

WPF:

Modify:

- `src/VideoMonitor.Wpf/ViewModels/DeviceEditDraftViewModel.cs`
- `src/VideoMonitor.Wpf/ViewModels/DeviceGroupTreeItemViewModel.cs`
- `src/VideoMonitor.Wpf/ViewModels/DeviceManagementViewModel.cs`
- `src/VideoMonitor.Wpf/ViewModels/MonitorViewModel.cs`
- `src/VideoMonitor.Wpf/ViewModels/SecondaryMonitorViewModel.cs`
- `src/VideoMonitor.Wpf/ViewModels/VideoTileViewModel.cs`
- `src/VideoMonitor.Wpf/ViewModels/MainViewModel.cs`
- `src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml`
- `src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml.cs` only when focus handling is required
- `src/VideoMonitor.Wpf/Views/SecondaryMonitorWindow.xaml`
- `src/VideoMonitor.Wpf/MainWindow.xaml`
- `src/VideoMonitor.Wpf/MainWindow.xaml.cs`
- `src/VideoMonitor.Wpf/Controls/StatusBar.xaml`
- `src/VideoMonitor.Wpf/App.xaml.cs`

Create:

- `src/VideoMonitor.Wpf/ViewModels/ServerSettingsViewModel.cs`
- `src/VideoMonitor.Wpf/ViewModels/ServerStatusViewModel.cs`
- `src/VideoMonitor.Wpf/Views/ServerSettingsWindow.xaml`
- `src/VideoMonitor.Wpf/Views/ServerSettingsWindow.xaml.cs`

## Task 1 — Schema V3 Group Kind Contracts and Persistence

Files:

- Modify `src/VideoMonitor.Core/Models/DeviceGroup.cs`
- Modify `src/VideoMonitor.Core/Catalog/DeviceGroupDto.cs`
- Modify `src/VideoMonitor.Core/Catalog/CatalogRequests.cs`
- Modify `src/VideoMonitor.Infrastructure/Persistence/SqliteDatabaseInitializer.cs`
- Modify `src/VideoMonitor.Infrastructure/Persistence/SqliteCentralCatalogRepository.cs`
- Tests: `tests/VideoMonitor.Core.Tests/Infrastructure/SqliteDatabaseInitializerTests.cs`
- Tests: `tests/VideoMonitor.Core.Tests/Infrastructure/SqliteCentralCatalogRepositoryTests.cs`

Contracts:

- Add `public MonitorGroupType? Kind { get; set; }` to `DeviceGroup`.
- `DeviceGroupDto` is `record DeviceGroupDto(Guid Id, string Name, Guid? ParentId, int Sort, bool Enabled, MonitorGroupType? Kind, long Revision)`.
- `CreateGroupRequest` is `(Guid Id, string Name, Guid? ParentId, int Sort, bool Enabled, MonitorGroupType? Kind)`.
- `UpdateGroupRequest` is `(string Name, Guid? ParentId, int Sort, bool Enabled, MonitorGroupType? Kind, long ExpectedRevision)`.
- Set schema version to 3 and add nullable `device_groups.group_kind`.
- Use `MonitorGroupType` only. Do not add `MonitorGroupKind` or another equivalent enum.
- V2 to V3 migration is the sole location allowed to recognize historical names: `卸矿站监控 -> UnloadingStation`, `溜井监控 -> Chute`, `巷道监控 -> Tunnel`. Other Roots remain `NULL`.

Tests must prove current version 3, known-root mapping, unknown-root `NULL`, idempotent migration, Root Kind round-trip, Child Kind `NULL` round-trip, and rejection of an invalid stored enum rather than silent coercion.

TDD commands:

```powershell
dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~SqliteDatabaseInitializerTests
```

After the smallest implementation:

```powershell
dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SqliteDatabaseInitializerTests|FullyQualifiedName~SqliteCentralCatalogRepositoryTests"
dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj
```

Commit: `feat: add catalog group kind schema v3`

TDD checklist:

- [ ] RED: add the migration, round-trip, and invalid-enum tests; run the focused command above and confirm failure because V3 Kind persistence is absent.
- [ ] GREEN: add only the V3 field, migration, and repository mapping; run the focused and affected-suite commands above and confirm PASS.
- [ ] Commit the Task 1 files with the stated message, then stop for review.

## Task 2 — Enforce Two-Level Group Semantics on Server

Files:

- Modify `src/VideoMonitor.Server/Catalog/CatalogApplicationService.cs`
- Modify `CatalogEndpoints.cs` only if compilation requires an existing DTO signature update
- Tests: `tests/VideoMonitor.Server.Tests/CatalogApplicationServiceTests.cs`
- Tests: `tests/VideoMonitor.Server.Tests/CatalogApiTests.cs`

Validation contract:

- A new Root has `ParentId == null`, a defined non-null Kind, and no parent.
- A new Child has a Root parent and `Kind == null`.
- A Root remains a Root. A formally assigned Root Kind cannot change; a legacy `NULL` Root may receive one valid Kind once.
- A Child always points directly to a Root, has `Kind == null`, and may move to another Root.
- Reject Root/Child conversion, Child nesting, parent cycles, and Device-to-Root assignment with `CATALOG_VALIDATION_FAILED`.
- A Device target must be a Child.

Test new Root without Kind, Child with Kind, Child pointing to Child, Root-to-Child, Child-to-Root, Child move Root A to Root B, Root Kind mutation, one-time legacy Kind assignment, Device-to-Root, Device-to-Child, and GET serialization of Root Kind plus Child `null`.

Focused command:

```powershell
dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter FullyQualifiedName~CatalogApplicationServiceTests
```

Affected suite and build:

```powershell
dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj
dotnet build .\VideoMonitor.sln -c Debug
```

Commit: `feat: enforce catalog group hierarchy semantics`

TDD checklist:

- [ ] RED: add the Root/Child/Device validation cases; run the focused command and confirm failure because the existing service accepts an invalid hierarchy.
- [ ] GREEN: add the smallest service validation and preserve the existing error contract; run the focused command, affected Server suite, and Debug build and confirm PASS.
- [ ] Commit the Task 2 files with the stated message, then stop for review.

## Task 3 — Client Settings and Atomic Save

Files:

- Create `src/VideoMonitor.Wpf/Configuration/ClientSettings.cs`
- Create `src/VideoMonitor.Wpf/Configuration/IClientSettingsStore.cs`
- Create `src/VideoMonitor.Wpf/Configuration/ClientSettingsPathProvider.cs`
- Create `src/VideoMonitor.Wpf/Configuration/JsonClientSettingsStore.cs`
- Modify `.gitignore` to add `.devdata/`
- Tests: `tests/VideoMonitor.Core.Tests/Configuration/ClientSettingsStoreTests.cs`

Contracts:

```csharp
public sealed record ClientServerSettings(string? BaseUrl);

public sealed record ClientSettings(ClientServerSettings Server)
{
    public static ClientSettings Empty { get; } = new(new(null));
}

public interface IClientSettingsStore
{
    ClientSettings Load();

    Task SaveAsync(
        ClientSettings settings,
        CancellationToken cancellationToken = default);
}
```

Use `C:\ProgramData\VideoMonitor\Client\` by default and allow an injected `D:\Work\VideoMonitor\.devdata\client\` root. The application does not change ACLs; deployment creates the directory and grants Modify to the WPF runtime identity.

`Load()` returns `Empty` when the file is absent and throws `InvalidDataException` with a safe message for malformed JSON. Save uses same-directory `client-settings.tmp`, a `FileStream`, and `Flush(flushToDisk: true)`. If the target is missing, use same-directory atomic rename/create; if it exists, use atomic replace. A failed operation preserves the previous target and performs best-effort temporary cleanup.

Tests cover first-save round-trip, existing-file replacement, and replacement failure preserving the old settings.

Commit: `feat: add atomic client server settings`

TDD checklist:

- [ ] RED: add first-save, replacement, and failure-preservation cases; run the ClientSettingsStoreTests filter and confirm failure because the settings store does not exist.
- [ ] GREEN: implement the path provider and atomic write behavior only; run the focused and affected Core test suite and confirm PASS.
- [ ] Commit the Task 3 files with the stated message, then stop for review.

## Task 4 — CatalogApiClient

Files:

- Create `src/VideoMonitor.Wpf/Catalog/CatalogApiException.cs`
- Create `src/VideoMonitor.Wpf/Catalog/CatalogApiClient.cs`
- Tests: `tests/VideoMonitor.Core.Tests/Catalog/CatalogApiClientTests.cs`

`CatalogApiClient` never mutates `HttpClient.BaseAddress` during a Server switch. Every operation receives an explicit `Uri baseUri`.

Public surface:

```csharp
Task CheckReadyAsync(Uri baseUri, CancellationToken cancellationToken = default);
Task<CatalogSnapshotDto> GetCatalogAsync(Uri baseUri, CancellationToken cancellationToken = default);
Task<DeviceGroupDto> CreateGroupAsync(Uri baseUri, CreateGroupRequest request, CancellationToken cancellationToken = default);
Task<DeviceGroupDto> UpdateGroupAsync(Uri baseUri, Guid id, UpdateGroupRequest request, CancellationToken cancellationToken = default);
Task DeleteGroupAsync(Uri baseUri, Guid id, long expectedRevision, CancellationToken cancellationToken = default);
Task<CameraDeviceDto> CreateDeviceAsync(Uri baseUri, CreateDeviceRequest request, CancellationToken cancellationToken = default);
Task<CameraDeviceDto> UpdateDeviceAsync(Uri baseUri, Guid id, UpdateDeviceRequest request, CancellationToken cancellationToken = default);
Task DeleteDeviceAsync(Uri baseUri, Guid id, long expectedRevision, CancellationToken cancellationToken = default);
```

Use the existing Stage 5B-1 endpoint and error contracts after reading `CatalogEndpoints.cs`; do not guess delete revision transport. Catalog GET performs zero password unprotect operations. Test ready 200, Catalog Kind deserialization, 409 code/current revision, malformed error responses mapped to safe `CATALOG_UNAVAILABLE`, transport failure, exactly one write request, no automatic POST/PUT/DELETE retry, and no password or raw-server-body disclosure.

Commit: `feat: add central catalog api client`

TDD checklist:

- [ ] RED: add HTTP handler cases for the listed responses and request rules; run the CatalogApiClientTests filter and confirm failure because the client is absent.
- [ ] GREEN: implement explicit-URI requests and safe response mapping; run the focused and affected Core test suites and confirm PASS.
- [ ] Commit the Task 4 files with the stated message, then stop for review.

## Task 5 — Password-Safe Read Model, Cache, Dispatcher, and Legacy Adapter

Files:

- Create `src/VideoMonitor.Core/Catalog/IDeviceCatalogReadModel.cs`
- Create `src/VideoMonitor.Wpf/Catalog/IUiDispatcher.cs`
- Create `src/VideoMonitor.Wpf/Catalog/WpfUiDispatcher.cs`
- Create `src/VideoMonitor.Wpf/Catalog/ClientCatalogCache.cs`
- Create `src/VideoMonitor.Wpf/Catalog/LegacyDeviceCatalogReadModel.cs`
- Tests: cache, dispatcher, and legacy-adapter tests under `tests/VideoMonitor.Core.Tests/Catalog/`

The read-model contract is:

```csharp
public interface IDeviceCatalogReadModel
{
    IReadOnlyList<DeviceGroupDto> GetGroups();

    IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId);

    CameraDeviceDto? GetDevice(Guid deviceId);

    event EventHandler? Changed;
}
```

The dispatcher contract is:

```csharp
public interface IUiDispatcher
{
    Task InvokeAsync(Action action, CancellationToken cancellationToken = default);
}
```

`ClientCatalogCache` stores only a complete `CatalogSnapshotDto` or equivalent password-safe snapshot. It never stores Core `CameraDevice`, `Password`, or `PasswordCiphertext`. Replacement prepares and validates a complete snapshot, atomically swaps the reference, and publishes `Changed` through the dispatcher only when content differs. `GetGroup` and `GetDevice` use Guid identity. The legacy adapter is restricted to `SingleCameraTest` and maps password to `HasPassword` inside the adapter without exposing it.

Tests cover atomic replacement, no notification for identical snapshots, Guid lookup, dispatcher publication, reflection absence of sensitive DTO properties, and legacy `HasPassword` mapping.

Commit: `feat: add password safe client catalog cache`

TDD checklist:

- [ ] RED: add cache replacement, dispatcher, DTO-safety, and legacy-adapter cases; run the focused Catalog tests and confirm failure because the read model and cache are absent.
- [ ] GREEN: implement the safe snapshot cache and adapter boundaries; run focused and affected Core tests and confirm PASS.
- [ ] Commit the Task 5 files with the stated message, then stop for review.

## Task 6 — ServerConnectionCoordinator

Files:

- Create `src/VideoMonitor.Wpf/Catalog/ServerConnectionState.cs`
- Create `src/VideoMonitor.Wpf/Catalog/IClientConnectionClock.cs`
- Create `src/VideoMonitor.Wpf/Catalog/SystemClientConnectionClock.cs`
- Create `src/VideoMonitor.Wpf/Catalog/ServerConnectionCoordinator.cs`
- Add only a minimal internal client seam to `CatalogApiClient.cs` if deterministic tests require it; do not add a second transport implementation
- Tests: `tests/VideoMonitor.Core.Tests/Catalog/ServerConnectionCoordinatorTests.cs`

States are `Unconfigured`, `Connecting`, `Connected`, and `Unavailable`. Status is:

```csharp
public sealed record ServerConnectionStatus(
    Uri? BaseUri,
    ServerConnectionState State,
    DateTimeOffset? LastSuccessfulSyncUtc,
    bool IsStale);
```

Clock:

```csharp
public interface IClientConnectionClock
{
    DateTimeOffset UtcNow { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);

    double NextJitterUnit();
}
```

The coordinator exposes `Status`, `StatusChanged`, `RunAsync`, `RefreshNowAsync`, `ProbeAsync`, and `SwitchServerAsync(Uri candidate, Func<bool> hasUnsavedDraft, CancellationToken cancellationToken = default)`.

Use one process loop, a `SemaphoreSlim` single-flight refresh gate, shutdown cancellation, and bounded retry delays `2s -> 5s -> 10s -> 15s -> 15s` with ±20% jitter. Online Catalog refresh uses a configurable 30-second-scale period with ±20% jitter. A failed first connection leaves an empty cache; a post-success failure preserves the stale cache and marks `Unavailable`; reconnect requires a complete Catalog refresh. Unchanged snapshots do not notify.

Switching probes B with readiness and Catalog GET without changing A. It checks the Draft before and after probing, persists settings atomically, then commits BaseUri B, Catalog B, and `Connected` together. A settings-save failure leaves A BaseUri, cache, and state unchanged. Once B is accepted, later B failure reconnects to B and never silently returns to A.

Deterministic tests cover no configuration, first connect, stale mode, reconnect, retry schedule, jitter, periodic refresh, single-flight behavior, failed B probe, settings failure, and Draft blocking.

Commit: `feat: add central server connection coordinator`

TDD checklist:

- [ ] RED: add deterministic coordinator tests for connection, refresh, retry, and switch behavior; run the coordinator filter and confirm failure because the coordinator is absent.
- [ ] GREEN: implement one loop, single-flight refresh, bounded backoff, and atomic switching; run focused and affected Core tests and confirm PASS.
- [ ] Commit the Task 6 files with the stated message, then stop for review.

## Task 7 — Monitor Projection and Fixed Nullable 4+3

Files:

- Modify `src/VideoMonitor.Core/Models/MonitorGroup.cs`
- Modify `src/VideoMonitor.Core/Services/MonitorCatalogProjection.cs`
- Modify `src/VideoMonitor.Core/Services/MonitorLayoutSnapshot.cs`
- Modify `src/VideoMonitor.Core/Services/MonitorSwitchService.cs`
- Tests: `tests/VideoMonitor.Core.Tests/Services/MonitorCatalogProjectionTests.cs`
- Tests: `tests/VideoMonitor.Core.Tests/Services/MonitorSwitchServiceTests.cs`

`MonitorGroup` contains `GroupId`, `RootGroupId`, `RootName`, `RootSort`, and `Sort`. `MonitorLayoutSnapshot` contains `IReadOnlyList<CameraInfo?> MainSlots` and `IReadOnlyList<CameraInfo?> SecondarySlots`.

`MonitorCatalogProjection.CreateGroups(IDeviceCatalogReadModel catalog)` accepts only the central read model in formal mode. It includes enabled Roots with non-null Kind and direct enabled Children with direct Root parents. Malformed or missing parents are excluded without throwing. Only enabled devices/channels are projected. Central `CameraInfo.Status` starts as `Unknown`; no Chinese name-to-type mapping is allowed.

`MonitorSwitchService` accepts `IReadOnlyList<MonitorGroup>`, exposes `ReplaceGroups`, `SwitchChuteGroup(Guid)`, `SwitchTunnelGroup(Guid)`, and `SwitchUnloadingGroup(Guid)`, and orders defaults by Root.Sort, Child.Sort, GroupId. It preserves a selected Guid when valid and rejects wrong-kind Guids without name lookup.

Main always maps four slots as Chute 0, Chute 1, Chute 2, Tunnel 0. Secondary always has three UnloadingStation slots. Missing entries are `null`.

Tests cover empty data, one/two Chute groups, Tunnel, UnloadingStation, same-Kind Roots, duplicate names, deterministic ordering, deleted/disabled selection fallback, and wrong-kind identity.

Commit: `feat: make monitor layout catalog tolerant`

TDD checklist:

- [ ] RED: add projection, hierarchy filtering, default-order, and nullable-slot cases; run the two focused service filters and confirm failure because the central read-model projection is absent.
- [ ] GREEN: implement the two-level projection and Guid-based switch behavior; run the focused and affected Core suites and confirm PASS.
- [ ] Commit the Task 7 files with the stated message, then stop for review.

## Task 8 — Monitor, Secondary, and VideoTile DTO Refactor

Files:

- Modify `src/VideoMonitor.Wpf/ViewModels/MonitorViewModel.cs`
- Modify `src/VideoMonitor.Wpf/ViewModels/SecondaryMonitorViewModel.cs`
- Modify `src/VideoMonitor.Wpf/ViewModels/VideoTileViewModel.cs`
- Modify `src/VideoMonitor.Wpf/ViewModels/MonitorTreeItemViewModel.cs` only if required
- Modify `src/VideoMonitor.Wpf/Views/SecondaryMonitorWindow.xaml`
- Tests: `tests/VideoMonitor.Core.Tests/Views/MonitorCatalogRefreshTests.cs`
- Tests: `tests/VideoMonitor.Core.Tests/Views/MonitorUiStateTests.cs`
- Create `tests/VideoMonitor.Core.Tests/Views/SecondaryMonitorCatalogTests.cs`

Formal `VideoTile` updates consume `CameraInfo`, `CameraDeviceDto?`, `CameraChannelDto?`, and runtime `CameraStatus`; they do not consume Core `CameraDevice`. Add `ResetUnconfigured()` so an empty slot displays `CameraName = "未配置"`, `Status = Unknown`, `IP = "--"`, stream = `"--"`, and the existing unconfigured visual state.

`MonitorViewModel` listens to `IDeviceCatalogReadModel.Changed`, re-projects groups, calls `MonitorSwitchService.ReplaceGroups`, rebuilds the tree while preserving selected Guids, and renders nullable slots. `SecondaryMonitorViewModel` uses dynamic UnloadingStation groups and Guid/object command parameters rather than fixed names. `SecondaryMonitorWindow.xaml` uses an `ItemsControl` for those groups.

Tests cover empty Catalogs, duplicate Child names, same-Kind Roots, selected-Guid preservation, delete fallback, null-slot reset, and dynamic Secondary groups.

Commit: `feat: bind monitor views to central catalog read model`

TDD checklist:

- [ ] RED: add ViewModel and Secondary Catalog refresh cases; run the affected View tests and confirm failure because the ViewModels still depend on the old source shape.
- [ ] GREEN: bind the ViewModels and tiles to password-safe DTO/read-model data with nullable slot reset; run the focused and affected WPF test suites and confirm PASS.
- [ ] Commit the Task 8 files with the stated message, then stop for review.

## Task 9 — Async Command Service and Device Management Draft

Files:

- Create `src/VideoMonitor.Wpf/Catalog/IDeviceCatalogCommandService.cs`
- Create `src/VideoMonitor.Wpf/Catalog/RemoteDeviceCatalogCommandService.cs`
- Create `src/VideoMonitor.Wpf/Catalog/LegacyDeviceCatalogCommandService.cs`
- Modify `src/VideoMonitor.Wpf/ViewModels/DeviceEditDraftViewModel.cs`
- Modify `src/VideoMonitor.Wpf/ViewModels/DeviceGroupTreeItemViewModel.cs`
- Modify `src/VideoMonitor.Wpf/ViewModels/DeviceManagementViewModel.cs`
- Tests: `tests/VideoMonitor.Core.Tests/Catalog/DeviceCatalogCommandServiceTests.cs`
- Tests: `tests/VideoMonitor.Core.Tests/Views/DeviceManagementDraftTests.cs`

The command contract is:

```csharp
public interface IDeviceCatalogCommandService
{
    bool CanWrite { get; }

    event EventHandler? AvailabilityChanged;

    Task<DeviceGroupDto> CreateGroupAsync(CreateGroupRequest request, CancellationToken cancellationToken = default);
    Task<DeviceGroupDto> UpdateGroupAsync(Guid id, UpdateGroupRequest request, CancellationToken cancellationToken = default);
    Task DeleteGroupAsync(Guid id, long expectedRevision, CancellationToken cancellationToken = default);
    Task<CameraDeviceDto> CreateDeviceAsync(CreateDeviceRequest request, CancellationToken cancellationToken = default);
    Task<CameraDeviceDto> UpdateDeviceAsync(Guid id, UpdateDeviceRequest request, CancellationToken cancellationToken = default);
    Task DeleteDeviceAsync(Guid id, long expectedRevision, CancellationToken cancellationToken = default);
}
```

Remote commands use only the coordinator's current Connected BaseUri, issue one write, and then run a full refresh. They do not blindly retry. After an ambiguous Create/Delete timeout, a refresh checks known identity presence/absence. An ambiguous Update, especially one containing `NewPassword`, raises a safe uncertainty exception and retains the Draft because GET cannot prove a password write.

The legacy command adapter is async-shaped but only supports local `SingleCameraTest`. Device Management receives `IDeviceCatalogReadModel` and `IDeviceCatalogCommandService`, uses DTO collections and `AsyncRelayCommand`, and exposes `IsSaving`, `IsServerAvailable`, `OperationError`, and `HasUnsavedDraft`. Add Child remains UI-only until Save. Cancel performs zero writes. Device edits preserve existing channel IDs and unedited channels; new IDs are generated before POST. Blank password maps to `NewPassword = null`; non-empty replaces it; old password is never shown. Offline and 409 states disable writes or preserve the Draft as appropriate.

Tests cover one-write behavior, timeout identity checks, uncertainty for password updates, Draft retention, 409 handling, and offline command availability.

Commit: `feat: make device management use async catalog drafts`

TDD checklist:

- [ ] RED: add command-service and Draft cases for one-write behavior, timeout ambiguity, password safety, conflicts, and offline mode; run the focused command and confirm failure because the async command boundary is absent.
- [ ] GREEN: implement the remote/legacy command services and DTO-based Draft flow; run the focused and affected WPF/Core suites and confirm PASS.
- [ ] Commit the Task 9 files with the stated message, then stop for review.

## Task 10 — Root Category Management

Files:

- Modify `src/VideoMonitor.Wpf/ViewModels/DeviceManagementViewModel.cs`
- Modify `src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml`
- Modify `src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml.cs` only if focus handling is required
- Tests: `tests/VideoMonitor.Core.Tests/Views/DeviceManagementGroupTests.cs`

This Task is required because an empty Catalog must allow the user to create the first Root.

Expose `RootKindOptions = Enum.GetValues<MonitorGroupType>()`, `IsRootEditorOpen`, `EditingRootId`, `RootEditName`, `RootEditKind`, `CanEditRootKind`, and commands `BeginAddRootCommand`, `BeginEditRootCommand`, `SaveRootCommand`, `CancelRootEditCommand`, and `DeleteRootCommand`.

New Root behavior: create a local Draft with required Name, required Kind, client-generated Guid, `ParentId = null`, next Sort, and `Enabled = true`. Cancel performs zero writes. A mapped Root's Kind is read-only; a legacy `Kind = null` Root may be assigned once. Child creation never asks for Kind. Add a small “新增分类” entry and Root context actions for edit/delete without redesigning the page.

Required automation IDs: `AddRootCategoryButton`, `RootCategoryNameTextBox`, `RootCategoryKindComboBox`, `SaveRootCategoryButton`, and `CancelRootCategoryButton`.

Tests cover Root draft cancel, required fields, one-time legacy assignment, immutable mapped Kind, stable Guid creation, and delete command routing.

Commit: `feat: add root category management`

TDD checklist:

- [ ] RED: add Root draft and command cases; run the focused DeviceManagementGroupTests filter and confirm failure because Root management is absent.
- [ ] GREEN: implement the smallest Root editor and command bindings without redesigning the page; run focused and affected WPF tests and confirm PASS.
- [ ] Commit the Task 10 files with the stated message, then stop for review.

## Task 11 — Server Settings UI and Status

Files:

- Create `src/VideoMonitor.Wpf/ViewModels/ServerSettingsViewModel.cs`
- Create `src/VideoMonitor.Wpf/ViewModels/ServerStatusViewModel.cs`
- Create `src/VideoMonitor.Wpf/Views/ServerSettingsWindow.xaml`
- Create `src/VideoMonitor.Wpf/Views/ServerSettingsWindow.xaml.cs`
- Modify `src/VideoMonitor.Wpf/ViewModels/MainViewModel.cs`
- Modify `src/VideoMonitor.Wpf/MainWindow.xaml`
- Modify `src/VideoMonitor.Wpf/MainWindow.xaml.cs`
- Modify `src/VideoMonitor.Wpf/Controls/StatusBar.xaml`
- Tests: `tests/VideoMonitor.Core.Tests/Views/ServerSettingsViewModelTests.cs`
- Tests: `tests/VideoMonitor.Core.Tests/Views/MainHeaderControlStateTests.cs`

Display states are Unconfigured = 未配置, Connecting = 连接中, Connected = 已连接, and Unavailable = 连接失败. Null last-sync time displays `--`; otherwise use local `yyyy-MM-dd HH:mm:ss`.

Settings UI provides BaseUrl, Test Connection, and Save. Test only probes and never switches. A successful test result is cleared when the URL changes. Save calls `SwitchServerAsync`, which probes again and cannot use a previous Test result as a consistency shortcut. `HasUnsavedDraft` blocks Save.

Open the settings window from MainWindow. Bind StatusBar's lower-right state and last sync to the real central connection state. The existing green system labels must either bind to actual overall state or be renamed to client-running state; do not redesign unrelated styling.

Tests cover state labels, last-sync formatting, repeat probe on Save, Draft blocking, and no false healthy indication after Server failure.

Commit: `feat: add central server settings and status ui`

TDD checklist:

- [ ] RED: add status and settings interaction cases; run the focused settings/header filters and confirm failure because the Server settings UI is absent.
- [ ] GREEN: implement settings Test/Save and real status binding while preserving existing styling; run focused and affected WPF tests and confirm PASS.
- [ ] Commit the Task 11 files with the stated message, then stop for review.

## Task 12 — Formal Central Composition and SingleCameraTest Compatibility

Files:

- Modify `src/VideoMonitor.Wpf/App.xaml.cs`
- Optional only when App size requires it: create `src/VideoMonitor.Wpf/Configuration/ApplicationCatalogComposition.cs` for catalog mode composition/lifecycle only
- Tests: `tests/VideoMonitor.Core.Tests/Composition/ApplicationCatalogCompositionTests.cs`
- Update `tests/VideoMonitor.Core.Tests/ShutdownCleanupCoordinatorTests.cs` only when shutdown behavior requires it

Formal mode composition:

```text
ClientSettingsStore
CatalogApiClient
ClientCatalogCache
ServerConnectionCoordinator
RemoteDeviceCatalogCommandService
```

Startup loads SingleCameraTest options and client settings, creates an empty safe cache, creates central services, projects an empty Catalog, constructs the safe monitor switch service and ViewModels/windows, shows the Shell first, and starts the coordinator in the background. Server offline does not exit WPF.

Formal mode does not create `JsonDeviceCatalogStore`, `InMemoryDeviceCatalog`, or `DeviceCatalogPersistenceCoordinator`, and never falls back to JSON. `SingleCameraTest=true` keeps the existing JSON store, in-memory Catalog, persistence coordinator, and local playback source, while UI read/write access goes through the legacy password-safe read/command adapters.

Remove App-level hardcoded group names such as `备用1`, `Z-1#巷`, `2#主溜井`, and `3#主溜井`. Single-camera playback remains DeviceId/ChannelId based. Group switching is Guid based. Remote code contains no synchronous blocking via `.Result`, `.Wait()`, or `GetAwaiter().GetResult()`.

Shutdown cancels the central coordinator and disposes HTTP resources. The local compatibility mode continues persistence flush and playback cleanup. The composition must preserve one authoritative Catalog path per mode and one lifecycle owner.

Commit: `feat: compose formal central catalog client mode`

TDD checklist:

- [ ] RED: add composition and shutdown cases for formal and SingleCameraTest modes; run the focused composition filters and confirm failure because the central composition is not wired.
- [ ] GREEN: implement the two explicit mode compositions and lifecycle ownership only; run focused and affected suites and confirm PASS.
- [ ] Commit the Task 12 files with the stated message, then stop for review.

## Task 13 — Final Acceptance

This is a verification gate, not a feature Task.

Fresh verification:

```powershell
dotnet restore .\VideoMonitor.sln
dotnet build .\VideoMonitor.sln -c Debug --no-restore
dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj -c Debug --no-build
dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj -c Debug --no-build
dotnet test .\VideoMonitor.sln -c Debug --no-build
```

Record test totals, failure count, build errors, and warnings.

Local Server smoke uses isolated development storage:

```powershell
cd D:\Work\VideoMonitor
$env:Storage__RootPath="$PWD\.devdata\server"
dotnet run --project .\src\VideoMonitor.Server\VideoMonitor.Server.csproj --urls http://127.0.0.1:5080
```

From another PowerShell process, verify `/health/live`, `/health/ready`, and `/api/v1/catalog`; expect 200/200/200 and a valid empty Catalog.

WPF smoke and manual acceptance:

1. Launch with no endpoint and confirm 未配置.
2. Set `http://127.0.0.1:5080`, Test, then Save.
3. Confirm 已连接 and an empty Catalog does not crash.
4. Create Root “测试溜井区” with Kind Chute, Child 401, and a Device.
5. Edit a non-password field with a blank password editor and confirm success without asking for the old password.
6. Stop Server; confirm WPF stays open, reports failure, retains stale data, and disables writes.
7. Restart Server; confirm reconnect and full refresh.
8. Confirm an unsaved Draft blocks endpoint switching.
9. Confirm same-name Children under different Roots remain independently selectable.
10. Confirm partial 4+3 data renders unconfigured slots.

Safety verification scans:

- hardcoded legacy display names must not be runtime dependencies;
- the three historical Chinese names may occur only in V3 migration compatibility/tests;
- TLS validation bypass APIs must not occur in `src`;
- synchronous blocking patterns must not occur in the WPF central path;
- `JsonDeviceCatalogStore` and `DeviceCatalogPersistenceCoordinator` must occur in App only in the explicit local compatibility composition;
- `git ls-files` must produce no path under `.devdata/`.

Task 13 does not create an empty commit. If a genuine test-only gap is found, create a separate test commit describing the exact acceptance gap.

## Plan Execution Protocol

1. After Sol approves the plan, land the approved spec and plan docs in the formal `master` baseline.
2. Start Task 1 from the newest approved baseline on a new review branch.
3. Luna implements exactly one Task at a time.
4. Every Task report includes branch, SHA, RED command and failure evidence, GREEN command and result, affected-suite result, and `git status --short`.
5. Stop after every Task.
6. Sol performs an independent GitHub review for every Task.
7. Fix all Critical and Important findings before starting the next Task.
8. Do not start Stage 5C before Task 13 passes.

## Plan Self-Review

### Spec coverage

Map every approved spec area to an implementation Task: core architecture and startup (Task 12), connection/refresh and Dispatcher boundary (Tasks 5–6), V3 Kind and migration (Tasks 1–2), two-level hierarchy (Tasks 2 and 10), monitor tree, Guid identity, fixed 4+3 and default selection (Tasks 7–8), runtime status (Tasks 7–8), Draft/password/Revision behavior (Task 9), settings and atomic switching (Tasks 3, 6, and 11), status UI (Task 11), errors (Tasks 4, 6, and 9), cloud-ready boundary and JSON compatibility (Task 12), and non-goals (Global Constraints and Task 13).

### Deferred-marker and deferral scan

Every code-changing Task has concrete files, test behavior, RED command, implementation contract, GREEN command, and commit message. The plan contains no deferred marker text, generic test-only instruction, or unspecified future behavior.

### Type consistency

Check that all Tasks use the existing `MonitorGroupType`, the stated `DeviceGroupDto` and request field order, the exact read-model and command-service boundaries, `ServerConnectionStatus`, `CameraDeviceDto`, nullable layout slots, and Guid switching consistently.

### Scope

Confirm no Task adds authentication, authorization, JWT, tenant/cloud features, Edge Agent, StreamManager, ZLM lifecycle, playback resolving, runtime health implementation, or dynamic LayoutProfile. Confirm the plan does not create an implementation for Stage 5C.

The final plan must remain an implementation plan only; landing it changes no production code, tests, project files, or runtime behavior.
