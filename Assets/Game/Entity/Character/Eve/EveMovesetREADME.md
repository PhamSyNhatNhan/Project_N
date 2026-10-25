# Eve moveset — 2026-10-03

RulesVersion46: activation entry definitions declare category arrays and explicit
affectedByAttackSpeed. Attack/Repeat keep LIVE speed scaling; other entries including
Burst Ultimate finishers remain unaffected. Dynamic categories affect ALL mapped
Control gates, but never infer speed. Context carries entry/invocation/generation
through profiles; successful Try commits one activation trigger. Standard sweep hit
delivery freezes contribution/tag context before damage and emits OnHit afterward.
RulesVersion47 attaches the two registered Eve Burst Buffs below. Other generic
templates remain examples. See PNSkillREADME for the shared trigger contract.
RulesVersion48 changes Burn to5% ATK/stack, cap35, period.8/duration15 Entity
seconds and world VFX. No other elemental/Vulnerability Buff is attached to Eve.

## Blazing Assault and Rising Momentum

Required tuning references blazingAssaultBuffId/risingMomentumBuffId select the
registered profiles (default base:eve_blazing_assault/base:eve_rising_momentum).
Parser, content/reference validation and runtime load validate these references;
Skill owns conditions/lifetime and uses selected apply_buff factory with real
invocation contexts. No new input/Visual/projectile/UI mechanism is added.

Blazing Assault has buff tag, Game clock, no independent expiry, Source grouping,
single instance/source and ordinary dispel allowed. Burst grants it; its owned Live
contribution filters Eve attack entry/category Attack and applies1 Burn stack at
each valid target hit. Normal and repeat attacks count; Ultimate does not. Burst
end removes its exact instance via normal bypass cleanup. Existing target Burn
continues; later hits after Burst end do not gain Burn. Skill disable alone retains
Burst's entity-owned Buff; entity suspension returns/reacquires its contribution
through standard Buff lifecycle without activation replay.

Rising Momentum has buff tag, Game clock, no independent expiry, Source grouping,
ordinary dispel allowed and custom stacks. Per-stack registered modifiers: ATK+.1
group2 and Attack Speed+10 group0 (percentage points). Values are not duplicated in
Skill. Technical maximumStacks=Int32.MaxValue imposes no extra gameplay cap; actual
chain length remains controlled by existing slash resources. A repeat sweep's FIRST
valid hit grants/reapplies once, even with multiple targets. Misses never grant.
Bonus starts after that hit; current attack damage retains its captured source while
LIVE speed affects remaining timing, and subsequent strokes use new ATK.

Skill owns held-chain lifetime across its separate invocations. Release finishes
the current stroke before reset; exhaustion, interruption, rejected continuation,
disable, death, unload or Burst end reset Momentum. Normal Attack does not grant it.
Cleanup returns only Buff-owned stat sources; other owners are not restored from
stale values. Apply may be Pending; receipt cleanup queues behind it and removes
only that exact handle, preventing stranded contributions.

Example with original ATK10/Burst flat+100: first tier yields121 ATK/160% speed,
second132/170%. Independent group2 modifiers add within that group. Cleanup resets
Momentum to no active instance, not slash charges. No iframe/VFX tuning was changed.

Gameplay, Ability definitions and VFX tuning now come from
Assets/StreamingAssets/BaseContent/characters/EveCharacter.json.
Eve owns targeting/combo/reward logic. Ability holds passive entity state only.

The eve_moveset entry contains skill.abilities (seven entity-owned definitions)
and skill.tuning (motion profile IDs, rewards, Burst bonus/cost, input timings and energy phases).
PNAbilityManager loads these definitions BEFORE Skill, even on an entity without
a skill component; Skill requires their IDs/types and never silently defines missing
states. Scene Inspector character-ID fallbacks are supported. Duplicate IDs across
Inspector, standalone asset and selected content fail loading; do not configure the
same Eve abilities a second time in the manager Inspector. There is no global
Ability registry: definitions/instances belong to each entity independently.

