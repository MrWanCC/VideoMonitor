# WPF Monitor Interaction Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不改变已通过监控业务规则的前提下，完成品牌、工具栏、侧栏折叠、单画面区域放大、当前监控信息折叠和界面精度收尾，并保留所有现有 `VideoTile` 实例。

**Architecture:** 保持现有轻量 MVVM。临时 UI 状态和命令放在现有 ViewModel，业务切换继续由 `MonitorSwitchService` 负责；XAML 负责展示与绑定；code-behind 仅处理窗口尺寸和既有控件的布局重排。单画面模式只调整四个既有 `VideoTile` 的 `Visibility`、行列与 `GridSpan`，不创建、不销毁控件，也不替换 DataContext。

**Tech Stack:** C#、.NET 8、WPF、XAML、CommunityToolkit.Mvvm、xUnit

**Spec:** `docs/superpowers/specs/2026-08-27-wpf-monitor-interaction-polish-design.md`

## Global Constraints

- 当前分支保持 `feature/wpf-video-monitor-ui`，不 merge、不 push。
- 不修改主屏 3+1、溜井 1/2/3 联动、巷道只切第 4 路、卸矿站副屏 3 路切换等业务规则。
- 不接入 ZLMediaKit、LibVLCSharp、HCNetSDK、数据库、UDP/RTP、录像或告警后台。
- 不新建项目，不重构现有 WPF 架构，不修改既有 Core 业务测试源码。
- `IsSidebarCollapsed`、`IsSingleTileMode`、`SelectedVideoSlot`、`IsDetailPanelCollapsed` 只保存在运行时内存，应用重启恢复默认状态。
- 顶部“全屏”始终表示四画面监控区域全屏；单画面放大是视频网格内部状态，两者必须分离。
- 每个任务完成后执行对应验证并创建本地 commit。

---

### Task 1: Add Ephemeral UI State and Tests

**Files:**
- Modify: `tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj`
- Create: `tests/VideoMonitor.Core.Tests/ViewModels/MonitorUiStateTests.cs`
- Modify: `src/VideoMonitor.Wpf/ViewModels/MainViewModel.cs`
- Modify: `src/VideoMonitor.Wpf/ViewModels/MonitorViewModel.cs`
- Modify: `src/VideoMonitor.Wpf/ViewModels/VideoTileViewModel.cs`

- [ ] **Step 1: Make the existing test project able to reference WPF ViewModels**

Change the test target to Windows and add the existing WPF project reference:

```xml
<TargetFramework>net8.0-windows</TargetFramework>

<ProjectReference Include="..\..\src\VideoMonitor.Wpf\VideoMonitor.Wpf.csproj" />
```

Keep the existing Core reference and existing test package versions unchanged.

- [ ] **Step 2: Write failing tests for the four requested UI-state guarantees**

Create `MonitorUiStateTests.cs` with tests equivalent to:

```csharp
[Fact]
public void ToggleSingleTile_EntersModeWithRequestedExistingSlot()
{
    var viewModel = CreateMonitorViewModel();
    var requestedSlot = viewModel.MainTiles[2];

    viewModel.ToggleSingleTileCommand.Execute(requestedSlot);

    Assert.True(viewModel.IsSingleTileMode);
    Assert.Same(requestedSlot, viewModel.SelectedVideoSlot);
}

[Fact]
public void ToggleSingleTile_SameSlotAgain_RestoresFourViewState()
{
    var viewModel = CreateMonitorViewModel();
    var requestedSlot = viewModel.MainTiles[1];

    viewModel.ToggleSingleTileCommand.Execute(requestedSlot);
    viewModel.ToggleSingleTileCommand.Execute(requestedSlot);

    Assert.False(viewModel.IsSingleTileMode);
    Assert.Equal(4, viewModel.MainTiles.Count);
    Assert.Same(requestedSlot, viewModel.SelectedVideoSlot);
}

[Fact]
public void ToggleSidebar_DoesNotChangeCurrentMonitorGroups()
{
    var monitor = CreateMonitorViewModel();
    var main = new MainViewModel(monitor);
    var before = Snapshot(monitor);

    main.ToggleSidebarCommand.Execute(null);

    Assert.True(main.IsSidebarCollapsed);
    Assert.Equal(before, Snapshot(monitor));
}

[Fact]
public void ToggleDetailPanel_DoesNotChangeCurrentMonitorGroups()
{
    var monitor = CreateMonitorViewModel();
    var before = Snapshot(monitor);

    monitor.ToggleDetailPanelCommand.Execute(null);

    Assert.True(monitor.IsDetailPanelCollapsed);
    Assert.Equal(before, Snapshot(monitor));
}
```

