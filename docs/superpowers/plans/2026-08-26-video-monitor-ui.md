# Video Monitor UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a runnable .NET 8 WinForms mining video-monitor UI with fixed 3+1 main-screen switching, three-channel secondary-screen switching, mock data, display detection, and main-grid fullscreen behavior.

**Architecture:** `MonitorSwitchService` owns the seven display slots and publishes immutable snapshots; forms only translate user selections into service calls and render snapshots. Reusable WinForms controls own video-tile and grid presentation, while `ScreenService` isolates monitor placement from UI composition.

**Tech Stack:** C# 12, .NET SDK 8.0.424, `net8.0-windows`, WinForms, AntdUI 2.4.6, xUnit, Git

**Spec:** `docs/superpowers/specs/2026-08-26-video-monitor-ui-design.md`

## Global Constraints

- Target exactly `net8.0-windows`; do not target .NET Framework or .NET 9.
- Use AntdUI 2.4.6 for modern controls and WinForms `TableLayoutPanel` for percentage layouts.
- Use only mock data; do not add ZLMediaKit, SQL Server, SQLite, HCNetSDK, LibVLCSharp, UDP/RTP, recording, alarm backends, or permissions.
- Main screen is always a 2×2 grid with shaft slots 1–3 and tunnel slot 4.
- Secondary screen is always one row of three equal-width tiles and a 540-pixel window height.
- Keep `MainForm` as composition code; business switching stays in `MonitorSwitchService`.

---

### Task 1: Solution scaffold and switching domain

**Files:**
- Create: `global.json`
- Create: `.gitignore`
- Create: `VideoMonitor.sln`
- Create: `src/VideoMonitor.Client/VideoMonitor.Client.csproj`
- Create: `tests/VideoMonitor.Client.Tests/VideoMonitor.Client.Tests.csproj`
- Create: `src/VideoMonitor.Client/Models/CameraInfo.cs`
- Create: `src/VideoMonitor.Client/Models/MonitorGroup.cs`
- Create: `src/VideoMonitor.Client/Models/MonitorGroupType.cs`
- Create: `src/VideoMonitor.Client/Services/MonitorLayoutSnapshot.cs`
- Create: `src/VideoMonitor.Client/Services/MonitorSwitchService.cs`
- Create: `src/VideoMonitor.Client/Mock/MockMonitorData.cs`
- Create: `tests/VideoMonitor.Client.Tests/Services/MonitorSwitchServiceTests.cs`

**Interfaces:**
- Produces: `CameraInfo(string Name, string GroupName, int ChannelNumber, bool IsOnline = true)`.
- Produces: `MonitorGroup(string Name, MonitorGroupType Type, IReadOnlyList<CameraInfo> Cameras)`.
- Produces: `MonitorSwitchService.SwitchShaftGroup`, `SwitchTunnel`, `SwitchUnloadingGroup`, `Current`, and `LayoutChanged`.
- Produces: `MockMonitorData.CreateGroups()` returning every required group.

- [ ] **Step 1: Create the SDK-style solution and project configuration**

Run:

```powershell
dotnet new globaljson --sdk-version 8.0.424 --roll-forward latestPatch
dotnet new sln -n VideoMonitor
dotnet new winforms -n VideoMonitor.Client -o src/VideoMonitor.Client -f net8.0-windows
dotnet new xunit -n VideoMonitor.Client.Tests -o tests/VideoMonitor.Client.Tests -f net8.0
dotnet sln VideoMonitor.sln add src/VideoMonitor.Client/VideoMonitor.Client.csproj tests/VideoMonitor.Client.Tests/VideoMonitor.Client.Tests.csproj
dotnet add src/VideoMonitor.Client/VideoMonitor.Client.csproj package AntdUI --version 2.4.6
dotnet add tests/VideoMonitor.Client.Tests/VideoMonitor.Client.Tests.csproj reference src/VideoMonitor.Client/VideoMonitor.Client.csproj
```

Create a repository `.gitignore` containing the standard Visual Studio exclusions for `.vs/`, `bin/`, `obj/`, test results, and user-specific `*.user` files. Run `dotnet --version` after creating `global.json`; expected output is `8.0.424`.

Set the test target to Windows because it references the WinForms client:

```xml
<PropertyGroup>
  <TargetFramework>net8.0-windows</TargetFramework>
  <EnableWindowsTargeting>true</EnableWindowsTargeting>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
</PropertyGroup>
```

- [ ] **Step 2: Write the three failing switching tests**

Create tests that express the exact business boundaries:

```csharp
public sealed class MonitorSwitchServiceTests
{
    private readonly IReadOnlyList<MonitorGroup> groups = MockMonitorData.CreateGroups();

    [Fact]
    public void SwitchShaftGroup_ReplacesFirstThreeMainSlots_AndKeepsTunnel()
    {
        var service = CreateService();
        var originalTunnel = service.Current.MainSlots[3];

        service.SwitchShaftGroup(Group("西402溜井"));

        Assert.Equal(new[] { 1, 2, 3 }, service.Current.MainSlots.Take(3).Select(x => x.ChannelNumber));
        Assert.All(service.Current.MainSlots.Take(3), x => Assert.Equal("西402溜井", x.GroupName));
        Assert.Same(originalTunnel, service.Current.MainSlots[3]);
    }

    [Fact]
    public void SwitchTunnel_ReplacesOnlyFourthMainSlot()
    {
        var service = CreateService();
        var originalShaft = service.Current.MainSlots.Take(3).ToArray();

        service.SwitchTunnel(Group("Z-2#巷"));

        Assert.Equal(originalShaft, service.Current.MainSlots.Take(3));
        Assert.Equal("Z-2#巷", service.Current.MainSlots[3].Name);
    }

    [Fact]
    public void SwitchUnloadingGroup_ReplacesAllSecondarySlots_AndKeepsMainSlots()
    {
        var service = CreateService();
        var originalMain = service.Current.MainSlots.ToArray();

        service.SwitchUnloadingGroup(Group("3#主溜井"));

        Assert.Equal(originalMain, service.Current.MainSlots);
        Assert.All(service.Current.SecondarySlots, x => Assert.Equal("3#主溜井", x.GroupName));
        Assert.Equal(new[] { 1, 2, 3 }, service.Current.SecondarySlots.Select(x => x.ChannelNumber));
    }

    private MonitorSwitchService CreateService() => new(
        Group("备用1"), Group("Z-1#巷"), Group("2#主溜井"));

    private MonitorGroup Group(string name) => groups.Single(x => x.Name == name);
}
```

- [ ] **Step 3: Run the tests and verify RED**

Run:

```powershell
dotnet test tests/VideoMonitor.Client.Tests/VideoMonitor.Client.Tests.csproj
```

Expected: compilation fails because `CameraInfo`, `MonitorGroup`, `MockMonitorData`, and `MonitorSwitchService` do not exist.

- [ ] **Step 4: Implement the minimal models, mock data, and switching service**

Use immutable records and copied arrays so subscribers cannot mutate service state:

```csharp
public enum MonitorGroupType { UnloadingStation, Shaft, Tunnel }

public sealed record CameraInfo(
    string Name,
    string GroupName,
    int ChannelNumber,
    bool IsOnline = true);

public sealed record MonitorGroup(
    string Name,
    MonitorGroupType Type,
    IReadOnlyList<CameraInfo> Cameras);

public sealed record MonitorLayoutSnapshot(
    IReadOnlyList<CameraInfo> MainSlots,
    IReadOnlyList<CameraInfo> SecondarySlots);
```

Implement `MonitorSwitchService` with exact type and channel-count validation before replacing any slots:

```csharp
public void SwitchShaftGroup(MonitorGroup group)
{
    Validate(group, MonitorGroupType.Shaft, 3);
    Current = Current with
    {
        MainSlots = group.Cameras.Take(3).Concat(Current.MainSlots.Skip(3)).ToArray()
    };
    LayoutChanged?.Invoke(this, Current);
}

public void SwitchTunnel(MonitorGroup group)
{
    Validate(group, MonitorGroupType.Tunnel, 1);
    Current = Current with
    {
        MainSlots = Current.MainSlots.Take(3).Append(group.Cameras[0]).ToArray()
    };
    LayoutChanged?.Invoke(this, Current);
}

public void SwitchUnloadingGroup(MonitorGroup group)
{
    Validate(group, MonitorGroupType.UnloadingStation, 3);
    Current = Current with { SecondarySlots = group.Cameras.Take(3).ToArray() };
    LayoutChanged?.Invoke(this, Current);
}
```