Normal right/left/horizontal, empowered right/left/horizontal, Repeat right/left,
normal Ulti and Burst-Ulti right/left/horizontal each have their own action payload.
They select base:eve_sweep_action, a NEW independent EveSweepAction.
No old slash mesh, prefab, texture, track or slash VFX/hit operation is used by
Eve's actual attacks. The legacy eve_slash entry and GetSlashVfxPool API remain
available only for existing callers/mods and legacy regression checks. Old
base:slash_action overrides do not modify the new base:eve_sweep_action.

## Control leases (2026-10-06)

Eve TryAttack/Repeat/Skill/Dash/Ultimate/Burst use registered category/global and
exact activation-entry gates before resource commits. Activation entries are
eve_attack_entry, eve_skill_entry, eve_ultimate_entry, eve_burst_entry; Dash uses
eve_dash. These identities do not duplicate action-profile tuning. Sequence and
motion locks own independent leases. Skill facing respects face_skill instead of
temporarily enabling CanFlip. Explicit TryInterrupt cancels current cast; locks
alone do not interrupt or end Burst. See Core/Entity/Shared/Control/PNControlREADME.md.

## Registered motion and shared player Dash

PNGameplayContentModule owns the public IDs and registers PNMotionAction under
project_n:motion_action / base:motion_action, alongside SweepAction. It also
registers project_n:player_dash / base:player_dash before any entity is created.
Default data is distance1.5, duration.16, ignoreEntities=true, three entity-owned
PerStack charges recovering5s, Game clock, opt-in entity CDR. Compatible registry
Action and default Dash data overrides are supported; all definitions are validated
before entities load. The logic stays in PNPlayerSkill, not a registered skill class.

PNSkill.BuiltInSkillIds lets PNPlayerSkill select registered shared content even
when a character has no authored skills. PNAbilityManager loads these selected
shared Ability templates before Skill, deduplicating shared/character skill IDs.
It still rejects duplicate Ability definitions; runtime charges are never global.
GetData/CreateAction remain limited to character-selected or explicitly built-in
skills. Shared data is parsed per entity, never mutated at invocation time.

Eve selects base:eve_dash with its existing eve_dash_ready Ability and JSON distance,
duration/collision parameters; its seven Ability definitions are unchanged.
Eve only supplies interruption/face/VFX callbacks: Dash can interrupt ordinary
casts, never Ulti. PNPlayerSkill owns immediate-press Dash, movement/last movement
direction, charge spending/CDR/recovery and invocation cancellation/lifecycle.

Eve JSON adds eve_skill_rush, eve_empowered_rush, eve_repeat_rush,
eve_ultimate_retreat and eve_ultimate_rush, all selecting base:motion_action.
Their perform payloads hold distance, duration and ignoreEntities; moveset tuning
references their IDs. CreateMotionAction uses the registry factory and context,
with a validated shorter distance for target approaches. A skill cannot request a
distance beyond the registered maximum. Timings and collision parameters always
come from selected Action data. Movement lock operations also use registry factories.
No production caller creates a motion action directly with constructor parameters.
EveMotionAction remains a parameterless compatibility type derived from PNMotionAction.
Old constructor-based callers must migrate to CreateMotionAction and registered data.

## Controls

| Move | Keyboard | Gamepad |
| --- | --- | --- |
| Normal Attack | Tap J | Tap East |
| Stack Attack | Hold J (PNRepeat) | Hold East |
| Skill | U | West |
| Dash (immediate press) | K | South |
| Ultimate | I | Right shoulder |
| Burst | O | D-pad up |
| Jump (existing Move) | Space | North |

