# 矿山视频监控 UI 框架设计

## 目标与边界

创建一个可编译运行的 .NET 8 WinForms 桌面应用，本阶段仅实现现代深色工业监控 UI、假数据展示、主副屏识别和固定业务切换。项目不接入真实视频、数据库、录像、告警后台、权限或任何用户明确排除的协议与组件。

## 技术选型

- 目标框架：`net8.0-windows`
- SDK：.NET SDK 8.0.424
- UI：WinForms、AntdUI 2.4.6、原生 `TableLayoutPanel`
- 测试：xUnit
- IDE：Visual Studio 2022 兼容的 SDK 风格解决方案和项目

AntdUI 用于按钮等现代视觉控件；稳定的百分比自适应布局使用 WinForms 原生容器。UI 通过代码构建，不依赖设计器生成文件，避免布局逻辑分散。

## 解决方案结构

解决方案包含：

- `src/VideoMonitor.Client`：WinForms 应用
- `tests/VideoMonitor.Client.Tests`：核心切换逻辑单元测试

客户端按职责拆分：

- `Forms/MainForm.cs`：主界面组织、全屏进入与恢复、协调主副屏窗体
- `Forms/SecondaryMonitorForm.cs`：卸矿站三画面和顶部组切换按钮
- `Controls/VideoTileControl.cs`：单个模拟视频格子及状态显示
- `Controls/VideoGridControl.cs`：主屏 2×2 或副屏 1×3 的可靠布局容器
- `Controls/MonitorTreeControl.cs`：分组树及组选择事件
- `Models/CameraInfo.cs`：摄像头显示信息
- `Models/MonitorGroup.cs`：监控组和摄像头集合
- `Models/MonitorGroupType.cs`：卸矿站、溜井、巷道三种业务类型
- `Services/MonitorSwitchService.cs`：固定槽位状态及业务切换规则
- `Services/ScreenService.cs`：显示器检测与副屏窗口定位
- `Mock/MockMonitorData.cs`：任务指定的全部假数据

## 业务状态与数据流

`MonitorSwitchService` 持有七个当前槽位：主屏四槽和副屏三槽。启动时前三个主槽为“备用1”三路，第四个主槽为“Z-1#巷”，副屏默认为“2#主溜井”三路。

切换规则固定如下：

- 选择溜井组：仅替换主屏槽位 1、2、3。
- 选择巷道：仅替换主屏槽位 4。
- 选择卸矿站组：仅替换副屏槽位 1、2、3。

服务完成切换后发出状态变化事件。窗体订阅事件并调用相应 `VideoTileControl.SetCamera`，树控件和副屏按钮都只调用同一个服务，不复制业务规则。

## 主屏 UI

主窗体为深蓝黑背景，蓝色表示选中，绿色表示在线，橙色表示异常。常规布局由左侧导航、中间监控区、右侧监控树及必要的顶部区域组成。

中间区域使用两行两列、行列均为 50% 的 `TableLayoutPanel`：前三格展示当前溜井组，第四格展示当前巷道。导航中只有“实时监控”执行功能，其他项目仅保留视觉入口。

监控树按卸矿站、溜井、巷道展示任务指定组。只有可选择的组节点触发切换，分类节点不改变画面。

## 主屏全屏

“全屏”按钮隐藏左侧导航、右侧树、顶部非必要区域和底部区域，使主屏 2×2 区域填满窗体。按 Esc 恢复各区域的可见状态和原窗体边框/窗口状态。该功能只控制整个四画面区域，不实现单格全屏。

## 副屏 UI 与显示器规则

副屏窗体内容高度固定为 540 像素，内部视频区始终为一行三列，列宽均为 `33.333%`，三个格子不会换行。顶部仅保留“2#主溜井”和“3#主溜井”两个切换按钮。

当 `Screen.AllScreens.Length >= 2` 时，副屏窗体放在第二显示器 `WorkingArea` 左上角，宽度等于其可用宽度，高度为 540。只有一块显示器时，副屏作为普通可拖动窗口显示，不抛出错误。

## VideoTileControl

每个格子显示摄像头名称、在线状态、当前组名、通道号和“模拟视频画面”占位区。公开接口为：

- `SetCamera(CameraInfo camera)`
- `ShowOnline()`
- `ShowOffline()`
- `ShowError(string message)`

真实播放能力未来只在该控件内部替换，本阶段不预留或实现任何真实流媒体接口。

## 错误处理

业务服务拒绝类型不匹配或通道数不足的组，并抛出参数异常，以避免产生部分切换。UI 假数据在启动时一次创建；单屏环境按测试窗口路径处理，不视为错误。

## 测试与验证

xUnit 测试直接使用真实 `MonitorSwitchService` 和假数据，覆盖：

1. 切换溜井组时主屏前三槽同时变化且第四槽保持不变。
2. 切换巷道时仅主屏第四槽变化且前三槽保持不变。
3. 切换卸矿站组时副屏三槽同时变化且主屏保持不变。

实现完成后运行 `dotnet build` 和 `dotnet test`。最后启动应用进行冒烟验证，确认窗体能打开且默认布局无启动异常；不借此扩展真实视频或后台功能。

## Git 阶段

独立提交边界为：设计规格、解决方案与核心业务测试、UI 与交互、最终验证修正。每次提交只包含对应阶段内容。
