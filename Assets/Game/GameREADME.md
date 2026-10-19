# Game

VI: Hitbox mới: `Assets/EveEngine/Hitbox/EveHitboxREADME.md`. Engine dò collider,
Core PNHitboxTargets tìm entity/gộp ID, Game PNHitboxCast giữ lịch trúng từng cast.
Action sở hữu hit window, clock và damage. Eve slash đã nối Sector và damage;
xem Assets/Game/Skill/Actions/Slash/PNSlashCombatREADME.md.

EN: Hitboxes separate Engine collider queries, Core entity resolution and Game
invocation hit history. Eve slash now applies sector-based damage; see PNSlashCombatREADME.

VI: DLL mod Windows Mono và SDK: `Assets/EveEngine/Mods/EveModsREADME.md`.
Assembly phân lớp: EveEngine → ProjectN.Core → ProjectN.Game.
ProjectN.Input chứa PlayerControls và inputactions; Core tham chiếu assembly này.
EveGameRuntime nằm trong Game/Runtime, giữ GUID script cũ. PNArcSlash dùng asmref
ProjectN.Game. ID dùng chung của game nằm trong Core/Content/Registry/PNContentIds.cs.
Registry Core truy cập bằng extension method như `hub.Actions()`.

EN: Windows Mono mods/SDK are documented in EveModsREADME. Assemblies follow
Engine → Core → Game, with generated controls in ProjectN.Input. EveGameRuntime
now belongs to Game/Runtime; its existing script GUID is preserved.

VI: Registry công khai mới dùng `base:` cho tham chiếu Engine/Game. Xem
`Assets/Core/Content/Registry/PNBaseRegistryREADME.md` để dùng binding, override và
cấu hình chọn provider. ID định nghĩa gốc và danh tính runtime vẫn được giữ.

EN: Public registry references now use `base:` across Engine/Game. See
`Assets/Core/Content/Registry/PNBaseRegistryREADME.md` for bindings, override declarations
and provider selection. Original definition IDs and runtime identities are preserved.

VI: Thư mục gốc cho nội dung và triển khai của bản game mới. EveEngine cung cấp
framework dùng lại; Core cung cấp lớp nền và luật chung Project_N; Game chứa
nhân vật và các hành vi cụ thể được xây trên Core.

EN: Root for the new game's content and implementations. EveEngine provides the
reusable framework, Core provides Project_N base classes and shared rules, and
Game contains characters and concrete behavior built on Core.

| Folder | Purpose / Mục đích |
| --- | --- |
| Entity/Character/Eve | Eve character scripts / Script nhân vật Eve |
| Skill/Actions/Slash | Concrete slash action / Action chém cụ thể |
| Skill/Operations/Movement | Reusable movement operations / Operation di chuyển dùng chung |
| Skill/Operations/VFX | Reusable VFX operations / Operation VFX dùng chung |
| Content/Registry | Game stat, combat and implementation modules and IDs / Module stat, combat và triển khai cụ thể của game cùng ID |

VI: Action/Operation dùng chung cho Player và Enemy, không đặt riêng dưới Eve.
Runtime đăng ký PNContentModule rồi PNGameplayContentModule; cả hai module nằm
trong Game/Content/Registry. Core giữ hợp đồng registry. Các ID, tên class và
RulesVersion không đổi khi tổ chức lại thư mục. JSON giữ trong StreamingAssets;
prefab Resources và VFX giữ đường dẫn hiện tại. Test thuộc Assets/Tests.

EN: Actions/operations are shared by players and enemies rather than owned by Eve.
The runtime registers PNContentModule followed by PNGameplayContentModule.
Both modules live in Game/Content/Registry; registry contracts remain in Core.
Folder organization preserves IDs, class names and RulesVersion. JSON remains in
StreamingAssets; Resources prefabs and VFX retain their paths. Tests belong in Assets/Tests.

VI: PNContentModule đăng ký loại damage Physical/Magic/True và nguyên tố Fire/Ice/Lightning
trong hai registry riêng. Xem Content/Registry/PNCombatRegistryREADME.md để tra ID và API.

EN: PNContentModule registers Physical/Magic/True damage types and Fire/Ice/Lightning
elements in separate registries. See Content/Registry/PNCombatRegistryREADME.md for IDs and API.

VI: PNContentModule cũng đăng ký đường tính Scale/Mitigation bằng factory Core.
Modifier dùng CalculationId để lọc trước khi gộp nhóm; mod code có thể thêm đường tính
và logic mới. README ở Assets/Core/Entity/Shared/Stats/Calculation.

