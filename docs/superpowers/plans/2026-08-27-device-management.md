# Device Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a dark industrial device-management page with independent mock data, in-memory group/device CRUD, responsive editor drawer, navigation, tests, and runtime screenshots while preserving all monitoring behavior.

**Architecture:** Add mutable device domain models and a mock initialization factory in Core. `DeviceManagementViewModel` receives those objects through its constructor and owns all in-memory CRUD, filtering, inline edit, drawer, and confirmation state; XAML displays that state while code-behind is limited to focus, key forwarding, and responsive drawer placement. `MainViewModel` keeps both monitor and device-management ViewModels alive and navigation changes only View visibility.

**Tech Stack:** C# 12, .NET 8, WPF, XAML, CommunityToolkit.Mvvm 8.4.2, xUnit

**Spec:** `docs/superpowers/specs/2026-08-27-device-management-design.md`

## Global Constraints

- Work only on `feature/wpf-video-monitor-ui`; create local commits, do not merge and do not push.
- Do not change `MonitorSwitchService`, main-screen 3+1 rules, chute/tunnel switching, secondary three-channel switching, or existing monitoring behavior.
- Do not add SQL Server, SQLite, Repository, `IDeviceService`, `InMemoryDeviceService`, Server API, ZLMediaKit, LibVLCSharp, HCNetSDK, real UDP/RTP/RTSP communication, recording, or alarm backend.
- Device management uses independent objects created by `MockDeviceData`; it does not update `MockMonitorData`.
- One list row is one physical `CameraDevice`; current UI edits one default `CameraChannel`, but the model keeps a `1:N` channel collection.
- Fixed root categories cannot be added, renamed, or deleted; only second-level business groups are managed.
- All CRUD, validation, filtering, and confirmation decisions live in ViewModels, never in `DeviceView.xaml.cs`.
- Reuse the existing Theme/ResourceDictionary, font, vector-icon, spacing, color, border, radius, selection-blue, and online-green resources.
- Preserve existing MonitorView and DeviceView instances during navigation.

---

### Task 1: Device Domain Models and Mock Initialization Data

**Files:**
- Create: `src/VideoMonitor.Core/Models/DeviceGroup.cs`
- Create: `src/VideoMonitor.Core/Models/CameraDevice.cs`
- Create: `src/VideoMonitor.Core/Models/CameraChannel.cs`
- Create: `src/VideoMonitor.Core/Models/TransportMode.cs`
- Create: `src/VideoMonitor.Core/Models/StreamType.cs`
- Create: `src/VideoMonitor.Core/Mock/MockDeviceDataSet.cs`
- Create: `src/VideoMonitor.Core/Mock/MockDeviceData.cs`
- Create: `tests/VideoMonitor.Core.Tests/Mock/MockDeviceDataTests.cs`

**Interfaces:**
- Produces: `MockDeviceData.Create(): MockDeviceDataSet`
- Produces: `MockDeviceDataSet.Groups: IReadOnlyList<DeviceGroup>`
- Produces: `MockDeviceDataSet.Devices: IReadOnlyList<CameraDevice>`
- Produces: mutable Core models consumed by `DeviceManagementViewModel` in Tasks 2–3.

- [ ] **Step 1: Write failing mock-data tests**

Create `MockDeviceDataTests.cs`:

```csharp
using VideoMonitor.Core.Mock;

namespace VideoMonitor.Core.Tests.Mock;

public sealed class MockDeviceDataTests
{
    [Fact]
    public void Create_ReturnsFixedCategoriesAndBusinessGroups()
    {
        var data = MockDeviceData.Create();
        var roots = data.Groups.Where(group => group.ParentId is null).ToArray();

        Assert.Equal(new[] { "卸矿站监控", "溜井监控", "巷道监控" },
            roots.OrderBy(group => group.Sort).Select(group => group.Name));
        Assert.Contains(data.Groups, group => group.Name == "西401溜井" && group.ParentId is not null);
        Assert.Contains(data.Groups, group => group.Name == "2#主溜井" && group.ParentId is not null);
    }

    [Fact]
    public void Create_West401ContainsThreePhysicalDevicesWithOneDefaultChannelEach()
    {
        var data = MockDeviceData.Create();
        var group = data.Groups.Single(item => item.Name == "西401溜井");
        var devices = data.Devices.Where(device => device.GroupId == group.Id).ToArray();

        Assert.Equal(3, devices.Length);
        Assert.Equal(new[] { "192.168.17.5", "192.168.17.6", "192.168.17.7" },
            devices.Select(device => device.IpAddress));
        Assert.All(devices, device =>
        {
            var channel = Assert.Single(device.Channels);
            Assert.Equal(1, channel.ChannelNo);
            Assert.Equal(device.Id, channel.DeviceId);
        });
    }
}
```

