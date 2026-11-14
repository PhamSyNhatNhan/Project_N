## Current skill2 V6: textured dash wake (2026-11-01)

The full-height dark/violet body ribbons now use an owned `DashTrailMask.tga` copy of NamuFX `wind_trail01.tga` plus existing `SlashFlow` detail. Own shader mode25 draws torn gaps, stretched density and offset inner light; geometry/history/fade and spear-tip layers unchanged. No Pack scripts/shaders used. Native presentation check PASS83 rendered frames, explicit mask/flow bindings, both renderers and resource cleanup; inspected captures/GIF in `.utmp/StarkeeperTrailTexture/Native/`. Console errors0, main scene clean/stopped. No new Player/combat/performance evidence.

## Current puppet strings (2026-11-01)

Two Visual puppet strings: inherited `head_top` uses `project_n:puppet_main`; Starkeeper-only `head_back` on the hood at [-200,-250] art pixels uses `project_n:puppet_drape` to bow behind the hood before rising. Both follow hood/head motion and mirror with facing through the existing string system. No extra per-character renderer or trail. Authoring source updated in ArtSource/VisualDemo/export_starkeeper.py. Native preview verified two batched strings and separated sockets; latest draped right/left rendered frames inspected in .utmp/StarkeeperDrapeString/. Console errors0, original scene clean/stopped. No fresh Player/build run.

# Starkeeper

## Current skill2 V5: full-height body trail (2026-11-01)

Trail attaches at half actor CapsuleHeight and has broad soft coverage reaching
at least the character's projected height near the root; geometry halfwidth is
70% of CapsuleHeight, inner violet layer70% of outer width. Private shader mode25
replaces the narrow filament profile for body layers only. Tail tapers with both
history time and distance cap, including short histories, so it does not end in
a broad cut. No silhouettes. Tip force and all spear/combat behavior unchanged.
Latest `.utmp/StarkeeperPointVfxV5/Final/`: native83rendered frames with height,
attachment/time/fade/renderers/resource checks PASS; shader/Console errors0,
Editor stopped, scene clean. No fresh Player/AI/combat-suite/performance run.

## V4: body trail only (2026-11-01)

User removed the frozen character silhouettes. No ghost meshes/materials, actor
pose copying or atlas borrowing remain in the cast. Body trail now has a soft
purple-black outer ribbon and a narrow luminous violet inner streak, both following
the real lower torso/cloak path, tapering and fading quickly. Tip pressure/core,
gold side sparks, V2 spear throw/plant/spectral dash/recall and combat stay intact.
Unused shader mode23 is historical; nothing draws it.

Latest `.utmp/StarkeeperPointVfxV4/Verified/`: 83actual frames, no silhouettes,
layered trail attachment/phase/held-time/fade/both-renderers and existing resource
checks PASS; actual Action Editor physics combat PASS31. Shader errors0, Editor
stopped, main scene clean. No fresh Player/AI/performance verification.
Earlier V3 echo descriptions below are superseded.

## V3: body afterimages and spear-tip pressure (2026-11-01)

V2 throw/plant/spectral spear/recall remains. The dash now leaves a separate soft
purple-black wake from lower torso/cloak, plus up to three frozen mesh-pose echoes
of the real character. Captures are spaced by movement/time, retain original pose
and world transform, fade over 0.19s, and render behind the main actor. Main white
mask stays readable. They are cast-owned render copies, not new actors/entities;
no strings/part shadows duplicated. Current mesh/atlas is read only, borrowed atlas
not destroyed, own copies released with cast. Bounded per-capture allocation is
not a performance measurement. No shared Visual code or appearance changes.

Tip adds a slim violet-white core, curved open-backed energy pressure sheath,
brief launch intensity and six tiny gold side sparks. It follows actual spectral
tip/axis and disappears quickly on stop; the former coarse cone stays removed.
Private shader modes23/24, modes0-22 untouched, existing two URP passes/warmup.
Combat/path damage, first-skill slash/warning and all timings unchanged; no AOE.

Latest `.utmp/StarkeeperPointVfxV3/Verified/`: 83actual renders (77timeline plus
renderer/case shots). Tests cover three frozen-pose/position echoes, no early
activation, held-age freeze, body trail, force-layer attachment/lifetime, both
renderers, source mesh/atlas survival and owned cleanup. Sweep regression21frames,
content PASS12, actual registered Action Editor physics combat PASS31. Shader and
Console errors0; PNHall clean, Editor stopped. No fresh Player/realAI/perf evidence.
Older variants below are historical art trials, current user decisions win.

