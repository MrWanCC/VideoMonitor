# Stage 3.5 Shared Machine Data Directory Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the default device catalog and its DPAPI boundary from the current Windows user to the whole machine, while preserving the existing catalog, bootstrapper, persistence coordinator, and schema architecture.

**Architecture:** `JsonDeviceCatalogStore` will accept an explicit file path and `DataProtectionScope`, with production defaults based on `CommonApplicationData` and `LocalMachine`. `DeviceCatalogBootstrapper` will load the new ProgramData store first; only when it is missing will it load the old LocalAppData/CurrentUser store, save and reload into ProgramData, then rename the old formal file to a migrated backup. Fresh initialization will save directly to ProgramData. `DeviceCatalogPersistenceCoordinator` will continue using the same new production store and unchanged FIFO behavior.

**Tech Stack:** .NET 8, WPF, C#, `System.Text.Json`, Windows DPAPI (`System.Security.Cryptography.ProtectedData`), xUnit.

**Spec:** `C:\Users\Wan\.codex\attachments\cefa1546-40a8-440c-b36c-a02da4d37f84\pasted-text.txt`

## Global Constraints

- Keep `SchemaVersion = 1`; do not implement schema migration.
- Production default path is `%ProgramData%\VideoMonitor\data\device-catalog.json` using `Environment.SpecialFolder.CommonApplicationData`; never hardcode `C:\ProgramData`.
- Production default DPAPI scope is `DataProtectionScope.LocalMachine`; old LocalAppData migration reads with `DataProtectionScope.CurrentUser`.
- Preserve plaintext `CameraDevice.Password` only in memory and `dpapi:v1:<base64>` on disk; never log credentials or secrets.
- Do not modify `IDeviceCatalog`, Snapshot fields, Playback, ZLM, MonitorSwitchService, or lifecycle behavior.
- Do not add DI, Options, SQLite, EF Core, Server Store, installer/ACL commands, or new NuGet packages.
- Preserve safe atomic `.tmp`/replace/move/`.bak` writes and existing Store semaphore behavior.
- Do not commit Git.

---

### Task 1: Add failing Store scope and default-path tests

**Files:**
- Modify: `tests/VideoMonitor.Core.Tests/Infrastructure/JsonDeviceCatalogStoreTests.cs`
- Modify: `tests/VideoMonitor.Core.Tests/Infrastructure/JsonDeviceCatalogPasswordProtectionTests.cs`

**Interfaces:**
- Consumes the current `JsonDeviceCatalogStore` constructors and default path.
- Produces tests that require `JsonDeviceCatalogStore(string filePath, DataProtectionScope protectionScope)` and a production default scope exposed through behavior or a minimal testable property.

- [ ] **Step 1: Add tests for the new default path and default protection behavior.**

```csharp
[Fact]
public void DefaultFilePath_UsesCommonApplicationDataDataDirectory()
{
    var expected = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "VideoMonitor", "data", "device-catalog.json");

    Assert.Equal(expected, JsonDeviceCatalogStore.DefaultFilePath);
}

[Fact]
public async Task ExplicitLocalMachineScope_RoundTripsPassword()
{
    var path = CreatePath();
    var store = new JsonDeviceCatalogStore(path, DataProtectionScope.LocalMachine);

    await store.SaveAsync(CreateSnapshot("machine-password"));
    var loaded = await store.LoadAsync();

    Assert.Equal("machine-password", loaded!.Devices.Single().Password);
}
```

- [ ] **Step 2: Add a test proving the default store writes a LocalMachine-compatible password without inspecting or logging credentials.** Use the existing temporary-path store with the explicit scope if the default ProgramData path must not be touched by tests; assert the JSON contains `dpapi:v1:` and not the plaintext.

- [ ] **Step 3: Run the focused tests and verify they fail because the constructor/defaults still use LocalApplicationData and CurrentUser.**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~JsonDeviceCatalogStoreTests`

Expected: FAIL in the new default-path/scope tests, with existing tests identifying the old LocalApplicationData expectation.

### Task 2: Make JsonDeviceCatalogStore path and DPAPI scope explicit

**Files:**
- Modify: `src/VideoMonitor.Infrastructure/Persistence/JsonDeviceCatalogStore.cs`
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/JsonDeviceCatalogStoreTests.cs`
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/JsonDeviceCatalogPasswordProtectionTests.cs`

**Interfaces:**
- Consumes `IDeviceCatalogStore` unchanged.
- Produces:
  - `JsonDeviceCatalogStore()` using `DefaultFilePath` and `DataProtectionScope.LocalMachine`.
  - `JsonDeviceCatalogStore(string filePath)` using the production default scope.
  - `JsonDeviceCatalogStore(string filePath, DataProtectionScope protectionScope)` for migration/tests.

- [ ] **Step 1: Add the `protectionScope` field and constructor overloads without changing serialization or atomic-write code.**

```csharp
private readonly DataProtectionScope protectionScope;

