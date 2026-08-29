# VideoMonitor Architecture

> 当前架构入口。重大架构变更必须先更新此文件或对应 `docs/superpowers/specs/` 设计文档并提交 Git。

## Current direction

VideoMonitor 正从单机 WPF 直接控制 ZLMediaKit 的验证架构，演进为中心化多客户端架构。

```text
Camera -> ZLMediaKit -> WPF                 视频数据链
WPF -> VideoMonitor.Server -> ZLMediaKit    控制链
ZLM -> VideoMonitor.Server Hooks            媒体状态通知
```

`.NET Server` 不转发视频数据。

## Production assumptions

- 约 100 路注册摄像机；
- 1 个中心 ZLMediaKit；
- 约 10 台 WPF Client；
- 每台 Client 最多 4 主屏 + 3 副屏 = 7 路本地播放；
- Stream 按需启动，而不是默认永久拉 100 路；
- 同一个 `DeviceId + ChannelNo + StreamType` 在中心只允许一个共享上游启动流程。

## Projects

目标工程职责：

```text
VideoMonitor.Core
  Domain + interfaces

VideoMonitor.Infrastructure
  SQLite / secrets / backup / ZLM integration

VideoMonitor.Server
  ASP.NET Core API / central catalog
  StreamManager / Reconciler / ZLM Hooks

VideoMonitor.Wpf
  UI / PlaybackManager / ServerPlaybackSourceResolver
```

当前 `LocalZlmPlaybackSourceProvider` 是开发/过渡实现，不是正式生产控制边界。

## Data ownership

中心 Server 是唯一正式设备数据源。

```text
Server:
  SQLite
  Camera Credentials
  Groups / Devices / Channels / StreamProfiles
  Backup / Recovery

WPF:
  ServerAddress
  ClientId/ClientName
  UI preferences
  non-sensitive read-only catalog cache
```

WPF 不保存正式 Camera Password、ZLM Secret 或自己的可编辑权威设备库。

## Streaming rules

Canonical StreamKey:

```text
DeviceId + ChannelNo + StreamType
```

关键规则：

- per-StreamKey Single Flight；
- `addStreamProxy success != Media Ready`；
- Ready 必须核验真实 ZLM Media；
- Proxy Exists / Media Missing 必须 Reconcile；
- Camera -> ZLM 重连主要由 ZLM 负责；
- 无人观看生命周期优先使用 ZLM Reader + no-reader delay/hook；
- WPF 不维护全局最后观看者 RefCount；
- 不把 Runtime Stream 状态持久化为 SQLite 业务真相；
- Server/ZLM 重启后 Runtime State 可重建。

## WPF playback rules

整个 WPF Process：

```text
1 LibVLC
1 PlaybackManager
```

每个 Tile：

```text
independent Assignment
independent CTS
independent PlaybackSession
independent MediaPlayer
```

快速 A -> B -> C 切换必须满足：

> Last assignment wins, not last task completion.

使用 `AssignmentVersion/Generation + CancellationTokenSource` 防止旧异步结果回写。

第一版切换先 Stop/Dispose Old Session，再启动 New Session，避免无压测情况下临时翻倍到 14 路 Decoder。

## Status separation

不要混用：

```text
DeviceStatus
StreamStatus
PlaybackState
```

三者分别表示 Camera、Server/ZLM Stream、本地 WPF Tile。

## Deployment

第一版：

```text
Windows-first
VideoMonitor.Server = Windows Service
ZLMediaKit = separate Windows Service/process
SQLite = embedded DB
Persistent data = ProgramData
```

核心业务保持跨平台；Linux 后续替换 Service hosting、paths 和 machine-secret protector，不重写 StreamManager/API/SQLite/WPF 协议边界。

## Security

- Camera Password/ZLM Secret 不向普通 WPF 返回；
- SQLite 中敏感字段使用应用层加密；
- Windows Master Key 使用 DPAPI LocalMachine 保护；
- 另有独立 Recovery 机制支持换机灾难恢复；
- 日志禁止输出完整带密码 RTSP URL、Camera Password、ZLM Secret；
- ZLM 管理 API 优先 loopback/private，仅 Server 使用。

## Detailed approved design

详见：

`docs/superpowers/specs/2026-08-29-centralized-video-monitoring-architecture-design.md`

该文档包含：

- 页面最终信息架构；
- StreamManager/SingleFlight/ColdStartLimiter；
- Server API + ZLM Hooks；
- Reconcile/异常恢复；
- SQLite/加密/备份/JSON迁移；
- WPF PlaybackManager；
- Windows-first部署；
- 压测、故障与 8h/24h 稳定性验收。

## Rule

如果聊天内容与仓库正式架构文档冲突，以仓库中**更新日期更晚且明确标记 superseding/approved 的设计文档**为准。重大架构决定只存在于聊天中、不进入 Git，不视为正式项目方案。
