# Device Catalog Backup Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 当正式设备目录损坏时，使用完整验证通过的 `.bak` 安全恢复，并向 WPF 用户明确提示恢复发生。

**Architecture:** `DeviceCatalogBootstrapper` 继续负责启动决策，只在正式 JSON 存在且加载/业务校验失败时调用小型恢复服务。恢复服务使用独立的 `JsonDeviceCatalogStore` 校验 `.bak`，先以唯一文件名保存损坏正式文件，再通过不轮换 `.bak` 的原子替换恢复正式路径，最后重新从正式 Store 加载并构造 Catalog。

**Tech Stack:** .NET 8, C#, WPF, `System.Text.Json`, Windows DPAPI `LocalMachine`, xUnit。

**Spec:** `C:\Users\Wan\.codex\attachments\a5e041ef-5fbc-4868-93d9-3b63a9541d21\pasted-text.txt`

## Global Constraints

- 保持 `SchemaVersion = 1`，不实现版本迁移。
- 不修改 `IDeviceCatalog`、`InMemoryDeviceCatalog`、Playback、ZLM、`MonitorSwitchService` 或 `DeviceCatalogPersistenceCoordinator`。
- 正式文件不存在时继续执行现有 LocalAppData migration / Mock 初始化，不读取 `.bak`。
- 正式文件存在但损坏且 `.bak` 无效时启动失败，不回退 Mock，不覆盖正式文件或 `.bak`。
- 恢复成功后不调用普通 `SaveAsync`，避免健康 `.bak` 被损坏正式文件覆盖。
- 不记录或显示密码、DPAPI 密文、RTSP URL、ZLM Secret 或原始 JSON。
- 使用 TDD：每个行为先补失败测试并确认 RED，再写最小生产实现。

### Task 1: Recovery decision and result state

**Files:**
- Modify: `src/VideoMonitor.Wpf/Configuration/DeviceCatalogBootstrapper.cs`
- Modify: `src/VideoMonitor.Infrastructure/Persistence/JsonDeviceCatalogStore.cs`
- Test: `tests/VideoMonitor.Core.Tests/Configuration/DeviceCatalogBootstrapperTests.cs`

**Interfaces:**
- `JsonDeviceCatalogStore.FilePath` exposes the normalized path needed only by the WPF startup recovery boundary.
- `JsonDeviceCatalogStore.ProtectionScope` lets recovery validate the sibling backup using the same DPAPI scope.
- `DeviceCatalogBootstrapper.RecoveryOccurred` is reset at each initialization and becomes `true` only after recovery and final reload succeed.

- [ ] **Step 1: Write failing tests** for a valid formal catalog with a valid `.bak` being loaded normally without recovery, and for a corrupt formal catalog triggering the recovery path only when the formal file exists.
- [ ] **Step 2: Run the focused tests** and confirm they fail because recovery state and decision path do not exist.
- [ ] **Step 3: Add the minimal store path/scope accessors and bootstrapper recovery decision/result state.** Keep the existing no-file path unchanged.
- [ ] **Step 4: Run the focused tests** and confirm they pass.

### Task 2: Validated backup recovery service

**Files:**
- Create: `src/VideoMonitor.Wpf/Configuration/DeviceCatalogRecoveryService.cs`
- Modify: `src/VideoMonitor.Wpf/Configuration/DeviceCatalogBootstrapper.cs`
- Test: `tests/VideoMonitor.Core.Tests/Configuration/DeviceCatalogBootstrapperTests.cs`

**Interfaces:**
- `DeviceCatalogRecoveryService.RecoverAsync(JsonDeviceCatalogStore formalStore, Func<DeviceCatalogSnapshot, InMemoryDeviceCatalog> catalogFactory, Exception formalLoadFailure, CancellationToken cancellationToken)` validates and restores a backup without using normal `SaveAsync`.
- Recovery returns the catalog constructed from a fresh reload of the formal path and throws safe data/operation exceptions on every failure branch.

- [ ] **Step 1: Write failing tests** for malformed formal JSON plus valid backup, unsupported backup schema, invalid backup DPAPI, invalid backup relationships, and missing backup.
- [ ] **Step 2: Run the focused tests** and confirm they fail because the bootstrapper currently stops at the formal load error and never reads `.bak`.
- [ ] **Step 3: Implement the smallest recovery service:** load the backup with the same JSON Store and DPAPI scope; validate through the existing Catalog factory; archive the corrupt formal bytes using a unique `device-catalog.corrupt-yyyyMMdd-HHmmss[-N].json` path; copy backup bytes to a same-directory temporary recovery file; flush it; call `File.Replace(temp, formal, null, true)` so `.bak` is untouched; reload formal through the original Store and construct the final Catalog.
- [ ] **Step 4: Run the focused tests** and confirm valid recovery and all rejection paths pass.

### Task 3: Corrupt evidence and failure-path coverage

**Files:**
- Modify: `src/VideoMonitor.Wpf/Configuration/DeviceCatalogRecoveryService.cs`
- Modify: `src/VideoMonitor.Wpf/Configuration/DeviceCatalogBootstrapper.cs`
- Test: `tests/VideoMonitor.Core.Tests/Configuration/DeviceCatalogBootstrapperTests.cs`

- [ ] **Step 1: Write failing tests** for repeated corrupt archives, corrupt-archive creation failure, restore failure, post-restore reload failure, healthy `.bak` preservation, and the distinction between missing formal file and corrupt formal file.
- [ ] **Step 2: Run the focused tests** and confirm each failure exposes a missing safety guarantee rather than a test setup error.
- [ ] **Step 3: Add unique archive creation with `CreateNew`, cleanup only of a newly-created partial archive, recovery temp cleanup that never masks the original exception, and no Mock fallback after any recovery failure.
- [ ] **Step 4: Run the focused tests** and confirm formal/backup/corrupt evidence invariants pass.

### Task 4: WPF recovery notification

**Files:**
- Modify: `src/VideoMonitor.Wpf/App.xaml.cs`
- Modify: `tests/VideoMonitor.Core.Tests/Configuration/DeviceCatalogBootstrapperTests.cs`

- [ ] **Step 1: Write a failing source-wiring test** requiring the explicit recovery state to flow from Bootstrapper to App and a generic user-facing recovery notification.
- [ ] **Step 2: Run the focused test** and confirm the current App has no recovery notification.
- [ ] **Step 3: Read the state once after bootstrap, show one safe message only when `RecoveryOccurred` is true, and keep startup failures generic/safe without exposing exception details.
- [ ] **Step 4: Run the focused test** and confirm it passes.

### Task 5: Full verification and commit

**Files:**
- Verify only; no additional production scope.

- [ ] **Step 1: Run** `git diff --check`.
- [ ] **Step 2: Run** `dotnet build VideoMonitor.sln` and `dotnet test VideoMonitor.sln`; require at least the original 153 tests plus all recovery tests.
- [ ] **Step 3: Perform the manual recovery check using a protected copy of the current ProgramData files; restore the protected copies afterward and verify the second launch has no recovery prompt.
- [ ] **Step 4: Review staged diff and commit with** `fix: recover device catalog from validated backup`.
- [ ] **Step 5: Re-run status, build, and tests after commit; do not push.**
