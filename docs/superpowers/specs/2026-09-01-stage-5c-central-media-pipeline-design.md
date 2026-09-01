# Stage 5C — Central Media Pipeline & Stream Management Design

Status: Approved design for written-spec review; implementation not started

## 1. Purpose and scope

Stage 5C upgrades the Stage 5B-2 central Catalog client into a central media
playback architecture. The formal media path is:

```text
WPF
  -> VideoMonitor.Server
  -> StreamManager
  -> ZLMediaKit
  -> WPF LibVLC
```

The Server is the only component that resolves persisted camera credentials
and controls ZLMediaKit. The formal WPF client does not receive camera
passwords, password ciphertext, or the ZLMediaKit administration secret, and
does not call ZLMediaKit directly.

Stage 5B-2 `SingleCameraTest.Enabled=true` remains an explicit local
compatibility path. It is not promoted into the formal central architecture.

This document defines the architecture, ownership boundaries, runtime
semantics, security rules, and acceptance goals. It is not a file-by-file
implementation plan.

## 2. Architectural boundaries

The following boundaries are mandatory:

- Server SQLite remains the authoritative Catalog and media configuration
  store.
- Formal WPF writes go through the Server API and are reflected in the client
  cache only after confirmed Server success.
- Formal WPF consumes password-safe Catalog read models and ID-based playback
  contracts.
- Camera credentials and ZLM administration credentials remain inside the
  Server boundary.
- Runtime media state is not persisted as Catalog configuration.
- Catalog presence never means that a camera or stream is Online.
- Names are display data only; identity is based on GUIDs and stable media
  keys.

The existing WPF `IPlaybackSourceProvider`, `PlaybackSource`,
`LocalZlmPlaybackSourceProvider`, and `ZlmClient` are current local playback
building blocks. The local provider remains limited to the compatibility
composition. Formal central playback uses a Server-backed abstraction rather
than passing `Core.CameraDevice` into monitor code.

## 3. Deployment model

The default deployment places these components on one site Server PC:

- `VideoMonitor.Server` as an independent Windows Service;
- ZLMediaKit as an independent process or Service;
- SQLite for Server configuration and Catalog data;
- Server configuration and operational logs.

ZLMediaKit is not embedded in the `VideoMonitor.Server` process.

The default same-host control path is:

```text
VideoMonitor.Server -> http://127.0.0.1:<api-port> -> ZLMediaKit API
```

The design must also support placing ZLMediaKit on another machine. Two
addresses therefore have distinct meanings:

- `ZlmApiBaseUrl`: the address used by Server to call the ZLMediaKit API;
- `PlaybackBaseUrl`: the address used by WPF to play a safe RTSP stream.

The playback URL returned to WPF must never be built from the Server's local
ZLM address merely because both services happen to share a host. For example,
`rtsp://127.0.0.1/...` is not a valid default for a remote WPF client.

Catalog health and media health are separate. A stopped or unavailable ZLM
must not make Catalog GET or Device Management unusable, and must not make
the Server Catalog `/health/ready` endpoint fail as a whole. Media health is
reported through an independent media-health view or endpoint.

## 4. Configuration authority

Formal media configuration is authoritative in Server SQLite, not in WPF
JSON and not in a normal WPF `appsettings.json` secret. The configuration
model includes at least:

- `ZlmApiBaseUrl`;
- `PlaybackBaseUrl`;
- `Vhost`;
- `FormalApp`;
- `TestApp`;
- protected `ZlmSecretCiphertext`;
- `NoReaderGraceSeconds`;
- configuration `Revision`.

The initial defaults are:

```text
FormalApp = videomonitor
TestApp = videomonitor-test
NoReaderGraceSeconds = 30
```

The Server uses the existing machine-level secret-protection approach for
the ZLM secret. Plaintext exists only transiently while processing a test or
save request. GET responses expose `HasSecret`, never the secret or its
ciphertext. Logs and telemetry never contain the secret.

The WPF settings UI separates Test from Save:

- Test probes a candidate configuration without making it authoritative.
- Save commits a validated configuration and increments its revision.

Testing a media configuration checks ZLM reachability, secret validity,
`getServerConfig`, `PlaybackBaseUrl` syntax, and, where required, reachability
of the RTSP playback endpoint. It does not arbitrarily pull a camera. Camera
testing is the Device Test Stream feature.

## 5. Media data flow and health

