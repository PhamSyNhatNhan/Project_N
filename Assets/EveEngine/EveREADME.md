# EveEngine

## World lifetime and execution (Rules79 / Mod API6)

`EveSessionContext.Worlds` owns instances, incarnation tombstones and scoped cleanup.
`EveWorldContext` separates publication from execution: Ready/Paused worlds keep
published entities; only Ready advances gameplay/physics. Entity IDs remain global
within the play, with a world index and content-slot metadata. `CanUse` permits
state capture/cleanup; `CanExecute` also requires no actor hold and a runnable world;
`CanSimulate`/`CanMutate` additionally require gameplay authority. Hold tokens are
independent leases and do not disable or republish the entity.

World unload drains entities, adapter/Area ownership and leases before reversing
resource acquisition. Cleanup stops at the first failed resource and retains it
for retry. `EveWorldCleanupPendingException` represents outstanding noncancelable
scene IO, leaves the world Unloading and play Stopping, and is pumped without
pretending completion. Actual failures remain Failed/diagnosable.

`PNWorldPhysicsLoop` in Core explicitly simulates each local physics owner once per
fixed boundary after Move producers. Ground probes and cached casts resolve the
actual world owner. Editor-only `RelocateWorldFixture` validates held relocation;
the production transfer transaction is a later World foundation S5 task.

## Deferred admission prerequisite (P4, 2026-10-18)

The additive eight-argument scheduler Enqueue overload accepts `canBegin`. A false
predicate defers factory allocation and rotates pending admission fairly, under the
same preparation budget and original deadline. Callback failure follows candidate
failure/cleanup. `EveEntityDirectory.IsReserved` includes preparing/retiring IDs;
TryGet's published-only visibility is insufficient to detect pending cleanup.
Mirror hide/show uses this prerequisite to avoid duplicate same-generation enrollment.
`MaximumResidents` exposes the configured bound for adapter capacity/fence validation.

## Current prepared-publication work (P3, 2026-10-17)

`EveEntityPreparationScheduler` retains its original Enqueue overload and adds a
publication-preparation callback. A completed loader can wait in Prepared until its
required projection transactions are validated; it remains owned/counts resident and
retains its original timeout. No repeated PrepareNext or temporary public Ready is
used to wait for network data. BeforePublish performs prepared swaps, not wire parsing.

`SchedulePreparation` admits cancellable bounded projection work into the same
preparation budget/step allowance. Stop retires jobs and candidates; caller ownership
must still dispose the buffers/subscriptions it retained. One-step profiles alternate
live projection work with entity preparation so neither monopolizes progress.
Quantum/mod callbacks are not preemptible; elapsed budgets are admission guards, not
hard wall-clock guarantees. `EveBindingInbox.ReserveBytes` supplies idempotent retained
byte leases; adapters must keep encoded/decoded/staging ownership charged until release.

## Current content/play lifetime (P1 S1–S2, 2026-10-16)

`EveRuntime.EnsureContentLoaded()` builds one `EveContentContext` (sealed registry, compatibility and mod
instances) without starting gameplay. `BeginSession(role)` creates a fresh fixed-role context from that
content. `Initialize()` explicitly combines those two operations for existing Offline/test callers and
is idempotent for an already-running same-role session. Changing Role requires ending the play first.
`EndSession()` invalidates and cleans entities and ends mod play hooks, retaining content. Use
`UnloadContent()` for terminal disposal/reload; the next EnsureContentLoaded builds a new generation.

`EveRegistrySession.Context/Current/IsReady` still describe the current play, not content readiness.
`EveRuntimeEventIds.ContentReady` is distinct from gameplay `Ready`; loading content alone never activates
loaders through the play-ready event. Cleanup failure keeps Stopping and ownership for RetryCleanup,
including requested content disposal if its runtime owner is destroyed. Registered mod instances survive
play ends so definitions/factories do not reference disposed objects. See `Mods/EveModsREADME.md`.

This is the Engine foundation. App-launch bootstrap, menu/scene coordination and full Game UI/Area
binding belong to S3; Mirror admission/enrollment is not implemented by S1–S2. Older descriptions below
that clear content on every EndSession are superseded by this contract.

Current lifecycle integration (RulesVersion67 / Mod API6): see
`Networking/EveActorLifecycleREADME.md` for Prepared/Commit, publication guards,
generation directory, budgeted real-object cleanup and actor/Buff snapshot adapters.
Production policy and transport remain unselected/unconnected. Older entries below
are historical when they describe synchronous-only loading or absent replication.

## English

EveEngine is reusable Unity code. Core gameplay configuration belongs to `Assets/Core`; the engine contains no Project_N IDs. Existing gameplay is not migrated yet.

