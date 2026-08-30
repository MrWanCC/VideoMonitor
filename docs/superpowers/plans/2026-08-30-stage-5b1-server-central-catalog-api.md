# Stage 5B-1 Server Central Catalog API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `VideoMonitor.Server` expose a versioned, password-safe, optimistic-concurrency Catalog API backed by SQLite V2 while preserving Stage 5A health, backup, and secret-protection behavior.

**Architecture:** Shared wire contracts live in `VideoMonitor.Core`. `VideoMonitor.Server` owns application validation and HTTP mapping. `VideoMonitor.Infrastructure` owns SQLite transactions and secret persistence. `CameraDevice + CameraChannel[]` is one aggregate; `DeviceGroup` is another; both use configuration `Revision` checked inside the same transaction as update/delete.

**Tech Stack:** .NET 8, ASP.NET Core minimal API, Microsoft.Data.Sqlite 8.0.0, existing `ISecretProtector`, xUnit 2.5.3, Microsoft.AspNetCore.Mvc.Testing 8.0.0.

**Spec:** `docs/superpowers/specs/2026-08-29-stage-5b-central-catalog-api-wpf-data-source-design.md`

## Global Constraints

- Baseline: `10033149f9b05416dd0082e589dd818eeb7a99a2`.
- Server SQLite is the only authoritative production Catalog.
- Do not add User/Login/RBAC/JWT/RefreshToken/Client Enrollment.
- Do not add Legacy JSON migration APIs or production editable JSON fallback.
- `CameraChannel` has no independent revision; channel changes increment parent `CameraDevice.Revision` exactly once.
- GET/runtime status changes never increment configuration Revision.
- Catalog reads never return plaintext password or `password_ciphertext`.
- `UpdateDeviceRequest.NewPassword == null` preserves the existing protected password; a non-empty value replaces it.
- CameraDevice + channels update atomically in one SQLite transaction.
- HTTP endpoints contain no SQL and never log secret-bearing request bodies.
- Preserve all Stage 5A health/readiness/backup/security tests.

---

## File Structure

- Modify `src/VideoMonitor.Core/Models/DeviceGroup.cs` — add `Revision`.
- Modify `src/VideoMonitor.Core/Models/CameraDevice.cs` — add `Revision`.
- Modify `src/VideoMonitor.Core/Services/InMemoryDeviceCatalog.cs` — copy Revision.
- Create `src/VideoMonitor.Core/Catalog/*` — safe read DTOs, create/update requests, stable error DTO.
- Modify `src/VideoMonitor.Infrastructure/Persistence/SqliteDatabaseInitializer.cs` — V2 migration.
- Modify `src/VideoMonitor.Infrastructure/Persistence/SqliteDeviceCatalogStore.cs` — preserve Revision under the Stage 5A snapshot store.
- Create `src/VideoMonitor.Infrastructure/Persistence/ICentralCatalogRepository.cs`.
- Create `src/VideoMonitor.Infrastructure/Persistence/CatalogRepositoryResult.cs`.
- Create `src/VideoMonitor.Infrastructure/Persistence/SqliteCentralCatalogRepository.cs`.
- Create `src/VideoMonitor.Server/Catalog/CatalogApplicationService.cs`.
- Create `src/VideoMonitor.Server/Catalog/CatalogOperationResult.cs`.
- Create `src/VideoMonitor.Server/Catalog/CatalogEndpoints.cs`.
- Modify `src/VideoMonitor.Server/Program.cs`.
- Extend Core/Infrastructure and Server test projects.

---

### Task 1: Configuration Revision + SQLite V2

**Files:**
- Modify: `src/VideoMonitor.Core/Models/DeviceGroup.cs`
- Modify: `src/VideoMonitor.Core/Models/CameraDevice.cs`
- Modify: `src/VideoMonitor.Core/Services/InMemoryDeviceCatalog.cs`
- Modify: `src/VideoMonitor.Infrastructure/Persistence/SqliteDatabaseInitializer.cs`
- Modify: `src/VideoMonitor.Infrastructure/Persistence/SqliteDeviceCatalogStore.cs`
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/SqliteDatabaseInitializerTests.cs`
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/SqliteDeviceCatalogStoreTests.cs`