The formal flow for an ordinary monitor tile is:

1. WPF requests a stream using `DeviceId`, `ChannelId`, and `StreamType`.
2. Server validates the Catalog identity and resolves the camera credential
   internally.
3. StreamManager ensures the corresponding ZLM proxy and waits for actual
   media registration.
4. Server issues a short-lived playback authorization and builds a safe
   playback URL from `PlaybackBaseUrl`.
5. WPF gives that URL to LibVLC and reports playback runtime events through a
   UI-safe playback boundary.

The Catalog API remains usable when ZLM is unavailable. Media health instead
describes whether ZLM is configured, healthy, unavailable, or rejected the
configuration. These states must not be conflated with Catalog health.

## 6. On-demand streaming

Formal streaming is on demand. Server startup does not pull every configured
camera.

The first real playback requirement for a media key calls `EnsureStream`.
Additional consumers reuse the same Camera-to-ZLM upstream. When the last
real reader leaves, the Server waits through the configured no-reader grace
period before considering the owned proxy for cleanup.

An always-on policy may be considered in a later evolution, but Stage 5C does
not add an always-on policy UI or make all cameras active by default.

## 7. Stream identity

The stable business identity is:

```text
MediaStreamKey = DeviceId + ChannelId + StreamType
```

It must not be derived from Camera Name, Group Name, or a Chinese display
name. A formal stream ID is deterministic and length-bounded. One valid
representation is:

```text
vm_<DeviceId>_<ChannelId>_<main-or-sub>
```

The exact encoding may use an equivalent safe format, but the same key must
always resolve to the same formal stream identity.

Formal streams use:

```text
App = videomonitor
```

Test streams use:

```text
App = videomonitor-test
StreamId = test_<high-entropy-guid>
```

The formal and test namespaces are separate. A test stream must never replace
or alter a formal stream.

## 8. StreamManager responsibilities

Server introduces a formal StreamManager with these responsibilities:

- serialize concurrent `EnsureStream` operations per `MediaStreamKey`;
- query current ZLM state before creating a proxy;
- reuse a matching registered stream;
- call `addStreamProxy` only when no usable stream exists;
- wait for actual media registration using `getMediaList` or an equivalent
  authoritative query;
- return playback information only after the stream is Ready;
- perform bounded retry and bounded cleanup on failed setup.

For concurrent requests for one key, only one Camera-to-ZLM proxy creation
attempt is allowed. A successful `addStreamProxy` response alone is not proof
that the stream is ready.

Retries are finite and classify failure safely. An infinite retry loop is not
permitted.

## 9. Credential boundary

The public Catalog DTO remains password-safe. It must not gain `Password` or
`PasswordCiphertext` merely to support StreamManager.

Server contains an internal credential-resolution boundary. Only the internal
media component may resolve the stored Device, Channel, and camera password
and construct a credential-bearing camera RTSP URI.

That URI must never be:

- returned to WPF;
- included in ordinary logs;
- returned in an error response;
- written as plaintext telemetry.

The RTSP URI builder must encode credentials and other URI components
correctly. Automated coverage must include special characters such as `@`,
`%`, `#`, `&`, and `:` without exposing them in diagnostic output.

The formal WPF never receives a Server-resolved camera password. A user-entered
password in a Test Stream editor is a transient input supplied for that one
request; it is not placed into the Catalog cache, a password-bearing domain
object, or logs, and is cleared when the test session ends.

## 10. Runtime state model

Stage 5C does not collapse all media state into `CameraStatus Online/Offline`.
It defines separate runtime concepts.

### 10.1 MediaServerHealth

`MediaServerHealth` expresses the ZLM control-plane state:

- `Unconfigured`;
- `Healthy`;
- `Unavailable`;
- `ConfigurationError`.

### 10.2 StreamRuntimeState

`StreamRuntimeState` expresses a formal or test stream lifecycle:

- `Idle`;
- `Starting`;
- `Ready`;
- `Stopping`;
- `Faulted`.

`Idle` is not `Offline`. It means no active upstream is currently required.

### 10.3 SourceObservation

`SourceObservation` records the latest bounded observation of the camera:

- `Unknown`;
- `Reachable`;
- `ConnectFailed`;
- `AuthFailed`.

An observation may include `ObservedAtUtc`, `LastSuccessUtc`, a safe
`LastErrorCode`, and a sanitized `LastErrorMessage`. It must not contain a
credential-bearing URI or a secret.

