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
| Task 1 | `MediaSettingsDto`, `UpdateMediaSettingsRequest`, `TestMediaSettingsRequest`, `IMediaSettingsRepository`, `IMediaSettingsService`, `MediaRuntimeSettings`, `IMediaRuntimeSettingsProvider` | Task 2, Task 3, Task 4, Task 7 |
| Task 2 | `MediaStreamKey`, `MediaStreamNamespace`, `MediaStreamRequest`, `ResolvedCameraSource`, `ZlmMediaEvidence`, `IZlmMediaGateway`, `ICameraMediaCredentialReader`, `CameraMediaCredential`, `ICameraSourceResolver`, `SourceBindingResult` | Task 3, Task 5 |
| Task 3 | `IStreamManager`, `StreamEnsureResult`, `FormalStreamDescriptor`, `StreamOwnership`, `MediaServerHealth`, `StreamRuntimeState`, `SourceObservation`, `MediaRuntimeSnapshot`, complete observation fields, `IMediaObservationRecorder`, `IMediaReconcileContributor`, `IZlmHookTrustPolicy` | Task 4, Task 5, Task 6, Task 7 |
| Task 4 | `PlaybackMediaIdentity`, `EnsurePlaybackStreamRequest`, `EnsurePlaybackStreamResponse`, `IPlaybackStreamService`, `IPlaybackSigningKeyProvider`, `IPlaybackTicketIssuer`, `IPlaybackTicketValidator`, `PlaybackTicket`, `PlaybackTicketValidationResult`, `PlaybackAuthorizationResult` | Task 5, Task 6 |
| Task 5 | `TestStreamStartRequest`, `CameraDeviceDraftDto`, `ResolvedTestCameraSource`, `TestStreamErrorCode`, `TestSessionDto`, `ITestCameraSourceResolver`, `ITestStreamProxyController`, `TestStreamProxyHandle`, `ITestStreamService`, `TestStreamApiClient`, `TestPreviewSource`, `TestStreamOrphanReconcileContributor` | Task 8 and existing WPF test-preview surface |
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
- Create: `src/VideoMonitor.Infrastructure/Persistence/MediaRuntimeSettings.cs` — Server/Infrastructure-only runtime record containing the plaintext ZLM secret only for an active operation.
- Create: `src/VideoMonitor.Infrastructure/Persistence/IMediaRuntimeSettingsProvider.cs` — Server/Infrastructure-only provider contract.
- Create: `src/VideoMonitor.Infrastructure/Persistence/SqliteMediaRuntimeSettingsProvider.cs` — reads the singleton row, decrypts the ciphertext with `ISecretProtector.UnprotectAsync`, and returns the runtime record without logging or retaining it in a public DTO.
- Create: `src/VideoMonitor.Infrastructure/Persistence/SqliteMediaSettingsRepository.cs` — transactional read/update implementation over the new `media_settings` singleton table.
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
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/SqliteMediaRuntimeSettingsProviderTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/Catalog/MediaSettingsApiClientTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/ViewModels/MediaSettingsViewModelTests.cs`.

**Interfaces**

- Consumes: existing `SqliteConnectionFactory`, `IMasterKeyProvider`, `ISecretProtector`, `CatalogOperationResult<T>`, `CatalogApiException`, and `ServerReadinessState`.
- Produces: `MediaSettingsDto`, `UpdateMediaSettingsRequest`, `TestMediaSettingsRequest`, `IMediaSettingsRepository`, `IMediaSettingsService`, `MediaRuntimeSettings`, and `IMediaRuntimeSettingsProvider`.

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

public sealed record MediaRuntimeSettings(
    string ZlmApiBaseUrl,
    string PlaybackBaseUrl,
    string Vhost,
    string FormalApp,
    string TestApp,
    string ZlmSecret,
    int NoReaderGraceSeconds,
    long Revision);

public interface IMediaRuntimeSettingsProvider
{
    Task<MediaRuntimeSettings> GetAsync(
        CancellationToken cancellationToken = default);
}

public interface IMediaSettingsRepository
{
    Task<MediaSettingsDto> GetAsync(CancellationToken cancellationToken = default);
    Task<MediaSettingsStorageRecord> ReadStorageAsync(
        CancellationToken cancellationToken = default);
    Task<CatalogRepositoryResult<MediaSettingsDto>> UpdateAsync(
        UpdateMediaSettingsRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record MediaSettingsStorageRecord(
    string ZlmApiBaseUrl,
    string PlaybackBaseUrl,
    string Vhost,
    string FormalApp,
    string TestApp,
    string ZlmSecretCiphertext,
    int NoReaderGraceSeconds,
    long Revision);

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

Task 1 chooses a new singleton table and does not overload the existing key/value rows:

```sql
CREATE TABLE IF NOT EXISTS media_settings (
    id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
    zlm_api_base_url TEXT NOT NULL,
    playback_base_url TEXT NOT NULL,
    vhost TEXT NOT NULL,
    formal_app TEXT NOT NULL,
    test_app TEXT NOT NULL,
    zlm_secret_ciphertext TEXT NOT NULL,
    no_reader_grace_seconds INTEGER NOT NULL,
    revision INTEGER NOT NULL
);
```

The V3-to-V4 migration creates this table and inserts exactly one row: `id = 1`, `zlm_api_base_url = ''`, `playback_base_url = ''`, `vhost = '__defaultVhost__'`, `formal_app = 'videomonitor'`, `test_app = 'videomonitor-test'`, `zlm_secret_ciphertext = ''`, `no_reader_grace_seconds = 30`, and `revision = 1`. Empty URLs plus empty secret project to `Unconfigured`, not `ConfigurationError`; the runtime provider returns an empty runtime secret for this initial row without attempting to decrypt an empty ciphertext. `SqliteDatabaseInitializer.CurrentSchemaVersion` becomes exactly 4. Updating settings protects a replacement secret before the SQLite transaction, then updates all non-secret fields and increments `revision` only under `WHERE id = 1 AND revision = expectedRevision`; a null or blank edit secret binds the existing ciphertext unchanged. For a configured row, the runtime provider reads the protected value and calls `UnprotectAsync` with a dedicated media-settings purpose. Its plaintext result is consumed only by Server/Infrastructure services and is never injected into WPF.

### TDD steps

- [ ] Add `SqliteDatabaseInitializerTests.V3DatabaseUpgradesToV4MediaSettings`, arrange a V3 database with existing groups/devices/channels, run `InitializeAsync`, and assert `MAX(schema_migrations.version) == 4`, one `media_settings` row with the exact initial values above, and unchanged Catalog counts.
- [ ] Add `SqliteDatabaseInitializerTests.V4InitializationIsIdempotent`, run `InitializeAsync` twice, and assert one singleton row, one version-4 migration row, and unchanged `media_settings.revision`.
- [ ] Run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SqliteDatabaseInitializerTests.V3DatabaseUpgradesToV4MediaSettings"`; expected RED is the absent V4 migration.
- [ ] Add the exact `media_settings` table and V3-to-V4 migration, set `SqliteDatabaseInitializer.CurrentSchemaVersion = 4`, and run the migration test; expected GREEN includes preservation of all existing Catalog rows.
- [ ] Add `SqliteMediaSettingsRepositoryTests.DefaultsAreCreatedWithExpectedNamespaceAndRevision`, arranging a fresh initialized database and asserting the three defaults, `Revision == 1`, and `HasSecret == false`.
- [ ] Run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SqliteMediaSettingsRepositoryTests.DefaultsAreCreatedWithExpectedNamespaceAndRevision"`; expected RED is the missing repository implementation.
- [ ] Implement `SqliteMediaSettingsRepository.GetAsync` to project the row without ciphertext and run the default test; expected GREEN.
- [ ] Add `SqliteMediaSettingsRepositoryTests.UpdateProtectsSecretAndGetNeverReturnsCiphertext`, asserting through a private database query that the stored value is an encrypted envelope while the DTO exposes only `HasSecret == true`.
- [ ] Run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SqliteMediaSettingsRepositoryTests.UpdateProtectsSecretAndGetNeverReturnsCiphertext"`; expected RED is the absent protected update.
- [ ] Implement `UpdateAsync` so a non-empty secret is protected before the transaction and the row is updated only by the expected Revision; run the focused test and expect GREEN.
- [ ] Add `SqliteMediaSettingsRepositoryTests.NullOrBlankSecretPreservesExistingProtectedValue` and `SqliteMediaSettingsRepositoryTests.StaleRevisionDoesNotChangeSettings`; assert the raw ciphertext and every persisted field are unchanged on preservation/conflict.
- [ ] Run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SqliteMediaSettingsRepositoryTests.NullOrBlankSecretPreservesExistingProtectedValue|FullyQualifiedName~SqliteMediaSettingsRepositoryTests.StaleRevisionDoesNotChangeSettings"`; expected RED is missing preserve/conflict behavior.
- [ ] Add `SqliteMediaRuntimeSettingsProviderTests.GetDecryptsSavedCredentialOnlyAtRuntimeBoundary` and `SqliteMediaRuntimeSettingsProviderTests.EmptyInitialRowProjectsUnconfiguredWithoutDecrypt`; run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SqliteMediaRuntimeSettingsProviderTests"`; expected RED identifies the missing provider and empty-initial-row behavior.
- [ ] Implement `MediaRuntimeSettings` and `SqliteMediaRuntimeSettingsProvider.GetAsync` with this shape: `var stored = await repository.ReadStorageAsync(cancellationToken); var secret = await protector.UnprotectAsync(stored.ZlmSecretCiphertext, "media-settings:zlm-secret", cancellationToken); return new MediaRuntimeSettings(stored.ZlmApiBaseUrl, stored.PlaybackBaseUrl, stored.Vhost, stored.FormalApp, stored.TestApp, secret, stored.NoReaderGraceSeconds, stored.Revision);` The provider has no public DTO or retained secret field; for the exact empty initial row it returns the unconfigured runtime state without decrypting an empty ciphertext. Run the same focused provider test command and expect GREEN.
- [ ] Add `MediaSettingsApiTests.GetNeverReturnsSecretOrCiphertext`, `MediaSettingsApiTests.PutUsesExpectedRevisionAndReturns409OnConflict`, `MediaSettingsApiTests.PostTestDoesNotPersistCandidate`, and `MediaSettingsApiTests.BlankEditSecretPreservesExistingSecret`; assert status codes and safe JSON fields, never secret values.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaSettingsApiTests"`; expected RED is the absent endpoint/service behavior.
- [ ] Implement service and endpoint mapping for `GET /api/v1/media/settings`, `PUT /api/v1/media/settings`, and `POST /api/v1/media/settings/test`; run the same command and expect GREEN.
- [ ] Add `MediaSettingsApiClientTests.GetAndPutUseVersionedMediaSettingsPaths` and `MediaSettingsViewModelTests.TestDoesNotSaveOrStartCamera`; assert safe WPF state and no camera API call.
- [ ] Implement the WPF client/view using existing `CatalogApiException` mapping and safe state messages; run both focused client/view-model tests and expect GREEN.
- [ ] Run focused Core and Server media-settings tests, then the full Core, Server and solution suites; build/rebuild and run the shared security/diff scans.
- [ ] Commit on `review/stage-5c-task1-media-settings` with `feat: add media settings and secret storage`, push only that review branch, and stop for Sol review.