| Folder | Responsibility |
| --- | --- |
| Core | Stable namespaced IDs |
| Registry | Generic typed registries, hub and shared content session |
| Content | Definitions, content registry modules/accessors, sources and loading |
| Runtime | Session bootstrap and ordered entity loading |
| Networking | Content compatibility, host identity allocation, spawn and command contracts |
| Events | Immediate local notifications and channels |
| Entity | Dynamic stats, 2D/3D Move, flexible Skill data loading and the shared Inspector |
| Tool | Reusable EveEasy utilities; caller-owned GameObject pooling over Unity ObjectPool |

`Tool/EveToolREADME.md` documents `EveEasyPooling`, its Get/Release API and lifecycle notifications. Unity owns the idle list; the wrapper handles prefab setup, ownership and cleanup. Pools are independent of Skill, entity loading and registries.

### Session and entity loading

`EveRegistrySession.Current` owns the sealed registry for a play session; `IsReady` says whether it is available. It contains definitions, not live HP, buffs or equipment state. `EveRuntime` builds a private hub using `EveContentBootstrap`, publishes it only after successful validation, then sends `EveRuntimeEventIds.Ready` with an empty `EveRuntimeReady` notification. A failed bootstrap does not expose partial content.

Content modules use `hub.GetOrCreate<T>()` while registering definitions and `registry.Register(definition)` takes the ID from the definition. Once sealed, callers use `hub.Get<T>()`; registration access through `GetOrCreate<T>()` fails.

Built-in definitions are grouped under `Content/Definitions`, one file per Stat, Character and Skill definition. `Content/Registry/EveRegistryDefinitions.cs` provides named access: `hub.Stats.Get(statId)`, `hub.Skills.Get(skillId)` and `hub.Characters.Get(characterId)`. These properties read existing typed registries; custom types still use `Get<T>()`. Generic `EveRegistry` and `EveRegistryHub` have separate files under `Registry`. The engine module lives in `Content/Registry/EveBuiltInContentModule.cs`; Project_N's module lives in `Assets/Game/Content/Registry/PNContentModule.cs`. Bootstrap, JSON loader, documents and directory sources live under `Content/Loading`.

The runtime must be on a root GameObject. `DontDestroyOnLoad` preserves it across scene changes; a runtime component in a later scene is discarded without reloading the active registry. `EndSession()` unloads registered entities (including disabled ones), invalidates the session and clears the registry. It keeps the runtime GameObject so `Initialize()` can start another session. Destroying the owner also ends the session. States are `Stopped → Starting → Ready → Stopping → Stopped`, with `Failed` and `LastError` for bootstrap errors. Failed bootstrap releases ownership, allowing retry from this or another runtime.

`EveEntityLoader` subscribes to Ready, then checks `EveRegistrySession.IsReady`. Entities created before bootstrap wait; late spawns use the published session. No loader references or searches for the runtime GameObject. Each `EveIEntityLoadStep` supplies `Dependencies`, `LoadOrder`, `Load(EveEntityLoadContext)` and `Unload()`. Dependencies are component types and must resolve to exactly one module; missing, ambiguous and cyclic dependencies fail before any step runs. Dependencies take precedence over order; independent ties use type names, never Inspector component order.

The loader records a step before calling it. If loading fails, it unloads the failed step and previous steps in reverse order, continuing cleanup even if one Unload throws. Inspect `State`/`LastError`; direct `Load` throws, Unity/event callbacks log and allow other entities to load. Incomplete cleanup blocks retry and reserves the entity ID until explicit `Unload()` succeeds. Unload must support partial initialization and repeated cleanup. Gameplay modules must gate Update/input/side effects on `loader.IsReady`; they must release listeners, spawned objects and other resources in Unload. `OnDisable` removes the Ready subscription but preserves loaded state for same-session pooling; destroy or session end unloads it.

`EveRegistrySession.Context` provides `SessionId`, registry, role, entities and compatibility. Old contexts become inactive. Cross-session pooling requires `loader.Unload()`, `identity.ResetIdentity()` and a new load; never reuse an old context. Scene changes within a session do not rebuild content. Changing mods requires ending the session.

If session cleanup fails, it remains `Stopping` with an inactive context. Failed loaders remain registered; `PendingCleanupCount` and `LastError` expose progress. New sessions are blocked. Repair the failing Unload and call `EndSession()` again, or `EveRegistrySession.RetryCleanup()` if the runtime owner has been destroyed. Only successful cleanup releases the old context and enters `Stopped`.

Every loaded entity has `EveEntityIdentity`. Offline loaders allocate negative local IDs. Host/client loaders wait for an assigned positive ID and character definition; they never allocate local IDs. `EveEntityDirectory` rejects duplicate IDs and exposes only Ready entities through `TryGet`. `CharacterId` selects a shared definition; `EntityId` identifies one instance; `OwnerId` identifies its controlling peer (0 is host). Unload the old entity before transferring its ID to a replacement on another floor. `EveStat` uses the spawn identity's CharacterId when present, otherwise its Inspector configuration.

### Coop contracts

Choose Offline/Host/Client through runtime `sessionRole` before initialization. Use `EveRegistrySession.Context.Coop`; no LAN transport is included yet. Transport must marshal calls onto Unity's main thread and supply the authenticated sender ID from the connection, not from client payloads.

