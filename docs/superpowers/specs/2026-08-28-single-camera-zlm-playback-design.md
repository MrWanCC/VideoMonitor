# 单路海康 IPC 经 ZLMediaKit 播放设计

## 目标与边界

本阶段只打通一条真实视频链路：海康 Camera01 经 RTSP 由 ZLMediaKit 拉流，再由 LibVLCSharp.WPF 在主屏左上 `VideoTile` 播放。

其他三个主屏画面与三个副屏画面继续显示模拟占位。本阶段不实现 PLC UDP、多路真实播放、数据库、Server、HCNetSDK、PTZ、录像、复杂自动重连或流媒体管理页面。

现有 `MonitorSwitchService`、主屏 3+1、卸矿站副屏三路、单画面放大、页面实例保持等业务与交互保持不变。

## 项目结构

新增 `VideoMonitor.Infrastructure`，引用 `VideoMonitor.Core`：

- `Hikvision/HikvisionRtspUrlBuilder.cs`：唯一的海康 RTSP 地址构造入口。
- `ZLMediaKit/ZlmOptions.cs`：ZLM HTTP、RTSP、Vhost、App 等配置。
- `ZLMediaKit/ZlmClient.cs`：封装 ZLM Web API。
- `ZLMediaKit/ZlmApiResponse.cs`：保留 API code、message 与返回数据。
- `ZLMediaKit/ZlmStreamInfo.cs`：媒体列表中本阶段需要的字段。
- `ZLMediaKit/StreamIdGenerator.cs`：生成稳定 ASCII StreamId。

`VideoMonitor.Wpf/Playback` 新增：

- `VlcPlaybackService.cs`：应用级共享 `LibVLC`，创建和释放单画面会话。
- `PlaybackSession.cs`：保存 `MediaPlayer`、`CameraChannelId`、`StreamId`、`PlaybackUrl`、`ProxyKey`、`OwnsProxy` 与生命周期状态。
- `PlaybackSource.cs`：Provider 准备完成后返回的播放源信息，不包含播放器实例。
- `IPlaybackSourceProvider.cs`：为协调器提供 `PrepareAsync(CameraDevice, CameraChannel)` 与 `ReleaseAsync(PlaybackSource)`。
- `LocalZlmPlaybackSourceProvider.cs`：本阶段实现，内部串联本地字段覆盖、海康 RTSP 构造、ZLM 代理及媒体注册确认。
- `SingleCameraPlaybackCoordinator.cs`：只依赖 `IPlaybackSourceProvider` 与 `VlcPlaybackService`，不直接依赖具体 `ZlmClient`，负责把准备好的播放源交给 VLC。

`VideoMonitor.Core.Tests` 增加 Infrastructure 引用，用可替换 `HttpMessageHandler` 测试请求与响应，不访问真实网络。

## 配置与敏感信息

仓库提交：

- `appsettings.example.json`：只含示例 ZLM 配置。
- `local-device.example.json`：只含示例设备字段。

本机使用并加入 `.gitignore`：

- `appsettings.Development.json`：真实 ZLM `BaseUrl`、`Secret`、`RtspHost`、`RtspPort`，以及 `EnableSingleCameraTest`。
- `local-device.json`：Camera01 对现有“西401溜井 · 通道1”的真实 IP、RTSP 端口、用户名、密码和本地设备标识覆盖。

不直接消费配置中带凭据的完整 `SourceUrl`。真实 RTSP 地址必须由 `HikvisionRtspUrlBuilder` 根据字段生成。密码不得写入普通日志、错误文本或截图；需要记录 RTSP 地址时只允许输出脱敏形式。

配置文件使用 `System.Text.Json` 读取，避免为这一小段配置引入额外配置框架。缺少本地配置且开发验证未启用时保持 Placeholder；显式启用但配置无效时显示配置错误。

## StreamId 规则

ZLM StreamId 使用：

`device_{deviceId:N}_channel_{channelNo}_{streamType}`

其中 `deviceId` 是 `CameraDevice.Id` 的无连字符 GUID，`channelNo` 是通道号。规则只含 ASCII 字母、数字和下划线，不包含中文名称、IP、用户名或密码。设备重命名、IP 或账号变化不会改变 StreamId。

开发配置中的 `camera001` 仅作为本地设备标识，不直接替代 ZLM StreamId。

## 海康 RTSP 规则

`HikvisionRtspUrlBuilder` 接收 `CameraDevice` 与 `CameraChannel`，生成：

`rtsp://{username}:{password}@{ip}:{rtspPort}/Streaming/Channels/{channelCode}`

通道编码规则为：

- 主码流：`channelNo * 100 + 1`
- 辅码流：`channelNo * 100 + 2`

因此通道 1 主码流为 101，辅码流为 102；通道 2 对应 201/202。WPF 层不得拼接海康 RTSP 地址。

## ZLM 调用与代理生命周期

`ZlmClient` 使用注入的 `HttpClient`，实现：

- `CheckServerAsync`：调用服务器配置接口确认 API 可用。
- `GetMediaListAsync`：按 Vhost、App、Stream 筛选媒体。
- `AddStreamProxyAsync`：固定 `rtp_type=0`，即 ZLM 内部使用 TCP 拉取摄像头 RTSP。
- `DeleteStreamProxyAsync`：只接收 ZLM 返回的代理 key。

调用顺序：

1. 检查 ZLM API。
2. 查询目标 StreamId 是否已存在。
3. 已存在时复用，创建 `PlaybackSession` 且 `OwnsProxy=false`。
4. 不存在时调用 `addStreamProxy`，保存返回 key，设置 `OwnsProxy=true`。
5. 在有界超时内短间隔轮询 `getMediaList`，确认相同 Vhost/App/Stream 已注册。
6. 构造 `rtsp://{RtspHost}:{RtspPort}/{App}/{StreamId}` 交给 VLC。
7. 应用退出时先停止播放器；仅当 `OwnsProxy=true` 且 `ProxyKey` 有效时调用 `delStreamProxy`。

