# 矿山视频监控 WPF UI 设计

## 目标与范围

在 `feature/wpf-video-monitor-ui` 分支新增独立的 .NET 8 WPF 实现。现有 `VideoMonitor.Client` WinForms 代码和测试完整保留为参考，但不再修改其 UI，也不作为新 WPF 解决方案的运行入口。

本阶段只实现 XAML UI、MVVM、假数据、固定业务切换、主副屏定位和四画面区域全屏。不得接入 ZLMediaKit、LibVLCSharp、HCNetSDK、SQL Server、SQLite、UDP/RTP、GB28181、录像、告警后台或权限系统。

## 技术与项目结构

- SDK：.NET SDK 8.0.424，由现有 `global.json` 固定
- `src/VideoMonitor.Core`：`net8.0` 类库，包含模型、假数据目录、布局快照和切换服务
- `src/VideoMonitor.Wpf`：`net8.0-windows` WPF 应用，包含 XAML、ViewModel、主题、控件、页面和显示器服务
- `tests/VideoMonitor.Core.Tests`：`net8.0` xUnit 测试项目，只测试 Core 业务
- MVVM：CommunityToolkit.Mvvm 8.4.2

`VideoMonitor.sln` 作为 WPF 当前方向的解决方案，包含上述三个新项目。旧 WinForms 项目文件仍在分支中，可以单独构建和查阅，但不继续扩展。

## Core 业务模型

Core 使用不可变模型：

- `CameraInfo`：名称、组名、通道号、在线状态、模拟码率、码流类型
- `MonitorGroup`：组名、业务类型、摄像头集合
- `MonitorGroupType`：`UnloadingStation`、`Chute`、`Tunnel`
- `MonitorLayoutSnapshot`：主屏四槽和副屏三槽
- `MonitorSwitchService`：固定槽位状态与切换事件
- `MockMonitorData`：任务指定的全部组和摄像头

启动默认值为：主屏前三槽“备用1”，第四槽“Z-1#巷”，副屏三槽“2#主溜井”。切换规则不可配置：

- `SwitchChuteGroup` 只整体替换主屏 1/2/3，保留第 4 槽。
- `SwitchTunnel` 只替换主屏第 4 槽，保留 1/2/3。
- `SwitchUnloadingGroup` 只整体替换副屏三槽，不改变主屏。

Core 不引用 WPF、WinForms 或任何播放/数据组件。

## MVVM 数据流

`MonitorViewModel` 和 `SecondaryMonitorViewModel` 共享一个 `MonitorSwitchService`。主屏树的叶节点命令按组类型调用服务，服务发布新的不可变快照；两个 ViewModel 分别刷新自己的 `ObservableCollection<VideoTileViewModel>`。

`MainViewModel` 负责当前导航项、监控页和全屏状态。其他导航项保留可切换的静态页面，不实现设备或流媒体业务。窗口代码后置只处理 WPF 窗口生命周期、第二屏定位和 Esc 这类视图职责，不承载监控切换规则。

## 统一视觉系统

所有页面引用合并后的 ResourceDictionary：

- `Themes/Colors.xaml`：统一颜色画刷
- `Themes/Typography.xaml`：标题、正文、辅助文字字号和字体
- `Themes/Buttons.xaml`：导航、主按钮、图标按钮样式
- `Themes/Controls.xaml`：卡片、树、状态徽标、滚动条和输入控件样式

颜色严格使用确认值：背景 `#07111D`、侧栏 `#091522`、面板 `#0B1927`、卡片 `#0C1C2B`、边框 `#17334D`、主蓝 `#1687FF`、选中蓝 `#0E5FAE`、在线绿 `#25D366`、警告橙 `#FF9800`、离线灰 `#74808D`、危险红 `#FF4D4F`、主文字 `#F3F6FA`、次文字 `#9EADBA`、弱文字 `#697887`。

控件圆角统一为 6，间距只使用 8、12、16、24 的组合。视觉保持克制、低发光、深色工业监控风格。

## VideoTile

`Controls/VideoTile.xaml` 是真正可复用的视频容器，绑定 `VideoTileViewModel`。顶部显示摄像头名、Online/Warning/Offline 状态徽标和控制图标占位；中间为 16:9 感知的深色模拟画面区域；底部显示模拟码率和码流类型。

状态通过枚举和样式触发器切换颜色，不在页面中重复写色值。以后真实播放器只替换中间内容区域，主副屏 Grid 和业务 ViewModel 不需要改动。

## 主屏布局

`MainWindow` 由顶栏、主体和底部状态栏组成。主体为左导航、中间监控页和右监控树三列；导航固定展示实时监控、设备管理、流媒体管理、录像回放、告警中心、系统配置。

`MonitorView.xaml` 的视频区域使用两行两列等分 Grid，位置固定：槽位 1、2、3 为溜井，槽位 4 为巷道。右树按卸矿站、溜井和巷道分区，仅叶节点执行命令。

## 副屏布局与显示器定位

`SecondaryMonitorWindow` 总高度为 540。内容使用三列 `Width="*"` 的单行 Grid，三个 VideoTile 永不换行。顶部组切换条为覆盖在画面上方的紧凑区域，不改变三列结构。

`ScreenService` 读取 `Screen.AllScreens` 工作区。存在第二显示器时，主窗口置于主屏，副窗口位于第二显示器工作区左上，宽度等于工作区宽度、高度 540；只有一个显示器时，副窗口以普通可拖动测试窗口显示，初始高度仍为 540。

## 四画面全屏

全屏命令只控制整个四画面监控区域。进入后隐藏左导航、右树、顶栏和底栏，将中间 2×2 Grid 铺满无边框最大化窗口；Esc 恢复进入前的窗口边框、尺寸、状态和各区域可见性。不实现单摄像头全屏。

## 测试与验证

Core 测试覆盖：

1. `SwitchChuteGroup` 同时更新主屏 1/2/3 且第 4 槽引用保持不变。
2. `SwitchTunnel` 只更新第 4 槽且 1/2/3 保持不变。
3. `SwitchUnloadingGroup` 同时更新副屏三槽且主屏保持不变。

阶段验证包含 Core 测试、WPF 编译和运行冒烟。最终运行 `dotnet build VideoMonitor.sln`、`dotnet test VideoMonitor.sln`，启动 WPF 应用并保存实际窗口截图。

## Git 约束

实现只提交到 `feature/wpf-video-monitor-ui`。按 Core、主题与 VideoTile、主屏、树与切换、副屏与定位、全屏与最终验证划分本地提交。不合并 `master`，不配置或推送远程仓库。