关键原子性断言示例：

```csharp
var before = await fixture.ReadCiphertextAsync();
var result = await fixture.Service.UpdateAsync(
    fixture.Request with { ZlmSecret = null, ExpectedRevision = 1 });

Assert.True(result.IsSuccess);
Assert.Equal(before, await fixture.ReadCiphertextAsync());
Assert.DoesNotContain("ZlmSecret", fixture.Serialize(result.Value));
```

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
- Create: `src/VideoMonitor.Infrastructure/Persistence/ICameraMediaCredentialReader.cs` — Server-only reader contract for the protected camera credential boundary.
- Create: `src/VideoMonitor.Infrastructure/Persistence/SqliteCameraMediaCredentialReader.cs` — validates the DeviceId/ChannelId relation, reads the protected ciphertext and decrypts only for the Server media operation.
- Create: `src/VideoMonitor.Infrastructure/Persistence/CameraMediaCredential.cs` — internal source-building record; never a Catalog DTO.
- Create: `src/VideoMonitor.Server/Media/ICameraSourceResolver.cs` — Server-internal credential resolution contract.
- Create: `src/VideoMonitor.Server/Media/CameraSourceResolver.cs` — read authoritative Catalog device/channel and decrypt only inside the Server media boundary.
- Create: `src/VideoMonitor.Server/Media/SourceBindingVerifier.cs` — compare source binding and return only `Matched`, `Mismatch`, or `InsufficientEvidence`.
- Create: `src/VideoMonitor.Server/Media/SourceBindingResult.cs` — safe status types with no URI/password fields.
- Modify: `src/VideoMonitor.Infrastructure/Hikvision/HikvisionRtspUrlBuilder.cs` — reuse the existing builder through the Server resolver and add only required special-character coverage.
- Modify: `src/VideoMonitor.Server/Program.cs` — register gateway, resolver and source-binding services.
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/MediaStreamKeyTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/ZlmClientTests.cs` — extend the existing test file for evidence fields and safe request construction.
- Test: `tests/VideoMonitor.Server.Tests/Media/CameraSourceResolverTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/SqliteCameraMediaCredentialReaderTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/SourceBindingVerifierTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/HikvisionRtspUrlBuilderTests.cs` — extend existing URI tests.

**Interfaces**

- Consumes: Task 1 `IMediaRuntimeSettingsProvider` for current ZLM configuration/secret, existing password-safe `CameraDeviceDto`/`CameraChannelDto` for identity checks, `ICentralCatalogRepository`, `ISecretProtector`, `ZlmClient`, and `HikvisionRtspUrlBuilder`.
- Produces: `MediaStreamKey`, `MediaStreamNamespace`, Server-internal `MediaStreamRequest`, `IZlmMediaGateway`, `ZlmMediaEvidence`, `ICameraMediaCredentialReader`, `CameraMediaCredential`, `ICameraSourceResolver`, and `SourceBindingResult`.

Lock these signatures:

```csharp
public readonly record struct MediaStreamKey(
    Guid DeviceId,
    Guid ChannelId,
    StreamType StreamType)
{
    public string ToFormalStreamId() =>
        $"vm_{DeviceId:N}_{ChannelId:N}_{StreamType.ToString().ToLowerInvariant()}";
}

public static class MediaStreamIdGenerator
{
    public static string GenerateFormal(MediaStreamKey key);
    public static bool TryParseFormal(
        string value, out MediaStreamKey key);
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

public sealed record CameraMediaCredential(
    Guid DeviceId,
    Guid ChannelId,
    string IpAddress,
    int RtspPort,
    string Username,
    string Password,
    int ChannelNo,
    StreamType StreamType,
    TransportMode TransportMode);

public interface ICameraMediaCredentialReader
{
    Task<CameraMediaCredential> ReadAsync(
        Guid deviceId,
        Guid channelId,
        CancellationToken cancellationToken = default);
}

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
        string vhost, string app, string? stream, CancellationToken cancellationToken = default);
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

`ZlmMediaEvidence.OriginUrl` is internal-only and credential-bearing. It may be used by `SourceBindingVerifier` and ownership proof, but it must never be part of a public DTO or diagnostic result. The Server-side `ZlmClient` construction receives `IMediaRuntimeSettingsProvider`, so its request URI uses the current runtime `ZlmApiBaseUrl`, `Vhost`, app and plaintext secret only inside the request scope; the existing `(HttpClient, ZlmOptions)` constructor remains for SingleCameraTest. URI construction must pass tests containing `@`, `%`, `#`, `&`, and `:` in credentials. Request failure messages and logs contain only safe categories and exception type names.

`SqliteCameraMediaCredentialReader` validates the `DeviceId`/`ChannelId` relation before decrypting `password_ciphertext` with the existing purpose `camera-password:{DeviceId:N}`. `CameraMediaCredential.Password` is consumed only by `CameraSourceResolver` to build one transient source URI and is not returned by any catalog, playback or diagnostic contract.

`MediaStreamIdGenerator.GenerateFormal` returns `$"vm_{key.DeviceId:N}_{key.ChannelId:N}_{key.StreamType.ToString().ToLowerInvariant()}"`. `TryParseFormal` accepts only that four-part shape, parses both GUIDs with `Guid.TryParseExact(..., "N", ...)`, accepts only defined `StreamType` values, and returns false for names or arbitrary strings. This preserves strict restart-adoption parsing.

### TDD steps

- [ ] Add `MediaStreamKeyTests.FormalIdIsStableForSameIdentityAndIgnoresNames`, assert the same IDs for the same GUIDs/types and different IDs for different channel/type values.
- [ ] Add `ZlmClientTests.GetMediaListParsesCompleteEvidenceWithoutLoggingOriginUrl` and extend the existing request test to assert encoded query values; use a fake HTTP handler and no real secret.
- [ ] Run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~MediaStreamKeyTests|FullyQualifiedName~ZlmClientTests"`; expected RED is the absent `vm_` generator and complete evidence mapping.
- [ ] Add `MediaStreamKey`, `MediaStreamNamespace`, `MediaStreamRequest`, and `ZlmMediaEvidence` with the exact signatures above; run the same filter and expect the key/evidence compile failures to become GREEN after the minimal implementation.
- [ ] Add `HikvisionRtspUrlBuilderTests.SpecialCharactersRemainInUriComponents` for each of `@`, `%`, `#`, `&`, and `:` and assert only URI component round-trip, not a printed full credential URI.
- [ ] Add `SqliteCameraMediaCredentialReaderTests.ReadDecryptsSavedCredentialInternally`, `SqliteCameraMediaCredentialReaderTests.WrongDeviceChannelRelationFailsSafely`, and `CameraSourceResolverTests.PublicCatalogReadRemainsPasswordSafe`; the first asserts the internal reader calls `UnprotectAsync`, the second asserts a safe failure before decryption, and the third asserts `GetDeviceAsync` has no password/ciphertext field.
- [ ] Run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~HikvisionRtspUrlBuilderTests|FullyQualifiedName~SqliteCameraMediaCredentialReaderTests"`; expected RED is the absent credential reader and special-character coverage.
- [ ] Implement `ICameraMediaCredentialReader.ReadAsync(Guid deviceId, Guid channelId, CancellationToken cancellationToken = default)` against the authoritative SQLite row, validate the channel belongs to the device, decrypt the saved credential only in this boundary, and run the focused filter expecting GREEN.
- [ ] Add `CameraSourceResolverTests.ResolveUsesCredentialReaderAndRuntimeSettings` and `SourceBindingVerifierTests.ReturnsInsufficientEvidenceWhenOriginOrIdentityEvidenceIsMissing`; assert safe status values and verify no source URI is included in a failure.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~CameraSourceResolverTests|FullyQualifiedName~SourceBindingVerifierTests"`; expected RED is the absent resolver/verifier behavior.
- [ ] Implement the smallest gateway extension, evidence mapping, `vm_` deterministic generator, resolver and verifier; make `ZlmClient` use `IMediaRuntimeSettingsProvider` for Server requests while preserving the local constructor.
- [ ] Run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~MediaStreamKeyTests|FullyQualifiedName~ZlmClientTests|FullyQualifiedName~HikvisionRtspUrlBuilderTests|FullyQualifiedName~SqliteCameraMediaCredentialReaderTests"` and the Server media filter; confirm GREEN.
- [ ] Run full Core, Server and solution tests, build/rebuild, the credential/log scan, `git diff --check`, and changed-file scan.
- [ ] Commit on `review/stage-5c-task2-media-foundation` with `feat: add server media foundation`, push only that review branch, and stop for Sol review.

