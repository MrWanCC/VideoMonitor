# Single Camera ZLM Playback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Play Camera01 through ZLMediaKit and LibVLCSharp.WPF in only the upper-left main-screen VideoTile while preserving all existing monitor behavior and simulated tiles.

**Architecture:** Add a Core-referencing Infrastructure project for Hikvision RTSP and ZLM HTTP integration. In WPF, keep `SingleCameraPlaybackCoordinator` behind `IPlaybackSourceProvider`; the local provider owns direct-ZLM validation behavior while one application-scoped LibVLC and one runtime PlaybackSession survive layout and navigation changes.

**Tech Stack:** C# 12, .NET 8, WPF, System.Text.Json, HttpClient, LibVLCSharp.WPF 3.10.1, VideoLAN.LibVLC.Windows 3.0.23.1, xUnit 2.5.3

**Spec:** `docs/superpowers/specs/2026-08-28-single-camera-zlm-playback-design.md`

## Global Constraints

- Only Camera01 / “西401溜井 · 通道1” may become a real stream.
- The other three main tiles and all three secondary tiles remain simulated.
- Do not modify `MonitorSwitchService` or the 3+1 / secondary switching rules.
- ZLM pulls Hikvision RTSP with `rtp_type=0` (TCP); this is unrelated to future PLC UDP.
- Real camera credentials and the ZLM Secret stay only in Git-ignored local JSON files.
- WPF direct control of ZLM is temporary single-machine validation; no Server API, registry, hook, lease, or reference-count implementation belongs in this plan.
- A reused ZLM stream sets `OwnsProxy=false`; only a proxy created by this process may be deleted.
- UI-only layout and navigation changes must not recreate the VideoTile, MediaPlayer, PlaybackSession, or ZLM proxy.
- Do not merge or push.

---

### Task 1: Infrastructure Project, Hikvision URL, and Stable Stream ID

**Files:**
- Create: `src/VideoMonitor.Infrastructure/VideoMonitor.Infrastructure.csproj`
- Create: `src/VideoMonitor.Infrastructure/Hikvision/HikvisionRtspUrlBuilder.cs`
- Create: `src/VideoMonitor.Infrastructure/ZLMediaKit/StreamIdGenerator.cs`
- Modify: `VideoMonitor.sln`
- Modify: `tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj`
- Create: `tests/VideoMonitor.Core.Tests/Infrastructure/HikvisionRtspUrlBuilderTests.cs`
- Create: `tests/VideoMonitor.Core.Tests/Infrastructure/StreamIdGeneratorTests.cs`

**Interfaces:**
- Consumes: `CameraDevice`, `CameraChannel`, and `StreamType` from `VideoMonitor.Core.Models`.
- Produces: `HikvisionRtspUrlBuilder.Build(CameraDevice, CameraChannel) : Uri`, `HikvisionRtspUrlBuilder.Redact(Uri) : string`, and `StreamIdGenerator.Generate(CameraDevice, CameraChannel) : string`.

- [ ] **Step 1: Add Infrastructure references and write failing URL tests**

```csharp
[Theory]
[InlineData(StreamType.Main, 1, "/Streaming/Channels/101")]
[InlineData(StreamType.Sub, 1, "/Streaming/Channels/102")]
[InlineData(StreamType.Main, 2, "/Streaming/Channels/201")]
[InlineData(StreamType.Sub, 2, "/Streaming/Channels/202")]
public void Build_UsesHikvisionChannelEncoding(StreamType streamType, int channelNo, string expectedPath)
{
    var (device, channel) = CreateDevice(streamType, channelNo);
    var uri = HikvisionRtspUrlBuilder.Build(device, channel);
    Assert.Equal(expectedPath, uri.AbsolutePath);
    Assert.Equal("192.168.0.2", uri.Host);
    Assert.Equal(554, uri.Port);
}

[Fact]
public void Redact_DoesNotExposePassword()
{
    var (device, channel) = CreateDevice(StreamType.Main, 1);
    var redacted = HikvisionRtspUrlBuilder.Redact(HikvisionRtspUrlBuilder.Build(device, channel));
    Assert.Contains("admin:******@", redacted);
    Assert.DoesNotContain(device.Password, redacted);
}
```

