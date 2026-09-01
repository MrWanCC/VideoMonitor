# Stage 5C — Central Media Pipeline & Stream Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在已批准的 Stage 5C 设计边界内，为中央 Server 建立受控的媒体配置、ZLMediaKit 流生命周期、播放授权、真实测试流、正式 WPF 播放和最小媒体诊断能力，并以硬件验收证明完整链路。

**Architecture:** Server SQLite 继续是 Catalog 与媒体配置的权威来源。Server 内部使用受机器保护的密钥解析摄像头和 ZLMediaKit 凭据；`StreamManager` 按 `MediaStreamKey` 串行化流操作并维护运行时所有权。WPF 正式播放只提交设备/通道 ID，通过 Server 获取短期播放票据和安全播放地址；`SingleCameraTest.Enabled=true` 时继续使用现有本地兼容组合。运行时媒体状态只在内存中维护，不写入 Catalog SQLite。

**Tech Stack:** .NET 8、ASP.NET Core Minimal API、`Microsoft.Data.Sqlite` 8.0.0、现有 MasterKeyProvider/AesGcmSecretProtector、现有 `ZlmClient`、WPF、LibVLCSharp、xUnit、`Microsoft.AspNetCore.Mvc.Testing`。

**Spec:** `docs/superpowers/specs/2026-09-01-stage-5c-central-media-pipeline-design.md`

## Global Constraints

- 本计划按 Task 1 到 Task 8 顺序执行；每个 Task 都有独立 review branch、独立 commit 和独立 Sol review gate。禁止并行实现后续 Task。
- 每个实现 Task 都先写失败测试，运行精确 focused filter 确认失败，再写最小实现；focused、项目回归、solution test、build、静态安全扫描和 `git diff --check` 全部通过后才能提交。
- 每个 Task 从合并后的最新 `master` 创建 `review/stage-5c-taskN-<short-description>` 分支。实现者提交并推送 review branch 后停止，等待 Sol 独立 GitHub source review；只有批准后才允许在 `master` 上执行 `git merge --ff-only`。
- `Server SQLite` 是唯一正式 Catalog 权威。正式 WPF 写入只能通过 Server API；客户端 cache 仅在 Server 成功响应后更新。
- 正式中央路径使用 `CatalogSnapshotDto` 和其它 password-safe read model，绝不把完整 `CameraDevice` 传入 Monitor、Secondary Monitor 或正式播放路径。
- `CameraDevice.Password`、数据库 `password_ciphertext`、ZLM Secret、签名密钥、credential-bearing RTSP URI 只能存在于受限的 Server/Infrastructure 内部；不得进入 DTO、异常、日志、诊断、遥测、WPF cache 或计划文档。
- `CameraStatus`、`StreamId`、`MediaServerHealth`、`StreamRuntimeState`、`SourceObservation`、`ViewerCount`、ownership 和 session 均为运行时数据，不写入 Catalog SQLite。
- `LocalZlmPlaybackSourceProvider`、`ApplicationCatalogComposition` 的 SingleCameraTest 本地兼容路径继续可用；正式中央 WPF 不直接调用 ZLM，不构造摄像头 RTSP。
- 不引入 User Login、JWT、RBAC、Client Enrollment、账号权限系统、Token state table、WebSocket、SSE、SignalR、VMS 后台、历史媒体分析或 ZLM 集群 failover。
- 所有 HTTP 客户端只接受标准 absolute HTTP/HTTPS URI；开发和受控调试允许 HTTP，生产部署要求 HTTPS；任何代码都不得绕过 TLS certificate validation。
- 不能使用 `.Result`、`.Wait()` 或 `GetAwaiter().GetResult()`。取消必须沿 async 链传播，后台循环必须有明确的 shutdown token。
- 不修改既有 warning debt；每次 build 要求 0 errors，且没有由当前 Task 新增的 warning。记录本轮实际 warning 数量，与合并前基线比较。
- 计划中的测试不能使用真实摄像头密码、ZLM Secret 或生产 URL；硬件验收记录只写脱敏的主机标识和结果。

## Dependency and Type Ledger

下表锁定跨 Task 类型，后续 Task 不得重新定义同名替代品：

| Producer | Types made available | Consumers |
| --- | --- | --- |
| Task 1 | `MediaSettingsDto`, `UpdateMediaSettingsRequest`, `TestMediaSettingsRequest`, `IMediaSettingsRepository`, `IMediaSettingsService` | Task 2, Task 3, Task 4, Task 7 |
| Task 2 | `MediaStreamKey`, `MediaStreamNamespace`, `MediaStreamRequest`, `ZlmMediaEvidence`, `IZlmMediaGateway`, `ICameraSourceResolver`, `SourceBindingResult` | Task 3 |
| Task 3 | `IStreamManager`, `StreamEnsureResult`, `FormalStreamDescriptor`, `StreamOwnership`, `MediaServerHealth`, `StreamRuntimeState`, `SourceObservation`, `MediaRuntimeSnapshot` | Task 4, Task 5, Task 6, Task 7 |
| Task 4 | `PlaybackTicketIssuer`, `PlaybackTicketValidator`, `PlaybackTicket`, `PlaybackTicketValidationResult`, `PlaybackAuthorizationResult` | Task 5, Task 6 |
| Task 5 | `TestStreamStartRequest`, `TestSessionDto`, `ITestStreamService`, `TestStreamApiClient`, `TestPreviewSource` | Task 8 and existing WPF test-preview surface |
| Task 6 | `IFormalPlaybackSourceProvider`, `FormalPlaybackSource`, `PlaybackRuntimeEvent`, `IPlaybackRuntimeEventSink`, `FormalPlaybackCoordinator` | Task 7 and acceptance |
| Task 7 | `MediaDiagnosticsSnapshotDto`, `MediaStreamDiagnosticsDto`, `IMediaDiagnosticsReadModel` | WPF diagnostics and Task 8 |

Every new public contract must have one owning layer and one owning Task. Internal evidence and credential resolver types must remain internal to Server/Infrastructure.

## Shared Verification Commands

Unless a Task gives a narrower command, use the real projects in the solution:

```powershell
dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj
dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj
dotnet test .\VideoMonitor.sln -c Debug
dotnet build .\VideoMonitor.sln -c Debug
```

For a changed WPF or infrastructure implementation, also run `dotnet build .\VideoMonitor.sln -c Debug -t:Rebuild` before committing. Every Task performs:

```powershell
git diff --check
git diff --name-only <task-base>..HEAD
$tokens = @('T'+'BD','TO'+'DO','FIX'+'ME','implement '+'later','similar '+'to','appropriate '+'tests','add '+'error handling')
rg -n ($tokens -join '|') <changed-files>
rg -n "Password|password_ciphertext|Zlm Secret|secret|originUrl|rtsp://|Data Source=" <changed-files>
```

The second scan is reviewed rather than blindly rejected: legitimate private field names and test placeholders must not cause secrets or credential-bearing values to be logged, returned, serialized, or committed.

---

## Task 1 — Media Settings & Secret Storage

**Files:**