Populate exactly the five shaft groups, five tunnel groups, and two unloading groups listed in the specification. Channel names use `<组名>-通道N`, except `备用1` and `备用2`, whose camera display names are `通道1` through `通道3` as requested.

- [ ] **Step 5: Run tests and verify GREEN**

Run:

```powershell
dotnet test tests/VideoMonitor.Client.Tests/VideoMonitor.Client.Tests.csproj
```

Expected: 3 tests passed, 0 failed.

- [ ] **Step 6: Commit the domain stage**

```powershell
git add global.json .gitignore VideoMonitor.sln src tests
git commit -m "feat: add monitor switching domain"
```

---

### Task 2: Reusable video controls and monitor tree

**Files:**
- Create: `src/VideoMonitor.Client/Controls/VideoTileControl.cs`
- Create: `src/VideoMonitor.Client/Controls/VideoGridControl.cs`
- Create: `src/VideoMonitor.Client/Controls/MonitorTreeControl.cs`
- Create: `tests/VideoMonitor.Client.Tests/Controls/VideoTileControlTests.cs`
- Create: `tests/VideoMonitor.Client.Tests/Controls/VideoGridControlTests.cs`

**Interfaces:**
- Consumes: `CameraInfo`, `MonitorGroup`, `MonitorGroupType`.
- Produces: `VideoTileControl.SetCamera`, `ShowOnline`, `ShowOffline`, `ShowError`.
- Produces: `VideoGridControl.CreateMainGrid`, `CreateSecondaryGrid`, `SetCameras`.
- Produces: `MonitorTreeControl.GroupSelected`.

- [ ] **Step 1: Write failing control behavior tests**

```csharp
[Fact]
public void SetCamera_UpdatesDisplayedMetadata()
{
    using var tile = new VideoTileControl();
    tile.SetCamera(new CameraInfo("西401溜井-通道2", "西401溜井", 2));
    Assert.Equal("西401溜井-通道2", tile.CameraNameText);
    Assert.Equal("西401溜井", tile.GroupNameText);
    Assert.Equal("通道 2", tile.ChannelText);
    Assert.Equal("在线", tile.StatusText);
}

[Fact]
public void CreateSecondaryGrid_UsesOneRowAndThreeEqualPercentColumns()
{
    using var grid = VideoGridControl.CreateSecondaryGrid();
    Assert.Equal(1, grid.Grid.RowCount);
    Assert.Equal(3, grid.Grid.ColumnCount);
    Assert.All(grid.Grid.ColumnStyles.Cast<ColumnStyle>(), x =>
        Assert.Equal(33.333f, x.Width, 3));
}
```

- [ ] **Step 2: Run control tests and verify RED**

Run:

```powershell
dotnet test tests/VideoMonitor.Client.Tests/VideoMonitor.Client.Tests.csproj --filter "FullyQualifiedName~Controls"
```

Expected: compilation fails because the three control classes do not exist.

- [ ] **Step 3: Implement the dark video tile and grids**

Build `VideoTileControl` from nested docked panels and labels. Expose read-only text properties only for observable UI verification:

```csharp
public string CameraNameText => cameraNameLabel.Text;
public string GroupNameText => groupLabel.Text;
public string ChannelText => channelLabel.Text;
public string StatusText => statusLabel.Text;

public void SetCamera(CameraInfo camera)
{
    cameraNameLabel.Text = camera.Name;
    groupLabel.Text = camera.GroupName;
    channelLabel.Text = $"通道 {camera.ChannelNumber}";
    if (camera.IsOnline) ShowOnline(); else ShowOffline();
}
```

Use green `#35D07F` for online, muted gray for offline, orange `#FF9F43` for errors, blue borders for the active industrial theme, and a centered “模拟视频画面” placeholder.

For the main grid, create two 50% rows and two 50% columns. For the secondary grid, create one 100% row and these exact styles:

```csharp
grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
```

- [ ] **Step 4: Implement the categorized monitor tree**

Use a dark-themed WinForms `TreeView` inside `MonitorTreeControl`. Store each selectable `MonitorGroup` in its node `Tag`, raise `GroupSelected` only when `Tag is MonitorGroup`, and group nodes under “卸矿站监控”, “溜井监控”, and “巷道监控”.