- [ ] **Step 2: Write failing stable StreamId tests**

```csharp
[Fact]
public void Generate_IsStableAndIndependentOfMutableFields()
{
    var device = CreateDevice(Guid.Parse("50000000-0000-0000-0000-000000000001"), "原名称", "192.168.0.2");
    var channel = CreateChannel(device.Id, 1);
    var before = StreamIdGenerator.Generate(device, channel);
    device.Name = "新名称";
    device.IpAddress = "10.0.0.20";
    device.Username = "other";
    var after = StreamIdGenerator.Generate(device, channel);
    Assert.Equal("device_50000000000000000000000000000001_channel_1_main", before);
    Assert.Equal(before, after);
}

[Fact]
public void Generate_ContainsOnlyAsciiLettersDigitsAndUnderscores()
{
    var device = CreateDevice(Guid.NewGuid(), "西401溜井", "192.168.0.2");
    var streamId = StreamIdGenerator.Generate(device, CreateChannel(device.Id, 1));
    Assert.Matches("^[a-z0-9_]+$", streamId);
}
```

- [ ] **Step 3: Run the focused tests and verify RED**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~HikvisionRtspUrlBuilderTests|FullyQualifiedName~StreamIdGeneratorTests"`

Expected: compilation fails because both production types are absent.

- [ ] **Step 4: Implement the minimal URL and StreamId functions**

```csharp
public static Uri Build(CameraDevice device, CameraChannel channel)
{
    ArgumentNullException.ThrowIfNull(device);
    ArgumentNullException.ThrowIfNull(channel);
    if (channel.ChannelNo < 1) throw new ArgumentOutOfRangeException(nameof(channel.ChannelNo));
    var suffix = channel.StreamType == StreamType.Main ? 1 : 2;
    var code = checked(channel.ChannelNo * 100 + suffix);
    var credentials = $"{Uri.EscapeDataString(device.Username)}:{Uri.EscapeDataString(device.Password)}";
    return new Uri($"rtsp://{credentials}@{device.IpAddress}:{device.RtspPort}/Streaming/Channels/{code}");
}

public static string Generate(CameraDevice device, CameraChannel channel) =>
    $"device_{device.Id:N}_channel_{channel.ChannelNo}_{channel.StreamType.ToString().ToLowerInvariant()}";
```

- [ ] **Step 5: Run focused tests and commit**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~HikvisionRtspUrlBuilderTests|FullyQualifiedName~StreamIdGeneratorTests"`

Expected: all focused tests pass.

Commit: `feat: add hikvision stream identity infrastructure`

---

### Task 2: Typed ZLMediaKit Client

**Files:**
- Create: `src/VideoMonitor.Infrastructure/ZLMediaKit/ZlmOptions.cs`
- Create: `src/VideoMonitor.Infrastructure/ZLMediaKit/ZlmApiResponse.cs`
- Create: `src/VideoMonitor.Infrastructure/ZLMediaKit/ZlmStreamInfo.cs`
- Create: `src/VideoMonitor.Infrastructure/ZLMediaKit/ZlmClient.cs`
- Create: `tests/VideoMonitor.Core.Tests/Infrastructure/ZlmClientTests.cs`
- Create: `tests/VideoMonitor.Core.Tests/Infrastructure/StubHttpMessageHandler.cs`

**Interfaces:**
- Consumes: injected `HttpClient`, `ZlmOptions`, RTSP `Uri`, and StreamId.
- Produces: `CheckServerAsync`, `AddStreamProxyAsync`, `GetMediaListAsync`, and `DeleteStreamProxyAsync`, each returning `ZlmApiResponse<T>` with `IsSuccess`, `Code`, `Message`, and typed `Data`.

- [ ] **Step 1: Write a reusable queued HTTP handler and failing request test**

