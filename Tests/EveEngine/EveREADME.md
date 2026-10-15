
## Current: Buff reliability and presentation coop (RulesVersion66 / Mod API5)

Buff registration requires explicit ImplementationId; Derive preserves it. Buff and
Area reference schemas use validateReferences/PNContentReferenceContext, including
dependency/alias/staged batch checks; property-name inference was removed. Nested
Action/Operation schemas may delegate their registered reference callbacks.
Stat resource max callbacks run only when max actually changes. Buff stat-source
keys use checked instance+sequence, retained through behavior changes/resume.
Automatic Buff manager clock/runtime failures retire only its collection and cleanup;
caller argument errors and per-behavior callback errors retain separate handling.

Buff gameplay is authoritative. Host/solo and client UI/VFX use a common replica
presentation source; UI Numbers and VFX clock data are separate. Elemental VFX is
presenter-owned/grouped, not acquired by gameplay behavior. No production transport,
Move/Skill/HP/Area replication is implied. See Assets/Core/Entity/Shared/Buff/PNBuffCoopREADME.md for epochs/sequences,
resync/cues/presenter scope and trust limits. Earlier no-replication statements refer
to the former local contract; the new transport-independent proxy is implemented.

# Eve smoke checks

RulesVersion61 Buff presentation: PNBuffPresentationTests exercises full four-slot
publication, nullable/zero/negative values, immutable cache, no duplicate events,
instance/source grouping, stable group order, raw-clock freeze/integer countdown,
suspend/resume, full override/stale writers, failed apply, observer reentrancy/faults
and disposal. State snapshot and all existing gameplay tests remain independent.
Native PNBuffEditorChecks and actual PNBuffPlayReview cover gameplay regression.
PNBuffUIPlayReview.Run in Ready unpaused Play verifies real Toolkit five-column
player/enemy layout, four numbers, Burn fallback, pointer picking transparency,
suspend and cleanup. Evidence: .utmp/BuffUIReview; run in selected Game/Simulator,
preserving authored scene and view settings. Counts below are historical.

2026-10-06 control foundation: fresh PASS1205 smoke + PASS25 DLL checks.
PNControlTests covers registry/aliases/custom rights, atomic leases, authority,
observer failure, overlapping locks/base permissions, all five direct Try gates,
global/exact entry gates, no resource loss, facing, motion, menu, interruption and
stale handles. Native PNControlEditorChecks PASS36 and shared Dash PASS16; actual
Play review PASS11 with held keyboard walking, PhysX/Dash, release and disable.
Evidence: .utmp/ControlReview. RulesVersion43/API2; SDK exported/example built.
Native DLL helper and Player builds were not rerun. Earlier counts are history.

2026-10-05 latest verification: PASS1146 smoke + PASS25 real DLL checks for registered
enemy classification (RulesVersion42/API2). PNEnemyTypeEditorChecks.Run passed33
native Unity checks in a disposable preview scene. Evidence:
.utmp/EnemyTypeReview/{SmokeResults,NativeResults}.txt. SDK exported successfully;
existing native DLL helper and Player builds were not rerun for this change.
Older dated entries below describe historical verification, not this run.

2026-10-05 fresh verification: PASS1067 smoke + PASS25 real DLL checks for multi-
component values and Heal (RulesVersion41/API2). Unity PNHealEditorChecks.Run passed
14 native checks; RunValueAction and real Mono DLL checks also freshly passed.
Coverage includes component arrays and dependencies, custom non-aggregation,
shared Scale/Mitigation heal, cap/dead/zero, selected overrides, authority/session,
immutable snapshots, callbacks, cancellation and partial delivery. SDK/native fixture
rebuilt successfully. See .utmp/ValueHealReview/Results.txt and HANDOFF.md.
Counts below are historical; Player builds/CoreCLR remain unverified.

2026-10-04 fresh verification: PASS995 smoke + PASS25 real DLL checks for Scale
group-0 flats (RulesVersion40/API2). Coverage includes add-before entity stats,
add-after non-stat flats, Evaluate isolation, aliases, atomic source validation,
resources/cache/floor snapshots and selected overrides/legacy helpers/nested calls.
Unity PNDamageFlowEditorChecks.RunFlatScale passed12 native component checks; existing
Run/RunValueAction and native Mono DLL checks also rerun successfully. See
[verification results](../../.utmp/FlatScaleReview/Results.txt) and HANDOFF.md;
older counts below are historical. Player builds/CoreCLR remain unverified.