Use existing `MockMonitorData` and `MonitorSwitchService` in the helper. The snapshot must compare the four main camera names, three secondary camera names, current chute name, current tunnel name, and current unloading group.

- [ ] **Step 3: Run the focused tests and confirm they fail for missing UI state**

Run:

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~MonitorUiStateTests
```

Expected: compilation fails because the new commands/properties do not exist yet.

- [ ] **Step 4: Implement sidebar state in `MainViewModel`**

Add generated state and a toggle command while leaving navigation/fullscreen behavior untouched:

```csharp
[ObservableProperty]
private bool isSidebarCollapsed;

public IRelayCommand ToggleSidebarCommand { get; }

private void ToggleSidebar()
{
    IsSidebarCollapsed = !IsSidebarCollapsed;
}
```

Initialize `ToggleSidebarCommand` in the existing constructor. Do not persist this property.

- [ ] **Step 5: Implement single-tile and detail state in `MonitorViewModel`**

Add:

```csharp
[ObservableProperty]
private bool isSingleTileMode;

[ObservableProperty]
private bool isDetailPanelCollapsed;

[ObservableProperty]
private VideoTileViewModel selectedVideoSlot;

public IRelayCommand<VideoTileViewModel> ToggleSingleTileCommand { get; }
public IRelayCommand ExitSingleTileModeCommand { get; }
public IRelayCommand ToggleDetailPanelCommand { get; }
```

Initialize `SelectedVideoSlot = MainTiles[0]` after the four existing tile ViewModels are created. Implement commands as:

```csharp
private void ToggleSingleTile(VideoTileViewModel? slot)
{
    if (slot is null || !MainTiles.Contains(slot))
    {
        return;
    }

    if (IsSingleTileMode && ReferenceEquals(SelectedVideoSlot, slot))
    {
        IsSingleTileMode = false;
        return;
    }

    SelectedVideoSlot = slot;
    IsSingleTileMode = true;
}

private void ExitSingleTileMode() => IsSingleTileMode = false;

private void ToggleDetailPanel()
{
    IsDetailPanelCollapsed = !IsDetailPanelCollapsed;
}
```

Do not call `MonitorSwitchService` from these UI commands. Existing group selection commands remain unchanged.

- [ ] **Step 6: Expose mock detail values on the existing tile ViewModel**

Add display-only properties required by the compact detail panel without changing Core models:

```csharp
public string IpAddress => "192.168.17.5";
public string Resolution => "1920×1080";
```

Continue deriving title, channel, status, bitrate, stream and timestamp from the current camera/slot data.

- [ ] **Step 7: Run focused and full tests**

Run:

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~MonitorUiStateTests
dotnet test VideoMonitor.sln
```

Expected: all tests pass with zero failures.

- [ ] **Step 8: Commit the UI-state stage locally**

```powershell
git add tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj tests/VideoMonitor.Core.Tests/ViewModels/MonitorUiStateTests.cs src/VideoMonitor.Wpf/ViewModels/MainViewModel.cs src/VideoMonitor.Wpf/ViewModels/MonitorViewModel.cs src/VideoMonitor.Wpf/ViewModels/VideoTileViewModel.cs
git commit -m "feat: add transient monitor UI state"
```

---

### Task 2: Apply Product Branding, Toolbar Reduction, and Sidebar Collapse

**Files:**
- Modify: `src/VideoMonitor.Wpf/Themes/Colors.xaml`
- Modify: `src/VideoMonitor.Wpf/MainWindow.xaml`
- Modify: `src/VideoMonitor.Wpf/MainWindow.xaml.cs`
- Modify: `src/VideoMonitor.Wpf/Views/SecondaryMonitorWindow.xaml`

- [ ] **Step 1: Run a static contract check and confirm the old UI fails it**

Run:

