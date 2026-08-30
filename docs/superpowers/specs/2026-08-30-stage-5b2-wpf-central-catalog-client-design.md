# Stage 5B-2 — WPF Central Catalog Client and Group Semantics Design

Status: Approved design for written-spec review; implementation not started

Stage 5B-1 Server Central Catalog API is complete. Stage 5B-2 defines the WPF client boundary that consumes that API and the small domain evolution needed to make group semantics stable. This document is an architectural specification, not an implementation plan.

Stage 5B-2 covers:

1. Switching the formal WPF monitor to the central Catalog data source.
2. A process-local, password-safe Catalog cache.
3. Server connection, refresh, reconnect, and endpoint settings.
4. Remote asynchronous CRUD from Device Management.
5. Read-only Catalog access for Monitor and Secondary Monitor.
6. Removing runtime dependence on fixed Chinese group names.
7. A small Server/Core V3 `MonitorGroupKind` evolution.
8. The current fixed 4+3 business layout, including empty and incomplete Catalogs.
9. Clear seams for a future Cloud Control Plane and Site Edge deployment.

No cloud capability is implemented by this design.

## 1. Core architecture

The formal WPF data path is:

```text
VideoMonitor.Server
        |
        | /api/v1 Catalog API
        v
CatalogApiClient
        |
ServerConnectionCoordinator
        |
ClientCatalogCache
        |
        +--> MonitorViewModel             read only
        +--> SecondaryMonitorViewModel    read only
        +--> DeviceManagementViewModel    read model + async API writes
```

The following boundaries are mandatory:

- Server SQLite is the single authoritative Catalog source.
- WPF does not maintain a second editable authoritative Catalog.
- `CatalogApiClient` is responsible for HTTP only.
- Each WPF process has exactly one `ServerConnectionCoordinator`.
- `ServerConnectionCoordinator` owns initial loading, periodic refresh, reconnect, connection state, and endpoint switching.
- `ClientCatalogCache` is process-local, memory-only, and password-safe.
- A read-only boundary such as `IDeviceCatalogReadModel` exposes `GetGroups()`, `GetDevices(groupId)`, `GetDevice(deviceId)`, and `Changed`.
- Formal Monitor and Secondary Monitor do not depend on a synchronous `IDeviceCatalog` with Add/Update/Delete operations.
- Remote HTTP is never placed behind synchronous `IDeviceCatalog` calls through `.Result`, `.Wait()`, or `GetAwaiter().GetResult()`.
- Existing `IDeviceCatalog` and JSON persistence remain only for the explicitly enabled `SingleCameraTest` development compatibility path.

## 2. WPF startup and Server connection

Formal central mode starts as follows:

```text
WPF startup
-> read client settings
-> create an empty ClientCatalogCache
-> show Shell/MainWindow normally
-> connect to Server asynchronously in the background
-> GET /health/ready
-> GET /api/v1/catalog
-> atomically replace ClientCatalogCache after success
```

Server unavailability must never terminate WPF startup. Connection state at minimum expresses:

- `Unconfigured`
- `Connecting`
- `Connected`
- `Unavailable`

The coordinator also exposes `LastSuccessfulSyncUtc` for the UI.

Connection rules:

- An unconfigured Server opens the application normally with an empty Catalog.
- A failed first connection opens the application normally with an empty Catalog.
- A disconnect after a successful sync retains the last snapshot.
- A retained snapshot is explicitly marked stale/offline.
- A stale snapshot is read-only and all writes are disabled.
- Server recovery is `Connected` and current only after a complete `GET /api/v1/catalog` succeeds.
- A Server failure never causes an automatic JSON fallback.
- The old development JSON path is allowed only when `SingleCameraTest.Enabled=true`.

The first connection/reconnect backoff is fixed to:

```text
2s -> 5s -> 10s -> 15s -> 15s ...
```

Each delay includes a random jitter of plus or minus 20 percent. Online Catalog refresh uses a 30-second-scale period with plus or minus 20 percent jitter. The period is a configuration or implementation parameter, not an architectural constant.

Refresh requirements:

- A refresh never overlaps a previous refresh.
- The shutdown `CancellationToken` terminates connection and refresh loops.
- An unchanged snapshot does not create a `Changed` notification storm.
- A failed refresh retains the previous process cache and changes Server availability state.
- Failed refresh enters the bounded reconnect/backoff behavior already defined by the coordinator.
- Refresh never invokes JSON fallback.