1. Exchange `Compatibility` and the host SessionId. Host calls `AdmitPeer`; client calls `AdmitHost` only for its trusted host connection. Reject mismatched protocol, `RulesVersion` or content hashes before spawning. Pack hashes include manifests (even empty packs), canonical JSON and code-registered definitions; filesystem paths/whitespace/property order do not affect them. Array order is retained. Core must bump RulesVersion when executable gameplay rules change.
2. Host calls `SpawnHost(createInactive, characterId, ownerId)`. The factory must create a **fresh inactive** entity with loader, identity and modules. The engine assigns a host ID, loads modules and activates only on success; failure cleans up and destroys the new object. Send the returned `EveEntitySpawnMessage` to admitted clients, which call `ApplyHostSpawn(message, createInactive)`. Transport must accept these messages only from the host. This is initial spawning; resynchronization of already changed stats/positions is future work.
3. Register host command handlers with `RegisterCommand`. Deliver `EveEntityCommand` through `HandleCommand(authenticatedPeerId, command)`. It validates admission, host SessionId, increasing sequence per peer, Ready entity, ownership and known command. A handler must validate its gameplay payload, cooldown/range/cost and produce authoritative results. Invalid requests do not invoke handlers. Reconnect with a fresh peer ID; disconnect removes command access, while game code decides whether to despawn the player's entity.

These are transport-independent contracts, not a playable LAN implementation. Transport, world snapshots, late-join state synchronization, movement prediction and Skill/Move result replication remain future work. Local events never become network messages automatically.

Deferred by decision: after both Skill and Move are implemented, validate host-only gameplay mutations versus client application of replicated state, including direct stat setters and buff effects. This authority work is not implemented in the current stat API.

### Files and mods

Project_N configures paths in `Assets/Core/Runtime/EveGameRuntime.cs`:

- Base content: `Application.streamingAssetsPath/BaseContent` (source: `Assets/StreamingAssets/BaseContent`).
- Mods: `Application.persistentDataPath/Mods`.
- Optional selection: `Mods/enabled.json`. Without it, every immediate mod folder containing `manifest.json` is enabled automatically.

The engine's `EveDirectoryContentSource` takes directories and a selected folder list. It reads ordinary files; no ScriptableObject or Unity import is required for mods. This reader targets platforms where these directories are filesystem-accessible, including the current Windows project. Packaged Android/WebGL StreamingAssets need a different reader through `ReadContentDocuments()`.

Example layout:

```text
BaseContent/
  manifest.json
  characters/EveCharacter.json
Mods/
  example_mod/
    manifest.json
    characters/hero.json
```

To install a mod, copy its folder into `Mods`. The optional `enabled.json` can select a subset or disable all mods with an empty array:

```json
{"schemaVersion": 1, "enabledMods": ["example_mod"]}
```

Example mod `manifest.json`:

```json
{"schemaVersion": 1, "namespace": "example_mod", "version": "1.0.0", "dependencies": {"project_n": "1.0.0"}}
```

Only the mod folder is needed for the default install. Directories without a root manifest are ignored. Dependencies must already be enabled and match the exact version string; automatic installation and version ranges are not supported. Packs are ordered by dependencies, with stable name ordering. All JSON under each selected pack except its root manifest is content; files use the existing `schemaVersion: 1` document format. Unity `.meta` files are ignored. Definitions must use the pack namespace. Character stat references use `statId` (legacy `id` is still read), for example `{"statId":"project_n:health","base":100,"initialCurrent":40}`. References may target the same pack, `eve`, or a directly declared dependency; referenced definitions must exist. Unknown or duplicate JSON fields, ambiguous `id`/`statId`, and resource current values above base are errors. Duplicate namespaces/IDs, cycles, missing or mismatched dependencies, malformed content and unresolved references stop bootstrap. Changes take effect next session.

`EveIContentModule` supplies definitions from code. `EveBuiltInContentModule` registers engine definitions automatically; `PNContentModule` registers Project_N Health/Attack/Defense. Each module also exposes its stable IDs. JSON mods can add definitions using supported schemas; executable mod logic and LAN transport remain future work. Content compatibility checks are available through the coop contracts above.

### Events

`EveEventManager.Global` is always available. `Subscribe<TPayload>` / `Publish<TPayload>` use an `EveId` and optional channel. First use binds the payload type; listeners without a channel receive every channel. Dispose subscriptions on disable. Events are immediate: no registry ownership, stored Ready payload or replay. Local events do not replicate over LAN.

### Stats

`EveStatSet` owns two maps: base stats by ID and contributions by source. `SetSource` replaces one source; `RemoveSource` removes it. Group 0 adds to the base, percentages add within higher groups, and different groups multiply. `.20` means +20%. `Evaluate(attack, damage, fireDamage)` selects relevant IDs: 100 base, +50% damage and +20% fire in group 2, +10% damage in group 3 gives `100 × 1.70 × 1.10 = 187`.