```powershell
$main = Get-Content src/VideoMonitor.Wpf/MainWindow.xaml -Raw
$secondary = Get-Content src/VideoMonitor.Wpf/Views/SecondaryMonitorWindow.xaml -Raw
if ($main -notmatch '罗河铁矿-620视频管理机' -or $secondary -notmatch '罗河铁矿-620视频管理机' -or $main -match '布局|抓图') { throw 'Branding and toolbar contract not met' }
```

Expected: the command throws because old branding and redundant toolbar entries still exist.

- [ ] **Step 2: Add shared dimensions to the theme dictionary**

Add only reusable layout values used by this task:

```xml
<system:Double x:Key="SidebarExpandedWidth">188</system:Double>
<system:Double x:Key="SidebarCollapsedWidth">56</system:Double>
<system:Double x:Key="PageToolbarHeight">40</system:Double>
<system:Double x:Key="DetailPanelExpandedHeight">104</system:Double>
<system:Double x:Key="DetailPanelCollapsedHeight">44</system:Double>
```

Use the dictionary's existing `System` namespace alias or add the standard `clr-namespace:System;assembly=mscorlib` alias consistent with the file.

- [ ] **Step 3: Replace current product branding and reduce the top toolbar**

In `MainWindow.xaml`:

- Set `Window.Title="罗河铁矿-620视频管理机"`.
- Replace the two-part logo text with one title: `罗河铁矿-620视频管理机`.
- Keep the site/status presentation.
- Keep only fullscreen, alarm and settings toolbar buttons.
- Remove layout and snapshot buttons, commands and tooltips from XAML.
- Keep alarm default background transparent and retain only the existing mild-blue hover treatment.

In `SecondaryMonitorWindow.xaml`, set:

```xml
Title="罗河铁矿-620视频管理机 - 卸矿站监控（副屏）"
```

- [ ] **Step 4: Make the navigation XAML support both widths**

Change the left column's initial width to `188` and remove any fixed `Width=200` from its inner container. Keep all existing navigation commands.

Create local styles that hide labels only when collapsed:

```xml
<Style x:Key="SidebarTextStyle" TargetType="TextBlock" BasedOn="{StaticResource NavigationTextStyle}">
    <Style.Triggers>
        <DataTrigger Binding="{Binding IsSidebarCollapsed}" Value="True">
            <Setter Property="Visibility" Value="Collapsed" />
        </DataTrigger>
    </Style.Triggers>
</Style>
```

Add meaningful `ToolTip` text to every navigation button so collapsed icon-only mode remains understandable. Remove the favorites section. Bind the bottom collapse button to `ToggleSidebarCommand`; show its label only when expanded and reverse its vector arrow with a trigger when collapsed.

- [ ] **Step 5: Apply the actual column width in window behavior code**

In `MainWindow.xaml.cs`:

- Subscribe to `IsSidebarCollapsed` alongside existing fullscreen property handling.
- Add constants `188d` and `56d`.
- Add `ApplySidebarState()` that sets `NavigationColumn.Width` from the ViewModel state.
- When leaving four-view fullscreen, call `ApplySidebarState()` instead of hardcoding `200`.
- Before entering top fullscreen, execute `viewModel.Monitor.ExitSingleTileModeCommand` so top fullscreen always starts in 2×2 four-view mode.
- Keep existing WindowChrome, Esc, minimize, maximize, close, and secondary-screen code unchanged.

- [ ] **Step 6: Re-run static contract and compile**

Run:

```powershell
$main = Get-Content src/VideoMonitor.Wpf/MainWindow.xaml -Raw
$secondary = Get-Content src/VideoMonitor.Wpf/Views/SecondaryMonitorWindow.xaml -Raw
if ($main -notmatch '罗河铁矿-620视频管理机' -or $secondary -notmatch '罗河铁矿-620视频管理机' -or $main -match '>布局<|>抓图<') { throw 'Branding and toolbar contract not met' }
dotnet build VideoMonitor.sln
dotnet test VideoMonitor.sln
```

Expected: contract passes, build succeeds, tests pass.

- [ ] **Step 7: Commit branding and navigation locally**

