## Slower boost downlight fade - 2026-11-03 (latest)

User requested slower halo fade only. Downlight0.38->0.70s,stars fade0.50->0.98s and linger after light;weapon Lift keeps original0.38s lowering curve. Buff callback/radius2m/amount/duration/targets unchanged,Rules120. Cancel still clears immediately,no persistent foot effect. Fresh native StarchanterVfx PASS44 HaloSlowReview;wide native GIF .utmp/StarchanterVfx/Starchanter_Halo_Slow_Fade.gif1280x96042frames1.4s30fps includes full6m apex. No new actual AI/Player/build/full smoke/standalone/performance run.

## Whole-weapon strike + high celestial blessing — 2026-11-03 (latest)

Boost radius is now **2 m** (diameter4 m), in the authoritative registered payload. A6 m high source shines down into that circle; visible celestial points use round luminous cores/soft halos instead of four-point icons. Main light0.38 s, stars0.68 s; no persistent foot effects. Boost recipient bursts are small round gold motes. Existing Buff amount/duration/cooldown unchanged.

Melee uses the complete fork: lower-shaft grip, raised backswing, forward-down contact during the existing0.25 s windup, follow-through/recovery. Its wake is sampled from actual shaft bottom to prong tip at each pose, replacing the detached crescent entirely. Direction/distance follow the target, and damage admission still gates hit particles. Actual attack damage/reach/cooldown unchanged. No rig JSON change.

Native PASS43 `.utmp/StarchanterVfx/WholeForkHighStarsChecks/native.txt`; full-length endpoint, round-star, high-source/radius and existing lifecycle checks. Final30 fps GIFs Starchanter_Whole_Fork_Strike.gif / Starchanter_Celestial_Blessing_2m.gif in the same root; owned materials afterward0. Focused smoke/Play expectations updated but not rerun. No fresh gameplay Play Mode/Player/build evidence. Prior entries below are history.

## Radius 1.5 m + inverted-V stars — 2026-11-03 (latest)

Actual registered speed-buff radius is now **1.5 m** (3 m diameter); recipient search, approach and visual share it. Buff amount/duration/cooldown unchanged. The one-shot gold halo is a cone converging at1.8 m height (inverted V), with28 billboard gold stars replacing all vertical stripes. Halo fades in0.38 entity seconds; stars last0.68 s. No persistent foot effects. Slash art is about40% larger (outer0.74 m /width0.22 m), without changing damage or1.2 m melee reach.

Native PASS41 `.utmp/StarchanterVfx/ConeStarsFinalChecks/native.txt`, actual GIFs Starchanter_Cone_Stars_15m.gif / Starchanter_Larger_Slash.gif in the same root; owned materials afterward=0. Smoke radius assertion and Play distance expectation updated but those suites not rerun. No new Play Mode/Player/build. Older notes below are history.

## One-shot blessing and diagonal slash — 2026-11-03 (latest user decision)

Replaces the previous speed wakes / recipient-owned art. Boost now lifts the gold focus and emits ONE descending gold light volume at its real registered radius (0.5 m): thin rays, soft textured veil, faint circular footprint. `BoostPulse` fires once in the real Apply step; pulse fades within 0.38 entity seconds. Recipient gold motes last 0.3 s. No persistent foot trails, Buff-handle visual tracking or ongoing recipient emission. `PNStarchanterSpeedVisual` is now a caster-owned IDisposable helper; same source/meta retained. Cancelling before application produces no downlight. Gameplay Buff values/duration unchanged.

Melee now sweeps the focus diagonally with a three-layer textured crescent, revealing along its arc and eroding in 0.36 s, using existing project slash textures/shader mode14. The old expanding resonance wave is replaced. Admitted-strike gating and brief violet/gold contact motes remain; damage/reach/windup/cooldown unchanged. Fast interrupted-heal fades remain as approved.

Native PASS38: `.utmp/StarchanterVfx/DownlightSlashReleaseChecks/native.txt`; GIFs `Starchanter_Gold_Downlight.gif` and `Starchanter_Diagonal_Slash.gif` in the same root. Actual Unity render preview, not a fresh Play Mode/Player run. Owned materials after review=0. No shared contract/version/skill changes. Older entries below are history and may describe superseded art.