凭据关系与安全失败断言示例：

```csharp
var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
    fixture.Reader.ReadAsync(fixture.DeviceId, fixture.ChannelFromOtherDeviceId));

Assert.DoesNotContain(fixture.PasswordMarker, exception.Message);
Assert.Equal(0, fixture.Protector.UnprotectCalls);
Assert.Equal(SourceBindingResult.InsufficientEvidence,
    fixture.Verifier.Verify(fixture.MissingOriginEvidence));
```

### Acceptance

The Server can derive a stable formal stream identity, query and parse complete ZLM evidence through the existing client, resolve camera source credentials internally, and compare source binding without leaking secrets. No public Catalog DTO or WPF cache gains a password or `originUrl`.

## Task 3 — StreamManager & Runtime Reconciliation

**Files:**

- Create: `src/VideoMonitor.Core/Media/MediaRuntimeContracts.cs` — shared safe enums/value records for runtime state and observation; no source URI or secret fields.
- Create: `src/VideoMonitor.Server/Media/StreamManager.cs` — public orchestration facade only; keep raw evidence, scheduling and hook parsing elsewhere.
- Create: `src/VideoMonitor.Server/Media/IStreamManager.cs` — `EnsureStreamAsync`, exact release operations and safe result contract.
- Create: `src/VideoMonitor.Server/Media/StreamEnsureResult.cs` — success descriptor or safe failure category.
- Create: `src/VideoMonitor.Server/Media/FormalStreamDescriptor.cs` — non-secret vhost/app/stream identity used by Task 4.
- Create: `src/VideoMonitor.Server/Media/MediaRuntimeRegistry.cs` — in-memory state and ownership registry.
- Create: `src/VideoMonitor.Server/Media/IMediaRuntimeStore.cs` — snapshot/read/update contract for runtime state.
- Create: `src/VideoMonitor.Server/Media/MediaStreamGate.cs` — per-key `SemaphoreSlim` gate, not a global lock.
- Create: `src/VideoMonitor.Server/Media/MediaOwnershipClassifier.cs` — `OwnedCurrentProcess`, `OwnedAdopted`, `NotOwned`, `External` classification.
- Create: `src/VideoMonitor.Server/Media/MediaReconcilerHostedService.cs` — startup, recovery and bounded periodic reconcile loop.
- Create: `src/VideoMonitor.Server/Media/MediaHookEndpoints.cs` — fast enqueue endpoints for `POST /api/v1/media/hooks/on-stream-changed` and `POST /api/v1/media/hooks/on-stream-none-reader`.
- Create: `src/VideoMonitor.Server/Media/MediaEventProcessor.cs` — background event processing, no heavy work in hook request.
- Create: `src/VideoMonitor.Server/Media/MediaServerHealthState.cs` — `Unconfigured`, `Healthy`, `Unavailable`, `ConfigurationError`.
- Create: `src/VideoMonitor.Server/Media/MediaStreamRuntimeState.cs` — registry transition logic for `Idle`, `Starting`, `Ready`, `Stopping`, `Faulted`, source observation and viewer count.
- Create: `src/VideoMonitor.Server/Media/IMediaObservationRecorder.cs` — Server-internal recorder for saved-device `MediaStreamKey` observations.
- Create: `src/VideoMonitor.Server/Media/IMediaReconcileContributor.cs` — bounded contributor contract invoked by the shared reconciler.
- Create: `src/VideoMonitor.Server/Media/IZlmHookTrustPolicy.cs` — caller trust boundary for every ZLM hook endpoint.
- Create: `src/VideoMonitor.Server/Media/LoopbackZlmHookTrustPolicy.cs` — default policy accepting only `IPAddress.IsLoopback(remoteAddress)`.
- Modify: `src/VideoMonitor.Server/Program.cs` — register runtime singletons, hosted reconciler and hook endpoints.
- Test: `tests/VideoMonitor.Server.Tests/Media/StreamManagerTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/MediaOwnershipClassifierTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/MediaReconcilerHostedServiceTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/MediaHookTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/MediaRuntimeRegistryTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/MediaObservationRecorderTests.cs`.

**Interfaces**

- Consumes: Task 1 media settings and Task 2 `MediaStreamKey`, `MediaStreamRequest`, `IZlmMediaGateway`, `ICameraSourceResolver`, `ZlmMediaEvidence`, `SourceBindingResult`.
- Produces: `IStreamManager`, `StreamEnsureResult`, `FormalStreamDescriptor`, `StreamOwnership`, `MediaServerHealth`, `StreamRuntimeState`, `SourceObservation`, `ViewerCount`, `MediaRuntimeSnapshot`, `MediaStreamRuntimeInfo` with `ObservedAtUtc`, `LastSuccessUtc`, `SafeLastErrorCode`, and `SafeLastErrorMessage`, `IMediaObservationRecorder`, `IMediaReconcileContributor`, and `IZlmHookTrustPolicy`.

Lock these signatures:

```csharp
public interface IStreamManager
{
    Task<StreamEnsureResult> EnsureStreamAsync(
        MediaStreamRequest request,
        CancellationToken cancellationToken = default);
    Task CleanupOwnedStreamIfEligibleAsync(
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

public sealed record MediaStreamRuntimeInfo(
    MediaStreamKey Key,
    StreamRuntimeState RuntimeState,
    SourceObservation SourceObservation,
    ViewerCount ViewerCount,
    StreamOwnership Ownership,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? ObservedAtUtc,
    DateTimeOffset? LastSuccessUtc,
    string? SafeLastErrorCode,
    string? SafeLastErrorMessage,
    bool IsStale);

public sealed record MediaRuntimeSnapshot(
    MediaServerHealth ServerHealth,
    IReadOnlyList<MediaStreamRuntimeInfo> Streams);

public interface IMediaObservationRecorder
{
    void Record(
        MediaStreamKey key,
        SourceObservation observation,
        DateTimeOffset observedAtUtc,
        string? safeErrorCode,
        string? safeErrorMessage);
}

public interface IMediaReconcileContributor
{
    Task ReconcileAsync(
        CancellationToken cancellationToken = default);
}

public interface IZlmHookTrustPolicy
{
    bool IsTrusted(IPAddress? remoteAddress);
}
```

`EnsureStreamAsync` acquires only the gate for its `MediaStreamKey`, queries ZLM first, and reuses an existing stream only after all proof is present: configured vhost, configured FormalApp, deterministic identity, live Catalog identity, pull/proxy-compatible origin, source binding match, and ownership allowed. A matching schema/vhost/app/stream with failed proof is `MediaStreamIdentityConflict`: no reuse, delete, overwrite, or duplicate `addStreamProxy`. `OwnedCurrentProcess` retains the exact returned ProxyKey. `OwnedAdopted` may be exact-closed after restart only with full proof; `NotOwned` and `External` are never deleted.

Every registry observation writes `ObservedAtUtc`. A successful observation also writes `LastSuccessUtc`; a failed observation writes a safe `SafeLastErrorCode` and `SafeLastErrorMessage` without the source URI or secret. Diagnostics derives stale state from the observation clock and a bounded freshness threshold. `Idle` is a lifecycle state and remains distinct from an unavailable/offline source.

After a successful `addStreamProxy` the manager polls media evidence until registration is real; ZLM code 0 alone is insufficient. Retry is bounded. Reconciliation runs at startup, after media-server recovery and about every 30 seconds without overlap; unavailable ZLM uses `5s -> 10s -> 30s -> 60s` backoff. `MediaReconcilerHostedService` invokes the registered `IMediaReconcileContributor` instances under the same bounded, cancellation-aware cycle; the formal contributor is owned by Task 3 and the test orphan contributor is added by Task 5. Formal no-reader cleanup uses actual ZLM reader count and the Task 1 grace setting, default 30 seconds. Restart-adopted cleanup uses exact `schema + vhost + app + stream` close only. `CleanupOwnedStreamIfEligibleAsync` is a Server-internal lifecycle primitive; it is not exposed as a formal WPF release endpoint. Formal WPF release only disconnects its playback reader, after which ZLM reader count, hook/reconcile and grace-period policy decide cleanup.

Task 3 maps `POST /api/v1/media/hooks/on-stream-changed` and `POST /api/v1/media/hooks/on-stream-none-reader`; Task 4 maps `POST /api/v1/media/hooks/on-play`. All ZLM hook endpoints first call `IZlmHookTrustPolicy`. Stage 5C's default `LoopbackZlmHookTrustPolicy` accepts only `IPAddress.IsLoopback(remoteAddress)` because Server and ZLM are deployed on the same machine. A non-loopback caller receives 403, does not enqueue an event, does not validate a ticket and does not trigger cleanup. Hook request parameters such as ZLM admin fields are not an authentication mechanism. A trusted `on-stream-none-reader` request is only a check signal; cleanup must re-read ReaderCount and ownership before acting. The media settings API can support a future remote ZLM base URL, but cross-machine hook trust is a later deployment-hardening concern.

