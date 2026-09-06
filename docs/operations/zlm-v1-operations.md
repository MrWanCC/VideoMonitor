# VideoMonitor V1 ZLM Operations

This document describes the supported Windows-first operating procedure for the V1 ZLMediaKit edition. It does not define an installer, automatic certificate provisioning, or automatic ZLMediaKit lifecycle management.

## 1. Deployment model

- `VideoMonitor.Server` runs as the Windows Service named `VideoMonitor.Server`.
- ZLMediaKit runs as a separate Windows process or service.
- WPF runs as a desktop client.
- Server owns catalog, credentials, settings, and media-control APIs.
- Server does not proxy or forward media bytes.

## 2. Startup order

Start components in this order:

1. ZLMediaKit
2. VideoMonitor.Server
3. WPF Client

After Server startup, check readiness before using catalog or media operations.

## 3. Shutdown order

Stop components in this order:

1. WPF Client
2. VideoMonitor.Server
3. ZLMediaKit

Allow the WPF client to close normally so playback diagnostics can flush before the process exits.

## 4. Server health

`GET /health/live` reports that the Server process is alive. It is a process-liveness check and does not verify ZLMediaKit.

`GET /health/ready` reports Server initialization readiness, including database and secret-protection readiness. A not-ready response is returned until those Server prerequisites are ready. It is not a ZLMediaKit health check.

## 5. Media health

`GET /api/v1/media/diagnostics` reports safe media-runtime diagnostics, including ZLMediaKit/media runtime state, active streams, viewer state, and fault state where available.

Use this endpoint after Server readiness succeeds when the WPF client reports a media problem. Diagnostics must not expose camera credentials, management secrets, playback authorization material, or credential-bearing media URLs.

## 6. Persistent paths

The default Server root is:

```text
%ProgramData%\VideoMonitor\Server\
```

The Server storage layout includes:

```text
data\
security\
backups\
logs\
```

Important paths:

```text
Database:    %ProgramData%\VideoMonitor\Server\data\videomonitor.db
Master key:  %ProgramData%\VideoMonitor\Server\security\master-key.protected
```

The actual root can be changed by the Server storage configuration. When it is changed, use the configured root and retain the same `data`, `security`, `backups`, and `logs` layout.

## 7. Playback diagnostics logs

WPF playback diagnostics are written to:

```text
%ProgramData%\VideoMonitor\Logs\playback-diagnostics-*.log
```

These are client playback diagnostics, not Server ASP.NET file logs. Review them only through approved handling procedures and do not redistribute files containing sensitive operational data.

## 8. Server logs

VideoMonitor.Server currently does not provide a formal built-in file-logging persistence strategy. When hosted as a Windows Service, inspect errors through the Windows Service, Event, or process-hosting environment available in the deployment. Do not assume that a file named `server.log` exists under the Server data directory.

## 9. Backup

`SqliteBackupService` exists internally and creates a consistent database backup with a manifest under the configured backups directory. V1 currently has no formal operator-facing online backup command, API, or UI entry point.

Recommended V1 same-host cold backup:

1. Close WPF.
2. Stop `VideoMonitor.Server`.
3. Back up the entire `%ProgramData%\VideoMonitor\Server\` directory, including `data`, `security`, and existing backup metadata.
4. Start `VideoMonitor.Server`.
5. Check `GET /health/ready`.

ZLMediaKit does not need to be stopped to back up the Server database directory.

## 10. Restore

For same-host recovery:

1. Stop `VideoMonitor.Server`.
2. Back up the current Server directory before replacing anything.
3. Restore the complete Server directory.
4. Confirm that the restored directory contains both the database and `security\master-key.protected`.
5. Start `VideoMonitor.Server`.
6. Check `/health/live` and `/health/ready`.
7. Verify catalog and media settings through the normal Server/WPF flow.

WARNING: `master-key.protected` is protected with Windows DPAPI `LocalMachine` scope. Copying this file to another Windows machine is not equivalent to a portable cross-machine restore.

Cross-machine disaster recovery is **not delivered in V1**. Do not use an unverified DPAPI bypass. Replacement-host recovery is deferred.

## 11. Common fault flow

When WPF cannot connect to Server:

1. Check `/health/live`.
2. Check `/health/ready`.
3. If readiness fails, investigate Server initialization, database access, and secret protection.

When Server is ready but media is abnormal:

1. Query `/api/v1/media/diagnostics`.
2. Check the affected stream's safe runtime state, viewer state, and fault state.
3. If ZLMediaKit is unavailable, check the ZLMediaKit process and its management API.
4. Treat unavailable, stale, or faulted runtime evidence as a media-runtime issue rather than catalog proof.

For a single-camera issue, use the exact `DeviceId`, `ChannelId`, and `StreamType` in safe diagnostics. Retry only when the stream is faulted and the normal retry path is available.

## 12. Known media limitation

Under current field conditions, Hikvision H.265 through the ZLMediaKit RTSP proxy and LibVLC path has shown approximately 2–3 seconds of additional latency with occasional stutter or catch-up. V1 does not continue media-parameter tuning for this limitation. WebRTC and MediaMTX remain future directions only.

## 13. Field validation pending

The V1 field seal remains pending validation of:

- 5 physical cameras;
- 7 formal playback tiles: 4 main and 3 secondary;
- 2 duplicated physical sources;
- real playback and runtime status;
- ZLMediaKit restart/recovery;
- camera or network fault and retry/recovery;
- the known latency/stutter behavior.