EN: PNContentModule also registers Core's Scale/Mitigation calculation factories.
Modifier CalculationId is filtered before grouping; code mods can add new routes
and logic. Documentation lives in Assets/Core/Entity/Shared/Stats/Calculation.

## Camera director (play session)

`PNCameraDirector` (Assets/Game/Runtime/Camera) owns the one rendering camera of a play session (output Camera + Brain + AudioListener tagged MainCamera under the
runtime root). World `MainCamera` groups are claimed from `PNWorldPresentation` (`Claim`/`Release`, original state returned under current view visibility); only the
viewed world's virtual cameras are admitted to the output through a reserved Brain channel chosen at runtime. Use `PNViewCamera.Current` (Core) for popups, buff visuals and
UI instead of `Camera.main`; subscribe to `PNViewCamera.Updated` for work that must run after Cinemachine wrote the camera. `--pn-legacy-camera` skips the director.
`PNTransitionController` (same folder) opens one transition screen per local-view change (join, own-actor transfer, world lost),
closes it when the view, actor, input gate and a rendered frame are ready (real time, min/max display), and isolates a failing
screen. `IPNCameraService` (Core) is implemented by the director: shake on a private impulse channel (director-owned events only),
zoom (player base + modifiers, composed from the base each frame and clamped by `PNCameraProfile`), camera-only lock-on rig,
cinematic rig with runtime blend asset (shots, sequences, waypoint paths, aim), opt-in yaw (billboards/shadows follow only while a yaw override is live),
bounds (Confiner3D), follow tuning, free orbit, per-request ease and clock, a state snapshot, and handles that report why they ended.
Full API and rules: `.agents/skills/project-n-camera`.

## Roguelite flow: Hall, lobby, run (P5 L1-L5)

Scenes live in `Assets/Game/Scenes/` (`Shared/PNHall`, `Roguelite/PNLobby`, `Test/ErMode`); paths are constants in `PNScenes`
(`PNEntrySceneMarker.cs`) and each is a registered world: `project_n:hall_world` (`base:hall_world`),
`project_n:roguelite_lobby` (`base:roguelite_lobby`, empty policy Discard) and `project_n:scene_world` (the run, ErMode).
- **Entry scene marker.** `PNEntrySceneMarker` on a root object of a startup/entry scene names its world definition and, for an
  Offline start without `--pn-mode`, the mode (`hall` for PNHall). No marker = old contract (`base:scene_world`, configured mode).
  `PNGameSession` records the entry definition in `PNWorldRuntime.SetEntryScene(scene, definition)`; a client's default-world
  replica is matched against that definition, so host and client must have loaded the same entry scene before connecting.
- **Modes.** `project_n:hall` (`PNHallPolicy`, offline home) and `project_n:roguelite`; `PNPlayConfig.ModeExplicit` is true only
  when `--pn-mode` was given.
- **Coordinator.** `PNGameFlow` (static API on a persistent component): `StartRoguelite(Offline|Host|Join, address)`,
  `ReturnToHall(notice)`, `CanStartRun/StartRun`. Every scene change first ends the current play and waits for Stopped and an idle
  room driver, then loads the scene (single mode) and begins the play. Offline goes straight to the run scene; Host/Join go to
  the lobby. `StartRun` (host) locks admissions, creates a `base:scene_world` instance, moves every lobby player with the group
  transfer (slot = the one carrying the `player` spawn, scene worlds name slots `chunk-N`), then marks the room started.
  `PNGameFlow.Phase` is derived from the local world's definition. A client whose room closes, or a play that ends unexpectedly,
  returns to the Hall with a notice. Nothing in the run ends it yet (no win/death flow): leave through the pause menu.
- **Neutral lobby/LAN contract** is `Assets/Core/PNLobbyContracts.cs` (`IPNLobbyDriver`, `PNLobbyInfo`, `PNLanRoomInfo`);
  `PNMirrorDriver` implements it next to `IPNRoomSessionDriver`. Game never references Mirror.
- **World scenes carry no `EventSystem` and no `Global Light 2D`.** Several worlds coexist (lobby + run), and URP 2D allows one global light per layer / Unity one
  EventSystem; the cameras render with `UniversalRenderer3D`, so 2D lights have no effect anyway. `PNUiRuntime` owns the single EventSystem (created only if no
  scene provides one). Do not author either into a world scene.
- UI: `PNModeMenu` (see `UI/PNUIREADME.md`). Limits: the HUD is rebuilt per session, so there is no overlay while a scene loads;
  the first transition screen can still be closing when the Hall appears.