## 3. Dispatcher and WPF thread boundary

HTTP requests, waiting, and response parsing may run on a background thread. The following operations must be marshaled to the WPF Dispatcher:

- publishing `Changed` after an authoritative ClientCatalogCache snapshot commit;
- modifying `ObservableCollection` instances;
- modifying ViewModel UI state;
- rebuilding the Monitor tree;
- updating VideoTiles.

An HTTP background task must not directly mutate a ViewModel `ObservableCollection`.

The cache commit itself is atomic from the client perspective: a complete validated snapshot is prepared off the UI thread, then the cache reference/state is committed once, and only then is the UI notification dispatched.

## 4. Server/Core V3 group kind

Runtime code must stop identifying group behavior from the Chinese root names `卸矿站监控`, `溜井监控`, and `巷道监控`.

The stable business semantic is `MonitorGroupKind`:

- `UnloadingStation`
- `Chute`
- `Tunnel`

The implementation uses one semantic enum. If the existing `MonitorGroupType` can be retained only by a direct rename or compatibility-preserving reuse, it is renamed/reused; two overlapping enums must not coexist. Runtime selection and switching use the resulting stable enum and GroupId, never display text.

### V3 database field

Schema V3 adds the nullable field:

```text
device_groups.group_kind TEXT NULL
```

The field has these semantics:

- Root group: `ParentId == null` and `GroupKind != null` in the formal valid state.
- Child group: `ParentId != null` and `GroupKind == null` in the database; runtime inherits the kind from its root.
- Multiple roots may use the same kind.

For example, two independent mine areas may each have a root of kind `Chute`:

```text
Mine A Chute (Chute)
├─ 401
└─ 402

Mine B Chute (Chute)
└─ 501
```

The database and domain are not restricted to exactly three global roots. `Name` is display text and never carries type identity.

## 5. Group hierarchy constraints

The Server is the authoritative validator. WPF validation exists only for user experience.

The permitted hierarchy operations are:

- Root to Root: Name, Sort, and Enabled may be changed. Once assigned, Root Kind is immutable.
- Child to Child: Name, Sort, and Enabled may be changed. A child may move to another valid root/child hierarchy and then inherits the new root kind.
- Root to Child: rejected.
- Child to Root: rejected.
- Parent cycles: rejected.

Every `CameraDevice` must belong to an actual business Child group. A device cannot be assigned directly to a Root category.

Root categories express monitoring business semantics and directories. Child groups express selectable operational groups.

## 6. V2 to V3 migration

The current database schema version is 2. V3 is a real migration and must not alter historical V1/V2 migrations to pretend that `group_kind` existed earlier.

V3 adds `device_groups.group_kind` as nullable using an SQLite-safe schema operation. The migration may perform a one-time compatibility mapping for historical root names:

```text
卸矿站监控 -> UnloadingStation
溜井监控   -> Chute
巷道监控   -> Tunnel
```

Chinese name recognition exists only inside this one-time migration. Runtime code never infers kind from `Name`.

An existing Root that cannot be recognized retains `group_kind = NULL`. Device Management displays it as “未分类/需选择监控类型”. The first successful repair/save of that Root must assign a Kind, after which the Kind is locked.

A new database does not insert three system roots automatically. An empty Catalog is valid.

## 7. Monitor tree semantics

The Monitor tree preserves the actual hierarchy even when roots share a `MonitorGroupKind`.

```text
一矿区溜井 (Chute)
├─ 401
└─ 402

二矿区溜井 (Chute)
├─ 501
└─ 502
```

The client must not merge these into one fixed display category:

```text
溜井监控
├─ 401
├─ 402
├─ 501
└─ 502
```

Rules:

- `Root.Name` determines the first-level display text.
- `Child.Name` determines the selectable operational group text.
- `Root.Kind` determines the layout/business behavior inherited by a Child.
- A fixed Chinese title is never a runtime identity.

## 8. Identity and lookup

The identity mapping is:

```text
GroupId = identity
Name    = display text
Kind    = layout/business semantics
Sort    = ordering
```