## Remaining skill VFX + interrupted fade — 2026-11-03

Interrupted healing now stops new smoke immediately: boundary/constellation marks fade in 0.20 s, the field cloud/dust/waves in 0.45 s. Successful completion keeps its 1.55 s tail and final-wave hold.

Boost: gold prong light, upward recipient ribbons, fine dust and soft foot wakes on movement. `PNStarchanterSpeedVisual` is recipient-owned and tracks exact successful Buff handles, including multiple grants and early dispel; it does not duplicate gameplay duration. Caster finishing does not remove a granted Buff's art. Idle leaves sparse dust; moving wakes expire after stopping; owner death/disable/pool cleanup releases resources.

Strike: `PNStarchanterStrikeVisual` retracts/thrusts the fork with the existing windup, then draws a violet textured directional pressure wave and brief echoes, with violet/gold contact motes. `PNStarchanterBehavior` releases only for an admitted strike; cancelled windups produce no wave. No new projectile/damage volume or gameplay values.

Native private review PASS 38 (`.utmp/StarchanterVfx/SupportFinalChecks/native.txt`), rendered GIFs Starchanter_Gold_Speed.gif / Starchanter_Violet_Strike.gif / Starchanter_Heal_Interrupted.gif in the same root. Actual Unity disposable preview, not a new Play Mode/Player run. Existing shader modes/materials reused; no shared contracts/version/skill changes. Earlier entries below are history.

## Particle smoke + denser rim — 2026-11-02