### 10.4 ViewerCount

Viewer count is based on ZLM's actual media reader information. A WPF session
dictionary or local client count is not a substitute for the actual ZLM
reader count.

## 11. Runtime persistence and reconciliation

Runtime state is not written to Catalog SQLite. In particular, SQLite does
not persist:

- runtime proxy key truth;
- `Ready` or `Starting` state;
- ViewerCount;
- current ZLM sessions.

SQLite persists configuration. ZLMediaKit is authoritative for current media
existence and reader state. Server maintains in-memory runtime state and
reconciles it with ZLM hooks and periodic queries.

On Server restart:

1. reconnect to ZLM;
2. query actual media state;
3. classify streams using the ownership rules;
4. rebuild runtime state without blindly deleting every existing stream.

Reconciliation runs immediately at startup, immediately after media-server
recovery, and on a normal bounded periodic interval of about 30 seconds. Two
reconciliations must not overlap. When ZLM is unavailable, the Server uses
bounded backoff such as `5s -> 10s -> 30s -> 60s`; recovery returns to the
normal interval.

## 12. Ownership and safe restart adoption

Server may delete only a proxy that is demonstrably in the VideoMonitor
management domain. The presence of a stream in ZLM is not proof of ownership.

The internal media query used for this decision must retain enough evidence to
make the comparison. Depending on the ZLM API version, that evidence includes
`schema`, `vhost`, `app`, `stream`, `originType`/`originTypeStr`, `originUrl`,
`createStamp` or `aliveSecond`, and `totalReaderCount`. These are
Server-internal media fields, not public DTO fields.

The managed namespaces `videomonitor` and `videomonitor-test` are reserved for
VideoMonitor. Formal IDs are deterministic under the `vm_` namespace and test
IDs use the `test_` namespace. Server must also verify the configured vhost,
app, stream identity, and the result of a ZLM proxy/media query before marking
a stream as owned.

Streams outside the reserved namespaces are `External` and are never deleted
by VideoMonitor. A stream in a reserved namespace that cannot be verified as
created for the matching Device/Channel is `NotOwned` until a safe ownership
proof is available.

Within the current Server process, a stream becomes `OwnedCurrentProcess` only
after StreamManager itself successfully calls `addStreamProxy` and retains the
returned `ProxyKey`. Normal cleanup of that stream may use that exact key.

After restart the `ProxyKey` may be lost. Restart adoption therefore requires
all of the following proofs:

1. `vhost` equals the configured `Vhost`.
2. `app` equals the configured `FormalApp`.
3. `stream` strictly parses as one deterministic `MediaStreamKey`.
4. The parsed `DeviceId`, `ChannelId`, and `StreamType` still exist in the
   authoritative Catalog.
5. `originType`/`originTypeStr` identifies a pull/proxy-compatible source.
6. The observed source binding matches the exact camera source that Server
   would construct from the current Catalog and internally resolved
   credential.

The source comparison may use the sensitive `originUrl` internally, but its
result is reduced to a safe state such as `SourceBindingMatched=true/false`.
If any proof is unavailable or fails, the stream is `NotOwned` and is not
deleted. `app == videomonitor`, a `vm_` prefix, or any other single signal is
insufficient. Server must never infer ownership from Camera Name or Group
Name.

`originUrl` may contain a credential-bearing value such as
`rtsp://username:password@camera/...`. It is therefore secret-bearing
internal data. It may be used transiently by Server media foundation for
source-binding verification, but it must never be returned to WPF, placed in
a public media DTO or diagnostics row, written to ordinary or structured logs,
included in exception details, or emitted as plaintext telemetry. After the
comparison, only a safe result such as `SourceBindingMatched` and a sanitized
error category may remain in runtime diagnostics.

A restart-adopted stream that has passed every proof but has no original
`ProxyKey` may be cleaned up only with a ZLM-supported exact stream-close
operation such as `close_streams`. The operation must specify the complete
`schema + vhost + app + stream` identity. App-only, vhost-only, prefix-only,
and broad batch deletion are forbidden.

This rule allows a Server restart to preserve a still-live stream without
turning arbitrary external ZLM media into a deletion target.

## 13. Hooks and reconciler

ZLM hooks are low-latency notifications, not a reliable message queue. Stage
5C considers at least:

- `on_play`;
- `on_stream_changed`;
- `on_stream_none_reader`;
- the necessary media-server keepalive or availability signal.