### TDD steps

- [ ] Add `StreamManagerTests.ConcurrentEnsureForSameKeyUsesOneAddProxy`, `StreamManagerTests.ReadyEvidenceRequiresRegistration`, `StreamManagerTests.SuccessfulEnsureUpdatesObservedAtUtc`, and `StreamManagerTests.AddProxySuccessWithoutMediaRegistrationFailsAndCleansOwnedProxy`; assert per-key serialization, observation timestamp update and bounded cleanup.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~StreamManagerTests.ConcurrentEnsureForSameKeyUsesOneAddProxy|FullyQualifiedName~StreamManagerTests.ReadyEvidenceRequiresRegistration"`; expected RED identifies the missing manager and shared runtime contracts.
- [ ] Add `MediaStreamGate` and `IStreamManager.EnsureStreamAsync(MediaStreamRequest request, CancellationToken cancellationToken = default)` with one gate per key and a query-first body; run the same command and expect GREEN for serialization and registration evidence.
- [ ] Add `StreamManagerTests.NotOwnedIdentityConflictFailsClosedWithoutDeleteOrAdd`, asserting no reuse, delete, overwrite, or duplicate add for an occupied exact identity.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~StreamManagerTests.NotOwnedIdentityConflictFailsClosedWithoutDeleteOrAdd"`; expected RED identifies the missing fail-closed branch.
- [ ] Add the `MediaStreamIdentityConflict` result mapping and complete proof predicate; run the focused test and expect GREEN with zero add/delete calls.
- [ ] Add `MediaOwnershipClassifierTests.RestartAdoptionRequiresAllProof`, `MediaOwnershipClassifierTests.MissingEvidenceIsNotOwned`, and `MediaOwnershipClassifierTests.CurrentProcessRetainsProxyKey`; cover vhost, FormalApp, deterministic key, Catalog identity, origin type, source binding and ownership.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaOwnershipClassifierTests"`; expected RED identifies missing ownership proof.
- [ ] Add `MediaOwnershipClassifier.Classify(ZlmMediaEvidence evidence, MediaStreamKey key, SourceBindingResult binding, bool currentProcessOwnsProxy)` and call the strict `vm_` parser; run the ownership filter and expect GREEN.
- [ ] Add `MediaReconcilerHostedServiceTests.StartupAndRecoveryReconcileDoNotOverlap`, `MediaReconcilerHostedServiceTests.UnavailableServerUsesBoundedBackoff`, and `MediaReconcilerHostedServiceTests.NoReaderUsesConfiguredGracePeriod`; assert cancellation stops the loop.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaReconcilerHostedServiceTests"`; expected RED identifies missing scheduling and cancellation behavior.
- [ ] Add `MediaReconcilerHostedService.RunAsync(CancellationToken cancellationToken)` with one active reconcile gate, startup/recovery triggers and the specified backoff; run the reconciler filter and expect GREEN.
- [ ] Add `MediaHookTests.HookOnlyEnqueuesAndDoesNotRunZlmWorkInline`, `MediaHookTests.LoopbackCallerIsAccepted`, `MediaHookTests.NonLoopbackCallerReturns403WithoutEnqueue`, and `MediaHookTests.NoneReaderTrustedHookStillRechecksZlmBeforeCleanup`; assert trust is checked before enqueue/validation and trusted none-reader events re-check ReaderCount and ownership.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaHookTests"`; expected RED identifies missing hook trust and enqueue behavior.
- [ ] Add `MediaRuntimeRegistryTests.RuntimeSnapshotContainsNoSecretOrOriginUrl` and `MediaObservationRecorderTests.SavedDeviceObservationUpdatesTimestampAndSafeErrorFields`; assert runtime state excludes credential-bearing evidence.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaRuntimeRegistryTests|FullyQualifiedName~MediaObservationRecorderTests"`; expected RED identifies missing safe observation storage.
- [ ] Implement `IMediaObservationRecorder.Record(...)` and the bounded hook-event channel; run the registry/observation filter and expect GREEN.
- [ ] Implement `IZlmHookTrustPolicy` and the loopback-first hook guard in `MediaHookEndpoints`; run the hook filter and expect GREEN with 403/no enqueue for non-loopback callers.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~StreamManagerTests|FullyQualifiedName~MediaOwnershipClassifierTests|FullyQualifiedName~MediaReconcilerHostedServiceTests|FullyQualifiedName~MediaHookTests|FullyQualifiedName~MediaRuntimeRegistryTests|FullyQualifiedName~MediaObservationRecorderTests"`; expected: all Task 3 focused tests PASS.
- [ ] Run all Server tests, all Core tests, solution tests, build/rebuild, secret scan and diff checks.
- [ ] Commit on `review/stage-5c-task3-stream-manager` with `feat: add managed media stream lifecycle`, push only that review branch, and stop for Sol review.

并发与冲突断言示例：

```csharp
var results = await Task.WhenAll(
    fixture.Manager.EnsureStreamAsync(fixture.Request),
    fixture.Manager.EnsureStreamAsync(fixture.Request));

Assert.All(results, result => Assert.True(result.IsSuccess));
Assert.Equal(1, fixture.Gateway.AddStreamProxyCalls);
Assert.Equal("vm_", results[0].Stream!.Stream[..3]);

var conflict = await fixture.Manager.EnsureStreamAsync(fixture.NotOwnedRequest);
Assert.Equal("MediaStreamIdentityConflict", conflict.FailureCode);
Assert.Equal(0, fixture.Gateway.DeleteCalls);
Assert.Equal(0, fixture.Gateway.AddStreamProxyCallsForNotOwnedIdentity);
```

### Acceptance

Concurrent callers for one key cannot create duplicate upstreams; unrelated keys can proceed independently. Identity collisions fail closed. Current-process and proven restart-adopted streams have bounded exact cleanup. Missing or suspicious evidence remains `NotOwned`. Runtime and logs contain no source URL or secret.

## Task 4 — Playback Authorization

**Files:**

- Create: `src/VideoMonitor.Core/Media/PlaybackContracts.cs` — safe request/response contracts for formal and test playback identities plus the formal Ensure Playback endpoint.
- Create: `src/VideoMonitor.Server/Playback/PlaybackTicket.cs` — payload and serialized ticket contract.
- Create: `src/VideoMonitor.Server/Playback/PlaybackTicketIssuer.cs` — stateless HMAC issuer.
- Create: `src/VideoMonitor.Server/Playback/PlaybackTicketValidator.cs` — constant-time signature/claim validation.
- Create: `src/VideoMonitor.Infrastructure/Persistence/IPlaybackSigningKeyProvider.cs` — Server/Infrastructure-only signing-key boundary.
- Create: `src/VideoMonitor.Infrastructure/Persistence/SqlitePlaybackSigningKeyProvider.cs` — protected durable key in the existing `server_settings` table.
- Create: `src/VideoMonitor.Server/Playback/IPlaybackStreamService.cs` — formal stream ensure orchestration contract.
- Create: `src/VideoMonitor.Server/Playback/PlaybackStreamService.cs` — Catalog validation, stream ensure, ticket issue and safe URL assembly.
- Create: `src/VideoMonitor.Server/Playback/PlaybackAuthorizationResult.cs` — safe success/failure categories.
- Create: `src/VideoMonitor.Server/Playback/PlaybackAuthorizationEndpoints.cs` — issue endpoint and ZLM `on-play` validation endpoint.
- Modify: `src/VideoMonitor.Server/Program.cs` — register issuer, validator, key provider and map endpoints.
- Test: `tests/VideoMonitor.Server.Tests/Playback/PlaybackTicketIssuerTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Playback/PlaybackTicketValidatorTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Playback/PlaybackAuthorizationTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/Infrastructure/SqlitePlaybackSigningKeyProviderTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/Playback/PlaybackContractTests.cs`.

**Interfaces**

- Consumes: Task 1 `IMediaRuntimeSettingsProvider`, Task 2 credential/source boundaries, Task 3 `IStreamManager`, `FormalStreamDescriptor`, and `IZlmHookTrustPolicy`, current-process machine protection, actual ZLM `vhost/app/stream` callback fields.
- Produces: `PlaybackMediaIdentity`, `EnsurePlaybackStreamRequest`, `EnsurePlaybackStreamResponse`, `IPlaybackStreamService`, `IPlaybackSigningKeyProvider`, `IPlaybackTicketIssuer`, `IPlaybackTicketValidator`, `PlaybackTicket`, `PlaybackTicketValidationResult`, and `PlaybackAuthorizationResult`.

Lock these signatures:

```csharp
public sealed record EnsurePlaybackStreamRequest(
    Guid DeviceId,
    Guid ChannelId,
    StreamType StreamType);

public sealed record PlaybackMediaIdentity(
    string Vhost,
    string App,
    string Stream);

public sealed record EnsurePlaybackStreamResponse(
    string StreamId,
    Uri PlaybackUrl,
    DateTimeOffset ExpiresAtUtc,
    StreamRuntimeState RuntimeState);

public interface IPlaybackStreamService
{
    Task<CatalogOperationResult<EnsurePlaybackStreamResponse>> EnsureAsync(
        EnsurePlaybackStreamRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPlaybackSigningKeyProvider
{
    Task<byte[]> GetOrCreateAsync(
        CancellationToken cancellationToken = default);
}

public interface IPlaybackTicketIssuer
{
    Task<PlaybackTicket> IssueAsync(
        PlaybackMediaIdentity media,
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

`POST /api/v1/playback/streams/ensure` accepts only `EnsurePlaybackStreamRequest`. `PlaybackStreamService.EnsureAsync` executes this order: validate DeviceId/ChannelId/StreamType against the authoritative Catalog; obtain the Server-only `CameraMediaCredential`; build the `MediaStreamKey`; call Task 3 `IStreamManager.EnsureStreamAsync`; require `StreamRuntimeState.Ready`; convert the returned `FormalStreamDescriptor` to `new PlaybackMediaIdentity(stream.Vhost, stream.App, stream.Stream)`; issue a Task 4 ticket; build the playback URL from `PlaybackBaseUrl`; return `EnsurePlaybackStreamResponse`. WPF never sends camera source URL, camera password or ZLM Secret. Test Stream uses the same issuer with `new PlaybackMediaIdentity(configuredVhost, configuredTestApp, testStreamId)`; the issuer is neutral over Formal and Test namespaces.

The HMAC payload binds `Vhost`, `App`, `StreamId`, `ExpiresUtc`, and a random `Nonce`. The signing key is persisted under the existing `server_settings` key `playback.signing-key.v1`: first use creates 32 random bytes with `RandomNumberGenerator`, protects the Base64 value using `ISecretProtector.ProtectAsync(value, "playback-signing-key:v1", cancellationToken)`, and stores the protected envelope in one SQLite transaction; concurrent callers use a gate and re-read before insert. A restart reads and unprotects the same value. The key is separate from camera password and ZLM Secret, never returned by Media Settings GET, has no WPF UI, and is never logged. Ticket lifetime is a 60-second connection authorization window, not playback duration. Nonces are not stored for one-time consumption and no token table is added. Formal tickets bind `FormalApp`; test tickets bind `TestApp`.

The ZLM `on-play` endpoint is `POST /api/v1/media/hooks/on-play` and first applies the Task 3 `IZlmHookTrustPolicy`; an untrusted caller receives 403 before ticket validation. A trusted request compares actual vhost/app/stream to the validated claims. Missing, malformed, bad-signature, expired, wrong-vhost, wrong-app and wrong-stream tickets all fail closed with safe status/code only. WPF receives a signed media URL or safe playback response, never a ZLM admin URL or bypass credential.

### TDD steps

- [ ] Add `SqlitePlaybackSigningKeyProviderTests.FirstUseCreatesDurableProtectedKey`, `SqlitePlaybackSigningKeyProviderTests.ConcurrentGetOrCreateReturnsOneKey`, and `SqlitePlaybackSigningKeyProviderTests.ReloadReturnsSameKey`; assert no raw key is in `server_settings` and no key is logged.
- [ ] Run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SqlitePlaybackSigningKeyProviderTests"`; expected RED identifies missing durable signing-key storage.
- [ ] Implement `IPlaybackSigningKeyProvider.GetOrCreateAsync(CancellationToken cancellationToken = default)` with the exact `server_settings` key, dedicated purpose and concurrency gate; run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SqlitePlaybackSigningKeyProviderTests"` and expect GREEN.
- [ ] Add `PlaybackTicketIssuerTests.FormalIdentityCanIssue`, `PlaybackTicketIssuerTests.TestIdentityCanIssue`, and `PlaybackTicketIssuerTests.IssueBindsAllClaimsAndUsesSixtySecondWindow`, asserting both namespaces, payload claims and the 60-second window without printing the signature or key.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~PlaybackTicketIssuerTests"`; expected RED identifies missing issuer claims.
- [ ] Implement `PlaybackTicketIssuer.IssueAsync` using the persisted independent key and a 60-second expiry; run the focused test and expect GREEN.
- [ ] Add `PlaybackTicketValidatorTests.RejectsMissingMalformedBadSignatureExpiredAndMismatchedClaims`; assert each result is invalid and contains no secret material.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~PlaybackTicketValidatorTests"`; expected RED identifies missing fail-closed validation.
- [ ] Implement constant-time signature and exact vhost/app/stream claim validation; run the validator filter and expect GREEN.
- [ ] Add `PlaybackAuthorizationTests.EnsureValidIdsReturnsSafePlaybackResponse`, `PlaybackAuthorizationTests.WrongDeviceChannelRelationIsRejected`, `PlaybackAuthorizationTests.WrongStreamTypeIsRejected`, `PlaybackAuthorizationTests.NotOwnedIdentityConflictIsSafe`, `PlaybackAuthorizationTests.ZlmUnavailableIsSafe`, `PlaybackAuthorizationTests.OnPlayRequiresExactVhostAppAndStream`, `PlaybackAuthorizationTests.OnPlayRejectsUntrustedCallerBeforeTicketValidation`, `PlaybackAuthorizationTests.FormalTicketCannotAuthorizeTest`, `PlaybackAuthorizationTests.TestTicketCannotAuthorizeFormal`, and `PlaybackAuthorizationTests.NoAdminBypassIsReturnedToClient`.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~PlaybackAuthorizationTests"`; expected RED identifies missing `POST /api/v1/playback/streams/ensure` and on-play mapping.
- [ ] Implement `PlaybackStreamService.EnsureAsync` and map `CATALOG_VALIDATION_FAILED` to 400, missing identity to 404, `MediaStreamIdentityConflict` to 409, and media unavailability to 503. Add the `on-play` hook guard using `IZlmHookTrustPolicy` before exact ticket validation. Return no source URI, password, ZLM Secret or admin URL; run the authorization filter and expect GREEN.
- [ ] Add `PlaybackContractsTests.EnsureRequestContainsOnlyIdsAndStreamType` and `PlaybackResponseTests.ResponseContainsOnlySafePlaybackFields` in `tests/VideoMonitor.Core.Tests/Playback/PlaybackContractTests.cs`; assert the client contract cannot carry a source URI or password.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~PlaybackTicketIssuerTests|FullyQualifiedName~PlaybackTicketValidatorTests|FullyQualifiedName~PlaybackAuthorizationTests"`; expected: all Task 4 focused Server tests PASS.
- [ ] Run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SqlitePlaybackSigningKeyProviderTests|FullyQualifiedName~PlaybackContractTests"`; expected: all Task 4 focused Core tests PASS.
- [ ] Run all Server/Core/solution tests, build/rebuild, secret scans and diff checks.
- [ ] Commit on `review/stage-5c-task4-playback-authorization` with `feat: add media playback authorization`, push only that review branch, and stop for Sol review.

正式 Ensure API 断言示例：

```csharp
using var response = await fixture.Client.PostAsJsonAsync(
    "/api/v1/playback/streams/ensure",
    new EnsurePlaybackStreamRequest(
        fixture.DeviceId, fixture.ChannelId, fixture.StreamType));
var body = await response.Content.ReadAsStringAsync();

Assert.Equal(HttpStatusCode.OK, response.StatusCode);
var result = JsonSerializer.Deserialize<EnsurePlaybackStreamResponse>(body, fixture.JsonOptions);
Assert.Equal("rtsp", result!.PlaybackUrl.Scheme);
Assert.Equal(fixture.ExpectedPlaybackHost, result.PlaybackUrl.Host);
Assert.Equal(string.Empty, result.PlaybackUrl.UserInfo);
Assert.Contains($"/{fixture.FormalApp}/{fixture.ExpectedFormalStreamId}", result.PlaybackUrl.AbsolutePath, StringComparison.Ordinal);
Assert.DoesNotContain(fixture.CameraHost, result.PlaybackUrl.ToString(), StringComparison.OrdinalIgnoreCase);
Assert.DoesNotContain(fixture.UsernameMarker, result.PlaybackUrl.ToString(), StringComparison.Ordinal);
Assert.DoesNotContain(fixture.PasswordMarker, result.PlaybackUrl.ToString(), StringComparison.Ordinal);
Assert.DoesNotContain(fixture.ZlmSecretMarker, result.PlaybackUrl.ToString(), StringComparison.Ordinal);
Assert.DoesNotContain("/Streaming/Channels/", result.PlaybackUrl.ToString(), StringComparison.OrdinalIgnoreCase);
Assert.DoesNotContain("admin", result.PlaybackUrl.Query, StringComparison.OrdinalIgnoreCase);
```

### Acceptance

Only a fresh Server-issued ticket for the exact configured vhost, app and stream passes the play boundary. A ticket never exposes credentials, can be used for a connection window without a token table, and does not grant ZLM administration.

## Task 5 — Real Test Stream

**Files:**

