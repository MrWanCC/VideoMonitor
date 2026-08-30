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
- WPF Client 数量不按固定 10 台设计，控制面和共享流模型应支持约 100 台或更多受控内网客户端；
- 每台 Client 最多 4 主屏 + 3 副屏 = 7 路本地播放；
- Stream 按需启动，而不是默认永久拉 100 路；
- 同一个 `DeviceId + ChannelNo + StreamType` 在中心只允许一个共享上游启动流程；
- 第一版接受单 VideoMonitor.Server + 单 ZLMediaKit，不做自动 HA。

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
  Groups / Devices / Channels (ChannelNo + StreamType)
  Configuration Revision
  Backup / Recovery

WPF:
  ServerAddress
  UI preferences
  process-local non-sensitive catalog cache
```

WPF 不保存正式 Camera Password、ZLM Secret 或自己的可编辑权威设备库。

V1 keeps `StreamType` on `CameraChannel`; `StreamId` and `CameraStatus` are runtime-only.

从 Stage 5B 起，正式 WPF 设备目录读写必须通过 Server。Server 不可用时不能自动退回本地 JSON 继续编辑。现有 JSON Catalog 只允许作为开发期单摄像头验证兼容路径，后续随 ServerPlaybackSourceResolver 完成而退出。

Server 在线期间，WPF 通过带少量客户端 jitter 的 bounded 周期 `GET /api/v1/catalog` 低频刷新远端目录；刷新不可重叠，失败保留现有缓存并进入 bounded reconnect/backoff，不触发 JSON fallback。

## Catalog concurrency

设备和分组配置采用按 Aggregate 的 Configuration Revision：

```text
DeviceGroup.Revision
CameraDevice.Revision
```

`CameraChannel` 不单独持有 Revision，通道配置变化使父 `CameraDevice.Revision` 增加。

Revision 同时用于：

- 多 WPF 客户端乐观并发控制，防止静默覆盖；
- 后续 StreamManager 判断流启动后设备配置是否已经失效。

普通 GET、Camera Online/Offline、Stream Runtime 状态不能增加 Configuration Revision。

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

当前产品按受控内网部署考虑。开发环境允许本机 HTTP；正式 WPF -> Server Catalog API 使用 HTTPS 或等价的已认证加密私网传输，因为新增/修改设备时可能传输 Camera Password。正式部署只开放需要的 Server/ZLM 端口。WPF 不直接拥有 Camera 密码或 ZLM 管理 Secret。

## Security

- 当前阶段没有用户账号、登录或 RBAC 业务需求，不为 Stage 5B 额外引入 JWT/RefreshToken/Client Enrollment；
- “页面隐藏/客户端模式”不视为安全权限边界；
- Camera Password/ZLM Secret 不向普通 WPF 返回；
- SQLite 中敏感字段使用应用层加密；
- Windows Master Key 使用 DPAPI LocalMachine 保护；
- 另有独立 Recovery 机制支持换机灾难恢复；
- 日志禁止输出完整带密码 RTSP URL、Camera Password、ZLM Secret；
- ZLM 管理 API 优先 loopback/private，仅 Server 使用；
- 未来若出现真实身份/权限需求，可以在现有 API 前增加认证授权，但不能改变 Server 作为唯一 Catalog 权威数据源的边界。

## Detailed approved designs

基础中心化设计：

`docs/superpowers/specs/2026-08-29-centralized-video-monitoring-architecture-design.md`

Stage 5B 中央 Catalog 设计：

`docs/superpowers/specs/2026-08-29-stage-5b-central-catalog-api-wpf-data-source-design.md`

Stage 5B 设计明确 supersede 早期架构文档中“必须实现旧 JSON 正式迁移”的要求，因为项目尚未现场投产，没有需要保留的生产 Legacy Catalog。

## Rule

如果聊天内容与仓库正式架构文档冲突，以仓库中**更新日期更晚且明确标记 superseding/approved 的设计文档**为准。重大架构决定只存在于聊天中、不进入 Git，不视为正式项目方案。