如本次创建代理后媒体注册超时，协调器仍保留返回 key并尝试清理本次代理。禁止删除启动前已经存在的代理。

所有 API 结果保留 HTTP 失败、ZLM `code`、ZLM `msg` 和阶段信息，不使用吞异常后返回 `false` 的方式。

## 播放源抽象与生产迁移边界

`IPlaybackSourceProvider` 是本阶段唯一新增的生产迁移接缝，保持轻量：

- `PrepareAsync(CameraDevice, CameraChannel)`：返回 `PlaybackSource`。
- `ReleaseAsync(PlaybackSource)`：释放本次 Provider 拥有的运行期资源。

`PlaybackSource` 至少包含：

- `CameraChannelId`
- `StreamId`
- `PlaybackUrl`
- `ProxyKey`
- `OwnsProxy`

`SingleCameraPlaybackCoordinator` 不知道 ZLM Secret、海康 RTSP 拼接规则或代理 API，只负责播放状态与 VLC 会话。当前注入 `LocalZlmPlaybackSourceProvider`，其内部仍执行：

`local-device` 字段覆盖 → `HikvisionRtspUrlBuilder` → `ZlmClient` → `AddStreamProxy`/已存在流复用 → MediaList 确认 → 返回 ZLM RTSP 播放地址。

`PlaybackSession` 明确记录 `CameraChannelId`、`StreamId` 与 `PlaybackUrl`。`ProxyKey` 和 `OwnsProxy` 仅用于当前运行期释放本次进程创建的代理，禁止写入设备模型、数据库、长期配置或其他持久化存储。

当前 WPF 直接控制 ZLM 只用于第一阶段单机真实视频验证。未来多客户端生产架构由 `VideoMonitor.Server` 统一管理 AddStreamProxy、DeleteStreamProxy、ZLM Hook、StreamRegistry、引用计数/Lease 与 ZLM 健康状态；WPF 最终只向 Server 获取播放地址，不持有摄像头真实账号密码，也不持有 ZLM API Secret。

本阶段不创建 `ServerPlaybackSourceProvider`，不实现 `VideoMonitor.Server`，也不实现上述生产端能力。未来只需用 `ServerPlaybackSourceProvider` 替换当前 Provider，协调器与播放器边界保持不变。

## VLC 与 WPF 生命周期

使用正式版：

- `LibVLCSharp.WPF` 3.10.1
- `VideoLAN.LibVLC.Windows` 3.0.23.1

应用级只创建一个共享 `LibVLC`。当前真实左上画面创建一个 `MediaPlayer` 与一个 `PlaybackSession`。`VideoTile` 不拥有全局 LibVLC，也不在布局变化时创建或销毁播放器。

`VideoTile` 保持现有实例，只新增四种播放展示状态：

- `Placeholder`：模拟视频画面。
- `Loading`：正在连接视频。
- `Playing`：显示 `VideoView`。
- `Error`：显示阶段性错误标题及简述。

播放器会话由应用组合根和协调器持有。双击放大/恢复、侧栏折叠、详情折叠、Monitor/Device 页面切换只改变现有 View 的布局或 Visibility，不触发重新准备 PlaybackSource、重新 AddStreamProxy、重新创建 MediaPlayer 或重新缓冲。

为避免 WPF airspace 问题，第一阶段优先使用 `VideoView` 支持的内容承载方式；如现有透明 Overlay 不稳定，标题栏保持在视频上方，码率与码流移到安全区域，不引入透明 Window 或 `AllowsTransparency=True`。

## 开发验证入口

`EnableSingleCameraTest=true` 时，程序仅初始化 Camera01，并让启动时的溜井组对齐“西401溜井”，随后连接左上 VideoTile。配置为 false 或缺失时，所有画面保持当前模拟状态。

该入口集中在应用组合根与 `SingleCameraPlaybackCoordinator`，不向正式导航或设备管理页面增加按钮，后续可以直接移除。

## 错误状态

协调器至少区分：

- ZLM 不可连接。
- 摄像头 RTSP 拉流失败。
- ZLM API 注册代理失败。
- ZLM 媒体注册超时。
- LibVLC 播放启动失败。

VideoTile 显示短标题和简述；内部异常和 ZLM code/message 用于诊断，但任何诊断文本都不得包含密码或未脱敏 RTSP URL。

## 测试与验证

先写失败测试，再写最小实现。自动测试覆盖：

1. 通道 1 主码流生成 101。
2. 通道 1 辅码流生成 102。
3. 多通道编码规则正确。
4. StreamId 稳定。
5. StreamId 只含允许的 ASCII 字符。
6. ZLM 请求使用配置中的 App/Vhost，且 `rtp_type=0`。
7. addStreamProxy 成功响应解析并保留 key。
8. ZLM API 错误 code/message 完整传递。
9. 已存在流时 `OwnsProxy=false`。
10. 本次创建代理时 `OwnsProxy=true`，释放时使用返回 key。

现有测试必须全部继续通过。完成后执行 Debug/Release 所需的 `dotnet build` 与 `dotnet test`。

人工验证依次检查 ZLM API、Camera01 RTSP 端口、addStreamProxy 返回、MediaList 注册、ZLM RTSP 播放、左上真实画面、单画面放大无重缓冲、页面往返保持播放，以及错误 RTSP 的非阻塞错误展示。

实际凭据只存在于 Git 忽略文件；人工验证截图不得包含配置文件、Secret 或明文密码。