- Create: `src/VideoMonitor.Core/Media/TestStreamContracts.cs` — `TestStreamStartRequest`, safe `TestSessionDto`, `TestStreamErrorCode`.
- Create: `src/VideoMonitor.Server/Media/TestStreamService.cs` — draft-aware source selection, test-proxy orchestration, session ownership and TTL cleanup without formal `MediaStreamKey`.
- Create: `src/VideoMonitor.Server/Media/ITestCameraSourceResolver.cs` — draft-aware Server-only source contract separate from the saved-Catalog resolver.
- Create: `src/VideoMonitor.Server/Media/TestCameraSourceResolver.cs` — maps only the source fields from the current editor draft, falls back to saved credential only for an existing device with blank password, and builds the transient RTSP source.
- Create: `src/VideoMonitor.Server/Media/ResolvedTestCameraSource.cs` — transient test-source result with nullable existing IDs and no formal `MediaStreamKey`.
- Create: `src/VideoMonitor.Server/Media/ITestStreamProxyController.cs` — test-only proxy lifecycle boundary separate from formal `IStreamManager`.
- Create: `src/VideoMonitor.Server/Media/TestStreamProxyController.cs` — configured `TestApp`, random `test_<GUID>`, collision-safe `addStreamProxy`, registration verification and current-process cleanup.
- Create: `src/VideoMonitor.Server/Media/TestSessionRegistry.cs` — in-memory two-minute sessions and exact cleanup handles.
- Create: `src/VideoMonitor.Server/Media/TestStreamOrphanReconcileContributor.cs` — Vhost/TestApp/GUID/origin/age proof and exact close for unowned test orphans.
- Modify: `src/VideoMonitor.Server/Media/MediaReconcilerHostedService.cs` — invoke the Task 5 orphan contributor through `IMediaReconcileContributor`.
- Create: `src/VideoMonitor.Server/Media/TestStreamEndpoints.cs` — start/stop endpoints.
- Modify: `src/VideoMonitor.Server/Program.cs` — register test service/session registry and map endpoints.
- Create: `src/VideoMonitor.Wpf/Catalog/TestStreamApiClient.cs` — start/stop client using safe responses.
- Create: `src/VideoMonitor.Wpf/Playback/TestPreviewSource.cs` — non-persistent preview source contract.
- Create: `src/VideoMonitor.Wpf/ViewModels/TestPreviewViewModel.cs` — preview state and cleanup command.
- Modify: `src/VideoMonitor.Wpf/ViewModels/DeviceManagementViewModel.cs` — pass ID-only draft data to test flow; never save the device as a side effect.
- Modify: `src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml` — restore the approved Test Stream action and real preview surface.
- Modify: `src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml.cs` — required preview hookup and cancellation only.
- Test: `tests/VideoMonitor.Server.Tests/Media/TestStreamServiceTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/TestCameraSourceResolverTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/TestStreamProxyControllerTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/TestSessionRegistryTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/TestStreamOrphanReconcileContributorTests.cs`.
- Test: `tests/VideoMonitor.Server.Tests/Media/TestStreamApiTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/Playback/TestStreamApiClientTests.cs`.
- Test: `tests/VideoMonitor.Core.Tests/ViewModels/TestPreviewViewModelTests.cs`.

**Interfaces**

- Consumes: Task 2 `ICameraMediaCredentialReader`, `IZlmMediaGateway` and RTSP builder, Task 3 `IMediaObservationRecorder`, `IMediaReconcileContributor`, and `IZlmHookTrustPolicy`, Task 4 `IPlaybackTicketIssuer`/`PlaybackMediaIdentity` safety rules, existing `DeviceEditDraftViewModel`, `IPlaybackEngine`, and WPF cancellation patterns. It does not consume or fabricate a formal `MediaStreamKey`, and it does not call formal `IStreamManager`.
- Produces: `TestStreamStartRequest`, source-only `CameraDeviceDraftDto`, `ResolvedTestCameraSource`, `ITestCameraSourceResolver`, `ITestStreamProxyController`, `TestStreamProxyHandle`, `TestStreamErrorCode`, `TestSessionDto`, `ITestStreamService`, `TestStreamApiClient`, `TestPreviewSource`, and `TestStreamOrphanReconcileContributor`.

Lock these signatures:

```csharp
public sealed record TestStreamStartRequest(
    Guid? ExistingDeviceId,
    Guid? ExistingChannelId,
    CameraDeviceDraftDto Draft,
    DateTimeOffset RequestedAtUtc);

public sealed record CameraDeviceDraftDto(
    string IpAddress,
    int RtspPort,
    string Username,
    string? Password,
    int ChannelNo,
    StreamType StreamType,
    TransportMode TransportMode);

public sealed record ResolvedTestCameraSource(
    Uri SourceUri,
    Guid? ExistingDeviceId,
    Guid? ExistingChannelId,
    int ChannelNo,
    StreamType StreamType);

public interface ITestCameraSourceResolver
{
    Task<ResolvedTestCameraSource> ResolveAsync(
        TestStreamStartRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record TestStreamProxyHandle(
    string Vhost,
    string App,
    string StreamId,
    string ProxyKey,
    DateTimeOffset CreatedAtUtc);

public interface ITestStreamProxyController
{
    Task<TestStreamProxyHandle> StartAsync(
        ResolvedTestCameraSource source,
        CancellationToken cancellationToken = default);
    Task StopAsync(
        TestStreamProxyHandle handle,
        CancellationToken cancellationToken = default);
}

public enum TestStreamErrorCode
{
    InvalidDraft,
    MediaServerUnavailable,
    AuthFailed,
    ConnectFailed,
    MediaRegistrationTimeout,
    PlaybackPreparationFailed,
    CatalogUnavailable,
    IdentityConflict,
    SessionNotFound
}

public sealed record TestSessionDto(
    Guid SessionId,
    Guid? DeviceId,
    Guid? ChannelId,
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

The service accepts a new unsaved draft, an existing device with unsaved edits, an existing device with blank password (which uses the Server-saved credential), an existing device with non-empty transient password override, and a new device whose draft password may be empty. A new empty password is valid: it is an unprotected empty source credential for the test operation and does not invoke protection for a nonexistent password. No test request calls the Catalog create/update repository or fabricates a DeviceId/ChannelId for a new draft.

`TestStreamProxyController` is the test-only proxy authority. It reads the configured Vhost/TestApp, generates a high-entropy random `test_<valid GUID>`, checks the exact identity before calling `addStreamProxy`, polls for registration, and retains the current-process ProxyKey in the `TestStreamProxyHandle`. It never calls formal `IStreamManager`, never creates a formal `MediaStreamKey`, and never applies formal restart-adoption reuse rules. If an exact identity collision is observed, it regenerates within a bounded count or fails safely; it never reuses, deletes or overwrites the collision. Stop and TTL cleanup use only the current-process handle.

The session TTL is two minutes, independent of the 60-second playback ticket. Stop, editor close, device switch, application shutdown and TTL expiry all perform exact session cleanup. `TestStreamOrphanReconcileContributor` is registered in the shared `MediaReconcilerHostedService` through `IMediaReconcileContributor`; it lists the configured TestApp with `GetMediaListAsync(configuredVhost, configuredTestApp, null, cancellationToken)`, skips active session/current-process handles, and exact-closes only evidence proving configured Vhost, configured TestApp, exact valid `test_<GUID>`, pull/proxy-compatible origin and age greater than two minutes. It never closes a collision or evidence with a different Vhost/App/identity. Test preview uses a Server-issued ticket and shows a real Camera -> ZLM -> LibVLC image.

`ITestCameraSourceResolver` is separate from Task 2 `ICameraSourceResolver`: it receives the source-only `CameraDeviceDraftDto` assembled from the current `DeviceEditDraftViewModel` fields `IpAddress`, `RtspPort`, `Username`, `Password`, `ChannelNo`, `StreamType` and `TransportMode`. For an existing device, blank `Password` selects the protected saved credential through `ICameraMediaCredentialReader`; a non-empty `Password` is a transient override. For a new device, the draft value, including empty password, is used directly. It returns `ResolvedTestCameraSource` with nullable existing IDs and no required formal key. The resulting source URI exists only during the test operation.

For an existing saved device/channel, `TestStreamService` records successful or failed source observations through `IMediaObservationRecorder` using the corresponding `MediaStreamKey`. For a new unsaved draft, it records no formal runtime observation; only the `TestSessionDto` and temporary UI result carry the outcome.

### TDD steps

- [ ] Add `TestCameraSourceResolverTests.NewDraftReturnsSourceWithoutFormalIdentity`, `TestCameraSourceResolverTests.ExistingBlankPasswordUsesSavedCredential`, `TestCameraSourceResolverTests.ExistingNonEmptyPasswordIsTransient`, and `TestCameraSourceResolverTests.NewEmptyPasswordIsAllowed`; assert the returned type is `ResolvedTestCameraSource`, new drafts keep both existing IDs null, and the Catalog write service is never called.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~TestCameraSourceResolverTests"`; expected RED identifies the absent draft-aware result type and source branches.
- [ ] Add `ResolvedTestCameraSource`, change `ITestCameraSourceResolver.ResolveAsync(TestStreamStartRequest request, CancellationToken cancellationToken = default)` to return it, and define the source-only `CameraDeviceDraftDto`; run the resolver filter and expect GREEN for the contract shape.
- [ ] Implement the three resolver branches: existing plus blank password calls `ICameraMediaCredentialReader`, existing plus non-empty password builds a transient credential, and new uses the exact draft including empty password; run the resolver filter and expect GREEN.
- [ ] Add `TestStreamProxyControllerTests.StartUsesConfiguredTestAppAndRandomTestGuid`, `TestStreamProxyControllerTests.CollisionRegeneratesWithoutDeletingExistingStream`, and `TestStreamProxyControllerTests.RegistrationMustBeObservedBeforeSuccess`; assert no formal `MediaStreamKey` or `IStreamManager` call is possible and an occupied identity never receives delete/overwrite.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~TestStreamProxyControllerTests"`; expected RED identifies the absent test-only proxy boundary.
- [ ] Add `ITestStreamProxyController.StartAsync(ResolvedTestCameraSource source, CancellationToken cancellationToken = default)` and `StopAsync(TestStreamProxyHandle handle, CancellationToken cancellationToken = default)` with the exact handle fields above; run the proxy filter and expect GREEN for the contract compile path.
- [ ] Implement `TestStreamProxyController.StartAsync` as a bounded loop over a random `test_<GUID>`: read Vhost/TestApp, call `GetMediaListAsync(vhost, app, candidate)`, regenerate on any exact collision, call `AddStreamProxyAsync` only for an empty identity, poll evidence until registration, and retain only the current-process `ProxyKey` in the handle; run the proxy filter and expect GREEN.
- [ ] Add `TestStreamServiceTests.NewDraftStartsWithoutCatalogWrite`, `TestStreamServiceTests.ExistingEditUsesSavedPasswordWhenDraftIsBlank`, `TestStreamServiceTests.SuccessfulExistingTestUpdatesObservedAtUtc`, `TestStreamServiceTests.FailedExistingTestUpdatesObservation`, `TestStreamServiceTests.NewDraftDoesNotCreateFormalObservation`, and `TestStreamServiceTests.EmptyNewPasswordIsAllowed`; assert existing saved identities use `IMediaObservationRecorder`, new drafts do not, and no Catalog repository write occurs.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~TestStreamServiceTests.NewDraftStartsWithoutCatalogWrite|FullyQualifiedName~TestStreamServiceTests.ExistingEditUsesSavedPasswordWhenDraftIsBlank|FullyQualifiedName~TestStreamServiceTests.SuccessfulExistingTestUpdatesObservedAtUtc|FullyQualifiedName~TestStreamServiceTests.FailedExistingTestUpdatesObservation|FullyQualifiedName~TestStreamServiceTests.NewDraftDoesNotCreateFormalObservation|FullyQualifiedName~TestStreamServiceTests.EmptyNewPasswordIsAllowed"`; expected RED identifies the service's missing resolver/proxy/observation orchestration.
- [ ] Implement `TestStreamService.StartAsync` with `ResolvedTestCameraSource`, `ITestStreamProxyController`, `new PlaybackMediaIdentity(proxy.Vhost, proxy.App, proxy.StreamId)`, the Task 4 ticket issuer, nullable session IDs and a two-minute registry entry; call `IMediaObservationRecorder` only when both existing IDs are present and never call `IStreamManager`; run the service filter and expect GREEN.
- [ ] Add `TestStreamServiceTests.MediaServerUnavailableIsMapped`, `TestStreamServiceTests.AuthFailedRequiresEvidence`, `TestStreamServiceTests.ConnectFailedIsSafeWithoutAuthEvidence`, `TestStreamServiceTests.MediaRegistrationTimeoutIsMapped`, and `TestStreamServiceTests.PlaybackPreparationFailedIsMapped`; assert each safe `TestStreamErrorCode` and absence of source URI/password in the result.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~TestStreamServiceTests.MediaServerUnavailableIsMapped|FullyQualifiedName~TestStreamServiceTests.AuthFailedRequiresEvidence|FullyQualifiedName~TestStreamServiceTests.ConnectFailedIsSafeWithoutAuthEvidence|FullyQualifiedName~TestStreamServiceTests.MediaRegistrationTimeoutIsMapped|FullyQualifiedName~TestStreamServiceTests.PlaybackPreparationFailedIsMapped"`; expected RED identifies missing evidence-backed error mapping.
- [ ] Implement the five required mappings, returning `AuthFailed` only when the source evidence proves authentication failure and otherwise `ConnectFailed`; run the error filter and expect GREEN.
- [ ] Add `TestSessionRegistryTests.StopEditorCloseSwitchAndShutdownCleanExactSession`, `TestSessionRegistryTests.TtlExpiryStopsCurrentProcessProxy`, `TestStreamOrphanReconcileContributorTests.RestartOrphanRequiresConfiguredVhostTestAppGuidOriginAndAge`, and `TestStreamOrphanReconcileContributorTests.NonMatchingEvidenceIsUntouched`.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~TestSessionRegistryTests|FullyQualifiedName~TestStreamOrphanReconcileContributorTests"`; expected RED identifies missing exact cleanup and reconciler integration.
- [ ] Implement `TestStreamOrphanReconcileContributor : IMediaReconcileContributor` using `GetMediaListAsync(configuredVhost, configuredTestApp, null, cancellationToken)`, skipping active session/current-process handles and exact-closing only Vhost/TestApp/valid `test_<GUID>`/pull-or-proxy/age-greater-than-two-minutes evidence; modify `MediaReconcilerHostedService` to invoke the contributor and run the cleanup filter expecting GREEN.
- [ ] Add `TestStreamApiTests.StartReturnsTicketBackedSafePreviewResponse`, `TestStreamApiTests.StopDoesNotWriteCatalog`, and `TestPreviewViewModelTests.StopAndCloseReleaseSession`; assert nullable IDs, Server-issued ticket usage, no Catalog write and no camera password in WPF state.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~TestStreamApiTests"` and `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~TestStreamApiClientTests|TestPreviewViewModelTests"`; expected RED identifies missing transport/preview behavior.
- [ ] Implement `TestStreamApiClient` for `POST /api/v1/test-streams` and `DELETE /api/v1/test-streams/{sessionId}`, then connect the WPF preview to the safe session response, Server ticket and cancellation cleanup; run both client/view-model filters and expect GREEN.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~TestStreamServiceTests|TestStreamProxyControllerTests|TestSessionRegistryTests|TestStreamOrphanReconcileContributorTests|TestStreamApiTests"` and `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~TestStreamApiClientTests|TestPreviewViewModelTests"`; Expected: all Task 5 focused tests PASS.
- [ ] Run all project/solution tests, build/rebuild, secret scans and diff checks.
- [ ] Commit on `review/stage-5c-task5-real-test-stream` with `feat: add real test stream preview`, push only that review branch, and stop for Sol review.