```csharp
var handler = new StubHttpMessageHandler("""{"code":0,"data":{"key":"proxy-key"}}""");
var client = new ZlmClient(new HttpClient(handler), Options(app: "mine", vhost: "custom"));
var result = await client.AddStreamProxyAsync("stream_1", new Uri("rtsp://user:pass@camera/live"), CancellationToken.None);
Assert.True(result.IsSuccess);
Assert.Equal("proxy-key", result.Data!.Key);
Assert.Contains("app=mine", handler.LastRequestUri!.Query);
Assert.Contains("vhost=custom", handler.LastRequestUri.Query);
Assert.Contains("rtp_type=0", handler.LastRequestUri.Query);
```

- [ ] **Step 2: Write failing response and error propagation tests**

```csharp
[Fact]
public async Task AddStreamProxy_PropagatesZlmError()
{
    var handler = new StubHttpMessageHandler("""{"code":-1,"msg":"proxy failed"}""");
    var result = await CreateClient(handler).AddStreamProxyAsync("stream_1", CameraUri(), CancellationToken.None);
    Assert.False(result.IsSuccess);
    Assert.Equal(-1, result.Code);
    Assert.Equal("proxy failed", result.Message);
}

[Fact]
public async Task GetMediaList_ParsesMatchingStream()
{
    var json = """{"code":0,"data":[{"schema":"rtsp","vhost":"__defaultVhost__","app":"live","stream":"stream_1"}]}""";
    var result = await CreateClient(new StubHttpMessageHandler(json)).GetMediaListAsync("stream_1", CancellationToken.None);
    Assert.Equal("stream_1", Assert.Single(result.Data!).Stream);
}
```

- [ ] **Step 3: Run focused tests and verify RED**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~ZlmClientTests`

Expected: compilation fails because the ZLM client types are absent.

- [ ] **Step 4: Implement URL-encoded GET requests and typed JSON parsing**

Implement one private request method that:

```csharp
private async Task<ZlmApiResponse<T>> GetAsync<T>(string endpoint, IReadOnlyDictionary<string, string?> values, CancellationToken token)
```

It must prepend `secret`, URL-encode every key/value, distinguish non-success HTTP status, parse ZLM `code`/`msg`, return typed `data`, and sanitize exception messages without including the requested URL. `AddStreamProxyAsync` must send `vhost`, `app`, `stream`, `url`, `rtp_type=0`, `timeout_sec=5`, and `retry_count=1`.

- [ ] **Step 5: Run focused tests and commit**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~ZlmClientTests`

Expected: all focused tests pass.

Commit: `feat: add typed zlmediakit client`

---

### Task 3: Local Configuration Without Committed Secrets

**Files:**
- Modify: `.gitignore`
- Create: `src/VideoMonitor.Wpf/appsettings.example.json`
- Create: `src/VideoMonitor.Wpf/local-device.example.json`
- Create: `src/VideoMonitor.Wpf/Configuration/SingleCameraTestOptions.cs`
- Create: `src/VideoMonitor.Wpf/Configuration/LocalDeviceOptions.cs`
- Create: `src/VideoMonitor.Wpf/Configuration/LocalConfigurationLoader.cs`
- Modify: `src/VideoMonitor.Wpf/VideoMonitor.Wpf.csproj`
- Create: `tests/VideoMonitor.Core.Tests/Configuration/LocalConfigurationLoaderTests.cs`

**Interfaces:**
- Consumes: JSON files beside the executable.
- Produces: `LocalConfigurationLoader.Load(string baseDirectory) : LocalPlaybackConfiguration` containing validated `ZlmOptions`, `SingleCameraTestOptions`, and `LocalDeviceOptions`.

- [ ] **Step 1: Write failing configuration tests**