- [ ] **Step 2: Run the tests and verify missing-type failure**

Run:

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~MockDeviceDataTests
```

Expected: compilation fails because `MockDeviceData` and the device models do not exist.

- [ ] **Step 3: Implement the Core models**

Use mutable classes because the current ViewModel commits edits into the in-memory objects:

```csharp
namespace VideoMonitor.Core.Models;

public sealed class DeviceGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentId { get; set; }
    public int Sort { get; set; }
    public bool Enabled { get; set; } = true;
}
```

```csharp
namespace VideoMonitor.Core.Models;

public sealed class CameraDevice
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid GroupId { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public int SdkPort { get; set; } = 8000;
    public int RtspPort { get; set; } = 554;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public TransportMode TransportMode { get; set; } = TransportMode.Auto;
    public CameraStatus Status { get; set; } = CameraStatus.Online;
    public bool Enabled { get; set; } = true;
    public string Remark { get; set; } = string.Empty;
    public List<CameraChannel> Channels { get; } = [];
}
```

```csharp
namespace VideoMonitor.Core.Models;

public sealed class CameraChannel
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public int ChannelNo { get; set; } = 1;
    public string ChannelName { get; set; } = string.Empty;
    public StreamType StreamType { get; set; } = StreamType.Main;
    public string StreamId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}
```

Create `TransportMode` and `StreamType` exactly as defined in the spec.

- [ ] **Step 4: Implement `MockDeviceData` as initialization only**

Define:

```csharp
public sealed record MockDeviceDataSet(
    IReadOnlyList<DeviceGroup> Groups,
    IReadOnlyList<CameraDevice> Devices);
```

`MockDeviceData.Create()` must create three deterministic root categories, all required child groups, and three physical 西401 devices. Use private creation helpers only; do not add add/edit/delete methods.

Each 西401 device uses:

```csharp
new CameraDevice
{
    Id = deviceId,
    Name = $"西401溜井 · 通道{index}",
    GroupId = west401.Id,
    IpAddress = $"192.168.17.{4 + index}",
    SdkPort = 8000,
    RtspPort = 554,
    Username = "admin",
    Password = "mock-password",
    Manufacturer = "海康威视",
    Model = "IPC",
    TransportMode = index switch { 2 => TransportMode.Tcp, 3 => TransportMode.Udp, _ => TransportMode.Auto },
    Status = CameraStatus.Online,
    Enabled = true
};
```

Add one `CameraChannel` with `ChannelNo = 1`, channel name `通道1`, stream Main/Main/Sub for indices 1/2/3, and a non-secret StreamId.

- [ ] **Step 5: Run focused and full tests**

Run:

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~MockDeviceDataTests
dotnet test VideoMonitor.sln
```

Expected: 2 focused tests pass and all existing tests remain green.

- [ ] **Step 6: Commit domain and mock data**

```powershell
git add src/VideoMonitor.Core/Models src/VideoMonitor.Core/Mock/MockDeviceData.cs src/VideoMonitor.Core/Mock/MockDeviceDataSet.cs tests/VideoMonitor.Core.Tests/Mock/MockDeviceDataTests.cs
git commit -m "feat: add device management domain models"
```

---

### Task 2: Group Selection, Search, and Inline Group CRUD

**Files:**
- Create: `src/VideoMonitor.Wpf/ViewModels/DeviceGroupTreeItemViewModel.cs`
- Create: `src/VideoMonitor.Wpf/ViewModels/DeviceDialogMode.cs`
- Create: `src/VideoMonitor.Wpf/ViewModels/DeviceManagementViewModel.cs`
- Create: `tests/VideoMonitor.Core.Tests/ViewModels/DeviceManagementGroupTests.cs`

**Interfaces:**
- Consumes: `DeviceManagementViewModel(IEnumerable<DeviceGroup>, IEnumerable<CameraDevice>)`
- Produces: `Groups`, `GroupSections`, `SelectedGroup`, `Devices`, `SearchKeyword`
- Produces: inline edit properties and group commands named in the spec.
- Produces: `IsDialogOpen`, `DialogMode`, `DialogMessage`, `ConfirmDialogCommand`, `CancelDialogCommand` reused by Task 3 and XAML.

- [ ] **Step 1: Write failing group/filter tests**

Create tests for the required behavior:

```csharp
[Fact]
public void SelectGroup_FiltersDevicesToSelectedGroup()
{
    var viewModel = CreateViewModel();
    var group = viewModel.Groups.Single(item => item.Name == "西401溜井");

    viewModel.SelectGroupCommand.Execute(group);

    Assert.Equal(3, viewModel.Devices.Count);
    Assert.All(viewModel.Devices, device => Assert.Equal(group.Id, device.GroupId));
}

[Theory]
[InlineData("西401", 3)]
[InlineData("192.168.17", 3)]
[InlineData("192.168.17.6", 1)]
public void SearchKeyword_FiltersCurrentGroupByNameOrIp(string keyword, int expected)
{
    var viewModel = CreateViewModel("西401溜井");

    viewModel.SearchKeyword = keyword;

    Assert.Equal(expected, viewModel.Devices.Count);
}

[Fact]
public void DeleteNonEmptyGroup_IsBlockedWithoutConfirmation()
{
    var viewModel = CreateViewModel("西401溜井");
    var group = viewModel.SelectedGroup!;

    viewModel.DeleteGroupCommand.Execute(group);

    Assert.True(viewModel.IsDialogOpen);
    Assert.Equal(DeviceDialogMode.Information, viewModel.DialogMode);
    Assert.Equal("该分组下仍有设备，请先移动或删除设备。", viewModel.DialogMessage);
    Assert.Contains(group, viewModel.Groups);
}

[Fact]
public void CancelRename_DoesNotChangeOriginalName()
{
    var viewModel = CreateViewModel("西402溜井");
    var group = viewModel.SelectedGroup!;

    viewModel.BeginRenameGroupCommand.Execute(group);
    viewModel.EditingGroupName = "已修改但未保存";
    viewModel.CancelGroupEditCommand.Execute(null);

    Assert.Equal("西402溜井", group.Name);
}
```

Add an empty-group deletion test that verifies `Confirmation` mode appears first and deletion happens only after `ConfirmDialogCommand`.

- [ ] **Step 2: Run the focused tests and verify failure**

Run:

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~DeviceManagementGroupTests
```

Expected: compilation fails because the new ViewModels do not exist.

- [ ] **Step 3: Implement tree item and dialog state types**

`DeviceGroupTreeItemViewModel` wraps a Core group and owns display-only state:

```csharp
public sealed class DeviceGroupTreeItemViewModel : ObservableObject
{
    public DeviceGroupTreeItemViewModel(DeviceGroup group, IEnumerable<DeviceGroupTreeItemViewModel>? children = null);
    public DeviceGroup Group { get; }
    public ObservableCollection<DeviceGroupTreeItemViewModel> Children { get; }
    public bool IsExpanded { get; set; }
    public bool IsSelected { get; set; }
    public bool IsEditing { get; set; }
    public bool IsRoot => Group.ParentId is null;
}
```

Define `DeviceDialogMode.None`, `Information`, and `Confirmation`.

- [ ] **Step 4: Implement selection and filtering in `DeviceManagementViewModel`**

Constructor:

```csharp
public DeviceManagementViewModel(
    IEnumerable<DeviceGroup> groups,
    IEnumerable<CameraDevice> devices)
```

Copy input objects into internal observable collections, build root/child sections ordered by `Sort`, select `备用1` when present (otherwise first child), and call `RefreshDevices()`.

Filtering must be:

```csharp
private void RefreshDevices()
{
    Devices.Clear();
    if (SelectedGroup is null) return;

    var keyword = SearchKeyword.Trim();
    foreach (var device in allDevices.Where(device =>
                 device.GroupId == SelectedGroup.Id
                 && (keyword.Length == 0
                     || device.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                     || device.IpAddress.Contains(keyword, StringComparison.OrdinalIgnoreCase))))
    {
        Devices.Add(device);
    }
}
```

Call it from the `SelectedGroup` and `SearchKeyword` setters.

- [ ] **Step 5: Implement inline add/rename commands**

`BeginAddGroup(DeviceGroup root)` creates a temporary child object with a new ID and empty name, adds it to `Groups`, records its ID as the pending new group, sets `EditingGroupId`, and rebuilds tree state.

`BeginRenameGroup(DeviceGroup child)` rejects roots, stores only `EditingGroupId` and the existing name in `EditingGroupName`; it does not mutate `child.Name`.

`CommitGroupEdit()`:

- trims the input;
- cancels an empty new group;
- keeps edit mode with `GroupEditError = "分组名称不能为空。"` for an empty rename;
- rejects a duplicate sibling with `GroupEditError = "同一分类下已存在同名分组。"`;
- applies the valid name;
- clears pending/edit state and rebuilds the tree.

`CancelGroupEdit()` removes only a pending new group, leaves existing names unchanged, and clears edit state.

Expose both command names by pointing aliases at the same command instances:

```csharp
public IRelayCommand<DeviceGroup> AddGroupCommand => BeginAddGroupCommand;
public IRelayCommand<DeviceGroup> RenameGroupCommand => BeginRenameGroupCommand;
```

- [ ] **Step 6: Implement deletion and dialog decisions**

For non-empty groups, set information mode and the exact required message without assigning a pending confirmation action.

For empty child groups, set confirmation mode/message and store a private action that removes the group, repairs selection if needed, rebuilds sections, and refreshes devices. Root groups return without changes.

`ConfirmDialogCommand` invokes the stored action only in Confirmation mode; `CancelDialogCommand` clears the dialog without mutation.

- [ ] **Step 7: Run focused and full tests**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~DeviceManagementGroupTests
dotnet test VideoMonitor.sln
```

