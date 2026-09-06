# VideoMonitor V1 — ZLM Edition

VideoMonitor V1 is a Windows-first central video-monitoring system built with C#, .NET 8, WPF, SQLite, ZLMediaKit, and LibVLCSharp.

**Current status:** V1 planned software scope complete; final field validation pending.

## Current V1 architecture

```text
Camera RTSP
    -> ZLMediaKit
    -> WPF LibVLC playback

WPF
    -> VideoMonitor.Server
    -> ZLMediaKit
```

VideoMonitor.Server owns the catalog and media control APIs. It does not proxy or forward media bytes.

The V1 implementation includes:

- central SQLite catalog;
- device and group management;
- server-side protection of camera credentials;
- ZLM StreamManager and formal playback ensure;
- playback authorization tickets;
- four main and three secondary WPF playback tiles;
- playback diagnostics;
- real runtime camera-status projection;
- reconnect and reconciliation;
- asynchronous playback cleanup on shutdown and stop paths.

Development-only local JSON compatibility code is not the formal production catalog authority. Production catalog reads and writes go through VideoMonitor.Server.

## Current V1 field acceptance topology

This topology describes the current field validation target, not a system capacity limit:

```text
Physical cameras:             5
Formal playback tiles:        7
Main tiles:                   4
Secondary tiles:              3
Duplicated physical sources:  2
```

Seven formal tiles do not mean seven independent cameras. Two physical sources are intentionally represented in more than one formal playback assignment.

## V1 known media limitation

Under the current field conditions, the following path has been observed to add approximately 2–3 seconds of latency and to occasionally stutter or catch up:

```text
Hikvision H.265 -> ZLMediaKit RTSP proxy -> LibVLC
```

V1 does not continue media-parameter tuning for this limitation. WebRTC and MediaMTX are future directions only; they are not an immediate V1 fix and are not part of the current supported V1 path.

## Deployment model

- `VideoMonitor.Server` runs as the Windows Service named `VideoMonitor.Server`.
- ZLMediaKit runs as a separate Windows process or service.
- The WPF client is a desktop application.
- Server persistent data is stored under `%ProgramData%\VideoMonitor\Server\` by default.
- ZLMediaKit is a separate media-plane component; Server does not carry video bytes.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for ownership and runtime boundaries and [docs/operations/zlm-v1-operations.md](docs/operations/zlm-v1-operations.md) for startup, health, backup, restore, and fault handling.

## Screenshots

### Main monitor

![Main monitor](artifacts/screenshots/wpf-video-monitor-ui-scale-pass2-main.png)

### Secondary monitor

![Secondary monitor](artifacts/screenshots/wpf-video-monitor-ui-scale-pass2-secondary.png)

## Technology

- C#
- .NET SDK 8.0.424
- `net8.0-windows`
- WPF
- CommunityToolkit.Mvvm 8.4.2
- SQLite
- xUnit
- ZLMediaKit
- LibVLCSharp

## Repository layout

```text
VideoMonitor.sln
├─ src/
│  ├─ VideoMonitor.Core/          domain models and interfaces
│  ├─ VideoMonitor.Infrastructure/SQLite, secrets, backup, ZLM integration
│  ├─ VideoMonitor.Server/        central catalog and media APIs
│  └─ VideoMonitor.Wpf/           desktop UI and playback
└─ tests/
   ├─ VideoMonitor.Core.Tests/
   └─ VideoMonitor.Server.Tests/
```

## Build and test

```powershell
dotnet restore
dotnet build .\VideoMonitor.sln
dotnet test .\VideoMonitor.sln
```

The repository's `global.json` pins the .NET SDK family. Windows deployment also requires ZLMediaKit and the LibVLC runtime used by the WPF client.

## Final field validation

Software scope is complete, but the V1 seal still requires field validation of the five physical-camera / seven-tile topology, real playback, runtime status, ZLM restart/recovery, camera or network faults, retry/recovery, and the known latency/stutter limitation.