Evaluated values are cached per selected ID set. Adding a base or changing a source clears the derived cache; subclasses that change values through their own paths must call `InvalidateCache()`. Resource maximums still update immediately when a source changes.

Project_N uses `PNStat.EndFloorAndCaptureSnapshot()` to remove temporary sources before recording a floor-transfer snapshot. Calling it ends the floor's temporary stat effects.

Project_N evaluates stats through `PNStatSet`: every percentage group has a minimum multiplier of zero, and the final value has a minimum of zero. Modifier values may still be negative. New signed stats will need an explicit Core policy. This gameplay rule uses `RulesVersion` `project_n:2`.

`EveResourceStat` stores Current and a maximum-change policy. `GetMax` clamps the effective maximum at zero; Current can preserve its amount or ratio when Max changes. `EveStat`, sets, definitions, entries and modifiers can be subclassed; public behavior is virtual. The shared `EveStatEditor` displays Base, Final and resource Current. Game-specific transfer rules live in Core.

`EveCharacterDefinition.Stats` is a copied read-only collection: changing the input list or casting the published list cannot mutate it. Derived definitions must preserve immutable published data when overriding virtual properties. Project_N floor snapshots include SessionId as well as EntityId and CharacterId; restore validates all three before changing state. Floor snapshots are for transfers within a live session, not cross-session saves.

### Move

`EveMove` is the abstract shared command/lifecycle base. Attach one concrete variant per entity: `EveMove2D` uses `Rigidbody2D` on XY, while `EveMove3D` uses `Rigidbody` on XZ and preserves vertical velocity during planar movement. Both require `EveEntityLoader` and a loaded `EveStat`, but no Skill or input component. The bootstrap always registers `eve:move_speed` and `eve:jump_speed`; a 2D character needs a move-speed base value, and a jumping 3D character needs both. Move reads the current effective values, so stat modifiers affect later movement and jumps. Call `SetMoveDirection` for ordinary movement and `MoveTo` for a directional dash. Use `MoveToXY`/`MoveToXZ`, their stop-distance variants, and `TransformToXY`/`TransformToXZ` for target moves and planar teleport. The shared APIs `MoveToPoint`, `MoveToPointWithStopDistance` and `TeleportToPoint` use a Vector2 on the variant's movement plane.

One special command runs at a time. A new command cancels the old one; `CommandEnded` reports ID, mode and completion, timeout or cancellation. Delayed teleport can be cancelled. Movement lock and disable stop planar motion; Unload stops all body motion and clears listeners. Special-command speeds are supplied by callers; ordinary speed comes from `eve:move_speed`. Speeds use world units per second; durations use fixed-step time. Command results are local callbacks, not network replication.

`EveMove3D` also supports physical jumping. Add a 3D `Collider` on the same object as the `Rigidbody`; without it, ground sensing stays false and `TryJump()` cannot start. The probe casts below that collider's bounds, ignores triggers and the entity's own Rigidbody, and accepts surfaces up to `MaxGroundSlope` on `GroundLayers`. Configure `GroundProbeRadius` and `GroundProbeDistance` for the collider footprint in the Inspector. `TryJump()` sets upward takeoff velocity from the current `eve:jump_speed`, returns false when airborne or `CanJump` is false, and emits `JumpStarted`. `IsGrounded`, `VerticalMotion` (`Grounded`, `Rising`, `Falling`) and `Landed` expose state transitions. Ground must be left after a jump before landing can fire; walking off an edge enters `Falling`. `CanMove` only locks planar movement, while `CanJump` controls jumping. Input buffering, coyote time, extra jumps, animation, AI, gravity and Rigidbody constraints are game decisions. Enable gravity and leave Y unfrozen for physical jumps.

## Tiếng Việt

EveEngine là mã Unity dùng lại. Cấu hình gameplay riêng nằm trong `Assets/Core`; engine không chứa ID của Project_N. Gameplay cũ chưa được chuyển sang engine.

| Thư mục | Trách nhiệm |
| --- | --- |
| Core | ID ổn định có namespace |
| Registry | Registry theo loại tổng quát, hub và phiên nội dung chung |
| Content | Định nghĩa, triển khai registry nội dung, nguồn dữ liệu và nạp |
| Runtime | Khởi tạo phiên và nạp entity theo thứ tự |
| Networking | Đối chiếu nội dung, cấp ID host, hợp đồng spawn và lệnh |
| Events | Thông báo nội bộ tức thời và channel |
| Entity | Chỉ số động, Move 2D/3D, nạp dữ liệu Skill linh hoạt và Inspector chung |
| Tool | Utilities EveEasy dùng lại; pooling GameObject bọc ObjectPool của Unity |

`Tool/EveToolREADME.md` mô tả EveEasyPooling, hook và event lấy–trả. Caller sở hữu và dọn pool; tool không tự liên kết Skill, registry hoặc vòng đời nạp entity.

### Phiên chơi và nạp entity