Expected: all group/filter tests and all prior tests pass.

- [ ] **Step 8: Commit group management**

```powershell
git add src/VideoMonitor.Wpf/ViewModels/DeviceGroupTreeItemViewModel.cs src/VideoMonitor.Wpf/ViewModels/DeviceDialogMode.cs src/VideoMonitor.Wpf/ViewModels/DeviceManagementViewModel.cs tests/VideoMonitor.Core.Tests/ViewModels/DeviceManagementGroupTests.cs
git commit -m "feat: add device group management"
```

---

### Task 3: Device Draft and In-Memory Device CRUD

**Files:**
- Create: `src/VideoMonitor.Wpf/ViewModels/DeviceEditDraftViewModel.cs`
- Modify: `src/VideoMonitor.Wpf/ViewModels/DeviceManagementViewModel.cs`
- Create: `tests/VideoMonitor.Core.Tests/ViewModels/DeviceManagementCrudTests.cs`

**Interfaces:**
- Produces: `DeviceEditDraftViewModel EditDraft`
- Produces: `AddDeviceCommand`, `EditDeviceCommand`, `DeleteDeviceCommand`, `SaveDeviceCommand`, `CancelEditCommand`
- Produces: `IsEditPanelOpen`, `IsEditing`, `SelectedDevice`, `ValidationMessage`, child-group choices.

- [ ] **Step 1: Write failing device CRUD tests**

Cover the required flow with tests equivalent to:

```csharp
[Fact]
public void AddDevice_AddsDeviceAndDefaultChannelToCurrentGroup()
{
    var viewModel = CreateViewModel("西401溜井");
    var before = viewModel.Devices.Count;
    viewModel.AddDeviceCommand.Execute(null);
    FillValidDraft(viewModel.EditDraft, "新增IPC", "192.168.17.20");

    viewModel.SaveDeviceCommand.Execute(null);

    Assert.Equal(before + 1, viewModel.Devices.Count);
    var added = viewModel.Devices.Single(device => device.Name == "新增IPC");
    Assert.Equal(viewModel.SelectedGroup!.Id, added.GroupId);
    Assert.Equal(1, Assert.Single(added.Channels).ChannelNo);
}

[Fact]
public void EditDevice_SaveUpdatesOriginalObject()
{
    var viewModel = CreateViewModel("西401溜井");
    var original = viewModel.Devices[0];
    viewModel.EditDeviceCommand.Execute(original);
    viewModel.EditDraft.Name = "已编辑设备";

    viewModel.SaveDeviceCommand.Execute(null);

    Assert.Equal("已编辑设备", original.Name);
}

[Fact]
public void CancelEdit_DoesNotMutateOriginalObject()
{
    var viewModel = CreateViewModel("西401溜井");
    var original = viewModel.Devices[0];
    var originalName = original.Name;
    viewModel.EditDeviceCommand.Execute(original);
    viewModel.EditDraft.Name = "未保存名称";

    viewModel.CancelEditCommand.Execute(null);

    Assert.Equal(originalName, original.Name);
}

[Fact]
public void DeleteDevice_RemovesOnlyAfterConfirmation()
{
    var viewModel = CreateViewModel("西401溜井");
    var device = viewModel.Devices[0];
    viewModel.DeleteDeviceCommand.Execute(device);
    Assert.Contains(device, viewModel.Devices);

    viewModel.ConfirmDialogCommand.Execute(null);

    Assert.DoesNotContain(device, viewModel.Devices);
}

[Fact]
public void EditDevice_MoveGroup_RemovesItFromCurrentAndAddsItToTarget()
{
    var viewModel = CreateViewModel("西401溜井");
    var device = viewModel.Devices[0];
    var target = viewModel.Groups.Single(group => group.Name == "西402溜井");
    viewModel.EditDeviceCommand.Execute(device);
    viewModel.EditDraft.GroupId = target.Id;

    viewModel.SaveDeviceCommand.Execute(null);

    Assert.DoesNotContain(device, viewModel.Devices);
    viewModel.SelectGroupCommand.Execute(target);
    Assert.Contains(device, viewModel.Devices);
}
```

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~DeviceManagementCrudTests
```

Expected: compilation fails because editor/CRUD members do not exist.

- [ ] **Step 3: Implement `DeviceEditDraftViewModel`**

Use observable string fields for numeric inputs so invalid text can be validated without binding exceptions:

```csharp
public sealed partial class DeviceEditDraftViewModel : ObservableObject
{
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private Guid? groupId;
    [ObservableProperty] private string ipAddress = string.Empty;
    [ObservableProperty] private string sdkPort = "8000";
    [ObservableProperty] private string rtspPort = "554";
    [ObservableProperty] private string username = string.Empty;
    [ObservableProperty] private string password = string.Empty;
    [ObservableProperty] private string manufacturer = string.Empty;
    [ObservableProperty] private string model = string.Empty;
    [ObservableProperty] private string remark = string.Empty;
    [ObservableProperty] private string channelNo = "1";
    [ObservableProperty] private string channelName = "通道1";
    [ObservableProperty] private StreamType streamType = StreamType.Main;
    [ObservableProperty] private TransportMode transportMode = TransportMode.Auto;