public JsonDeviceCatalogStore()
    : this(DefaultFilePath, DataProtectionScope.LocalMachine)
{
}

public JsonDeviceCatalogStore(string filePath)
    : this(filePath, DataProtectionScope.LocalMachine)
{
}

public JsonDeviceCatalogStore(
    string filePath,
    DataProtectionScope protectionScope)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
    this.filePath = Path.GetFullPath(filePath);
    this.protectionScope = protectionScope;
}

public static string DefaultFilePath => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "VideoMonitor", "data", "device-catalog.json");
```

- [ ] **Step 2: Replace only the two hard-coded `DataProtectionScope.CurrentUser` calls with `protectionScope`.** Keep password format, error handling, DTO boundaries, atomic write, `.bak`, `.tmp`, and semaphore unchanged.

- [ ] **Step 3: Run Store and password tests and verify the focused suite passes.**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~JsonDeviceCatalog`

Expected: PASS, including LocalMachine round-trip and existing CurrentUser tests that explicitly construct the old-scope store.

### Task 3: Add failing bootstrapper migration tests

**Files:**
- Modify: `tests/VideoMonitor.Core.Tests/Configuration/DeviceCatalogBootstrapperTests.cs`

**Interfaces:**
- Consumes `DeviceCatalogBootstrapper(IDeviceCatalogStore store, string? legacyDevicePath, Func<MockDeviceDataSet>? mockDataFactory)`.
- Produces tests requiring a second explicit old-catalog path/store input while preserving existing legacy `local-device.json` behavior.

- [ ] **Step 1: Add tests for ProgramData-first behavior.** Use a recording/fake store for the new store and a real or recording old store path; assert the old formal file and legacy file are not read when the new store returns data.

```csharp
[Fact]
public async Task InitializeAsync_WhenProgramDataCatalogExists_DoesNotReadOldCatalogOrLegacy()
{
    // Arrange a new-store snapshot, an old catalog sentinel, and a legacy file.
    // Make the old-store load throw if invoked.
    // Act: await bootstrapper.InitializeAsync();
    // Assert: new snapshot is used and old/legacy files remain untouched.
}
```

- [ ] **Step 2: Add a test for old LocalAppData migration.** Save an old snapshot with `DataProtectionScope.CurrentUser`, initialize a missing new ProgramData store, assert the new store is saved/reloaded with the complete device/group/channel data and plaintext password restored in memory, and assert the old formal file is renamed to `device-catalog.currentuser.migrated.json` (or the chosen exact equivalent).

- [ ] **Step 3: Add failure tests for corrupted/unsupported/undecryptable old catalogs, failed new reload, and failed old-file rename.** Assert no Mock fallback, no old-file deletion, no legacy deletion, and no overwrite of the valid new file. For rename failure, assert the new file remains and the thrown message contains no username/password/ciphertext.

- [ ] **Step 4: Add fresh-install and ProgramData permission-path tests.** Assert missing new and old catalogs initialize Mock directly into the new store. Use a deterministic invalid/unwritable test path or injectable directory operation only if the existing file API makes the permission case impossible on Windows; do not add ACL-changing code.

- [ ] **Step 5: Run the focused bootstrapper tests and verify they fail before production changes.**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~DeviceCatalogBootstrapperTests`

Expected: FAIL because Bootstrapper currently has only one store/path and always treats a missing store as Mock initialization.

### Task 4: Implement ProgramData-first Bootstrapper migration

**Files:**
- Modify: `src/VideoMonitor.Wpf/Configuration/DeviceCatalogBootstrapper.cs`
- Modify: `src/VideoMonitor.Wpf/App.xaml.cs`
- Test: `tests/VideoMonitor.Core.Tests/Configuration/DeviceCatalogBootstrapperTests.cs`

**Interfaces:**
- Consumes the current `IDeviceCatalogStore`, `MockDeviceData`, `LocalDeviceOptions`, `LocalDeviceCatalogOverride`, and `DeviceCatalogSnapshotFactory`.
- Produces a bootstrapper that accepts the new store plus an optional old-catalog path, with the old store internally created as `DataProtectionScope.CurrentUser` only when the new formal file is absent.

- [ ] **Step 1: Add minimal Bootstrapper constructor support for the old catalog path.** Default it to `%LocalAppData%\VideoMonitor\data\device-catalog.json` derived from `Environment.SpecialFolder.LocalApplicationData`; preserve test injection of paths and Mock factory.

```csharp
private readonly string oldCatalogPath;