The user rejected prior mesh/card clouds. Current clouds use three native ParticleSystems inspired by the supplied Aqsa Nadeem tutorial (https://www.youtube.com/watch?v=H0jUEuPENKI): body smoke, finer wisps and a separate annular rim emitter. Each has randomized lifetime/size/rotation, slow velocity, alpha fade at birth/death and manual entity-clock simulation. Rim smoke adds density at 0.86 x radius, leaving the center less dense. Existing boundary and outward healing waves stay unchanged; healing stays GREEN. Teleport motes/wisps/ripple are now PURPLE.

Texture-only copies: Hovl Studio Smoke3.png -> SmokeBody.png; Smoke24.png -> SmokeWisps.png, under this mob's Resources/StarchanterVfx, verified byte-identical with new GUIDs. No pack code/shaders used. Old cloud-lane/card meshes were removed from the active implementation; historical artifacts remain.

Native VFX PASS 22 (`.utmp/StarchanterVfx/RimSmokeFinalChecks/`). Latest full GIF `Starchanter_DenseRim_Smoke.gif` (132 frames/8.8 s); teleport `Starchanter_Purple_Teleport.gif` (36/2.4 s), both in `.utmp/StarchanterVfx/`, 1024x768. Native preview rendering, not new Play Mode evidence. Older notes below are history.

## Cloud boundary + teleport revision — 2026-11-02

Current cloud cards have irregular elliptical edge erosion, faster internal flow and stronger individual lane/puff motion. A stable thin green boundary + soft halo marks the exact registered healing radius. Gameplay values and prior outward pulses remain unchanged.

`PNStarchanterTeleportVisual` owns world-anchored departure/arrival motes and wisps plus an arrival ripple. `PNStarchanterHealAction` calls `TeleportArrived` after movement succeeds; the owner drives existing actor `SetFade` through out/in and restores it on cancel/teardown. Fixed-world dust does not follow the entity to its new point. No shared Visual code or pack code changed.

Native VFX PASS 22 in `.utmp/StarchanterVfx/TeleportBoundaryFinalChecks/`. Full GIF `Starchanter_CloudBoundary_Healing.gif` (132 frames / 8.8 s) and close teleport `Starchanter_Teleport_Close.gif` (36 / 2.4 s), both in `.utmp/StarchanterVfx/` at 1024x768. Preview rendering uses distinct source/destination; real Play Mode teleport is not newly verified. Earlier notes below are history.

## Floating galaxy revision — 2026-11-02

Current field supersedes the flat annulus: 12 floating cloud lanes, 36 soft cloud volumes, small suspended dust and four drifting constellations. On each real heal, energy travels from the caster outward through the registered 2 m radius, raising/lighting clouds as it passes. Final front is taller and stronger, with green puppet silhouette light; the previous three large star heads and rotating arcs are removed. Radius/heal values, 60% focus and two strings unchanged.

Masks only from NamuFX: `_CommonAssets/Images/Common/Tex_Smoke.tga` and `_CommonAssets/Images/Noise/noise11.png`, copied byte-identically to `Resources/StarchanterVfx/Cloud.tga` / `Flow.png` with new GUIDs. Project code/shader implements flow, geometry and lifecycle; no pack scripts or shader code copied. The final silhouette overlay borrows the live actor mesh/atlas and releases only its own material/object.

Native VFX checks PASS 17, `.utmp/StarchanterVfx/GalaxyVolumeChecks/`. Actual Unity rendered GIF: `.utmp/StarchanterVfx/Starchanter_Floating_Galaxy_Healing.gif` (1024x768, 132 frames, 8.8 s, entire 4 m diameter). Frames `.utmp/StarchanterVfx/GalaxyVolumeFrames/`. No fresh Play Mode/Player/build or shared warm-up test run. Prior notes below are history.

## Moving healing field — 2026-11-02

Replaced the healing rings/large round dust with `PNStarchanterHealingZone`: a full-radius (registered 2 m) flowing green nebula annulus, fine grains, six drifting constellation clusters, travelling light on their nodes, a soft green wash on each real heal and three cyan-green moving arcs/star heads on the final heal. Small recipient dust remains green. The previous smaller gold focus and two puppet strings are preserved. The field follows the caster, uses the entity clock, fades after cancellation/completion and disposes all owned meshes/materials. Gameplay radius/ticks/amounts are unchanged.

Private shader `PN/StarchanterHealingField` uses circular procedural noise to avoid UV seams; registered in `PNShaderWarmupContent` (nineteen entries including aliases as before). Existing standalone warm-up test count updated to nineteen and a field alias check added. VFX native checks PASS 14; field shader compiled without shader errors. Warm-up checks returned PASS 14 / 57 variants, but the existing `PNShaderWarmup.Pump` line 171 (`Destroy(batch)` in Edit Mode) logged cleanup errors: this older unrelated path was not rewritten.

GIF: `.utmp/StarchanterVfx/Starchanter_Healing_Full_Zone_Final.gif`, 1024×768, 132 frames, 15 fps / 8.8 seconds. Actual Unity native preview renders through `RenderStarchanterHealingMovie(output, first, count)`, full 4 m diameter with Starchanter and an ally for scale. Uses real registered timing (0.6 out, 0.7 in; pulses 1.5/3/4.5/5 s into the channel). Frames generated in bounded batches, globals restored, temporary preview scene/resources disposed; Starchanter owned materials afterward = 0. This is a rendered preview, not new Player/Play Mode gameplay evidence. No replay of the stalled Play Mode flow.

Latest revision: focus uniform scale **0.6** (40% smaller). Stronger layered resonance: textured violet membranes and body haze, gold gathering motes and a reveal up the fork, broad soft waves with bright edges, larger green healing motes and rising recipient lights, short gold boost streaks. Two authored strings use `puppet_main` at `chant_crown` and `puppet_drape` at `chant_hood_tip`; game and editor work documents agree. Native revision check PASS 14 in `.utmp/StarchanterVfx/LayeredStringsFinal/`. Fresh Play Mode verification did not complete (editor stuck at frame 2/loading); prior PlayV1 evidence predates this revision.

## Presentation — 2026-11-02

`PNStarchanterPresentation` on the prefab owns the open gold tuning fork (3D extruded/faceted U, stem and lower pendant, no central star). The fork hovers beside the rig, mirrors with facing, fades with the registered teleport-out/in times and vibrates during casting. Violet membranes between the tines and expanding resonance waves use the existing warmed `PN/StarweaverEffects` shader and existing glow/ribbon/noise textures. No pack scripts or new shared shader contract.

Healing dust is **green**, the user's general healing convention. A sparse low green field appears during channeling; real recipients emit upward green motes on each actual heal, with a larger final burst. The heal Action supplies its registered timing/radius and real recipient positions. Boost emits short gold motes on successful Buff recipients. Complete/cancel release the active channel into a short finishing/failure wave; disable, death, actor replacement and pool return remove owned geometry, particles, meshes, materials and camera-facing registration. Simulation uses the puppet/entity clock (manual ParticleSystem simulation, including zero-delta pause). No damage/healing/cooldown/radius values changed; no new RulesVersion, NetworkVersion or Mod API.

Checks: `PNVisualEditorChecks.RunStarchanterVfx(output)` is a private mob-art preview (11 checks, seven real rendered phases, green emission, zero-delta, hover, mirroring, interruption, teardown and reuse). Actual Play Mode uses the existing `PNStarchanterPlayReview.Case`; see `.utmp/StarchanterVfx/PlayV1/starchanter.txt` and captures. Preview frames are separate from Play Mode evidence; no new standalone Player build or coop/mobile test.

## Earlier implementation notes

EN: a mob with no skills yet (2026-11-02). Content: `StreamingAssets/BaseContent/characters/StarchanterCharacter.json` (`project_n:starchanter`, `body.prefab` `starchanter`, `body.visual` `project_n:starchanter`, stats estimated from the Starweaver / Starkeeper to be balanced later, empty `skill`). Its look is the rig-editor document `visual/starchanter.json`. Prefab: `Prefab/Starchanter.prefab`, catalog key `project_n:starchanter`. Components are the Starweaver's without its presentation: `EveEntityIdentity`, `EveEntityLoader`, `EveTimeScale`, `PNStarchanterStat`, `PNAbilityManager`, `PNBuffManager`, `PNEnemyMove`, `PNStarchanterSkill`, `PNStarchanterBehavior`, `PNVisualBody` (child `Placeholder body` hidden while the puppet is drawn). Check: `PNStarMobChecks.Run()`. Scripts (`Script/`): `PNStarchanterStat` and `PNStarchanterSkill` are empty extension points; `PNStarchanterCombatContent.Register` (called from `PNGameplayContentModule`) registers the skills; `PNStarchanterBehavior` picks between the two skills (heal first when someone is hurt below 95%, else the boost, else stands still); `PNStarchanterSupport` holds the ally selection (same layer), `PNStarchanterHealAction` / `PNStarchanterBoostAction` run the skills, `PNStarchanterCombatContent` registers them (heal 16 s, boost 10 s; every number is registered payload). Skills (2026-11-02): heal = teleport next to the hurt ally with the lowest absolute health, 2 m zone for 5 s, 5% of each recipient's max health every 1.5 s and 10% at the end, `block_hits` guard (2 hits, 5.5 s) that cancels the cast and skips the final heal when its hits are spent; boost = walk to the nearest ally (within 8 m), then `speed_boost` (+0.2 move speed, 5 s) on the allies within 0.5 m and on itself, 4 s timeout, cooldown spent when the approach starts. Placed in ErMode (`Starchanter Spawn`, 3.5, 0.03, 1). Checked in a Player (`PNStarchanterPlayReview`, `-pn-menu-play-starchanter`). Strike (2026-11-02): when no ally is in sight (the boost `seek`, 8 m) it fights close up: heal on itself when hurt, then `project_n:starchanter_strike` (`base:damage_action`, 1000% attack power (user 2026-11-03), tag `base:physical`, category `base:attack`, cooldown 5 s, reach `attackRange` 1.2 m with a 0.25 s windup, both tuned in `PNStarchanterBehavior`), then the boost on itself, then it walks up to the nearest hostile it can see. The two support skills are category `base:skill` with no tag. The ErMode `Dummy` was removed from the scene (it was an entity on the Starchanter's layer, so it counted as an ally). Not done: Lorica break wiring beyond the shared one, a Player run, scene placement.

VI: kẻ địch chưa có skill. Dữ liệu ở file character và `visual/starchanter.json`; prefab và khóa catalog như trên; chỉ số ước lượng từ hai quái cũ để cân bằng lại sau.