VI: Bộ mới nhất PASS671 checks (615 cũ +56 mới). EveRegistryBindingTests kiểm tra
base dùng chung Engine/Game, danh tính ổn định, override/contract/dependency, JSON
rollback, conflict/selection/khóa session, stat/tag/cache/resource và hash coop.

EN: Latest suite PASS671 checks (615 previous +56 new). EveRegistryBindingTests covers
shared public Engine/Game references, stable identity, override contracts/dependencies,
JSON rollback, conflict selection/sealing, stat/tag/cache/resource behavior and coop hashes.

VI: Bộ mới nhất PASS 615 checks, gồm skill chọn hướng di chuyển cho input chém,
context chuẩn hóa/chụp aim, hướng chém liên tục 25 độ, idle/lock giữ hướng,
reset khi unload và dữ liệu HP/ATK/DEF cùng skill rỗng của dummy.
Kiểm tra mod dùng số nhân vật base thực tế, không cố định chỉ có Eve.
PNDummyEditorChecks.Run kiểm tra bản sao dummy trong scene bằng Unity thật.
PNSkillValueTests thêm kiểm tra raw/stat scaling,
dữ liệu sai, stat thiếu, overflow, đọc buff tại Perform, bonus một lần, mục tiêu lặp,
target chưa Ready, partial failure/retry, cancel và unload. Unity native
PNDamageFlowEditorChecks.RunValueAction xác nhận action bằng component thật.

EN: Latest suite: 615 passing checks, including skill-selected movement facing
for input attacks, explicit aim overrides, direction validation/normalization and continuous slash facing,
idle/lock preservation, unload reset, dummy JSON stats and empty skills.
Mod checks use the current base character count. PNDummyEditorChecks.Run verifies
the migrated scene dummy with real components in a disposable preview scene.
Value/action coverage includes parsing,
raw/percentage scaling, live buffs, overflow, explicit targets, deduplication,
partial failure/retry safety and lifecycle cancellation. RunValueAction provides
opt-in native component verification.

VI: Bộ mới nhất PASS 542 checks. PNDamageFlowTests kiểm tra payload bất biến, gửi/nhận,
snapshot bonus, DEF/kháng/HP, overkill, death, callback lỗi, override, authority,
session cũ và unload/reload. PNDamageFlowEditorChecks.Run là kiểm tra Unity thật
tường minh trong preview scene dùng tạm; không chạy tự động hoặc lưu scene gameplay.

EN: Latest suite: 542 passing checks. Damage flow coverage includes immutable packets,
bonus snapshots, HP/death, observer errors, overrides, authority, stale sessions and
reload boundaries. PNDamageFlowEditorChecks.Run provides opt-in native component
checks in a disposable preview scene, without saving gameplay scenes.

VI: Bộ mới nhất PASS 481 checks. PNTagTests thêm kiểm tra registry và mod tag tổng quát,
bonus/kháng, group 0, metadata từng hook, copy/read-only, dữ liệu sai, action không
value, loader rollback và hash coop.

EN: Latest suite: 481 passing checks. PNTagTests covers extensible registration,
bonuses/resistance, group 0, per-hook metadata, immutable copies, malformed/unknown
tags, actions without values, loader rollback and coop hashes.

VI: Bộ mới nhất PASS 442 checks. PNDamageCalculationTests thêm kiểm tra K=500,
DEF có dấu, factor Physical/Magic/True và mod, kháng theo tags sau DEF, cache,
giới hạn input/overflow, HP không đổi và hash factor cho coop.

EN: Latest suite: 442 passing checks. PNDamageCalculationTests covers K=500,
signed DEF, built-in/mod type factors, tagged mitigation after DEF, caches,
input/overflow validation, unchanged HP and coop factor fingerprints.

VI: Bộ mới nhất PASS 397 checks. PNStatCalculationTests kiểm tra lọc Scale/Mitigation,
bonus/kháng dùng chung source, nhóm 0, nguyên tố/loại damage, cache/resource, mod với
logic mới, factory/hash coop và giữ đường tính khi chuyển ải.