`EveRegistrySession.Current` giữ registry đã khóa của phiên; `IsReady` cho biết đã có dữ liệu. Registry chứa định nghĩa, không chứa HP, buff hay trang bị đang chạy của entity. `EveRuntime` dùng `EveContentBootstrap` dựng hub riêng, chỉ công bố sau khi kiểm tra thành công, rồi phát `EveRuntimeEventIds.Ready` với thông báo rỗng `EveRuntimeReady`. Bootstrap lỗi không làm lộ dữ liệu chưa hoàn chỉnh.

Runtime phải gắn trên GameObject gốc. `DontDestroyOnLoad` giữ runtime khi đổi scene; component runtime trong scene sau bị bỏ qua mà không nạp lại registry. `EndSession()` unload entity đã đăng ký (kể cả đang disable), vô hiệu phiên và xóa registry. GameObject runtime được giữ để `Initialize()` bắt đầu phiên khác. Hủy owner cũng kết thúc phiên. Trạng thái: `Stopped → Starting → Ready → Stopping → Stopped`; bootstrap lỗi có `Failed` và `LastError`. Khi bootstrap lỗi, owner được giải phóng để chính runtime đó hoặc runtime khác thử lại.

`EveEntityLoader` đăng ký Ready rồi kiểm tra `EveRegistrySession.IsReady`. Entity có trước bootstrap sẽ chờ; entity spawn muộn dùng phiên đã công bố. Loader không giữ tham chiếu hoặc tìm GameObject runtime. `EveIEntityLoadStep` có `Dependencies`, `LoadOrder`, `Load(EveEntityLoadContext)` và `Unload()`. Dependencies khai báo kiểu component, mỗi kiểu phải khớp đúng một module. Thiếu, mơ hồ hoặc vòng phụ thuộc đều lỗi trước khi chạy bước đầu. Phụ thuộc được ưu tiên trước LoadOrder; các bước độc lập cùng thứ tự được sắp theo tên kiểu.

Loader ghi nhận bước trước khi gọi Load. Khi lỗi, nó Unload cả bước lỗi và các bước trước theo thứ tự ngược, tiếp tục dọn dù một Unload lỗi. Xem `State`/`LastError`; gọi Load trực tiếp sẽ throw, callback Unity/event ghi log và cho entity khác tiếp tục nạp. Dọn chưa xong sẽ chặn retry và giữ chỗ ID đến khi gọi Unload thành công. Unload phải xử lý được khởi tạo một phần và dọn lại nhiều lần. Module gameplay phải kiểm tra `loader.IsReady` trước Update/input/tác động; Unload phải gỡ listener, object được tạo và tài nguyên khác. OnDisable chỉ gỡ listener Ready, giữ dữ liệu để pool trong cùng phiên; hủy object hoặc kết thúc phiên mới Unload.

`EveRegistrySession.Context` cung cấp SessionId, registry, vai trò, danh sách entity và dữ liệu tương thích. Context cũ bị vô hiệu khi kết thúc phiên. Pool qua phiên mới cần `loader.Unload()`, `identity.ResetIdentity()` rồi nạp lại; không dùng context cũ. Chuyển scene trong cùng phiên không nạp lại nội dung. Đổi mod cần kết thúc phiên trước.

Nếu cleanup phiên lỗi, trạng thái giữ ở `Stopping` cùng context đã vô hiệu. Loader lỗi vẫn được đăng ký; xem `PendingCleanupCount` và `LastError`. Chặn tạo phiên mới. Sau khi sửa Unload lỗi, gọi lại `EndSession()` hoặc `EveRegistrySession.RetryCleanup()` nếu owner runtime đã bị hủy. Chỉ khi dọn thành công mới bỏ context cũ và chuyển về `Stopped`.

Mỗi entity có `EveEntityIdentity`. Offline tự cấp ID âm. Loader host/client chờ ID dương và định nghĩa nhân vật được gán; không tự cấp ID local. `EveEntityDirectory` từ chối ID trùng và chỉ trả entity Ready qua TryGet. CharacterId chọn định nghĩa dùng chung; EntityId phân biệt instance; OwnerId là peer điều khiển (0 là host). Phải Unload entity cũ trước khi chuyển ID sang entity thay thế ở ải mới. EveStat ưu tiên CharacterId trong danh tính spawn, nếu chưa có mới dùng cấu hình Inspector.

### Hợp đồng coop

Chọn Offline/Host/Client qua sessionRole của runtime trước khi khởi tạo. Dùng `EveRegistrySession.Context.Coop`; chưa có transport LAN. Transport phải chuyển lời gọi về main thread Unity và cung cấp peer ID từ kết nối đã xác thực, không tin ID người gửi trong payload client.

