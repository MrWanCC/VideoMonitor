# VideoMonitor V2 中心化架构设计

**状态：** 已讨论确认，尚未进入业务代码实施  
**日期：** 2026-08-29  
**范围：** 中心设备数据、VideoMonitor.Server 控制面、ZLMediaKit 流生命周期、WPF 4+3 多画面播放、加密/备份/恢复、部署与上线验收。

## 1. 背景与目标

当前项目已经验证了单摄像机链路：

```text
Hikvision RTSP -> ZLMediaKit -> LibVLCSharp -> WPF VideoTile
```

正式目标拓扑约为：

- 约 100 路注册摄像机；
- 1 个中心 ZLMediaKit；
- 约 10 台 WPF Client；
- 每个 Client 最多 4 个主屏 Tile + 3 个副屏 Tile = 7 路同时播放；
- 不要求 100 路永久拉流，按实际观看需求拉取；
- 多客户端观看同一路时共享一个 Camera -> ZLM 上游。

正式版本必须做到：

- WPF 不保存摄像机密码和 ZLM API Secret；
- 所有客户端使用一套中心设备数据；
- Server 不经过视频数据；
- 同一路并发请求只能触发一次上游启动；
- 快速切换、摄像机掉线、ZLM/Server 重启、Hook 丢失和僵尸 Proxy 后能够恢复；
- Windows 优先部署，但核心服务不绑定 Windows。

本设计取代“正式 WPF 直接管理 ZLM Proxy”的方向。现有 `LocalZlmPlaybackSourceProvider` 仅保留为开发/过渡适配器。

## 2. 总体边界

### 2.1 视频数据链

```text
Camera -> ZLMediaKit -> WPF / LibVLC
```

`.NET Server` 不代理、不转发、不转码视频数据。

### 2.2 控制链

```text
WPF -> VideoMonitor.Server -> ZLM API
ZLM -> VideoMonitor.Server Internal Hooks
```

正式 WPF 不拥有：

```text
Camera Password
Camera credential-bearing RTSP URL
ZLM Secret
ProxyKey
OwnsProxy
AddStreamProxy / DelStreamProxy
```

Server 负责设备凭据、流创建、真正 Ready 校验、异常 Reconcile 和 ZLM 管理接口。

## 3. 第一版部署拓扑

Windows-first：

```text
中心 Windows 主机
├─ VideoMonitor.Server   (.NET 8 ASP.NET Core Windows Service)
├─ ZLMediaKit            (独立 Service/进程)
└─ C:\ProgramData\VideoMonitor\Server\...

Camera -> ZLM
WPF -> Server API
WPF -> ZLM Playback
```

Server 和 ZLM 即使同机也必须是独立进程。

必须区分：

```text
MediaServer.BaseUrl       Server 调 ZLM API，可为 127.0.0.1
MediaServer.PlaybackHost  WPF 播放地址，必须是 Client 可访问地址
```

第一版不做双 Server、双 ZLM、数据库集群或自动 HA。

## 4. 最终页面信息架构

### 4.1 实时监控

保留当前监控产品方向：

- 左侧设备/分组树；
- 4 路主屏；
- 3 路副屏；
- 分组切换；
- 单 Tile 替换；
- Tile 独立状态。

WPF PlaybackState：

```text
Empty
Resolving
Connecting
Playing
Reconnecting
Failed
```

用户文案可以区分：

```text
未选择
正在获取视频流
正在连接视频
播放中
正在重连
摄像机离线
视频服务异常
本机播放失败
```

### 4.2 设备管理

现有页面外观尽量保留，但正式数据源改成 Server API。

管理：

- DeviceGroup；
- CameraDevice；
- CameraChannel；
- 主码流 / 子码流（由 `CameraChannel.StreamType` 表达）；
- Enabled；
- 厂商、型号、IP、SDK/RTSP Port、备注等。

第一版领域模型不新增独立 `StreamProfile` 实体：同一物理通道的 Main/Sub
分别对应两条 `CameraChannel` 记录，由 `StreamType` 区分。

密码允许录入/修改，但查询详情不返回原密码，只返回 `hasPassword` 等信息。

### 4.3 系统状态