EN: Latest suite: 397 passing checks. PNStatCalculationTests covers Scale/Mitigation
isolation, shared-source bonuses/resistance, group 0, element/damage IDs, caches,
resources, mod-defined formulas, factory/coop hashes and routed floor snapshots.

VI: PNCombatRegistryTests kiểm tra hai registry damage/nguyên tố, ID mặc định, ID không hợp lệ,
trùng ID, mở rộng qua code module, khóa registry và hash tương thích coop. Bộ smoke mới nhất PASS 362 checks.

EN: PNCombatRegistryTests covers separate damage/element registries, built-in and invalid IDs,
duplicates, code-module extension, sealing and coop compatibility hashes. Latest smoke suite: 362 passing checks.

VI: Cập nhật slash pooling: bộ smoke PASS 348 checks, kiểm tra reuse, setup failure, disable, unload/reload và destroy owner. Play Mode input J thật PASS: cùng VFX/mesh được dùng lại, Exit/Cancel trả pool và unload hủy mesh.

EN: Latest full suite: 348 passing checks. Pooled slash coverage includes reuse, failed setup, disable, unload/reload and owner destruction. Live Play Mode input integration separately confirms mesh reuse and teardown.

## EveEasyPooling

VI: `EveEasyPoolingTests.cs` kiểm tra wrapper Get/Release, giữ chỗ khi lấy inactive object, trả hai lần/nhầm pool, ReleaseAll/Clear/Dispose, maxSize chỉ giới hạn object rảnh, ổn định qua các đợt 800 object và observer lỗi/đệ quy. Bộ smoke hiện PASS 339 checks. Tests/UnityObjectPoolStub chỉ thay thế Unity ngoài Editor. `EveEasyPoolingEditorChecks.Run()` PASS với ObjectPool Unity thật: hook/reset, lỗi hook, external destroy, setup trước OnEnable, ba đợt 800 object chỉ tạo 800 instance và cleanup. Không tự chạy; không lưu scene gameplay.

EN: Smoke verifies wrapper ownership, retention and cleanup using a test-only ObjectPool substitute. Opt-in real Unity Editor checks independently verify native ObjectPool behavior, lifecycle hooks and repeated large bursts. This is correctness validation, not a gameplay performance benchmark. The previous generic pool and rental handles have been removed.

## TimeScale / Hệ số thời gian

EN: `EveTimeScaleTests.cs` covers independent 5/7-second expiry, additive rates and floor penetration, masked modifiers, zero and above-one rates, paused clocks, owned-effect cleanup, stable IDs, invalid inputs, reset callback isolation, scaled movement/dash/jump events, gravity compensation, freeze/resume, pending teleport, unload and Player immunity configuration. These checks use Unity substitutes; they do not simulate PhysX contacts. The Core/engine sources also compile against the project's Unity reference assemblies.

VI: `EveTimeScaleTests.cs` kiểm tra hết hạn riêng 5/7 giây, cộng dồn hệ số và giảm ngưỡng, modifier bị che bởi immunity, hệ số 0 và lớn hơn 1, pause, dọn owner, ID không tái sử dụng, dữ liệu sai, callback khi reset, di chuyển/dash/nhảy theo thời gian, bù gravity, dừng/tiếp tục, teleport chờ, unload và ngưỡng Player. Dùng Unity thay thế nên không xác nhận va chạm PhysX; mã Core/engine cũng được biên dịch với thư viện tham chiếu Unity của project.

## English

Run from the project root with PowerShell 7 and the .NET SDK installed:

```powershell
./Tests/EveEngine/EveRunSmokeTests.ps1
```

Latest: PASS680 regular checks + PASS25 DLL checks. DLL fixtures compile separately
and exercise dependent-library execution, override registration, backend/API/path/
host-copy rejection, hash compatibility, partial initialization cleanup, retry and
restart requirements. Each DLL test process owns its loaded assembly state.

Hitbox physics uses real Unity via PNHitboxEditorChecks.Run(): PASS82 checks covering
eight shapes, collider boundary intersection, thin/tilted regions, sector holes/wide
angles, scaled targets, continuous direction, crowded buffers, layers/triggers,
entity filtering/dedupe and per-cast once/repeat/session lifetime, ellipse axes,
cone taper/cap, concave polygon triangulation/winding/immutability/limits and Inspector.
Hitbox folders
are excluded from this stub runner; it does not simulate PhysX. Gameplay scene is
preserved by the native preview-scene checks.