**Interfaces:**

```csharp
public long Revision { get; set; } = 1;
```

`SqliteDatabaseInitializer.CurrentSchemaVersion` becomes `2`; V2 adds `revision INTEGER NOT NULL DEFAULT 1` to `device_groups` and `camera_devices` without rewriting historical V1 DDL.

- [ ] **Step 1: Write failing migration tests**

Add tests for: fresh DB -> V2; real V1 DB -> V2; existing V1 rows -> revision 1; second initialization is idempotent; DB version 3 is rejected.

```csharp
[Fact]
public async Task InitializeAsync_UpgradesV1RowsToRevisionOne()
{
    await CreateV1DatabaseAsync();
    await initializer.InitializeAsync();

    Assert.Equal(2L, await ScalarLongAsync("SELECT MAX(version) FROM schema_migrations;"));
    Assert.Equal(1L, await ScalarLongAsync("SELECT revision FROM device_groups LIMIT 1;"));
    Assert.Equal(1L, await ScalarLongAsync("SELECT revision FROM camera_devices LIMIT 1;"));
}
```

- [ ] **Step 2: Run and verify failure**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~SqliteDatabaseInitializerTests
```

Expected: new V2 assertions fail because schema is still V1.

- [ ] **Step 3: Implement sequential V2 migration**

Use explicit sequential migrations:

```csharp
if (version < 1) { await ApplyV1SchemaAsync(...); await InsertMigrationAsync(..., 1, ...); version = 1; }
if (version < 2)
{
    await ExecuteNonQueryAsync(connection, transaction, """
        ALTER TABLE device_groups ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;
        ALTER TABLE camera_devices ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;
        """, cancellationToken);
    await InsertMigrationAsync(..., 2, ...);
}
```

- [ ] **Step 4: Preserve Revision in legacy in-memory/snapshot code**

`InMemoryDeviceCatalog.UpdateGroup` and `CopyDevice` copy Revision. `SqliteDeviceCatalogStore` SELECT/INSERT statements include Revision so a Stage 5A-style snapshot round-trip never resets V2 revisions to 1.

- [ ] **Step 5: Add round-trip test**

Save/load a group Revision 7 and device Revision 11 and assert exact values survive.

- [ ] **Step 6: Run focused tests**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SqliteDatabaseInitializerTests|FullyQualifiedName~SqliteDeviceCatalogStoreTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/VideoMonitor.Core/Models/DeviceGroup.cs src/VideoMonitor.Core/Models/CameraDevice.cs src/VideoMonitor.Core/Services/InMemoryDeviceCatalog.cs src/VideoMonitor.Infrastructure/Persistence/SqliteDatabaseInitializer.cs src/VideoMonitor.Infrastructure/Persistence/SqliteDeviceCatalogStore.cs tests/VideoMonitor.Core.Tests/Infrastructure/SqliteDatabaseInitializerTests.cs tests/VideoMonitor.Core.Tests/Infrastructure/SqliteDeviceCatalogStoreTests.cs
git commit -m "feat: add catalog configuration revisions"
```

---

### Task 2: Shared Password-Safe Catalog Contracts

**Files:**
- Create: `src/VideoMonitor.Core/Catalog/CatalogSnapshotDto.cs`
- Create: `src/VideoMonitor.Core/Catalog/DeviceGroupDto.cs`
- Create: `src/VideoMonitor.Core/Catalog/CameraDeviceDto.cs`
- Create: `src/VideoMonitor.Core/Catalog/CameraChannelDto.cs`
- Create: `src/VideoMonitor.Core/Catalog/CatalogRequests.cs`
- Create: `src/VideoMonitor.Core/Catalog/CatalogErrorDto.cs`
- Test: `tests/VideoMonitor.Core.Tests/Models/CatalogContractTests.cs`