Dash uses Press(behavior=0), executes at key-down and never repeats on hold or release.
PlayerSkillInputConfig.PressImmediately defaults false for other buttons.
Ulti's right-shoulder binding is Eve-local, preserving the shared Input asset.
PNRepeat now reads the live control on timeout: Unity's timeout context does not
contain cached magnitude. Tap remains separate; long holds do not produce a Tap.
Releasing Repeat stops further hits after the current one. Empty stacks stop the
chain until release, without switching to the normal combo in the same hold.

## Mechanics

- Normal Attack: right/down -> left/up -> larger horizontal. Three distinct Action
  invocations; idle >1s resets the combo. Only normal/Repeat attack timelines use
  live `attack_speed / 100` after EveTimeScale. Zero speed blocks new normal attacks
  and pauses existing ones. Skill/Dash/Ulti timings do not use ATK Speed.
- Skill (RulesVersion86): a Dash-like glide, not a target rush. It follows the current or last movement direction like Dash (no target
  acquisition) over the `eve_skill_rush` profile: distance 2, duration .16, `ignoreEntities` true; `skillStopDistance` no longer exists.
  It has its own 5 s cooldown and does not spend Dash charges or create Dash's TimeScale area. When the glide starts, the profile's
  `OnActionStart` effect runs `base:create_projectile` (position Self, direction Invocation): three `base:eve_skill_homing` projectiles
  (`project_n:homing` derived in code: speed 7, size .175, turnRate 540, homeAfter .25, life 3.5 s Time, Reacquire, no gravity, small glowing trail
  from `PNTrailHomingProjectile`). The volley aims at the nearest live Enemy-layer target within `aimRadius` 8 (else along the glide
  direction): the base direction is that target, the sides are +-30 degrees (`spread` 30) and the middle one is tilted 30 degrees up
  (`pitches [0,30,0]`) so the shots converge when they home. Each projectile deals 150% ATK (physical + fire/ice/lightning tags, can crit,
  iframe default) and is the damage prepared once for the volley. Reward unchanged: on accepted activation gains 5 Burst points outside
  Burst, or 5 slash stacks inside it, grants a 10s next-normal-Attack state (refreshed on reuse) and subtracts 2s from Ultimate recovery.
  Only a normal Tap Attack consumes the state; Repeat and Ulti do not. The empowered normal attack rushes first, then creates a fresh
  enlarged slash. Its movement and slash share the normal-attack speed clock. Consumes the state at attack acceptance; Dash cancellation
  does not restore it. Three projectiles on one target share the target's iframe: with the default iframe the later hits of a converging
  volley can land at the fixed .3 multiplier (a measured volley of ATK 10 dealt 34.5 instead of 45). Projectiles are authority-only until
  projectile networking exists.
- Dash: always follows current/last movement direction, with NO target acquisition.
  Standing still retains the last movement direction. Three charges, PerStack recovery5s; subsequent use preserves the active
  recovery. Uses its full configured dashDistance regardless of nearby target distance.
  dashIgnoresEntities controls entity-collider clipping; solid world walls always clip.
  Motion uses Rigidbody.position and temporarily suspends interpolation, restoring
  the authored setting on exit/cancel. Can cancel Attack, Repeat or Skill, never Ulti.
- Ulti: retreat -> charge exactly1s -> select lowest absolute current HP target ->
  rush -> one large horizontal slash. No gauge condition, cooldown25s. In Burst,
  it instead runs right/down, left/up, horizontal, then ends Burst. A Burst captured
  by this finisher keeps its stat effects even after the15s state timer expires,
  until the third slash completes. Lifecycle cancellation/death clears effects.
- Burst: requires and spends100 points, no cooldown, lasts15s; adds100 to ATK's
  base/group0 and50 attack-speed percentage points (100->150), grants6 stacks.
  Gauge maximum150; slash-stack maximum12. Cannot activate again while active.
- Normal hit: once per slash that hits at least one hostile, gains2 points outside
  Burst or2 stacks inside Burst; subtracts1s from current Ulti recovery. Repeated
  colliders/multiple victims do not multiply the reward.