PNSlashCombatEditorChecks.Run(): PASS20 real Eve JSON/VFX/PhysX -> Dummy HP checks:
hit windows/hitches/pause, late entrants, teams/dedupe, snapshot damage/captured aim,
cancel/callback/receiver failure cleanup and client authority. PNSlashHitOperationStub
is a no-hit substitute for the .NET runner; it verifies action transitions rather
than real combat. Nine new smoke checks validate combat slash payload rejection.

Real Unity SDK/Mono verification: export SDK with Tools/Mods/ExportModSdk.ps1,
run Tests/EveEngine/BuildNativeModFixture.ps1, then opt-in
EveDllModEditorChecks.Run() in idle Edit Mode. Passed on Windows Unity 6000.6.3f1;
player builds/CoreCLR remain unverified. No scene save/reload is required.

The runner compiles actual EveEngine/Core/Eve test scripts plus minimal Unity/Editor substitutes, using Newtonsoft from Unity's package cache. It does not require a NuGet restore or change Library. Build outputs and JSON fixtures use the system temp directory. Checks cover session lifecycle, loader ordering, late spawn, two instances of one character, wrong-owner snapshot rejection, correct floor transfer, automatic mod discovery, optional selection, dependency validation, namespace ownership, invalid references and failed-bootstrap retry. These checks do not simulate Unity's scene manager or prove Inspector/Play Mode behavior.

## Tiếng Việt

Chạy lệnh phía trên từ gốc project bằng PowerShell 7, máy đã cài .NET SDK.

Runner biên dịch mã EveEngine/Core/Eve thật cùng API Unity/Editor thay thế tối thiểu, dùng Newtonsoft từ cache package Unity. Không cần NuGet restore, không sửa Library. File build và JSON kiểm thử nằm trong thư mục temp hệ thống. Kiểm tra vòng đời phiên, thứ tự loader, spawn muộn, hai instance cùng nhân vật, từ chối snapshot sai người, chuyển ải đúng instance, tự tìm mod, file chọn mod tùy chọn, phụ thuộc, namespace, tham chiếu sai và thử lại bootstrap sau lỗi. Bộ kiểm tra không mô phỏng scene manager của Unity và không xác nhận Inspector/Play Mode.

## Additional lifecycle and coop coverage / Bổ sung kiểm tra vòng đời và coop

EN: `EveLifecycleTests.cs` checks partial-step rollback, cleanup failure/retry, missing/ambiguous/cyclic dependencies, owner recovery, listener isolation, disabled-entity teardown, session identity reset, duplicate IDs, canonical content/manifest/code definition hashes, protocol/rules mismatch, host/client spawn, ownership and replay rejection. Coop is tested in process without a LAN transport. Eve scripts and Inspector were also compiled against Unity 6000.3.10f1 references; Play Mode and real scene transitions remain unverified.

VI: `EveLifecycleTests.cs` kiểm tra hoàn tác bước nạp một phần, lỗi cleanup/thử lại, phụ thuộc thiếu/mơ hồ/vòng, phục hồi owner, cô lập listener lỗi, dọn entity disable, reset danh tính qua phiên, ID trùng, hash JSON/manifest/định nghĩa code, sai giao thức/luật, spawn host/client, quyền sở hữu và chặn replay. Coop được kiểm tra trong process, chưa qua transport LAN. Các script Eve và Inspector cũng được biên dịch với DLL Unity 6000.3.10f1; chưa kiểm chứng Play Mode hoặc chuyển scene thật.

EN: `EveDataSafetyTests.cs` additionally checks rejection of old-session floor snapshots with reused host IDs, immutable definition collection casts and input aliases, retained failed cleanup, blocked restart, repeated cleanup retries and recovery after runtime destruction.

EN: `EveMoveTests.cs` checks the required Stat dependency, 2D movement without jump data, engine stat IDs, movement and jump modifiers, ordinary and special movement, arrival, stop distance, timeout, command replacement, cancellation callbacks, delayed teleport, facing, movement lock, XZ mapping, preserved vertical motion and unload. The physics step is simulated; Play Mode movement and collision still need verification.