    public string RtspPreview =>
        $"rtsp://***@{(string.IsNullOrWhiteSpace(IpAddress) ? "--" : IpAddress)}:{RtspPort}/Streaming/Channels/{ChannelNo}01";
}
```

Notify `RtspPreview` when IP, RTSP port, or channel number changes. Add methods `ResetForAdd(Guid groupId)` and `Load(CameraDevice device)` that copy values without retaining a model reference.

- [ ] **Step 4: Implement add/edit drawer state and validation**

`AddDeviceCommand` requires a selected second-level group, resets the draft with that group, clears selection/validation, sets `IsEditing = false`, and opens the drawer.

`EditDeviceCommand` copies the selected device/default channel into the draft, sets `SelectedDevice`, `IsEditing = true`, and opens the drawer.

Validation must use `IPAddress.TryParse`, `int.TryParse`, and exact ranges from the spec. Return the first actionable Chinese message and keep the drawer open.

- [ ] **Step 5: Implement save, cancel, move, and delete**

On valid add, create one new `CameraDevice` and one new `CameraChannel`, add the device to the internal all-device collection, close drawer, and refresh current rows.

On valid edit, copy fields into `SelectedDevice` and its first channel. Create a first channel only if a malformed input object has none. Changing GroupId must reuse the original object and refresh the current list so it disappears when moved.

Cancel clears drawer/draft state without touching the selected model.

Delete opens the shared confirmation overlay with a private action that removes the selected device from the all-device collection and refreshes rows. It must not affect any group.

- [ ] **Step 6: Run focused and full tests**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~DeviceManagementCrudTests
dotnet test VideoMonitor.sln
```

Expected: all CRUD tests and all prior tests pass.

- [ ] **Step 7: Commit device CRUD**

```powershell
git add src/VideoMonitor.Wpf/ViewModels/DeviceEditDraftViewModel.cs src/VideoMonitor.Wpf/ViewModels/DeviceManagementViewModel.cs tests/VideoMonitor.Core.Tests/ViewModels/DeviceManagementCrudTests.cs
git commit -m "feat: add in-memory device CRUD"
```

---

### Task 4: Persistent Page Navigation

**Files:**
- Modify: `src/VideoMonitor.Wpf/ViewModels/MainViewModel.cs`
- Modify: `src/VideoMonitor.Wpf/App.xaml.cs`
- Modify: `src/VideoMonitor.Wpf/MainWindow.xaml`
- Create: `src/VideoMonitor.Wpf/Converters/NavigationVisibilityConverter.cs`
- Modify: `tests/VideoMonitor.Core.Tests/ViewModels/MonitorUiStateTests.cs`
- Create: `tests/VideoMonitor.Core.Tests/ViewModels/MainNavigationTests.cs`

**Interfaces:**
- Consumes: `MainViewModel(MonitorViewModel, DeviceManagementViewModel)`
- Produces: `MainViewModel.DeviceManagement`
- Produces: one persistent `DeviceView` in `MainWindow.xaml`.

- [ ] **Step 1: Write failing navigation lifetime test**

```csharp
[Fact]
public void Navigate_PreservesDeviceManagementInstanceAndState()
{
    var monitor = CreateMonitorViewModel();
    var deviceManagement = CreateDeviceManagementViewModel();
    var main = new MainViewModel(monitor, deviceManagement);
    deviceManagement.SearchKeyword = "192.168";

    main.NavigateCommand.Execute("设备管理");
    main.NavigateCommand.Execute("实时监控");
    main.NavigateCommand.Execute("设备管理");

    Assert.Same(deviceManagement, main.DeviceManagement);
    Assert.Equal("192.168", main.DeviceManagement.SearchKeyword);
}
```