未保存 Draft 零写入断言示例：

```csharp
var result = await fixture.Service.StartAsync(
    new TestStreamStartRequest(null, null, fixture.NewDraft, fixture.Clock.UtcNow));

Assert.True(result.IsSuccess);
Assert.Equal(0, fixture.CatalogCommandService.WriteCalls);
Assert.Equal("videomonitor-test", result.Value!.App);
Assert.StartsWith("test_", result.Value.StreamId, StringComparison.Ordinal);
```

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

- Consumes: Task 3 runtime behavior through Server API, Task 4 `EnsurePlaybackStreamRequest`/`EnsurePlaybackStreamResponse`, Task 5 preview separation, existing `CatalogSnapshotDto`, `ClientCatalogCache`, `IPlaybackEngine`, `VlcPlaybackService`, `LocalZlmPlaybackSourceProvider`, and `ApplicationCatalogComposition`.
- Produces: `IFormalPlaybackSourceProvider`, `FormalPlaybackSource`, `PlaybackRuntimeEvent`, `IPlaybackRuntimeEventSink`, and `FormalPlaybackCoordinator`.

Lock these signatures:

```csharp
public interface IFormalPlaybackSourceProvider
{
    Task<FormalPlaybackSource> PrepareAsync(
        Guid deviceId,
        Guid channelId,
        StreamType streamType,
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

Formal WPF sends `DeviceId`, `ChannelId` and `StreamType` to `POST /api/v1/playback/streams/ensure`; it does not send a source URL, camera password or ZLM Secret. It does not call ZLM, build RTSP, resolve credentials or construct a `CameraDevice`. `LocalZlmPlaybackSourceProvider` continues to implement the existing `IPlaybackSourceProvider` for SingleCameraTest only. Every reconnect obtains a fresh Server `EnsurePlaybackStreamResponse` and 60-second ticket. Recovery delays are `1s -> 2s -> 5s -> 10s -> 15s -> 30s -> 30s ...`; transient failures retry, while AuthFailed, invalid configuration and invalid channel stop. User Stop cancels recovery and does not schedule another attempt. `ReleaseAsync` only disconnects the WPF reader; it must not call an immediate formal-proxy-delete endpoint. Playback engine details are translated before reaching ViewModels.

### TDD steps

- [ ] Add `RemotePlaybackSourceProviderTests.PrepareUsesIdsAndServerTicketWithoutZlmDependency`, asserting the provider calls only the Server client and returns safe source fields.
- [ ] Run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~RemotePlaybackSourceProviderTests.PrepareUsesIdsAndServerTicketWithoutZlmDependency"`; expected RED identifies the missing exact Ensure Playback contract.
- [ ] Add `CatalogApiClient.EnsurePlaybackStreamAsync(Uri baseUri, EnsurePlaybackStreamRequest request, CancellationToken cancellationToken = default)` and implement `RemotePlaybackSourceProvider.PrepareAsync(Guid deviceId, Guid channelId, StreamType streamType, CancellationToken cancellationToken = default)` as an ID-only call; run the focused test and expect GREEN.
- [ ] Add `FormalPlaybackCoordinatorTests.TransientFailureUsesBoundedRecoveryDelays`, `FormalPlaybackCoordinatorTests.PermanentFailureStopsWithoutRetry`, `FormalPlaybackCoordinatorTests.UserStopCancelsRecovery`, and `FormalPlaybackCoordinatorTests.RetryRequestsFreshTicket`.
- [ ] Run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~FormalPlaybackCoordinatorTests"`; expected RED identifies missing recovery and cancellation transitions.
- [ ] Implement the coordinator loop so each retry calls `PrepareAsync` with the same full key and receives a new `ExpiresAtUtc`; run the focused tests and expect GREEN.
- [ ] Add `PlaybackRuntimeEventTests.LibVlcEventsBecomeStablePlayingStoppedFailedEvents`.
- [ ] Run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~PlaybackRuntimeEventTests"`; expected RED identifies the missing event translation boundary.
- [ ] Implement the LibVLC event adapter in `VlcPlaybackService.cs` and publish only `PlaybackRuntimeEventKind` plus safe failure code; run the event test and expect GREEN.
- [ ] Add `FormalMonitorPlaybackTests.ProjectionUsesCatalogDtoIdsOnly` and `SecondaryFormalPlaybackTests.CatalogRefreshPreservesSelectionByGuid`; assert no `CameraDevice` construction in central playback.
- [ ] Run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~FormalMonitorPlaybackTests|FullyQualifiedName~SecondaryFormalPlaybackTests"`; expected RED identifies missing ID-only monitor wiring.
- [ ] Implement Monitor/Secondary composition wiring and stable-ID projection without changing local SingleCameraTest behavior; run the two view-model filters and expect GREEN.
- [ ] Run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~RemotePlaybackSourceProviderTests|FullyQualifiedName~FormalPlaybackCoordinatorTests|FullyQualifiedName~PlaybackRuntimeEventTests|FullyQualifiedName~FormalMonitorPlaybackTests|FullyQualifiedName~SecondaryFormalPlaybackTests"`; expected: all Task 6 focused tests PASS.
- [ ] Run all Task 6 focused tests plus `LocalZlmPlaybackSourceProviderTests`; expected: all formal playback and local-compatibility regression tests PASS.
- [ ] Run all Core/Server/solution tests, build/rebuild, sync-over-async and credential scans, and diff checks.
- [ ] Commit on `review/stage-5c-task6-formal-playback` with `feat: add formal central playback`, push only that review branch, and stop for Sol review.

