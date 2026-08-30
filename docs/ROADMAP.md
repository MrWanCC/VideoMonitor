# VideoMonitor Roadmap

**Updated:** 2026-08-29

## Current state

已完成：

- WPF 基础监控 UI；
- Device Catalog 与设备管理基础能力；
- 本机 JSON 持久化、SchemaVersion、原子替换、`.bak` 恢复、DPAPI 等开发期验证；
- 单摄像机 Hikvision RTSP -> ZLMediaKit -> LibVLCSharp -> WPF Tile 播放验证；
- 中心化 V2 架构设计；
- Stage 5A Server foundation and central data。

Stage 5A 已完成并进入 `master`，基线：

```text
1eae3ff75c68b9d0fde4c6b2097ebe02505550f0
```

已确认新的 Stage 5B 方向：项目尚未现场投产，不实现旧 JSON 正式迁移系统；直接把 Server SQLite 切为正式 Catalog 唯一权威数据源。

正式设计：

```text
docs/superpowers/specs/2026-08-29-centralized-video-monitoring-architecture-design.md
docs/superpowers/specs/2026-08-29-stage-5b-central-catalog-api-wpf-data-source-design.md
```

## Recommended implementation stages

### Stage 5A — Server foundation and central data — COMPLETE

已完成：

```text
VideoMonitor.Server
SQLite V1
central Device Catalog persistence
secret protection abstraction
backup foundation
health/readiness foundation
```

### Stage 5B — Central Catalog API and WPF data source

目标：

```text
SQLite V2 configuration revisions
Catalog REST API
safe Device/Group DTOs
password write-only semantics
optimistic concurrency
WPF CatalogApiClient
process-local ClientCatalogCache
device management -> Server API
monitor tree -> same central client cache
Server unavailable/reconnect handling
```

关键原则：

```text
Server SQLite = single source of truth
no production editable JSON fallback
no silent last-write-wins
no password returned by catalog reads
```

当前没有账号权限需求，本阶段不做 User/Login/RBAC/JWT/Client Enrollment。

旧 `JsonDeviceCatalogStore` 只允许暂时服务于开发期单摄像头验证兼容路径，不做 Legacy Migration API，也不做多客户端旧配置合并。

### Stage 5C — ZLM Server integration and StreamManager

目标：

```text
ZlmClient moved behind Server/Infrastructure
StreamKey
StreamEntry
DeviceRevision invalidation
SingleFlight
MediaReady verification
ColdStartLimiter
structured stream errors
```

### Stage 5D — ZLM Hooks and reconciliation

目标：

```text
on_stream_changed
on_stream_none_reader
on_server_started
StreamReconciler
ZLM restart recovery
stale proxy/media mismatch recovery
```

### Stage 5E — WPF ServerPlaybackSourceResolver

目标：

```text
WPF no longer owns production ZLM API calls
POST /api/v1/playback/resolve
safe PlaybackDescriptor
structured error mapping
remove production dependency on local Camera credentials
```

`LocalZlmPlaybackSourceProvider` 只保留开发/过渡路径，并在该路径失去必要性后删除。

### Stage 5F — WPF 4+3 PlaybackManager

目标：

```text
1 app-lifetime LibVLC
1 process PlaybackManager
7 independent tile controllers/sessions/players
AssignmentVersion + Cancellation
LastAssignmentWins
group switching
client-side bounded reconnect
clean shutdown
```

### Stage 5G — System status and client resilience

目标：

```text
System Status page
Server/ZLM/Stream/Backup health
optional non-sensitive read-only disk cache if real offline-start need is confirmed
client diagnostics/version metadata as needed
```

本阶段不重新引入可编辑本地 Catalog。

### Stage 5H — Deployment packaging

目标：

```text
Windows Service hosting
Server/ZLM separate lifecycle
Program Files vs ProgramData
HTTPS or equivalent authenticated encrypted intranet transport
production config
secret handling
logging/retention
backup destination
upgrade flow
```

第一版部署不假设固定只有约 10 台 WPF。控制面、Catalog API 和共享 Stream 模型按约 100 台或更多受控内网客户端设计。

### Stage 5I — Production validation

必须验证：

```text
100-client control-plane/catalog load simulation
multiple clients editing same aggregate -> conflict, not overwrite
multiple clients reading same catalog
many clients same stream -> one upstream
representative clients same 7 streams
unique-stream cold-start pressure
rapid A->B->C
100 group switches
camera disconnect/reconnect
client kill/network loss
ZLM restart
Server restart
hook loss
proxy exists/media missing
backup + replacement-host recovery
7 x real H.264/H.265 per representative WPF
8h / 24h soak
credential leak scan
```

不要求第一版准备 100 台物理 WPF 做每项测试；控制面可使用自动化并发客户端模拟，实际视频解码使用代表性物理客户端和真实 Camera/ZLM 压测。

ColdStartLimit、TTL、Retry Delay 等最终生产参数必须通过测试确定，不能提前拍脑袋写死。

## Deferred / non-goals

第一版不做：

- ASP.NET 视频代理；
- 100 路永久拉流；
- WPF distributed Lease/Heartbeat global refcount；
- Server/ZLM HA Cluster；
- 外部 MySQL/SQL Server/PostgreSQL 服务；
- 没有业务需求时提前建设 User/Login/RBAC/JWT；
- Client Enrollment/每客户端长期认证 Token；
- Legacy JSON 正式迁移/多客户端旧 Catalog 合并；
- 未经真实硬件验证的 seamless double-decoder switching。

## Working rule for future AI sessions

任何新 ChatGPT/Codex/Luna Session 开始修改项目前，先阅读：

```text
docs/ARCHITECTURE.md
docs/ROADMAP.md
docs/superpowers/specs/2026-08-29-centralized-video-monitoring-architecture-design.md
docs/superpowers/specs/2026-08-29-stage-5b-central-catalog-api-wpf-data-source-design.md
```

再检查当前 Git HEAD 和最近实现计划。

重大设计变化必须先更新文档并提交 Git；没有进入 Git 的聊天决定不算正式架构。