**Interfaces:**

```csharp
public sealed record CatalogSnapshotDto(IReadOnlyList<DeviceGroupDto> Groups, IReadOnlyList<CameraDeviceDto> Devices);
public sealed record DeviceGroupDto(Guid Id, string Name, Guid? ParentId, int Sort, bool Enabled, long Revision);
public sealed record CameraChannelDto(Guid Id, Guid DeviceId, int ChannelNo, string ChannelName, StreamType StreamType, bool Enabled);
public sealed record CameraDeviceDto(Guid Id, Guid GroupId, string Name, string IpAddress, int SdkPort, int RtspPort, string Username, bool HasPassword, string Manufacturer, string Model, TransportMode TransportMode, bool Enabled, string Remark, long Revision, IReadOnlyList<CameraChannelDto> Channels);
public sealed record CameraChannelInput(Guid Id, int ChannelNo, string ChannelName, StreamType StreamType, bool Enabled);
public sealed record CreateGroupRequest(Guid Id, string Name, Guid? ParentId, int Sort, bool Enabled);
public sealed record UpdateGroupRequest(string Name, Guid? ParentId, int Sort, bool Enabled, long ExpectedRevision);
public sealed record CreateDeviceRequest(Guid Id, Guid GroupId, string Name, string IpAddress, int SdkPort, int RtspPort, string Username, string Password, string Manufacturer, string Model, TransportMode TransportMode, bool Enabled, string Remark, IReadOnlyList<CameraChannelInput> Channels);
public sealed record UpdateDeviceRequest(Guid GroupId, string Name, string IpAddress, int SdkPort, int RtspPort, string Username, string? NewPassword, string Manufacturer, string Model, TransportMode TransportMode, bool Enabled, string Remark, long ExpectedRevision, IReadOnlyList<CameraChannelInput> Channels);
public sealed record CatalogErrorDto(string Code, string Message, long? CurrentRevision = null);
```

- [ ] **Step 1: Write failing contract tests**

```csharp
[Fact]
public void CameraDeviceDto_DoesNotExposePasswordValue()
{
    var names = typeof(CameraDeviceDto).GetProperties().Select(p => p.Name).ToArray();
    Assert.Contains(nameof(CameraDeviceDto.HasPassword), names);
    Assert.DoesNotContain("Password", names);
    Assert.DoesNotContain("PasswordCiphertext", names);
}
```

Also assert only Create/Update request records carry secret write fields.

- [ ] **Step 2: Run and verify failure**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~CatalogContractTests
```

- [ ] **Step 3: Implement the exact records above**

No persistence-only type or ciphertext field belongs under `VideoMonitor.Core.Catalog`.

- [ ] **Step 4: Run focused tests**

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/VideoMonitor.Core/Catalog tests/VideoMonitor.Core.Tests/Models/CatalogContractTests.cs
git commit -m "feat: add central catalog contracts"
```

---

### Task 3: Central SQLite Repository Read/Create

**Files:**
- Create: `src/VideoMonitor.Infrastructure/Persistence/ICentralCatalogRepository.cs`
- Create: `src/VideoMonitor.Infrastructure/Persistence/CatalogRepositoryResult.cs`
- Create: `src/VideoMonitor.Infrastructure/Persistence/SqliteCentralCatalogRepository.cs`
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/SqliteCentralCatalogRepositoryTests.cs`

**Interfaces:**

```csharp
public enum CatalogRepositoryStatus { Success, NotFound, RevisionConflict, GroupNotEmpty, ChannelConflict }
public sealed record CatalogRepositoryResult<T>(CatalogRepositoryStatus Status, T? Value = default, long? CurrentRevision = null);
public sealed record CatalogRepositoryDeleteResult(CatalogRepositoryStatus Status, long? CurrentRevision = null);