- Repeat: spends1 stack before its rush, alternates right/left larger slashes,
  never generates gauge or stacks; a successful hit still subtracts1s Ulti cooldown.
- Skill activation subtracts2s from current Ulti recovery. Outside-Burst Ulti grants
  10 points at its first successful hit, once per cast. No gauge accrues during Burst.
- Burst end (expiry or completed finisher) immediately clears all slash stacks,
  removes only Burst's stat source and adds30 points, capped150. Expiry during
  Repeat immediately cancels both rush/slash and restores movement. Refund is once.
- Other inputs require the current cast to finish. No queued casts or animation
  cancel windows. Dash is the only ordinary interruption exception.

All cooldowns opt into entity CDR through the Skill helper; parameter updates affect
future recovery cycles. `ReduceRemaining(seconds)` separately advances current
recovery, clamped by recovery completion, so normal/Skill hits can finish Ulti CD.
Ability clocks default to Game; motion/charge/slashes use Eve's effective entity
clock. The Skill executes effects in response to state notifications; Ability never
changes ATK, creates VFX, chooses targets or performs actions.

Targets come from the shared selector (RulesVersion99, `PNAutoTarget` over the
world's `PNTargetCandidates`; see "Choosing a target" in PNCombatREADME): live
entities with a hit box on Eve's TargetLayers within TargetRange (planar, from the
box center). Ordinary casts follow the player's auto target setting (Nearest,
LowestHealth, LowestHealthPercent; higher aggro first) and the character-wide held
target (Nearest switches only to a target 20% nearer); Ulti keeps its own rule:
lowest absolute HP after charging, then nearest distance, then entity ID, without
changing the held target. Missing targets use the last movement
direction. Skill forces immediate sprite facing (along its glide) while preserving movement locks.
Each cast keeps its selected direction/target through its phase; next Attack/Repeat
acquires again. Rush stops short of targets and capsule sweeps clip solid obstacles,
ignore self/children and ground contacts parallel to motion. The capsule query
  leaves .02 vertical contact skin to avoid false zero-distance floor hits. No invulnerability.

## JSON tuning

Sweep perform payloads accept optional finite signed `iframeDuration`. Omission uses
the receiver fallback (enemy .2s/player 1s); explicit zero leaves its timer unchanged.
Negative duration extends active iframe, positive consumes it. Every hit during iframe
receives the fixed .3 multiplier and remains a successful hit for moveset rewards.
Current Eve profiles intentionally omit this field. Dash still has no invulnerability;
Buff integration is deferred. See Core/Entity/Shared/Combat/PNCombatREADME.md.

Edit EveCharacter.json and reload the content session/enter Play Mode again.
There is no serialized Moveset Inspector override. skill.Moveset exposes the loaded
settings for debugging. Required numeric fields are validated; malformed windows,
geometry, durations and missing/incompatible states fail clearly.

| Data location | Contains |
| --- | --- |
| eve_moveset.data.skill.abilities | IDs, kinds, capacity, initial quantity, recovery, cooldown, duration, clock |
| eve_moveset.data.skill.tuning | Target range, motion profile IDs, combo reset, rewards, Ulti CD reductions, Burst cost/bonuses/refund, Repeat timings |
| tuning.weapon | Light blade base length/width/depth, hand forward/side offset, charge pose height/length/angle, normal/Burst brightness |
| tuning.energy | Motion trail lifetime/width, Burst/charge particle rates, Burst size/lifetime/upward-speed ranges, aura radius, light intensity, pulse/impact counts |
| each motion profile's data.action.perform | Distance, duration, ignoreEntities, permissionId |
| each attack's data.action.perform | Damage/tags, duration, sweep angles/tilt/radius, timing, ribbon width/tail/intensity, trail width, sparks, hit sampling |