增加轻量系统状态页：

- Server Online/Uptime；
- SQLite 状态；
- ZLM Online/Version；
- 摄像机总数/启用/异常；
- Ready/Starting/Reconnecting/Failed Stream 数量；
- 活动 Stream 明细；
- 最近备份状态。

首页只保留简洁服务状态提示，不变成运维大屏。

### 4.4 系统设置

客户端本机仅保存：

- ServerAddress；
- ClientId；
- ClientName；
- 自动连接/开机启动；
- 窗口/副屏位置等本机 UI 偏好。

客户端不配置 ZLM Secret 或整套 Camera Credentials。

## 5. 三套状态必须分离

### DeviceStatus

```text
Unknown
Online
Warning
Offline
```

### StreamStatus

```text
Stopped
Starting
Ready
Idle
Reconnecting
Failed
```

### PlaybackState

```text
Empty
Resolving
Connecting
Playing
Reconnecting
Failed
```

例如：

```text
DeviceStatus  = Online
StreamStatus  = Ready
Client3 PlaybackState = Failed
```

说明上游正常，问题属于 Client3 本地播放层。

## 6. StreamKey 与 StreamManager

### 6.1 唯一 StreamKey

```text
DeviceId + ChannelNo + StreamType
```

Main/Sub 是两个不同 StreamKey。

`StreamId` 为运行时派生值，不作为数据库权威字段。

### 6.2 生命周期

```text
Stopped -> Starting -> Ready
                    -> Failed

Ready -> Reconnecting -> Ready/Failed
Ready -> Idle -> Stopped
Failed -> Starting（后续新请求满足重试条件）
```

`Idle` 表示无人观看且等待 ZLM no-reader TTL 的业务状态。

### 6.3 Single Flight

同一个 StreamKey：

```text
10 个 Client 同时 Resolve
=
1 个 StartingTask
+
10 个等待者
```

严禁产生 10 次并发 `addStreamProxy`。

同一 StreamEntry 的 Ensure/Close/Reconcile 需要异步协调，但不能使用一个全局大锁阻塞所有摄像机。

不要在长锁中 await ZLM 网络操作。

### 6.4 Ready 判定

禁止：

```text
addStreamProxy code=0
-> Ready
```

必须核验 exact ZLM Media：

```text
vhost / app / stream / schema
```

Proxy 存在但 Media 不存在属于 Starting/Reconnecting/Stale，不得返回假 Ready。

### 6.5 Failed 冷却

失败不能永久毒死 Entry；后续允许重试，但需要配置化短冷却，防止 10 个 Client 对离线/密码错误 Camera 形成请求风暴。

### 6.6 全局 ColdStartLimiter

Single Flight 只解决“同一路重复”，不能解决 70 路不同 Camera 同时冷启动。

增加独立、可配置的 ColdStartLimiter，仅约束真实 `Stopped -> Starting`。

生产值不提前写死 8/16/32，必须通过真实压测确定。

## 7. ZLM 生命周期职责

### 7.1 Camera -> ZLM 重连

优先由 ZLM `PlayerProxy` 自身负责。

Server 不运行与 ZLM 抢控制权的高频：

```text
DelStreamProxy
AddStreamProxy
DelStreamProxy
AddStreamProxy
```

Server 负责观察、状态、真正异常时 Reconcile。

### 7.2 无人观看关闭

第一版依赖 ZLM 实际 Reader：

```text
reader = 0
-> streamNoneReaderDelayMS
-> on_stream_none_reader
-> Server 判断
-> close true/false
```

WPF 不维护全局 RefCount，也不依赖 Release API 判断“最后一个观看者”。

这样 Client 断电、Kill、蓝屏、网线拔掉也不会因为 Release 没发而破坏正确性。

### 7.3 Hooks

公开 Client API：

```text
/api/v1/...
```

ZLM 内部 Hook：

```text
/internal/zlm/hooks/...
```

第一版关注：

- `on_stream_changed`
- `on_stream_none_reader`
- `on_server_started`
- 需要时再加 keepalive/server-exit

Hook 使用 loopback/可信网络 + Hook Secret 保护。