## V2: gold spear anchor and spectral dash (2026-11-01)

Skill2 throws the gold spear in a short arc and plants its blade at the clamped
snapshot destination. The planted weapon emits faint ground energy/dust and a
brief pre-dash pulse. A thin violet spectral spear forms beside Starkeeper from
tail to tip as gold motes gather; it leads the dash, then dissolves. The gold
weapon recalls to its normal animated pose after success, block or cancellation.
No target star, crude cone or endpoint explosion. Damage remains the original
contact motion along the path; throwing/planting/recall add no damage or colliders.
Registered 2s windup/.25s motion/recovery and all combat data are unchanged.

The physical proxy borrows authored mesh/material, temporarily hides the original
renderer and restores its prior enabled state. Spectral mesh is an owned narrowed
clone with reveal UVs. Private shader modes20/22; existing0-19 untouched, both URP
passes and current warmup remain. No Pack code or new copied/generated texture.
The full-weapon aura helper's optional surface-strength argument affects only
this cast; sweep retains its current full glow and user-reverted slash.

Fixed64sample histories record real gold/spectral tip positions and Action ages,
interpolate short tapered trails (gold throw/recall; violet + faint afterimage
for dash), cap length .65m. No appending at held age. Preview rewinds must recreate
the effect. Cancel recall lasts .30s on Actor.ClockDelta with world ownership;
source disable/unload disposes immediately. No-preview Editor cancel disposes
immediately. All borrowed assets and prior renderer visibility are preserved.

Latest `.utmp/StarkeeperPointVfxV2/Verified/`: 82actual render frames (77timeline),
fixed planted tip, no old star/cone/colliders, spectral reveal/gathering, bent
recorded trail, held time, success/block/cancel recall, renderer state and owned
resources/borrowed asset checks PASS on both renderers. Sweep regression PASS21
frames, content PASS12, actual-Action disposable Editor physics combat PASS31
including path damage/no endpoint AOE/wall stop/weapon restoration.
Shader/Console errors0, PNHall clean, Editor stopped. No fresh Player/realAI/nav,
coop/mobile/performance or actual Play cancel-tail lifecycle run.
Older versions below are history and do not override the current user decisions.

## V1 Star Point VFX and full-spear glow (2026-11-01)

Skill2 now marks its clamped snapshot destination with a thick upright gold star
and violet halo. The former floor ring and connecting warning line are not drawn.
During charge, gold motes gather along the entire spear and at the target star;
the spear surface and its blade/shaft/tail aura turn violet. During the lunge,
a violet energy prow leads the actual tip, with a short wake and fewer gold motes.
Completed motion makes the star flash into gold/violet fragments. Blocked motion
fades the star without bursting. ALL damage remains the existing shared contact
motion along the path; no endpoint AOE/collider/Operation or payload change.

PNStarkeeperPointVisual is Action-owned. Supplied age freezes all effects with
the actor timeline; target XZ stays fixed. Cancel during Play leaves only a 0.22s
harmless star fade clocked by Actor.ClockDelta, then releases it. The root remains
registered to world presentation; source disable/unload destroys it immediately.
No-preview Edit Mode cancellation disposes immediately. Preview checks explicitly
advance/freeze the cancel tail. PNStarkeeperWeaponGlow is shared only inside this
mob's two cast visuals; it borrows the authored mesh, owns overlay materials and
an aura mesh. It ignores gold vertex RGB for purple surface shading and covers
the whole weapon. Original gold material/asset mesh is unchanged. Private shader
modes17/18/19 use existing warmup and both URP passes; modes0-16 untouched.
Skill1 slash remains the user-reverted V7 appearance, with improved weapon glow.

Actual checks: PNStarkeeperPointVfxChecks.Run(output) PASS81rendered frames with
fixed upright target, gathering/full-spear glow, prow/wake, reached/blocked cases,
held time/cancel fade, both renderers, owned material+mesh cleanup and shared spear
asset preservation. PNStarkeeperEditorChecks.Run PASS12 and RunCombat PASS30:
real disposable Editor floor/path/endpoint-off-path target/wall validates path
damage and absence of endpoint AOE, successful versus blocked star outcome and
cleanup. Existing sweep VFX check PASS21frames. No fresh real AI/Player/coop/mobile
or performance run; explicit Editor cancel-tail tick is not a Play lifecycle test.