VI: `EveMoveTests.cs` kiểm tra phụ thuộc Stat bắt buộc, Move 2D không cần dữ liệu nhảy, ID chỉ số của engine, modifier tốc độ đi và nhảy, di chuyển thường và đặc biệt, tới đích, khoảng cách dừng, hết thời gian, thay lệnh, callback hủy, teleport có delay, hướng nhìn, khóa di chuyển, ánh xạ XZ, giữ vận tốc Y và unload. Bước vật lý được mô phỏng; chuyển động và va chạm trong Play Mode vẫn cần kiểm chứng.

VI: `EveDataSafetyTests.cs` bổ sung kiểm tra snapshot phiên cũ khi host dùng lại ID, ép kiểu collection định nghĩa và sửa list đầu vào, giữ cleanup lỗi, chặn restart, thử lại cleanup nhiều lần và phục hồi khi runtime đã bị hủy.

EN: `EveSkillTests.cs` covers flexible JSON payloads, missing lists, strict envelopes, duplicate IDs/properties, pack namespaces, batch rollback, immutable character IDs, per-entity JSON copies, derived data parsing, missing code references, coop fingerprints, identity selection and failed-load cleanup. The full smoke suite passes 264 checks. Engine content loading and JSON-copy behavior were also verified in Unity 6000.6.3f1 Editor; Core Skill gameplay remains pending.

VI: `EveSkillTests.cs` kiểm tra JSON linh hoạt, danh sách bỏ trống, cấu trúc bao ngoài, ID/property trùng, namespace pack, rollback batch, ID chỉ đọc, bản sao JSON từng entity, override phân tách, tham chiếu code thiếu, hash coop, danh tính và cleanup lỗi nạp. Bộ smoke qua 264 checks. Đã kiểm tra nạp content và sao chép dữ liệu trong Unity 6000.6.3f1 Editor; gameplay Skill của Core để bàn sau.

EN: Nullable Skill data adds checks for omitted/explicit JSON null, code defaults, hook null output, entity readiness, ID presence versus null payloads, unload and Core validation. The full smoke suite now passes 273 checks; nullable content loading was verified in Unity Editor 6000.6.3f1.

VI: Skill data tùy chọn bổ sung kiểm tra JSON null/bỏ data, giá trị mặc định từ code, hook trả null, entity Ready, ID tồn tại khác dữ liệu null, unload và kiểm tra riêng của Core. Bộ smoke hiện qua 273 checks; đã xác nhận nạp trong Unity Editor 6000.6.3f1.

PNSharedDashTests covers globally registered default Dash without Eve content,
two independent entity recovery collections, registry Action/data overrides,
CDR, disable/reload and invalid evaluated distance. PNSharedDashEditorChecks
adds native Rigidbody and interpolation checks for two non-Eve entities.

PNIFrameTests covers Gradual receiver defaults, signed/zero/missing hit durations,
content validation, timer breaking/extension, two sources, HP/timer rejection
atomicity, overflow, death retention, source-unload snapshots and receiver reload.
PNIFrameEditorChecks.Run() uses disposable native preview scenes and registered
damage actions, plus real Eve sweep payloads and Game clock/Update checks. These
checks do not establish network transport, replication or co-op balance.

PNUIDataTests covers required Ability visibility, metadata types/Resources paths,
portrait loading/compatibility hashes and immutable display aggregation. Native
PNUIPlayReview.Run() captures the actual UI with disposable three-player fixtures
and verifies local HUD ownership, selection, world visibility and camera cleanup.
See Assets/Game/UI/PNUIREADME.md and .utmp/CombatUIReview.
# Enemy classification checks

`PNEnemyTypeTests` runs in the standalone smoke suite and verifies the `enemy_types`
registry, locked originals/public aliases, custom code registration, character
metadata/dependency validation, immutable JSON, compatibility fingerprints and
Loader initialization/unload/reload. `PNEnemyTypeEditorChecks.Run()` verifies the
real Unity components and authored Dummy JSON in a disposable preview scene.
See `Assets/Core/Entity/Enemy/Stats/PNEnemyTypeREADME.md` for the contract.

# Buff checks (RulesVersion46)

PNBuffTests exercises the actual Core Buff runtime, registered factories and JSON
extension batches with Unity test substitutes. Native helpers:
PNBuffEditorChecks.Run (idle stopped Edit Mode), PNBuffPlayReview.Run (actual Ready
Play Mode). Buff review evidence is under .utmp/BuffReview; native physics/input
results are distinct from smoke results. Contract: Assets/Core/Entity/Shared/Buff/PNBuffREADME.md.