Every selection, reference, switch, persistence of current selection, and resolution operation uses Guid identity. Runtime code must not use `group.Name` for `Single(...)`, `First(...)`, lookup, switching identity, or current selection recovery.

In particular, the existing Secondary Monitor name-based group lookup is removed during implementation. Same-name groups are legal and remain independent.

## 9. Fixed 4+3 layout

Stage 5B-2 does not introduce a dynamic `LayoutProfile`.

The current business layout remains:

- Main: three Chute slots and one Tunnel slot.
- Secondary: three UnloadingStation slots.

The fixed Tiles remain instantiated:

- Main: four Tiles.
- Secondary: three Tiles.

The layout handles incomplete Catalogs without throwing:

- zero devices is valid;
- fewer than three devices is valid;
- a missing Kind is valid;
- a device may be deleted or disabled;
- an empty Server Catalog is valid;
- an initially empty Server Catalog is valid.

An unavailable slot is reset and displays “未配置”. No dynamic grid, general slot engine, or `LayoutProfile` is introduced for this stage.

## 10. Deterministic default group

No `DefaultGroupId` configuration is added in Stage 5B-2.

For each Kind, the default selectable business Child is the first enabled valid Child ordered by:

```text
Root.Sort
-> Child.Sort
-> Child.GroupId
```

Only enabled Roots and enabled Children participate. Device quantity, online rate, and display name never influence default group selection. A shortage of devices fills empty slots without changing the selected group.

If a future field deployment requires an explicit default group, that is a separate design decision involving `DefaultGroupId` or a layout profile.

## 11. CameraStatus

The central Catalog describes configuration, not current device health.

When a Server Catalog device is projected into a WPF `CameraDevice`, its initial status is always `CameraStatus.Unknown`. Presence in the Catalog never implies `Online`.

`Online`, `Warning`, and `Offline` are produced by a later runtime health/status mechanism. The previous exit-time status is not used as the next startup fact.

## 12. Device Management write model

Add/Edit uses a local Draft. Opening an editor and Cancel do not write to Server.

Save follows:

```text
Draft
-> async CatalogApiClient
-> Server transaction
-> success
-> full GET /api/v1/catalog
-> ClientCatalogCache replacement
-> UI refresh
```

WPF never mutates the authoritative cache before a Server write succeeds.

On a failed write:

- the Draft remains available;
- the authoritative cache is unchanged;
- the UI does not display a false success.

WPF creates the stable Guid for a new Group, Device, or Channel before the POST. POST, PUT, and DELETE are not blindly retried after a timeout. For an ambiguous timeout, a follow-up query of the known identity determines whether the operation committed.

## 13. Password boundary

Existing-device password editors start blank. GET DTOs expose only `HasPassword`; they never return plaintext or ciphertext.

Update semantics:

- blank editor means `NewPassword = null`, preserving the current password;
- a non-empty value replaces the current password;
- an empty string is not a clear-password operation.

Stage 5B-2 does not implement credential clearing. Any future clear operation requires a separate explicit design.

## 14. Optimistic concurrency

Stage 5B-1 optimistic concurrency remains the consistency mechanism:

```text
Revision + expectedRevision + HTTP 409
```

No pessimistic edit lock is introduced.

For two writers at Revision 3:

```text
A save -> Revision 4
B save -> HTTP 409
```

WPF must not overwrite on conflict. It displays a clear conflict message, retains the current Draft, and lets the user inspect the latest data and explicitly retry. Automatic merge and last-write-wins are not used.

An advisory or lease edit lock is outside this stage; Revision remains the final data consistency boundary.

## 15. Server settings

The client-side settings file is proposed at:

```text
C:\ProgramData\VideoMonitor\client-settings.json
```

It stores only non-sensitive settings, for example:

```json
{
  "Server": {
    "BaseUrl": "https://video-site.example.com"
  }
}
```

It must not store camera passwords, ZLM secrets, tokens, or Server private secrets. BaseUrl does not need encryption.

Server BaseUrl is parsed as a standard absolute URI. The client does not assume a `192.168.x.x` address or a fixed IP. HTTP is allowed for development/internal debugging; production requires HTTPS.

TLS certificate validation must never be bypassed. In particular, `ServerCertificateCustomValidationCallback = accept all` and equivalent behavior are prohibited.

## 16. Test-before-save and atomic Server switch