Evidence `.utmp/StarkeeperPointVfxV1/Verified/` (77timeline PNGs +4case/renderers,
GIF, timeline.tsv, native.txt), `.utmp/StarkeeperPointVfxV1/SweepVerified/`.
Shader/Console errors0, Editor stopped, PNHall clean. Main scene never saved/reloaded.
Older entries below are art history; current user decisions override them.

## Current: pre-V8 slash with luminous V8 warning (2026-11-01)

User reverted only the slash appearance. Mode14 is back to V7's textured violet
body, sparse three glints, broken rim and early echo, with prior transparency.
Shaft connection/range/timing unchanged. V8 luminous warning nodes, halo, dust
and filament links remain; no gameplay or other character changes.
Latest native evidence `.utmp/StarkeeperSlashV8/SlashReverted/`: 21 renders,
content PASS12, combat PASS21, shader/Console errors0; no fresh Player run.
V8 spectral-wisp slash described below is superseded.

## V8 trial: luminous constellation and spectral cut (2026-11-01)

Art direction: coherent spectral blade structure (Adula reference), with restrained
purple/gold cosmic wisps (Nebula reference). This is a project-specific trial,
not a claim of recreating Elden Ring's effects or implementation.

Warning nodes now emit gold-white cores, broad amber halos, soft flare and tiny
gathering dust. Additive shading replaces the gold bead/metal sphere appearance.
Links have bright cores and soft shoulders, broken early and strongly revealed
near release. Both are phase-driven, camera-facing nodes use the existing shared
system. Larger halo quads have inset endpoints to fit the slash projection.
Only sweep one warns; no bottom borders/fill. No camera bloom change is needed.

Slash shading now uses two long smooth flowing wisps, a bright irregular leading
edge and thin outer rim, faint mist-like echo and a few tiny gold glints. Visible
cellular chips/scales removed; copied mask/flow/dissolve textures supply soft
density. The wake fades from shaft side outward, brightness drops quickly after
the cut. Existing gold particles, purple weapon glow, shaft attachment and nominal
outer sector remain. Combat and skill2 unchanged, white mask/purple skin retained.
Own shader modes14/15/16 share existing URP passes/warmup, modes0-13 untouched.

Latest `.utmp/StarkeeperSlashV8/Verified/`: 21 actual renders; luminous core+halo
pixel check with no preview bloom, both-renderer warning, projected span, prior
shaft/range/time/cleanup checks PASS; content PASS12, combat PASS21. Shader/Console
errors0, PNHall clean, Editor stopped. No fresh Player/AI/performance verification.
Older trials below are historical; appearance remains for user judgment.

## V7: compact floating constellation lights (2026-11-01)

Sweep warning now uses five ROUND gold lights, with shaded bright cores and
soft halos, facing the camera through the existing IPNCameraFacing system.
One preallocated mesh draws all nodes; project shader mode15 supplies apparent
volume (billboard impostors, not sphere geometry). No four-point floor stamps.
Nodes and thin gold links sit at +0.3m, matching slash outer-arc height. Endpoint
angles/radii are inset so their projected width fits inside the slash span.
Unequal size/spacing retained; no lower borders, origin marker or filled region.
Only sweep one warns; Star Point remains unchanged. The warning is an inset
constellation band, not a precise full outline of the unchanged damage sector.
V6 shaft-connected slash, purple weapon glow and skin/white mask remain.

Latest `.utmp/StarkeeperSlashV7/Verified/`: 21 actual rendered frames, both warning
renderers, node-facing and projected-width checks, prior slash containment/shaft
attachment/freeze/cleanup; content PASS12 and combat PASS21. Shader/Console errors0,
PNHall clean, Editor stopped. No fresh Player/AI/camera-rotation-in-Play evidence.
Earlier versions below are historical art trials.

## V6: shaft-connected wake and gold warning band (2026-11-01)

Slash wake now reaches the actual animated spear shaft: inner vertices attach
within 0.035m during the 0.12s swing, then freeze with the retained mark.
Outer arc still follows the registered sector origin/radius/angle, including
reverse/reaimed sweep two. The connector bends from the shaft to the planar
outer arc; outer-edge containment replaces V5's all-vertex containment claim.
Transparent shading, gold particles and purple weapon glow remain unchanged.