```powershell
git add src/VideoMonitor.Wpf/Themes/Colors.xaml src/VideoMonitor.Wpf/MainWindow.xaml src/VideoMonitor.Wpf/MainWindow.xaml.cs src/VideoMonitor.Wpf/Views/SecondaryMonitorWindow.xaml
git commit -m "feat: refine branding and collapsible navigation"
```

---

### Task 3: Simplify VideoTile and Preserve Instances in Single-Tile Mode

**Files:**
- Modify: `src/VideoMonitor.Wpf/Controls/VideoTile.xaml`
- Modify: `src/VideoMonitor.Wpf/Views/MonitorView.xaml`
- Modify: `src/VideoMonitor.Wpf/Views/MonitorView.xaml.cs`

- [ ] **Step 1: Run a static contract check and confirm existing tile chrome fails**

Run:

```powershell
$tile = Get-Content src/VideoMonitor.Wpf/Controls/VideoTile.xaml -Raw
$monitor = Get-Content src/VideoMonitor.Wpf/Views/MonitorView.xaml -Raw
if (($tile | Select-String -Pattern '<Button' -AllMatches).Matches.Count -ne 0 -or $monitor -notmatch 'MainTile1') { throw 'VideoTile/single-tile contract not met' }
```

Expected: the command throws because tile header buttons remain and the four controls are not named.

- [ ] **Step 2: Reduce the tile header to title and status only**

In `VideoTile.xaml`:

- Keep the current header height and data-bound complete title.
- Keep status point, status text and existing status colors.
- Remove snapshot, sound, more and fullscreen buttons from the header.
- Leave timestamp, bitrate and stream badge overlays unchanged.
- Do not add new controls or click handlers.

- [ ] **Step 3: Name the four existing controls and forward double-click intent**

In `MonitorView.xaml`, name the existing controls without replacing them:

```xml
<controls:VideoTile x:Name="MainTile1"
                    DataContext="{Binding MainTiles[0]}"
                    MouseDoubleClick="OnVideoTileMouseDoubleClick" />
```

Repeat for `MainTile2`–`MainTile4`. Do not use a second overlay grid, `ContentControl`, new `VideoTile`, template re-instantiation or DataContext reassignment.

- [ ] **Step 4: Implement layout-only single-tile mode in code-behind**

In `MonitorView.xaml.cs`:

- Subscribe/unsubscribe to the current `MonitorViewModel.PropertyChanged` when DataContext changes.
- On double-click, execute `ToggleSingleTileCommand` with the clicked tile's existing `VideoTileViewModel` DataContext.
- Add a reset method that restores all four controls to their original row/column, span `1`, `Visibility.Visible`, and normal Z order.
- When single mode is active, keep the selected existing control visible, set it to row `0`, column `0`, row span `2`, column span `2`, and collapse the other three controls.

Core layout method:

```csharp
private void ApplySingleTileLayout()
{
    ResetFourTileLayout();

    if (DataContext is not MonitorViewModel viewModel || !viewModel.IsSingleTileMode)
    {
        return;
    }

    foreach (var tile in MainTiles)
    {
        var isSelected = ReferenceEquals(tile.DataContext, viewModel.SelectedVideoSlot);
        tile.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;

        if (isSelected)
        {
            Grid.SetRow(tile, 0);
            Grid.SetColumn(tile, 0);
            Grid.SetRowSpan(tile, 2);
            Grid.SetColumnSpan(tile, 2);
            Panel.SetZIndex(tile, 1);
        }
    }
}
```

`MainTiles` above is a private array/list containing `MainTile1`–`MainTile4`; it contains the same XAML-created objects for the lifetime of the view.

- [ ] **Step 5: Validate the preservation contract and run tests**

Run:

```powershell
$tile = Get-Content src/VideoMonitor.Wpf/Controls/VideoTile.xaml -Raw
$monitor = Get-Content src/VideoMonitor.Wpf/Views/MonitorView.xaml -Raw
$code = Get-Content src/VideoMonitor.Wpf/Views/MonitorView.xaml.cs -Raw
if (($tile | Select-String -Pattern '<Button' -AllMatches).Matches.Count -ne 0) { throw 'VideoTile header buttons remain' }
foreach ($name in 'MainTile1','MainTile2','MainTile3','MainTile4') { if ($monitor -notmatch $name) { throw "$name missing" } }
if ($code -match 'new\s+VideoTile') { throw 'Single-tile mode must preserve existing VideoTile instances' }
dotnet build VideoMonitor.sln
dotnet test VideoMonitor.sln
```