public interface ICentralCatalogRepository
{
    Task<DeviceCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default);
    Task<DeviceGroup?> GetGroupAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CameraDevice?> GetDeviceAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CatalogRepositoryResult<DeviceGroup>> CreateGroupAsync(DeviceGroup group, CancellationToken cancellationToken = default);
    Task<CatalogRepositoryResult<CameraDevice>> CreateDeviceAsync(CameraDevice device, CancellationToken cancellationToken = default);
    Task<CatalogRepositoryResult<DeviceGroup>> UpdateGroupAsync(DeviceGroup group, long expectedRevision, CancellationToken cancellationToken = default);
    Task<CatalogRepositoryDeleteResult> DeleteGroupAsync(Guid id, long expectedRevision, CancellationToken cancellationToken = default);
    Task<CatalogRepositoryResult<CameraDevice>> UpdateDeviceAsync(CameraDevice device, string? newPassword, long expectedRevision, CancellationToken cancellationToken = default);
    Task<CatalogRepositoryDeleteResult> DeleteDeviceAsync(Guid id, long expectedRevision, CancellationToken cancellationToken = default);
}
```

Create device receives initial plaintext in `device.Password`, protects it before INSERT, and returns internal trusted Server model. Update ignores `device.Password`; `newPassword == null` preserves stored ciphertext.

- [ ] **Step 1: Write failing read/create tests**

Cover one consistent snapshot; group/device start revision 1; CameraDevice + channels create in one transaction; raw DB contains ciphertext and not plaintext; internal GetDevice decrypts correctly.

- [ ] **Step 2: Run and verify failure**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~SqliteCentralCatalogRepositoryTests
```

- [ ] **Step 3: Implement consistent read/create**

`GetCatalogAsync` reads groups/devices/channels in one deferred read transaction. Reuse the Stage 5A private-cache/WAL-safe connection pattern. Device create encrypts before/inside write flow and commits the aggregate once.

- [ ] **Step 4: Run tests**

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/VideoMonitor.Infrastructure/Persistence/ICentralCatalogRepository.cs src/VideoMonitor.Infrastructure/Persistence/CatalogRepositoryResult.cs src/VideoMonitor.Infrastructure/Persistence/SqliteCentralCatalogRepository.cs tests/VideoMonitor.Core.Tests/Infrastructure/SqliteCentralCatalogRepositoryTests.cs
git commit -m "feat: add central catalog repository"
```

---

### Task 4: Revision-Protected Update/Delete + Atomicity

**Files:**
- Modify: `src/VideoMonitor.Infrastructure/Persistence/SqliteCentralCatalogRepository.cs`
- Modify: `tests/VideoMonitor.Core.Tests/Infrastructure/SqliteCentralCatalogRepositoryTests.cs`

**Interfaces:** Task 3 signatures remain unchanged.

- [ ] **Step 1: Add failing concurrency/rollback tests**

```csharp
var a = await repository.GetDeviceAsync(id);
var b = await repository.GetDeviceAsync(id);
a!.Name = "A committed";
Assert.Equal(2, (await repository.UpdateDeviceAsync(a, null, a.Revision)).Value!.Revision);
b!.Name = "B stale";
var stale = await repository.UpdateDeviceAsync(b, null, b.Revision);
Assert.Equal(CatalogRepositoryStatus.RevisionConflict, stale.Status);
Assert.Equal(2, stale.CurrentRevision);
```

Also test stale delete, non-empty group delete, duplicate `(device_id, channel_no, stream_type)`, channel failure rollback, and password-protection failure preserving old ciphertext/revision.

- [ ] **Step 2: Run and verify failure**

Same focused repository test command.

- [ ] **Step 3: Implement guarded write inside the transaction**

Device parent row uses:

```sql
UPDATE camera_devices
SET group_id=$groupId, name=$name, ip_address=$ipAddress,
    sdk_port=$sdkPort, rtsp_port=$rtspPort, username=$username,
    password_ciphertext=$passwordCiphertext, manufacturer=$manufacturer,
    model=$model, transport_mode=$transportMode, enabled=$enabled,
    remark=$remark, revision=revision+1