- [ ] **Step 2: Run focused test and verify constructor failure**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~MainNavigationTests
```

Expected: compilation fails because `MainViewModel` does not accept/expose device management.

- [ ] **Step 3: Extend `MainViewModel` and update composition root**

Change constructor to:

```csharp
public MainViewModel(MonitorViewModel monitor, DeviceManagementViewModel deviceManagement)
{
    Monitor = monitor;
    DeviceManagement = deviceManagement;
    // existing commands unchanged
}

public DeviceManagementViewModel DeviceManagement { get; }
```

Update the existing `MonitorUiStateTests` helper call to supply an independently created empty/device mock ViewModel. Do not alter its assertions.

In `App.OnStartup`, call `MockDeviceData.Create()`, construct one `DeviceManagementViewModel`, and pass it to `MainViewModel`. Do not share those objects with `MockMonitorData`.

- [ ] **Step 4: Implement visibility conversion and persistent view instances**

Create `NavigationVisibilityConverter`:

```csharp
public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
    string.Equals(value as string, parameter as string, StringComparison.Ordinal)
        ? Visibility.Visible
        : Visibility.Collapsed;
```

In `MainWindow.xaml` resources register it. Keep the existing `MonitorView` and add one `DeviceView`:

```xml
<pages:MonitorView x:Name="MonitorContent"
                   Grid.Column="1" Margin="8,0"
                   DataContext="{Binding Monitor}"
                   Visibility="{Binding SelectedNavigation, Converter={StaticResource NavigationVisibilityConverter}, ConverterParameter=实时监控}" />
<controls:MonitorTree x:Name="TreeChrome"
                      Grid.Column="2"
                      Visibility="{Binding SelectedNavigation, Converter={StaticResource NavigationVisibilityConverter}, ConverterParameter=实时监控}"
                      DataContext="{Binding Monitor}" />
<pages:DeviceView x:Name="DeviceContent"
                  Grid.Column="1" Grid.ColumnSpan="2" Margin="8,0,8,0"
                  DataContext="{Binding DeviceManagement}"
                  Visibility="{Binding SelectedNavigation, Converter={StaticResource NavigationVisibilityConverter}, ConverterParameter=设备管理}" />
```

Do not place either page in a DataTemplate or create them in a navigation click handler.

- [ ] **Step 5: Make fullscreen code tolerate hidden device tree/page state**

Keep the existing full-screen logic unchanged for real-time monitoring. Ensure `ExitMonitorFullscreen()` restores column widths and lets XAML bindings decide MonitorTree/DeviceView visibility; do not force `TreeChrome.Visibility = Visible` when `SelectedNavigation != 实时监控`.

- [ ] **Step 6: Run focused and full tests/build**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~MainNavigationTests
dotnet build VideoMonitor.sln
dotnet test VideoMonitor.sln
```

Expected: navigation test passes, build has zero errors, all tests pass.

- [ ] **Step 7: Commit navigation**

```powershell
git add src/VideoMonitor.Wpf/ViewModels/MainViewModel.cs src/VideoMonitor.Wpf/App.xaml.cs src/VideoMonitor.Wpf/MainWindow.xaml src/VideoMonitor.Wpf/Converters/NavigationVisibilityConverter.cs tests/VideoMonitor.Core.Tests/ViewModels/MonitorUiStateTests.cs tests/VideoMonitor.Core.Tests/ViewModels/MainNavigationTests.cs
git commit -m "feat: add persistent device page navigation"
```

---

### Task 5: Device Management XAML, Drawer, and UI Behaviors

**Files:**
- Replace: `src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml`
- Modify: `src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml.cs`
- Create: `src/VideoMonitor.Wpf/Behaviors/PasswordBoxBinding.cs`
- Create: `src/VideoMonitor.Wpf/Converters/DeviceDisplayConverters.cs`
- Modify: `src/VideoMonitor.Wpf/Themes/Controls.xaml`
- Modify: `src/VideoMonitor.Wpf/Themes/Icons.xaml`

**Interfaces:**
- Consumes all `DeviceManagementViewModel` properties/commands from Tasks 2–3.
- Produces responsive 250 + star + 380 layout and five screenshotable states.

- [ ] **Step 1: Add reusable input/list styles and missing vector icons**

Add keyed styles to `Controls.xaml`:

- `IndustrialTextBoxStyle`: 36px height, card background, thin default border, 6px horizontal padding, Primary focus border.
- `IndustrialComboBoxStyle`: same height/background/border/font density.
- `DeviceListHeaderTextStyle`: muted 12px semi-bold.
- `DeviceActionButtonStyle`: transparent by default, mild blue hover, 30px height.

Add Geometry resources `IconAdd`, `IconEdit`, and `IconDelete` to `Icons.xaml`; do not use Unicode glyphs.

- [ ] **Step 2: Add display converters and PasswordBox binding**