Warning is now ONLY an irregular gold constellation band ahead: seven stars
with varied size, angular spacing and inset radius, linked by thin lines.
No origin star, lower radii, filled region or continuous circular border.
Endpoints retain the sector's full range/angle; interior nodes are decorative
insets, so this band does not continuously outline every point of the sector.
Only sweep one warns, following the caster's step. Sweep two and Star Point
behaviors remain as previously specified. White mask/purple skin preserved.

Latest `.utmp/StarkeeperSlashV6/Final/`: actual 20 render frames; both strokes'
outer containment and shaft attachment, held time/retention, both renderers,
mesh/material cleanup PASS; content PASS12, combat PASS21 including no lower
warning edges and gold tint. Shader/Console errors0, PNHall clean, Editor stopped.
Inspected 02,05,06,14. No fresh Player/AI/performance run. Older trials below
are history and remain pending user visual acceptance where superseded.

## V5: first-only constellation warning and matching slash (2026-11-01)

Sweep one warns with violet connected star nodes outlining the registered sector,
not a filled yellow wedge. Marker follows the caster's first step, disappears at
the first hit and NEVER returns for sweep two (including reaim). Star Point is
unchanged. Slash now grows within the same planar registered radius/angle and
hit origin (1.4m / 100deg currently), at hitbox center height +0.3m. Both strokes,
including the reaimed reverse stroke, retain the mark after their 0.12s growth.
All five layers stay inside the nominal sector. Cosmetic band width remains
0.46m, with the transparent fill/ink and sparse glints/particles from V4.
This replaces V2/V3's smaller arc sampled from the spear billboard plane.

The original gold weapon gets a short purple overlay/edge/tip glow on each cut;
only the overlay material is owned, its shared spear mesh remains the asset.
Gameplay timings, hitbox, damage and steps are unchanged. All VFX use Action age.
Skin already matches cloak #554d6d. User clarified to keep the white mask, so the
head PNG and tint None remain; native checks assert both colors/contracts.

Latest evidence `.utmp/StarkeeperSlashV5/Verified/`: 20 native render frames,
all-layer sector containment both strokes, freeze/retained geometry, purple glow,
both renderers, mesh/material cleanup; content PASS12, combat PASS19 including
warning-follow and absence before sweep two. Shader and Console errors zero;
PNHall remains clean and Editor stopped. No fresh Player/AI/performance run.
Older trial sections below are historical.

## Quét Sao textured astral layers V3 (2026-10-31)

Latest revision (2026-11-01): preserve original width (inner edge 0.10 m)
and transparent body (ink 0.30; violet fill 0.18 + veins * 0.25). Remove the
five-star chain and constellation lines; three smaller uneven glints appear in
short staggered pulses during erosion. Four gold particles shed from the actual
sweep edge per stroke at 0.025 s intervals, with radial/tangential velocity and
short life. Stroke-owned emission counter prevents repeats at held Action age.
Uses the existing bounded particle mesh and clock. Echo remains faint/early fading.
Latest actual native renders/check report: `.utmp/StarkeeperSlashV4/Sparse/`.
Earlier Thin and linked-star trials are superseded. Fresh render/cleanup checks
and existing content/combat checks pass; no fresh Player/AI verification.

Current appearance supersedes V2: five draws per stroke with independent shading,
blend and disappearance. Alpha ink uses torn cloudy/cellular texture; violet fill
has animated stretched energy veins; additive rim is broken violet-white/gold;
additive sparse four-point glints pulse during erosion;
a rotated, narrower alpha echo fades earlier. Three small glints per stroke are shaded
within slash UVs. Existing bounded release/contact particles remain. Spear-following
geometry and 0.36 s presentation life are unchanged; no combat or skill 2 changes.

Texture-only copies from `Assets/Pack/NamuFX/_CommonAssets/Images/Noise/`:
`noise11.png` -> `Resources/StarkeeperVfx/SlashFlow.png` (SHA256 885149EC...16841CE),
`noise14.png` -> `SlashGrain.png` (3E58B0B6...EE7ADDC). Byte hashes match.
Own GUIDs/importers: linear, Repeat/Bilinear, mipmaps off, uncompressed.
No Pack scripts/shaders/materials/prefabs/meshes used. Own shader mode 14 receives
two new textures; modes 0-13 untouched, both existing URP passes share HLSL.
Flow, erosion and star reveal use the supplied Action age, including held time.