1. Trao đổi Compatibility và SessionId của host. Host gọi AdmitPeer; client chỉ gọi AdmitHost cho kết nối host tin cậy. Khác giao thức, RulesVersion hoặc hash nội dung thì từ chối trước spawn. Hash gồm manifest (kể cả gói rỗng), JSON chuẩn hóa và định nghĩa đăng ký bằng code; không phụ thuộc đường dẫn máy, khoảng trắng hoặc thứ tự thuộc tính. Giữ thứ tự mảng. Core phải tăng RulesVersion khi luật gameplay trong code đổi.
2. Host gọi `SpawnHost(createInactive, characterId, ownerId)`. Factory phải tạo entity **mới và chưa active**, có loader, identity và module. Engine cấp ID, nạp rồi mới active; lỗi sẽ dọn và hủy object mới. Gửi EveEntitySpawnMessage trả về tới client đã tham gia; client gọi `ApplyHostSpawn(message, createInactive)`. Transport chỉ được nhận message này từ host. Đây là spawn ban đầu; khôi phục stat/vị trí đã thay đổi sẽ bổ sung sau.
3. Đăng ký handler tại host bằng RegisterCommand. Nhận EveEntityCommand qua `HandleCommand(authenticatedPeerId, command)`: kiểm tra peer đã tham gia, phiên host, sequence tăng theo peer, entity Ready, quyền sở hữu và lệnh đã biết. Handler gameplay phải kiểm tra payload, cooldown/tầm/cost và tạo kết quả có thẩm quyền. Yêu cầu sai không gọi handler. Kết nối lại dùng peer ID mới; ngắt kết nối chặn lệnh, còn giữ hay despawn nhân vật do game quyết định.

Đây là hợp đồng độc lập transport, chưa phải LAN chơi được. Transport, snapshot thế giới, đồng bộ người vào muộn, dự đoán di chuyển và truyền kết quả Skill/Move sẽ bổ sung sau. Event nội bộ không tự thành message mạng.

Đã thống nhất hoãn: sau khi triển khai xong cả Skill và Move, kiểm chứng quyền host thay đổi gameplay và đường client áp dụng trạng thái đồng bộ, gồm setter stat trực tiếp và hiệu ứng buff. API stat hiện tại chưa triển khai giới hạn quyền này.

### File và mod

Project_N cấu hình đường dẫn trong `Assets/Core/Runtime/EveGameRuntime.cs`:

- Nội dung gốc: `Application.streamingAssetsPath/BaseContent` (mã nguồn: `Assets/StreamingAssets/BaseContent`).
- Mod: `Application.persistentDataPath/Mods`.
- File chọn mod tùy chọn: `Mods/enabled.json`. Nếu chưa có file, mọi thư mục mod cấp đầu có `manifest.json` sẽ tự được bật.

`EveDirectoryContentSource` nhận các thư mục và danh sách folder mod. Nó đọc file thông thường; mod không cần ScriptableObject hoặc import vào Unity. Reader hiện dùng cho nền tảng truy cập các thư mục này qua filesystem, gồm Windows đang phát triển. StreamingAssets đóng gói trên Android/WebGL cần reader khác qua `ReadContentDocuments()`.

Cấu trúc thư mục và mẫu JSON ở phần English phía trên áp dụng chung. Để cài mod theo mặc định, chỉ cần chép thư mục mod có manifest và các file nội dung vào `Mods`. Thư mục không có manifest gốc được bỏ qua. Nếu muốn chọn một phần hoặc tắt hết mod, thêm `enabled.json` với `schemaVersion: 1` và danh sách `enabledMods` (dùng mảng rỗng để tắt hết). Manifest khai báo namespace, version và dependencies.

Các gói phụ thuộc phải được bật sẵn và đúng chuỗi phiên bản; chưa tự cài phụ thuộc hoặc hỗ trợ khoảng phiên bản. Gói nạp theo phụ thuộc và tên ổn định. Mọi JSON trong gói được bật, trừ manifest ở gốc, đều là nội dung theo schemaVersion 1 hiện có. Bỏ qua `.meta`. Định nghĩa phải thuộc namespace của gói. Tham chiếu stat trong nhân vật dùng `statId` (vẫn đọc `id` cũ), ví dụ `{"statId":"project_n:health","base":100,"initialCurrent":40}`. Tham chiếu chỉ được trỏ tới chính gói đó, `eve`, hoặc gói được khai báo trực tiếp trong dependencies; định nghĩa được trỏ tới phải tồn tại. Trường JSON không biết hoặc trùng, khai báo đồng thời `id`/`statId`, và current resource lớn hơn base đều báo lỗi. Trùng namespace/ID, vòng phụ thuộc, thiếu hoặc sai phiên bản phụ thuộc, JSON lỗi và tham chiếu thiếu đều dừng bootstrap. Thay đổi có hiệu lực từ phiên sau.

`EveIContentModule` cung cấp định nghĩa từ code. `EveBuiltInContentModule` tự đăng ký định nghĩa của engine; `PNContentModule` đăng ký Health/Attack/Defense của Project_N. Mỗi module cũng công bố các ID ổn định của mình. Mod JSON có thể thêm định nghĩa theo schema đã hỗ trợ; logic mod thực thi và transport LAN sẽ làm sau. Đối chiếu nội dung đã có qua hợp đồng coop phía trên.

