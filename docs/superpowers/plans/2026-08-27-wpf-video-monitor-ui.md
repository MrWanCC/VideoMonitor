# WPF Video Monitor UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a high-fidelity .NET 8 WPF mining-monitor desktop UI with MVVM, fixed 3+1 main switching, a one-row three-tile secondary monitor, display placement, and monitor-area fullscreen behavior.

**Architecture:** A platform-neutral Core library owns immutable monitor models, mock data, and all slot switching. The WPF project references Core and uses CommunityToolkit.Mvvm ViewModels plus ResourceDictionary-driven XAML; window code-behind is limited to window placement and fullscreen chrome.

**Tech Stack:** C# 12, .NET SDK 8.0.424, WPF/XAML, CommunityToolkit.Mvvm 8.4.2, xUnit

**Spec:** `docs/superpowers/specs/2026-08-27-wpf-video-monitor-ui-design.md`

## Global Constraints

- Work only on `feature/wpf-video-monitor-ui`; do not merge or push.
- Preserve `VideoMonitor.Client` and its tests without modifying WinForms UI code.
- Add `VideoMonitor.Core`, `VideoMonitor.Wpf`, and `VideoMonitor.Core.Tests` as independent projects.
- Use native WPF XAML, ResourceDictionary, styles, and MVVM; do not add a large UI framework.
- Keep real video, databases, recording, alarms, permissions, ZLMediaKit, LibVLCSharp, HCNetSDK, UDP/RTP, and GB28181 out of this phase.

---

### Task 1: WPF solution skeleton and Core switching domain

**Files:**
- Create: `src/VideoMonitor.Core/VideoMonitor.Core.csproj`
- Create: `src/VideoMonitor.Core/Models/CameraInfo.cs`
- Create: `src/VideoMonitor.Core/Models/CameraStatus.cs`
- Create: `src/VideoMonitor.Core/Models/MonitorGroup.cs`
- Create: `src/VideoMonitor.Core/Models/MonitorGroupType.cs`
- Create: `src/VideoMonitor.Core/Services/MonitorLayoutSnapshot.cs`
- Create: `src/VideoMonitor.Core/Services/MonitorSwitchService.cs`
- Create: `src/VideoMonitor.Core/Mock/MockMonitorData.cs`
- Create: `tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj`
- Create: `tests/VideoMonitor.Core.Tests/Services/MonitorSwitchServiceTests.cs`
- Modify: `VideoMonitor.sln`

**Interfaces:**
- `MonitorSwitchService.SwitchChuteGroup(MonitorGroup)` replaces only main slots 1–3.
- `MonitorSwitchService.SwitchTunnel(MonitorGroup)` replaces only main slot 4.
- `MonitorSwitchService.SwitchUnloadingGroup(MonitorGroup)` replaces only the three secondary slots.

- [ ] **Step 1: Scaffold the three new project paths and set the WPF solution membership**

```powershell
dotnet new classlib -n VideoMonitor.Core -o src/VideoMonitor.Core -f net8.0
dotnet new wpf -n VideoMonitor.Wpf -o src/VideoMonitor.Wpf -f net8.0
dotnet new xunit -n VideoMonitor.Core.Tests -o tests/VideoMonitor.Core.Tests -f net8.0
dotnet sln VideoMonitor.sln remove src/VideoMonitor.Client/VideoMonitor.Client.csproj tests/VideoMonitor.Client.Tests/VideoMonitor.Client.Tests.csproj
dotnet sln VideoMonitor.sln add src/VideoMonitor.Core/VideoMonitor.Core.csproj src/VideoMonitor.Wpf/VideoMonitor.Wpf.csproj tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj
dotnet add src/VideoMonitor.Wpf/VideoMonitor.Wpf.csproj reference src/VideoMonitor.Core/VideoMonitor.Core.csproj
dotnet add tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj reference src/VideoMonitor.Core/VideoMonitor.Core.csproj
dotnet add src/VideoMonitor.Wpf/VideoMonitor.Wpf.csproj package CommunityToolkit.Mvvm --version 8.4.2
```

- [ ] **Step 2: Write the three failing Core tests**