Hook 只是信号，不是真相；丢 Hook 后必须能够通过 Reconcile 修复。

## 8. Server API

### 8.1 Playback Resolve

核心：

```text
POST /api/v1/playback/resolve
```

Client 只提交业务身份，例如：

```text
DeviceId
ChannelId/ChannelNo
StreamType
```

Server：

1. 校验设备/通道/码流是否存在启用；
2. 获取 Server 保存的凭据；
3. 生成 StreamKey；
4. `StreamManager.EnsureReadyAsync`；
5. 等 ZLM Media 真正 Ready；
6. 返回安全 Playback Descriptor。

返回可包含：

```text
StreamId
StreamType
PlaybackUrl
```

绝不返回：

```text
Camera Password
原始带密码 RTSP URL
ZLM Secret
ProxyKey
OwnsProxy
```

### 8.2 结构化错误

第一版至少：

```text
DEVICE_NOT_FOUND
DEVICE_DISABLED
CHANNEL_NOT_FOUND
STREAM_START_TIMEOUT
CAMERA_UNREACHABLE
CAMERA_AUTH_FAILED
MEDIA_SERVER_UNAVAILABLE
STREAM_START_FAILED
```

### 8.3 Catalog API

中心化：

```text
/api/v1/device-groups
/api/v1/devices
/api/v1/monitor/device-tree
```

具体 REST 细节实施计划中确定，但必须保持 V1 版本边界。

查询设备详情不返回密码原文。

### 8.4 Status API

提供 summary + 管理员活动 Stream 查看。

Client 注册可用于运维/版本统计，但不参与流 RefCount。

## 9. Reconcile 与异常恢复

### 9.1 Runtime State 可重建

这些不进 SQLite：

```text
StreamEntry
Ready/Starting
ViewerCount
Proxy current state
PlaybackSession
```

Server 重启后从 ZLM 真实状态重新认识，不默认删除全部 Proxy 后重建。

### 9.2 StreamReconciler

低频核验非 Stopped / 最近活动 Stream，用于处理：

```text
Hook 丢失
僵尸 Proxy
Proxy exists / Media missing
Server/ZLM 状态漂移
服务重启
```

不要每秒轮询全部 100 台 Camera。

### 9.3 ZLM 重启

ZLM Offline -> Online 后：

```text
旧 Entry 标记 Stale/Unknown
不一次性拉 100 路
谁需要谁 Resolve/恢复
```

### 9.4 Server 重启

ZLM 若仍正常，不删除健康媒体。

第一次 Resolve 或启动 Reconcile 可发现已存在 Ready Media 并重建 StreamEntry。

### 9.5 Device Revision

Camera 配置持久化 revision/version。

StreamEntry 记录启动时对应的 DeviceRevision。

IP/Port/Password/Channel/Profile 变化后，旧 Entry 被标记失效，下一次安全时机重建，不能要求重启 Server 才生效。

## 10. 中心数据：SQLite

第一版中心正式业务数据使用 SQLite。

原因不是 100 路“数据大”，而是已经出现关系、事务和中心唯一数据源需求，同时又不希望部署 MySQL/SQL Server/PostgreSQL 服务。

建议表：

```text
device_groups
camera_devices
camera_channels
server_settings
schema_migrations
```

关键约束：

```text
UNIQUE(device_id, channel_no, stream_type)
```

第一版 `camera_channels` 的业务身份是：

```text
(device_id, channel_no, stream_type)
```

因此同一物理 `channel_no` 可以同时存在 Main 和 Sub 两条记录。

以下字段和状态不属于正式 SQLite 持久化数据：

- `CameraChannel.StreamId`：继续由 `StreamIdGenerator` 在运行时派生；
- `CameraDevice.Status` / `CameraStatus`：运行时设备状态，Server 启动后重新探测或计算；
- Runtime Stream 状态：不把上次退出时的 Online/Offline、Ready 等状态当作下次启动事实。

正式持久化的数据主要是 `DeviceGroup`、`CameraDevice` 配置和
`CameraChannel` 配置。

第一版不为了数据库规范化提前拆分 `StreamProfile`。只有未来确实需要持久化
`Resolution`、`Bitrate`、`FPS`、`Codec`、`ProfileName` 或第三路及更多码流配置时，
才通过数据库 Migration 和领域模型升级为：

