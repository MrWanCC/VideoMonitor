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
  - Responsibility: hold stable group identity, hierarchy links, display fields, ordering, enabled state, and nullable `MonitorGroupType` Kind.
- `src/VideoMonitor.Core/Catalog/DeviceGroupDto.cs`
  - Responsibility: expose the password-safe group read contract used across the Server API and WPF client.
- `src/VideoMonitor.Core/Catalog/CatalogRequests.cs`
  - Responsibility: define group/device/channel write request contracts, including expected revisions and password write semantics.
- `src/VideoMonitor.Core/Models/MonitorGroup.cs`
  - Responsibility: represent the monitor projection identity and root/child ordering metadata without owning Catalog persistence.
- `src/VideoMonitor.Core/Services/MonitorCatalogProjection.cs`
  - Responsibility: convert the password-safe read model into valid two-level monitor groups and Guid-based selections.
- `src/VideoMonitor.Core/Services/MonitorLayoutSnapshot.cs`
  - Responsibility: carry nullable fixed Main and Secondary slot collections for the 4+3 layout.
- `src/VideoMonitor.Core/Services/MonitorSwitchService.cs`
  - Responsibility: validate Kind-specific Guid switches, deterministic defaults, and selection fallback for projected groups.

Create:

- `src/VideoMonitor.Core/Catalog/IDeviceCatalogReadModel.cs`
  - Responsibility: expose the central password-safe read-only Catalog boundary and its change notification.

Server / Infrastructure:

Modify:

- `src/VideoMonitor.Infrastructure/Persistence/SqliteDatabaseInitializer.cs`
  - Responsibility: apply and validate SQLite schema versions and the V2-to-V3 `group_kind` migration.
- `src/VideoMonitor.Infrastructure/Persistence/SqliteCentralCatalogRepository.cs`
  - Responsibility: persist and read central groups, devices, channels, revisions, and protected credential values through SQLite transactions.
- `src/VideoMonitor.Server/Catalog/CatalogApplicationService.cs`
  - Responsibility: validate two-level group and device mutations and translate repository results into application outcomes.

- `src/VideoMonitor.Server/Catalog/CatalogEndpoints.cs`
  - Responsibility: retain the approved endpoint transport and error contracts, changing only when the Task 1 DTO signature update requires compilation alignment.

WPF Catalog:

Create:

- `src/VideoMonitor.Wpf/Catalog/CatalogApiException.cs`
  - Responsibility: represent safe HTTP Catalog failures with machine-readable code and optional current revision.
- `src/VideoMonitor.Wpf/Catalog/CatalogMutationUncertainException.cs`
  - Responsibility: represent an ambiguous remote mutation using only a safe operation name and entity Guid.
- `src/VideoMonitor.Wpf/Catalog/CatalogApiClient.cs`
  - Responsibility: perform only HTTP serialization/deserialization, endpoint invocation, and safe Catalog error mapping; own no BaseUrl and no retry policy.
- `src/VideoMonitor.Wpf/Catalog/IDeviceCatalogCommandService.cs`
  - Responsibility: define asynchronous device/group command operations independently of transport and local compatibility implementation.
- `src/VideoMonitor.Wpf/Catalog/ClientCatalogCache.cs`
  - Responsibility: store a process-local password-safe `CatalogSnapshotDto`, perform Guid lookup, atomically replace snapshots, and publish UI-dispatched changes.
- `src/VideoMonitor.Wpf/Catalog/IUiDispatcher.cs`
  - Responsibility: abstract dispatching cache notifications and ViewModel mutations to the WPF UI thread.
- `src/VideoMonitor.Wpf/Catalog/WpfUiDispatcher.cs`
  - Responsibility: adapt the active WPF Dispatcher to `IUiDispatcher` without embedding Catalog policy.
- `src/VideoMonitor.Wpf/Catalog/LegacyDeviceCatalogReadModel.cs`
  - Responsibility: adapt the legacy local `IDeviceCatalog` to password-safe DTO reads for explicit `SingleCameraTest` mode only.
- `src/VideoMonitor.Wpf/Catalog/LegacyDeviceCatalogCommandService.cs`
  - Responsibility: adapt local `IDeviceCatalog` writes to the asynchronous command boundary for `SingleCameraTest` only.
- `src/VideoMonitor.Wpf/Catalog/RemoteDeviceCatalogCommandService.cs`
  - Responsibility: route one remote mutation through the current connected Server and request a full refresh after success.
- `src/VideoMonitor.Wpf/Catalog/ServerConnectionState.cs`
  - Responsibility: define connection states and the immutable status value shown by WPF.
- `src/VideoMonitor.Wpf/Catalog/IClientConnectionClock.cs`
  - Responsibility: abstract UTC time, delay, and deterministic jitter generation for coordinator behavior.
- `src/VideoMonitor.Wpf/Catalog/SystemClientConnectionClock.cs`
  - Responsibility: provide production time, cancellation-aware delays, and bounded random jitter.
- `src/VideoMonitor.Wpf/Catalog/ServerConnectionCoordinator.cs`
  - Responsibility: own endpoint state, initial connect, refresh, reconnect/backoff, stale mode, and atomic Server switching.

WPF configuration:

Create:

- `src/VideoMonitor.Wpf/Configuration/ClientSettings.cs`
  - Responsibility: define the non-sensitive client Server endpoint settings value.
- `src/VideoMonitor.Wpf/Configuration/IClientSettingsStore.cs`
  - Responsibility: define synchronous load and cancellation-aware durable client-settings save operations.
- `src/VideoMonitor.Wpf/Configuration/ClientSettingsPathProvider.cs`
  - Responsibility: resolve the ProgramData Client settings path or an injected development root.
- `src/VideoMonitor.Wpf/Configuration/JsonClientSettingsStore.cs`
  - Responsibility: read safe JSON settings and perform same-directory flushed atomic create/replace writes.

Modify:

- `.gitignore`
  - Responsibility: prevent local `.devdata/` development storage from entering Git.

WPF:

Modify:

- `src/VideoMonitor.Wpf/ViewModels/DeviceEditDraftViewModel.cs`
  - Responsibility: hold local DTO-based device edit fields and password write-only draft state.
- `src/VideoMonitor.Wpf/ViewModels/DeviceGroupTreeItemViewModel.cs`
  - Responsibility: present password-safe group tree items and Guid selection state.
- `src/VideoMonitor.Wpf/ViewModels/DeviceManagementViewModel.cs`
  - Responsibility: orchestrate DTO-based presentation, local drafts, async commands, and conflict/error UX; own no HTTP or authoritative Catalog storage.
- `src/VideoMonitor.Wpf/ViewModels/MonitorViewModel.cs`
  - Responsibility: consume the central read model, rebuild the monitor tree, preserve Guid selection, and render nullable slots.
- `src/VideoMonitor.Wpf/ViewModels/SecondaryMonitorViewModel.cs`
  - Responsibility: consume dynamic UnloadingStation groups and route selection by Guid.
- `src/VideoMonitor.Wpf/ViewModels/VideoTileViewModel.cs`
  - Responsibility: render password-safe device/channel DTO data, runtime status, and unconfigured tile state.
- `src/VideoMonitor.Wpf/ViewModels/MainViewModel.cs`
  - Responsibility: expose central connection status and Server settings actions to the shell.
- `src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml`
  - Responsibility: provide the existing Device Management layout plus minimal Root editor bindings.
- `src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml.cs` only when focus handling is required
  - Responsibility: provide only required focus/keyboard plumbing for the Root editor.
- `src/VideoMonitor.Wpf/Views/SecondaryMonitorWindow.xaml`
  - Responsibility: render dynamic Secondary group choices and fixed nullable tiles.
- `src/VideoMonitor.Wpf/MainWindow.xaml`
  - Responsibility: bind the shell Server status and settings entry without redesigning unrelated UI.
- `src/VideoMonitor.Wpf/MainWindow.xaml.cs`
  - Responsibility: open the Server settings window and coordinate shell-level lifecycle hooks.
- `src/VideoMonitor.Wpf/Controls/StatusBar.xaml`
  - Responsibility: display actual central Server availability and last successful sync.
- `src/VideoMonitor.Wpf/App.xaml.cs`
  - Responsibility: compose the formal central mode or explicit SingleCameraTest compatibility mode and own shutdown order.

Create:

- `src/VideoMonitor.Wpf/ViewModels/ServerSettingsViewModel.cs`
  - Responsibility: validate and execute Test/Save endpoint commands while preserving Draft and atomic-switch rules.
- `src/VideoMonitor.Wpf/ViewModels/ServerStatusViewModel.cs`
  - Responsibility: map `ServerConnectionStatus` to safe localized display state and last-sync text.
- `src/VideoMonitor.Wpf/Views/ServerSettingsWindow.xaml`
  - Responsibility: provide minimal BaseUrl, Test Connection, and Save controls.
- `src/VideoMonitor.Wpf/Views/ServerSettingsWindow.xaml.cs`
  - Responsibility: bind the settings window to its ViewModel and close without owning connection policy.

Tests:

- `tests/VideoMonitor.Core.Tests/Infrastructure/SqliteDatabaseInitializerTests.cs`
  - Responsibility: verify schema V3 creation, idempotent upgrade, root Kind migration, and unsupported-version rejection.
- `tests/VideoMonitor.Core.Tests/Infrastructure/SqliteCentralCatalogRepositoryTests.cs`
  - Responsibility: verify central group/device persistence and repository-facing Kind/channel contracts.
- `tests/VideoMonitor.Server.Tests/CatalogApplicationServiceTests.cs`
  - Responsibility: verify Root/Child/Device hierarchy validation and application error mapping.
- `tests/VideoMonitor.Server.Tests/CatalogApiTests.cs`
  - Responsibility: verify HTTP serialization of Kind and the approved Catalog endpoint behavior.
- `tests/VideoMonitor.Core.Tests/Configuration/ClientSettingsStoreTests.cs`
  - Responsibility: verify client settings paths, first-save behavior, atomic replacement, and failure preservation.
- `tests/VideoMonitor.Core.Tests/Catalog/CatalogApiClientTests.cs`
  - Responsibility: verify explicit-URI HTTP calls, safe errors, no write retry, and password-safe Catalog reads.
- `tests/VideoMonitor.Core.Tests/Catalog/ClientCatalogCacheTests.cs`
  - Responsibility: verify atomic password-safe snapshot replacement, Guid lookup, and change suppression.
- `tests/VideoMonitor.Core.Tests/Catalog/LegacyDeviceCatalogReadModelTests.cs`
  - Responsibility: verify local compatibility projection exposes only safe DTO fields and HasPassword.
- `tests/VideoMonitor.Core.Tests/Catalog/ServerConnectionCoordinatorTests.cs`
  - Responsibility: verify connection state, periodic refresh, bounded retry/jitter, single-flight behavior, and atomic switching.
- `tests/VideoMonitor.Core.Tests/Services/MonitorCatalogProjectionTests.cs`
  - Responsibility: verify two-level DTO projection, filtering, ordering, and runtime status initialization.
- `tests/VideoMonitor.Core.Tests/Services/MonitorSwitchServiceTests.cs`
  - Responsibility: verify Guid-based Kind switching, defaults, and fixed 4+3 nullable layout behavior.
- `tests/VideoMonitor.Core.Tests/ViewModels/MonitorCatalogRefreshTests.cs`
  - Responsibility: verify monitor ViewModel refresh and selected-Guid preservation.
- `tests/VideoMonitor.Core.Tests/ViewModels/MonitorUiStateTests.cs`
  - Responsibility: verify nullable tile reset, runtime status presentation, and existing monitor UI state.
- `tests/VideoMonitor.Core.Tests/ViewModels/SecondaryMonitorCatalogTests.cs`
  - Responsibility: verify dynamic Secondary groups and Guid-based selection.
- `tests/VideoMonitor.Core.Tests/Catalog/DeviceCatalogCommandServiceTests.cs`
  - Responsibility: verify one-write remote commands, uncertainty mapping, refresh confirmation, and offline behavior.
- `tests/VideoMonitor.Core.Tests/ViewModels/DeviceManagementDraftTests.cs`
  - Responsibility: verify DTO-based Draft retention, password safety, conflicts, and asynchronous command state.
- `tests/VideoMonitor.Core.Tests/ViewModels/DeviceManagementGroupTests.cs`
  - Responsibility: verify Root Draft validation, stable IDs, Kind assignment, and command routing.
- `tests/VideoMonitor.Core.Tests/ViewModels/ServerSettingsViewModelTests.cs`
  - Responsibility: verify Test versus Save semantics, repeat probe, Draft blocking, and settings status presentation.
- `tests/VideoMonitor.Core.Tests/ViewModels/MainHeaderControlStateTests.cs`
  - Responsibility: verify shell status bindings preserve accurate central connection state.
- `tests/VideoMonitor.Core.Tests/Composition/ApplicationCatalogCompositionTests.cs`
  - Responsibility: verify formal central composition versus explicit SingleCameraTest compatibility composition.
- `tests/VideoMonitor.Core.Tests/Services/ShutdownCleanupCoordinatorTests.cs`
  - Responsibility: verify one-time shutdown ownership and mode-specific cancellation/cleanup.

## Task 1 — Schema V3 Group Kind Contracts and Persistence

Files:

- Modify `src/VideoMonitor.Core/Models/DeviceGroup.cs`
- Modify `src/VideoMonitor.Core/Catalog/DeviceGroupDto.cs`
- Modify `src/VideoMonitor.Core/Catalog/CatalogRequests.cs`
- Modify `src/VideoMonitor.Infrastructure/Persistence/SqliteDatabaseInitializer.cs`
- Modify `src/VideoMonitor.Infrastructure/Persistence/SqliteCentralCatalogRepository.cs`
- Modify tests: `tests/VideoMonitor.Core.Tests/Infrastructure/SqliteDatabaseInitializerTests.cs`
- Modify tests: `tests/VideoMonitor.Core.Tests/Infrastructure/SqliteCentralCatalogRepositoryTests.cs`

Interfaces:

Consumes:

- `DeviceGroup` with `Guid Id`, `long Revision`, `string Name`, `Guid? ParentId`, `int Sort`, and `bool Enabled`.
- `DeviceGroupDto(Guid Id, string Name, Guid? ParentId, int Sort, bool Enabled, long Revision)`.
- `CreateGroupRequest(Guid Id, string Name, Guid? ParentId, int Sort, bool Enabled)`.
- `UpdateGroupRequest(string Name, Guid? ParentId, int Sort, bool Enabled, long ExpectedRevision)`.
- `SqliteDatabaseInitializer.InitializeAsync(CancellationToken cancellationToken = default)`.
- `ICentralCatalogRepository.GetCatalogAsync(CancellationToken cancellationToken = default)`.
- `ICentralCatalogRepository.GetGroupAsync(Guid id, CancellationToken cancellationToken = default)` and `GetDeviceAsync(Guid id, CancellationToken cancellationToken = default)`.
- `ICentralCatalogRepository.CreateGroupAsync(DeviceGroup group, CancellationToken cancellationToken = default)` and `CreateDeviceAsync(CameraDevice device, CancellationToken cancellationToken = default)`.
- `ICentralCatalogRepository.UpdateGroupAsync(DeviceGroup group, long expectedRevision, CancellationToken cancellationToken = default)`, `DeleteGroupAsync(Guid id, long expectedRevision, CancellationToken cancellationToken = default)`, `UpdateDeviceAsync(CameraDevice device, string? newPassword, long expectedRevision, CancellationToken cancellationToken = default)`, and `DeleteDeviceAsync(Guid id, long expectedRevision, CancellationToken cancellationToken = default)`.

Produces:

- `DeviceGroup.Kind` with type `MonitorGroupType?`.
- `DeviceGroupDto(Guid Id, string Name, Guid? ParentId, int Sort, bool Enabled, MonitorGroupType? Kind, long Revision)`.
- `CreateGroupRequest(Guid Id, string Name, Guid? ParentId, int Sort, bool Enabled, MonitorGroupType? Kind)` and `UpdateGroupRequest(string Name, Guid? ParentId, int Sort, bool Enabled, MonitorGroupType? Kind, long ExpectedRevision)`.
- `SqliteDatabaseInitializer.CurrentSchemaVersion == 3` and the nullable `device_groups.group_kind` migration contract.

Contracts:

- Add `public MonitorGroupType? Kind { get; set; }` to `DeviceGroup`.
- `DeviceGroupDto` is `record DeviceGroupDto(Guid Id, string Name, Guid? ParentId, int Sort, bool Enabled, MonitorGroupType? Kind, long Revision)`.
- `CreateGroupRequest` is `(Guid Id, string Name, Guid? ParentId, int Sort, bool Enabled, MonitorGroupType? Kind)`.
- `UpdateGroupRequest` is `(string Name, Guid? ParentId, int Sort, bool Enabled, MonitorGroupType? Kind, long ExpectedRevision)`.
- Set schema version to 3 and add nullable `device_groups.group_kind`.
- Use `MonitorGroupType` only. Do not add `MonitorGroupKind` or another equivalent enum.
- V2 to V3 migration is the sole location allowed to recognize historical names: `卸矿站监控 -> UnloadingStation`, `溜井监控 -> Chute`, `巷道监控 -> Tunnel`. Other Roots remain `NULL`.

Update the existing `SqliteDatabaseInitializerTests.InitializeAsync_CreatesCurrentSchema` assertion so `MAX(schema_migrations.version) == 3`. Update `InitializeAsync_IsIdempotent` and `InitializeAsync_ConcurrentCallsRemainConsistent` so versions 1, 2, and 3 each occur once. Change `NewerSchemaVersion_IsRejected` to insert version 4 because version 3 is now supported. Also prove known-root mapping, unknown-root `NULL`, idempotent migration, Root Kind round-trip, Child Kind `NULL` round-trip, and rejection of an invalid stored enum rather than silent coercion.

Before editing current-schema assertions, locate other assumptions with:

```powershell
rg -n "SchemaVersion|MAX\(version\)|version = 2|VALUES \(3" tests src
```

Change only assertions tied to the current schema; do not alter historical migration behavior.

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

Execution steps:

- [ ] Step 1: Write failing tests.

  ```csharp
  [Fact]
  public async Task InitializeAsync_UpgradesV2KnownRootKind()
  {
      using var context = TestContext.Create();
      await context.CreateV2DatabaseAsync();
      Assert.Equal(2, await context.ReadMaxSchemaVersionAsync());
      await context.InsertRootAsync("溜井监控");

      await context.CreateInitializer().InitializeAsync();

      Assert.Equal("Chute", await context.ReadGroupKindAsync("溜井监控"));
  }

  [Fact]
  public async Task InitializeAsync_LeavesUnknownRootKindNull()
  {
      using var context = TestContext.Create();
      await context.CreateV2DatabaseAsync();
      Assert.Equal(2, await context.ReadMaxSchemaVersionAsync());
      await context.InsertRootAsync("现场自定义分类");

      await context.CreateInitializer().InitializeAsync();

      Assert.Null(await context.ReadGroupKindAsync("现场自定义分类"));
      Assert.Equal(3, await context.ReadMaxSchemaVersionAsync());
  }

  // Extend the existing TestContext from SqliteDatabaseInitializerTests; these helpers execute real SQLite operations.
  public async Task CreateV2DatabaseAsync()
  {
      await CreateV1DatabaseAsync();
      await using var connection = CreateConnection();
      await connection.OpenAsync();
      await using var transaction = await connection.BeginTransactionAsync();
      await using var command = connection.CreateCommand();
      command.Transaction = transaction;
      command.CommandText = """
          ALTER TABLE device_groups
          ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;

          ALTER TABLE camera_devices
          ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;

          INSERT OR IGNORE INTO schema_migrations(version, applied_at_utc)
          VALUES (2, $appliedAtUtc);
          """;
      command.Parameters.AddWithValue("$appliedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
      await command.ExecuteNonQueryAsync();
      await transaction.CommitAsync();
  }

  public async Task InsertRootAsync(string name)
  {
      await using var connection = CreateConnection();
      await connection.OpenAsync();
      await using var command = connection.CreateCommand();
      command.CommandText = "INSERT INTO device_groups(id, name, parent_id, sort, enabled) VALUES ($id, $name, NULL, 0, 1);";
      command.Parameters.Add(new SqliteParameter("$id", Guid.NewGuid().ToString("N")));
      command.Parameters.Add(new SqliteParameter("$name", name));
      await command.ExecuteNonQueryAsync();
  }

  public async Task<string?> ReadGroupKindAsync(string name)
  {
      await using var connection = CreateConnection();
      await connection.OpenAsync();
      await using var command = connection.CreateCommand();
      command.CommandText = "SELECT group_kind FROM device_groups WHERE name = $name ORDER BY rowid DESC LIMIT 1;";
      command.Parameters.Add(new SqliteParameter("$name", name));
      var value = await command.ExecuteScalarAsync();
      return value is null || Convert.IsDBNull(value) ? null : (string)value;
  }

  public async Task<int> ReadMaxSchemaVersionAsync()
  {
      await using var connection = CreateConnection();
      await connection.OpenAsync();
      await using var command = connection.CreateCommand();
      command.CommandText = "SELECT MAX(version) FROM schema_migrations;";
      return Convert.ToInt32(await command.ExecuteScalarAsync());
  }
  ```

  Extend the existing `TestContext`/`CreateV1DatabaseAsync` pattern with `CreateV2DatabaseAsync`, which upgrades that real fixture to V2 before inserting the test Roots; do not create a separate empty fixture.
- [ ] Step 2: Run RED.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~SqliteDatabaseInitializerTests
  ```

  Confirm failure because the V3 field, migration, and current-version assertions do not yet exist.
- [ ] Step 3: Write the minimal implementation.

  ```csharp
  public sealed class SqliteDatabaseInitializer
  {
      public const int CurrentSchemaVersion = 3;

      public Task InitializeAsync(CancellationToken cancellationToken = default)
      {
          // Apply historical migrations, then one idempotent V2 -> V3 group_kind migration.
          // Existing migration history remains unchanged.
      }

      private Task ApplyV3Async(CancellationToken cancellationToken) { }
  }
  ```

  Add `MonitorGroupType? Kind` to the domain/DTO/request boundaries and parameterized repository mapping. Reject invalid stored enum text instead of coercing it.
- [ ] Step 4: Run focused GREEN.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~SqliteDatabaseInitializerTests
  ```

  Confirm PASS.
- [ ] Step 5: Run affected suite/build.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SqliteDatabaseInitializerTests|FullyQualifiedName~SqliteCentralCatalogRepositoryTests"
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj
  dotnet build .\VideoMonitor.sln -c Debug
  ```
- [ ] Step 6: Review the diff.

  ```powershell
  git diff --check
  git status --short
  ```
- [ ] Step 7: Commit the exact Task 1 paths.

  ```powershell
  git add src/VideoMonitor.Core/Models/DeviceGroup.cs src/VideoMonitor.Core/Catalog/DeviceGroupDto.cs src/VideoMonitor.Core/Catalog/CatalogRequests.cs src/VideoMonitor.Infrastructure/Persistence/SqliteDatabaseInitializer.cs src/VideoMonitor.Infrastructure/Persistence/SqliteCentralCatalogRepository.cs tests/VideoMonitor.Core.Tests/Infrastructure/SqliteDatabaseInitializerTests.cs tests/VideoMonitor.Core.Tests/Infrastructure/SqliteCentralCatalogRepositoryTests.cs
  git commit -m "feat: add catalog group kind schema v3"
  ```
- [ ] Step 8: STOP and return the branch, SHA, RED evidence, GREEN result, affected-suite result, and `git status --short` for Sol review.

## Task 2 — Enforce Two-Level Group Semantics on Server

Files:

- Modify `src/VideoMonitor.Server/Catalog/CatalogApplicationService.cs`
- Modify `CatalogEndpoints.cs` only if compilation requires an existing DTO signature update
- Modify tests: `tests/VideoMonitor.Server.Tests/CatalogApplicationServiceTests.cs`
- Modify tests: `tests/VideoMonitor.Server.Tests/CatalogApiTests.cs`

Interfaces:

Consumes:

- Task 1's `DeviceGroup`/request/DTO Kind contracts.
- `Task<CatalogOperationResult<DeviceGroupDto>> CatalogApplicationService.CreateGroupAsync(CreateGroupRequest request, CancellationToken cancellationToken = default)`.
- `Task<CatalogOperationResult<DeviceGroupDto>> CatalogApplicationService.UpdateGroupAsync(Guid id, UpdateGroupRequest request, CancellationToken cancellationToken = default)` and `Task<CatalogOperationResult<object?>> CatalogApplicationService.DeleteGroupAsync(Guid id, long expectedRevision, CancellationToken cancellationToken = default)`.
- `Task<CatalogOperationResult<CameraDeviceDto>> CatalogApplicationService.CreateDeviceAsync(CreateDeviceRequest request, CancellationToken cancellationToken = default)`.
- `Task<CatalogOperationResult<CameraDeviceDto>> CatalogApplicationService.UpdateDeviceAsync(Guid id, UpdateDeviceRequest request, CancellationToken cancellationToken = default)` and `Task<CatalogOperationResult<object?>> CatalogApplicationService.DeleteDeviceAsync(Guid id, long expectedRevision, CancellationToken cancellationToken = default)`.

Produces:

- The same `Task<CatalogOperationResult<DeviceGroupDto>> CreateGroupAsync(CreateGroupRequest, CancellationToken)`, `Task<CatalogOperationResult<DeviceGroupDto>> UpdateGroupAsync(Guid, UpdateGroupRequest, CancellationToken)`, `Task<CatalogOperationResult<object?>> DeleteGroupAsync(Guid, long, CancellationToken)`, `Task<CatalogOperationResult<CameraDeviceDto>> CreateDeviceAsync(CreateDeviceRequest, CancellationToken)`, `Task<CatalogOperationResult<CameraDeviceDto>> UpdateDeviceAsync(Guid, UpdateDeviceRequest, CancellationToken)`, and `Task<CatalogOperationResult<object?>> DeleteDeviceAsync(Guid, long, CancellationToken)` signatures with two-level Root/Child/Device validation and `CATALOG_VALIDATION_FAILED` mapping.
- Root/Child Kind and ParentId invariants consumed by Task 11 Root UI and Task 7 projection.

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

Execution steps:

- [ ] Step 1: Write failing tests.

  ```csharp
  [Fact]
  public async Task CreateRootWithoutKind_ReturnsValidationFailure()
  {
      var fake = new FakeCentralCatalogRepository();
      var service = CreateService(fake);

      var result = await InvokeAsync(
          service,
          "CreateGroupAsync",
          new CreateGroupRequest(Guid.NewGuid(), "Root", null, 0, true, null),
          CancellationToken.None);

      AssertError(result, "CATALOG_VALIDATION_FAILED", 400);
  }

  [Fact]
  public async Task CreateDeviceAgainstRoot_ReturnsValidationFailure()
  {
      var rootId = Guid.NewGuid();
      var fake = new FakeCentralCatalogRepository
      {
          Groups =
          {
              [rootId] = new DeviceGroupDto(rootId, "Root", null, 0, true, MonitorGroupType.Chute, 1)
          }
      };
      var service = CreateService(fake);

      var result = await InvokeAsync(
          service,
          "CreateDeviceAsync",
          ValidCreateDeviceRequest(rootId),
          CancellationToken.None);

      AssertError(result, "CATALOG_VALIDATION_FAILED", 400);
  }

  // Reuse CreateService, InvokeAsync, AssertError, ValidCreateDeviceRequest, and
  // FakeCentralCatalogRepository already present in CatalogApplicationServiceTests.
  ```

  Add the same fixture pattern for Child-with-Kind, Child-to-Child, Root/Child conversion, Device-to-Root, and a valid Device-to-Child case.
- [ ] Step 2: Run RED.

  ```powershell
  dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter FullyQualifiedName~CatalogApplicationServiceTests
  ```

  Confirm failure because the current service accepts at least one invalid Root/Child/Device relationship.
- [ ] Step 3: Write the minimal implementation.

  ```csharp
  private static string? ValidateGroupMutation(
      DeviceGroupDto? current,
      Guid? parentId,
      MonitorGroupType? kind,
      IReadOnlyList<DeviceGroupDto> groups)
  {
      var parent = parentId is Guid id ? groups.SingleOrDefault(group => group.Id == id) : null;
      var isRoot = parentId is null;
      if (isRoot && kind is null && current is null) return "CATALOG_VALIDATION_FAILED";
      if (!isRoot && parent is null) return "CATALOG_VALIDATION_FAILED";
      if (!isRoot && parent!.ParentId is not null) return "CATALOG_VALIDATION_FAILED";
      if (!isRoot && kind is not null) return "CATALOG_VALIDATION_FAILED";
      if (current is not null && current.ParentId is null && parentId is not null) return "CATALOG_VALIDATION_FAILED";
      if (current is not null && current.ParentId is not null && parentId is null) return "CATALOG_VALIDATION_FAILED";
      if (current is not null && current.ParentId is null && current.Kind is not null && current.Kind != kind) return "CATALOG_VALIDATION_FAILED";
      return null;
  }
  ```

  Apply the same parent check to device create/update, preserve the repository result mapping, and keep the approved error code.
- [ ] Step 4: Run focused GREEN.

  ```powershell
  dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter FullyQualifiedName~CatalogApplicationServiceTests
  ```

  Confirm PASS.
- [ ] Step 5: Run the affected suite/build.

  ```powershell
  dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj
  dotnet build .\VideoMonitor.sln -c Debug
  ```
- [ ] Step 6: Review the diff.

  ```powershell
  git diff --check
  git status --short
  ```
- [ ] Step 7: Commit the exact Task 2 paths.

  ```powershell
  git add src/VideoMonitor.Server/Catalog/CatalogApplicationService.cs src/VideoMonitor.Server/Catalog/CatalogEndpoints.cs tests/VideoMonitor.Server.Tests/Catalog/CatalogApplicationServiceTests.cs tests/VideoMonitor.Server.Tests/Catalog/CatalogApiTests.cs
  git commit -m "feat: enforce catalog group hierarchy semantics"
  ```
- [ ] Step 8: STOP and return the branch, SHA, RED evidence, GREEN result, affected-suite result, and `git status --short` for Sol review.

## Task 3 — Client Settings and Atomic Save

Files:

- Create `src/VideoMonitor.Wpf/Configuration/ClientSettings.cs`
- Create `src/VideoMonitor.Wpf/Configuration/IClientSettingsStore.cs`
- Create `src/VideoMonitor.Wpf/Configuration/ClientSettingsPathProvider.cs`
- Create `src/VideoMonitor.Wpf/Configuration/JsonClientSettingsStore.cs`
- Modify `.gitignore` to add `.devdata/`
- Create tests: `tests/VideoMonitor.Core.Tests/Configuration/ClientSettingsStoreTests.cs`

Interfaces:

Consumes:

- `ClientSettingsPathProvider` root resolution and the existing JSON/file-system APIs available to the WPF project.
- `string ClientSettingsPathProvider.GetPath(string? injectedRoot = null)` and `ClientSettings.Empty`.

Produces:

- `ClientServerSettings(string? BaseUrl)` and `ClientSettings(ClientServerSettings Server)` with `ClientSettings.Empty`.
- `ClientSettings IClientSettingsStore.Load()` and `Task IClientSettingsStore.SaveAsync(ClientSettings settings, CancellationToken cancellationToken = default)`.
- `string ClientSettingsPathProvider.GetPath(string? injectedRoot = null)` and `JsonClientSettingsStore` atomic persistence behavior.

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

Execution steps:

- [ ] Step 1: Write failing tests.

  ```csharp
  [Fact]
  public async Task FirstSave_RoundTripsBaseUrl()
  {
      var store = new JsonClientSettingsStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
      var expected = new ClientSettings(new ClientServerSettings("https://server-b"));

      await store.SaveAsync(expected);

      Assert.Equal(expected, store.Load());
  }

  [Fact]
  public async Task ReplaceFailure_PreservesOldFile()
  {
      var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
      var store = new JsonClientSettingsStore(root);
      var old = new ClientSettings(new ClientServerSettings("https://server-a"));
      await store.SaveAsync(old);
      var targetPath = Path.Combine(root, "client-settings.json");
      await using var targetLock = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read);

      await Assert.ThrowsAnyAsync<IOException>(() => store.SaveAsync(
          new ClientSettings(new ClientServerSettings("https://server-b"))));

      Assert.Equal(old, store.Load());
  }
  ```

  Add a test that asserts a malformed file throws `InvalidDataException` without replacing the old bytes.
- [ ] Step 2: Run RED.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~ClientSettingsStoreTests
  ```

  Confirm failure because the client settings types and store do not exist.