Hook requests must validate the trusted caller or hook secret, parse only the
required fields, enqueue an internal event, and return promptly. A hook
handler must not wait synchronously for a camera, ZLM proxy, a long-held lock,
or extensive database work.

Heavy work runs in a background event processor or equivalent. The periodic
reconciler remains the eventual-consistency mechanism when a hook is dropped
or delayed.

## 14. Reader-driven no-reader lifecycle

Formal stream lifetime is driven by ZLM's actual `ReaderCount`/reader data,
not by a WPF heartbeat.

When the last reader leaves, the configured no-reader grace period begins.
After the grace period and the corresponding no-reader observation, Server
rechecks all cleanup conditions:

- the stream is VideoMonitor-owned;
- the total ZLM reader count is still zero;
- no `EnsureStream` or `Starting` critical section is active;
- the stream still satisfies the cleanup policy.

Only then may Server delete the owned proxy. Current-process ownership uses
the retained exact `ProxyKey`; restart-adopted ownership without that key uses
the exact stream-close identity described above. The default grace period is
30 seconds. A no-reader hook alone must never directly trigger deletion.

Test Stream lifecycle is separate: it is controlled by a test session and a
hard TTL rather than by the formal reader-driven policy.

## 15. Playback API

Formal central playback is ID-based. The request contains:

```text
DeviceId
ChannelId
StreamType
```

The conceptual endpoint is:

```text
POST /api/v1/playback/streams/ensure
```

Server validates the IDs, resolves the camera credential internally, calls
StreamManager, waits for Ready, issues a short-lived playback authorization,
and returns only safe playback information:

- `StreamId`;
- `PlaybackUrl`;
- `ExpiresAtUtc`;
- `RuntimeState`.

The response never contains the camera password, camera source RTSP URI, or
ZLM administration secret.

The playback URL uses the configured `PlaybackBaseUrl`, the safe stream
identity, and the authorization required by the playback boundary. It is not
a raw ZLM administrator URL.

## 16. Playback authorization

All formal central playback uses a short-lived authorization. The first
implementation uses a stateless HMAC ticket bound to the complete media
identity and containing at least:

- `Vhost`;
- `App`;
- `StreamId`;
- `ExpiresUtc`;
- a random `Nonce`.

The default TTL is 60 seconds. This is a 60-second connection authorization
window; it does not limit an already authorized playback session to 60
seconds. The first version does not bind the token to a client IP.

The playback signing key is distinct from camera passwords and the ZLM secret.
Server generates it securely, stores it with machine-level protection, and
never logs it or asks the user to configure it.

WPF cannot generate a bare formal playback URL. When ZLM `on_play` reports the
actual `vhost`, `app`, and `stream`, Server validates all three against the
ticket claims, then validates expiry and the HMAC. Any mismatch rejects the
playback request. ZLM administrator bypass parameters are not formal WPF
playback credentials.

The nonce provides entropy and domain separation; it does not make the ticket
one-time-use. Within its 60-second TTL, a valid signed ticket may be reused to
establish connections for the same bound media identity. Stage 5C does not add
a token state table.

This token is a media access ticket, not a user login system. Stage 5C does
not introduce User Login, JWT identity, or RBAC.

## 17. Formal WPF playback boundary

`LocalZlmPlaybackSourceProvider` remains limited to
`SingleCameraTest.Enabled=true`. Its direct `ZlmClient` and local catalog
dependencies are not used by formal central monitor playback.

Formal WPF uses a Server-backed playback source provider or equivalent
abstraction that accepts the stable IDs required by the playback API. It does
not reconstruct a complete `Core.CameraDevice` for central playback and does
not build a camera RTSP URL.

The existing LibVLC-based `IPlaybackEngine` remains behind a playback
boundary. The formal provider supplies a safe `PlaybackSource` or an evolved
equivalent without exposing Server credentials to ViewModels.

The local compatibility composition remains available for SingleCameraTest;
it is not removed in order to implement formal mode.

## 18. Playback runtime events

The existing playback abstraction currently has Start/Stop responsibilities.
Formal playback additionally needs a UI-safe runtime state boundary capable of
reporting:

- `Playing`;
- `Stopped`;
- `Failed`.

LibVLC `Play()` returning successfully is not proof that the stream will stay
healthy. ViewModels must not subscribe directly to a collection of LibVLC
implementation details. The playback boundary translates engine events into
stable application events and sanitized failure information.