```csharp
[Fact]
public void Load_WhenDisabled_DoesNotRequireSensitiveFiles()
{
    using var directory = new TemporaryDirectory();
    directory.Write("appsettings.Development.json", """{"SingleCameraTest":{"Enabled":false}}""");
    var config = LocalConfigurationLoader.Load(directory.Path);
    Assert.False(config.SingleCameraTest.Enabled);
}

[Fact]
public void Load_WhenEnabled_ParsesDiscreteCameraFields()
{
    using var directory = ValidConfigurationDirectory();
    var config = LocalConfigurationLoader.Load(directory.Path);
    Assert.Equal("192.168.0.2", config.Device.IpAddress);
    Assert.Equal("camera001", config.Device.LocalIdentifier);
    Assert.DoesNotContain("SourceUrl", directory.Read("local-device.json"));
}
```

- [ ] **Step 2: Run focused tests and verify RED**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~LocalConfigurationLoaderTests`

Expected: compilation fails because configuration types are absent.

- [ ] **Step 3: Implement minimal System.Text.Json loading and validation**

Load `appsettings.Development.json` first. When `SingleCameraTest.Enabled` is true, require non-empty ZLM BaseUrl/Secret/RtspHost and local-device IP/username/password, valid ports, and channel number greater than zero. Error messages may identify a missing field but must never include field values for Secret or Password.

- [ ] **Step 4: Add safe example files and ignore real files**

Add exact ignore entries:

```gitignore
src/VideoMonitor.Wpf/appsettings.Development.json
src/VideoMonitor.Wpf/local-device.json
```

Example JSON contains only documentation addresses and placeholders such as `CHANGE_ME`; the project copies the two real files to output only when they exist.

- [ ] **Step 5: Run focused tests, inspect Git, and commit**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~LocalConfigurationLoaderTests`

Run: `git status --short --ignored`

Expected: tests pass; real local JSON files are ignored and example files are tracked.

Commit: `feat: add secure local playback configuration`

---

### Task 4: Playback Source Provider and Proxy Ownership

**Files:**
- Create: `src/VideoMonitor.Wpf/Playback/PlaybackSource.cs`
- Create: `src/VideoMonitor.Wpf/Playback/PlaybackSourceException.cs`
- Create: `src/VideoMonitor.Wpf/Playback/IPlaybackSourceProvider.cs`
- Create: `src/VideoMonitor.Wpf/Playback/LocalZlmPlaybackSourceProvider.cs`
- Create: `tests/VideoMonitor.Core.Tests/Playback/LocalZlmPlaybackSourceProviderTests.cs`

**Interfaces:**
- Consumes: `CameraDevice`, `CameraChannel`, `LocalDeviceOptions`, `ZlmClient`, and `ZlmOptions`.
- Produces: `PrepareAsync(CameraDevice, CameraChannel, CancellationToken) : Task<PlaybackSource>` and `ReleaseAsync(PlaybackSource, CancellationToken) : Task`.

- [ ] **Step 1: Write failing existing-stream ownership test**

```csharp
[Fact]
public async Task Prepare_WhenStreamAlreadyExists_ReusesWithoutOwnership()
{
    var handler = Responses(MediaListWith(TargetStreamId));
    var provider = CreateProvider(handler);
    var source = await provider.PrepareAsync(Device(), Channel(), CancellationToken.None);
    Assert.False(source.OwnsProxy);
    Assert.Null(source.ProxyKey);
    Assert.Equal(TargetStreamId, source.StreamId);
    await provider.ReleaseAsync(source, CancellationToken.None);
    Assert.DoesNotContain(handler.Requests, request => request.AbsolutePath.EndsWith("/delStreamProxy"));
}
```

- [ ] **Step 2: Write failing created-proxy lifecycle test**

```csharp
[Fact]
public async Task Prepare_WhenStreamMissing_OwnsReturnedProxyKeyAndReleasesIt()
{
    var handler = Responses(EmptyMediaList(), AddProxy("owned-key"), MediaListWith(TargetStreamId), DeleteSucceeded());
    var provider = CreateProvider(handler);
    var source = await provider.PrepareAsync(Device(), Channel(), CancellationToken.None);
    Assert.True(source.OwnsProxy);
    Assert.Equal("owned-key", source.ProxyKey);
    await provider.ReleaseAsync(source, CancellationToken.None);
    Assert.Contains(handler.Requests, request => request.Query.Contains("key=owned-key"));
}
```