WHERE id=$id AND revision=$expectedRevision;
```

If affected rows == 0, determine NotFound vs RevisionConflict inside the same transaction and roll back. Only after the guarded parent update succeeds may the channel set be replaced. Group update/delete follows the same pattern.

- [ ] **Step 4: Run repository tests**

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/VideoMonitor.Infrastructure/Persistence/SqliteCentralCatalogRepository.cs tests/VideoMonitor.Core.Tests/Infrastructure/SqliteCentralCatalogRepositoryTests.cs
git commit -m "feat: protect catalog writes with revisions"
```

---

### Task 5: Catalog Application Service

**Files:**
- Create: `src/VideoMonitor.Server/Catalog/CatalogOperationResult.cs`
- Create: `src/VideoMonitor.Server/Catalog/CatalogApplicationService.cs`
- Test: `tests/VideoMonitor.Server.Tests/CatalogApplicationServiceTests.cs`

**Interfaces:**

```csharp
public sealed record CatalogOperationResult<T>(bool IsSuccess, T? Value, int StatusCode, CatalogErrorDto? Error);
```

Service methods: GetCatalog/GetGroups/GetDevices/GetDevice/Create/Update/Delete group/device, each async and cancellation-aware.

Stable codes:

```text
CATALOG_VALIDATION_FAILED
DEVICE_NOT_FOUND
GROUP_NOT_FOUND
DEVICE_REVISION_CONFLICT
GROUP_REVISION_CONFLICT
GROUP_NOT_EMPTY
CHANNEL_CONFLICT
CATALOG_UNAVAILABLE
CATALOG_READ_FAILED
CATALOG_WRITE_FAILED
```

- [ ] **Step 1: Write failing validation/result tests**

Cover Guid.Empty, missing group, invalid IP, ports outside 1–65535, channel <=0, duplicate channel identity, invalid enum value, blank create password, `NewPassword=null`, RevisionConflict and GroupNotEmpty mappings.

- [ ] **Step 2: Run and verify failure**

```powershell
dotnet test tests/VideoMonitor.Server.Tests/VideoMonitor.Server.Tests.csproj --filter FullyQualifiedName~CatalogApplicationServiceTests
```

- [ ] **Step 3: Implement validation and safe DTO mapping**

`CameraDeviceDto.HasPassword = !string.IsNullOrEmpty(device.Password)`. Never copy password into read DTO/error text/log text.