```csharp
[Fact]
public void SwitchChuteGroup_ReplacesSlotsOneToThree_AndKeepsSlotFour()
{
    var service = CreateService();
    var tunnel = service.Current.MainSlots[3];
    service.SwitchChuteGroup(Group("西402溜井"));
    Assert.All(service.Current.MainSlots.Take(3), camera => Assert.Equal("西402溜井", camera.GroupName));
    Assert.Same(tunnel, service.Current.MainSlots[3]);
}

[Fact]
public void SwitchTunnel_ReplacesOnlySlotFour()
{
    var service = CreateService();
    var chute = service.Current.MainSlots.Take(3).ToArray();
    service.SwitchTunnel(Group("Z-2#巷"));
    Assert.Equal(chute, service.Current.MainSlots.Take(3));
    Assert.Equal("Z-2#巷", service.Current.MainSlots[3].Name);
}

[Fact]
public void SwitchUnloadingGroup_ReplacesAllSecondarySlots_AndKeepsMain()
{
    var service = CreateService();
    var main = service.Current.MainSlots.ToArray();
    service.SwitchUnloadingGroup(Group("3#主溜井"));
    Assert.Equal(main, service.Current.MainSlots);
    Assert.All(service.Current.SecondarySlots, camera => Assert.Equal("3#主溜井", camera.GroupName));
}
```

- [ ] **Step 3: Run tests and verify RED**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj
```

Expected: compilation fails because the Core types do not exist.

- [ ] **Step 4: Implement minimal immutable Core models and switching service**

Use `CameraStatus { Online, Warning, Offline }`, immutable records, exact group-type/channel-count validation, copied arrays in snapshots, and the task-specified mock group names. Give online mock cameras bitrate values such as `4.2 Mbps` and stream type `主码流`.

```csharp
public void SwitchChuteGroup(MonitorGroup group)
{
    Validate(group, MonitorGroupType.Chute, 3);
    Current = Current with
    {
        MainSlots = group.Cameras.Take(3).Concat(Current.MainSlots.Skip(3)).ToArray()
    };
    LayoutChanged?.Invoke(this, Current);
}
```

- [ ] **Step 5: Verify Core and commit**

```powershell
dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj
dotnet build VideoMonitor.sln
git add VideoMonitor.sln src/VideoMonitor.Core src/VideoMonitor.Wpf/VideoMonitor.Wpf.csproj tests/VideoMonitor.Core.Tests
git commit -m "feat: add WPF solution and monitor core"
```

---

### Task 2: Theme dictionaries and application shell resources

**Files:**
- Create: `src/VideoMonitor.Wpf/Themes/Colors.xaml`
- Create: `src/VideoMonitor.Wpf/Themes/Typography.xaml`
- Create: `src/VideoMonitor.Wpf/Themes/Buttons.xaml`
- Create: `src/VideoMonitor.Wpf/Themes/Controls.xaml`
- Modify: `src/VideoMonitor.Wpf/App.xaml`

**Interfaces:**
- Produces named brush keys from the design spec and shared styles `NavigationButtonStyle`, `PrimaryButtonStyle`, `IconButtonStyle`, `PanelBorderStyle`, and typography styles.

- [ ] **Step 1: Add all design tokens to ResourceDictionary files**

Define each confirmed color once as a `SolidColorBrush`, define corner radius 6 and spacing resources, and base typography on `Microsoft YaHei UI`.

```xml
<SolidColorBrush x:Key="WindowBackgroundBrush" Color="#07111D" />
<SolidColorBrush x:Key="PrimaryBlueBrush" Color="#1687FF" />
<CornerRadius x:Key="CardCornerRadius">6</CornerRadius>
<Thickness x:Key="Spacing16">16</Thickness>
```

- [ ] **Step 2: Merge dictionaries at application scope and verify build**

```xml
<ResourceDictionary.MergedDictionaries>
  <ResourceDictionary Source="Themes/Colors.xaml" />
  <ResourceDictionary Source="Themes/Typography.xaml" />
  <ResourceDictionary Source="Themes/Buttons.xaml" />
  <ResourceDictionary Source="Themes/Controls.xaml" />