### Event

`EveEventManager.Global` luôn sẵn sàng. `Subscribe<TPayload>` / `Publish<TPayload>` nhận `EveId` và channel tùy chọn. Lần dùng đầu cố định kiểu payload; listener không có channel nhận mọi channel. Gỡ subscription khi disable. Event phát tức thời, không sở hữu registry, không lưu hoặc phát lại Ready. Event nội bộ không tự truyền qua LAN.

### Chỉ số

`EveStatSet` có hai map: chỉ số nền theo ID và đóng góp theo nguồn. `SetSource` thay một nguồn; `RemoveSource` gỡ nguồn. Nhóm 0 cộng vào nền, phần trăm cùng nhóm lớn hơn 0 cộng nhau, các nhóm khác nhau nhân nhau. `.20` nghĩa là +20%. `Evaluate(attack, damage, fireDamage)` chọn ID liên quan: nền 100, damage +50% và fire +20% nhóm 2, damage +10% nhóm 3 cho `100 × 1.70 × 1.10 = 187`.

Giá trị tính ra được cache theo tập ID đã chọn. Thêm chỉ số nền hoặc đổi nguồn đóng góp sẽ xóa cache suy ra; lớp kế thừa thay đổi giá trị theo đường riêng cần gọi `InvalidateCache()`. Giới hạn resource vẫn được cập nhật ngay khi nguồn thay đổi.

Module nội dung dùng `hub.GetOrCreate<T>()` trong lúc đăng ký và `registry.Register(definition)` tự lấy ID từ definition. Sau khi registry được seal, bên đọc dùng `hub.Get<T>()`; gọi `GetOrCreate<T>()` sẽ báo lỗi. Project_N dùng `PNStat.EndFloorAndCaptureSnapshot()` để gỡ các nguồn tạm trước khi chụp dữ liệu chuyển ải.

Các định nghĩa Stat, Character và Skill nằm trong `Content/Definitions`, mỗi loại một file. Script phụ `Content/Registry/EveRegistryDefinitions.cs` cung cấp cách đọc ngắn: `hub.Stats.Get(statId)`, `hub.Skills.Get(skillId)` và `hub.Characters.Get(characterId)`. Các thuộc tính đọc registry theo loại đã tồn tại; nội dung riêng tiếp tục dùng `Get<T>()`. Cơ chế tổng quát `EveRegistry` và `EveRegistryHub` có file riêng trong `Registry`. Module engine nằm ở `Content/Registry/EveBuiltInContentModule.cs`; module Project_N ở `Assets/Game/Content/Registry/PNContentModule.cs`. Bootstrap, loader JSON, document và nguồn thư mục nằm trong `Content/Loading`.

Project_N tính chỉ số qua `PNStatSet`: hệ số của từng nhóm phần trăm và kết quả cuối có giới hạn dưới bằng 0. Giá trị modifier vẫn có thể âm. Nếu thêm chỉ số cần giá trị âm, Core phải định nghĩa quy tắc riêng cho nó. Luật gameplay này dùng `RulesVersion` `project_n:2`.

`EveResourceStat` giữ Current và quy tắc khi Max đổi. `GetMax` chặn giới hạn hiệu lực tại 0; Current có thể giữ lượng hoặc tỷ lệ. Có thể kế thừa `EveStat`, set, definition, entry và modifier; hành vi công khai là virtual. `EveStatEditor` chung hiển thị Base, Final và Current của resource. Quy tắc chuyển ải riêng nằm trong Core.

`EveCharacterDefinition.Stats` sao chép đầu vào và bọc collection chỉ đọc: sửa list đầu vào hoặc ép kiểu list đã công bố đều không đổi được nội dung. Lớp kế thừa phải giữ tính bất biến của dữ liệu công bố khi override thuộc tính virtual. Snapshot chuyển ải Project_N ghi SessionId cùng EntityId và CharacterId; restore kiểm tra cả ba trước khi thay đổi dữ liệu. Snapshot dùng để chuyển ải trong cùng phiên đang chạy, không dùng làm save qua phiên khác.

### Di chuyển

`EveMove` là lớp trừu tượng dùng chung logic lệnh và vòng đời. Mỗi entity gắn một bản cụ thể: `EveMove2D` dùng `Rigidbody2D` trên XY; `EveMove3D` dùng `Rigidbody` trên XZ và giữ vận tốc Y khi di chuyển trên mặt phẳng. Cả hai cần `EveEntityLoader` và `EveStat` đã nạp, nhưng không cần Skill hay component input. Bootstrap luôn đăng ký `eve:move_speed` và `eve:jump_speed`; nhân vật 2D cần giá trị nền tốc độ đi, nhân vật 3D có nhảy cần cả hai. Move đọc giá trị hiệu lực hiện tại nên modifier tác động đến lần di chuyển hoặc nhảy kế tiếp. Gọi `SetMoveDirection` để đi thường và `MoveTo` để lao theo hướng. Dùng `MoveToXY`/`MoveToXZ`, biến thể dừng gần đích và `TransformToXY`/`TransformToXZ` để đi tới điểm hoặc dịch chuyển trên mặt phẳng. API chung `MoveToPoint`, `MoveToPointWithStopDistance` và `TeleportToPoint` dùng Vector2 theo mặt phẳng của từng bản.