Damage defaults: normal100%, empowered160%, Repeat140%, Ulti350%, each Burst-Ulti
hit180% ATK. Normal/Repeat duration .32s; Ulti .42s. World radii: normal1.15,
third1.55, empowered1.95, Repeat2.15, Ulti3. Defaults are starting balance values.
New profiles preserve the prior physical/fire/ice/lightning tags. Each profile owns
its tags independently; change them in JSON to alter damage classification.

## Sword and energy VFX

EveWeaponRig presents a sword-shaped light volume, not a metal weapon. Its pointed
ridged core and tapered overlays are additive white/pale yellow, with no metal parts or brown/orange faces. A third light diamond forms the grip
behind the root (gripLength=.19, gripWidth=.07, gripDepth=.035). A soft radial
white/yellow layer joins the blade, two guards and grip (junctionSize=.15,
junctionGlow=.14); three crossed sheets keep it visible from different angles.
Grip and junction remain fixed-size and follow attack/charge visibility. Two symmetrical pointed light
diamonds form its guard, connected by a faint narrow light joint. They lie in
the blade plane and stay fixed at guardLength=.17 / guardWidth=.065 /
guardDepth=.035 when the outer energy blade extends. No Sprite/texture/camera projection.
BaseLength=.85, width=.085, depth=.04 remain fixed; only the outer energy layers
lengthen to attack radius. Hidden idle/Dash/Skill/empowered approach/Burst alone;
visible during attack and Ulti charge. Normal attacks animate windup/sweep/recovery.

HandOffset=.28 forward and HandSideOffset=.14 toward the cast-relative side keep
its root outside the entity center. EveSweepAction applies the planar hand offset
once to the frozen action root: blade, trail and hit queries share that origin.
Charge uses the same hand offset. Height=.14 remains per-profile / charge tuning.
Old EveSword.png is retained as an unused historical asset.

Local +X is aim; startAngle/endAngle sweep lateral +Z/-Z. Tilt produces right/down,
left/up and horizontal planes. Blade, trail and damage keep the same world pose;
camera movement never changes the geometry or attack direction.

EveSweepVisual records recent blade positions (max(innerRadius,.08 radius) -> tip),
covering almost the whole base/extended light blade. Its root edge is softer than
the outer edge; short-lived diamond sections taper into the recent-motion tail.
Each ring has near/upper/far/lower vertices, closed connecting faces and caps.
Width AND depth shrink toward older samples. Per-profile trailDepth controls the
cross-section; shader face contrast makes sides visible rather than alpha-clipping
all side faces. TrailRenderer adds a narrow tip highlight; light particles travel
tangentially with positive/negative out-of-plane drift. Visible stretched sparks
use per-profile particleSize (.07/.09), particleLifetime=.42, particleSpeed=1.8
and particleRate=160/240. Hits emit white/yellow sparks with radial velocity.
TrailFraction=.65/.7 retains about .2-.3 seconds of motion; Afterglow=.18 fades
frozen geometry after the cast ends, without extending hits or input locks.
Completed particles continue on entity time, then the visual destroys itself;
cancellation clears active geometry immediately.
Hit queries still sample both time and angular distance, once per hostile per action.

Burst emits gold light motes from the body, with varied upward velocity, lateral
drift, size and lifetime. It does not create a sprite outline or persistent floor
ring. Activation retains a brief expanding ring and sparks. The sword is brighter
only while visible during attacks.
Ultimate draws light particles into the raised sword during its one-second charge.
Dash uses a narrow movement wake. Cosmetic particles use entity time; normal/Repeat
sword animation and hit timing share the ATK-speed-adjusted action clock.
Completion drains short particle tails; cancellation clears active blade/trail.
Bloom uses the existing scene setup; production effects never change global cameras
or volumes. VFX Graph is not needed for these effects.