public DeviceCatalogBootstrapper(
    IDeviceCatalogStore store,
    string? legacyDevicePath = null,
    Func<MockDeviceDataSet>? mockDataFactory = null,
    string? oldCatalogPath = null)
{
    // Keep the existing arguments and add only the old formal-catalog path.
}
```

- [ ] **Step 2: Implement strict startup priority.** The first `store.LoadAsync()` result wins. Only when it returns `null`, check the old formal path. If it exists, construct `new JsonDeviceCatalogStore(oldCatalogPath, DataProtectionScope.CurrentUser)`, load it, validate it through `CreateCatalog`, and use that snapshot as migration input. Do not read `local-device.json` in the ProgramData-existing or old-catalog-existing branches.

- [ ] **Step 3: Save and reload migration data through the new store before renaming the old formal file.** Use the existing snapshot factory and existing `CreateCatalog` validation. Rename only the formal old file after save, reload, and catalog construction succeed; keep `.bak` and `.tmp` untouched.

- [ ] **Step 4: Preserve first-install Mock + `local-device.json` flow for the branch where both formal catalogs are absent.** Save the resulting snapshot directly through the new ProgramData store, reload, restore Mock runtime statuses, then delete `local-device.json` only after all checks succeed.

- [ ] **Step 5: Wrap old-file rename and ProgramData directory/write failures in clear, credential-free exceptions.** Do not invoke `icacls`, request elevation, fall back to LocalAppData, or fall back to Mock after an existing catalog load failure. Add a code comment documenting that the installer must create `%ProgramData%\VideoMonitor` and grant only application-directory Modify access.

- [ ] **Step 6: Run focused Bootstrapper tests and confirm they pass.**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~DeviceCatalogBootstrapperTests`

Expected: PASS with strict ProgramData-first, old-catalog migration, backup rename, and failure behavior.

### Task 5: Wire production Store and coordinator to the new default

**Files:**
- Modify: `src/VideoMonitor.Wpf/App.xaml.cs`
- Test: `tests/VideoMonitor.Core.Tests/Configuration/DeviceCatalogBootstrapperTests.cs`

**Interfaces:**
- Consumes `JsonDeviceCatalogStore()` defaulting to ProgramData/LocalMachine and the existing `DeviceCatalogPersistenceCoordinator` constructor.
- Produces one production Store and one Coordinator sharing the single Catalog; no Playback/ZLM lifecycle changes.

- [ ] **Step 1: Keep App’s single `deviceCatalogStore` variable and pass it to Bootstrapper and Coordinator.** No new Catalog, no direct Mock initialization, and no change to monitor/playback composition.

- [ ] **Step 2: Preserve shutdown `await persistenceCoordinator.DisposeAsync()` so Changed FIFO is flushed through the ProgramData Store.** Keep existing UI error text generic and secret-free.

- [ ] **Step 3: Add source/wiring assertions that App uses the shared Store and Coordinator, while not asserting implementation details of Playback or ZLM.**

- [ ] **Step 4: Run the focused integration/wiring tests.**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~DeviceCatalog`

Expected: PASS.

### Task 6: Full verification and manual migration check

**Files:**
- Verify only; do not modify unrelated dirty files.

- [ ] **Step 1: Inspect current state without changing the running application.** Check the old LocalAppData formal catalog, new ProgramData formal catalog, migrated backup, `.bak`, and `.tmp` paths. Never print file contents containing password fields.

- [ ] **Step 2: If no user-owned `VideoMonitor.Wpf` instance is running and the user has authorized the manual run, start the built application and verify:** ProgramData creation, device/group preservation, old-file rename, group modification persistence after clean close/restart, and absence of plaintext password in the ProgramData JSON. If an existing user process is running, do not close or operate it; report the manual check as not executed.

- [ ] **Step 3: Run the required final commands.**

Run: `git diff --check`

Run: `dotnet build VideoMonitor.sln`

Run: `dotnet test VideoMonitor.sln`

Expected: exit code 0; no test failures; no new warnings/errors attributable to this change.

- [ ] **Step 4: Review the diff for prohibited changes.** Confirm no changes to Playback, ZLM, MonitorSwitchService, `IDeviceCatalog`, Snapshot fields, schema version, ACLs, or Git history.

- [ ] **Step 5: Do not commit Git.** Report default path/scope, migration order, failure/backup behavior, modified files, tests, build/test output, manual verification status, and remaining installer ACL work.