- [ ] **Step 3: Write failing stage-specific error tests**

Test ZLM connection failure, addStreamProxy API failure, and bounded MediaList timeout. Assert `PlaybackSourceException.Stage` and public Chinese message, and assert that no exception text contains the real password.

- [ ] **Step 4: Run focused tests and verify RED**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~LocalZlmPlaybackSourceProviderTests`

Expected: compilation fails because Provider types are absent.

- [ ] **Step 5: Implement local overlay, reuse, create, poll, and release**

`PrepareAsync` clones the selected device/channel, applies discrete local fields, generates StreamId from immutable DeviceId plus ChannelNo, builds Hikvision RTSP, checks ZLM, reuses an existing matching stream, or creates and polls it. The resulting `PlaybackUrl` is `rtsp://{RtspHost}:{RtspPort}/{App}/{StreamId}`. Use a finite timeout and cancellable short delays. `ReleaseAsync` calls delete only when `OwnsProxy` is true and `ProxyKey` is non-empty.

- [ ] **Step 6: Run focused tests and commit**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~LocalZlmPlaybackSourceProviderTests`

Expected: all focused tests pass.

Commit: `feat: prepare single camera playback through zlm`

---

### Task 5: LibVLC Playback Session and Coordinator

**Files:**
- Modify: `src/VideoMonitor.Wpf/VideoMonitor.Wpf.csproj`
- Create: `src/VideoMonitor.Wpf/Playback/PlaybackState.cs`
- Create: `src/VideoMonitor.Wpf/Playback/PlaybackSession.cs`
- Create: `src/VideoMonitor.Wpf/Playback/VlcPlaybackService.cs`
- Create: `src/VideoMonitor.Wpf/Playback/SingleCameraPlaybackCoordinator.cs`
- Modify: `src/VideoMonitor.Wpf/ViewModels/VideoTileViewModel.cs`
- Create: `tests/VideoMonitor.Core.Tests/Playback/SingleCameraPlaybackCoordinatorTests.cs`

**Interfaces:**
- Consumes: `IPlaybackSourceProvider`, one application-scoped `VlcPlaybackService`, and upper-left `VideoTileViewModel`.
- Produces: a long-lived `PlaybackSession` with `CameraChannelId`, `StreamId`, `PlaybackUrl`, `ProxyKey`, `OwnsProxy`, `Media`, and `MediaPlayer`.

- [ ] **Step 1: Add exact stable packages**

```xml
<PackageReference Include="LibVLCSharp.WPF" Version="3.10.1" />
<PackageReference Include="VideoLAN.LibVLC.Windows" Version="3.0.23.1" />
```

- [ ] **Step 2: Write failing ViewModel state tests**

```csharp
[Fact]
public void ShowError_ExposesSpecificTitleAndDetail()
{
    var tile = new VideoTileViewModel();
    tile.ShowError("拉流失败", "摄像头RTSP连接超时");
    Assert.Equal(PlaybackState.Error, tile.PlaybackState);
    Assert.Equal("拉流失败", tile.PlaybackErrorTitle);
    Assert.Equal("摄像头RTSP连接超时", tile.PlaybackErrorDetail);
}
```

- [ ] **Step 3: Write failing coordinator release test with fakes**

Use a fake `IPlaybackSourceProvider` returning a `PlaybackSource`; verify `StartAsync` moves Placeholder → Loading → Playing and `DisposeAsync` calls `ReleaseAsync` exactly once. Introduce a narrow internal `IPlaybackEngine` implemented by `VlcPlaybackService` so the unit test does not initialize native VLC.

- [ ] **Step 4: Run focused tests and verify RED**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SingleCameraPlaybackCoordinatorTests|FullyQualifiedName~VideoTileViewModel"`

Expected: compilation fails because playback types and state methods are absent.