## 19. Automatic recovery

When the user still requires a formal tile, transient failures are recovered
with bounded backoff, for example:

```text
1s -> 2s -> 5s -> 10s -> 15s -> 30s -> 30s ...
```

Each retry re-enters the formal path:

```text
WPF -> Server EnsureStream -> new short-lived token -> LibVLC reconnect
```

An expired token is never cached indefinitely for reconnect.

The recovery policy may retry transient network failure, temporary camera
unavailability, unexpected media disappearance, and temporary ZLM or Server
unavailability. It must stop automatic retries for definite authentication
failure, invalid configuration, invalid channel, or equivalent permanent
failures. Those cases wait for user Retry, a configuration change, an
explicit retest, or an equivalent corrective action.

The policy prevents camera lockout and retry storms.

## 20. Real Test Stream

Stage 5C restores the previously removed Test Stream entry as a real feature.
It proves the complete path:

```text
Camera -> ZLMediaKit -> WPF LibVLC
```

It is not a ping, a TCP-connect check, or a success-only notification.

Test Stream accepts both:

- a new Device Draft that has not been saved;
- an existing Device with unsaved edits.

For an existing Device, an empty password Draft means “use the current Server
credential for this test”; a non-empty Draft password is used only for this
test. A successful test never saves the Device automatically.

For a new Device there is no existing Server credential, so the Draft supplies
the connection values; an empty password remains a legal possible value.

Test Stream uses the isolated `videomonitor-test` app and a random test Stream
ID. Its lifecycle and cleanup cannot affect a formal stream. Test playback is
also authorized: WPF must obtain a Server-issued ticket bound to the
configured `Vhost`, `TestApp`, and exact `test_<valid-high-entropy-guid>`
stream ID. A formal ticket cannot authorize `TestApp`, and a test ticket
cannot authorize `FormalApp`; WPF may not play a random test ID directly.

Test Session TTL and playback-ticket TTL are independent. A Test Session has a
hard two-minute lifetime, while a playback ticket has a 60-second connection
authorization window. The ticket window does not extend the Test Session and
the Test Session does not turn the ticket into a long-lived credential.

## 21. Test Preview lifecycle

After Test Stream succeeds, WPF displays a real temporary video preview, not
only a “connected” message. The Device editor state is:

```text
Test Stream -> Loading -> temporary video preview -> state -> Stop Test
```

Cleanup occurs when the user stops the test, the editor closes, device
selection changes, the application/session ends, or the hard TTL expires. The
hard TTL is two minutes.

If WPF crashes, the in-memory Test Session and its expiry may be lost on a
Server restart. Reconciliation can identify a restart orphan only when all of
these conditions hold: `app` equals the configured `TestApp`; `stream` exactly
matches `test_<valid-high-entropy-guid>`; the origin type is pull/proxy
compatible; and `createStamp` or `aliveSecond` proves that the stream has
exceeded the two-minute Test TTL. The TestApp namespace is reserved for
VideoMonitor. A `test_` prefix in another app is not sufficient. When any
evidence is missing, the stream is `NotOwned` and is not deleted.

An expired, proven managed test orphan may be closed with the exact
`schema + vhost + app + stream` identity. Test lifecycle is not implemented as
a simplified copy of formal reader-driven lifecycle.

## 22. Test error taxonomy

The media boundary uses only error categories that can be supported by real
evidence. The minimum categories are:

- `MediaServerUnavailable`;
- `AuthFailed`, only when authentication failure is established;
- `ConnectFailed`;
- `MediaRegistrationTimeout`;
- `PlaybackPreparationFailed`.

When a more specific category cannot be proven, the system returns a broader
safe category. No error response exposes a password, secret, or
credential-bearing camera RTSP URI.

## 23. Minimal media diagnostics

Stage 5C includes a lightweight media diagnostics view for field operations,
not a full media analytics platform.

The view reports ZLM health and summaries of:

- active streams;
- viewer connections;
- faulted streams.

Each stream row may show:

- Camera display name;
- Channel;
- StreamType;
- StreamRuntimeState;
- actual ViewerCount;
- Ownership;
- StartedAt;
- SourceObservation;
- LastSuccess;
- a sanitized last error message.

The first operations are Refresh and Retry faulted stream. Destructive “Stop
All” controls are not primary operations.