```csharp
private void OnAfterSelect(object? sender, TreeViewEventArgs e)
{
    if (e.Node.Tag is MonitorGroup group)
        GroupSelected?.Invoke(this, group);
}
```

- [ ] **Step 5: Run tests and build**

```powershell
dotnet test tests/VideoMonitor.Client.Tests/VideoMonitor.Client.Tests.csproj
dotnet build VideoMonitor.sln
```

Expected: all tests pass and the solution builds with 0 errors.

- [ ] **Step 6: Commit the reusable control stage**

```powershell
git add src/VideoMonitor.Client/Controls tests/VideoMonitor.Client.Tests/Controls
git commit -m "feat: add reusable monitor controls"
```

---

### Task 3: Screen placement and forms

**Files:**
- Create: `src/VideoMonitor.Client/Services/ScreenService.cs`
- Create: `src/VideoMonitor.Client/Forms/MainForm.cs`
- Create: `src/VideoMonitor.Client/Forms/SecondaryMonitorForm.cs`
- Modify: `src/VideoMonitor.Client/Program.cs`
- Delete: `src/VideoMonitor.Client/Form1.cs`
- Delete: `src/VideoMonitor.Client/Form1.Designer.cs`
- Delete: `src/VideoMonitor.Client/Form1.resx`
- Create: `tests/VideoMonitor.Client.Tests/Services/ScreenServiceTests.cs`

**Interfaces:**
- Consumes: groups, `MonitorSwitchService`, `VideoGridControl`, `MonitorTreeControl`.
- Produces: `ScreenService.ConfigureSecondaryWindow(Form)`.
- Produces: runnable `MainForm` and `SecondaryMonitorForm`.

- [ ] **Step 1: Write failing screen-placement tests**

```csharp
[Fact]
public void CalculateSecondaryBounds_UsesSecondWorkingAreaAndFixedHeight()
{
    var areas = new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(1920, 40, 2560, 1400) };
    var bounds = ScreenService.CalculateSecondaryBounds(areas);
    Assert.Equal(new Rectangle(1920, 40, 2560, 540), bounds);
}

[Fact]
public void CalculateSecondaryBounds_SingleScreenUsesSafeTestWindow()
{
    var bounds = ScreenService.CalculateSecondaryBounds(new[] { new Rectangle(0, 0, 1920, 1040) });
    Assert.Equal(new Rectangle(80, 80, 1440, 540), bounds);
}
```

- [ ] **Step 2: Run screen tests and verify RED**

```powershell
dotnet test tests/VideoMonitor.Client.Tests/VideoMonitor.Client.Tests.csproj --filter "FullyQualifiedName~ScreenServiceTests"
```

Expected: compilation fails because `ScreenService` does not exist.

- [ ] **Step 3: Implement screen calculation and real-screen adapter**

```csharp
public static Rectangle CalculateSecondaryBounds(IReadOnlyList<Rectangle> workingAreas)
{
    if (workingAreas.Count >= 2)
    {
        var area = workingAreas[1];
        return new Rectangle(area.Left, area.Top, area.Width, 540);
    }

    var primary = workingAreas[0];
    return new Rectangle(primary.Left + 80, primary.Top + 80,
        Math.Min(1440, Math.Max(900, primary.Width - 160)), 540);
}

public void ConfigureSecondaryWindow(Form form)
{
    var screens = Screen.AllScreens;
    form.StartPosition = FormStartPosition.Manual;
    form.Bounds = CalculateSecondaryBounds(screens.Select(x => x.WorkingArea).ToArray());
    form.FormBorderStyle = screens.Length >= 2 ? FormBorderStyle.None : FormBorderStyle.Sizable;
}
```

- [ ] **Step 4: Implement the secondary form**

Compose a 540-pixel-high form from a compact header and `VideoGridControl.CreateSecondaryGrid()`. Wire both AntdUI group buttons to `SwitchUnloadingGroup`; subscribe to `LayoutChanged` and call `SetCameras(snapshot.SecondarySlots)`.

```csharp
private void SwitchGroup(string groupName) =>
    switchService.SwitchUnloadingGroup(groups.Single(x => x.Name == groupName));
```