- [ ] Step 3: Write the minimal implementation.

  ```csharp
  public sealed class JsonClientSettingsStore : IClientSettingsStore
  {
      private readonly string filePath;

      public JsonClientSettingsStore(string root)
      {
          filePath = Path.Combine(Path.GetFullPath(root), "client-settings.json");
      }

      public ClientSettings Load() =>
          File.Exists(filePath)
              ? JsonSerializer.Deserialize<ClientSettings>(File.ReadAllText(filePath))
                  ?? throw new InvalidDataException("Client settings are invalid.")
              : ClientSettings.Empty;

      public async Task SaveAsync(ClientSettings settings, CancellationToken cancellationToken = default)
      {
          var directory = Path.GetDirectoryName(filePath)!;
          Directory.CreateDirectory(directory);
          var temporaryPath = Path.Combine(directory, "client-settings.tmp");
          await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
          {
              await JsonSerializer.SerializeAsync(stream, settings, cancellationToken: cancellationToken);
              await stream.FlushAsync(cancellationToken);
              stream.Flush(flushToDisk: true);
          }
          if (File.Exists(filePath))
              File.Replace(temporaryPath, filePath, destinationBackupFileName: null);
          else
              File.Move(temporaryPath, filePath);
      }
  }
  ```

  Keep `JsonClientSettingsStore` non-sensitive and make temporary cleanup best effort without masking the primary exception.
- [ ] Step 4: Run focused GREEN.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~ClientSettingsStoreTests
  ```

  Confirm PASS.
- [ ] Step 5: Run the affected suite/build.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj
  dotnet build .\VideoMonitor.sln -c Debug
  ```
- [ ] Step 6: Review the diff.

  ```powershell
  git diff --check
  git status --short
  ```
- [ ] Step 7: Commit the exact Task 3 paths.

  ```powershell
  git add src/VideoMonitor.Wpf/Configuration/ClientSettings.cs src/VideoMonitor.Wpf/Configuration/IClientSettingsStore.cs src/VideoMonitor.Wpf/Configuration/ClientSettingsPathProvider.cs src/VideoMonitor.Wpf/Configuration/JsonClientSettingsStore.cs .gitignore tests/VideoMonitor.Core.Tests/Configuration/ClientSettingsStoreTests.cs
  git commit -m "feat: add atomic client server settings"
  ```
- [ ] Step 8: STOP and return the branch, SHA, RED evidence, GREEN result, affected-suite result, and `git status --short` for Sol review.

## Task 4 — CatalogApiClient

Files:

- Create `src/VideoMonitor.Wpf/Catalog/CatalogApiException.cs`
- Create `src/VideoMonitor.Wpf/Catalog/CatalogApiClient.cs`
- Create tests: `tests/VideoMonitor.Core.Tests/Catalog/CatalogApiClientTests.cs`

Interfaces:

Consumes:

- `CatalogSnapshotDto`, `DeviceGroupDto`, `CameraDeviceDto`, `CameraChannelDto`, and the existing Stage 5B-1 HTTP endpoint/error contracts.
- `CreateGroupRequest(Guid Id, string Name, Guid? ParentId, int Sort, bool Enabled)`, `UpdateGroupRequest(string Name, Guid? ParentId, int Sort, bool Enabled, long ExpectedRevision)`, `CreateDeviceRequest(Guid Id, Guid GroupId, string Name, string IpAddress, int SdkPort, int RtspPort, string Username, string Password, string Manufacturer, string Model, TransportMode TransportMode, bool Enabled, string Remark, IReadOnlyList<CameraChannelInput> Channels)`, and `UpdateDeviceRequest(Guid GroupId, string Name, string IpAddress, int SdkPort, int RtspPort, string Username, string? NewPassword, string Manufacturer, string Model, TransportMode TransportMode, bool Enabled, string Remark, long ExpectedRevision, IReadOnlyList<CameraChannelInput> Channels)`.
- `CatalogErrorDto(string Code, string Message, long? CurrentRevision)` for safe error-field mapping.

Produces:

- `CatalogApiException(string code, long? currentRevision = null)` with safe properties `Code` and `CurrentRevision`, plus `static Task<CatalogApiException> FromResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)` that parses only the approved error envelope.
- `CatalogApiClient.CheckReadyAsync(Uri baseUri, CancellationToken cancellationToken = default)`.
- `CatalogApiClient.GetCatalogAsync(Uri baseUri, CancellationToken cancellationToken = default)`.
- Explicit-URI asynchronous Create/Update/Delete group and device methods using the Stage 5B-1 request DTOs.

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

Execution steps:

- [ ] Step 1: Write failing tests.

  ```csharp
  [Fact]
  public async Task ConflictResponse_MapsCodeAndRevision()
  {
      var handler = new RecordingHttpMessageHandler(HttpStatusCode.Conflict, "{\"code\":\"GROUP_REVISION_CONFLICT\",\"currentRevision\":7}");
      var client = new CatalogApiClient(new HttpClient(handler));

      var error = await Assert.ThrowsAsync<CatalogApiException>(() =>
          client.UpdateGroupAsync(new Uri("https://server-b/"), Guid.NewGuid(), ValidUpdateRequest()));

      Assert.Equal("GROUP_REVISION_CONFLICT", error.Code);
      Assert.Equal(7, error.CurrentRevision);
      Assert.Equal(1, handler.RequestCount);
  }

  [Fact]
  public async Task WriteFailure_IsNotRetried()
  {
      var handler = new RecordingHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "{}");
      var client = new CatalogApiClient(new HttpClient(handler));

      await Assert.ThrowsAsync<CatalogApiException>(() =>
          client.DeleteDeviceAsync(new Uri("https://server-b/"), Guid.NewGuid(), 3));

      Assert.Equal(1, handler.RequestCount);
  }

  private static UpdateGroupRequest ValidUpdateRequest() =>
      new("Group", null, 0, true, MonitorGroupType.Chute, 6);

  private sealed class RecordingHttpMessageHandler : HttpMessageHandler
  {
      private readonly HttpStatusCode statusCode;
      private readonly string body;
      public int RequestCount { get; private set; }

      public RecordingHttpMessageHandler(HttpStatusCode statusCode, string body)
      {
          this.statusCode = statusCode;
          this.body = body;
      }

      protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
      {
          RequestCount++;
          return Task.FromResult(new HttpResponseMessage(statusCode)
          {
              Content = new StringContent(body, Encoding.UTF8, "application/json")
          });
      }
  }
  ```

  Add the same handler fixture for ready 200, Catalog Kind deserialization, malformed error bodies, transport exceptions, and zero password-unprotect calls on Catalog GET.
- [ ] Step 2: Run RED.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~CatalogApiClientTests
  ```

  Confirm failure because the HTTP client and safe exception mapping do not exist.
- [ ] Step 3: Write the minimal implementation.

  ```csharp
  public sealed class CatalogApiClient
  {
      private readonly HttpClient httpClient;

      public CatalogApiClient(HttpClient httpClient) => this.httpClient = httpClient;

      public async Task<CatalogSnapshotDto> GetCatalogAsync(Uri baseUri, CancellationToken cancellationToken = default)
      {
          using var response = await httpClient.GetAsync(new Uri(baseUri, "/api/v1/catalog"), cancellationToken);
          response.EnsureSuccessStatusCode();
          return await response.Content.ReadFromJsonAsync<CatalogSnapshotDto>(cancellationToken: cancellationToken)
              ?? throw new CatalogApiException("CATALOG_UNAVAILABLE");
      }

      private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
      {
          using var response = await httpClient.SendAsync(request, cancellationToken);
          if (!response.IsSuccessStatusCode)
              throw await CatalogApiException.FromResponseAsync(response, cancellationToken);
          return (await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken))!;
      }
  }
  ```

  Build each write request with its explicit URI, map only approved safe error fields, and never include response bodies, credentials, or retry logic in exceptions.
- [ ] Step 4: Run focused GREEN.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~CatalogApiClientTests
  ```

  Confirm PASS.
