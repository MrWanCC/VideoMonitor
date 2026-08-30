# Stage 5B — Central Catalog API and WPF Data Source Design

**Status:** Approved design; implementation not started
**Date:** 2026-08-29
**Supersedes:** The Stage 5B legacy JSON migration direction in `docs/ROADMAP.md` and the legacy migration requirement in the earlier centralized architecture design for this project phase.

## 1. Purpose

Stage 5A established `VideoMonitor.Server`, SQLite persistence, application-level secret encryption, backup primitives, and readiness/health foundations.

The project has not yet been deployed to a production site. Existing WPF `device-catalog.json` data is development data, not production data that must be migrated. Therefore Stage 5B will not build a legacy migration subsystem.

Stage 5B makes the Server SQLite catalog the only authoritative device configuration source for the production architecture and moves WPF catalog reads/writes onto a central Server API while preserving the existing device-management UI structure as much as practical.

Core invariant:

> Server SQLite is the single source of truth. WPF may cache server data for display, but it must not maintain a second editable authoritative catalog.

## 2. Scope

Stage 5B includes:

- versioned central Catalog REST API under `/api/v1`;
- DeviceGroup read/create/update/delete;
- CameraDevice read/create/update/delete;
- CameraChannel configuration as part of the CameraDevice aggregate;
- password write/update without returning plaintext passwords;
- `hasPassword` in read DTOs;
- per-group and per-device configuration revisions;
- optimistic concurrency protection;
- SQLite V2 migration for revision fields;
- WPF `ICatalogApiClient`;
- WPF process-local `ClientCatalogCache` / read model;
- device-management async loading/saving/error states;
- monitor and secondary-monitor catalog reads from the same central client cache;
- Server unavailable/reconnect behavior;
- structured API errors;
- tests for API behavior, concurrency, atomicity, password handling, and WPF cache behavior.

Stage 5B explicitly does not include:

- user accounts;
- login UI;
- RBAC/roles;
- JWT or refresh tokens;
- client enrollment/registration credentials;
- legacy JSON migration APIs;
- multi-client legacy catalog merge;
- ZLMediaKit StreamManager;
- playback resolve API;
- ZLM hooks or reconciliation;
- a separate configuration application;
- persistent offline editable WPF catalog;
- field-level merge UI for concurrent edits.

## 3. Runtime topology and ownership

Production catalog path:

```text
VideoMonitor.Server
    |
    | Catalog REST API
    v
WPF CatalogApiClient
    |
    v
ClientCatalogCache
    |                     |
    v                     v
DeviceManagement       Monitor / Secondary Monitor
(write through Server) (read only)
```

Server ownership:

```text
SQLite
DeviceGroup configuration
CameraDevice configuration
CameraChannel configuration
Camera credentials
configuration revisions
```

WPF ownership:

```text
ServerAddress
process-local server catalog cache
UI state and local UI preferences
```

The WPF cache is not persisted as an editable authoritative data store in Stage 5B.

## 4. Legacy JSON boundary

Current WPF startup still uses `JsonDeviceCatalogStore`, `DeviceCatalogBootstrapper`, `InMemoryDeviceCatalog`, and `DeviceCatalogPersistenceCoordinator`. The existing single-camera development playback path also depends on that local catalog and the current `LocalZlmPlaybackSourceProvider`.

Stage 5B must not silently break that development verification path before Stage 5E replaces production playback resolution.

Therefore two paths are temporarily allowed:

```text
Production catalog path:
WPF -> Server Catalog API -> SQLite

Development single-camera compatibility path:
SingleCameraTest -> local JSON catalog -> LocalZlmPlaybackSourceProvider
```

The local JSON path is development-only. It must never be used as:

```text
Server unavailable -> fall back to editable JSON
```

No automatic bidirectional synchronization is allowed.

After the Server playback resolver replaces the local ZLM production path, the remaining JSON compatibility path can be removed in a later stage.

### 4.1 Online catalog refresh

While the Server is available, each WPF client also performs a low-frequency background catalog refresh using a bounded periodic `GET /api/v1/catalog`. Stage 5B does not introduce WebSocket, SSE, or SignalR catalog notifications. An initial interval in the 15–30 second range is a suitable implementation starting point, but the interval is a configuration/implementation parameter rather than an architecture constant.