DOT/Burn additions cover Apply stack input, cap/full refresh/cadence, per-source
instances, Live dead-source ATK, unload fallback, explicit trigger/stack Operations,
Pending reentrant trigger, cap contribution shrink and depletion. Native Buff helper
also includes destroyed-source Burn cases; availability/actual execution must be
reported separately from compiling helper code. Current evidence: .utmp/BurnReview.

Skill-effects additions (RulesVersion46): PNSkillEffectsTests covers hybrid and dynamic
category admission, explicit speed/Live zero, Snapshot/Live contributions, receiver-
callback mutation boundaries, filtered damage tags, Any/All, PerHit owner isolation,
Buff-only activation and load generations. PNSkillEffectsEditorChecks.Run adds real
damage/Burn, Rigidbody auxiliary movement/locks and parent/independent cleanup.
PNBuffPlayReview now covers actual Eve PhysX Attack -> Burn after Snapshot owner
removal, and Skill activation -> self Burn without hit. Evidence:
.utmp/SkillEffectsReview. No authored gameplay is automatically given these Buffs.

RulesVersion47 adds Eve-only authored Burst integration. EveBurstBuffEditorChecks.Run
uses disposable native fixtures for normal/repeat Burn contacts, one Momentum tier
per multi-target stroke, group2/speed math, held chain/miss/release/interruption,
Pending cleanup and lifecycle. EveBurstBuffPlayReview.Run verifies actual Update and
OnDisable in Ready unpaused Play. Evidence: .utmp/EveBurstBuffReview. Other characters
and unrelated Buff/UI/projectile systems remain unchanged.

RulesVersion48 adds elemental Vulnerabilities and DOTs with scoped world VFX.
PNElementalBuffEditorChecks.Run covers receiver mitigation, source grouping/shared
rendering, Entity clocks, scope cleanup and actual URP images. PNElementalBuffPlayReview.Run
requires Ready unpaused Play; it checks actual Update/callbacks, DOT+vulnerability,
capture fallback and renderer cleanup. Evidence: .utmp/ElementalBuffReview.
RulesVersion49 refines VFX and derives presentation size from the root body collider.
The same elemental native/Play helpers now cover collider edits/replacement,
nonuniform/mirrored/singular scale, trigger exclusion and two-material cleanup.
Current evidence: .utmp/ElementalVfxPolish. Historical checks above are not new runs.

RulesVersion50 changes Fire to rising particle sprites, Ice to six-armed snowflakes
and Lightning adds spark points. Independent profiles may overlap. Native47/Play14
rerun evidence and inspected URP frames: .utmp/ElementalVfxParticles.

RulesVersion51 reduces Frostbite visual.size to .156 and arrow size to .098;
arrow slot zero sits above the collider with side slots at staggered heights.
Fresh render/layout evidence: .utmp/ElementalVfxLayout.

RulesVersion52 further reduces Frostbite visual.size to .117 and anchors the top
arrow by its own extent above the collider, with smaller bob. Current evidence:
.utmp/ElementalVfxCompact. Previous layout evidence is historical.
# Category damage tags (RulesVersion62)

Historical evidence below; current naming and fresh checks are recorded next.

PNSkillEffectsTests covers category-to-tag mapping, mod categories, shared tags,
constructor compatibility, link validation, final suppression/dedup, Snapshot/Live,
auxiliary isolation, captured outgoing bonuses, receiver mitigation, crit preservation
and mapping-only fingerprint changes. PNTagTests includes five new built-in tags.
Fresh standalone evidence: .utmp/CategoryDamageReview/SmokeResults.txt.
Native entry point: PNCategoryDamageEditorChecks.RunBatch, wrapping disposable
PNSkillEffectsEditorChecks (including category packet/event checks) and flat Scale
checks plus PNSlashCombatEditorChecks. Final evidence: native Skill21, flat Scale12,
Slash57 in .utmp/CategoryDamageReview/NativeResults.txt. Legacy Slash fixture now
expires receiver iframe between independent full-damage cases and checks rejection
of Client authoritative locks. Prior failures are retained in that directory.
FinalAudit records scene/session/clock/console state. Native Edit/PhysX checks do
not establish Play input, VFX appearance, Player/CoreCLR or replication correctness.