Expected: contract passes, build succeeds, all tests pass.

- [ ] **Step 6: Commit the tile stage locally**

```powershell
git add src/VideoMonitor.Wpf/Controls/VideoTile.xaml src/VideoMonitor.Wpf/Views/MonitorView.xaml src/VideoMonitor.Wpf/Views/MonitorView.xaml.cs
git commit -m "feat: add instance-safe single tile viewing"
```

---

### Task 4: Align Tool Rows and Build the Compact Current Monitor Panel

**Files:**
- Modify: `src/VideoMonitor.Wpf/Views/MonitorView.xaml`
- Modify: `src/VideoMonitor.Wpf/Views/MonitorView.xaml.cs`
- Modify: `src/VideoMonitor.Wpf/Controls/MonitorTree.xaml`

- [ ] **Step 1: Run a static contract check and confirm the old detail tabs fail**

Run:

```powershell
$monitor = Get-Content src/VideoMonitor.Wpf/Views/MonitorView.xaml -Raw
if ($monitor -notmatch '当前监控信息' -or $monitor -match '告警信息|录像计划|存储信息') { throw 'Current monitor detail contract not met' }
```

Expected: command throws while the old multi-tab detail panel remains.

- [ ] **Step 2: Put breadcrumb and tree search on one shared visual baseline**

In `MonitorView.xaml`:

- Set the first row to `48`.
- Give the breadcrumb container `Height="{StaticResource PageToolbarHeight}"`, `VerticalAlignment="Center"`, and horizontal margins only.
- Remove the local layout/snapshot actions from this row.

In `MonitorTree.xaml`:

- Set the first row to `48`.
- Give the search/filter container the same shared `PageToolbarHeight`, `VerticalAlignment="Center"`, and horizontal margins only.
- Keep the existing search/filter behavior and tree hierarchy unchanged.

- [ ] **Step 3: Replace old device tabs with the compact panel**

Set the expanded detail row to `104` and collapsed row to `44`. The header must contain:

- `当前监控信息`
- current chute name
- current tunnel name
- selected slot title
- one collapse/expand vector button bound to `ToggleDetailPanelCommand`

The body is a four-column compact grid bound to `SelectedVideoSlot`:

```xml
<TextBlock Text="{Binding SelectedVideoSlot.IpAddress}" />
<TextBlock Text="{Binding SelectedVideoSlot.Status, Converter={StaticResource CameraStatusTextConverter}}" />
<TextBlock Text="{Binding SelectedVideoSlot.StreamLabel}" />
<TextBlock Text="{Binding SelectedVideoSlot.Resolution}" />
<TextBlock Text="{Binding SelectedVideoSlot.Bitrate}" />
<TextBlock Text="{Binding SelectedVideoSlot.Timestamp}" />
```

Use muted label styles and primary value styles. Remove all old alarm/record/storage tab controls and unused edit/refresh actions. Bind body visibility to `IsDetailPanelCollapsed=False`; do not retain a separate local expansion boolean.

- [ ] **Step 4: Make detail row height follow ViewModel state without changing business**

In `MonitorView.xaml.cs`, extend the existing ViewModel property subscription:

```csharp
private void ApplyDetailPanelState()
{
    if (DataContext is not MonitorViewModel viewModel)
    {
        return;
    }

    DetailRow.Height = new GridLength(
        viewModel.IsDetailPanelCollapsed ? 44d : 104d);
}
```

Remove the old `detailExpanded` field and old click handler. In `SetFullscreen(false)`, restore the row height from `IsDetailPanelCollapsed`; do not hardcode the old 132 px height.

- [ ] **Step 5: Verify detail and alignment contracts**

Run:

```powershell
$monitor = Get-Content src/VideoMonitor.Wpf/Views/MonitorView.xaml -Raw
$tree = Get-Content src/VideoMonitor.Wpf/Controls/MonitorTree.xaml -Raw
if ($monitor -notmatch '当前监控信息' -or $monitor -match '告警信息|录像计划|存储信息') { throw 'Old detail tabs remain' }
if ($monitor -notmatch 'PageToolbarHeight' -or $tree -notmatch 'PageToolbarHeight') { throw 'Toolbar alignment token not shared' }
dotnet build VideoMonitor.sln
dotnet test VideoMonitor.sln
```