Clients add a small per-client jitter to avoid synchronized requests. A refresh may not overlap another refresh: if the previous request has not completed, the next scheduled attempt is skipped or deferred. The refresh loop observes the application shutdown `CancellationToken` and stops promptly during shutdown.

After a successful refresh, WPF replaces `ClientCatalogCache` and raises its `Changed` event only when the Server snapshot has an actual member or configuration-Revision change. An unchanged snapshot produces no notification storm. Refresh never overwrites an unsubmitted `DeviceManagementViewModel` draft; the draft keeps its original expected Revision and a later save is still resolved by the Server's `409 Conflict` response when stale.

If refresh fails, WPF keeps the existing process cache, marks the Server unavailable, and enters the existing bounded reconnect/backoff flow. Refresh never falls back to editable JSON. Push notification is not required in Stage 5B; a future ETag, global catalog revision, SSE, or similar optimization may reduce polling, but is outside this stage.

## 5. Aggregate boundaries

### 5.1 CameraDevice aggregate

A CameraDevice is the concurrency and persistence aggregate for:

```text
CameraDevice
├─ identity and group
├─ name / IP / SDK port / RTSP port
├─ username
├─ protected password
├─ manufacturer / model / transport mode / enabled / remark
├─ CameraChannels[]
└─ Revision
```

CameraChannel does not have an independent revision in Stage 5B.

Changes to any persisted CameraDevice or CameraChannel configuration increment the parent CameraDevice revision.

This revision is deliberately reusable by Stage 5C StreamManager: a StreamEntry can record the DeviceRevision used when the upstream stream was created and detect stale configuration later.

### 5.2 DeviceGroup aggregate

Each DeviceGroup has its own revision.

Creating a group starts at revision 1. Updating persisted group configuration increments the group revision.

## 6. Database V2 migration

Stage 5A database schema version is V1. Stage 5B must add a V2 migration; it must not rewrite the historical V1 migration as though revision columns had always existed.

V2 adds:

```sql
ALTER TABLE device_groups
ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;

ALTER TABLE camera_devices
ADD COLUMN revision INTEGER NOT NULL DEFAULT 1;
```

The concrete migration implementation may use equivalent SQLite-safe migration steps if required by test or compatibility constraints, but the resulting schema semantics must be identical.

Rules:

- existing V1 rows become revision 1;
- new DeviceGroup rows start at revision 1;
- new CameraDevice rows start at revision 1;
- successful business configuration updates increment revision exactly once;
- GET operations never increment revision;
- runtime DeviceStatus/CameraStatus changes never increment configuration revision;
- runtime stream state never increments configuration revision.

## 7. Catalog API

Stage 5B API surface:

```text
GET    /api/v1/catalog

GET    /api/v1/device-groups
POST   /api/v1/device-groups
PUT    /api/v1/device-groups/{id}
DELETE /api/v1/device-groups/{id}?expectedRevision={revision}

GET    /api/v1/devices?groupId={groupId}
GET    /api/v1/devices/{id}
POST   /api/v1/devices
PUT    /api/v1/devices/{id}
DELETE /api/v1/devices/{id}?expectedRevision={revision}
```

`GET /api/v1/catalog` is the preferred WPF bootstrap/read-refresh endpoint. It returns groups, devices, and channels in one bounded payload to avoid N+1 startup calls. The expected deployment size of roughly 100 registered cameras is small enough for this first-version catalog snapshot model.

The resource-specific GET endpoints remain useful for focused reads, diagnostics, tests, and update-conflict refreshes.

For create operations, WPF generates stable GUIDs before sending the request for new groups, devices, and channels. Server creation uses those IDs rather than replacing them with new random IDs. This supports deterministic reconciliation after an ambiguous network timeout.

### 7.1 Read DTO security

Device read DTOs include configuration fields needed by WPF but never return the password ciphertext or plaintext password.

They expose:

```text
hasPassword: true|false
revision: number
```

There is no password-read endpoint.

### 7.2 Password create/update semantics

Create and update semantics are intentionally different:

- `CreateDeviceRequest.password` is the initial camera password value and is never echoed back by the Server.
- `UpdateDeviceRequest.newPassword = null` means preserve the existing protected password.
- `UpdateDeviceRequest.newPassword = <non-empty string>` means replace the password.
- Stage 5B does not add a separate ‘clear existing password to empty’ operation; if that becomes a real device requirement it can be added explicitly later instead of overloading blank UI input.

The Server encrypts an accepted password with the existing application secret-protection boundary and persists only ciphertext.

The WPF editor displays no existing password. When editing an existing device, the password field starts empty with user text equivalent to “leave blank to keep the current password”; blank edit input maps to `newPassword = null`.

## 8. Optimistic concurrency

Stage 5B uses per-aggregate revision-based optimistic concurrency rather than last-write-wins.

Example:

```text
Server Device A revision = 12
Client A reads revision 12
Client B reads revision 12

Client A update(expectedRevision=12)
-> commit succeeds
-> revision becomes 13

Client B update(expectedRevision=12)
-> 409 Conflict
-> DEVICE_REVISION_CONFLICT
```

Updates and deletes must verify expected revision inside the same transaction as the write/delete.

The Server must not perform a revision read outside the transaction and later write based on that stale check.

### 8.1 Conflict handling in WPF

The first version does not attempt field-level merge.

On `DEVICE_REVISION_CONFLICT` or `GROUP_REVISION_CONFLICT`, WPF shows a clear conflict message and offers to refresh the current Server version. The stale draft is not force-saved over the newer Server version.

### 8.2 Delete concurrency

Deletes require `expectedRevision` too.

A client that loaded revision 5 cannot delete an aggregate that another client has already changed to revision 6 without first refreshing.

### 8.3 Group non-empty rule

The WPF can provide early UI feedback, but the Server is authoritative.

Deleting a group that still contains devices returns:

```text
409 GROUP_NOT_EMPTY
```

The database foreign key remains a final integrity boundary.

## 9. Server application boundary

HTTP endpoints must not become the place where SQL and secret handling are implemented directly.

Required responsibility split:

```text
HTTP Endpoint
    -> CatalogApplicationService
        -> persistence/repository boundary
            -> SQLite
```

HTTP endpoint responsibilities:

- parse route/query/body;
- invoke application service;
- map structured result/errors to HTTP;
- avoid logging sensitive request bodies.

Catalog application service responsibilities:

- validate group/device/channel business rules;
- apply revision concurrency checks;
- apply password preserve/replace semantics;
- enforce group non-empty deletion rule;
- coordinate aggregate transaction boundaries;
- return safe DTOs/results.

Persistence/repository responsibilities:

- SQL;
- atomic aggregate writes;
- encrypted secret persistence;
- consistent reads.

Existing `SqliteDeviceCatalogStore` can be reused/refactored where it fits, but the API layer must not bypass a central catalog business boundary with scattered SQL.

## 10. Client-side boundaries

### 10.1 ICatalogApiClient

WPF uses an asynchronous Server client for remote operations, conceptually:

```text
GetCatalogAsync
GetDeviceAsync
CreateGroupAsync
UpdateGroupAsync
DeleteGroupAsync
CreateDeviceAsync
UpdateDeviceAsync
DeleteDeviceAsync
```

The interface returns safe client DTOs/results, not persistence entities containing Server-only secrets.

### 10.2 ClientCatalogCache / read model

The WPF process maintains one in-memory client cache that contains the most recent successful Server catalog snapshot.

It exposes a read-only catalog shape to monitor-oriented ViewModels:

```text
GetGroups()
GetDevices(groupId)
GetDevice(deviceId)
Changed
```

The cache does not expose direct authoritative mutation methods such as `UpdateDevice` to monitor ViewModels.

`MonitorViewModel` and `SecondaryMonitorViewModel` depend only on the read model.

`DeviceManagementViewModel` depends on:

```text
catalog read model
+
ICatalogApiClient
```

so all authoritative mutations flow through the Server.

## 11. WPF write flow

A WPF edit remains a draft until the Server confirms commit.

Correct flow:

```text
user edits draft
-> Save command
-> async Server request
-> Server transaction commits
-> Server returns latest safe DTO/revision
-> ClientCatalogCache updates
-> Changed event
-> device list and monitor tree refresh
```