</ResourceDictionary.MergedDictionaries>
```

```powershell
dotnet build VideoMonitor.sln
git add src/VideoMonitor.Wpf/Themes src/VideoMonitor.Wpf/App.xaml
git commit -m "feat: add WPF industrial theme"
```

---

### Task 3: Reusable VideoTile and ViewModel

**Files:**
- Create: `src/VideoMonitor.Wpf/ViewModels/VideoTileViewModel.cs`
- Create: `src/VideoMonitor.Wpf/Controls/VideoTile.xaml`
- Create: `src/VideoMonitor.Wpf/Controls/VideoTile.xaml.cs`
- Create: `src/VideoMonitor.Wpf/Converters/CameraStatusConverters.cs`

**Interfaces:**
- `VideoTileViewModel.Update(CameraInfo)` updates observable name, group, channel, status, bitrate, and stream type.
- `VideoTile` binds only to `VideoTileViewModel` and exposes a replaceable center content layer.

- [ ] **Step 1: Implement observable tile state and status converters**

Use `ObservableObject` with explicit `SetProperty` methods. Convert `CameraStatus` to the centralized Online/Warning/Offline brushes and Chinese status text.

- [ ] **Step 2: Build the high-fidelity VideoTile XAML**

Use a rounded card border, header metadata, centered simulated-video placeholder, subtle viewfinder marks, and bottom bitrate/stream metadata. Do not hardcode theme colors in the control.

- [ ] **Step 3: Build and commit**

```powershell
dotnet build VideoMonitor.sln
git add src/VideoMonitor.Wpf/ViewModels/VideoTileViewModel.cs src/VideoMonitor.Wpf/Controls src/VideoMonitor.Wpf/Converters
git commit -m "feat: add reusable WPF video tile"
```

---

### Task 4: Main monitor UI, tree, and fake-data switching

**Files:**
- Create: `src/VideoMonitor.Wpf/ViewModels/MonitorTreeItemViewModel.cs`
- Create: `src/VideoMonitor.Wpf/ViewModels/MonitorViewModel.cs`
- Create: `src/VideoMonitor.Wpf/ViewModels/MainViewModel.cs`
- Create: `src/VideoMonitor.Wpf/Controls/MonitorTree.xaml`
- Create: `src/VideoMonitor.Wpf/Controls/MonitorTree.xaml.cs`
- Create: `src/VideoMonitor.Wpf/Controls/StatusBar.xaml`
- Create: `src/VideoMonitor.Wpf/Controls/StatusBar.xaml.cs`
- Create: `src/VideoMonitor.Wpf/Views/Pages/MonitorView.xaml`
- Create: `src/VideoMonitor.Wpf/Views/Pages/MonitorView.xaml.cs`
- Create: `src/VideoMonitor.Wpf/Views/Pages/DeviceView.xaml`
- Create: `src/VideoMonitor.Wpf/Views/Pages/MediaView.xaml`
- Replace: `src/VideoMonitor.Wpf/MainWindow.xaml`
- Replace: `src/VideoMonitor.Wpf/MainWindow.xaml.cs`

**Interfaces:**
- `MonitorViewModel.MainTiles` always contains four tile ViewModels.
- `MonitorViewModel.SelectGroupCommand` routes Chute/Tunnel/UnloadingStation to the shared Core service.
- `MainViewModel.IsMonitorFullscreen` controls chrome visibility.

- [ ] **Step 1: Implement ViewModels and strict selection routing**

```csharp
private void SelectGroup(MonitorTreeItemViewModel item)
{
    if (item.Group is null) return;
    switch (item.Group.Type)
    {
        case MonitorGroupType.Chute: switchService.SwitchChuteGroup(item.Group); break;
        case MonitorGroupType.Tunnel: switchService.SwitchTunnel(item.Group); break;
        case MonitorGroupType.UnloadingStation: switchService.SwitchUnloadingGroup(item.Group); break;
    }
}
```

- [ ] **Step 2: Build the main static UI in XAML**

Use a three-row shell, a 208-pixel navigation column, a flexible center column, and a 304-pixel monitor-tree column. `MonitorView` uses two `*` rows and two `*` columns with four explicit `VideoTile` instances.

- [ ] **Step 3: Build categorized monitor tree and status bar**

Use a hierarchical template with category headers and clickable leaf buttons. Keep category order Unloading Station, Chute, Tunnel and exact mock names.

- [ ] **Step 4: Wire startup composition, build, and commit**

Create the shared Core service in `App.xaml.cs`, create Main/Monitor/Secondary ViewModels, and pass them to windows. Build before committing.

```powershell
dotnet build VideoMonitor.sln
git add src/VideoMonitor.Wpf
git commit -m "feat: add WPF main monitor UI"
```

---

### Task 5: Secondary monitor and display placement

**Files:**
- Create: `src/VideoMonitor.Wpf/ViewModels/SecondaryMonitorViewModel.cs`
- Create: `src/VideoMonitor.Wpf/Views/SecondaryMonitorWindow.xaml`
- Create: `src/VideoMonitor.Wpf/Views/SecondaryMonitorWindow.xaml.cs`
- Create: `src/VideoMonitor.Wpf/Services/ScreenService.cs`
- Modify: `src/VideoMonitor.Wpf/App.xaml.cs`

**Interfaces:**
- `SecondaryMonitorViewModel.Tiles` always contains exactly three tile ViewModels.
- `ScreenService.PlaceMainWindow` and `PlaceSecondaryWindow` use monitor working areas and a 540-pixel secondary height.

- [ ] **Step 1: Implement secondary ViewModel and group commands**

Subscribe to the shared service snapshot; bind two commands for `2#主溜井` and `3#主溜井` to `SwitchUnloadingGroup`.