```text
CameraChannel
└─ StreamProfile
```

当前不提前实现。

## 11. 数据目录

程序与数据分开：

```text
C:\Program Files\VideoMonitor\Server\
  VideoMonitor.Server.exe
  dll...

C:\ProgramData\VideoMonitor\Server\
  data\videomonitor.db
  security\...
  backups\...
  logs\...
  server-settings.json
```

通过 `IAppPathProvider` 等抽象取得路径，业务代码不散落硬编码 `C:\ProgramData`。

Linux 将来可对应 `/var/lib/videomonitor` 等目录，不重写核心业务。

## 12. 敏感数据加密与异机恢复

SQLite 本身不当作保密边界。

Camera Password、ZLM Secret 等采用应用层加密：

```text
Secret
-> AES-256-GCM
-> ciphertext + nonce/tag
-> SQLite/protected store
```

随机 Master Key 负责数据加密。

Master Key 通过：

```text
IMachineSecretProtector
```

保护。

Windows 第一版：

```text
DPAPI LocalMachine
```

Linux 后续替换实现。

但纯 DPAPI LocalMachine 无法保证“旧服务器全坏 -> 新机器恢复”，因此必须再提供独立 Recovery Key/Recovery Package 机制，用于在替换主机上恢复或重新包装 Master Key。

Recovery Secret 不与普通数据库备份长期放在同一公开目录。

## 13. Backup / Restore

使用 SQLite 一致性 Backup/Snapshot，不在任意写入时简单复制活跃 DB 文件。

层次：

```text
正式 videomonitor.db
+ 本机滚动一致性快照
+ NAS/备用机器异机备份
+ manifest/checksum
+ 独立 Recovery Secret
```

备份可配置保留数量。

连续配置变更可 debounce/coalesce 后生成快照，不必每点一次保存生成一个完整 Backup。

“Backup”和“Export JSON”必须区分：

- Backup：灾备恢复；
- Export：管理/迁移；
- 默认 Export 不包含明文密码。

## 14. 现有 JSON -> Server 迁移

旧 WPF Catalog 的密码可能用 DPAPI LocalMachine 绑定原电脑，因此不能直接复制 JSON 到 Server 解密。

推荐迁移：

```text
旧 WPF（原电脑）
-> 使用旧 IDeviceCatalogStore 正常解密
-> 内存 Catalog
-> 可信 Server API
-> Server 完整校验
-> 一个 SQLite Transaction
-> Server Master Key 重新加密
-> Commit
```

任一数据失败：

```text
ROLLBACK
```

不得留下“100 台只导入 72 台”的半迁移状态。

迁移确认前保留旧 JSON Legacy Backup。

正式迁移后：

```text
WPF -> Server
```

旧 JSON 不能在 Server 离线时偷偷恢复成第二套可编辑权威数据。

WPF 可保存不含敏感数据的只读 `catalog-cache.json` 供 Server 短暂离线时显示上次设备树。

## 15. WPF PlaybackManager

### 15.1 生命周期

全 App：

```text
1 个 LibVLC
1 个 PlaybackManager
```

每 Tile：

```text
独立 Assignment
独立 CTS
独立 PlaybackSession
独立 MediaPlayer
独立 PlaybackState
```

两个 Tile 显示同一路时：

- ZLM 上游共享 StreamKey；
- Client 本地仍是两个独立 MediaPlayer。

### 15.2 Slot

第一版固定：

```text
Main1
Main2
Main3
Main4
Secondary1
Secondary2
Secondary3
```

PlaybackManager 不依赖具体 MainWindow/SecondaryWindow 类型。

### 15.3 Assignment

只表达：

```text
DeviceId
ChannelId
StreamType
```

可附带显示名称。

不包含：

```text
PlaybackUrl
Password
ProxyKey
OwnsProxy
ZLM Secret
```

### 15.4 Last Assignment Wins

每 Slot 使用：

```text
AssignmentVersion / Generation
+
CancellationTokenSource
```

A -> B -> C 快速选择时，即使 A 最后返回，也必须：