Incorrect flow:

```text
mutate ClientCatalogCache first
-> request Server later
```

If the request fails, the draft may remain visible for retry, but the authoritative client cache must remain unchanged.

## 12. WPF UI preservation and state

The existing device-management page structure remains the basis of the UI. Stage 5B does not redesign the page.

Preserve the current major interaction structure:

- group tree;
- device list;
- search;
- add/edit/delete actions;
- editor panel;
- confirmation dialogs.

Add only the states required by a remote central data source:

```text
IsLoading
IsSaving
IsServerAvailable
OperationError
```

Expected behavior:

- save/delete actions are disabled while the same operation is in flight;
- double-click/repeated-save cannot create duplicate concurrent writes;
- initial loading is visible instead of freezing the UI thread;
- Server errors produce user-facing messages without exposing internal exception details;
- revision conflicts prompt refresh rather than overwrite;
- existing password is never populated into the password editor;
- on edit, untouched/blank password preserves the Server password.

## 13. Startup and reconnect behavior

Production Server mode no longer treats catalog-load failure as an application-fatal error.

Startup:

```text
read ServerAddress
-> start WPF shell
-> request GET /api/v1/catalog asynchronously
-> on success replace ClientCatalogCache
-> on failure show Server unavailable state
```

If Server is unavailable:

- WPF still starts;
- already-loaded process cache is not cleared;
- if there has never been a successful load, the device tree shows an unavailable/loading failure state;
- catalog write actions are disabled;
- no local editable fallback is created;
- no JSON synchronization starts.

Connection state can remain intentionally small:

```text
Unknown
Connected
Unavailable
```

Reconnect uses bounded backoff rather than a tight loop. A suitable first implementation can start around 2s/5s/10s and cap in the 15–30s range, with cancellation on application shutdown. Exact constants remain configuration/implementation details, but the loop must be bounded and non-busy.

After connectivity is restored, WPF refreshes `GET /api/v1/catalog` before considering its cache current.

## 14. HTTP retry semantics

Read and write requests have different retry rules.

### 14.1 GET

GET requests are side-effect free and may use a small bounded transient retry policy with cancellation.

### 14.2 POST / PUT / DELETE

Mutating requests are not blindly retried after an ambiguous timeout.

Example ambiguity:

```text
Server commits create
-> response is lost
-> WPF sees timeout
```

A blind retry could duplicate work.

For creates, the WPF generates aggregate IDs before POST. If a create result is ambiguous, after reconnect the client can query the known ID to determine whether the first request committed before allowing another explicit save attempt.

Stage 5B does not add a general distributed Idempotency-Key subsystem.

## 15. Atomicity and validation

CameraDevice plus CameraChannels are persisted atomically as one aggregate update.

If any channel validation, uniqueness constraint, encryption operation, or database write fails:

```text
ROLLBACK
```

Required consequences:

- no partially updated CameraDevice;
- no partially replaced channel set;
- no revision increment;
- existing password remains unchanged if replacement fails.

Important validation remains enforced on the Server even when WPF already validates it:

- valid IP format supported by current product contract;
- SDK/RTSP ports 1–65535;
- channel number > 0;
- valid enum values;
- valid referenced group;
- channel uniqueness `(device_id, channel_no, stream_type)`;
- group deletion only when allowed;
- IDs and relationships consistent.

Client-side validation exists for user experience, not as the central integrity boundary.

## 16. Structured error model

Stage 5B uses stable machine-readable codes. WPF logic must not parse Chinese message text.

Minimum mapping:

```text
400 CATALOG_VALIDATION_FAILED

404 DEVICE_NOT_FOUND
404 GROUP_NOT_FOUND

409 DEVICE_REVISION_CONFLICT
409 GROUP_REVISION_CONFLICT
409 GROUP_NOT_EMPTY
409 CHANNEL_CONFLICT

503 CATALOG_UNAVAILABLE

500 CATALOG_READ_FAILED
500 CATALOG_WRITE_FAILED
```

Responses may include a user-safe message and non-sensitive metadata such as `currentRevision` for conflicts.

They must not include:

- plaintext camera password;
- protected password ciphertext;
- credential-bearing RTSP URL;
- Master Key;
- ZLM secret;
- internal stack trace.