`DeviceDisplayConverters.cs` must map:

```csharp
StreamType.Main => "主码流"
StreamType.Sub => "辅码流"
TransportMode.Auto => "Auto"
TransportMode.Tcp => "TCP"
TransportMode.Udp => "UDP"
```

Also expose the first channel number/stream from a `CameraDevice`, returning `--` when absent.

Implement attached property:

```csharp
public static readonly DependencyProperty BoundPasswordProperty =
    DependencyProperty.RegisterAttached(
        "BoundPassword", typeof(string), typeof(PasswordBoxBinding),
        new FrameworkPropertyMetadata(string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnBoundPasswordChanged));
```

Subscribe once to `PasswordChanged`, guard recursive updates, and write only to the bound string. Never expose the password in another TextBlock.

- [ ] **Step 3: Build the three-region page shell**

Build the page shell with:

```xml
<Grid x:Name="DeviceLayout" SizeChanged="OnDeviceLayoutSizeChanged">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="250" />
        <ColumnDefinition Width="*" />
        <ColumnDefinition x:Name="DrawerColumn" Width="0" />
    </Grid.ColumnDefinitions>
    <Border Grid.Column="0" Style="{StaticResource PanelBorderStyle}"><!-- group tree --></Border>
    <Border Grid.Column="1" Margin="8,0,0,0" Style="{StaticResource PanelBorderStyle}"><!-- list --></Border>
    <Border x:Name="EditorDrawer" Grid.Column="2" Width="380" HorizontalAlignment="Right"
            Visibility="{Binding IsEditPanelOpen, Converter={StaticResource BooleanToVisibilityConverter}}"><!-- editor --></Border>
    <Grid Grid.ColumnSpan="3" Panel.ZIndex="50"><!-- confirmation overlay --></Grid>
</Grid>
```

The group panel uses root ItemsControl sections, each with a vector add button bound to `BeginAddGroupCommand`. Child rows use a low-saturation selected background and a dark ContextMenu bound to `BeginRenameGroupCommand`/`DeleteGroupCommand`.

- [ ] **Step 4: Implement inline edit visuals and UI-only forwarding**

In the child DataTemplate, use an `IsEditing` DataTrigger:

```xml
<TextBlock Text="{Binding Group.Name}">
    <TextBlock.Style>
        <Style TargetType="TextBlock">
            <Setter Property="Visibility" Value="Visible" />
            <Style.Triggers><DataTrigger Binding="{Binding IsEditing}" Value="True"><Setter Property="Visibility" Value="Collapsed" /></DataTrigger></Style.Triggers>
        </Style>
    </TextBlock.Style>
</TextBlock>
<TextBox Text="{Binding DataContext.EditingGroupName, RelativeSource={RelativeSource AncestorType=UserControl}, UpdateSourceTrigger=PropertyChanged}"
         Loaded="OnGroupEditorLoaded" KeyDown="OnGroupEditorKeyDown" LostFocus="OnGroupEditorLostFocus">
    <!-- inverse Visibility trigger for IsEditing -->
</TextBox>
```

Code-behind handlers may only focus/select and execute `CommitGroupEditCommand` or `CancelGroupEditCommand`. They must not inspect names, add/remove groups, or decide validation.

- [ ] **Step 5: Build the industrial device list**

Use an explicit Grid header and ItemsControl rows rather than default DataGrid chrome. Both header and row use identical column definitions for name, IP, SDK, RTSP, channel, stream, transport, status, and operations.

The toolbar binds current group, `AddDeviceCommand`, and `SearchKeyword`. Row Edit/Delete buttons bind commands through the UserControl DataContext. Status uses existing status brush/text converters.

- [ ] **Step 6: Build the complete editor drawer**

The ScrollViewer contains labeled sections 基础信息, 接入参数, 通道配置, 传输配置, RTSP信息. Bind every field to `EditDraft`, group ComboBox to enabled second-level `Groups`, and enum ComboBoxes to ViewModel-provided enum values.

Bind PasswordBox only through:

```xml
<PasswordBox behaviors:PasswordBoxBinding.BoundPassword="{Binding EditDraft.Password, Mode=TwoWay}" />
```

Bind the masked preview to `EditDraft.RtspPreview`. Disable 测试拉流 and set Tooltip exactly `接入ZLMediaKit后启用`. Bind Cancel/Save commands and display `ValidationMessage` without increasing the fixed drawer width.

- [ ] **Step 7: Implement dark in-page dialog overlay**

Bind visibility to `IsDialogOpen`. Use a translucent window background, centered 420px card, `DialogMessage`, and mode triggers:

- Information: one `知道了` button bound to `CancelDialogCommand`.
- Confirmation: `取消` and `确认删除`, bound to cancel/confirm commands.

