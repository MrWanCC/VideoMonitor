# Monitor Catalog Refresh Implementation Plan

**Goal:** Keep the real-time monitor projections synchronized with the shared `IDeviceCatalog` after device-group changes.

**Architecture:** Keep `InMemoryDeviceCatalog` as the only runtime data source. Rebuild the lightweight `MonitorCatalogProjection` on `Changed`, update the existing monitor tree collections in place, and preserve selection/expansion by stable group IDs. Do not change `MonitorSwitchService` or playback lifetimes.

**Tech Stack:** .NET 8, WPF, CommunityToolkit.Mvvm, xUnit.

## Global Constraints

- Do not create a second catalog.
- Do not modify `MonitorSwitchService`, ZLM, VLC, `PlaybackSession`, or playback lifecycle.
- Do not recreate `VideoTileViewModel` instances.
- Refresh monitor projections from the shared catalog on every catalog change.
- Preserve selected and expanded tree state when possible.

### Task 1: Make projected groups identifiable

**Files:**
- Modify: `src/VideoMonitor.Core/Models/MonitorGroup.cs`
- Modify: `src/VideoMonitor.Core/Services/MonitorCatalogProjection.cs`
- Test: `tests/VideoMonitor.Core.Tests/Services/MonitorCatalogProjectionTests.cs`

- [ ] Add a stable `GroupId` property to the Core `MonitorGroup` projection.
- [ ] Populate it from `DeviceGroup.Id` in `MonitorCatalogProjection`.
- [ ] Add a test asserting the projection retains the source group ID.

### Task 2: Refresh the main monitor tree

**Files:**
- Modify: `src/VideoMonitor.Wpf/ViewModels/MonitorViewModel.cs`
- Test: `tests/VideoMonitor.Core.Tests/ViewModels/MonitorCatalogRefreshTests.cs`

- [ ] Add tests for add, rename, and delete catalog changes affecting `TreeSections`.
- [ ] Make the monitor projection replaceable while keeping the existing `TreeSections` collection and tile instances.
- [ ] Rebuild the tree in place on `Catalog.Changed`.
- [ ] Preserve section expansion and selected child by `GroupId`.

### Task 3: Refresh the secondary monitor projection

**Files:**
- Modify: `src/VideoMonitor.Wpf/ViewModels/SecondaryMonitorViewModel.cs`
- Test: `tests/VideoMonitor.Core.Tests/ViewModels/MonitorCatalogRefreshTests.cs`

- [ ] Refresh the secondary view model's projected group lookup on `Catalog.Changed`.
- [ ] Resolve current tile metadata from the refreshed projection without changing tile or playback instances.

### Task 4: Verify

- [ ] Run the targeted monitor refresh tests.
- [ ] Run `git diff --check`.
- [ ] Run `dotnet build VideoMonitor.sln`.
- [ ] Run `dotnet test VideoMonitor.sln`.