## 17. Security boundary for Stage 5B

The product currently has no account/permission requirement. Stage 5B therefore does not introduce login or authorization infrastructure merely because the API is central.

This is an explicit non-goal, not an accidental omission.

Security requirements that do apply:

- camera plaintext password remains Server-side except transiently in an inbound explicit password-replacement request;
- API reads never return camera password plaintext/ciphertext;
- logs must not record request bodies containing passwords;
- Server stores camera password using the Stage 5A application secret protector;
- production WPF -> Server Catalog API uses HTTPS or an equivalent authenticated encrypted private transport because password create/update requests can carry plaintext camera credentials in transit; development may use local HTTP;
- production deployment exposes only required Server/ZLM ports and does not treat ‘same LAN’ as a reason to expose camera credentials broadly;
- adding future authentication/authorization must be possible in front of the API without changing the catalog ownership model.

UI hiding is not treated as a security permission boundary.

## 18. Test and acceptance requirements

### 18.1 Database migration

Verify:

- fresh database reaches schema V2;
- existing V1 database upgrades to V2;
- existing rows receive revision 1;
- rerunning initialization is idempotent;
- a database version above supported V2 is rejected.

### 18.2 Catalog API

Verify:

- create/read/update/delete group;
- create/read/update/delete CameraDevice with channels;
- `GET /api/v1/catalog` returns one consistent catalog snapshot;
- password is encrypted at rest;
- password is absent from read API responses;
- `hasPassword` is correct;
- `newPassword = null` preserves existing ciphertext/secret;
- password replacement increments device revision once;
- normal device update increments revision once;
- GET does not increment revision;
- runtime status does not affect configuration revision.

### 18.3 Concurrency

At minimum test two independent clients:

```text
A reads revision 1
B reads revision 1
A PUT -> success revision 2
B PUT expectedRevision 1 -> 409
final database = A committed version
```

Also verify stale delete receives 409.

### 18.4 Group integrity

Verify:

- non-empty group delete returns `GROUP_NOT_EMPTY`;
- group revision conflicts do not overwrite newer values;
- database foreign keys still protect integrity.

### 18.5 Channel and transaction atomicity

Verify:

- duplicate `(device_id, channel_no, stream_type)` is rejected;
- channel failure rolls back the entire device update;
- failed aggregate update does not increment revision;
- failed password replacement preserves old password and revision.

### 18.6 WPF behavior

Verify:

- WPF remains responsive during catalog network calls;
- WPF can start when Server is unavailable;
- unavailable Server does not clear a previously loaded process cache;
- write actions are disabled/unavailable when Server cannot be reached;
- save failure leaves ClientCatalogCache unchanged;
- successful save updates cache only after Server confirmation;
- conflict is surfaced and refresh obtains the latest Server state;
- reconnect refreshes the catalog;
- no production Server-unavailable path writes to local JSON.

### 18.7 Online catalog refresh

Verify:

- while the Server remains online, Client B's bounded periodic refresh observes a catalog change committed by Client A;
- the 100-client control-plane simulation includes the periodic catalog refresh request load;
- periodic refresh requests do not overlap and do not form a tight loop;
- a refresh never overwrites an unsubmitted device-management draft;
- an unchanged Server snapshot does not produce meaningless `Changed` notification storms.

### 18.8 Secret leak checks

Search tests/log output/API JSON for:

- test camera password literals;
- `password_ciphertext`;
- credential-bearing RTSP URLs;
- Master Key material;
- ZLM secret.

No secret may appear in normal API responses or logs.

## 19. Completion criteria

Stage 5B is complete only when all of the following are true:

```text
Server SQLite is the authoritative production catalog.
WPF production catalog reads come from Server.
WPF production catalog writes commit through Server.
Monitor and device-management views consume the same client cache.
No Server outage creates a second editable local catalog.
Concurrent edits cannot silently overwrite newer configuration.
Camera password is never returned by the catalog read API.
Legacy JSON remains development-only and is not a production fallback.
All Stage 5B Server and WPF tests pass.
Existing Stage 5A foundation tests remain passing.
```

Stage 5C may then build ZLM integration and StreamManager on top of the central catalog and its DeviceRevision semantics.