- Create: `src/VideoMonitor.Core/Media/MediaSettingsDto.cs` — password-safe `MediaSettingsDto` with `ZlmApiBaseUrl`, `PlaybackBaseUrl`, `Vhost`, `FormalApp`, `TestApp`, `HasSecret`, `NoReaderGraceSeconds`, `Revision`.
- Create: `src/VideoMonitor.Core/Media/MediaSettingsRequests.cs` — `UpdateMediaSettingsRequest` and `TestMediaSettingsRequest`; the test request may carry a candidate secret only over the protected Server request boundary and is never returned.
- Create: `src/VideoMonitor.Infrastructure/Persistence/IMediaSettingsRepository.cs` — persistence contract.
- Create: `src/VideoMonitor.Infrastructure/Persistence/SqliteMediaSettingsRepository.cs` — transactional read/update implementation over `server_settings` or its approved V3 extension.
- Modify: `src/VideoMonitor.Infrastructure/Persistence/SqliteDatabaseInitializer.cs:CurrentSchemaVersion and migration methods` — add the next schema migration required by the approved design, preserving the existing V3 history and schema version discipline.
- Create: `src/VideoMonitor.Server/Media/MediaSettingsService.cs` — validation, revision checks, secret-preservation semantics and non-persistent test operation.
- Create: `src/VideoMonitor.Server/Media/MediaSettingsEndpoints.cs` — `GET /api/v1/media/settings`, `PUT /api/v1/media/settings`, `POST /api/v1/media/settings/test`.
- Modify: `src/VideoMonitor.Server/Program.cs` — register repository/service and map the media settings endpoints.
- Create: `src/VideoMonitor.Wpf/Catalog/MediaSettingsApiClient.cs` — safe read/update/test client using the existing absolute HTTP/HTTPS and `CatalogApiException` conventions.
- Create: `src/VideoMonitor.Wpf/ViewModels/MediaSettingsViewModel.cs` — settings editor and test/save state; no camera fetch or playback operation.
- Modify: `src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml` — bind the media settings view without showing secret/ciphertext.
- Modify: `src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml.cs` — only the required view hookup.
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/SqliteMediaSettingsRepositoryTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/MediaSettingsApiTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/Catalog/MediaSettingsApiClientTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/ViewModels/MediaSettingsViewModelTests.cs`.

**Interfaces**

- Consumes: existing `SqliteConnectionFactory`, `IMasterKeyProvider`, `ISecretProtector`, `CatalogOperationResult<T>`, `CatalogApiException`, and `ServerReadinessState`.
- Produces: `MediaSettingsDto`, `UpdateMediaSettingsRequest`, `TestMediaSettingsRequest`, `IMediaSettingsRepository`, `IMediaSettingsService`.

Lock these signatures before implementation:

```csharp
public sealed record MediaSettingsDto(
    string ZlmApiBaseUrl,
    string PlaybackBaseUrl,
    string Vhost,
    string FormalApp,
    string TestApp,
    bool HasSecret,
    int NoReaderGraceSeconds,
    long Revision);

public sealed record UpdateMediaSettingsRequest(
    string ZlmApiBaseUrl,
    string PlaybackBaseUrl,
    string Vhost,
    string FormalApp,
    string TestApp,
    string? ZlmSecret,
    int NoReaderGraceSeconds,
    long ExpectedRevision);

public sealed record TestMediaSettingsRequest(
    string ZlmApiBaseUrl,
    string PlaybackBaseUrl,
    string Vhost,
    string FormalApp,
    string TestApp,
    string? ZlmSecret,
    int NoReaderGraceSeconds);

public interface IMediaSettingsRepository
{
    Task<MediaSettingsDto> GetAsync(CancellationToken cancellationToken = default);
    Task<CatalogRepositoryResult<MediaSettingsDto>> UpdateAsync(
        UpdateMediaSettingsRequest request,
        CancellationToken cancellationToken = default);
}