```text
generation mismatch
-> Dispose stale result
-> 不允许回写 Tile
```

核心不变量：

> 最后一次用户选择赢，不是最后完成的异步任务赢。

Resolve、Start、Retry、Delay 全部必须服从 Generation + Cancellation。

### 15.5 第一版切换策略

第一版：

```text
Stop/Dispose old local session
-> Resolve/start new
```

保持最多 7 个 Decoder/MediaPlayer。

“旧画面保留到新画面 Playing 再无缝 Swap”推迟到真实 7 路 H.264/H.265 压测以后，因为它可能临时产生最多 14 路本地解码。

### 15.6 7 路切组

七路可并发，但结果独立。

不能因为 C Offline 导致 A/B/D/E/F/G 全部等待或失败。

### 15.7 Client Retry

PlaybackManager 只负责：

```text
ZLM -> 当前 WPF
```

下游恢复。

使用 bounded backoff + jitter，并与 AssignmentVersion/CTS 绑定。

### 15.8 LibVLC 回调纪律

不要在 LibVLC 事件回调线程直接执行复杂 Stop/Dispose/Play/Resolve。

Event -> Signal -> PlaybackManager async control flow -> UI Dispatcher。

### 15.9 Shutdown

```text
禁止新 Assignment
-> Cancel 7 Slot
-> Stop/Dispose Sessions
-> Dispose MediaPlayer/Media
-> Dispose PlaybackManager
-> Dispose app-lifetime LibVLC
-> Dispose HTTP/其他资源
```

## 16. 目标工程职责

```text
VideoMonitor.Core
├─ Domain
└─ Streaming abstractions

VideoMonitor.Infrastructure
├─ SQLite persistence
├─ Secret protection
├─ Backup
└─ ZLM client/integration

VideoMonitor.Server
├─ ASP.NET Core API
├─ StreamManager
├─ StreamReconciler
├─ ZLM Hooks
├─ central Device Catalog services
└─ Status/Backup orchestration

VideoMonitor.Wpf
├─ PlaybackManager
├─ Tile Playback Controller
├─ ServerPlaybackSourceResolver
├─ local non-sensitive settings/cache
└─ UI
```

## 17. 部署、日志与升级

### Windows-first

第一版 Server/ZLM 注册为 Windows Service，并可独立重启。

Server 启动不要求 ZLM 已在线：

```text
Server API/DB 正常
ZLM Offline
-> MediaServerStatus Offline
-> 后台继续检测
-> ZLM 上线后恢复
```

### 配置分类

- 发布默认配置；
- 中心机器配置；
- Secret Store。

ZLM Secret 不长期明文放普通 appsettings。

### Health

至少区分：

```text
/health/live
/health/ready
```

并在系统状态页分别表示 Server、SQLite、ZLM 健康状态。

### Structured Logging

Stream 日志至少包含：

```text
StreamKey
DeviceId
ChannelNo
StreamType
Operation
ElapsedMs
Result
```

严禁完整打印：

```text
rtsp://user:password@...
Camera Password
ZLM Secret
```

ZLM 正式配置关闭可能输出源地址凭据的 API Debug。

### Upgrade

更新 Program Files 不碰 ProgramData。

Server 升级：

```text
一致性 Backup
-> Stop Service
-> 更新程序
-> DB Migration
-> Start
-> Health Check
```

禁止 schema 不兼容时删除数据库重建。

ZLM 可独立升级，系统状态记录其版本/build。

## 18. 上线验收矩阵

必须覆盖：