- [ ] **Step 4: Run tests**

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/VideoMonitor.Server/Catalog tests/VideoMonitor.Server.Tests/CatalogApplicationServiceTests.cs
git commit -m "feat: add catalog application service"
```

---

### Task 6: Versioned REST API

**Files:**
- Create: `src/VideoMonitor.Server/Catalog/CatalogEndpoints.cs`
- Modify: `src/VideoMonitor.Server/Program.cs`
- Create: `tests/VideoMonitor.Server.Tests/CatalogApiTests.cs`

**Routes:**

```text
GET    /api/v1/catalog
GET    /api/v1/device-groups
POST   /api/v1/device-groups
PUT    /api/v1/device-groups/{id}
DELETE /api/v1/device-groups/{id}?expectedRevision={revision}
GET    /api/v1/devices?groupId={groupId}
GET    /api/v1/devices/{id}
POST   /api/v1/devices
PUT    /api/v1/devices/{id}
DELETE /api/v1/devices/{id}?expectedRevision={revision}
```

- [ ] **Step 1: Write failing WebApplicationFactory CRUD tests**

Use existing temporary `TestServerFactory`. Assert status codes, returned revisions, and safe DTOs.

- [ ] **Step 2: Run and verify failure**

```powershell
dotnet test tests/VideoMonitor.Server.Tests/VideoMonitor.Server.Tests.csproj --filter FullyQualifiedName~CatalogApiTests
```

- [ ] **Step 3: Register/map**

```csharp
builder.Services.AddSingleton<ICentralCatalogRepository, SqliteCentralCatalogRepository>();
builder.Services.AddSingleton<CatalogApplicationService>();
...
app.MapCatalogEndpoints();
```

Endpoint code only parses input, calls service, maps `CatalogOperationResult<T>` to `IResult`; no SQL/secret handling.

- [ ] **Step 4: Run API + existing health tests**

```powershell
dotnet test tests/VideoMonitor.Server.Tests/VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~CatalogApiTests|FullyQualifiedName~ServerHealthTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/VideoMonitor.Server/Program.cs src/VideoMonitor.Server/Catalog/CatalogEndpoints.cs tests/VideoMonitor.Server.Tests/CatalogApiTests.cs
git commit -m "feat: expose central catalog api"
```

---

### Task 7: API Concurrency + Secret Safety

**Files:**
- Create: `tests/VideoMonitor.Server.Tests/CatalogConcurrencyTests.cs`
- Create: `tests/VideoMonitor.Server.Tests/CatalogSecurityTests.cs`
- Modify production files only if a test exposes a real defect.

- [ ] **Step 1: Add two-client conflict tests**

Two separate `HttpClient`s read revision 1; A PUT succeeds revision 2; B PUT with expected 1 returns HTTP 409 `DEVICE_REVISION_CONFLICT` + `CurrentRevision=2`; final DB/API state equals A. Also stale DELETE -> 409 and non-empty group -> 409.

- [ ] **Step 2: Add secret leak tests**

Use test-only literal `stage5b-secret-P@55`. Assert it is absent from every GET/error response and raw DB plaintext search; raw `password_ciphertext` begins with expected protected-envelope prefix and is never returned by API.

- [ ] **Step 3: Run focused tests**

```powershell
dotnet test tests/VideoMonitor.Server.Tests/VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~CatalogConcurrencyTests|FullyQualifiedName~CatalogSecurityTests"
```

- [ ] **Step 4: Make minimal fixes only**

Do not expand scope or add auth/push/stream behavior.

- [ ] **Step 5: Run all Server tests**

```powershell
dotnet test tests/VideoMonitor.Server.Tests/VideoMonitor.Server.Tests.csproj
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add tests/VideoMonitor.Server.Tests src/VideoMonitor.Server/Catalog src/VideoMonitor.Infrastructure/Persistence/SqliteCentralCatalogRepository.cs
git commit -m "test: verify central catalog safety and concurrency"
```

---

### Task 8: Stage 5B-1 Verification Gate

**Files:** no production changes unless verification exposes a defect.

- [ ] **Step 1: Restore/build**

```powershell
dotnet restore VideoMonitor.sln
dotnet build VideoMonitor.sln --no-restore
```

Expected: 0 errors. Report exact warning count and distinguish new warnings from existing warnings.

- [ ] **Step 2: Core/Infrastructure tests**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --no-build
```

Expected: PASS.

- [ ] **Step 3: Server tests**

```powershell
dotnet test tests/VideoMonitor.Server.Tests/VideoMonitor.Server.Tests.csproj --no-build
```

Expected: PASS.

- [ ] **Step 4: Full solution tests**

```powershell
dotnet test VideoMonitor.sln --no-build
```

Expected: PASS.

- [ ] **Step 5: Secret/scope scan**

```powershell
git grep -n -i -E "stage5b-secret-P@55|rtsp://[^ ]+:[^ ]+@" -- src tests
git diff --check
git status --short
git log --oneline --decorate -10
```

Inspect every secret-scan hit; test fixtures may intentionally contain the literal, production response/logging code may not. `git diff --check` and worktree must be clean.

- [ ] **Step 6: Stop for Sol review**

Report every task commit SHA, exact build/test counts, warnings, changed-file summary, and any deviation. Do not start Stage 5B-2 and do not push `master` until review passes.