Mỗi lúc chỉ chạy một lệnh đặc biệt. Lệnh mới hủy lệnh cũ; `CommandEnded` báo ID, kiểu và kết quả hoàn tất, hết thời gian hoặc bị hủy. Dịch chuyển có delay có thể bị hủy. Khóa hoặc disable dừng chuyển động trên mặt phẳng; Unload dừng toàn bộ body và xóa listener. Bên gọi cung cấp tốc độ cho lệnh đặc biệt; tốc độ đi thường lấy từ `eve:move_speed`. Tốc độ dùng đơn vị thế giới mỗi giây, thời hạn lệnh dùng thời gian bước vật lý cố định. Kết quả lệnh là callback nội bộ, chưa đồng bộ mạng.

`EveMove3D` hỗ trợ nhảy vật lý. Gắn `Collider` 3D cùng GameObject với `Rigidbody`; thiếu collider thì dò đất luôn sai và `TryJump()` không thể bắt đầu. Probe quét bên dưới bounds của collider, bỏ qua trigger và Rigidbody của entity, chỉ nhận mặt thuộc `GroundLayers` và không dốc hơn `MaxGroundSlope`. Chỉnh `GroundProbeRadius` và `GroundProbeDistance` theo kích thước collider trong Inspector. `TryJump()` đặt vận tốc bật lên từ `eve:jump_speed` hiện tại, trả false khi ở trên không hoặc `CanJump` bị khóa, và phát `JumpStarted`. `IsGrounded`, `VerticalMotion` (`Grounded`, `Rising`, `Falling`) và `Landed` báo trạng thái. Sau khi nhảy, entity phải rời mặt đất rồi mới có thể đáp; bước khỏi mép vực chuyển sang `Falling`. `CanMove` chỉ khóa chuyển động ngang; `CanJump` khóa nhảy riêng. Đệm nút, coyote time, nhảy thêm lần, animation, AI, trọng lực và constraints của Rigidbody do game quyết định. Bật gravity và không khóa Y khi cần nhảy vật lý.

### Skill / Kỹ năng

EN: `EveSkill` loads the optional `characters[].skill` list, whose entries contain only `id` and flexible `data`. Core supplies payload schemas and execution. See [EveSkillREADME](Entity/Skill/EveSkillREADME.md) for registration, loading overrides, JSON isolation and Core responsibilities.

VI: `EveSkill` nạp danh sách `characters[].skill` tùy chọn gồm `id` và `data` linh hoạt. Core quy định mẫu dữ liệu và thực thi. Xem [EveSkillREADME](Entity/Skill/EveSkillREADME.md) về đăng ký, override nạp, cô lập JSON và trách nhiệm Core.

### P1 S3: required play-service cleanup

Game subclasses bind play services in `EveRuntime.BindSession(context)` before the
Ready event. Register required teardown with `context.RegisterCleanup(Action)` before
initialization can fail. End invalidates the context, notifies Stopping observers, then
attempts all registered cleanup actions in reverse order before entity unload and mod
EndPlay. Failed actions remain in Stopping for RetryCleanup; successful ones are removed.
Stopping events are optional notifications and log observer errors; they do not replace
required cleanup. Content remains loaded between successful plays.

## Mirror foundation P1 (2026-10-16)

`EveEntityPreparationScheduler` owns factory candidates from borrow through successful return, including failures before a cleanup lease exists. `EvePreparationProfile` selects global elapsed prepare/cleanup budgets and capacity limits per play. Prepare has no minimum-step exception; nonpreemptible callback overruns consume the frame budget. Commit has a warning threshold only unless an independent caller explicitly selects another policy. `AdmissionCount` retains failed retiring objects until return; `ResidentCount` includes every borrowed object not returned.

`EveBindingInbox` bounds pre-Ready state/events per binding and globally. Latest state replaces one slot; required events retain FIFO order and accounting until successful application. Local binding tokens fence stale callbacks independently of identity generation.

`EveSceneEntityKey` stores stable scene/entity metadata in authored content; validation rejects missing/duplicate pairs. It does not allocate runtime entity IDs. `EveSessionContext.RegisterCleanup(Action)` retains its existing signature; the additional `(Action, bool afterEntities)` overload supports owners such as factories whose terminal disposal must follow entity return. Failed required cleanup remains retryable and blocks a new play; automatic pumping does not repeatedly rerun a failed service callback.

Mirror lives in Coop/Composition. These engine contracts do not depend on Mirror, KCP or Project_N character IDs. Fresh integration evidence and limits are recorded in `Docs/MirrorP1S4S10Implementation.md` and the newest handoff.