When the configured Server is A and the user enters Server B, the client probes B through a temporary HTTP client:

```text
GET B /health/ready
-> GET B /api/v1/catalog
```

Both requests must succeed before B is accepted.

If the probe fails, A's settings and Catalog cache remain unchanged.

If the probe succeeds, the switch atomically changes:

```text
Configured BaseUrl
+ Catalog Snapshot
+ Connection state
```

The UI must never expose Server B together with a Catalog from Server A. An unsaved Device Management Draft blocks a Server switch until the user saves or cancels it.

If B becomes unavailable immediately after a successful probe, B remains the configured endpoint and enters normal reconnect. The client does not silently return to A.

## 17. Connection and stale UI

The Settings area exposes at least Server BaseUrl, Test Connection, and Save.

The status area expresses:

- 未配置
- 连接中
- 已连接
- 连接失败
- 最后同步时间

The StatusBar “安全运行中” and Header “系统运行正常” must not contradict actual Server state. If retained, those labels are bound to real overall state or explicitly renamed to client-running status. The existing visual style is not otherwise redesigned.

The central Server state has priority in the lower-right status presentation.

## 18. Error handling

The client continues to use Stage 5B-1 machine-readable error codes:

- `CATALOG_VALIDATION_FAILED`
- `DEVICE_NOT_FOUND`
- `GROUP_NOT_FOUND`
- `DEVICE_REVISION_CONFLICT`
- `GROUP_REVISION_CONFLICT`
- `GROUP_NOT_EMPTY`
- `CHANNEL_CONFLICT`
- `CATALOG_UNAVAILABLE`
- `CATALOG_READ_FAILED`
- `CATALOG_WRITE_FAILED`

If V3 GroupKind or hierarchy validation needs more errors, only stable machine-readable codes are added. WPF does not parse Chinese messages.

Responses and logs must not disclose SQL, stack traces, secrets, internal exceptions, or password request bodies.

## 19. Cloud-ready boundary

Stage 5B-2 is Cloud-ready but does not implement a cloud platform.

Current topology:

```text
WPF
 |
 | HTTPS
 v
Site VideoMonitor.Server
 |
 + SQLite
 + ZLM
 + Cameras
```

Future topology may evolve to:

```text
             Cloud Control Plane
             /       |        \
          Site A   Site B    Site C
            |        |         |
        Edge/Site Edge/Site Edge/Site
          Server   Server    Server
            |
       Camera + ZLM
```

The following seams remain stable:

1. WPF knows API URIs, not SQLite.
2. ViewModels do not compose endpoints.
3. HTTP is centralized in `CatalogApiClient` and future API clients.
4. API routes remain versioned under `/api/v1`.
5. Entities use Guid identity rather than single-machine database auto-increment IDs.
6. Server persistence remains behind the repository boundary.
7. Control Plane and Media Plane remain separate.
8. `Server.BaseUrl` represents the control API only.
9. Server address is not assumed to equal the ZLM address.
10. Playback continues through `IPlaybackSourceProvider`/resolve boundaries.
11. Local ZLM, Edge ZLM, and future cloud media nodes can replace the playback source without exposing their origin to Tile or ViewModel.

Before a real cloud deployment, a separate architecture stage must design authentication, authorization, audit, SiteId, tenant isolation if required, rate limiting, client/device enrollment, certificate/token lifecycle, cloud storage, Edge registration, network traversal, and media routing. None of these are implemented here.

Cloud-ready does not mean Internet-ready. The current Server API relies on a trusted client/network, HTTPS, network segmentation, and ACLs; it is not approved for unauthenticated public Internet exposure.

## 20. JSON compatibility boundary

The following components remain only for the explicit `SingleCameraTest.Enabled=true` development path:

- `JsonDeviceCatalogStore`
- `DeviceCatalogBootstrapper`
- `DeviceCatalogPersistenceCoordinator`
- `LocalZlmPlaybackSourceProvider`

Formal central mode never performs:

```text
Server down -> editable JSON fallback
```

There is no two-way synchronization and no offline write. Server SQLite remains the sole authoritative Catalog.

## 21. Non-goals

Stage 5B-2 does not implement:

- user login;
- RBAC;
- JWT;
- client registration;
- tenant system;
- Site management platform;
- cloud platform;
- Edge Agent;
- message queue;
- SignalR/SSE;
- WebSocket Catalog push;
- edit lock;
- offline writes;
- disk Catalog cache;
- JSON migration subsystem;
- JSON authoritative fallback;
- StreamManager changes;
- ZLM central lifecycle;
- Playback Resolve API;
- ZLM hooks;
- camera runtime health detection;
- dynamic LayoutProfile;
- 9-grid;
- field-level merge;
- cloud video relay.

## 22. Required implementation and acceptance behavior

The implementation stages derived from this specification must cover at least the following behavior.

### Server/Core

- V2 to V3 migration;
- known historical Chinese Root mapping exactly once during migration;
- unrecognized Root remains unclassified;
- runtime no longer classifies by Name;
- a new Root requires a valid Kind;
- Child Kind is inherited and not separately persisted;
- Root to Child is rejected;
- Child to Root is rejected;
- parent cycles are rejected;
- direct device assignment to a Root is rejected;
- multiple Roots with the same Kind are legal.

### WPF client

- unconfigured Server starts normally;
- first connection failure does not stop startup;
- empty and partial Catalogs start normally;
- disconnect retains a stale snapshot;
- stale mode disables writes;
- reconnect performs a full refresh;
- no JSON fallback occurs;
- refresh does not overlap;
- jitter and backoff are testable through an abstract clock/delay or deterministic seam;
- cache Changed is published through the WPF Dispatcher;
- duplicate group names do not affect lookup or switching;
- all identity and switching uses Guid;
- multiple same-Kind roots remain separate in the tree;
- default group selection is deterministic by Sort plus Guid;
- zero, one, or two camera groups do not break fixed 4+3 layout;
- missing Kind slots reset to unconfigured;
- projected CameraStatus starts as Unknown;
- passwords are never returned;
- blank password preserves the existing credential;
- a failed write retains the Draft;
- a 409 preserves the Draft and does not overwrite the cache;
- write timeouts are not blindly retried;
- a failed new-Server probe preserves the old Server and cache;
- Server switching is atomic;
- an active unsaved Draft blocks Server switching;
- HTTP is allowed only in development settings;
- HTTPS validation is never bypassed.

## 23. Six adopted architecture-review corrections

This design explicitly adopts these corrections:

1. Multiple Roots preserve the real hierarchy and are not merged by Kind into a fixed Chinese directory.
2. Every selection and reference uses Guid; Name is display text only.
3. GroupKind has explicit Root/Child hierarchy constraints.
4. V3 migration handles historical Roots; Chinese recognition exists only during migration.
5. Server endpoint switching uses probe plus atomic switch and blocks a Draft from crossing Servers.
6. Background refresh and the WPF Dispatcher/UI-thread boundary are explicit.

## 24. Explicit decisions

The following decisions are fixed for Stage 5B-2:

- Multiple Roots with the same Kind are allowed.
- Root Kind is required in the formal valid state; transitional unclassified legacy Roots are the sole exception.
- Child Kind is not separately stored and is inherited from the Root.
- Once a Root Kind is formally assigned, it cannot be changed.
- Root/Child structural conversion is not allowed in Stage 5B-2.
- Direct assignment of a CameraDevice to a Root is not allowed.
- Default group is the first enabled Child ordered by Root.Sort, Child.Sort, and Child.GroupId.
- Empty Catalog is legal.
- Incomplete 4+3 layout is legal and uses unconfigured slots.
- Server unavailability does not exit WPF.
- A stale snapshot is read-only.
- Formal JSON fallback is prohibited.
- Edit lock is not implemented.
- Optimistic Revision concurrency is retained.
- Cloud capability is represented only by seams; no cloud feature is implemented.
- Production transport is HTTPS.
- TLS validation bypass is prohibited.
- Authentication is not implemented in Stage 5B-2.
- Public Internet exposure is not supported by the current API.

## 25. Scope and compatibility boundary

This specification is limited to the WPF central Catalog client, V3 group semantics, fixed-layout adaptation, and the connection/cache boundaries needed to consume the existing Server API.

It does not change the approved StreamManager, ZLM, PlaybackManager, or playback source design. It does not add authentication, authorization, login, JSON migration, or a cloud service. The implementation work must preserve the existing single-camera development path while ensuring that formal central mode has one authoritative Server Catalog and no editable local fallback.