Stage 5C does not add historical charts, bitrate analytics, recorder
management, or an alarm platform.

## 24. Acceptance and hardware goals

The final Stage 5C acceptance must verify:

1. Formal central WPF plays a real camera stream.
2. WPF does not expose or receive a Server-resolved camera password.
3. WPF does not hold the ZLM administration secret.
4. Two consumers of one key create only one Camera-to-ZLM upstream.
5. ViewerCount follows actual ZLM readers.
6. An owned proxy is released about 30 seconds after the last reader leaves.
7. A temporary camera network failure recovers formal playback.
8. A wrong password does not create infinite retries.
9. Catalog remains usable while ZLM is stopped.
10. Server reconciles automatically when ZLM recovers.
11. Server restart with ZLM still alive does not blindly clear media state.
12. Test Stream displays real Camera-to-ZLM-to-WPF video.
13. Test Stream cleanup works for normal and expired sessions.
14. Test Stream does not affect a formal stream.
15. Credential URI special-character boundaries are handled safely.
16. Logs contain no camera password, ZLM secret, or credential-bearing RTSP
    URL.

## 25. Stage 5C task decomposition

The subsequent implementation work is divided into eight architectural
responsibilities. This decomposition records dependencies without prescribing
files or implementation steps.

### Task 1 — Media Settings and Secret Storage

Owns authoritative media configuration, protected ZLM secret storage,
validation, revision protection, and safe Test-versus-Save semantics.

### Task 2 — Server Media Foundation

Owns ZLM control-plane integration, media health, safe URI construction, and
the Server-side media boundaries required by later tasks.

### Task 3 — StreamManager and Runtime Reconciliation

Owns per-key stream serialization, on-demand proxy reuse/creation,
registration confirmation, ownership, hooks, reconciliation, and formal
reader-driven lifecycle.

### Task 4 — Playback Authorization

Owns playback signing keys, short-lived HMAC authorization, ZLM play
validation, and safe playback URL issuance.

### Task 5 — Real Test Stream

Owns draft-aware Test Stream requests, isolated test namespace, real preview,
session/TTL cleanup, and test-specific errors.

### Task 6 — Formal Central Playback

Owns ID-based WPF playback requests, the Server-backed provider, LibVLC event
translation, and bounded automatic recovery.

### Task 7 — Minimal Media Diagnostics

Owns the lightweight media-health and runtime-stream diagnostics view,
including safe retry operations.

### Task 8 — Hardware Acceptance

Owns field validation of the complete formal and test media paths against the
acceptance goals in this document.

## 26. Explicit non-goals

Stage 5C does not implement:

- recording management;
- playback/history recording;
- WebRTC migration;
- HLS management UI;
- Cloud Control Plane;
- multi-ZLM clustering;
- load balancing;
- an RBAC or user-authentication system;
- an alarm center;
- historical bitrate charts;
- full-time probing of every idle camera;
- automatic ZLM failover clustering;
- general VMS platform expansion.

## 27. Compatibility with existing architecture

Stage 5C continues the approved Stage 5B-2 boundaries:

- Server SQLite remains the authoritative Catalog.
- Formal WPF never falls back to editable JSON.
- `SingleCameraTest` remains an explicit local compatibility path.
- The central password-safe DTO boundary is preserved.
- GUID identity is preserved.
- Runtime identity is never derived from Chinese names.
- Catalog configuration is not runtime health.
- Formal central playback does not reconstruct `Core.CameraDevice`.

The existing Stage 5B-2 design remains the client/catalog reference:
`docs/superpowers/specs/2026-08-30-stage-5b2-wpf-central-catalog-client-design.md`.
The current source shapes referenced here were checked against the existing
WPF playback boundary, local ZLM provider, ZLM client, Server DI, and
password-safe Catalog contracts.

## 28. Design quality gates

Before implementation begins, the written design must satisfy these gates:

- no unresolved design marker remains;
- formal Reader-driven lifecycle and Test Stream TTL lifecycle are clearly
  separate;
- `ZlmApiBaseUrl` and `PlaybackBaseUrl` are never conflated;
- Catalog health and media health remain independent;
- configuration persistence and runtime non-persistence remain distinct;
- ownership rules prevent unsafe deletion after Server restart;
- HMAC token expiry is a connection authorization window, not playback
  duration;
- stage scope remains limited to the stated media pipeline;
- no production code or implementation plan is part of this document.
