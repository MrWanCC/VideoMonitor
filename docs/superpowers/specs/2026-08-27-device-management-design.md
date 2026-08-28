# Device Management Design

## Goal

Add a device-management page to the existing WPF client without changing the established monitoring architecture or monitoring business rules. The page manages its own in-memory mock groups, physical camera devices, and default channels through MVVM commands.

The deliverable includes the device-management UI, ViewModels, Core domain models, mock initialization data, in-memory CRUD, navigation, tests, actual runtime screenshots, and local commits only.

## Scope Boundaries

This phase includes:

- selecting a second-level device group;
- creating, renaming, and deleting second-level groups;
- creating, editing, moving, and deleting physical camera devices;
- one default editable channel per device in the current UI;
- name/IP search within the selected group;
- a responsive right-side editor drawer;
- dark in-page confirmation and information overlays;
- ViewModel tests for filtering and CRUD behavior.

This phase does not include:

- SQL Server or SQLite;
- Repository, `IDeviceService`, or `InMemoryDeviceService` abstractions;
- Server API or network persistence;
- ZLMediaKit or real stream testing;
- LibVLCSharp or video rendering;
- HCNetSDK;
- real UDP, RTP, or RTSP communication;
- recording or alarm backend;
- multi-channel management UI;
- synchronization with the existing monitoring mock data.

`MonitorSwitchService`, the main-screen 3+1 rules, the secondary three-channel switching rules, and all existing monitoring behavior remain unchanged.

## Architecture

The existing projects and lightweight MVVM structure remain:

```text
VideoMonitor.Core
  Models
  Mock

VideoMonitor.Wpf
  Views/Pages
  ViewModels
  Controls/Behaviors
  Themes

VideoMonitor.Core.Tests
  ViewModels
  Services
```

Current data flow:

```text
MockDeviceData
    ↓ creates initial objects only
DeviceManagementViewModel
    ↓ exposes state and commands
DeviceView.xaml
```

Future replacement path, explicitly not implemented now:

```text
VideoMonitor.Server / DeviceService
    ├── DeviceManagementViewModel
    └── MonitorViewModel
```

`DeviceManagementViewModel` receives initialized group and device collections through its constructor. It does not call `MockDeviceData` internally and therefore does not depend on the mock factory's implementation details.

## Core Domain Models

### DeviceGroup

- `Guid Id`
- `string Name`
- `Guid? ParentId`
- `int Sort`
- `bool Enabled`

Groups with `ParentId == null` are fixed business categories:

- 卸矿站监控
- 溜井监控
- 巷道监控

Only their second-level child groups can be created, renamed, or deleted.

### CameraDevice

- `Guid Id`
- `string Name`
- `Guid GroupId`
- `string IpAddress`
- `int SdkPort`
- `int RtspPort`
- `string Username`
- `string Password`
- `string Manufacturer`
- `string Model`
- `TransportMode TransportMode`
- `CameraStatus Status`
- `bool Enabled`
- `string Remark`
- `List<CameraChannel> Channels`

One list row represents one physical `CameraDevice`. The current mock field situation is one physical camera per IP. The model still preserves `CameraDevice 1:N CameraChannel` for future NVR/DVR support.

### CameraChannel

- `Guid Id`
- `Guid DeviceId`
- `int ChannelNo`
- `string ChannelName`
- `StreamType StreamType`
- `string StreamId`
- `bool Enabled`

The current editor creates and edits the first/default channel only. It does not add a multi-channel management interface.

### Enums

```csharp
public enum TransportMode
{
    Auto,
    Tcp,
    Udp
}

public enum StreamType
{
    Main,
    Sub
}
```

UDP is stored as configuration only and does not start real communication.

## Mock Data

`MockDeviceData` creates and returns initial groups and devices. It contains no CRUD commands or business behavior.

It uses the same business group names as monitoring but returns independent objects. Editing device-management mock data does not alter `MockMonitorData`.

For `西401溜井`, the initial data contains three physical devices:

| Device | IP | SDK | RTSP | Default channel | Stream | Transport | Status |
|---|---|---:|---:|---:|---|---|---|
| 西401溜井 · 通道1 | 192.168.17.5 | 8000 | 554 | 1 | Main | Auto | Online |
| 西401溜井 · 通道2 | 192.168.17.6 | 8000 | 554 | 1 | Main | Tcp | Online |
| 西401溜井 · 通道3 | 192.168.17.7 | 8000 | 554 | 1 | Sub | Udp | Online |

Every mock device has its own IP and one default `CameraChannel` whose `ChannelNo` is `1`.

## Navigation and Page Lifetime

`MainViewModel` owns:

- `MonitorViewModel Monitor`
- `DeviceManagementViewModel DeviceManagement`
- `string SelectedNavigation`

`MainWindow.xaml` contains one existing `MonitorView` instance and one `DeviceView` instance. `SelectedNavigation` controls their visibility. Navigation does not recreate either View or ViewModel, so page selection, search text, draft state, and monitoring state are retained while the application remains open.

When `设备管理` is selected:

- the monitoring device tree is hidden;
- `DeviceView` spans the main content and former monitoring-tree columns;
- the global left navigation, top title bar, and bottom status bar remain;
- the existing monitoring view and its DataContext remain alive but hidden.

Other navigation buttons keep their current non-functional behavior in this phase.

## Device Management ViewModel

`DeviceManagementViewModel` owns:

- `ObservableCollection<DeviceGroup> Groups`
- hierarchical display sections derived from the flat groups;
- an internal collection containing all `CameraDevice` objects;
- `ObservableCollection<CameraDevice> Devices`, containing only current filtered rows;
- `DeviceGroup? SelectedGroup`
- `CameraDevice? SelectedDevice`
- `string SearchKeyword`
- `bool IsEditPanelOpen`
- `bool IsEditing`
- `DeviceEditDraftViewModel EditDraft`
- `Guid? EditingGroupId`
- `string EditingGroupName`
- confirmation/notice overlay state and message.

Required commands:

- `AddGroupCommand`
- `RenameGroupCommand`
- `DeleteGroupCommand`
- `BeginAddGroupCommand`
- `BeginRenameGroupCommand`
- `CommitGroupEditCommand`
- `CancelGroupEditCommand`
- `AddDeviceCommand`
- `EditDeviceCommand`
- `DeleteDeviceCommand`
- `SaveDeviceCommand`
- `CancelEditCommand`
- confirmation accept/cancel commands.

`AddGroupCommand` and `BeginAddGroupCommand` share the same begin-add behavior. `RenameGroupCommand` and `BeginRenameGroupCommand` share the same begin-rename behavior so both naming requirements are exposed without duplicating logic.

## Group Tree Interaction

The left panel is 250px wide at the 1920×1080 design baseline.

Each fixed first-level category shows:

- expand/collapse arrow;
- category name;
- a vector `+` button.

Clicking `+`:

1. creates a temporary second-level row under that category;
2. sets `EditingGroupId` to the temporary group's ID;
3. clears `EditingGroupName`;
4. displays a TextBox in place of the normal row;
5. focuses the TextBox and selects its text.

Editing behavior:

- Enter commits a valid non-empty, non-duplicate name;
- Esc cancels;
- losing focus commits a valid name and cancels an empty name;
- canceling a new group removes the temporary row;
- canceling a rename leaves the original model name unchanged;
- validation errors remain visible and keep the row in edit mode.

Second-level rows have a dark context menu with rename and delete actions. F2 support is not required in this phase.

XAML uses Binding and DataTriggers to switch between TextBlock and TextBox. Code-behind may only forward Enter/Esc/lost-focus events to ViewModel commands and perform focus/select-all UI behavior. It contains no group CRUD rules.

## Group Deletion

First-level categories cannot be renamed or deleted.

For a second-level group:

- if it contains devices, deletion stops immediately and opens an informational dark overlay with exactly: `该分组下仍有设备，请先移动或删除设备。`;
- it does not open a confirmation flow;
- it never performs cascade deletion;
- if it is empty, a dark confirmation overlay opens and deletion occurs only after explicit confirmation.

## Device List

The center column uses the remaining wide area and contains:

- current group title;
- `+ 新建设备` button;
- one search box for name or IP;
- a dark industrial list, not a default gray DataGrid.

Columns:

- 设备名称
- IP地址
- SDK端口
- RTSP端口
- 主通道
- 码流
- 传输方式
- 状态
- 操作

Rows use the existing color, typography, border, radius, selection-blue, and online-green resources. The selected row uses a low-saturation blue background.

Changing `SelectedGroup` immediately rebuilds `Devices` from the internal full collection. Changing `SearchKeyword` filters only the selected group, case-insensitively, against device name and IP address.

## Device Editor Drawer

At 1920×1080, the page uses:

```text
250px group tree + remaining device list + 380px editor drawer
```

The drawer is hidden by default and opens only for add/edit.

When `DeviceView.ActualWidth < 1360`, pure View code-behind changes the drawer to a 380px overlay aligned to the right side of the content area. At `1360` or wider it uses the normal third column. This layout-only behavior does not alter ViewModel state or CRUD logic.

The editor is a separate draft object so input never mutates the original device before Save.

### Basic Information

- required device name;
- required second-level group ComboBox;
- manufacturer;
- model;
- remark.

### Access Parameters

- required device IP;
- SDK port, default `8000`;
- RTSP port, default `554`;
- username;
- PasswordBox password.

Password input is connected through a small WPF binding behavior. The password is never displayed as plain text.

### Channel Configuration

- channel number, default `1`;
- channel name;
- stream type: Main/Sub displayed as 主码流/辅码流.

### Transport Configuration

- Auto/TCP/UDP;
- default Auto.

### RTSP Preview

The preview is read-only and always masks credentials:

```text
rtsp://***@192.168.17.5:554/Streaming/Channels/101
```

No actual password is interpolated into UI text.

The `测试拉流` button is disabled with Tooltip `接入ZLMediaKit后启用`.

### Save and Cancel

Save validates:

- required name;
- selected second-level group;
- valid IP address;
- SDK and RTSP ports in `1..65535`;
- channel number greater than zero.

Invalid input keeps the drawer open and shows a compact validation message.

Add creates one `CameraDevice` and one default `CameraChannel` only after validation passes.

Edit copies validated draft values back into the original device and its default channel. If the selected group changes:

- the device is removed from the current filtered list immediately after Save;
- it becomes visible when the target group is selected;
- no duplicate device object is created.

Cancel closes the drawer and discards the draft without changing the original object.

## Device Deletion

Clicking delete does not immediately change data. It opens the in-page dark confirmation overlay. Confirm removes the physical device and its owned in-memory channel objects. Cancel leaves all data unchanged.

This is not group cascade deletion; it is an explicit device deletion selected by the user.

## UI-Only Behaviors

`DeviceView.xaml.cs` is limited to:

- choosing wide-column versus overlay drawer layout based on actual width;
- focusing/selecting the inline group editor;
- forwarding TextBox Enter, Esc, and lost-focus events to ViewModel commands.

No CRUD, validation, filtering, or model mutation is implemented in code-behind.

## Error and Confirmation Presentation

The page uses an internal dark overlay instead of `MessageBox` or a new Window.

It supports:

- confirm/cancel mode for deleting a device or empty group;
- information-only mode for blocked non-empty group deletion;
- clear text and one primary action;
- no silent deletion.

## Testing

Add ViewModel tests covering at least:

1. selecting a group filters devices;
2. name search filters correctly;
3. IP search filters correctly;
4. adding a device adds it to the current group;
5. editing a device updates the original object;
6. canceling edit leaves the original unchanged;
7. deleting a device removes it after confirmation;
8. non-empty group deletion is blocked and does not open confirmation;
9. moving an edited device removes it from the current group and adds it to the target group;
10. canceling inline rename restores the original name.

Existing `MonitorSwitchService` and monitoring UI-state tests must continue to pass.

## Acceptance and Evidence

Run:

```powershell
dotnet build VideoMonitor.sln
dotnet test VideoMonitor.sln
```

Both commands must complete with zero errors and zero test failures.

Capture actual runtime screenshots for:

1. device-management main page;
2. add-device drawer;
3. edit-device drawer;
4. inline add/rename group state;
5. delete confirmation overlay.

Commit all work locally on `feature/wpf-video-monitor-ui`. Do not merge and do not push.