# Attack power stat / Attack tag (RulesVersion63 / Mod API3)

Historical migration/evidence; version64 naming and evidence follow below.

Stat IDs are base:attack_power / project_n:attack_power, constants AttackPower.
Attack tag uses base:attack / project_n:attack, constant PNContentIds.AttackTag.
Old stat attack and tag attack_damage IDs have no aliases. Other tags retain names.
EveRegistryBindingTests additionally verifies stat/tag separation, missing old IDs,
no double-applied modifier and rejection of the old ID as an entity base stat.
API3 manifests/exporter and rebuilt DLL fixtures cover the public symbol rename.
Fresh .utmp/AttackPowerReview evidence: SmokeResults PASS1490 + DLL25;
NativeResults Skill23 / Scale12 / Slash57; SdkResults export/example build success.
PNCategoryDamageEditorChecks.RunBatch writes into that review directory; create
RunNative.request there explicitly before script reload to run in idle Edit Mode.
FinalAudit records scene preserved, stopped session/timeScale1 and console errors0.

# Remaining category tags (RulesVersion64 / Mod API4)

Built-in combat tags and categories share attack/skill/ultimate/dash/burst spelling
in separate registries. Old *_damage category tag aliases/canonical IDs are gone.
Public constants are AttackTag/SkillTag/UltimateTag/DashTag/BurstTag; stat AttackPower
is unchanged. PNSkillEffectsTests checks each old ID, sender bonus and receiver
Mitigation selector; native Skill effects checks four renamed mappings/bonus paths.
The independently built SmokeCodeMod also consumes all five constants at startup.
Fresh .utmp/CategoryTagReview: smoke1505 + DLL25, native Skill31 / Scale12 / Slash57,
SDK API4 export/example build success. PNCategoryDamageEditorChecks.RunBatch writes
there and consumes an explicit RunNative.request after script reload in idle Edit.
Original scene/time/settings preserved; console errors0/warnings48, not cleared.

# Projectile architecture benchmark (fixture only, RulesVersion64 / Mod API4)

See Assets/Tests/Core/PNProjectileBenchmarkREADME.md. Actual Unity Play evidence
in .utmp/ProjectileBenchmark:118 cases /14,160 measured frames,300/500/1000 bodies,
90% homing player and sine boss, paired/reversed native Collider vs hitbox-grid
queries, churn/steady, identical rendering and query-only optimized controls.
Native state/geometry/event checks and measured/PNG parity pass. Owned resource
audit body/material/mesh/renderTexture/localScenes0; original clean scene/settings
restored in Edit, console errors0/existing camera warning1. Initial failures retained.
This does not implement Projectile, guarantee arbitrary-curve collision or prove
Player/mobile performance. No standalone smoke, RulesVersion/SDK bump for fixtures.

# Current group0 contribution metadata (RulesVersion65 / Mod API4)

Latest numeric contract: base:before/after metadata in contributions registry;
optional group0-only contributionId, omission after for Scale and Mitigation.
Mitigation accepts flats, including Heal. Existing Eve data is NOT migrated.
PNFlatContributionTests / PNFlatContributionEditorChecks cover the new contract;
current evidence .utmp/FlatContributionReview/SmokeFinal4 PASS1541 + real DLL25,
NativeResults flat17/Heal14/Buff46/Scale12/Skill31/Slash57. SDK API4 exported/example
built against current Unity assemblies. Older formula/PASS entries are historical.

# Standalone Projectile collision experiment (RulesVersion65 / Mod API4 unchanged)

See Assets/Tests/Core/PNProjectileCollisionPlayerREADME.md. Native release Mono
Windows64 evidence: .utmp/ProjectileCollisionPlayer/ResultsVerified,216 cases /
25,920 timed frames,300/500/1000, actual trigger callbacks vs overlap/sphere sweep,
matched1/2/4 steps, homing/sine, same motion/hitbox/lifecycle and visible VFX. Two
reversed repeats, reference missed/false life-contact metrics, pixel-validated
18 frames. Prior black batch/hidden-auto-camera trials are diagnostics, not VFX
evidence. Empty-player timings are not full-game/mobile FPS or exact curve proofs.
Original scene/build-list preserved; no production Projectile/RulesVersion/SDK edits.