1. 10 Client 同时 Resolve 同一路 -> 1 次上游冷启动；
2. 10 Client 同看 7 路 -> 70 个下游 Session，但只有 7 个 Camera -> ZLM 上游；
3. 约 70 个不同 StreamKey 同时冷启动 -> ColdStartLimiter 生效；
4. A -> B -> C 快速切换最终永远为 C；
5. 七路中一台离线，其余六路正常；
6. 同一路两个 Tile -> 两个独立 MediaPlayer，一个中心上游；
7. TTL 内切走再切回尽量复用 Warm Stream；
8. 最后 Reader 消失后按 TTL 关闭；
9. Close Hook 与新 Resolve 竞态不会把刚返回 Ready 的流删除；
10. Camera 拔网线/恢复，ZLM 主导上游重连，不产生 Server Add/Delete Storm；
11. WPF 强杀后不依赖 Release；
12. ZLM 重启后按需恢复，不一次拉 100 路；
13. Server 重启后不默认删除健康 ZLM Media；
14. Hook 丢失后 Reconciler 能纠偏；
15. Proxy Exists / Media Missing 不返回假 Ready；
16. 修改 IP/Password 后 Revision 使旧 Runtime Config 失效；
17. JSON Migration 全成或全回滚；
18. Replacement Server 能用 Backup + Recovery 机制恢复 Credential；
19. 日志无凭据泄漏。

## 19. 性能与稳定性

真实环境测试：

```text
7 x 1080p H.264
7 x 1080p H.265
真实码率
真实 GOP/I-frame
```

记录：

```text
CPU
GPU Video Decode
RAM
NIC
丢包
首帧
卡顿
Reconnect
```

分别统计：

```text
ServerColdStartLatency
ClientFirstFrameLatency
```

因为 `Media Ready` 不等于 Client 已拿到可解码关键帧。

网络示例：

```text
70 sessions * 4 Mbps ≈ 280 Mbps
70 sessions * 8 Mbps ≈ 560 Mbps
```

中心 Restream 解决 Camera Connection Multiplicity，但不消灭 ZLM -> Client 下行流量。

必须至少做：

```text
8h soak
24h soak
100 次反复切组
```

跟踪：

```text
WPF Private Bytes
Handle/Thread
MediaPlayer/Media/PlaybackSession 数量
StreamEntry 数量
ZLM Proxy
Reconnect/Failure
Server/ZLM CPU/RAM
```

7 个活跃 Tile 不能随着时间变成几十/几百个未释放 Native Player。

## 20. 第一版明确不做

```text
WPF 自建分布式 Lease/Heartbeat/Global RefCount
100 路永久拉流
ASP.NET 转视频
双 Server/ZLM HA
数据库集群
完整 RBAC 平台
未经压测的无缝双 Decoder 切换
拍脑袋写死 ColdStartLimit/TTL
```

## 21. 外部参考与已知风险

设计参考并吸收以下项目经验：

- ZLMediaKit 配置：  
  https://github.com/ZLMediaKit/ZLMediaKit/blob/master/conf/config.ini
- ZLMediaKit #2773：重复/并发 addStreamProxy 不能把 API Success 当 Ready：  
  https://github.com/ZLMediaKit/ZLMediaKit/issues/2773
- ZLMediaKit #4210：百路相机拉流场景出现 Proxy 存在但 Media 查询异常等问题：  
  https://github.com/ZLMediaKit/ZLMediaKit/issues/4210
- ZLMediaKit PlayerProxy：  
  https://github.com/ZLMediaKit/ZLMediaKit/blob/master/src/Player/PlayerProxy.cpp
- LibVLCSharp Best Practices：  
  https://github.com/videolan/libvlcsharp/blob/3.x/docs/best_practices.md
- Frigate / go2rtc Restream 思路：  
  https://github.com/blakeblackshear/frigate
- MediaMTX On-demand Source 生命周期可作为对照：  
  https://github.com/bluenviron/mediamtx

这些参考不能替代实际 ZLM Build、Hikvision Camera、Codec/GOP、LAN、WPF Client Hardware 的真实测试。

## 22. 最终决定

正式方向冻结为：

```text
Central VideoMonitor.Server
+ SQLite authoritative data
+ recoverable application-level secrets
+ StreamManager
+ per-StreamKey SingleFlight
+ global ColdStartLimiter
+ ZLM native reader / no-reader lifecycle
+ MediaReady verification
+ StreamReconciler
+ one WPF PlaybackManager
+ one app-lifetime LibVLC
+ one independent MediaPlayer per Tile
+ AssignmentVersion + Cancellation
+ Windows-first separate Server/ZLM services
+ real load/failure/soak acceptance
```

任何后续重大偏离必须先更新本设计或新增 superseding spec，并提交 Git 后再实施。
