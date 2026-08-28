# WPF 现场化与交互收尾设计

## 目标

在现有 `feature/wpf-video-monitor-ui` 分支上完成现场品牌、固定业务工具栏、单画面区域放大、左导航折叠和当前监控信息精简。保留现有 WPF、XAML、CommunityToolkit.Mvvm 和 `MonitorSwitchService` 架构，不新建项目，不重做已经通过的 3+1 与副屏业务。

## 不变范围

以下规则保持不变：

- 主屏固定为三路溜井加一路巷道。
- 溜井切换只更新主屏槽位 1、2、3。
- 巷道切换只更新主屏槽位 4。
- 卸矿站分组只驱动副屏三路。
- 副屏保持 540px 高、单排三等宽。
- 顶部全屏继续表示四画面整体全屏，Esc 恢复。
- 双屏定位继续由 `ScreenService` 负责。

本轮不接入 ZLMediaKit、LibVLCSharp、HCNetSDK、数据库、UDP/RTP、录像后台或告警后台。

## 品牌与工具栏

主窗口软件名、Window Title 和任务栏名称统一为“罗河铁矿-620视频管理机”。顶部品牌区删除 `/ ZLMediaKit`。副屏 Window Title 使用“罗河铁矿-620视频管理机 - 卸矿站监控（副屏）”。底部现有 ZLMediaKit 版本模拟状态可暂时保留。

主标题栏右侧只保留全屏、告警、设置和系统窗口按钮。删除布局与抓图入口。告警按钮默认透明，仅在 Hover 时显示轻微深蓝背景，红色数字 Badge 保留。

## 页面工具区对齐

主监控 breadcrumb 与右侧搜索区使用相同的外层行高和工具区高度：外层行高 48px，内部工具区约 40px，统一垂直居中。通过 Grid、Height、Padding 和 VerticalAlignment 对齐，不使用零散 Margin 补偿。

## VideoTile 精简与单画面模式

VideoTile 标题栏只显示：

- 左侧：`组名 · 通道号`
- 右侧：状态点和状态文字

删除抓图、声音、更多和单格全屏按钮。视频区 Overlay 的时间、码率和码流类型保持不变。

`MonitorViewModel` 增加：

- `IsSingleTileMode`
- `SelectedVideoSlot`
- `ToggleSingleTileCommand`

双击 VideoTile 时，View 只把当前 Tile 作为命令参数转发给 ViewModel。ViewModel 负责进入或退出单画面模式。

四个原有 VideoTile 实例始终存在，DataContext 始终绑定原有 `MainTiles[0..3]`。放大和恢复只通过现有控件的 Grid 行列、RowSpan、ColumnSpan、ZIndex 和 Visibility 状态切换。禁止创建替代 VideoTile，禁止销毁或重新绑定现有 Tile，确保后续视频播放器实例不会因切换模式被重建。

单画面模式只影响主屏中间视频网格，不隐藏顶部、左右侧栏、当前监控信息或状态栏，不影响副屏。切换溜井或巷道时，原 `VideoTileViewModel` 对象继续接收更新，当前放大槽位不丢失。

## 当前监控信息

删除告警信息、录像计划和存储信息三个未开发 Tab，将区域标题改为“当前监控信息”。

展开高度约 104px，折叠高度 44px。展开内容使用四个等宽信息块：

1. 当前溜井、当前巷道。
2. 选中画面、IP 地址。
3. 在线状态、码流类型。
4. 分辨率、码率、更新时间。

字段名使用弱化文字，字段值使用主文字或对应状态色。数据全部来自 ViewModel Binding。`SelectedVideoSlot` 默认指向槽位 1；进入单画面模式时更新为双击槽位，退出后保留最近选择，作为当前监控信息的数据来源。

`MonitorViewModel` 增加：

- `IsDetailPanelCollapsed`
- `ToggleDetailPanelCommand`

折叠只改变显示高度，不修改监控组、槽位或副屏状态。

## 左导航折叠

删除收藏夹区域。

`MainViewModel` 增加：

- `IsSidebarCollapsed`
- `ToggleSidebarCommand`

展开宽度为 188px，显示图标和文字；折叠宽度为 56px，只显示图标。所有导航按钮保留 ToolTip，选中状态继续使用低饱和深蓝背景和左侧主蓝强调线。底部按钮展开时显示“收起菜单”，折叠时只显示方向相反的箭头。

`MainWindow` code-behind 仅负责把 ViewModel 的折叠状态应用为 GridLength，这是窗口布局行为。进入顶部四画面全屏时导航列临时为 0；Esc 退出时根据 `IsSidebarCollapsed` 恢复为 188px 或 56px，不强制恢复展开状态。

## 状态生命周期

以下状态都是当前进程内的客户端临时 UI 状态：

- `IsSidebarCollapsed`
- `IsSingleTileMode`
- `SelectedVideoSlot`
- `IsDetailPanelCollapsed`

它们不写数据库、不写配置文件、不进入 Core Service。应用重启后使用默认值：侧栏展开、四画面模式、槽位 1 为当前选择、当前监控信息展开。

## MVVM 边界

- View / XAML：布局、Binding、Trigger、控件可见性和视觉状态。
- ViewModel：临时 UI 状态和命令。
- Core / Service：现有监控业务切换规则。
- code-behind：窗口 Chrome、窗口状态、Esc、GridLength 应用和双击事件到命令的轻量转发。

不在 MouseDoubleClick、按钮 Click 或窗口 code-behind 中复制监控业务规则。

## 测试

现有核心测试文件保持不变。为现有测试项目增加对 WPF ViewModel 的测试覆盖，并在 Windows 目标框架下运行：

1. 双击指定槽位进入单画面模式，`SelectedVideoSlot` 正确。
2. 再次切换退出单画面模式，四画面状态恢复。
3. 收起和展开左菜单不改变当前溜井、巷道或主屏槽位。
4. 折叠和展开当前监控信息不改变当前监控状态。
5. 原有溜井、巷道和卸矿站切换测试继续通过。

最终运行 `dotnet build` 和 `dotnet test`，要求 0 error。

## 验收与交付

运行应用并提供：

- 主页面截图。
- 左菜单折叠截图。
- 单个视频区域放大截图。
- 当前监控信息折叠截图。
- 副屏截图。

提交产品代码和验证产物到本地 `feature/wpf-video-monitor-ui` 分支。不得 merge `master`，不得 push。