public interface IMediaSettingsService
{
    Task<MediaSettingsDto> GetAsync(CancellationToken cancellationToken = default);
    Task<CatalogOperationResult<MediaSettingsDto>> UpdateAsync(
        UpdateMediaSettingsRequest request,
        CancellationToken cancellationToken = default);
    Task<CatalogOperationResult<MediaSettingsTestResult>> TestAsync(
        TestMediaSettingsRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record MediaSettingsTestResult(
    bool IsReachable,
    string? FailureCode);
```

The initial persisted fields are exactly `ZlmApiBaseUrl`, `PlaybackBaseUrl`, `Vhost`, `FormalApp`, `TestApp`, protected `ZlmSecretCiphertext`, `HasSecret` projection, `NoReaderGraceSeconds`, and `Revision`. Defaults are `FormalApp = videomonitor`, `TestApp = videomonitor-test`, and `NoReaderGraceSeconds = 30`. The stored secret uses the existing MasterKeyProvider plus AesGcmSecretProtector purpose-separated pattern; plaintext is never written to SQLite.

### TDD steps

- [ ] Add `SqliteMediaSettingsRepositoryTests.DefaultsAreCreatedWithExpectedNamespaceAndRevision`, arrange a fresh temporary database, initialize it, and assert the three defaults, `Revision == 1`, and `HasSecret == false`.
- [ ] Add `SqliteMediaSettingsRepositoryTests.UpdateProtectsSecretAndGetNeverReturnsCiphertext`, arrange a non-empty candidate secret, assert the raw stored value is an encrypted envelope through a private test query, and assert the DTO exposes only `HasSecret == true`.
- [ ] Add `SqliteMediaSettingsRepositoryTests.NullOrBlankSecretPreservesExistingProtectedValue`, arrange an existing protected value, update with null and blank values, and assert the raw value is byte-for-byte unchanged.
- [ ] Add `SqliteMediaSettingsRepositoryTests.StaleRevisionDoesNotChangeSettings`, arrange Revision 1, submit ExpectedRevision 0, and assert a conflict with every persisted field unchanged.
- [ ] Run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SqliteMediaSettingsRepositoryTests"` and record the expected missing-type/behavior RED.
- [ ] Implement only the repository schema, protected value mapping, defaults, and revision transaction needed to satisfy those tests.
- [ ] Add `MediaSettingsApiTests.GetNeverReturnsSecretOrCiphertext`, `MediaSettingsApiTests.PutUsesExpectedRevisionAndReturns409OnConflict`, `MediaSettingsApiTests.PostTestDoesNotPersistCandidate`, and `MediaSettingsApiTests.BlankEditSecretPreservesExistingSecret`; assert status codes and safe JSON fields, never secret values.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaSettingsApiTests"`; make the endpoint tests pass with `GET /api/v1/media/settings`, `PUT /api/v1/media/settings`, and `POST /api/v1/media/settings/test`.
- [ ] Add `MediaSettingsApiClientTests.GetAndPutUseVersionedMediaSettingsPaths` and `MediaSettingsViewModelTests.TestDoesNotSaveOrStartCamera`; assert the WPF layer has no password-bearing read model and does not call a camera/catalog playback API.
- [ ] Implement the WPF client/view using existing `CatalogApiException` mapping and safe state messages.
- [ ] Run focused Core and Server media-settings tests, then the full Core, Server and solution suites; build/rebuild and run the shared security/diff scans.
- [ ] Commit on `review/stage-5c-task1-media-settings` with `feat: add media settings and secret storage`, push only that review branch, and stop for Sol review.

### Acceptance

The settings screen can read and validate media configuration, update only with the expected Revision, preserve a null/blank edit secret, replace a non-empty secret through protection, and run a candidate connectivity test without saving it. GET and all WPF read models contain no secret or ciphertext. Task 1 does not resolve or pull a camera.

## Task 2 — Server Media Foundation

**Files:**

- Create: `src/VideoMonitor.Core/Media/MediaStreamKey.cs` — immutable `DeviceId + ChannelId + StreamType` identity and deterministic formal stream ID input.
- Create: `src/VideoMonitor.Server/Media/MediaStreamRequest.cs` — Server-internal request containing namespace, stream identity and source URI; no Core or public DTO exposes the source URI.
- Create: `src/VideoMonitor.Infrastructure/ZLMediaKit/IZlmMediaGateway.cs` — gateway contract over existing ZLM calls.
- Modify: `src/VideoMonitor.Infrastructure/ZLMediaKit/ZlmClient.cs` — implement the gateway and extend existing media calls; do not create a parallel HTTP client.
- Modify: `src/VideoMonitor.Infrastructure/ZLMediaKit/ZlmStreamInfo.cs` — deserialize `schema`, `vhost`, `app`, `stream`, `originType`, `originTypeStr`, `originUrl`, `createStamp`, `aliveSecond`, and `totalReaderCount`.
- Create: `src/VideoMonitor.Infrastructure/ZLMediaKit/ZlmMediaEvidence.cs` — internal evidence record with the complete fields above.
- Create: `src/VideoMonitor.Infrastructure/ZLMediaKit/MediaStreamIdGenerator.cs` — formal deterministic ID derived from `MediaStreamKey`, not names.
- Create: `src/VideoMonitor.Server/Media/ICameraSourceResolver.cs` — Server-internal credential resolution contract.
- Create: `src/VideoMonitor.Server/Media/CameraSourceResolver.cs` — read authoritative Catalog device/channel and decrypt only inside the Server media boundary.
- Create: `src/VideoMonitor.Server/Media/SourceBindingVerifier.cs` — compare source binding and return only `Matched`, `Mismatch`, or `InsufficientEvidence`.
- Create: `src/VideoMonitor.Server/Media/SourceBindingResult.cs` — safe status types with no URI/password fields.
- Modify: `src/VideoMonitor.Infrastructure/Hikvision/HikvisionRtspUrlBuilder.cs` — reuse the existing builder through the Server resolver and add only required special-character coverage.
- Modify: `src/VideoMonitor.Server/Program.cs` — register gateway, resolver and source-binding services.
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/MediaStreamKeyTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/ZlmClientTests.cs` — extend the existing test file for evidence fields and safe request construction.
- Test: `tests/VideoMonitor.Server.Tests/Media/CameraSourceResolverTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/SourceBindingVerifierTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/HikvisionRtspUrlBuilderTests.cs` — extend existing URI tests.

**Interfaces**

- Consumes: Task 1 `MediaSettingsDto`/`IMediaSettingsRepository`, existing `CameraDeviceDto`, `CameraChannelDto`, `ICentralCatalogRepository`, `ISecretProtector`, `ZlmClient`, and `HikvisionRtspUrlBuilder`.
- Produces: `MediaStreamKey`, `MediaStreamNamespace`, `MediaStreamRequest`, `IZlmMediaGateway`, `ZlmMediaEvidence`, `ICameraSourceResolver`, and `SourceBindingResult`.

Lock these signatures:

```csharp
public readonly record struct MediaStreamKey(
    Guid DeviceId,
    Guid ChannelId,
    StreamType StreamType)
{
    public string ToFormalStreamId() =>
        $"device_{DeviceId:N}_channel_{ChannelId:N}_{StreamType.ToString().ToLowerInvariant()}";
}

public enum MediaStreamNamespace { Formal, Test }

public sealed record MediaStreamRequest(
    MediaStreamNamespace Namespace,
    MediaStreamKey? CatalogKey,
    string Vhost,
    string App,
    string Stream,
    Uri SourceUri);

public sealed record ResolvedCameraSource(
    MediaStreamKey Key,
    Uri SourceUri,
    string SourceBindingFingerprint);

public sealed record ZlmMediaEvidence(
    string Schema,
    string Vhost,
    string App,
    string Stream,
    int? OriginType,
    string? OriginTypeStr,
    string? OriginUrl,
    long? CreateStamp,
    long? AliveSecond,
    int TotalReaderCount);

public interface IZlmMediaGateway
{
    Task<ZlmApiResponse<IReadOnlyList<ZlmMediaEvidence>>> GetMediaListAsync(
        string vhost, string app, string stream, CancellationToken cancellationToken = default);
    Task<ZlmApiResponse<ZlmAddStreamProxyData>> AddStreamProxyAsync(
        string vhost, string app, string stream, Uri sourceUri, CancellationToken cancellationToken = default);
    Task<ZlmApiResponse<ZlmDeleteStreamProxyData>> DeleteStreamProxyAsync(
        string proxyKey, CancellationToken cancellationToken = default);
    Task<ZlmApiResponse<JsonElement>> CloseExactStreamAsync(
        string schema, string vhost, string app, string stream, CancellationToken cancellationToken = default);
}

public interface ICameraSourceResolver
{
    Task<ResolvedCameraSource> ResolveAsync(
        MediaStreamKey key, CancellationToken cancellationToken = default);
}

public enum SourceBindingResult { Matched, Mismatch, InsufficientEvidence }
```

`ZlmMediaEvidence.OriginUrl` is internal-only and credential-bearing. It may be used by `SourceBindingVerifier` and ownership proof, but it must never be part of a public DTO or diagnostic result. URI construction must pass tests containing `@`, `%`, `#`, `&`, and `:` in credentials. Request failure messages and logs contain only safe categories and exception type names.

### TDD steps

- [ ] Add `MediaStreamKeyTests.FormalIdIsStableForSameIdentityAndIgnoresNames`, assert the same IDs for the same GUIDs/types and different IDs for different channel/type values.
- [ ] Add `ZlmClientTests.GetMediaListParsesCompleteEvidenceWithoutLoggingOriginUrl` and extend the existing request test to assert encoded query values; use a fake HTTP handler and no real secret.
- [ ] Add `HikvisionRtspUrlBuilderTests.SpecialCharactersRemainInUriComponents` for each of `@`, `%`, `#`, `&`, and `:` and assert only URI component round-trip, not a printed full credential URI.
- [ ] Add `CameraSourceResolverTests.ResolveUsesCatalogIdentityAndProtectsOnlyInsideServerBoundary` and `SourceBindingVerifierTests.ReturnsInsufficientEvidenceWhenOriginOrIdentityEvidenceIsMissing`; assert safe status values.
- [ ] Run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~MediaStreamKeyTests|FullyQualifiedName~ZlmClientTests|FullyQualifiedName~HikvisionRtspUrlBuilderTests"` and record RED for missing contracts/fields before implementation.
- [ ] Implement the smallest gateway extension, evidence mapping, deterministic key, resolver and verifier; reuse `ZlmClient` and the existing RTSP builder.
- [ ] Run the same focused filter and the Server media filter `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~CameraSourceResolverTests|FullyQualifiedName~SourceBindingVerifierTests"`; confirm GREEN.
- [ ] Run full Core, Server and solution tests, build/rebuild, the credential/log scan, `git diff --check`, and changed-file scan.
- [ ] Commit on `review/stage-5c-task2-media-foundation` with `feat: add server media foundation`, push only that review branch, and stop for Sol review.

### Acceptance

The Server can derive a stable formal stream identity, query and parse complete ZLM evidence through the existing client, resolve camera source credentials internally, and compare source binding without leaking secrets. No public Catalog DTO or WPF cache gains a password or `originUrl`.

## Task 3 — StreamManager & Runtime Reconciliation

**Files:**

- Create: `src/VideoMonitor.Server/Media/StreamManager.cs` — public orchestration facade only; keep raw evidence, scheduling and hook parsing elsewhere.
- Create: `src/VideoMonitor.Server/Media/IStreamManager.cs` — `EnsureStreamAsync`, exact release operations and safe result contract.
- Create: `src/VideoMonitor.Server/Media/StreamEnsureResult.cs` — success descriptor or safe failure category.
- Create: `src/VideoMonitor.Server/Media/FormalStreamDescriptor.cs` — non-secret vhost/app/stream identity used by Task 4.
- Create: `src/VideoMonitor.Server/Media/MediaRuntimeRegistry.cs` — in-memory state and ownership registry.
- Create: `src/VideoMonitor.Server/Media/IMediaRuntimeStore.cs` — snapshot/read/update contract for runtime state.
- Create: `src/VideoMonitor.Server/Media/MediaStreamGate.cs` — per-key `SemaphoreSlim` gate, not a global lock.
- Create: `src/VideoMonitor.Server/Media/MediaOwnershipClassifier.cs` — `OwnedCurrentProcess`, `OwnedAdopted`, `NotOwned`, `External` classification.
- Create: `src/VideoMonitor.Server/Media/MediaReconcilerHostedService.cs` — startup, recovery and bounded periodic reconcile loop.
- Create: `src/VideoMonitor.Server/Media/MediaHookEndpoints.cs` — fast enqueue endpoints for `on_stream_changed` and `on_stream_none_reader`.
- Create: `src/VideoMonitor.Server/Media/MediaEventProcessor.cs` — background event processing, no heavy work in hook request.
- Create: `src/VideoMonitor.Server/Media/MediaServerHealthState.cs` — `Unconfigured`, `Healthy`, `Unavailable`, `ConfigurationError`.
- Create: `src/VideoMonitor.Server/Media/MediaStreamRuntimeState.cs` — `Idle`, `Starting`, `Ready`, `Stopping`, `Faulted`, source observation and viewer count.
- Modify: `src/VideoMonitor.Server/Program.cs` — register runtime singletons, hosted reconciler and hook endpoints.
- Test: `tests/VideoMonitor.Server.Tests/Media/StreamManagerTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/MediaOwnershipClassifierTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/MediaReconcilerHostedServiceTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/MediaHookTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/MediaRuntimeRegistryTests.cs`.

**Interfaces**

- Consumes: Task 1 media settings and Task 2 `MediaStreamKey`, `MediaStreamRequest`, `IZlmMediaGateway`, `ICameraSourceResolver`, `ZlmMediaEvidence`, `SourceBindingResult`.
- Produces: `IStreamManager`, `StreamEnsureResult`, `FormalStreamDescriptor`, `StreamOwnership`, `MediaServerHealth`, `StreamRuntimeState`, `SourceObservation`, `ViewerCount`, and `MediaRuntimeSnapshot`.

Lock these signatures:

```csharp
public interface IStreamManager
{
    Task<StreamEnsureResult> EnsureStreamAsync(
        MediaStreamRequest request,
        CancellationToken cancellationToken = default);
    Task ReleaseOwnedStreamAsync(
        MediaStreamKey key, CancellationToken cancellationToken = default);
    MediaRuntimeSnapshot GetSnapshot();
}

public sealed record StreamEnsureResult(
    bool IsSuccess,
    FormalStreamDescriptor? Stream,
    string? FailureCode);

public sealed record FormalStreamDescriptor(
    string Vhost,
    string App,
    string Stream,
    MediaStreamKey Key);

public enum StreamOwnership { OwnedCurrentProcess, OwnedAdopted, NotOwned, External }
public enum MediaServerHealth { Unconfigured, Healthy, Unavailable, ConfigurationError }
public enum StreamRuntimeState { Idle, Starting, Ready, Stopping, Faulted }
public enum SourceObservation { Unknown, Reachable, ConnectFailed, AuthFailed }
public readonly record struct ViewerCount(int Value);

public sealed record MediaRuntimeSnapshot(
    MediaServerHealth ServerHealth,
    IReadOnlyList<MediaStreamRuntimeInfo> Streams);

public sealed record MediaStreamRuntimeInfo(
    MediaStreamKey Key,
    StreamRuntimeState RuntimeState,
    SourceObservation SourceObservation,
    ViewerCount ViewerCount,
    StreamOwnership Ownership,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? LastSuccessUtc,
    string? SafeLastError);
```

`EnsureStreamAsync` acquires only the gate for its `MediaStreamKey`, queries ZLM first, and reuses an existing stream only after all proof is present: configured vhost, configured FormalApp, deterministic identity, live Catalog identity, pull/proxy-compatible origin, source binding match, and ownership allowed. A matching schema/vhost/app/stream with failed proof is `MediaStreamIdentityConflict`: no reuse, delete, overwrite, or duplicate `addStreamProxy`. `OwnedCurrentProcess` retains the exact returned ProxyKey. `OwnedAdopted` may be exact-closed after restart only with full proof; `NotOwned` and `External` are never deleted.

After a successful `addStreamProxy` the manager polls media evidence until registration is real; ZLM code 0 alone is insufficient. Retry is bounded. Reconciliation runs at startup, after media-server recovery and about every 30 seconds without overlap; unavailable ZLM uses `5s -> 10s -> 30s -> 60s` backoff. Formal no-reader cleanup uses actual ZLM reader count and the Task 1 grace setting, default 30 seconds. Restart-adopted cleanup uses exact `schema + vhost + app + stream` close only.

### TDD steps

- [ ] Add `StreamManagerTests.ConcurrentEnsureForSameKeyUsesOneAddProxy`, `StreamManagerTests.ReadyEvidenceRequiresRegistration`, and `StreamManagerTests.AddProxySuccessWithoutMediaRegistrationFailsAndCleansOwnedProxy`; assert per-key serialization and bounded cleanup.
- [ ] Add `StreamManagerTests.NotOwnedIdentityConflictFailsClosedWithoutDeleteOrAdd`, asserting no reuse, delete, overwrite, or duplicate add for an occupied exact identity.
- [ ] Add `MediaOwnershipClassifierTests.RestartAdoptionRequiresAllProof`, `MediaOwnershipClassifierTests.MissingEvidenceIsNotOwned`, and `MediaOwnershipClassifierTests.CurrentProcessRetainsProxyKey`; cover vhost, FormalApp, deterministic key, Catalog identity, origin type, source binding and ownership.
- [ ] Add `MediaReconcilerHostedServiceTests.StartupAndRecoveryReconcileDoNotOverlap`, `MediaReconcilerHostedServiceTests.UnavailableServerUsesBoundedBackoff`, and `MediaReconcilerHostedServiceTests.NoReaderUsesConfiguredGracePeriod`; assert cancellation stops the loop.
- [ ] Add `MediaHookTests.HookOnlyEnqueuesAndDoesNotRunZlmWorkInline` and `MediaRuntimeRegistryTests.RuntimeSnapshotContainsNoSecretOrOriginUrl`.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~StreamManagerTests|FullyQualifiedName~MediaOwnershipClassifierTests|FullyQualifiedName~MediaReconcilerHostedServiceTests|FullyQualifiedName~MediaHookTests|FullyQualifiedName~MediaRuntimeRegistryTests"`; record RED before implementation.
- [ ] Implement the split runtime files in dependency order: state/registry, per-key gate, ownership proof, manager, hook queue, reconciler and DI. Do not place raw ZLM DTOs, ticket code or HTTP endpoint logic in `StreamManager.cs`.
- [ ] Re-run the exact focused filter and assert GREEN; then run all Server tests, all Core tests, solution tests, build/rebuild, secret scan and diff checks.
- [ ] Commit on `review/stage-5c-task3-stream-manager` with `feat: add managed media stream lifecycle`, push only that review branch, and stop for Sol review.

### Acceptance

Concurrent callers for one key cannot create duplicate upstreams; unrelated keys can proceed independently. Identity collisions fail closed. Current-process and proven restart-adopted streams have bounded exact cleanup. Missing or suspicious evidence remains `NotOwned`. Runtime and logs contain no source URL or secret.

## Task 4 — Playback Authorization

**Files:**

- Create: `src/VideoMonitor.Server/Playback/PlaybackTicket.cs` — payload and serialized ticket contract.
- Create: `src/VideoMonitor.Server/Playback/PlaybackTicketIssuer.cs` — stateless HMAC issuer.
- Create: `src/VideoMonitor.Server/Playback/PlaybackTicketValidator.cs` — constant-time signature/claim validation.
- Create: `src/VideoMonitor.Server/Playback/PlaybackSigningKeyProvider.cs` — machine-protected independent signing key.
- Create: `src/VideoMonitor.Server/Playback/PlaybackAuthorizationResult.cs` — safe success/failure categories.
- Create: `src/VideoMonitor.Server/Playback/PlaybackAuthorizationEndpoints.cs` — issue endpoint and ZLM `on_play` validation endpoint.
- Modify: `src/VideoMonitor.Server/Program.cs` — register issuer, validator, key provider and map endpoints.
- Test: `tests/VideoMonitor.Server.Tests/Playback/PlaybackTicketIssuerTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Playback/PlaybackTicketValidatorTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Playback/PlaybackAuthorizationTests.cs`.

**Interfaces**

- Consumes: Task 3 `IStreamManager` and `FormalStreamDescriptor`, Task 1 media settings, current-process machine protection, actual ZLM `vhost/app/stream` callback fields.
- Produces: `IPlaybackTicketIssuer`, `IPlaybackTicketValidator`, `PlaybackTicket`, `PlaybackTicketValidationResult`, `PlaybackAuthorizationResult`.

Lock these signatures:

```csharp
public interface IPlaybackTicketIssuer
{
    Task<PlaybackTicket> IssueAsync(
        FormalStreamDescriptor stream,
        CancellationToken cancellationToken = default);
}

public interface IPlaybackTicketValidator
{
    PlaybackTicketValidationResult Validate(
        string? encodedTicket,
        string actualVhost,
        string actualApp,
        string actualStream,
        DateTimeOffset now);
}

public sealed record PlaybackTicket(
    string Value,
    string Vhost,
    string App,
    string Stream,
    DateTimeOffset ExpiresUtc);

public sealed record PlaybackTicketValidationResult(
    bool IsValid,
    string? FailureCode);

public sealed record PlaybackAuthorizationResult(
    bool IsSuccess,
    Uri? PlaybackUrl,
    string? FailureCode);
```

The HMAC payload binds `Vhost`, `App`, `StreamId`, `ExpiresUtc`, and a random `Nonce`. The signing key is a separate machine-protected value and is not derived from camera password or ZLM Secret. Ticket lifetime is a 60-second connection authorization window, not playback duration. Nonces are not stored for one-time consumption and no token table is added. Formal tickets bind `FormalApp`; test tickets bind `TestApp`.

The ZLM `on_play` endpoint must compare actual vhost/app/stream to the validated claims. Missing, malformed, bad-signature, expired, wrong-vhost, wrong-app and wrong-stream tickets all fail closed with safe status/code only. WPF receives a signed media URL or safe playback response, never a ZLM admin URL or bypass credential.

### TDD steps

- [ ] Add `PlaybackTicketIssuerTests.IssueBindsAllClaimsAndUsesSixtySecondWindow`, asserting payload claims without printing the signature or key.
- [ ] Add `PlaybackTicketValidatorTests.RejectsMissingMalformedBadSignatureExpiredAndMismatchedClaims`; assert each result is invalid and contains no secret material.
- [ ] Add `PlaybackAuthorizationTests.OnPlayRequiresExactVhostAppAndStream`, `PlaybackAuthorizationTests.TestAppTicketCannotAuthorizeFormalApp`, and `PlaybackAuthorizationTests.NoAdminBypassIsReturnedToClient`.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~PlaybackTicketIssuerTests|FullyQualifiedName~PlaybackTicketValidatorTests|FullyQualifiedName~PlaybackAuthorizationTests"`; record RED before implementation.
- [ ] Implement independent machine-protected key storage, issuer, validator and minimal endpoints; do not add authentication or user identity.
- [ ] Run the same focused filter and confirm GREEN; run all Server/Core/solution tests, build/rebuild, secret scans and diff checks.
- [ ] Commit on `review/stage-5c-task4-playback-authorization` with `feat: add media playback authorization`, push only that review branch, and stop for Sol review.

### Acceptance

Only a fresh Server-issued ticket for the exact configured vhost, app and stream passes the play boundary. A ticket never exposes credentials, can be used for a connection window without a token table, and does not grant ZLM administration.

## Task 5 — Real Test Stream

**Files:**

- Create: `src/VideoMonitor.Core/Media/TestStreamContracts.cs` — `TestStreamStartRequest`, safe `TestSessionDto`, `TestStreamErrorCode`.
- Create: `src/VideoMonitor.Server/Media/TestStreamService.cs` — draft-aware source selection, session ownership and TTL cleanup.
- Create: `src/VideoMonitor.Server/Media/TestSessionRegistry.cs` — in-memory two-minute sessions and exact cleanup handles.
- Create: `src/VideoMonitor.Server/Media/TestStreamEndpoints.cs` — start/stop endpoints.
- Modify: `src/VideoMonitor.Server/Program.cs` — register test service/session registry and map endpoints.
- Create: `src/VideoMonitor.Wpf/Catalog/TestStreamApiClient.cs` — start/stop client using safe responses.
- Create: `src/VideoMonitor.Wpf/Playback/TestPreviewSource.cs` — non-persistent preview source contract.
- Create: `src/VideoMonitor.Wpf/ViewModels/TestPreviewViewModel.cs` — preview state and cleanup command.
- Modify: `src/VideoMonitor.Wpf/ViewModels/DeviceManagementViewModel.cs` — pass ID-only draft data to test flow; never save the device as a side effect.
- Modify: `src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml` — restore the approved Test Stream action and real preview surface.
- Modify: `src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml.cs` — required preview hookup and cancellation only.
- Test: `tests/VideoMonitor.Server.Tests/Media/TestStreamServiceTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/TestSessionRegistryTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/TestStreamApiTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/Playback/TestStreamApiClientTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/ViewModels/TestPreviewViewModelTests.cs`.

**Interfaces**

- Consumes: Task 2 source resolver, Task 3 `IStreamManager`, Task 4 ticket issuer, existing `DeviceEditDraftViewModel`, `IPlaybackEngine`, and WPF cancellation patterns.
- Produces: `TestStreamStartRequest`, `TestSessionDto`, `ITestStreamService`, `TestStreamApiClient`, `TestPreviewSource`.

Lock these signatures:

```csharp
public sealed record TestStreamStartRequest(
    Guid? ExistingDeviceId,
    Guid? ExistingChannelId,
    CameraDeviceDraftDto Draft,
    DateTimeOffset RequestedAtUtc);

public sealed record CameraDeviceDraftDto(
    Guid? DeviceId,
    string Name,
    string IpAddress,
    int SdkPort,
    int RtspPort,
    string Username,
    string? Password,
    TransportMode TransportMode,
    bool Enabled,
    string Remark,
    IReadOnlyList<CameraChannelInput> Channels);

public enum TestStreamErrorCode
{
    InvalidDraft,
    CatalogUnavailable,
    MediaUnavailable,
    IdentityConflict,
    SessionNotFound
}

public sealed record TestSessionDto(
    Guid SessionId,
    Guid? DeviceId,
    Guid ChannelId,
    string App,
    string StreamId,
    Uri PlaybackUrl,
    DateTimeOffset ExpiresUtc);

public interface ITestStreamService
{
    Task<CatalogOperationResult<TestSessionDto>> StartAsync(
        TestStreamStartRequest request,
        CancellationToken cancellationToken = default);
    Task<CatalogOperationResult<object?>> StopAsync(
        Guid sessionId, CancellationToken cancellationToken = default);
}
```

The service accepts a new unsaved draft, an existing device with unsaved edits, an existing device with blank password (which uses the Server-saved credential), an existing device with non-empty transient password override, and a new device whose draft password may be empty. A new empty password is valid: it is an unprotected empty source credential for the test operation and does not invoke protection for a nonexistent password. No test request calls the Catalog create/update repository.

Each test identity is `configured Vhost + TestApp + test_<valid GUID>` with high-entropy random generation. If a collision is observed, regenerate within a bounded count or fail safely; never reuse, delete or overwrite a collision. The session TTL is two minutes, independent of the 60-second playback ticket. Stop, editor close, device switch, application shutdown and TTL expiry all perform exact session cleanup. Restart orphan recognition requires configured Vhost, TestApp, exact valid `test_<GUID>`, pull/proxy-compatible origin and age greater than two minutes before exact close. Test preview uses a Server-issued ticket and shows a real Camera -> ZLM -> LibVLC image.

### TDD steps

- [ ] Add `TestStreamServiceTests.NewDraftStartsWithoutCatalogWrite`, `TestStreamServiceTests.ExistingEditUsesSavedPasswordWhenDraftIsBlank`, `TestStreamServiceTests.NonEmptyEditPasswordIsTransient`, and `TestStreamServiceTests.EmptyNewPasswordIsAllowed`; assert the repository write service is never called.
- [ ] Add `TestStreamServiceTests.SuccessUsesConfiguredTestAppAndTestGuid`, `TestStreamServiceTests.CollisionRegeneratesWithoutDeletingExistingStream`, and `TestStreamServiceTests.SessionExpiresAfterTwoMinutes`.
- [ ] Add `TestSessionRegistryTests.StopEditorCloseSwitchAndShutdownCleanExactSession`, `TestSessionRegistryTests.RestartOrphanRequiresVhostAppGuidOriginAndAge`, and `TestStreamApiTests.StartReturnsTicketBackedSafePreviewResponse`.
- [ ] Add `TestPreviewViewModelTests.StopAndCloseReleaseSession`, using fakes for API client and playback engine; assert no camera password is copied into the view model.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~TestStreamServiceTests|FullyQualifiedName~TestSessionRegistryTests|FullyQualifiedName~TestStreamApiTests"` and `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~TestStreamApiClientTests|FullyQualifiedName~TestPreviewViewModelTests"`; record RED before implementation.
- [ ] Implement the server session path, then the WPF preview path, keeping all draft data transient and using the Task 4 ticket boundary.
- [ ] Re-run both exact focused filters, then all project/solution tests, build/rebuild, secret scan and diff checks.
- [ ] Commit on `review/stage-5c-task5-real-test-stream` with `feat: add real test stream preview`, push only that review branch, and stop for Sol review.

### Acceptance

The operator can test a newly drafted or edited camera without saving it, sees a real preview, and can stop it deterministically. Formal and test namespaces are isolated, test sessions expire after two minutes, and no collision or orphan cleanup can touch an unproven stream.

## Task 6 — Formal Central Playback

**Files:**

- Create: `src/VideoMonitor.Wpf/Playback/IFormalPlaybackSourceProvider.cs` — ID-only formal source contract.
- Create: `src/VideoMonitor.Wpf/Playback/RemotePlaybackSourceProvider.cs` — Server API client and ticket-backed source preparation; no ZLM calls.
- Create: `src/VideoMonitor.Wpf/Playback/FormalPlaybackSource.cs` — safe channel/stream/url state.
- Create: `src/VideoMonitor.Wpf/Playback/PlaybackRuntimeEvent.cs` — `Playing`, `Stopped`, `Failed` stable events and safe failure category.
- Create: `src/VideoMonitor.Wpf/Playback/IPlaybackRuntimeEventSink.cs` — UI-safe event boundary.
- Create: `src/VideoMonitor.Wpf/Playback/FormalPlaybackCoordinator.cs` — ID-only start/stop and bounded recovery.
- Modify: `src/VideoMonitor.Wpf/Catalog/CatalogApiClient.cs` — formal playback request methods and safe response parsing.
- Modify: `src/VideoMonitor.Wpf/Playback/VlcPlaybackService.cs` — translate LibVLC events to `PlaybackRuntimeEvent`; keep interface ownership clear.
- Modify: `src/VideoMonitor.Wpf/Playback/PlaybackSession.cs` — retain only safe session data needed by the event boundary.
- Modify: `src/VideoMonitor.Wpf/ViewModels/MonitorViewModel.cs` — use safe Catalog DTOs and formal coordinator by stable IDs.
- Modify: `src/VideoMonitor.Wpf/ViewModels/SecondaryMonitorViewModel.cs` — same ID-only formal path.
- Modify: `src/VideoMonitor.Wpf/ViewModels/VideoTileViewModel.cs` — render runtime overlay without `CameraDevice` or password.
- Modify: `src/VideoMonitor.Wpf/Controls/VideoTile.xaml` — bind stable playback state/events only if required by current tile surface.
- Modify: `src/VideoMonitor.Wpf/Configuration/ApplicationCatalogComposition.cs` — compose formal remote provider while retaining the local SingleCameraTest provider.
- Test: `tests/VideoMonitor.Core.Tests/Playback/RemotePlaybackSourceProviderTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/Playback/FormalPlaybackCoordinatorTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/Playback/PlaybackRuntimeEventTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/ViewModels/FormalMonitorPlaybackTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/ViewModels/SecondaryFormalPlaybackTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/Playback/LocalZlmPlaybackSourceProviderTests.cs` — regression only; preserve existing local path.

**Interfaces**

- Consumes: Task 3 `IStreamManager` behavior through Server API, Task 4 ticket-backed playback response, Task 5 preview separation, existing `CatalogSnapshotDto`, `ClientCatalogCache`, `IPlaybackEngine`, `VlcPlaybackService`, `LocalZlmPlaybackSourceProvider`, and `ApplicationCatalogComposition`.
- Produces: `IFormalPlaybackSourceProvider`, `FormalPlaybackSource`, `PlaybackRuntimeEvent`, `IPlaybackRuntimeEventSink`, and `FormalPlaybackCoordinator`.

Lock these signatures:

```csharp
public interface IFormalPlaybackSourceProvider
{
    Task<FormalPlaybackSource> PrepareAsync(
        Guid deviceId,
        Guid channelId,
        CancellationToken cancellationToken = default);
    Task ReleaseAsync(
        FormalPlaybackSource source,
        CancellationToken cancellationToken = default);
}

public sealed record FormalPlaybackSource(
    Guid DeviceId,
    Guid ChannelId,
    string StreamId,
    Uri PlaybackUrl,
    DateTimeOffset TicketExpiresUtc);

public interface IPlaybackRuntimeEventSink
{
    void Publish(PlaybackRuntimeEvent runtimeEvent);
}

public sealed record PlaybackRuntimeEvent(
    Guid ChannelId,
    PlaybackRuntimeEventKind Kind,
    string? SafeFailureCode);

public enum PlaybackRuntimeEventKind { Playing, Stopped, Failed }
```

Formal WPF sends only DeviceId/ChannelId. It does not call ZLM, build RTSP, resolve credentials or construct a `CameraDevice`. `LocalZlmPlaybackSourceProvider` continues to implement the existing `IPlaybackSourceProvider` for SingleCameraTest only. Every reconnect obtains a fresh Server stream result and 60-second ticket. Recovery delays are `1s -> 2s -> 5s -> 10s -> 15s -> 30s -> 30s ...`; transient failures retry, while AuthFailed, invalid configuration and invalid channel stop. User Stop cancels recovery and does not schedule another attempt. Playback engine details are translated before reaching ViewModels.

### TDD steps

- [ ] Add `RemotePlaybackSourceProviderTests.PrepareUsesIdsAndServerTicketWithoutZlmDependency`, asserting the provider calls only the Server client and returns safe source fields.
- [ ] Add `FormalPlaybackCoordinatorTests.TransientFailureUsesBoundedRecoveryDelays`, `FormalPlaybackCoordinatorTests.PermanentFailureStopsWithoutRetry`, `FormalPlaybackCoordinatorTests.UserStopCancelsRecovery`, and `FormalPlaybackCoordinatorTests.RetryRequestsFreshTicket`.
- [ ] Add `PlaybackRuntimeEventTests.LibVlcEventsBecomeStablePlayingStoppedFailedEvents`.
- [ ] Add `FormalMonitorPlaybackTests.ProjectionUsesCatalogDtoIdsOnly` and `SecondaryFormalPlaybackTests.CatalogRefreshPreservesSelectionByGuid`; assert no `CameraDevice` construction in central playback.
- [ ] Run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~RemotePlaybackSourceProviderTests|FullyQualifiedName~FormalPlaybackCoordinatorTests|FullyQualifiedName~PlaybackRuntimeEventTests|FullyQualifiedName~FormalMonitorPlaybackTests|FullyQualifiedName~SecondaryFormalPlaybackTests"`; record RED before implementation.
- [ ] Implement formal provider/coordinator/event translation and composition wiring without changing local SingleCameraTest behavior.
- [ ] Re-run the exact focused filter plus `LocalZlmPlaybackSourceProviderTests`, then all Core/Server/solution tests, build/rebuild, sync-over-async and credential scans, and diff checks.
- [ ] Commit on `review/stage-5c-task6-formal-playback` with `feat: add formal central playback`, push only that review branch, and stop for Sol review.

### Acceptance

Monitor and Secondary Monitor display central catalog channels by stable IDs, obtain playback through Server authorization, and recover transient failures without retry storms. The local compatibility path remains available only for SingleCameraTest. No central WPF path contains camera password, direct ZLM calls or RTSP construction.

## Task 7 — Minimal Media Diagnostics

**Files:**

- Create: `src/VideoMonitor.Core/Media/MediaDiagnosticsDtos.cs` — safe snapshot and per-stream DTOs.
- Create: `src/VideoMonitor.Server/Media/MediaDiagnosticsService.cs` — projection from Task 3 runtime state and safe retry operation.
- Create: `src/VideoMonitor.Server/Media/MediaDiagnosticsEndpoints.cs` — `GET /api/v1/media/diagnostics`, `POST /api/v1/media/diagnostics/refresh`, `POST /api/v1/media/diagnostics/streams/{id}/retry`.
- Modify: `src/VideoMonitor.Server/Program.cs` — register service and map diagnostics endpoints.
- Create: `src/VideoMonitor.Wpf/Catalog/MediaDiagnosticsApiClient.cs` — safe DTO client.
- Create: `src/VideoMonitor.Wpf/ViewModels/MediaDiagnosticsViewModel.cs` — Refresh and Retry faulted commands.
- Modify: `src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml` — diagnostics surface alongside Task 1 settings.
- Modify: `src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml.cs` — only required binding hookup.
- Test: `tests/VideoMonitor.Server.Tests/Media/MediaDiagnosticsServiceTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/MediaDiagnosticsApiTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/Catalog/MediaDiagnosticsApiClientTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/ViewModels/MediaDiagnosticsViewModelTests.cs`.

**Interfaces**

- Consumes: Task 3 `MediaRuntimeSnapshot`, `MediaServerHealth`, `StreamRuntimeState`, `SourceObservation`, `ViewerCount`, ownership and safe failure categories; Task 6 stable playback events where the UI needs runtime display.
- Produces: `MediaDiagnosticsSnapshotDto`, `MediaStreamDiagnosticsDto`, `IMediaDiagnosticsReadModel`.

Lock these DTO fields:

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
    DateTimeOffset? LastSuccessUtc,
    string? SafeLastError);

public interface IMediaDiagnosticsReadModel
{
    Task<MediaDiagnosticsSnapshotDto> GetAsync(CancellationToken cancellationToken = default);
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task RetryFaultedAsync(Guid deviceId, Guid channelId, CancellationToken cancellationToken = default);
}
```

Diagnostics must not expose `originUrl`, camera password, ZLM Secret, signing key, ProxyKey, connection string or an admin URL. `Refresh` reads a safe projection; `RetryFaulted` re-enters the Task 3 formal path. There is no Stop All operation.

### TDD steps

- [ ] Add `MediaDiagnosticsServiceTests.ProjectionContainsHealthCountsAndSafeStreamFields`, `MediaDiagnosticsServiceTests.ProjectionOmitsSecretsAndOriginEvidence`, and `MediaDiagnosticsServiceTests.RetryFaultedUsesStreamManager`.
- [ ] Add `MediaDiagnosticsApiTests.GetDiagnosticsReturnsSafeReadModel`, `MediaDiagnosticsApiTests.RefreshDoesNotExposeInternalEvidence`, and `MediaDiagnosticsApiTests.DoesNotExposeStopAll`.
- [ ] Add `MediaDiagnosticsApiClientTests.ReadsSafeDiagnosticsDto` and `MediaDiagnosticsViewModelTests.RetryFaultedRefreshesStateWithoutShowingCredentialData`.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaDiagnosticsServiceTests|FullyQualifiedName~MediaDiagnosticsApiTests"` and the matching Core filter; record RED before implementation.
- [ ] Implement only safe projection, Refresh, Retry and the Media page bindings.
- [ ] Re-run focused tests, all project/solution tests, build/rebuild, secret scans, `git diff --check` and changed-file scan.
- [ ] Commit on `review/stage-5c-task7-media-diagnostics` with `feat: add minimal media diagnostics`, push only that review branch, and stop for Sol review.

### Acceptance

Operators can see Server health, active streams, viewers, faults and safe per-stream state, and can retry a faulted stream. No diagnostics response or UI contains origin URL, camera password, ZLM Secret, signing key or destructive Stop All.

## Task 8 — Hardware Acceptance

**Files:**

- Create: `docs/superpowers/acceptance/2026-09-01-stage-5c-hardware-acceptance.md` — redacted operator evidence and pass/fail record; never write real secrets or credential-bearing URLs.
- Test: physical acceptance run against the designated lab camera, ZLMediaKit host, Server and WPF machine; no production source file is changed by this Task.

**Interfaces**

- Consumes: the merged Task 1–7 Server, Infrastructure and WPF outputs, existing deployment configuration, operator-provided lab credentials kept outside Git, and the safe diagnostics/readiness surfaces.
- Produces: a redacted acceptance record covering all Stage 5C hardware goals; it produces no new runtime API.

Task 8 is an acceptance gate, not a normal code implementation. It starts from a clean `master` after Task 7 merge and uses `review/stage-5c-task8-hardware-acceptance`. The operator must not paste camera passwords, ZLM secrets, signing keys or full RTSP URLs into the acceptance record.

### Preparation and safe backup

- [ ] Confirm the machine, Server, ZLMediaKit and camera are the designated lab equipment; record only aliases, private-network labels and software versions.
- [ ] Export a safe backup of the Server SQLite Catalog and media settings using the supported backup operation. Store the backup outside the repository and record only its timestamp, hash and restore location class.
- [ ] Confirm WPF `SingleCameraTest.Enabled=false` for formal central verification; reserve the local compatibility configuration for the separate test-stream check.
- [ ] Confirm HTTPS production settings, or explicitly mark a controlled HTTP lab run; never disable certificate validation.

### Server/ZLM/WPF launch and test data

- [ ] Start ZLMediaKit with the lab deployment configuration, then start VideoMonitor Server and wait for `/health/live` 200 and `/health/ready` 200.
- [ ] Confirm the Catalog contains a valid Root -> Child -> Device/Channel path using the existing Device Management UI. Record IDs in redacted form only.
- [ ] Start WPF formal central mode and confirm Catalog data appears without local JSON authority.

### Required hardware actions and expected results

- [ ] Start one formal camera channel and verify a real image in the central WPF Monitor.
- [ ] Inspect WPF process/configuration and logs to prove no camera password and no ZLM Secret enter WPF memory-visible DTOs, UI text, or logs.
- [ ] Open the same formal channel in two consumers and verify one upstream proxy with actual ZLM `ViewerCount` covering both viewers.
- [ ] Stop both consumers and verify the formal no-reader policy removes the stream after the configured 30-second grace, not immediately and not through a WPF heartbeat.
- [ ] Disconnect the camera and verify bounded recovery; restore the camera and verify playback returns through a fresh Server stream/ticket path.
- [ ] Configure a bad camera credential in the controlled lab record and verify AuthFailed stops automatic retry rather than producing an infinite loop.
- [ ] Stop ZLMediaKit and verify Catalog/readiness surfaces remain available with safe unavailable status; restart ZLMediaKit and verify immediate reconciliation.
- [ ] Leave a proven formal stream live, restart Server, and verify adoption occurs only with full vhost/app/deterministic identity/Catalog/origin/source-binding evidence; test an evidence mismatch and verify it is NotOwned and untouched.
- [ ] Use a new and an edited unsaved camera draft to start Test Stream, verify a real preview, then stop it; verify no Catalog device write occurred.
- [ ] Verify Test Stream uses TestApp and a `test_<GUID>` identity, cannot authorize FormalApp, and is cleaned on Stop, editor close, device switch, application end and two-minute TTL.
- [ ] Run a controlled credential-special-character case for `@`, `%`, `#`, `&`, and `:` through the RTSP builder; verify the camera connection works or fails at the camera boundary without credentials in logs.
- [ ] Export redacted Server/WPF/ZLM logs and run a secret scan; verify no camera password, ZLM Secret or credential-bearing RTSP URL is present.

### TDD/verification and recovery

- [ ] Run the automated suites from the merged master before hardware actions and record exact totals.
- [ ] During the manual run, capture timestamps for Server startup, stream creation, reader count, no-reader cleanup, outage, recovery, restart adoption, Test Stream start and cleanup.
- [ ] On any failure, stop the affected test path, preserve redacted logs and do not alter source code as part of acceptance.
- [ ] Restore the safe SQLite/media-settings backup, stop temporary test sessions, confirm no lab test stream remains, and verify the Catalog revision and device/channel membership.
- [ ] Commit only the redacted acceptance record on the review branch with `test: record stage 5c hardware acceptance`, push that review branch, and stop for Sol review. If the record contains no file change, report the manual result without manufacturing a commit.

### Acceptance

All sixteen design goals are evidenced: formal real image; no WPF camera password; no WPF ZLM Secret; two consumers share one upstream; actual viewer count; 30-second no-reader cleanup; camera outage recovery; bad-password retry stop; Catalog alive while ZLM is down; ZLM recovery reconciliation; restart adoption proof; real Test Stream preview; Test cleanup; Test/Formal isolation; special-character credentials; and clean logs.

## Per-Task Review Gate

For every Task 1–8:

- [ ] Verify the branch was created from the latest approved `master` and has no unrelated worktree changes.
- [ ] Verify focused RED/GREEN evidence and exact test command output.
- [ ] Verify project regression, solution tests, Debug build/rebuild, no new warnings, security scans and `git diff --check`.
- [ ] Verify `git diff --name-only <task-base>..HEAD` contains only the Task file map.
- [ ] Commit with the exact Task message, push only `review/stage-5c-taskN-...`, and stop.
- [ ] After Sol approval, check out `master`, update with `git pull --ff-only origin master`, verify the expected base, run `git merge --ff-only origin/review/...`, rerun merged-master verification, then push `master` only when the gate passes.
- [ ] Retain review branches until Stage 5C is accepted; the next Task starts from the newly merged `master` and never from an unmerged sibling branch.

## Final Plan Self-Review

- [ ] Spec coverage: sections 1–9 are covered by Global Constraints and Tasks 1–2; sections 10–14 by Task 3; sections 15–16 by Task 4; sections 17–18 by Task 6; sections 19–22 by Tasks 3–5; section 23 by Task 7; section 24 by Task 8; sections 25–28 by this decomposition, review gate and verification strategy.
- [ ] Placeholder scan: run the seven-token unresolved-plan scan required by the task against `docs/superpowers/plans/2026-09-01-stage-5c-central-media-pipeline.md`; it returns no matches.
- [ ] Type consistency: Task 2 defines `MediaStreamKey`, gateway, resolver and evidence; Task 3 consumes them and produces stream/ownership state; Task 4 consumes `FormalStreamDescriptor` and produces tickets; Task 5 consumes tickets and produces TestSession contracts; Task 6 consumes ticket-backed Server responses and produces formal WPF contracts; Task 7 consumes runtime state and produces diagnostics DTOs.
- [ ] Safety consistency: no task persists runtime status, returns origin evidence, reintroduces JSON authority, bypasses tickets, lowers ownership proof, adds account/RBAC/JWT, or makes formal WPF depend on password-bearing `CameraDevice`.
- [ ] Scope consistency: no design spec modification is included; this plan is the only file created by the documentation task; Task 8 is hardware acceptance and does not invent production implementation.