- [ ] **Step 5: Implement shared LibVLC, per-session MediaPlayer, and coordinator**

`VlcPlaybackService` calls `Core.Initialize()` once in its constructor, creates one shared `LibVLC`, creates one `Media` and `MediaPlayer` per requested source, and disposes them in session order. `SingleCameraPlaybackCoordinator` never references `ZlmClient`; it updates the tile state, stores the current session, maps Provider errors to Error state, maps VLC start failure to “LibVLC播放失败”, and releases Provider resources on shutdown.

- [ ] **Step 6: Run focused tests and commit**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~SingleCameraPlaybackCoordinatorTests|FullyQualifiedName~VideoTileViewModel"`

Expected: all focused tests pass without loading a native player in tests.

Commit: `feat: add persistent vlc playback session`

---

### Task 6: VideoTile Playback States Without Recreating Controls

**Files:**
- Modify: `src/VideoMonitor.Wpf/Controls/VideoTile.xaml`
- Modify: `src/VideoMonitor.Wpf/Controls/VideoTile.xaml.cs`
- Verify unchanged behavior in: `src/VideoMonitor.Wpf/Views/Pages/MonitorView.xaml.cs`

**Interfaces:**
- Consumes: `VideoTileViewModel.PlaybackState`, `PlaybackSession.MediaPlayer`, `PlaybackErrorTitle`, and `PlaybackErrorDetail`.
- Produces: Placeholder, Loading, Playing, and Error visual states inside the existing `VideoTile` instance.

- [ ] **Step 1: Add a UI structure test that fails before XAML changes**

Add an XML-based test that loads `Controls/VideoTile.xaml` as text/XML and asserts it contains one `vlc:VideoView`, bindings to `PlaybackSession.MediaPlayer` and `PlaybackState`, and state labels “正在连接视频” and “拉流失败”.

- [ ] **Step 2: Run the focused structure test and verify RED**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~VideoTilePlaybackStructureTests`

Expected: fails because `VideoView` and state bindings are absent.

- [ ] **Step 3: Modify only the video body of VideoTile**

Keep the existing outer card and header. Add `xmlns:vlc="clr-namespace:LibVLCSharp.WPF;assembly=LibVLCSharp.WPF"`; place one `VideoView` in the body and bind its MediaPlayer. Use DataTriggers for four states. Retain overlays inside `VideoView.Content` when stable; keep title outside the native video region. Do not use `AllowsTransparency`, another Window, or a new VideoTile.

- [ ] **Step 4: Run structure and existing UI logic tests**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter "FullyQualifiedName~VideoTilePlaybackStructureTests|FullyQualifiedName~MonitorViewModelTests"`

Expected: focused tests pass; existing single-tile mode tests remain green.

- [ ] **Step 5: Commit**

Commit: `feat: render real playback state in video tile`

---

### Task 7: Application Composition and Runtime Cleanup

**Files:**
- Modify: `src/VideoMonitor.Wpf/App.xaml.cs`
- Modify: `src/VideoMonitor.Wpf/MainWindow.xaml.cs` only if a read-only accessor to the existing Monitor view is needed
- Create locally but do not track: `src/VideoMonitor.Wpf/appsettings.Development.json`
- Create locally but do not track: `src/VideoMonitor.Wpf/local-device.json`
- Create: `tests/VideoMonitor.Core.Tests/Playback/PlaybackCompositionTests.cs`

**Interfaces:**
- Consumes: local configuration, existing MockDeviceData device with immutable ID, `LocalZlmPlaybackSourceProvider`, `VlcPlaybackService`, and upper-left `MainTiles[0]`.
- Produces: one application-lifetime coordinator started only when `EnableSingleCameraTest=true` and safely disposed during real application shutdown.

- [ ] **Step 1: Write a failing composition test**

Extract a focused `SingleCameraPlaybackComposition.SelectDevice(MockDeviceDataSet, LocalDeviceOptions)` pure method. Test that it selects only “西401溜井 · 通道1”, returns its sole channel, and never selects Camera02–06.