- [ ] Step 5: Run the affected suite/build.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj
  dotnet build .\VideoMonitor.sln -c Debug
  ```
- [ ] Step 6: Review the diff.

  ```powershell
  git diff --check
  git status --short
  ```
- [ ] Step 7: Commit the exact Task 4 paths.

  ```powershell
  git add src/VideoMonitor.Wpf/Catalog/CatalogApiException.cs src/VideoMonitor.Wpf/Catalog/CatalogApiClient.cs tests/VideoMonitor.Core.Tests/Catalog/CatalogApiClientTests.cs
  git commit -m "feat: add central catalog api client"
  ```
- [ ] Step 8: STOP and return the branch, SHA, RED evidence, GREEN result, affected-suite result, and `git status --short` for Sol review.

## Task 5 — Password-Safe Read Model, Cache, Dispatcher, and Legacy Adapter

Files:

- Create `src/VideoMonitor.Core/Catalog/IDeviceCatalogReadModel.cs`
- Create `src/VideoMonitor.Wpf/Catalog/IUiDispatcher.cs`
- Create `src/VideoMonitor.Wpf/Catalog/WpfUiDispatcher.cs`
- Create `src/VideoMonitor.Wpf/Catalog/ClientCatalogCache.cs`
- Create `src/VideoMonitor.Wpf/Catalog/LegacyDeviceCatalogReadModel.cs`
- Create tests: `tests/VideoMonitor.Core.Tests/Catalog/ClientCatalogCacheTests.cs`
- Create tests: `tests/VideoMonitor.Core.Tests/Catalog/LegacyDeviceCatalogReadModelTests.cs`

Interfaces:

Consumes:

- `CatalogSnapshotDto`, `DeviceGroupDto`, `CameraDeviceDto`, and legacy `IDeviceCatalog`.
- `IUiDispatcher.InvokeAsync(Action action, CancellationToken cancellationToken = default)`.
- `IDeviceCatalog.GetGroups(): IReadOnlyList<DeviceGroup>`, `GetDevices(Guid groupId): IReadOnlyList<CameraDevice>`, `GetDevice(Guid deviceId): CameraDevice?`, `Changed`, and the existing Add/Update/Delete methods for the explicit local compatibility mode.

Produces:

- `IDeviceCatalogReadModel.GetGroups()`, `GetDevices(Guid groupId)`, `GetDevice(Guid deviceId)`, and `Changed`.
- `ClientCatalogCache` with `CatalogSnapshotDto Snapshot` and `Task ReplaceAsync(CatalogSnapshotDto snapshot, CancellationToken cancellationToken = default)`.
- `LegacyDeviceCatalogReadModel : IDeviceCatalogReadModel`, with password mapped only to `HasPassword`.

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

`ClientCatalogCache` stores only a complete `CatalogSnapshotDto` or equivalent password-safe snapshot. It never stores Core `CameraDevice`, `Password`, or `PasswordCiphertext`. Replacement prepares and validates a complete snapshot before dispatch, then performs compare, authoritative reference swap, and `Changed` publication entirely inside `IUiDispatcher`; the background caller never publishes or swaps the authoritative snapshot. `ApplyPreparedSnapshotOnUiThread` performs no I/O, network, asynchronous work, or cancellation check. `GetGroups`, `GetDevices(Guid)`, and `GetDevice(Guid)` use Guid identity. The legacy adapter is restricted to `SingleCameraTest` and maps password to `HasPassword` inside the adapter without exposing it.

Tests cover atomic replacement, no notification for identical snapshots, Guid lookup, dispatcher publication, reflection absence of sensitive DTO properties, and legacy `HasPassword` mapping.

Commit: `feat: add password safe client catalog cache`

Execution steps:

- [ ] Step 1: Write failing tests.

  ```csharp
  [Fact]
  public async Task IdenticalSnapshot_DoesNotRaiseChanged()
  {
      var dispatcher = new InlineUiDispatcher();
      var cache = new ClientCatalogCache(EmptySnapshot(), dispatcher);
      var changed = 0;
      cache.Changed += (_, _) => changed++;

      await cache.ReplaceAsync(EmptySnapshot());

      Assert.Equal(0, changed);
  }

  [Fact]
  public async Task ChangedHandler_SeesSnapshotOnlyAfterDispatcherCommit()
  {
      var dispatcher = new CapturingUiDispatcher();
      var initial = EmptySnapshot();
      var next = SnapshotWithOneGroup();
      var cache = new ClientCatalogCache(initial, dispatcher);
      CatalogSnapshotDto? observed = null;
      cache.Changed += (_, _) => observed = cache.Snapshot;

      await cache.ReplaceAsync(next);

      Assert.Same(initial, cache.Snapshot);
      dispatcher.RunPending();
      Assert.Same(next, cache.Snapshot);
      Assert.Same(next, observed);
  }

  [Fact]
  public async Task CacheType_DoesNotExposePasswordProperties()
  {
      var names = typeof(ClientCatalogCache).GetProperties()
          .Select(property => property.Name)
          .ToArray();

      Assert.DoesNotContain("Password", names);
      Assert.DoesNotContain("PasswordCiphertext", names);
  }

  private static CatalogSnapshotDto EmptySnapshot() => new(Array.Empty<DeviceGroupDto>(), Array.Empty<CameraDeviceDto>());

  private sealed class InlineUiDispatcher : IUiDispatcher
  {
      public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
      {
          action();
          return Task.CompletedTask;
      }
  }

  private sealed class CapturingUiDispatcher : IUiDispatcher
  {
      private Action? pending;
      public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
      {
          pending = action;
          return Task.CompletedTask;
      }
      public void RunPending() => (pending ?? throw new InvalidOperationException()).Invoke();
  }

  private static CatalogSnapshotDto SnapshotWithOneGroup() =>
      new([new DeviceGroupDto(Guid.NewGuid(), "Root", null, 0, true, MonitorGroupType.Chute, 1)], []);
  ```

  Add a Guid lookup test and a legacy-adapter test proving a non-empty local password becomes only `HasPassword = true` in the read model.
- [ ] Step 2: Run RED.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~ClientCatalogCacheTests|FullyQualifiedName~LegacyDeviceCatalogReadModelTests"
  ```

  Confirm failure because the password-safe read model and cache are absent.
- [ ] Step 3: Write the minimal implementation.

  ```csharp
  public sealed class ClientCatalogCache : IDeviceCatalogReadModel
  {
      private readonly IUiDispatcher dispatcher;
      private CatalogSnapshotDto snapshot;

      public ClientCatalogCache(CatalogSnapshotDto initial, IUiDispatcher dispatcher)
      {
          snapshot = initial;
          this.dispatcher = dispatcher;
      }

      public CatalogSnapshotDto Snapshot => snapshot;
      public event EventHandler? Changed;

      public Task ReplaceAsync(CatalogSnapshotDto next, CancellationToken cancellationToken = default) =>
          dispatcher.InvokeAsync(() => ApplyPreparedSnapshotOnUiThread(next), cancellationToken);

      internal bool ApplyPreparedSnapshotOnUiThread(CatalogSnapshotDto next)
      {
          if (SnapshotsEqual(snapshot, next)) return false;
          snapshot = next;
          Changed?.Invoke(this, EventArgs.Empty);
          return true;
      }

      public IReadOnlyList<DeviceGroupDto> GetGroups() => Snapshot.Groups;
      public IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId) => Snapshot.Devices.Where(device => device.GroupId == groupId).ToArray();
      public CameraDeviceDto? GetDevice(Guid deviceId) => Snapshot.Devices.SingleOrDefault(device => device.Id == deviceId);

      private static bool SnapshotsEqual(CatalogSnapshotDto left, CatalogSnapshotDto right) =>
          left.Groups.SequenceEqual(right.Groups) && left.Devices.SequenceEqual(right.Devices);
  }
  ```

  Compare complete DTO snapshots by value, perform the compare/swap/publication in the dispatcher action, and keep legacy password mapping inside the adapter.
- [ ] Step 4: Run focused GREEN.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~ClientCatalogCacheTests|FullyQualifiedName~LegacyDeviceCatalogReadModelTests"
  ```

  Confirm PASS.
- [ ] Step 5: Run the affected suite/build.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj
  dotnet build .\VideoMonitor.sln -c Debug
  ```
- [ ] Step 6: Review the diff.

  ```powershell
  git diff --check
  git status --short
  ```
- [ ] Step 7: Commit the exact Task 5 paths.

  ```powershell
  git add src/VideoMonitor.Core/Catalog/IDeviceCatalogReadModel.cs src/VideoMonitor.Wpf/Catalog/IUiDispatcher.cs src/VideoMonitor.Wpf/Catalog/WpfUiDispatcher.cs src/VideoMonitor.Wpf/Catalog/ClientCatalogCache.cs src/VideoMonitor.Wpf/Catalog/LegacyDeviceCatalogReadModel.cs tests/VideoMonitor.Core.Tests/Catalog/ClientCatalogCacheTests.cs tests/VideoMonitor.Core.Tests/Catalog/LegacyDeviceCatalogReadModelTests.cs
  git commit -m "feat: add password safe client catalog cache"
  ```
- [ ] Step 8: STOP and return the branch, SHA, RED evidence, GREEN result, affected-suite result, and `git status --short` for Sol review.

## Task 6 — ServerConnectionCoordinator

Files:

- Create `src/VideoMonitor.Wpf/Catalog/ServerConnectionState.cs`
- Create `src/VideoMonitor.Wpf/Catalog/IClientConnectionClock.cs`
- Create `src/VideoMonitor.Wpf/Catalog/SystemClientConnectionClock.cs`
- Create `src/VideoMonitor.Wpf/Catalog/ServerConnectionCoordinator.cs`
- Add only a minimal internal client seam to `CatalogApiClient.cs` if deterministic tests require it; do not add a second transport implementation
- Create tests: `tests/VideoMonitor.Core.Tests/Catalog/ServerConnectionCoordinatorTests.cs`

Interfaces:

Consumes:

- `IClientSettingsStore`, `CatalogApiClient` through the one internal `ICatalogConnectionClient` test seam, `ClientCatalogCache`, `IUiDispatcher`, `IClientConnectionClock`, and `Func<bool> hasUnsavedDraft`.
- `ClientSettings IClientSettingsStore.Load()` and `Task IClientSettingsStore.SaveAsync(ClientSettings settings, CancellationToken cancellationToken = default)`.
- `Task CatalogApiClient.CheckReadyAsync(Uri baseUri, CancellationToken cancellationToken = default)` and `Task<CatalogSnapshotDto> CatalogApiClient.GetCatalogAsync(Uri baseUri, CancellationToken cancellationToken = default)`.
- `CatalogSnapshotDto ClientCatalogCache.Snapshot` and `Task ClientCatalogCache.ReplaceAsync(CatalogSnapshotDto snapshot, CancellationToken cancellationToken = default)`.

Produces:

- `ServerConnectionState` values `Unconfigured`, `Connecting`, `Connected`, and `Unavailable`.
- `ServerConnectionStatus(Uri? BaseUri, ServerConnectionState State, DateTimeOffset? LastSuccessfulSyncUtc, bool IsStale)`.
- `ICatalogConnectionClient` with `Task CheckReadyAsync(Uri baseUri, CancellationToken cancellationToken = default)` and `Task<CatalogSnapshotDto> GetCatalogAsync(Uri baseUri, CancellationToken cancellationToken = default)`; production `CatalogApiClient` implements it and tests may provide one fake.
- `ServerConnectionCoordinator(IClientSettingsStore settingsStore, ICatalogConnectionClient apiClient, ClientCatalogCache cache, IUiDispatcher uiDispatcher, IClientConnectionClock clock)` plus `Status`, `StatusChanged`, `RunAsync(CancellationToken)`, `RefreshNowAsync(CancellationToken)`, `ProbeAsync(Uri, CancellationToken)`, and `SwitchServerAsync(Uri, Func<bool>, CancellationToken)`.

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

Use one process loop, a `SemaphoreSlim` single-flight refresh gate, and shutdown cancellation. `NextJitterUnit()` returns `0.0 <= value < 1.0`; every delay uses the single formula `jitteredDelay = baseDelay * (0.8 + 0.4 * NextJitterUnit())`. Reconnect bases are `2s -> 5s -> 10s -> 15s -> 15s`; the production Catalog refresh base is fixed at 30 seconds, producing `24s <= delay < 36s`. The fake clock/jitter makes these values deterministic in tests. A failed first connection leaves an empty cache; a post-success failure preserves the stale cache and marks `Unavailable`; reconnect requires a complete Catalog refresh. Unchanged snapshots do not notify.

Switching probes B with readiness and Catalog GET without changing A. Probe and Draft checks may honor the caller cancellation token. Immediately before durable settings persistence, call `cancellationToken.ThrowIfCancellationRequested()`. The successful atomic settings write of B is the Server switch commit point. After that point, do not accept the caller token: use `CancellationToken.None` for the Dispatcher commit, perform no network request, no additional disk write, and no new business validation. The final commit only sets Configured BaseUri B, ClientCatalogCache snapshot B, `Connected`, `LastSuccessfulSyncUtc = clock.UtcNow`, and `IsStale = false`. A settings-save failure before the commit point leaves A BaseUri, cache, and state unchanged. Once B is accepted, later B failure reconnects to B and never silently returns to A.

Deterministic tests cover no configuration, first connect, stale mode, reconnect, retry schedule, jitter, periodic refresh, single-flight behavior, failed B probe, settings failure, and Draft blocking. A successful-switch test runs the cache `Changed` handler and asserts that it observes the committed Server B `BaseUri` and `Connected` status, never disk B with memory A.

Commit: `feat: add central server connection coordinator`

Execution steps:

- [ ] Step 1: Write failing tests.

  ```csharp
  [Fact]
  public async Task FailedServerSwitch_KeepsServerA()
  {
      var fixture = await ConnectionFixture.ConnectedToAsync("https://server-a");
      fixture.Api.ProbeResult = false;

      await Assert.ThrowsAsync<CatalogApiException>(() =>
          fixture.Coordinator.SwitchServerAsync(new Uri("https://server-b"), () => false));

      Assert.Equal(new Uri("https://server-a"), fixture.Coordinator.Status.BaseUri);
      Assert.Equal(ServerConnectionState.Connected, fixture.Coordinator.Status.State);
  }

  [Fact]
  public async Task SuccessfulSwitch_PersistsBeforeFinalStateChange()
  {
      var fixture = await ConnectionFixture.ConnectedToAsync("https://server-a");
      fixture.Settings.SaveResult = Task.CompletedTask;

      await fixture.Coordinator.SwitchServerAsync(new Uri("https://server-b"), () => false);

      Assert.Equal(new Uri("https://server-b"), fixture.Coordinator.Status.BaseUri);
      Assert.Equal(1, fixture.Settings.SaveCount);
  }

  [Fact]
  public async Task ChangedHandler_SeesServerBStateDuringSuccessfulSwitch()
  {
      var fixture = await ConnectionFixture.ConnectedToAsync("https://server-a");
      fixture.Api.Snapshot = new CatalogSnapshotDto(
          [new DeviceGroupDto(Guid.NewGuid(), "Root B", null, 0, true, MonitorGroupType.Chute, 1)], []);
      ServerConnectionStatus? observedStatus = null;
      fixture.Cache.Changed += (_, _) => observedStatus = fixture.Coordinator.Status;

      await fixture.Coordinator.SwitchServerAsync(new Uri("https://server-b"), () => false);

      Assert.Equal(new Uri("https://server-b"), observedStatus!.BaseUri);
      Assert.Equal(ServerConnectionState.Connected, observedStatus.State);
  }

  [Fact]
  public async Task StatusChangedHandler_SeesServerBSnapshotDuringSuccessfulSwitch()
  {
      var fixture = await ConnectionFixture.ConnectedToAsync("https://server-a");
      fixture.Api.Snapshot = new CatalogSnapshotDto(
          [new DeviceGroupDto(Guid.NewGuid(), "Root B", null, 0, true, MonitorGroupType.Chute, 1)], []);
      CatalogSnapshotDto? observedSnapshot = null;
      fixture.Coordinator.StatusChanged += (_, _) =>
          observedSnapshot = fixture.Cache.Snapshot;

      await fixture.Coordinator.SwitchServerAsync(
          new Uri("https://server-b"),
          () => false);

      Assert.Same(fixture.Api.Snapshot, observedSnapshot);
      Assert.Equal(
          new Uri("https://server-b"),
          fixture.Coordinator.Status.BaseUri);
  }

  [Fact]
  public async Task RetryDelay_UsesBoundedDeterministicJitter()
  {
      var zero = new FakeConnectionClock(0.0);
      Assert.Equal(TimeSpan.FromSeconds(4), zero.Jitter(TimeSpan.FromSeconds(5)));
      Assert.Equal(TimeSpan.FromSeconds(24), zero.Jitter(TimeSpan.FromSeconds(30)));

      var half = new FakeConnectionClock(0.5);
      Assert.Equal(TimeSpan.FromSeconds(5), half.Jitter(TimeSpan.FromSeconds(5)));
      Assert.Equal(TimeSpan.FromSeconds(30), half.Jitter(TimeSpan.FromSeconds(30)));

      var nearOne = new FakeConnectionClock(0.999999);
      Assert.True(nearOne.Jitter(TimeSpan.FromSeconds(5)) < TimeSpan.FromSeconds(6));
      Assert.True(nearOne.Jitter(TimeSpan.FromSeconds(30)) < TimeSpan.FromSeconds(36));
  }

  private sealed class ConnectionFixture
  {
      private ConnectionFixture()
      {
          Api = new FakeCatalogApi();
          Settings = new FakeClientSettingsStore();
          Clock = new FakeConnectionClock(0.5);
          Cache = new ClientCatalogCache(new CatalogSnapshotDto([], []), new InlineDispatcher());
          Coordinator = new ServerConnectionCoordinator(Settings, Api, Cache, new InlineDispatcher(), Clock);
      }

      public ServerConnectionCoordinator Coordinator { get; }
      public FakeCatalogApi Api { get; }
      public FakeClientSettingsStore Settings { get; }
      public FakeConnectionClock Clock { get; }
      public ClientCatalogCache Cache { get; }

      public static async Task<ConnectionFixture> ConnectedToAsync(string baseUrl)
      {
          var fixture = new ConnectionFixture();
          await fixture.Coordinator.SwitchServerAsync(new Uri(baseUrl), () => false);
          return fixture;
      }
  }

  private sealed class FakeCatalogApi : ICatalogConnectionClient
  {
      public bool ProbeResult { get; set; } = true;
      public CatalogSnapshotDto Snapshot { get; set; } = new([], []);

      public Task CheckReadyAsync(Uri baseUri, CancellationToken cancellationToken = default) =>
          ProbeResult
              ? Task.CompletedTask
              : Task.FromException(new CatalogApiException("CATALOG_UNAVAILABLE"));

      public Task<CatalogSnapshotDto> GetCatalogAsync(Uri baseUri, CancellationToken cancellationToken = default) =>
          ProbeResult
              ? Task.FromResult(Snapshot)
              : Task.FromException<CatalogSnapshotDto>(new CatalogApiException("CATALOG_UNAVAILABLE"));
  }

  private sealed class FakeClientSettingsStore : IClientSettingsStore
  {
      public int SaveCount { get; private set; }
      public Task SaveResult { get; set; } = Task.CompletedTask;
      public ClientSettings Load() => ClientSettings.Empty;
      public async Task SaveAsync(ClientSettings settings, CancellationToken cancellationToken = default)
      {
          SaveCount++;
          await SaveResult.ConfigureAwait(false);
      }
  }

  private sealed class FakeConnectionClock : IClientConnectionClock
  {
      private readonly double jitterUnit;
      public FakeConnectionClock(double jitterUnit) => this.jitterUnit = jitterUnit;
      public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-08-31T00:00:00Z");
      public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
      public double NextJitterUnit() => jitterUnit;
      public TimeSpan Jitter(TimeSpan baseDelay) => baseDelay * (0.8 + 0.4 * jitterUnit);
  }

  private sealed class InlineDispatcher : IUiDispatcher
  {
      public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
      {
          action();
          return Task.CompletedTask;
      }
  }
  ```

  Add tests for stale-cache preservation, 2/5/10/15/15 reconnect bases, 30-second periodic refresh, single-flight refresh, and Draft blocking.
- [ ] Step 2: Run RED.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~ServerConnectionCoordinatorTests
  ```

  Confirm failure because the coordinator and deterministic clock seam are absent.
- [ ] Step 3: Write the minimal implementation.

  ```csharp
  public sealed class ServerConnectionCoordinator : IAsyncDisposable
  {
      private readonly SemaphoreSlim refreshGate = new(1, 1);
      private readonly CancellationTokenSource shutdown = new();
      private readonly ICatalogConnectionClient apiClient;
      private readonly ClientCatalogCache cache;
      private readonly IClientSettingsStore settingsStore;
      private readonly IUiDispatcher uiDispatcher;
      private readonly IClientConnectionClock clock;
      private Uri? configuredBaseUri;

      public ServerConnectionStatus Status { get; private set; } =
          new(null, ServerConnectionState.Unconfigured, null, true);

      public Task RunAsync(CancellationToken cancellationToken) => RunLoopAsync(cancellationToken);
      public Task ProbeAsync(Uri baseUri, CancellationToken cancellationToken = default) => ProbeReadyAndCatalogAsync(baseUri, cancellationToken);

      public async Task SwitchServerAsync(Uri candidate, Func<bool> hasUnsavedDraft, CancellationToken cancellationToken = default)
      {
          var preparedSnapshot = await ProbeAndPrepareAsync(candidate, cancellationToken).ConfigureAwait(false);
          if (hasUnsavedDraft()) throw new InvalidOperationException("Unsaved Catalog edits block a Server switch.");
          cancellationToken.ThrowIfCancellationRequested();
          await settingsStore.SaveAsync(new ClientSettings(new ClientServerSettings(candidate.ToString())), cancellationToken).ConfigureAwait(false);

          await uiDispatcher.InvokeAsync(() =>
          {
              configuredBaseUri = candidate;
              Status = new ServerConnectionStatus(candidate, ServerConnectionState.Connected, clock.UtcNow, false);
              cache.ApplyPreparedSnapshotOnUiThread(preparedSnapshot);
              StatusChanged?.Invoke(this, EventArgs.Empty);
          }, CancellationToken.None).ConfigureAwait(false);
      }

      public async Task RefreshNowAsync(CancellationToken cancellationToken = default)
      {
          if (!await refreshGate.WaitAsync(0, cancellationToken)) return;
          try
          {
              var snapshot = await apiClient.GetCatalogAsync(configuredBaseUri!, cancellationToken).ConfigureAwait(false);
              await cache.ReplaceAsync(snapshot, cancellationToken).ConfigureAwait(false);
          }
          finally { refreshGate.Release(); }
      }

      public async ValueTask DisposeAsync()
      {
          shutdown.Cancel();
          refreshGate.Dispose();
          shutdown.Dispose();
          await Task.CompletedTask.ConfigureAwait(false);
      }
  }
  ```

  Add the single process loop, the specified backoff/jitter, and the settings-write commit point around this seam; no overlapping refresh or automatic write retry.
- [ ] Step 4: Run focused GREEN.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~ServerConnectionCoordinatorTests
  ```

  Confirm PASS.
- [ ] Step 5: Run the affected suite/build.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj
  dotnet build .\VideoMonitor.sln -c Debug
  ```
- [ ] Step 6: Review the diff.

  ```powershell
  git diff --check
  git status --short
  ```
- [ ] Step 7: Commit the exact Task 6 paths.

  ```powershell
  git add src/VideoMonitor.Wpf/Catalog/ServerConnectionState.cs src/VideoMonitor.Wpf/Catalog/IClientConnectionClock.cs src/VideoMonitor.Wpf/Catalog/SystemClientConnectionClock.cs src/VideoMonitor.Wpf/Catalog/ServerConnectionCoordinator.cs src/VideoMonitor.Wpf/Catalog/CatalogApiClient.cs tests/VideoMonitor.Core.Tests/Catalog/ServerConnectionCoordinatorTests.cs
  git commit -m "feat: add central server connection coordinator"
  ```
- [ ] Step 8: STOP and return the branch, SHA, RED evidence, GREEN result, affected-suite result, and `git status --short` for Sol review.

## Task 7 — Monitor Projection and Fixed Nullable 4+3

Files:

- Modify `src/VideoMonitor.Core/Models/MonitorGroup.cs`
- Modify `src/VideoMonitor.Core/Services/MonitorCatalogProjection.cs`
- Modify `src/VideoMonitor.Core/Services/MonitorLayoutSnapshot.cs`
- Modify `src/VideoMonitor.Core/Services/MonitorSwitchService.cs`
- Modify tests: `tests/VideoMonitor.Core.Tests/Services/MonitorCatalogProjectionTests.cs`
- Modify tests: `tests/VideoMonitor.Core.Tests/Services/MonitorSwitchServiceTests.cs`

Interfaces:

Consumes:

- `IDeviceCatalogReadModel.GetGroups()`, `GetDevices(Guid)`, `GetDevice(Guid)`, and `Changed`.
- `DeviceGroupDto` with nullable `MonitorGroupType? Kind` and direct `ParentId` hierarchy.
- Existing `MonitorGroup(string Name, MonitorGroupType Type, IReadOnlyList<CameraInfo> Cameras)` and `MonitorSwitchService(MonitorGroup defaultChuteGroup, MonitorGroup defaultTunnelGroup, MonitorGroup defaultUnloadingGroup)` behavior, which this Task extends without changing the 3+1 business rule.

Produces:

- `MonitorGroup(string Name, MonitorGroupType Type, IReadOnlyList<CameraInfo> Cameras)` with `GroupId`, `RootGroupId`, `RootName`, `RootSort`, and `Sort` metadata.
- `MonitorCatalogProjection.CreateGroups(IDeviceCatalogReadModel catalog)`.
- `MonitorLayoutSnapshot(IReadOnlyList<CameraInfo?> MainSlots, IReadOnlyList<CameraInfo?> SecondarySlots)`.
- `void MonitorSwitchService.ReplaceGroups(IReadOnlyList<MonitorGroup>)`, `void SwitchChuteGroup(Guid)`, `void SwitchTunnelGroup(Guid)`, and `void SwitchUnloadingGroup(Guid)`.

`MonitorGroup` contains `GroupId`, `RootGroupId`, `RootName`, `RootSort`, and `Sort`. `MonitorLayoutSnapshot` contains `IReadOnlyList<CameraInfo?> MainSlots` and `IReadOnlyList<CameraInfo?> SecondarySlots`.

`MonitorCatalogProjection.CreateGroups(IDeviceCatalogReadModel catalog)` accepts only the central read model in formal mode. It includes enabled Roots with non-null Kind and direct enabled Children with direct Root parents. Malformed or missing parents are excluded without throwing. Only enabled devices/channels are projected. Central `CameraInfo.Status` starts as `Unknown`; no Chinese name-to-type mapping is allowed.

`MonitorSwitchService` accepts `IReadOnlyList<MonitorGroup>`, exposes `ReplaceGroups`, `SwitchChuteGroup(Guid)`, `SwitchTunnelGroup(Guid)`, and `SwitchUnloadingGroup(Guid)`, and orders defaults by Root.Sort, Child.Sort, GroupId. It preserves a selected Guid when valid and rejects wrong-kind Guids without name lookup.

Main always maps four slots as Chute 0, Chute 1, Chute 2, Tunnel 0. Secondary always has three UnloadingStation slots. Missing entries are `null`.

Tests cover empty data, one/two Chute groups, Tunnel, UnloadingStation, same-Kind Roots, duplicate names, deterministic ordering, deleted/disabled selection fallback, and wrong-kind identity.

Commit: `feat: make monitor layout catalog tolerant`

Execution steps:

- [ ] Step 1: Write failing tests.

  ```csharp
  [Fact]
  public void EmptyCatalog_ProducesFourMainAndThreeSecondaryNullSlots()
  {
      var groups = MonitorCatalogProjection.CreateGroups(new ReadModelStub());
      var layout = new MonitorSwitchService(groups).CurrentLayout;

      Assert.Equal(4, layout.MainSlots.Count);
      Assert.Equal(3, layout.SecondarySlots.Count);
      Assert.All(layout.MainSlots, slot => Assert.Null(slot));
      Assert.All(layout.SecondarySlots, slot => Assert.Null(slot));
  }

  [Fact]
  public void DuplicateChildNames_AreSelectedByGuid()
  {
      var first = new MonitorGroup("401", MonitorGroupType.Chute, Array.Empty<CameraInfo>()) { GroupId = Guid.NewGuid() };
      var second = first with { GroupId = Guid.NewGuid() };
      var service = new MonitorSwitchService(new[] { first, second });

      service.SwitchChuteGroup(second.GroupId);

      Assert.Equal(second.GroupId, service.SelectedChuteGroupId);
  }

  private sealed class ReadModelStub : IDeviceCatalogReadModel
  {
      public event EventHandler? Changed;
      public IReadOnlyList<DeviceGroupDto> GetGroups() => Array.Empty<DeviceGroupDto>();
      public IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId) => Array.Empty<CameraDeviceDto>();
      public CameraDeviceDto? GetDevice(Guid deviceId) => null;
  }
  ```

  Add cases for deterministic Root/Child ordering, deleted/disabled fallback, same-Kind Roots, and wrong-kind Guid rejection.
- [ ] Step 2: Run RED.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~MonitorCatalogProjectionTests|FullyQualifiedName~MonitorSwitchServiceTests"
  ```

  Confirm failure because nullable layout slots and central read-model projection are not implemented.