Expected: contract passes, build succeeds, tests pass.

- [ ] **Step 6: Commit detail/alignment stage locally**

```powershell
git add src/VideoMonitor.Wpf/Views/MonitorView.xaml src/VideoMonitor.Wpf/Views/MonitorView.xaml.cs src/VideoMonitor.Wpf/Controls/MonitorTree.xaml
git commit -m "feat: compact current monitor information"
```

---

### Task 5: Runtime Verification, Screenshots, and Final Local Commit

**Files:**
- Create: `artifacts/screenshots/wpf-interaction-polish-main.png`
- Create: `artifacts/screenshots/wpf-interaction-polish-sidebar-collapsed.png`
- Create: `artifacts/screenshots/wpf-interaction-polish-single-tile.png`
- Create: `artifacts/screenshots/wpf-interaction-polish-detail-collapsed.png`
- Create: `artifacts/screenshots/wpf-interaction-polish-secondary.png`

- [ ] **Step 1: Run clean full verification**

Run:

```powershell
dotnet build VideoMonitor.sln --configuration Release
dotnet test VideoMonitor.sln --configuration Release --no-build
```

Expected: zero build errors and all tests pass.

- [ ] **Step 2: Launch the actual WPF application at the 1920×1080 design size**

Run the Release executable, place the main window at the primary display origin, and size it to the 1920×1080 design baseline. Allow the app to initialize its secondary monitor window. Do not use design-time rendering or fabricated screenshots.

- [ ] **Step 3: Verify business interactions before capturing**

Using the running UI:

- Select `西401溜井`; confirm main slots 1/2/3 change and slot 4 does not.
- Select `Z-2#巷`; confirm only slot 4 changes.
- Select `2#主溜井`, then `3#主溜井`; confirm all three secondary slots switch together.
- Enter and exit top fullscreen with Esc; confirm it uses 2×2 four-view.
- Double-click each main tile once to confirm the requested existing tile fills only the video grid; double-click it again to restore 2×2.
- Collapse/expand navigation and current monitor panel; confirm selected groups and tile data remain unchanged.

- [ ] **Step 4: Capture the five required actual runtime states**

Capture using the real process window handles:

1. default main window
2. collapsed navigation
3. single-tile video grid
4. collapsed current monitor information
5. secondary monitor window

Save to the five paths listed above. Ensure no unrelated applications cover the windows.

- [ ] **Step 5: Visually inspect every saved image**

Open all five image files and confirm:

- new branding appears in both window titles
- top toolbar has only fullscreen/alarm/settings
- collapsed sidebar is icon-only and remains usable
- single-tile mode fills the existing video grid while shell/detail/status remain visible
- collapsed detail header is not vertically compressed
- secondary layout remains one row of three equal tiles at height 540
- no clipping, white native title bar, overlapping text, or missing status elements

If any issue is found, fix only the relevant XAML/style/window behavior, rerun build/test, and recapture affected images.

- [ ] **Step 6: Commit screenshots and any final verification-only correction locally**

```powershell
git add artifacts/screenshots/wpf-interaction-polish-main.png artifacts/screenshots/wpf-interaction-polish-sidebar-collapsed.png artifacts/screenshots/wpf-interaction-polish-single-tile.png artifacts/screenshots/wpf-interaction-polish-detail-collapsed.png artifacts/screenshots/wpf-interaction-polish-secondary.png
git commit -m "test: capture WPF interaction polish states"
```

- [ ] **Step 7: Confirm branch and remote constraints**

Run:

```powershell
git status --short --branch
git log --oneline --decorate -8
git remote -v
```

Expected:

- branch is `feature/wpf-video-monitor-ui`
- working tree is clean
- local commits are ahead of the remote branch
- no merge commit was created
- no `git push` was performed

- [ ] **Step 8: Report completion**

Report modified files, local commit hashes, build result, test result, five clickable screenshot paths, and remaining visual differences. Explicitly state that no merge or push occurred and no external video/database capability was added.