- [ ] **Step 2: Run focused test and verify RED**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~PlaybackCompositionTests`

Expected: compilation fails because composition helper is absent.

- [ ] **Step 3: Implement application startup with one coordinator**

At startup, load local configuration. If enabled, initialize the monitor with “西401溜井” as its chute group, compose `ZlmClient` → `LocalZlmPlaybackSourceProvider` → shared `VlcPlaybackService` → `SingleCameraPlaybackCoordinator`, show the existing MainWindow, then start only `monitorViewModel.MainTiles[0]`. Keep the coordinator in an App field.

- [ ] **Step 4: Implement deterministic shutdown**

On real application exit, cancel pending startup, await coordinator release, delete only an owned proxy, dispose the session/MediaPlayer/Media/shared LibVLC, then allow the existing main/secondary window shutdown flow to complete. Navigation, full-screen, tile double-click, sidebar, and detail panel code must not call the coordinator.

- [ ] **Step 5: Create ignored local configuration from the confirmed Camera01 and ZLM values**

Use discrete fields only. Verify `git check-ignore` reports both files and inspect `git diff --cached` to ensure no Secret or Password is staged.

- [ ] **Step 6: Run focused tests and commit tracked composition code**

Run: `dotnet test tests/VideoMonitor.Core.Tests/VideoMonitor.Core.Tests.csproj --filter FullyQualifiedName~PlaybackCompositionTests`

Expected: focused tests pass.

Commit: `feat: compose single camera playback at startup`

---

### Task 8: Full Verification and Real Video Evidence

**Files:**
- Create: `artifacts/single-camera-playback/main-playing.png`
- Create: `artifacts/single-camera-playback/main-single-tile.png`
- Create: `artifacts/single-camera-playback/main-after-navigation.png`
- Modify tracked files only if verification exposes a proven defect.

**Interfaces:**
- Consumes: the complete one-camera path and confirmed local network endpoints.
- Produces: build/test evidence, ZLM proxy/media evidence, three screenshots, startup timing, and a final local commit.

- [ ] **Step 1: Run clean solution verification serially**

Run: `dotnet build VideoMonitor.sln -c Debug -m:1`

Run: `dotnet test VideoMonitor.sln -c Debug --no-build`

Expected: 0 build errors and all tests pass.

- [ ] **Step 2: Verify ZLM API without printing credentials**

Load the ignored local JSON into process memory, call `CheckServerAsync`, then use application APIs to create/reuse the stream. Record only HTTP status, ZLM code/message, generated StreamId, ownership flag, and proxy key redacted to a short suffix.

- [ ] **Step 3: Verify Camera01 → ZLM → WPF**

Launch the Debug application. Confirm MediaList contains the generated StreamId under configured Vhost/App and the upper-left tile reaches Playing. Measure elapsed time from Prepare start to LibVLC Playing event as startup latency; label it startup latency rather than claiming glass-to-glass latency.

- [ ] **Step 4: Verify persistent UI behavior and capture screenshots**

Capture the normal 2×2 screen, double-click upper-left and capture the single-tile screen, restore, navigate to Device Management and back, and capture the returned Monitor page. Confirm the same `PlaybackSession` instance and proxy ownership/key remain unchanged across all operations.

- [ ] **Step 5: Verify failure display without hanging**

Temporarily use an invalid camera endpoint only in ignored local configuration, launch once, confirm bounded failure and specific Error state, then restore valid local configuration. Do not commit the invalid or valid secret-bearing files.

- [ ] **Step 6: Run final build/test, inspect secrets, and commit**

Run: `dotnet build VideoMonitor.sln -c Release -m:1`

Run: `dotnet test VideoMonitor.sln -c Release --no-build`

Run: `git diff --check`

Run a tracked-file scan for the confirmed ZLM Secret, camera password, and raw credential-bearing RTSP URL; expected result is no matches.

Commit any final tracked verification fixes with: `fix: complete single camera playback validation`

Do not merge or push.