The seven Ability IDs are public constants on EveCharacterSkill. Manager loads
first, then stats, then Skill. Gauge starts0, Dash3, Skill/Ulti ready by JSON.
Runtime Awake supplies a missing manager on older scene entities without saving
or modifying authored scene files.

Disabling Skill cancels its current cast; the Ability manager continues ticking.
Disabling the entity pauses Ability timers. Death cancels casts, removes Burst
effects without refund and resets Eve's states/resources; unload destroys owned
runtime subscriptions, motions, pools and presentation. Reload starts fresh.

## Verification

Smoke covers the new moveset plus legacy action/Ability contracts.
EveMovesetEditorChecks.Run() checks actual PhysX hits, targeting, rewards,
empowerment, expiry interruption, finisher hold, movement/death in a disposable scene.
Existing native Slash/Ability regressions also pass.
EveConfigurationEditorChecks.Run() changes JSON cooldowns, capacities, durations,
rewards, bonuses and attack geometry/damage to prove content controls these values.
It also verifies manager-only and legacy character-ID loading.
EveMovesetPlayModeChecks.Run() starts an asynchronous input fixture in Play Mode,
tests J Tap/Repeat/release/U/K/O/I and restores devices/settings/components.
Result is saved in .utmp/EveMovesetInputResult.txt.
EveVfxEditorChecks.RenderPreview() checks moving geometry, TrailRenderer positions,
real ParticleSystem emissions, phase cleanup and actual URP Bloom render, exporting
.utmp/EveSweepSequence.png (rows right/down, left/up, Ulti; columns .16/.32/.48/.66
normalized action time) without saving the active scene.

No player build, remote replication, hardware gamepad or production-scale
performance test was performed. Balance and visual tuning still need gameplay review.

EveSwordPlayReview.Run() is an opt-in ACTUAL ErMode Play Mode review. It drives the
existing Eve with dynamic interpolated Rigidbody, enemy collider and world ground;
captures ScreenCapture Game View frames at end-of-frame, and writes evidence to
.utmp/EveRegistryMotionReview/Results.txt. It tests movement-directed Dash with an opposing nearby target, no-target Dash,
wall clipping, actual Skill and empowered approach,
attack-only sword visibility, upward Burst particles,
sequential recovery/progress, interruption, combo rewards, Repeat and Burst finisher.
It repositions scene entities and temporarily widens the gameplay camera for Ulti
review only. Run in a backed-up scene and STOP PLAY MODE afterward to restore the
authored unsaved scene. Original ErMode is never saved/reloaded by the review.

The depth review freezes one actual attack frame, captures Game View at gameplay
and a lower side angle, and asserts the world blade rotation remains identical.
Time scale, camera pose/FOV and Cinemachine are restored immediately afterward.

PNSharedDashEditorChecks.Run() verifies two non-Eve players with only Dummy content:
Rigidbody distance/interpolation, independent3-charge recovery, immediate input
configuration, disable cancellation and unload/reload. Smoke additionally tests
registry overrides of Action implementation and default Dash parameters, CDR and
recovery progress, and rejects an evaluated distance above its registered maximum.


## Dash endpoint timescale field (RulesVersion55)

Eve's registered `base:eve_dash` motion distance is2 units (duration .16 unchanged).
Its registered OnComplete Operation effect uses Position=Self and base:create_area:
radius2.5, Game duration15, reduction.9, minimumReduction.9, fixed center,
independent lifetime, Include targetLayers ["Enemy","Projectile"] (layers 7 and 8). Each completed Dash
creates one instance at actual physics endpoint, including wall clipping. Cancelled
or rejected Dash does not create a field. Regions survive source unload/death and
expire by Game time; Eve/Player layers are excluded. Two overlapping fields can
reduce effective timescale to0; damage still receives normally. No hardcoded
completion tuning in Skill logic; selected content/Operation/area factories apply.
Shared player Dash keeps its own registered distance. See Core/Area/PNAreaREADME.md.