Fresh native `PNStarkeeperSweepVfxChecks.Run` renders 20 frames, verifies five
layers per stroke with alpha/additive blending and both texture bindings, held-age
freeze, retained geometry, completion/early-dispose mesh/material cleanup, then
runs existing content/stat/prefab PASS 10 and combat PASS 16. Inspected rendered
frames 06, 08, 14. Final shader error false, Console errors 0, Edit Mode PNHall
not dirty. Evidence `.utmp/StarkeeperSlashV3/Final/` (PNG, GIF, native.txt).
No fresh Player/AI, wall, coop, mobile or performance verification. Art trial
pending user judgment; mob-local checks add no generic API/skill change.

## Quét Sao retained slash V2 (2026-10-31)

Supersedes the V1 moving ribbon appearance. Each stroke samples the animated
spear's up vector, pivot and normal over its 0.12 s swing. The growing crescent
retains the whole swept mark, then erodes over a 0.36 s visual lifetime; it no
longer moves a short head along a fixed ground-sector arc. The second stroke
follows its real reverse swing/re-aim and is slightly larger.

Only texture data is copied from NamuFX `Simple Stylized Slash vol2`:
`Textures/SlashMaskTest.tga` -> `Resources/StarkeeperVfx/SlashMask.tga`,
`Textures/SlashMaskTest-Dissolve.tga` -> `SlashDissolve.tga`. Original bytes match;
copies have new GUIDs, linear color sampling, Clamp/Bilinear, no mipmaps or texture
compression. No NamuFX scripts, shaders, materials, meshes or prefabs are used.
Project-owned mode 14 in the already warmed `PN/StarweaverEffects` shader draws
the static mask, ink underlay, violet fill and gold rim, then erodes the tail.
Other shader modes are unchanged; both URP passes share the new branch.

The native VFX check now renders 20 phases including both renderers, asserts the
completed mark stays in place after the swing and checks both slash vertices and
particles at held Action age. It remains mob-local verification, not a generic
skill/API change. No new skill documentation entry is needed.

## Quét Sao VFX trial (2026-10-31)

`PNStarkeeperSweepVisual` is owned by one `PNStarkeeperSweepAction`. The Action
supplies age and emits each stroke with its actual hit origin/direction, including
the second sweep's re-aim. Charge glow and a narrow gold edge follow the animated
spear; the strokes combine violet-black, textured violet and thin gold meshes.
Bounded billboard star particles mark release and registered hit notifications.
No independent gameplay clock, damage, movement, cooldown or hitbox changes.

Reuses the already warmed `PN/StarweaverEffects` shader and existing copied masks;
no new Pack code, shader or texture. Each cast owns its meshes/materials and releases
them on completion/cancel; source disable/destruction also releases presentation.
Facing uses `IPNCameraFacing` with the shared camera system and an explicit preview
camera in native checks. This trial does not add Star Point VFX.

Native check `PNStarkeeperSweepVfxChecks.Run(outputPath)` (stopped disposable Editor)
renders the real puppet/spear and both sweep phases on both URP renderers, checks
held-age particles and resource cleanup, then runs existing content/combat checks.
`Batch()` writes into `PN_STARKKEEPER_VFX_OUTPUT`, or the clone-local
`.utmp/StarkeeperSlashV2` default, and exits. It is not a Player/AI acceptance run.
Latest evidence and limits are in HANDOFF.

EN: the normal melee enemy that comes after the Starweaver: a flying rig puppet (hood and face drawn as one piece, a long cloak) with a golden 3D spear. Two skills. Folder layout follows the other entities: `Prefab/` (the prefab), `Script/` (stat, skill, behavior, the two Actions, telegraph, spear presentation, combat data), `Art/` (the spear mesh and material). Plan and guide: `Docs/StarkeeperImplementationPlan.md`, `Docs/StarkeeperImplementationGuide.md`.