Set `MinimumSize.Height` and `MaximumSize.Height` to 540 only in two-screen borderless mode; keep the single-screen test window sizable horizontally but always assign a height of 540.

- [ ] **Step 5: Implement the main form composition and strict selection routing**

Use a three-column root `TableLayoutPanel` for left navigation, center content, and right tree. Put a 2×2 `VideoGridControl` in the center. Route the tree selection by type:

```csharp
private void OnGroupSelected(object? sender, MonitorGroup group)
{
    switch (group.Type)
    {
        case MonitorGroupType.Shaft:
            switchService.SwitchShaftGroup(group);
            break;
        case MonitorGroupType.Tunnel:
            switchService.SwitchTunnel(group);
            break;
        case MonitorGroupType.UnloadingStation:
            switchService.SwitchUnloadingGroup(group);
            break;
    }
}
```

Create six left navigation buttons, with only “实时监控” styled as selected and functional. Initialize grids from `switchService.Current` before showing forms.

- [ ] **Step 6: Implement main-grid fullscreen and Esc restore**

Keep references to the navigation panel, tree panel, top bar, bottom status area, and saved window state. Enter fullscreen by hiding those regions, setting `FormBorderStyle.None`, `WindowState.Maximized`, and focusing the main grid. Enable `KeyPreview` and restore on Esc:

```csharp
protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
{
    if (keyData == Keys.Escape && isMonitorFullscreen)
    {
        ExitMonitorFullscreen();
        return true;
    }
    return base.ProcessCmdKey(ref msg, keyData);
}
```

- [ ] **Step 7: Wire application startup and secondary lifetime**

In `Program.cs`, create groups, service, screen service, secondary form, and main form. Show the secondary form from the main form `Shown` event, and close it when the main form closes.

```csharp
ApplicationConfiguration.Initialize();
var groups = MockMonitorData.CreateGroups();
var service = new MonitorSwitchService(
    groups.Single(x => x.Name == "备用1"),
    groups.Single(x => x.Name == "Z-1#巷"),
    groups.Single(x => x.Name == "2#主溜井"));
Application.Run(new MainForm(service, groups, new ScreenService()));
```

- [ ] **Step 8: Run tests and build**

```powershell
dotnet test VideoMonitor.sln
dotnet build VideoMonitor.sln
```

Expected: all tests pass, build succeeds, 0 errors.

- [ ] **Step 9: Commit the UI and interaction stage**

```powershell
git add src tests
git commit -m "feat: add monitor desktop UI"
```

---

### Task 4: Runtime smoke verification and final corrections

**Files:**
- Modify only files whose verified behavior conflicts with the specification.

**Interfaces:**
- Consumes: complete solution.
- Produces: fresh build/test/runtime evidence and a clean Git worktree.

- [ ] **Step 1: Run the full automated verification**

```powershell
dotnet build VideoMonitor.sln
dotnet test VideoMonitor.sln --no-build
```

Expected: build succeeds with 0 errors; all tests pass with 0 failures.

- [ ] **Step 2: Launch the application for a bounded smoke test**

```powershell
dotnet run --project src/VideoMonitor.Client/VideoMonitor.Client.csproj
```

Verify the process opens without an unhandled exception, the main form shows 2×2 tiles, and the secondary form shows one row of three tiles at height 540. Close both windows after the check.

- [ ] **Step 3: Verify the required interaction sequence manually**

Check in order: default `备用1` + `Z-1#巷`; select `西401溜井`; select `西402溜井`; select `Z-2#巷`; select `2#主溜井`; select `3#主溜井`; enter full-monitor mode; press Esc. At every selection, confirm untouched slots remain unchanged.

- [ ] **Step 4: Re-run verification after any correction**

```powershell
dotnet build VideoMonitor.sln
dotnet test VideoMonitor.sln --no-build
git diff --check
```

Expected: 0 build errors, 0 failed tests, and no whitespace errors.

- [ ] **Step 5: Commit verified corrections only when files changed**

```powershell
git add src tests
git commit -m "fix: finalize monitor UI verification"
```

- [ ] **Step 6: Record final repository state**

```powershell
git status --short --branch
git log --oneline -5
dotnet --version
```

Expected: clean worktree, staged implementation commits visible, and SDK version beginning with `8.0` when selected by the project configuration.
