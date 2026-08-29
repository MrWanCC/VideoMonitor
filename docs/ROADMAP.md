# VideoMonitor Roadmap

**Updated:** 2026-08-29

## Current state

已完成：

- WPF 基础监控 UI；
- Device Catalog 与设备管理基础能力；
- 本机 JSON 持久化、SchemaVersion、原子替换、`.bak` 恢复、DPAPI 等验证；
- 单摄像机 Hikvision RTSP -> ZLMediaKit -> LibVLCSharp -> WPF Tile 播放验证；
- 中心化 V2 架构设计讨论。

正式 V2 架构设计：

`docs/superpowers/specs/2026-08-29-centralized-video-monitoring-architecture-design.md`

当前状态：

```text
Architecture approved
Documentation landing in Git
Business-code implementation NOT started
```

## Recommended implementation stages

### Stage 5A — Server foundation and central data

目标：

```text
VideoMonitor.Server
SQLite
central Device Catalog
secret protection abstraction
backup foundation
health/status foundation
```

先建立生产安全边界，不继续把正式 ZLM 管理能力写深到 WPF。

### Stage 5B — Existing catalog migration

目标：

```text
old WPF JSON/DPAPI
-> original machine decrypt
-> migration API
-> SQLite transaction
-> Server re-encryption
```

验证全量迁移、回滚和新 Server 读取。

### Stage 5C — ZLM Server integration and StreamManager

目标：

```text
ZlmClient moved behind Server/Infrastructure
StreamKey
StreamEntry
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
```

`LocalZlmPlaybackSourceProvider` 只保留开发/过渡路径。

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

### Stage 5G — UI data-source migration and system status

目标：

```text
Device management -> Server API
Monitor tree -> Server projection
System Status page
Server/ZLM/Stream/Backup health
client local non-sensitive cache
```

### Stage 5H — Deployment packaging

目标：

```text
Windows Service hosting
Server/ZLM separate lifecycle
Program Files vs ProgramData
production config
secret handling
logging/retention
backup destination
upgrade/migration flow
```

### Stage 5I — Production validation

必须验证：

```text
10 clients same stream
10 clients same 7 streams
~70 unique cold streams
rapid A->B->C
100 group switches
camera disconnect/reconnect
client kill/network loss
ZLM restart
Server restart
hook loss
proxy exists/media missing
backup + replacement-host recovery
7 x real H.264/H.265
8h / 24h soak
credential leak scan
```

ColdStartLimit、TTL、Retry Delay 等最终生产参数必须通过测试确定，不能提前拍脑袋写死。

## Deferred / non-goals

第一版不做：

- ASP.NET 视频代理；
- 100 路永久拉流；
- WPF distributed Lease/Heartbeat global refcount；
- Server/ZLM HA Cluster；
- 外部 MySQL/SQL Server/PostgreSQL 服务；
- 完整 RBAC 平台；
- 未经真实硬件验证的 seamless double-decoder switching。

## Working rule for future AI sessions

任何新 ChatGPT/Codex/Luna Session 开始修改项目前，先阅读：

```text
docs/ARCHITECTURE.md
docs/ROADMAP.md
docs/superpowers/specs/2026-08-29-centralized-video-monitoring-architecture-design.md
```

再检查当前 Git HEAD 和最近实现计划。

重大设计变化必须先更新文档并提交 Git；没有进入 Git 的聊天决定不算正式架构。