- [ ] Step 3: Write the minimal implementation.

  ```csharp
  public sealed record MonitorLayoutSnapshot(
      IReadOnlyList<CameraInfo?> MainSlots,
      IReadOnlyList<CameraInfo?> SecondarySlots);

  public static IReadOnlyList<MonitorGroup> CreateGroups(IDeviceCatalogReadModel catalog)
  {
      var groups = catalog.GetGroups();
      var roots = groups.Where(group => group.ParentId is null && group.Enabled && group.Kind is not null);
      return roots.SelectMany(root => groups
          .Where(child => child.ParentId == root.Id && child.Enabled)
          .OrderBy(child => child.Sort)
          .ThenBy(child => child.Id)
          .Select(child => ToMonitorGroup(root, child, catalog)))
          .OrderBy(group => group.RootSort)
          .ThenBy(group => group.Sort)
          .ThenBy(group => group.GroupId)
          .ToArray();
  }
  ```

  Preserve the existing `MonitorSwitchService` 3+1 behavior while making all group and slot selection Guid based.
- [ ] Step 4: Run focused GREEN.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~MonitorCatalogProjectionTests|FullyQualifiedName~MonitorSwitchServiceTests"
  ```

  Confirm PASS.
- [ ] Step 5: Run the affected suite/build.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj
  dotnet build .\VideoMonitor.sln -c Debug
  ```
- [ ] Step 6: Review the diff.

  ```powershell
  git diff --check
  git status --short
  ```
- [ ] Step 7: Commit the exact Task 7 paths.

  ```powershell
  git add src/VideoMonitor.Core/Models/MonitorGroup.cs src/VideoMonitor.Core/Services/MonitorCatalogProjection.cs src/VideoMonitor.Core/Services/MonitorLayoutSnapshot.cs src/VideoMonitor.Core/Services/MonitorSwitchService.cs tests/VideoMonitor.Core.Tests/Services/MonitorCatalogProjectionTests.cs tests/VideoMonitor.Core.Tests/Services/MonitorSwitchServiceTests.cs
  git commit -m "feat: make monitor layout catalog tolerant"
  ```
- [ ] Step 8: STOP and return the branch, SHA, RED evidence, GREEN result, affected-suite result, and `git status --short` for Sol review.

## Task 8 — Monitor, Secondary, and VideoTile DTO Refactor

Files:

- Modify `src/VideoMonitor.Wpf/ViewModels/MonitorViewModel.cs`
- Modify `src/VideoMonitor.Wpf/ViewModels/SecondaryMonitorViewModel.cs`
- Modify `src/VideoMonitor.Wpf/ViewModels/VideoTileViewModel.cs`
- Modify `src/VideoMonitor.Wpf/ViewModels/MonitorTreeItemViewModel.cs` only if required
- Modify `src/VideoMonitor.Wpf/Views/SecondaryMonitorWindow.xaml`
- Modify tests: `tests/VideoMonitor.Core.Tests/ViewModels/MonitorCatalogRefreshTests.cs`
- Modify tests: `tests/VideoMonitor.Core.Tests/ViewModels/MonitorUiStateTests.cs`
- Create tests: `tests/VideoMonitor.Core.Tests/ViewModels/SecondaryMonitorCatalogTests.cs`

Interfaces:

Consumes:

- `IDeviceCatalogReadModel`, `MonitorCatalogProjection.CreateGroups(IDeviceCatalogReadModel catalog)`, `MonitorLayoutSnapshot` nullable slots, and runtime `CameraStatus`.
- Existing `MonitorViewModel`, `SecondaryMonitorViewModel`, and `VideoTileViewModel` bindings that will be redirected.
- `IReadOnlyList<CameraInfo?> MonitorLayoutSnapshot.MainSlots` and `IReadOnlyList<CameraInfo?> MonitorLayoutSnapshot.SecondarySlots` produced by Task 7.

Produces:

- `VideoTileViewModel.Update(CameraInfo, CameraDeviceDto?, CameraChannelDto?, CameraStatus)` and `ResetUnconfigured()`.
- `MonitorViewModel` and `SecondaryMonitorViewModel` subscriptions to `IDeviceCatalogReadModel.Changed` with Guid selection preservation, and `VideoTileViewModel.Update(CameraInfo, CameraDeviceDto?, CameraChannelDto?, CameraStatus)`.
- `SecondaryMonitorWindow` dynamic group ItemsControl binding.

Formal `VideoTile` updates consume `CameraInfo`, `CameraDeviceDto?`, `CameraChannelDto?`, and runtime `CameraStatus`; they do not consume Core `CameraDevice`. Add `ResetUnconfigured()` so an empty slot displays `CameraName = "未配置"`, `Status = Unknown`, `IP = "--"`, stream = `"--"`, and the existing unconfigured visual state.

`MonitorViewModel` listens to `IDeviceCatalogReadModel.Changed`, re-projects groups, calls `MonitorSwitchService.ReplaceGroups`, rebuilds the tree while preserving selected Guids, and renders nullable slots. `SecondaryMonitorViewModel` uses dynamic UnloadingStation groups and Guid/object command parameters rather than fixed names. `SecondaryMonitorWindow.xaml` uses an `ItemsControl` for those groups.

Tests cover empty Catalogs, duplicate Child names, same-Kind Roots, selected-Guid preservation, delete fallback, null-slot reset, and dynamic Secondary groups.

Commit: `feat: bind monitor views to central catalog read model`

Execution steps:

- [ ] Step 1: Write failing tests.

  ```csharp
  [Fact]
  public void NullTile_ResetShowsUnconfiguredAndUnknown()
  {
      var tile = new VideoTileViewModel();

      tile.ResetUnconfigured();

      Assert.Equal("未配置", tile.CameraName);
      Assert.Equal(CameraStatus.Unknown, tile.Status);
      Assert.Equal("--", tile.IP);
  }

  [Fact]
  public void SecondaryDuplicateNames_SwitchesByGuid()
  {
      var first = new MonitorGroup("卸矿站", MonitorGroupType.UnloadingStation, Array.Empty<CameraInfo>()) { GroupId = Guid.NewGuid() };
      var second = first with { GroupId = Guid.NewGuid() };
      var viewModel = new SecondaryMonitorViewModel(new[] { first, second });

      viewModel.SelectGroupCommand.Execute(second.GroupId);

      Assert.Equal(second.GroupId, viewModel.SelectedGroupId);
  }
  ```

  Add a `VideoTileViewModel` constructor/factory in the test fixture only if the existing ViewModel requires services; the fixture must not construct a Core `CameraDevice` for the formal central path.
- [ ] Step 2: Run RED.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~MonitorCatalogRefreshTests|FullyQualifiedName~MonitorUiStateTests|FullyQualifiedName~SecondaryMonitorCatalogTests"
  ```

  Confirm failure because the ViewModels still require the old source shape or do not reset null slots.
- [ ] Step 3: Write the minimal implementation.

  ```csharp
  public void Update(CameraInfo info, CameraDeviceDto? device, CameraChannelDto? channel, CameraStatus status)
  {
      CameraName = info.Name;
      IP = device?.IpAddress ?? "--";
      Stream = channel?.StreamType.ToString() ?? "--";
      Status = status;
  }

  public void ResetUnconfigured()
  {
      CameraName = "未配置";
      IP = "--";
      Stream = "--";
      Status = CameraStatus.Unknown;
  }
  ```

  Change Monitor and Secondary bindings to consume the safe DTO read model and preserve Guid selections while keeping existing visual bindings.
- [ ] Step 4: Run focused GREEN.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~MonitorCatalogRefreshTests|FullyQualifiedName~MonitorUiStateTests|FullyQualifiedName~SecondaryMonitorCatalogTests"
  ```

  Confirm PASS.
- [ ] Step 5: Run the affected suite/build.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj
  dotnet build .\VideoMonitor.sln -c Debug
  ```
- [ ] Step 6: Review the diff.

  ```powershell
  git diff --check
  git status --short
  ```
- [ ] Step 7: Commit the exact Task 8 paths.

  ```powershell
  git add src/VideoMonitor.Wpf/ViewModels/MonitorViewModel.cs src/VideoMonitor.Wpf/ViewModels/SecondaryMonitorViewModel.cs src/VideoMonitor.Wpf/ViewModels/VideoTileViewModel.cs src/VideoMonitor.Wpf/ViewModels/MonitorTreeItemViewModel.cs src/VideoMonitor.Wpf/Views/SecondaryMonitorWindow.xaml tests/VideoMonitor.Core.Tests/ViewModels/MonitorCatalogRefreshTests.cs tests/VideoMonitor.Core.Tests/ViewModels/MonitorUiStateTests.cs tests/VideoMonitor.Core.Tests/ViewModels/SecondaryMonitorCatalogTests.cs
  git commit -m "feat: bind monitor views to central catalog read model"
  ```
- [ ] Step 8: STOP and return the branch, SHA, RED evidence, GREEN result, affected-suite result, and `git status --short` for Sol review.

## Task 9 — Async Command Service and Device Management Draft

Files:

- Create `src/VideoMonitor.Wpf/Catalog/IDeviceCatalogCommandService.cs`
- Create `src/VideoMonitor.Wpf/Catalog/CatalogMutationUncertainException.cs`
- Create `src/VideoMonitor.Wpf/Catalog/RemoteDeviceCatalogCommandService.cs`
- Create `src/VideoMonitor.Wpf/Catalog/LegacyDeviceCatalogCommandService.cs`
- Modify `src/VideoMonitor.Wpf/ViewModels/DeviceEditDraftViewModel.cs`
- Modify `src/VideoMonitor.Wpf/ViewModels/DeviceGroupTreeItemViewModel.cs`
- Modify `src/VideoMonitor.Wpf/ViewModels/DeviceManagementViewModel.cs`
- Create tests: `tests/VideoMonitor.Core.Tests/Catalog/DeviceCatalogCommandServiceTests.cs`
- Create tests: `tests/VideoMonitor.Core.Tests/ViewModels/DeviceManagementDraftTests.cs`

Interfaces:

Consumes:

- `IDeviceCatalogReadModel`, `CatalogApiClient`, `ServerConnectionCoordinator`, client settings connection state, and the existing DTO request records.
- `Task ServerConnectionCoordinator.RefreshNowAsync(CancellationToken cancellationToken = default)`, `Task ProbeAsync(Uri baseUri, CancellationToken cancellationToken = default)`, and `ServerConnectionStatus Status`.
- The explicit Stage 5B-1 DTO request records without exposing Core `CameraDevice` to the central cache.

Produces:

- `IDeviceCatalogCommandService` with `CanWrite`, `AvailabilityChanged`, `CreateGroupAsync(CreateGroupRequest, CancellationToken)`, `UpdateGroupAsync(Guid, UpdateGroupRequest, CancellationToken)`, `DeleteGroupAsync(Guid, long, CancellationToken)`, `CreateDeviceAsync(CreateDeviceRequest, CancellationToken)`, `UpdateDeviceAsync(Guid, UpdateDeviceRequest, CancellationToken)`, and `DeleteDeviceAsync(Guid, long, CancellationToken)`.
- `CatalogMutationUncertainException(string operation, Guid entityId, Exception? innerException = null)` with `Operation` and `EntityId`.
- `DeviceManagementViewModel(IDeviceCatalogReadModel catalog, IDeviceCatalogCommandService commands)` with `DeviceEditDraftViewModel EditDraft`, `IAsyncRelayCommand SaveDeviceCommand`, `bool HasUnsavedDraft`, `string? OperationErrorCode`, `bool HasOperationError`, and `bool LastOperationSucceeded`.
- `DeviceEditDraftViewModel` as pure local draft state; it does not hold `IDeviceCatalogCommandService` and does not expose `SaveAsync`.

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

The uncertainty type is:

```csharp
public sealed class CatalogMutationUncertainException : Exception
{
    public CatalogMutationUncertainException(
        string operation,
        Guid entityId,
        Exception? innerException = null)
        : base("The Catalog mutation result could not be confirmed.", innerException)
    {
        Operation = operation;
        EntityId = entityId;
    }

    public string Operation { get; }