- **Content (data):** `characters/StarkeeperCharacter.json` (`project_n:starkeeper`: stats, `body.prefab` `starkeeper`, `body.visual` `project_n:starkeeper`, `body.build` `normal`; hand-written) and the puppet in `visual/starkeeper.json` + `visual/starkeeper/*.png`, written by `ArtSource/VisualDemo/export_starkeeper.py` (never hand-edit the numbers).
- **Prefab:** `Prefab/Starkeeper.prefab`, catalog key `project_n:starkeeper`. Made from the Starweaver prefab; has `PNStarkeeperStat`, `PNStarkeeperSkill`, `PNStarkeeperBehavior` (behavior id `starkeeper`), `PNStarkeeperPresentation` and the child `Star spear`. After changing a serialized field of the behavior, re-add the component (Unity keeps old field values of the same name).
- **Skills (registered in `PNStarkeeperCombatContent`, all numbers there; the plan's starting values, NOT final):**
  - **Quet sao** `project_n:starkeeper_star_sweep`, Ability `<id>_ready` cooldown 3 s. `PNStarkeeperSweepAction`: windup 0.4 s, first sector sweep (100 degrees, 1.4 m) at 0.4 s, re-aim at the target at +0.2 s (at most 70 degrees from the first), second sweep at +0.35 s, recovery 0.6 s. 80% then 110% attack power, physical, each sweep rolls its own crit, one hit per target per sweep; the first hit carries `iframeDuration` 0.2 so the second lands in full against a target with the player's 1 s i-frame. Each sweep also steps toward the target during the 0.12 s before its hit: `min(0.5 m, distance - 0.55 m)`, so nothing when already close (registered `step`).
  - **Diem sao** `project_n:starkeeper_star_point`, cooldown 7 s. `PNStarkeeperStarPointAction : PNAgentContactMotionAction`: a star is marked at the target's feet (at most 3 m, clamped by the behavior from the registered `maxRange`), the Starkeeper stands still for 2 s while it blinks, lunges along the line to the star in 0.25 s (stops at walls, one hit per target on the swept path, 220% attack power physical), recovery 1.2 s. The lunge schema is the `motion` part of the payload; the shared Action got two protected hooks (`MotionPayload`, `MotionDistance`) for this, default behaviour unchanged.
  - Pacing: no timer in the behavior; the pause between attacks is the `recovery` in the registered data. The behavior only chooses (Quet sao <= 1.4 m, Diem sao 1.5-4 m, never the same skill twice while the other is ready and in range).
- **Puppet:** the humanoid head and face are `hide`n; `sk_head` carries its own face. Cloak = two bendable strips (`cloakB`, `cloakF`), the `hood` bone rocks a little. States: idle hover, walk fly, run fly_fast, hurt (overlay, no leg tracks), dead and stun (standing slump), recover, `star_sweep`, `star_point` (authored in `starkeeper_actions.py` on the registered timings; if a time changes in the data, change the keys).
- **Spear:** `Art/StarSpear.asset` + `StarSpear.mat` (same shader as the Starweaver's needle, faces carry the colours), built by `Assets/Game/Editor/PNStarkeeperWeaponAuthoring.cs` (menu Project N / Starkeeper). 0.5 m, hovers beside the puppet on the actor clock; the cast Actions drive its pose.
- **Scene:** `Starkeeper Spawn` (`PNSceneEnemySpawn`, `project_n:starkeeper`) in ErMode at x = 2.
- **Checks:** `PNStarkeeperEditorChecks.Run`/`RunCombat`, `PNVisualEditorChecks.RunStarkeeper`/`RunStarkeeperWeapon` (see HANDOFF for what was run and what was NOT: the lunge's wall stop and path hits, real AI combat, coop).

VI: kẻ địch cận chiến thường sau Starweaver, bay, đầu và mặt một mảnh, áo choàng dài, giáo vàng 3D. Hai skill: Quét sao (hai nhát, nhát 2 nhắm lại) và Điểm sao (đánh dấu sao, 2 s sau lao tới, dừng ở tường). Số liệu là giá trị khởi điểm của plan, chưa chốt. Khi di chuyển prefab phải kèm `.meta` để giữ GUID mà catalog tham chiếu.

## melee_scale (RulesVersion158)
The star sweep captures `PNHitCapture.Melee` when the telegraph first shows: `radius` and `thickness` scale by `melee_scale` (the 100° `angle`, the step distance and the behaviour's trigger range do not), the damage is prepared once per sweep with the extra `melee_damage` tag. The marker and VFX use the scaled radius. A Buff gained or lost during the windup does not change that cast.