- [ ] **Step 2: Build a strict one-row, three-column secondary XAML**

```xml
<Grid>
  <Grid.ColumnDefinitions>
    <ColumnDefinition Width="*" />
    <ColumnDefinition Width="*" />
    <ColumnDefinition Width="*" />
  </Grid.ColumnDefinitions>
</Grid>
```

Set window height 540 and place the compact group selector over the upper edge without creating a wrapping panel.

- [ ] **Step 3: Implement screen placement and single-screen fallback**

Use `System.Windows.Forms.Screen.AllScreens` only for monitor geometry. Convert working-area pixel coordinates to WPF device-independent units at window source initialization. On one screen, show a draggable test window at an offset; on two screens, use the second working area width and top-left position.

- [ ] **Step 4: Build and commit**

```powershell
dotnet build VideoMonitor.sln
git add src/VideoMonitor.Wpf
git commit -m "feat: add WPF secondary monitor"
```

---

### Task 6: Four-tile fullscreen, final verification, and screenshot

**Files:**
- Modify: `src/VideoMonitor.Wpf/MainWindow.xaml`
- Modify: `src/VideoMonitor.Wpf/MainWindow.xaml.cs`
- Modify only verified defects elsewhere.
- Create runtime artifact: `artifacts/screenshots/wpf-video-monitor.png`

**Interfaces:**
- Fullscreen button hides all shell chrome and maximizes the whole 2×2 region.
- Esc restores prior window state, style, bounds, and chrome visibility.

- [ ] **Step 1: Implement fullscreen view state and Esc handling**

Store prior `WindowStyle`, `WindowState`, and bounds in `MainWindow`; toggle `MainViewModel.IsMonitorFullscreen`. Bind shell chrome visibility and column/row dimensions to that state.

- [ ] **Step 2: Run final automated verification**

```powershell
dotnet build VideoMonitor.sln
dotnet test VideoMonitor.sln --no-build
git diff --check
```

Expected: 0 build warnings/errors and all Core tests pass.

- [ ] **Step 3: Launch and capture the actual WPF window**

```powershell
dotnet run --project src/VideoMonitor.Wpf/VideoMonitor.Wpf.csproj
```

Capture the running main window to `artifacts/screenshots/wpf-video-monitor.png`; inspect the image for the dark industrial shell, fixed 2×2 grid, visible tree, and no clipping.

- [ ] **Step 4: Commit final verified corrections and screenshot**

```powershell
git add src tests artifacts/screenshots/wpf-video-monitor.png
git commit -m "feat: finalize WPF monitor experience"
git status --short --branch
```

Expected: clean `feature/wpf-video-monitor-ui` worktree with no merge or push.