    public Guid EntityId { get; }
}
```

The constructor message is fixed safe text and never includes a password, request body, or raw response. Device Management catches this type, keeps the Draft, and does not display success. Create/Delete are converted to success only after a refresh proves the known Guid committed; ambiguous Update always remains uncertain when GET cannot prove the mutation.

Remote commands use only the coordinator's current Connected BaseUri, issue one write, and then run a full refresh. They do not blindly retry. After an ambiguous Create/Delete timeout, a refresh checks known identity presence/absence. An ambiguous Update, especially one containing `NewPassword`, raises a safe uncertainty exception and retains the Draft because GET cannot prove a password write.

The legacy command adapter is async-shaped but only supports local `SingleCameraTest`. Device Management receives `IDeviceCatalogReadModel` and `IDeviceCatalogCommandService`, uses DTO collections and `AsyncRelayCommand`, and exposes `IsSaving`, `IsServerAvailable`, `OperationError`, `HasUnsavedDraft`, and `SaveDeviceCommand`. `DeviceEditDraftViewModel` remains pure local state. Add Child remains UI-only until Save. Cancel performs zero writes. Device edits preserve existing channel IDs and unedited channels; new IDs are generated before POST. Blank password maps to `NewPassword = null`; non-empty replaces it; old password is never shown. Offline and 409 states disable writes or preserve the Draft as appropriate.

Tests cover one-write behavior, timeout identity checks, uncertainty for password updates, Draft retention, 409 handling, and offline command availability.

Commit: `feat: make device management use async catalog drafts`

Execution steps:

- [ ] Step 1: Write failing tests.

  ```csharp
  [Fact]
  public async Task BlankPassword_MapsToNoPasswordChange()
  {
      var commands = new FakeCatalogCommandService();
      var readModel = new DeviceReadModelStub(ExistingDevice());
      var viewModel = new DeviceManagementViewModel(readModel, commands);
      viewModel.EditDraft.Password = "";

      await viewModel.SaveDeviceCommand.ExecuteAsync(null);

      Assert.Null(commands.LastUpdate!.NewPassword);
  }

  [Fact]
  public async Task Conflict_RetainsDraft()
  {
      var commands = new FakeCatalogCommandService { NextFailure = new CatalogApiException("DEVICE_REVISION_CONFLICT", 9) };
      var readModel = new DeviceReadModelStub(ExistingDevice());
      var viewModel = new DeviceManagementViewModel(readModel, commands);
      viewModel.EditDraft.Name = "Unsubmitted";

      await viewModel.SaveDeviceCommand.ExecuteAsync(null);

      Assert.True(viewModel.HasUnsavedDraft);
      Assert.Equal("Unsubmitted", viewModel.EditDraft.Name);
      Assert.Equal("DEVICE_REVISION_CONFLICT", viewModel.OperationErrorCode);
      Assert.False(viewModel.LastOperationSucceeded);
  }

  [Fact]
  public async Task AmbiguousUpdate_SetsSafeErrorAndRetainsDraft()
  {
      var commands = new FakeCatalogCommandService { NextFailure = new CatalogMutationUncertainException("update-device", Guid.NewGuid()) };
      var readModel = new DeviceReadModelStub(ExistingDevice());
      var viewModel = new DeviceManagementViewModel(readModel, commands);
      viewModel.EditDraft.Password = "new-secret";

      await viewModel.SaveDeviceCommand.ExecuteAsync(null);

      Assert.True(viewModel.HasUnsavedDraft);
      Assert.True(viewModel.HasOperationError);
      Assert.False(viewModel.LastOperationSucceeded);
  }

  private static CameraDeviceDto ExistingDevice() =>
      new(Guid.NewGuid(), Guid.NewGuid(), "Camera", "192.0.2.10", 8000, 554, "user", true,
          "Maker", "Model", TransportMode.Tcp, true, "remark", 8, []);

  private sealed class DeviceReadModelStub : IDeviceCatalogReadModel
  {
      private readonly CameraDeviceDto device;
      public DeviceReadModelStub(CameraDeviceDto device) => this.device = device;
      public event EventHandler? Changed;
      public IReadOnlyList<DeviceGroupDto> GetGroups() => [];
      public IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId) => [device];
      public CameraDeviceDto? GetDevice(Guid deviceId) => device.Id == deviceId ? device : null;
  }

  private sealed class FakeCatalogCommandService : IDeviceCatalogCommandService
  {
      public UpdateDeviceRequest? LastUpdate { get; private set; }
      public Exception? NextFailure { get; init; }
      public bool CanWrite => true;
      public event EventHandler? AvailabilityChanged;
      public Task<DeviceGroupDto> CreateGroupAsync(CreateGroupRequest request, CancellationToken cancellationToken = default) => Task.FromResult<DeviceGroupDto>(null!);
      public Task<DeviceGroupDto> UpdateGroupAsync(Guid id, UpdateGroupRequest request, CancellationToken cancellationToken = default) => Task.FromResult<DeviceGroupDto>(null!);
      public Task DeleteGroupAsync(Guid id, long expectedRevision, CancellationToken cancellationToken = default) => Task.CompletedTask;
      public Task<CameraDeviceDto> CreateDeviceAsync(CreateDeviceRequest request, CancellationToken cancellationToken = default) => Task.FromResult<CameraDeviceDto>(null!);
      public Task<CameraDeviceDto> UpdateDeviceAsync(Guid id, UpdateDeviceRequest request, CancellationToken cancellationToken = default)
      {
          LastUpdate = request;
          return NextFailure is null ? Task.FromResult<CameraDeviceDto>(null!) : Task.FromException<CameraDeviceDto>(NextFailure);
      }
      public Task DeleteDeviceAsync(Guid id, long expectedRevision, CancellationToken cancellationToken = default) => Task.CompletedTask;
  }
  ```

  Add timeout identity checks and offline availability cases with the same fake command boundary; no test may assert a password value is returned by a read model.
- [ ] Step 2: Run RED.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~DeviceCatalogCommandServiceTests|FullyQualifiedName~DeviceManagementDraftTests"
  ```

  Confirm failure because the async command boundary and DTO-based Draft flow are absent.
- [ ] Step 3: Write the minimal implementation.

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

  public sealed class RemoteDeviceCatalogCommandService : IDeviceCatalogCommandService
  {
      public async Task<CameraDeviceDto> UpdateDeviceAsync(Guid id, UpdateDeviceRequest request, CancellationToken cancellationToken = default)
      {
          try
          {
              var result = await apiClient.UpdateDeviceAsync(currentBaseUri, id, request, cancellationToken).ConfigureAwait(false);
              await coordinator.RefreshNowAsync(cancellationToken).ConfigureAwait(false);
              return result;
          }
          catch (Exception exception) when (IsAmbiguousTransportFailure(exception))
          {
              throw new CatalogMutationUncertainException("update-device", id, exception);
          }
      }
  }
  ```

  Keep the command service to one write plus refresh, retain Draft on uncertainty/conflict, and use the legacy adapter only in explicit SingleCameraTest mode.
- [ ] Step 4: Run focused GREEN.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~DeviceCatalogCommandServiceTests|FullyQualifiedName~DeviceManagementDraftTests"
  ```

  Confirm PASS.
- [ ] Step 5: Run the affected suite/build.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj
  dotnet build .\VideoMonitor.sln -c Debug
  ```
- [ ] Step 6: Review the diff.

  ```powershell
  git diff --check
  git status --short
  ```
- [ ] Step 7: Commit the exact Task 9 paths.

  ```powershell
  git add src/VideoMonitor.Wpf/Catalog/IDeviceCatalogCommandService.cs src/VideoMonitor.Wpf/Catalog/CatalogMutationUncertainException.cs src/VideoMonitor.Wpf/Catalog/RemoteDeviceCatalogCommandService.cs src/VideoMonitor.Wpf/Catalog/LegacyDeviceCatalogCommandService.cs src/VideoMonitor.Wpf/ViewModels/DeviceEditDraftViewModel.cs src/VideoMonitor.Wpf/ViewModels/DeviceGroupTreeItemViewModel.cs src/VideoMonitor.Wpf/ViewModels/DeviceManagementViewModel.cs tests/VideoMonitor.Core.Tests/Catalog/DeviceCatalogCommandServiceTests.cs tests/VideoMonitor.Core.Tests/ViewModels/DeviceManagementDraftTests.cs
  git commit -m "feat: make device management use async catalog drafts"
  ```
- [ ] Step 8: STOP and return the branch, SHA, RED evidence, GREEN result, affected-suite result, and `git status --short` for Sol review.

## Task 10 — Root Category Management

Files:

- Modify `src/VideoMonitor.Wpf/ViewModels/DeviceManagementViewModel.cs`
- Modify `src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml`
- Modify `src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml.cs` only if focus handling is required
- Modify tests: `tests/VideoMonitor.Core.Tests/ViewModels/DeviceManagementGroupTests.cs`

Interfaces:

Consumes:

- `IDeviceCatalogReadModel`, `IDeviceCatalogCommandService`, `MonitorGroupType`, and the existing Device Management tree bindings. Construct `DeviceManagementViewModel` with `(IDeviceCatalogReadModel catalog, IDeviceCatalogCommandService commands)`; root editing remains a ViewModel concern while reads and writes use the two explicit boundaries.
- `IReadOnlyList<DeviceGroupDto> IDeviceCatalogReadModel.GetGroups()` and `Task<DeviceGroupDto> IDeviceCatalogCommandService.CreateGroupAsync(CreateGroupRequest request, CancellationToken cancellationToken = default)`.
- `Task<DeviceGroupDto> IDeviceCatalogCommandService.UpdateGroupAsync(Guid id, UpdateGroupRequest request, CancellationToken cancellationToken = default)` and `Task IDeviceCatalogCommandService.DeleteGroupAsync(Guid id, long expectedRevision, CancellationToken cancellationToken = default)`.

Produces:

- Root draft state with `string RootEditName`, `MonitorGroupType? RootEditKind`, `Guid? EditingRootId`, `IRelayCommand BeginAddRootCommand`, `IRelayCommand<Guid?> BeginEditRootCommand`, `IAsyncRelayCommand SaveRootCommand`, `IRelayCommand CancelRootEditCommand`, and `IAsyncRelayCommand DeleteRootCommand`.
- Root create/update/delete requests with `ParentId = null`, required `MonitorGroupType`, stable Guid, and one-time legacy Kind assignment.

This Task is required because an empty Catalog must allow the user to create the first Root.

Expose `RootKindOptions = Enum.GetValues<MonitorGroupType>()`, `IsRootEditorOpen`, `EditingRootId`, `RootEditName`, `RootEditKind`, `CanEditRootKind`, and commands `BeginAddRootCommand`, `BeginEditRootCommand`, `SaveRootCommand`, `CancelRootEditCommand`, and `DeleteRootCommand`.

New Root behavior: create a local Draft with required Name, required Kind, client-generated Guid, `ParentId = null`, next Sort, and `Enabled = true`. Cancel performs zero writes. A mapped Root's Kind is read-only; a legacy `Kind = null` Root may be assigned once. Child creation never asks for Kind. Add a small “新增分类” entry and Root context actions for edit/delete without redesigning the page.

Required automation IDs: `AddRootCategoryButton`, `RootCategoryNameTextBox`, `RootCategoryKindComboBox`, `SaveRootCategoryButton`, and `CancelRootCategoryButton`.

Tests cover Root draft cancel, required fields, one-time legacy assignment, immutable mapped Kind, stable Guid creation, and delete command routing.

Commit: `feat: add root category management`

Execution steps:

- [ ] Step 1: Write failing tests.

  ```csharp
  [Fact]
  public async Task CancelRootDraft_PerformsZeroWrites()
  {
      var fixture = DeviceManagementViewModelFixture.Empty();
      var viewModel = fixture.ViewModel;

      viewModel.BeginAddRootCommand.Execute(null);
      viewModel.RootEditName = "未提交分类";
      viewModel.CancelRootEditCommand.Execute(null);

      Assert.Equal(0, fixture.Commands.WriteCount);
  }

  [Fact]
  public async Task LegacyRootKind_MayBeAssignedOnlyOnce()
  {
      var fixture = DeviceManagementViewModelFixture.WithLegacyRoot(Guid.NewGuid());
      var viewModel = fixture.ViewModel;

      viewModel.BeginEditRootCommand.Execute(fixture.RootId);
      viewModel.RootEditKind = MonitorGroupType.Chute;
      await viewModel.SaveRootCommand.ExecuteAsync(null);

      Assert.Equal(1, fixture.Commands.WriteCount);
      Assert.Equal(MonitorGroupType.Chute, fixture.Commands.LastGroupUpdate!.Kind);
  }

  private sealed class RecordingCatalogCommandService : IDeviceCatalogCommandService
  {
      public int WriteCount { get; private set; }
      public UpdateGroupRequest? LastGroupUpdate { get; private set; }
      public bool CanWrite => true;
      public event EventHandler? AvailabilityChanged;
      public Task<DeviceGroupDto> CreateGroupAsync(CreateGroupRequest request, CancellationToken cancellationToken = default) { WriteCount++; return Task.FromResult<DeviceGroupDto>(null!); }
      public Task<DeviceGroupDto> UpdateGroupAsync(Guid id, UpdateGroupRequest request, CancellationToken cancellationToken = default) { WriteCount++; LastGroupUpdate = request; return Task.FromResult<DeviceGroupDto>(null!); }
      public Task DeleteGroupAsync(Guid id, long expectedRevision, CancellationToken cancellationToken = default) { WriteCount++; return Task.CompletedTask; }
      public Task<CameraDeviceDto> CreateDeviceAsync(CreateDeviceRequest request, CancellationToken cancellationToken = default) { return Task.FromResult<CameraDeviceDto>(null!); }
      public Task<CameraDeviceDto> UpdateDeviceAsync(Guid id, UpdateDeviceRequest request, CancellationToken cancellationToken = default) { return Task.FromResult<CameraDeviceDto>(null!); }
      public Task DeleteDeviceAsync(Guid id, long expectedRevision, CancellationToken cancellationToken = default) { return Task.CompletedTask; }
  }

  private sealed class DeviceManagementViewModelFixture
  {
      private DeviceManagementViewModelFixture(DeviceManagementViewModel viewModel, RecordingCatalogCommandService commands, Guid rootId)
      {
          ViewModel = viewModel;
          Commands = commands;
          RootId = rootId;
      }

      public DeviceManagementViewModel ViewModel { get; }
      public RecordingCatalogCommandService Commands { get; }
      public Guid RootId { get; }

      public static DeviceManagementViewModelFixture Empty()
      {
          var commands = new RecordingCatalogCommandService();
          var readModel = new DeviceManagementReadModelStub(Array.Empty<DeviceGroupDto>());
          return new(new DeviceManagementViewModel(readModel, commands), commands, Guid.Empty);
      }

      public static DeviceManagementViewModelFixture WithLegacyRoot(Guid rootId)
      {
          var commands = new RecordingCatalogCommandService();
          var readModel = new DeviceManagementReadModelStub(new[]
          {
              new DeviceGroupDto(rootId, "Legacy Root", null, 0, true, null, 1)
          });
          return new(new DeviceManagementViewModel(readModel, commands), commands, rootId);
      }
  }

  private sealed class DeviceManagementReadModelStub : IDeviceCatalogReadModel
  {
      private readonly IReadOnlyList<DeviceGroupDto> groups;

      public DeviceManagementReadModelStub(IReadOnlyList<DeviceGroupDto> groups) => this.groups = groups;

      public event EventHandler? Changed;
      public IReadOnlyList<DeviceGroupDto> GetGroups() => groups;
      public IReadOnlyList<CameraDeviceDto> GetDevices(Guid groupId) => Array.Empty<CameraDeviceDto>();
      public CameraDeviceDto? GetDevice(Guid id) => null;
  }
  ```

  Add cases for required name/kind, immutable mapped Kind, stable Guid creation, and delete command routing.
- [ ] Step 2: Run RED.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~DeviceManagementGroupTests
  ```

  Confirm failure because Root management and its explicit command bindings are absent.
- [ ] Step 3: Write the minimal implementation.

  ```csharp
  public IReadOnlyList<MonitorGroupType> RootKindOptions { get; } = Enum.GetValues<MonitorGroupType>();
  public bool IsRootEditorOpen { get; private set; }
  public Guid? EditingRootId { get; private set; }
  public string RootEditName { get; set; } = "";
  public MonitorGroupType? RootEditKind { get; set; }

  private async Task SaveRootAsync()
  {
      var request = new UpdateGroupRequest(RootEditName, null, NextSort(), true, RootEditKind, CurrentRevision);
      await commandService.UpdateGroupAsync(EditingRootId!.Value, request).ConfigureAwait(false);
  }
  ```

  Keep Root editor state local until Save, enforce the one-time legacy Kind rule, use one stable Guid per new Root, and preserve the existing page layout.