正式播放调用边界断言示例：

```csharp
var source = await fixture.Provider.PrepareAsync(
    fixture.DeviceId, fixture.ChannelId, StreamType.Sub, fixture.CancellationToken);

Assert.Equal(StreamType.Sub, fixture.Api.LastEnsureRequest!.StreamType);
Assert.Equal(fixture.ChannelId, source.ChannelId);
Assert.Equal(0, fixture.ZlmDirectClient.CallCount);
```

### Acceptance

Monitor and Secondary Monitor display central catalog channels by stable IDs, obtain playback through Server authorization, and recover transient failures without retry storms. The local compatibility path remains available only for SingleCameraTest. No central WPF path contains camera password, direct ZLM calls or RTSP construction.

## Task 7 — Minimal Media Diagnostics

**Files:**

- Create: `src/VideoMonitor.Core/Media/MediaDiagnosticsDtos.cs` — safe snapshot and per-stream DTOs.
- Create: `src/VideoMonitor.Server/Media/MediaDiagnosticsService.cs` — projection from Task 3 runtime state and safe retry operation.
- Create: `src/VideoMonitor.Server/Media/MediaDiagnosticsEndpoints.cs` — `GET /api/v1/media/diagnostics`, `POST /api/v1/media/diagnostics/refresh`, `POST /api/v1/media/diagnostics/streams/{deviceId}/{channelId}/{streamType}/retry`.
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
    DateTimeOffset? ObservedAtUtc,
    DateTimeOffset? LastSuccessUtc,
    string? SafeLastErrorCode,
    string? SafeLastErrorMessage,
    bool IsStale);

public interface IMediaDiagnosticsReadModel
{
    Task<MediaDiagnosticsSnapshotDto> GetAsync(CancellationToken cancellationToken = default);
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task RetryFaultedAsync(
        MediaStreamKey key,
        CancellationToken cancellationToken = default);
}
```

Diagnostics must not expose `originUrl`, camera password, ZLM Secret, signing key, ProxyKey, connection string or an admin URL. `Refresh` reads a safe projection; `RetryFaulted` accepts the complete `MediaStreamKey` and re-enters the Task 3 formal path without guessing `StreamType`. The projection marks an observation stale when `ObservedAtUtc` exceeds the configured freshness interval. There is no Stop All operation.

### TDD steps

- [ ] Add `MediaDiagnosticsServiceTests.ProjectionContainsHealthCountsAndSafeStreamFields`, `MediaDiagnosticsServiceTests.ProjectionOmitsSecretsAndOriginEvidence`, `MediaDiagnosticsServiceTests.OldObservationBecomesStaleProjection`, `MediaDiagnosticsServiceTests.IdleRemainsDistinctFromOffline`, and `MediaDiagnosticsServiceTests.RetryFaultedUsesStreamManager`.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaDiagnosticsServiceTests"`; expected RED identifies missing safe projection, timestamps and full-key retry.
- [ ] Implement `MediaDiagnosticsService` projection with `ObservedAtUtc`, `LastSuccessUtc`, `SafeLastErrorCode`, `SafeLastErrorMessage`, stale derivation and `RetryFaultedAsync(MediaStreamKey key, CancellationToken cancellationToken = default)`; run the service filter and expect GREEN.
- [ ] Add `MediaDiagnosticsApiTests.GetDiagnosticsReturnsSafeReadModel`, `MediaDiagnosticsApiTests.RefreshDoesNotExposeInternalEvidence`, and `MediaDiagnosticsApiTests.DoesNotExposeStopAll`.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaDiagnosticsApiTests"`; expected RED identifies missing transport mapping and unsafe-field guard.
- [ ] Implement the three fixed endpoint paths and map the complete device/channel/type route to `MediaStreamKey`; run the API filter and expect GREEN.
- [ ] Add `MediaDiagnosticsApiClientTests.ReadsSafeDiagnosticsDto` and `MediaDiagnosticsViewModelTests.RetryFaultedRefreshesStateWithoutShowingCredentialData`.
- [ ] Run `dotnet test .\tests\VideoMonitor.Core.Tests\VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~MediaDiagnosticsApiClientTests|FullyQualifiedName~MediaDiagnosticsViewModelTests"`; expected RED identifies missing WPF diagnostics client/view-model behavior.
- [ ] Implement the WPF safe DTO client and full-key Retry command; run the Core filter and expect GREEN.
- [ ] Run `dotnet test .\tests\VideoMonitor.Server.Tests\VideoMonitor.Server.Tests.csproj --filter "FullyQualifiedName~MediaDiagnosticsServiceTests|FullyQualifiedName~MediaDiagnosticsApiTests"` and the matching Core filter; expected: all Task 7 focused Server tests PASS.
- [ ] Run all project/solution tests, build/rebuild, secret scans, `git diff --check` and changed-file scan.
- [ ] Commit on `review/stage-5c-task7-media-diagnostics` with `feat: add minimal media diagnostics`, push only that review branch, and stop for Sol review.

观测与 stale 投影断言示例：

```csharp
fixture.Runtime.RecordObservation(
    fixture.Key,
    SourceObservation.ConnectFailed,
    fixture.Clock.UtcNow,
    "CAMERA_CONNECT_FAILED",
    "camera connection failed");

var diagnostics = await fixture.Diagnostics.GetAsync();
var stream = Assert.Single(diagnostics.Streams);
Assert.Equal("CAMERA_CONNECT_FAILED", stream.SafeLastErrorCode);
Assert.Equal(fixture.Clock.UtcNow, stream.ObservedAtUtc);
Assert.DoesNotContain("originUrl", fixture.Serialize(diagnostics));
```

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
- [ ] Type consistency: Task 1 owns the runtime media-settings provider; Task 2 owns `MediaStreamKey`, `MediaStreamRequest`, `ResolvedCameraSource`, the gateway, camera credential reader, resolver and evidence; Task 3 consumes those boundaries and produces stream/ownership state; Task 4 consumes `FormalStreamDescriptor`, owns durable signing-key storage and produces the exact Ensure Playback contracts/tickets; Task 5 consumes the credential reader and ticket boundary, owns the draft-aware `ITestCameraSourceResolver` and `TestStreamErrorCode`, and produces TestSession contracts; Task 6 consumes the same `EnsurePlaybackStreamRequest`/`EnsurePlaybackStreamResponse` through its `PrepareAsync(Guid deviceId, Guid channelId, StreamType streamType, ...)` signature and produces formal WPF contracts; Task 7 consumes runtime state and produces diagnostics DTOs with complete observation fields.
- [ ] Review-fix boundary checks: Task 1 keeps the ZLM Secret inside `IMediaRuntimeSettingsProvider`; Task 2 keeps camera credential reads Server/Infrastructure-only; Task 4 persists the protected playback signing key and exposes `POST /api/v1/playback/streams/ensure`; Task 5 supports unsaved drafts without Catalog writes and maps the five required safe error categories; Task 3 uses the strict `vm_` namespace and observation timestamps; Task 6 release only disconnects the reader and never directly deletes a shared formal proxy.
- [ ] Second-review checklist: new unsaved Test Stream uses `ResolvedTestCameraSource` without a fake formal key; `TestSessionDto` has nullable `DeviceId`/`ChannelId`; the draft DTO contains only real source fields; test proxy lifecycle is separate from formal `IStreamManager`; orphan cleanup is registered through `IMediaReconcileContributor`; saved test identities update observations while new drafts do not; `PlaybackMediaIdentity` supports Formal and Test; Ensure response permits safe RTSP; all hooks use loopback trust and the three exact paths; no stale bulk RED/implementation steps remain.
- [ ] Safety consistency: no task persists runtime status, returns origin evidence, reintroduces JSON authority, bypasses tickets, lowers ownership proof, adds account/RBAC/JWT, or makes formal WPF depend on password-bearing `CameraDevice`.
- [ ] Scope consistency: no design spec modification is included; this plan is the only file created by the documentation task; Task 8 is hardware acceptance and does not invent production implementation.