Do not call `MessageBox.Show` and do not create another Window.

- [ ] **Step 8: Implement responsive drawer placement in code-behind**

At width `>= 1360` and open: `DrawerColumn.Width = 380`, `Grid.Column(EditorDrawer) = 2`, normal Z-index.

At width `< 1360` and open: `DrawerColumn.Width = 0`, `Grid.Column(EditorDrawer) = 1`, align right over list, Z-index 20.

When closed, set drawer column to zero. The method reads only `ActualWidth` and `IsEditPanelOpen`; it does not mutate CRUD state.

- [ ] **Step 9: Run build/tests and static UI contract checks**

```powershell
$device = Get-Content src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml -Raw
if ($device -match 'MessageBox|IP1|IP2|IP3|IP4') { throw 'Forbidden device UI pattern found' }
if ($device -notmatch 'PasswordBox' -or $device -notmatch '测试拉流') { throw 'Required editor controls missing' }
dotnet build VideoMonitor.sln
dotnet test VideoMonitor.sln
```

Expected: contract passes, build has zero errors, all tests pass.

- [ ] **Step 10: Commit the complete UI stage**

```powershell
git add src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml.cs src/VideoMonitor.Wpf/Behaviors/PasswordBoxBinding.cs src/VideoMonitor.Wpf/Converters/DeviceDisplayConverters.cs src/VideoMonitor.Wpf/Themes/Controls.xaml src/VideoMonitor.Wpf/Themes/Icons.xaml
git commit -m "feat: build device management interface"
```

---

### Task 6: Runtime CRUD Verification, Screenshots, and Final Evidence

**Files:**
- Create: `artifacts/screenshots/device-management-main.png`
- Create: `artifacts/screenshots/device-management-add-drawer.png`
- Create: `artifacts/screenshots/device-management-edit-drawer.png`
- Create: `artifacts/screenshots/device-management-inline-group.png`
- Create: `artifacts/screenshots/device-management-delete-confirmation.png`

**Interfaces:**
- Consumes the complete running application.
- Produces acceptance evidence only; no new feature scope.

- [ ] **Step 1: Run fresh Release verification**

```powershell
dotnet build VideoMonitor.sln --configuration Release
dotnet test VideoMonitor.sln --configuration Release --no-build
```

Expected: zero errors, zero warnings where applicable, and all tests pass.

- [ ] **Step 2: Launch the actual WPF application and verify page lifetime**

At the primary display working area:

1. open 设备管理;
2. select 西401溜井 and verify three physical device rows;
3. enter a search, navigate to 实时监控, return, and verify search/selection remain;
4. verify monitoring still switches chute/tunnel correctly;
5. verify the unloading-station window remains operational.

- [ ] **Step 3: Verify group interactions**

Using the real UI:

- click a root `+`, type a unique group name, and commit with Enter;
- start another empty add and verify Esc cancels it;
- rename the new child inline and cancel once to verify original restoration;
- delete the empty child and confirm only through the overlay;
- attempt to delete 西401溜井 and verify the exact blocked message appears without a confirmation action.

- [ ] **Step 4: Verify device CRUD interactions**

Using the real UI:

- add a valid device to the selected group and verify one default channel;
- edit it and save a changed IP/name;
- edit and cancel, verifying no mutation;
- move it to another group and verify it leaves the current list and appears in the target;
- search by name and IP;
- delete a device only after confirmation;
- verify 测试拉流 stays disabled and credentials are masked in RTSP preview.

- [ ] **Step 5: Capture five actual runtime screenshots**

Save:

1. default device-management main page;
2. open add-device drawer;
3. open edit-device drawer;
4. group inline add/rename state;
5. delete confirmation overlay.

Use real process window handles and ensure no unrelated application overlaps the captured window.

- [ ] **Step 6: Inspect all screenshots**

Confirm 1920 baseline proportions, 250px group tree, readable center list, 380px drawer, dark confirmation overlay, consistent existing tokens, no plaintext password, no clipped rows, and no default gray WinForms/DataGrid appearance. Fix only evidence-backed visual defects, then rerun build/test and recapture affected states.

- [ ] **Step 7: Commit screenshots and final corrections**

```powershell
git add artifacts/screenshots/device-management-*.png
git commit -m "test: capture device management states"
```

- [ ] **Step 8: Confirm branch constraints and report**

```powershell
git status --short --branch
git log --oneline --decorate -10
git remote -v
```

Expected: clean `feature/wpf-video-monitor-ui`, local commits ahead of remote, no merge commit, and no push performed.

Report created/modified files, MVVM structure, models, total tests and results, build results, five clickable screenshot paths, and all real capabilities still not implemented. Stop without starting database or ZLMediaKit work.