- [ ] Step 4: Run focused GREEN.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~DeviceManagementGroupTests
  ```

  Confirm PASS.
- [ ] Step 5: Run the affected suite/build.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj
  dotnet build .\VideoMonitor.sln -c Debug
  ```
- [ ] Step 6: Review the diff.

  ```powershell
  git diff --check
  git status --short
  ```
- [ ] Step 7: Commit the exact Task 10 paths.

  ```powershell
  git add src/VideoMonitor.Wpf/ViewModels/DeviceManagementViewModel.cs src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml.cs tests/VideoMonitor.Core.Tests/ViewModels/DeviceManagementGroupTests.cs
  git commit -m "feat: add root category management"
  ```
- [ ] Step 8: STOP and return the branch, SHA, RED evidence, GREEN result, affected-suite result, and `git status --short` for Sol review.

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
- Create tests: `tests/VideoMonitor.Core.Tests/ViewModels/ServerSettingsViewModelTests.cs`
- Modify tests: `tests/VideoMonitor.Core.Tests/ViewModels/MainHeaderControlStateTests.cs`

Interfaces:

Consumes:

- `ServerConnectionStatus`, `ServerConnectionCoordinator.StatusChanged`, `IClientSettingsStore`, and `SwitchServerAsync(Uri, Func<bool>, CancellationToken)`.
- `event EventHandler? ServerConnectionCoordinator.StatusChanged`, `Task ServerConnectionCoordinator.ProbeAsync(Uri baseUri, CancellationToken cancellationToken = default)`, and `Task ServerConnectionCoordinator.SwitchServerAsync(Uri candidate, Func<bool> hasUnsavedDraft, CancellationToken cancellationToken = default)`.
- `ClientSettings IClientSettingsStore.Load()` and `Task IClientSettingsStore.SaveAsync(ClientSettings settings, CancellationToken cancellationToken = default)`.

Produces:

- `ServerSettingsViewModel` with `string BaseUrl`, `IAsyncRelayCommand TestConnectionCommand`, `IAsyncRelayCommand SaveCommand`, and Draft-blocking commands.
- `ServerStatusViewModel` state mapping and last-sync formatting.
- MainWindow/StatusBar bindings to the real central connection state.

Display states are Unconfigured = 未配置, Connecting = 连接中, Connected = 已连接, and Unavailable = 连接失败. Null last-sync time displays `--`; otherwise use local `yyyy-MM-dd HH:mm:ss`.

Settings UI provides BaseUrl, Test Connection, and Save. Test only probes and never switches. A successful test result is cleared when the URL changes. Save calls `SwitchServerAsync`, which probes again and cannot use a previous Test result as a consistency shortcut. `HasUnsavedDraft` blocks Save.

Open the settings window from MainWindow. Bind StatusBar's lower-right state and last sync to the real central connection state. The existing green system labels must either bind to actual overall state or be renamed to client-running state; do not redesign unrelated styling.

Tests cover state labels, last-sync formatting, repeat probe on Save, Draft blocking, and no false healthy indication after Server failure.

Commit: `feat: add central server settings and status ui`

Execution steps:

- [ ] Step 1: Write failing tests.

  ```csharp
  [Fact]
  public async Task TestConnection_DoesNotSwitchEndpoint()
  {
      var coordinator = new RecordingConnectionCoordinator(new Uri("https://server-a"));
      var viewModel = new ServerSettingsViewModel(coordinator, new ClientSettingsStoreStub());
      viewModel.BaseUrl = "https://server-b";

      await viewModel.TestConnectionCommand.ExecuteAsync(null);

      Assert.Equal(0, coordinator.SwitchCount);
      Assert.Equal(new Uri("https://server-a"), coordinator.Status.BaseUri);
  }

  [Fact]
  public async Task Save_ProbesAgainBeforeSwitch()
  {
      var coordinator = new RecordingConnectionCoordinator(new Uri("https://server-a"));
      var viewModel = new ServerSettingsViewModel(coordinator, new ClientSettingsStoreStub());
      viewModel.BaseUrl = "https://server-b";

      await viewModel.SaveCommand.ExecuteAsync(null);

      Assert.Equal(1, coordinator.ProbeCount);
      Assert.Equal(1, coordinator.SwitchCount);
  }

  private sealed class RecordingConnectionCoordinator
  {
      public ServerConnectionStatus Status { get; }
      public int ProbeCount { get; private set; }
      public int SwitchCount { get; private set; }
      public RecordingConnectionCoordinator(Uri baseUri) => Status = new(baseUri, ServerConnectionState.Connected, null, false);
      public Task ProbeAsync(Uri baseUri, CancellationToken cancellationToken = default) { ProbeCount++; return Task.CompletedTask; }
      public Task SwitchServerAsync(Uri baseUri, Func<bool> hasUnsavedDraft, CancellationToken cancellationToken = default) { SwitchCount++; return Task.CompletedTask; }
  }

  private sealed class ClientSettingsStoreStub : IClientSettingsStore
  {
      public ClientSettings Load() => ClientSettings.Empty;
      public Task SaveAsync(ClientSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
  }
  ```

  Add state-label, last-sync formatting, Draft blocking, and no-false-healthy-indication cases.
- [ ] Step 2: Run RED.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~ServerSettingsViewModelTests|FullyQualifiedName~MainHeaderControlStateTests"
  ```

  Confirm failure because the Server settings ViewModel and real status bindings are absent.
- [ ] Step 3: Write the minimal implementation.

  ```csharp
  public async Task TestConnectionAsync()
  {
      var candidate = new Uri(BaseUrl, UriKind.Absolute);
      await coordinator.ProbeAsync(candidate, CancellationToken.None).ConfigureAwait(false);
      IsTestSuccessful = true;
  }

  public async Task SaveAsync()
  {
      if (HasUnsavedDraft) return;
      var candidate = new Uri(BaseUrl, UriKind.Absolute);
      await coordinator.SwitchServerAsync(candidate, () => HasUnsavedDraft, CancellationToken.None).ConfigureAwait(false);
  }
  ```

  Bind localized status and last-sync text to `ServerConnectionStatus`; do not infer health from a previous successful Test.
- [ ] Step 4: Run focused GREEN.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~ServerSettingsViewModelTests|FullyQualifiedName~MainHeaderControlStateTests"
  ```

  Confirm PASS.
- [ ] Step 5: Run the affected suite/build.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj
  dotnet build .\VideoMonitor.sln -c Debug
  ```
- [ ] Step 6: Review the diff.

  ```powershell
  git diff --check
  git status --short
  ```
- [ ] Step 7: Commit the exact Task 11 paths.

  ```powershell
  git add src/VideoMonitor.Wpf/ViewModels/ServerSettingsViewModel.cs src/VideoMonitor.Wpf/ViewModels/ServerStatusViewModel.cs src/VideoMonitor.Wpf/Views/ServerSettingsWindow.xaml src/VideoMonitor.Wpf/Views/ServerSettingsWindow.xaml.cs src/VideoMonitor.Wpf/ViewModels/MainViewModel.cs src/VideoMonitor.Wpf/MainWindow.xaml src/VideoMonitor.Wpf/MainWindow.xaml.cs src/VideoMonitor.Wpf/Controls/StatusBar.xaml tests/VideoMonitor.Core.Tests/ViewModels/ServerSettingsViewModelTests.cs tests/VideoMonitor.Core.Tests/ViewModels/MainHeaderControlStateTests.cs
  git commit -m "feat: add central server settings and status ui"
  ```
- [ ] Step 8: STOP and return the branch, SHA, RED evidence, GREEN result, affected-suite result, and `git status --short` for Sol review.

## Task 12 — Formal Central Composition and SingleCameraTest Compatibility

Files:

- Modify `src/VideoMonitor.Wpf/App.xaml.cs`
- Optional only when App size requires it: create `src/VideoMonitor.Wpf/Configuration/ApplicationCatalogComposition.cs` for catalog mode composition/lifecycle only
- Create tests: `tests/VideoMonitor.Core.Tests/Composition/ApplicationCatalogCompositionTests.cs`
- Modify tests: `tests/VideoMonitor.Core.Tests/Services/ShutdownCleanupCoordinatorTests.cs` only when shutdown behavior requires it

Interfaces:

Consumes:

- `IClientSettingsStore`, `CatalogApiClient`, `ClientCatalogCache`, `ServerConnectionCoordinator`, `RemoteDeviceCatalogCommandService`, legacy adapters, existing playback composition, and WPF shutdown cleanup contracts.
- `ClientSettings IClientSettingsStore.Load()`, `Task IClientSettingsStore.SaveAsync(ClientSettings settings, CancellationToken cancellationToken = default)`, `Task ServerConnectionCoordinator.RunAsync(CancellationToken cancellationToken)`, and `ValueTask ServerConnectionCoordinator.DisposeAsync()`.
- `IDeviceCatalogReadModel`, `IDeviceCatalogCommandService`, and the existing local `JsonDeviceCatalogStore`/`InMemoryDeviceCatalog` composition contracts.

Produces:

- One formal composition of `IClientSettingsStore`, `CatalogApiClient`, `ClientCatalogCache`, `ServerConnectionCoordinator`, and `RemoteDeviceCatalogCommandService`, exposed as `CatalogComposition`.
- One explicit local composition of `JsonDeviceCatalogStore`, `InMemoryDeviceCatalog`, `DeviceCatalogPersistenceCoordinator`, and `LocalZlmPlaybackSourceProvider` only for `SingleCameraTest=true`.
- `ApplicationCatalogComposition` only if extracted, with mode composition and lifecycle ownership boundaries.

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

Execution steps:

- [ ] Step 1: Write failing tests.

  ```csharp
  [Fact]
  public void FormalMode_DoesNotInstantiateJsonCompatibilityPath()
  {
      using var composition = ApplicationCatalogComposition.Create(new SingleCameraTestOptions(false), new CompositionDependencies());

      Assert.False(composition.Services.Any(service => service is JsonDeviceCatalogStore));
      Assert.IsType<ClientCatalogCache>(composition.ReadModel);
  }

  [Fact]
  public void SingleCameraTest_InstantiatesLocalCompatibilityPath()
  {
      using var composition = ApplicationCatalogComposition.Create(new SingleCameraTestOptions(true), new CompositionDependencies());

      Assert.IsType<LegacyDeviceCatalogReadModel>(composition.ReadModel);
      Assert.NotNull(composition.LocalPlaybackSource);
  }

  private sealed class CompositionDependencies
  {
      public CatalogApiClient ApiClient { get; } = null!;
      public IClientSettingsStore Settings { get; } = null!;
      public IUiDispatcher UiDispatcher { get; } = null!;
      // Test-only factories supply isolated settings, HTTP, cache, and legacy Catalog dependencies.
  }
  ```

  Add shutdown assertions proving formal coordinator cancellation and local compatibility flush/cleanup are each owned exactly once.
- [ ] Step 2: Run RED.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~ApplicationCatalogCompositionTests|FullyQualifiedName~ShutdownCleanupCoordinatorTests"
  ```

  Confirm failure because the two explicit Catalog modes are not composed by the application.
- [ ] Step 3: Write the minimal implementation.

  ```csharp
  public static class ApplicationCatalogComposition
  {
      public static CatalogComposition Create(
          SingleCameraTestOptions options,
          CompositionDependencies dependencies) => options.Enabled
              ? CreateLocalCompatibility(dependencies)
              : CreateFormalCentralMode(dependencies);

      private static CatalogComposition CreateFormalCentralMode(CompositionDependencies dependencies)
      {
          var cache = new ClientCatalogCache(EmptySnapshot(), dependencies.UiDispatcher);
          var coordinator = new ServerConnectionCoordinator(dependencies.ApiClient, cache, dependencies.Settings);
          return new CatalogComposition(cache, coordinator, localPlaybackSource: null);
      }
  }

  public sealed record CatalogComposition(
      IDeviceCatalogReadModel ReadModel,
      ServerConnectionCoordinator Coordinator,
      object? LocalPlaybackSource)
  {
      public IReadOnlyList<object> Services { get; init; } = Array.Empty<object>();
  }
  ```

  Keep App.xaml.cs as the caller, preserve one lifetime owner per mode, and do not instantiate local JSON services in formal mode.
- [ ] Step 4: Run focused GREEN.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~ApplicationCatalogCompositionTests|FullyQualifiedName~ShutdownCleanupCoordinatorTests"
  ```

  Confirm PASS.
- [ ] Step 5: Run the affected suite/build.

  ```powershell
  dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj
  dotnet build .\VideoMonitor.sln -c Debug
  ```
- [ ] Step 6: Review the diff.

  ```powershell
  git diff --check
  git status --short
  ```
- [ ] Step 7: Commit the exact Task 12 paths.

  ```powershell
  git add src/VideoMonitor.Wpf/App.xaml.cs src/VideoMonitor.Wpf/Configuration/ApplicationCatalogComposition.cs tests/VideoMonitor.Core.Tests/Composition/ApplicationCatalogCompositionTests.cs tests/VideoMonitor.Core.Tests/Services/ShutdownCleanupCoordinatorTests.cs
  git commit -m "feat: compose formal central catalog client mode"
  ```
- [ ] Step 8: STOP and return the branch, SHA, RED evidence, GREEN result, affected-suite result, and `git status --short` for Sol review.

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

```powershell
rg -n "备用1|Z-1#巷|2#主溜井|3#主溜井" src tests
rg -n "卸矿站监控|溜井监控|巷道监控" src tests
rg -n "ServerCertificateCustomValidationCallback|DangerousAcceptAnyServerCertificateValidator" src
rg -n "\.Result|\.Wait\(\)|GetAwaiter\(\)\.GetResult\(\)" src/VideoMonitor.Wpf
rg -n "JsonDeviceCatalogStore|DeviceCatalogPersistenceCoordinator" src/VideoMonitor.Wpf/App.xaml.cs src/VideoMonitor.Wpf/Configuration
git ls-files | Select-String -Pattern "(^|/)\.devdata/"
```

Expected results:

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
