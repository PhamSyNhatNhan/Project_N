## World execution and owned cleanup (Rules79 / Mod API6)

World pause and actor holds stop authoritative Buff advance without disabling the
manager, losing contributions or ending publication. State capture/replication and
cleanup remain available. Live application/trigger requires runnable authority.
`PNBuffCollection.ReleaseOwned(handle)` ends that exact owned handle through normal
cleanup while the world is paused or the actor held; it does not apply or trigger
Buffs. The collection captures its original entity load context. Exact release also
works after gameplay invalidation while that same authoritative load still owns the
collection, until disposal. It cannot borrow a later load's authority; clients,
different sessions and disposed collections remain rejected. Ordinary mutations
still require runnable authority. Detached collections retain their mutation guard.
Areas use it to revoke only their grants. A failed revocation remains owned
for retry. Historical source snapshots remain valid; live targets use world policy.
Network Buff streams include world incarnation, membership and subscription scope;
old subscription traffic cannot apply to a new observation of the same actor.

## Current: Typed Buff presentation and Mirror adapter (RulesVersion76 / Mod API6)

`PNBuffManager` adds `TryCaptureState`, `StateChanged`, `OptionalCuePublished`,
`StageState`, `ApplyChange`, `HasBaseline` and `AppliedRevision`. `PNBuffFrame` and
`PNBuffChange` preserve immutable state, canonical group ordering and removal/cues;
the existing `PNBuffWireState` presenter API stays compatible. Use the manager's
stable `Presentation` interface for UI subscriptions across baseline refreshes.

Gameplay behavior remains authoritative. A client replica never recreates Buff
effects or Skill binding leases. Buff removal can leave a committed Skill cast running
under FinishCurrent; presence/icon state cannot identify the executing Skill.
Custom behavior private fields remain host-only unless explicitly published as
presentation data. Payload guards run before cloning; JSON/byte/depth/node bounds and
presenter-specific schema/version limits are separate from gameplay stack/lifetime.

See [coop API](PNBuffCoopREADME.md) and `Docs/MirrorP3Implementation.md` for the current
Mirror baseline/live/grace contracts, actual evidence and remaining platform limits.
Legacy codec/actor APIs remain available for their callers, but are not Mirror's path.

## Current: Skill binding lease template (RulesVersion75 / Mod API6)

`base:buff_skill_binding_template` grants a selected Skill profile into a slot through
an owned lease. Payload: duration, slot, profileId, priority, applyTransition and
releaseTransition. Suspend/expiry/cleanup returns the lease; resume regrants without
casting. Pending receipt failure ends the Buff with RuntimeError. See
[Skill binding contract](../Skill/PNSkillBindingREADME.md). Mirror P3 remains separate.


## Current: StackCount failure isolation in publication (RulesVersion71 / Mod API6)

A StackCount exception during publisher Capture is reported, the instance is retried once at the next outermost
Capture and ended with RuntimeError if the retry fails; other instances keep publishing and the manager is not torn
down. Contract details: `PNBuffCoopREADME.md` ("StackCount failures during publication").

## Replica-derived countdown (RulesVersion70 / Mod API6, additive)

A Buff definition may opt in to a countdown slot: `definition.WithCountdown(new PNBuffCountdown(slot))`
or content field `"countdown": {"slot":"BottomRight","rounding":"Ceiling","zero":"Hide"}` on a
`project_n.buffs` entry. It is off by default and NEVER inherited: `Derive` drops it, so a
derived/mod profile declares its own. v1 supports `Ceiling` rounding and `Hide` at zero only.
The effective profile (after override) selects the descriptor; `WithCountdown` copies every
definition component, registers nothing and keeps the existing constructor/`Derive` signatures.

Rule (one helper for host and replica): `Ceiling(Max(0, Expiration - time))`, hidden when
the result is 0, there is no expiration, or the value would exceed Int64. It is a pure
function: an out-of-range value hides that evaluation and recovers when valid again; the
host reports overflow once per writer and a replica once per instance/profile.
An active overflow set is checked on every `Advance`, so clock progression alone
can restore the value. Suspended/stopped entries are excluded; lifecycle and profile
changes clear or refresh membership. Ordinary countdowns retain their scheduled gate.

Host: an opted-in writer tracks the value itself and never calls the legacy
`WatchRemainingSeconds` callback, so stock Buffs stop publishing numbers each second.
Replicated Numbers keep the slot `null` while running and carry the last tracked value
while suspended (kept through resume, override and snapshot). Replica: derives the slot
from `VisualTime` and emits a UI-only `Updated` when the integer changes; wire revision,
sequence, cues and presenter rebuild are untouched. Profiles without a descriptor
publish exactly as before.

Migration: `PNBuffCollection.PresentationSnapshot/PresentationChanged` read the RAW
writer Numbers, so an opted-in stock Buff shows a `null` countdown there, also while
suspended. Read the countdown through the replica (`PNBuffManager.Presentation`), as the
HUD does. Stock definitions that opt in: every `PNBuffTimedBehavior` template, Burn,
Electrocution, Frostbite, the three vulnerabilities and `timescale_reduction` (not the
area variant). Specification: Docs/BuffCountdownSpec.md.

## Previous: Actor lifecycle integration (RulesVersion67 / Mod API6)

Buff collection is initialized during Prepare and guarded until whole-batch publish.
Publisher epoch is entity generation; actor envelope ordering drives ApplyLifecycle,
without per-target recovery. Presenter/UI flush after publication. See PNBuffCoopREADME
and Assets/EveEngine/Networking/EveActorLifecycleREADME.md. Policy/transport remain open.

## Historical: Buff reliability and presentation coop (RulesVersion66 / Mod API5)

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
Move/Skill/HP/Area replication is implied. See PNBuffCoopREADME for epochs/sequences,
resync/cues/presenter scope and trust limits. Earlier no-replication statements refer
to the former local contract; the new transport-independent proxy is implemented.

Read [PNBuffCoopREADME](PNBuffCoopREADME.md) for complete implemented contract.

# Current modifier routing (RulesVersion65 / Mod API4)

Stat-template data and Momentum modifier entries accept optional calculationId
and contributionId. Vulnerability data also accepts them, including group0.
contributionId is valid only on group0 and must name a registered definition.
Default omitted placement is after for Scale/Mitigation; explicit base:before
adds flat before percentage groups. Stat-template statId can target a registered
combat tag (e.g. heal/fire) as well as a stat. Built-in Eve data remains omitted,
deliberately taking the new after default. Definitions, routing, dependency checks,
resource ownership, cleanup and snapshot contracts remain enforced. Full details:
Assets/Core/Entity/Shared/Stats/Calculation/PNStatCalculationREADME.md.

# Registered entity Buffs

Implemented in RulesVersion53, additive Mod API2. Shared by player/enemy; separate
from passive Ability, legacy StatusEffect and Rogue Buff. Templates are registered
with concrete Eve Burst integration for Blazing Assault/Rising Momentum. Generic
templates are not automatically applied. RulesVersion61 adds event-fed Buff UI
and four-corner publication; Snapshot still exposes gameplay state per instance.

## Content and identities

`PNBuffBehavior.StackCount` reads the behavior's stack state (null when unsupported).
`PNBuffSnapshot.Stacks` captures that value independently of UI visibility/formatting.
The getter was renamed outright during development; there is no old-name alias.
Stack behaviors override StackCount. Recompile code against the current SDK.
This naming change does not change stack/timer gameplay or implement the proposed
four-corner presentation contract. RulesVersion60 / Mod API2 were retained for that
development rename; no released mod compatibility is claimed.

## Four-corner presentation (RulesVersion61, additive Mod API2)

Each behavior receives its own Presentation writer before Apply/behavior startup.
`Presentation.Publish(new PNBuffNumbers(topLeft: 0, topRight: 1,
bottomLeft: -2, bottomRight: 15))` replaces ALL four optional Int64 slots. Null hides
and clears a slot; zero/negative values are valid. Identical publications are ignored.
Behavior chooses numbers, corners, rounding and visibility. Project_N convention
is stacks top-right and remaining seconds bottom-right, not a Core restriction.
Current DOTs show stack1. StackCount/Stacks/Remaining remain independent state APIs.

Buffs.PresentationChanged supplies Added/Updated/Removed/Reset with collection-local
monotonic sequence and immutable state. Added/Updated include full four-slot payload,
handle, canonical/profile IDs, icon/glow/show metadata, suspended and GroupOrder.
PresentationSnapshot reads latest cache only, in live-ID group/instance order;
it never calls behavior. UI subscribes and reads cache once on attachment, then
receives events. Publication uses the existing mutation coordinator. Failed Apply
produces no Added. Listener faults report without ending gameplay Buffs; callback
mutations retain queued semantics. Existing Changed observers remain supported
and are not sent timer-presentation ticks.

Presentation.WatchRemainingSeconds(callback) is optional. It observes the instance's
existing Time/Expiration and chosen clock; callback receives ceil seconds or null,
and behavior publishes the full payload. Callback runs only when the displayed
second changes, including refresh/resume effects. Long frames publish final UI state
without skipping gameplay ticks. UI owns no countdown. Callback faults retire the
observer and report diagnostics, not end the Buff. Duration beyond Int64 display
range reports and retires the observer without changing gameplay duration.

Writer and timer observer belong to one behavior incarnation, independent of its
suspendable resource scope. Override retires the old writer and clears numbers;
instance identity/order stays. Pending suspended replacement remains empty until
startup. Old callbacks cannot publish into replacement/ended/unloaded instances.
Suspend retains cache and emits metadata; resume does not replay Apply. End emits
Removed; collection disposal clears cache, emits Reset and clears subscriptions.
This local contract does not implement network replication.

Built-in timed behaviors own their composition/helper in Game; custom behaviors
may publish any layout. Area timescale and Blazing Assault use no numbers. Stat,
DOT and Rising Momentum show stacks from1; timed effects show remaining duration.

`buffs` stores immutable `PNBuffDefinition`; `buff_tags` stores
`PNBuffTagDefinition`. Core vocabulary is registered by PNContentModule; factories,
schemas and gameplay templates by PNGameplayContentModule. Use public base aliases
where provided. The sealed registry selects implementations while identity remains
canonical. Content overrides and runtime full overrides are separate operations.

Definitions contain a factory, cloned payload, original tags, CanDispel,
multiple-instance/group limits and icon/glow/showInUI metadata. A factory owns its
payload and optional contextual-input validator. Omitted input validator accepts
no arbitrary input. Callers cannot replace registered tuning. Duration, ticks,
stack formula, reapply/full behavior and migration belong to the implementation.
There is no obligatory common stack/duration schema. Public IDs are registry IDs;
enums only describe closed runtime policies/clocks/results.

`PNBuffDefinition.Derive` creates another registered profile using a code factory;
it never mutates the original. JSON never names a CLR type. Example schema:

```json
{
  "schemaVersion": 1,
  "extensions": {
    "project_n": {
      "buffTags": [{ "id": "addon:stagger" }],
      "buffs": [{
        "id": "addon:stagger_effect",
        "factoryId": "base:buff_control_template",
        "data": {
          "duration": 2, "clock": "Game",
          "permissionIds": ["base:move", "base:jump", "base:skill_all"],
          "interrupt": true
        },
        "tags": ["base:debuff", "addon:stagger"],
        "multipleInstances": false, "maximumInstances": 1,
        "scope": "Target", "canDispel": true, "showInUI": true,
        "iconPath": "", "glow": ""   // glow: "" (white), a preset (white red orange gold green aqua blue purple pink) or hex #rgb/#rrggbb/#rrggbbaa; anything else is refused
      }]
    }
  }
}
```

Pack namespace must own definitions and declare project_n/dependencies for external
references. Engine's generic extension interface prepares non-mutating batches,
checks overrides against staged definitions, then commits after all references
validate. Core parses Buff fields, rejects unknown/duplicate fields, validates
factories/tags/permissions/stats/profile references. Same-batch tags, Buff target
references and built-in stat definitions are supported. Factory templates must
already be code-registered. Custom factories must validate their own payload and
references; built-in validators do not infer arbitrary mod schema.

Registered templates (all have base aliases): buff_stat_template,
buff_dot_template, buff_bomb_template, buff_control_template,
buff_stack_limit_template, buff_override_template, buff_immunity_template,
buff_skill_effect_template.
Their values are demonstration template defaults, not assigned gameplay balance.
buff_control_template has neutral metadata, not a finalized Stun/Silence design.
Derive/register a concrete profile with its actual tags/parameters.

Concrete `project_n:burn` / `base:burn` is registered and applied by Eve Burst's
Attack-hit contribution. Its Buff tags are debuff/dot/fire. `damage_over_time` was removed from
buff_tags without an old alias; `fire` in buff_tags is separate from combat tags.

## Elemental Buffs (RulesVersion48)

All original project_n IDs have public base aliases. Five new concrete Buffs are
registered independently; Electrocution/Frostbite use the DOT factory, not Burn.

| ID path | Cap | Duration | Period | ATK ratio per tick at n stacks |
| --- | --- | --- | --- | --- |
| burn | 35 | 15 | .8 | .05*n |
| electrocution | 10 | 5 | .4 | .10+.10*(n-1)+.03*(n*(n+1)/2-1) |
| frostbite | 5 | 12 | 1.5 | 1.50+.50*(n-1) |

All three concrete DOTs use target Entity clock including its minimum, Live ATK,
source grouping, no crit/Attack Speed and CanDispel=true. Stack reductions
recalculate current damage, without accumulated history. Buff tags: debuff/dot
and the corresponding element. Unrefreshed ticks: Burn18, Lightning12, Ice8;
exact-expiry Ice tick precedes removal. No extra expiry damage.

fire_vulnerability (Hỏa Nhược), ice_vulnerability (Băng Nhược) and
lightning_vulnerability (Lôi Nhược) grant target-owned matching combat-tag modifiers:
value+.75, group2, calculation base:mitigation. Duration15/Entity, one Target
instance, no stacks, reapply refresh from any source, CanDispel=true. Other-element
damage and sender Scale are unchanged. Same group2 adds; group1 resistance
multiplies separately. Tags: debuff/vulnerability/corresponding element. stat is
registered separately but NOT attached. Physical/Magic/True variants are deferred.
Only Eve's existing Burst applies Burn; new definitions are not auto-attached.

## DOT template and Burn

DOT factory schema (all fields required in registered payload):

```json
{
  "duration": 15, "clock": "Entity", "period": 0.8,
  "amount": 0, "attackRatio": 0.05, "maximumStacks": 35,
  "damageTags": ["base:magic", "base:fire"],
  "iframeDuration": 0, "canCrit": false
}
```

This is Burn's tuning. Generic DOT template defaults: duration5, period1, amount10,
attackRatio0, maximumStacks1, physical combat damage, Game clock, iframeDuration0,
canCrit=false. A single instance per source entity per target is the default
definition: multipleInstances=false, maximumInstances=1, scope=Source, CanDispel=true.
The source instance cap is not a stack cap. Derivations explicitly declare grouping.

Optional Apply input is `{"stacks":3}`; null or `{}` adds1. Amount must be a positive
Int32; unknown fields, null/fractional/zero/negative counts reject before effects.
Initial/reapply stacks clamp to maximum; additions use widened arithmetic. Reapply
always refreshes the registered duration, even at cap, without moving the tick
schedule. Tick damage is `(amount + currentSourceATK * attackRatio) * currentStacks`.
Burn at35 stacks produces175% current ATK each .8 Entity seconds. No Attack Speed scaling, automatic
crit or Attack OnHit chain is added. Packets still use normal scaling/mitigation/
iframe/attribution/events. Burn without refresh ticks18 times (last14.4s) then expires
at15s, with no expiration damage. Cap1 DOT still supports reapply/manual triggers.

Optional paired additionalStackAttackRatio/additionalStackGrowth fields change ATK
scaling to incremental: attackRatio is the first-stack ratio, each added stack k>=2
adds additionalStackAttackRatio+k*additionalStackGrowth. Both fields are required
together, finite/nonnegative; omit both to retain the linear formula above. Flat
amount still multiplies n. attackRatio contributions modify first-stack ratio in
incremental mode, per-stack ratio in linear mode; additional fields are registered
tuning, not contribution contracts. Formula uses double closed-form arithmetic.

DOT reads loaded source stats live, including a dead source. PNBuffSource.LoadedSource
allows that access without changing Live or ordinary dead-source CreateDamage rules.
PNStat.CaptureBuffCombatSource captures current calculation values at each effect;
custom captured calculation implementations may override this dedicated hook.
DOT caches the last valid ATK/combat capture on Apply/reapply/trigger. If source
unloads/destroys, that cache supplies fallback packets until expiry. Cache is refreshed
only while the original source is loaded; it is not an Apply-time ATK snapshot mode.
Packet creation still requires the original active authoritative session.

Collection.Trigger(handle) and ChangeStacks(handle, signedDelta) are coordinator-
serialized requests with receipts (may be Pending). Unsupported, stale, suspended or
dead-target execution rejects. Successful statuses are Triggered/StacksChanged.
Automatic and manual triggers call the same behavior Trigger function; manual calls
do not consume stacks, refresh duration or shift ticks. Manual plus automatic at the
same clock point both execute. Direct stack changes do not invoke Apply or refresh;
zero ends with StacksDepleted. Decreasing cap clamps immediately, including suspended
instances; increasing cap does not restore discarded stacks.

DOT supports PNBuffStackLimitContract field maximum (additive integral cap >=1) and
PNBuffValueContract fields amount/attackRatio (nonnegative additive aggregate).
DOT-to-DOT full override migrates stacks/cache/time only for equal clock/period;
otherwise initializes a fresh one-stack state/timer. This applies only to DOT
behavior; no universal Buff stack contract was introduced. Future crit-enabling Buff
contributions remain separate work; canCrit is currently registered profile tuning.

Skills use registry-created trigger_buff/change_buff_stacks Operations, including
through buff_action. Trigger payload: `{"buffId":"base:burn","sourceSelection":"Caster"}`;
stack payload additionally has signed integer delta. sourceSelection is mandatory
Caster or All, selecting each matching target instance explicitly; no arbitrary
instance selection by ID. A direct collection call selects an exact live handle.
Removing the granting Action never removes a collection-owned DOT.

Authoring workflow for agents: `.agents/skills/project-n-buff/SKILL.md`.
Concrete Eve profiles are also registered with base aliases: eve_blazing_assault
and eve_rising_momentum. Both use Source grouping and Skill-owned lifetime (no
independent expiry), buff tag and ordinary dispel. Blazing grants Live Eve Attack
OnHit -> Burn; Momentum adds per-stack stat payload modifiers (ATK group2 .1,
Attack Speed group0 10). See Assets/Game/Entity/Character/Eve/EveMovesetREADME.md.
This README remains the API contract; the skill explains implementation choices,
real examples and verification without assigning new gameplay rules.

Buff Skill integration: Context.ModifySkill owns a PNSkillContribution lease on the
loaded target PNSkill.Effects state. buff_skill_effect_template takes registered
duration/clock/contribution data; default demonstrates Snapshot Attack OnHit -> Burn.
Reapply refreshes its timer, suspend releases the contribution, resume reacquires
without a skill activation or initial effect replay. Selected full override cleanup
returns the previous lease. No Skill component is auto-created on a stat-only entity;
applying such an effect there fails explicitly. Categories/tags/effects in contribution
are not buff_tags and do not bypass their registries/reference validators.
See ../Skill/PNSkillREADME.md for trigger, selector, Snapshot/Live and auxiliary rules.

## World VFX

DOT/vulnerability payloads optionally declare visual, for example:
`{"kind":"Arrow","slot":-1,"color":[1,0.18,0.04],"size":0.14}`.
Kind is Arrow/Fire/Lightning/Ice, slot integer -16..16, size finite (0,2], RGB three
finite channels0..1. Unknown fields reject. Missing visual allocates no renderer.
Presentation follows selected profile and clock, not mutable tags/UI metadata.

RulesVersion49 defines size as a fraction of current projected body-collider height,
capped relative to width for narrow entities. Arrow slots and shape/motion extents
also scale with the collider. Body source is an enabled non-trigger root Capsule,
CharacterController, Box or Sphere collider; child hitboxes are never selected.
Center uses TransformPoint; dimensions use shape parameters and current transform
projected onto camera right/up, NOT renderer bounds or collider AABB. Collider edits,
replacement and uniform/nonuniform/mirrored scale update each LateUpdate. Missing
supported collider or singular parent scale hides VFX without changing Buff
lifecycle; no fixed-size fallback or collider/config edit. World mesh points use
inverse transform so inherited parent scale is not applied twice.

Vulnerabilities each own one hollow rounded down-arrow with gentle bob/glow,
following pause glyph styling: Fire left/red, Ice above/blue, Lightning right/yellow
and slightly below Fire. RulesVersion51 sets registered arrow size to .098 (70%
of prior .14), with width cap .231 of collider width. RulesVersion53 slot zero center
is .35 body height above collider center (85% from the projected bottom), inside
the upper body, with .01-height bob. Negative/positive slots flank it at .12/-.04 height.
Horizontal spacing is .75 collider width per signed slot. Bobbing remains.
Registered Frostbite visual.size is .117 (75% of .156, 45% of original .26),
shrinking snowflakes; ice dust/mist and movement remain unchanged.
No shared/rotating color. RulesVersion50 DOTs use rising orange-yellow particles
that shrink/fade plus embers, short branching yellow-white bolts/spark points,
and six-armed blue-white snowflakes with side branches, slow rotation/downward
drift, ice dust and low cold wisps. Each visual is independent; overlapping
profiles are accepted without coordinated layout or suppression. Fire particles
use the existing clock-driven procedural mesh sprites, not Unity ParticleSystem.
Alpha body and additive
glow use separate submeshes/materials. Smooth masks/procedural noise animate using
explicit _BuffTime from the selected clock, never global shader _Time. Tick pulses
the visual; intensity is bounded by stack/source count.
DOT instances on one target with the SAME selected profile share one renderer.
Different profiles remain separate. Additional profiles can author their own slots.

Behavior resource scope owns each visual lease. Last release deactivates/destroys
renderer, mesh and both body/glow materials; other sources survive. Suspend returns leases; resume
reacquires without replaying Apply. Reapply preserves animation. Override, death,
unload/destroy/session end use normal cleanup. Dedicated BuffElemental URP shader requires no
Eve entity; no scene, Character Visual, camera or UI assets are edited. Presentation
never advances Buff clocks. Replication remains unimplemented.

## Apply and instance groups

```csharp
var source = PNBuffSource.Capture(casterStat);
var buffs = target.GetComponent<PNBuffManager>().Buffs;
var receipt = buffs.Apply(new PNBuffApplyRequest(buffId, source));
if (receipt.Status == PNBuffStatus.Applied) {
    var display = buffs.Get(receipt.Handle);
}
```

Apply resolves selected content and validates the source session and contextual
input. Incoming effective profile/tags incorporate matching future contributions
before immunity and effects. Reapply checks incoming and existing effective tags.
At capacity, the existing behavior receives OnFull; single-instance behavior
receives OnReapply. The default returns false. Behavior uses sanctioned APIs to
implement its own handling; the manager preserves group limits and ownership.

Group scope is Target or Source entity. A projectile/cast does not invent another
source. System-created requests require an explicit stable source identity.
Multiple instances are separate from stacks. The default group shrink keeps oldest
instances, ending excess with LimitReduced. Instance-group limits stay attached
to original admission/group identity across per-instance behavior overrides; use
the separate group contract to alter them.

Handles identify a collection/load generation plus a monotonic instance number.
They are invalid after end/unload; IDs do not select an arbitrary instance. Runtime
snapshots/tags are immutable copies. Apply to dead/suspended targets is rejected by
the standard collection. Retained Buffs choose their own target-death behavior.

## Behavior, scheduling and ending

Implement PNBuffBehavior. OnApply runs once for initial creation. OnReapply/OnFull
own stack semantics. OnScheduled receives keys registered through context.Schedule.
SetDuration is optional; no duration means permanent. Positive finite delays and
periods are required, with a clock-precision progress check.

Choose Game, Entity (with minimum) or EntityWithoutMinimum (nonnegative raw scale).
All crossed schedule points are processed chronologically; ties preserve instance
and schedule order, and scheduled ticks at expiry precede expiry. The scheduler
does not drop valid catch-up ticks. Mutation completes between events, so ended
Buffs receive no later ticks. Callback-created/restarted behaviors do not consume
historical elapsed time from the enclosing frame. Clock advancement from a callback
or recursively is rejected. Presentation never advances clocks.

OnEnding(reason, finalSnapshot) is virtual/default no-op, called once. Reasons:
Expired, Dispelled, Bypass, ImmunityRemoved, LimitReduced, Replaced, Death, Unload,
SessionEnded, RuntimeError, SourceUnavailable. Mark Ending first, run the callback,
clean resources, detach/invalidate, then drain queued mutations. Cleanup exceptions
are reported but all owned resources are attempted. Runtime behavior faults end
the instance; apply receipts preserve the error. OnCleanup runs once per retired
behavior. Cleanup and lease release must be idempotent in implementations.

Ordinary removal respects the active profile's CanDispel. Explicit bypass skips
that check but uses identical cleanup. OnEnding can use the normal combat API or
enqueue work for other Buffs; it cannot grant new owned resources to its ending
instance. The bomb template deals damage only on Expired, not every removal reason.

## Contributions and ownership

Contribution leases have independent IDs and diagnostic owner names. They are
revocable and never save/restore a shared effective value. Contract type identifies
what is being contributed; the receiver decides arithmetic. Built-in contract
types cover stack maximum, values and group instance maximum. Custom subclasses
of PNBuffContribution/contract types support custom formulas/behavior contracts
without another registry. Supports/ValidateContributions declare acceptance;
validation should be pure, and unsupported initial contracts reject the grant.

```csharp
using (buffs.Contribute("equipment", new PNBuffNumberContribution(
    typeof(PNBuffStackLimitContract), "maximum", 2), buffId)) {
    // Receiver explicitly chooses additive calculation for this contract.
}
```

Selection: Current, Future or CurrentAndFuture with optional matching Buff ID.
An exact handle requires Current and no ID filter. A future scope survives target
retirement and ends with its source lease. Context.Contribute/Immune attach their
leases to the behavior resource scope. External contributions stay in the instance
ledger across behavior transitions. Group limit contributions require a group ID
filter (or all groups), CurrentAndFuture and no exact instance handle.

The stat template accepts maximum/value fields, clamps stacks when maximum drops,
including during suspend, and never restores discarded stacks. Custom behaviors
can choose their own group decay/limit semantics; they must not blindly restore
old values on removal. Receiver validation rejects invalid aggregate grants before
effects; invalid state after a source revocation fails cleanly rather than retain
a lease whose owner ended.

## Full overrides and tags/immunity

PNBuffOverrideContribution selects an entire registered profile/behavior while
retaining Buff ID/handle. Highest integer priority wins; equal priority chooses
newest authoritative grant. Losing leases stay eligible. Revoke recomputes from
remaining owners. New ID requires remove/create instead.

Replacement validates before old cleanup. TryMigrateFrom is optional, default false;
false clears behavior state, timers/schedules and clock, then initializes the new
behavior through OnBehaviorStarted. True preserves timers/schedules; implementation
copies its own state and restores resources. Migration preparation cannot grant
owned effects. Behavior transitions do not invoke instance OnEnding or expiration
gameplay. External contracts unsupported by the active behavior remain dormant;
the new behavior must explicitly support them to use them. Only still-live owners
return when a compatible behavior returns. Transition cleanup/effect failures end
the instance without pretending to undo damage already committed.

Tag modifiers add/suppress registered tags; suppress wins. Buff/debuff/neutral are
tags, with no separate category. Tags never imply effects or Control permissions.
Effective profile and tags are recomputed before new behavior effects. Immunity
matches canonical Buff IDs or effective tags, shared by all entity types. Enemy
classification does not automatically supply immunity.

Immune defaults to block new/reapply, retain existing. Purge additionally ends
matching existing instances using bypass CanDispel and ImmunityRemoved. Changes
to profile/tag/immunity trigger re-evaluation. Block-only immunity prevents a new
immune replacement profile from starting while keeping the old behavior. Removing
immunity may permit a retained pending override, but never resurrects purged Buffs.

## Unity, combat and Control

PNStat's preparation ensures PNControlManager/PNBuffManager before loader captures
steps. Buff manager depends on stat/control, shares one coordinator per session,
ticks without a Skill component, subscribes to death and unloads before stat/control.
Player/enemy collections are independent. Mutation requires active offline/host
authority; read-only client queries are available, replication is not implemented.

Whole entity/manager disable retains state, pauses clock and returns behavior-owned
effects. Resume restores resources without replaying Apply/initial interruption.
Overrides while suspended initialize on resume. Contribution changes can clamp
state while suspended, but context forbids granting effects then. Disable a Skill
alone does not erase Buffs. Default target-death hook ends Buff with no extra effect;
custom hooks can retain it. Direct HP-to-zero is detected on manager advance as well
as the normal Died event. Unload/destroy/session end always dispose runtime; cleanup
does not depend on retaining gameplay authority. Buff has no floor-change hook.

Context.ModifyStats owns a unique source key per scope, distinct from Buff ID.
PNStat protects these sources from floor-local cleanup and excludes them from
persistent transfer snapshots. Game modes decide Buff retention/removal; restoring
Buffs on a new entity is a separate game-mode task, not automatically serialized.

PNBuffSource captures source ID/session and optional weak live access plus immutable
PNCombatSourceSnapshot. Snapshot clones base values, modifiers and resource currents,
uses selected registered calculation logic, and never reads a destroyed component.
It can create packets after source death/unload while the original session remains
active/authoritative. Each snapshot damage packet rolls crit once; receiving uses
normal mitigation/iframe/attribution/events. Snapshot heal uses normal receive
rules. Arbitrary custom MonoBehaviour calculation state is not automatically copied;
custom snapshot semantics belong to the implementation/capture hook. A custom
PNCombatSourceSnapshot subclass can use the protected copy constructor to retain
immutable base capture/authority, then override calculation with captured state.

Bomb behavior chooses snapshot or live; its Live mode stops with SourceUnavailable
when live source is dead/unloaded. DOT has the persistent Live/fallback policy above. Context
supports autonomous Damage/Heal without fake PNSkillActionContext or direct HP edits.

Control context owns PNControlState leases. Immunity is checked before effects.
Permission grant and explicit TryInterrupt are separate; rejected interruption
keeps Buff/leases and blocks later activations. Resume never repeats the initial
interruption request. Buff end returns only its own leases, preserving Action/other
Buff owners. No bool snapshots, input reset, Mouse code or timeScale assumption.

Registered buff_action runs selected apply_buff/remove_buff/bypass_remove_buff/trigger_buff/change_buff_stacks
Operations using perform {operationId,data}. Factories come from registries, and
payload/reference validation follows the Action pipeline. Skill owns targeting and
admission. Disposing the granting Action/Operation does not remove collection-owned
Buffs; an Action-scoped effect needs an explicitly owned removal/lease contract.

## Ordering, failures and verification

PNBuffMutationCoordinator serializes mutations across entities. Callback-created
requests return Pending receipts, completed at the next mutation boundary. Damage
may commit synchronously; any resulting Buff mutations are queued. Observer errors
are isolated. A runaway queue beyond 10000 commands in one boundary faults the
coordinator, fails pending receipts and retires its collections/resources; recreate
runtime/session rather than silently drop commands or resume a corrupted queue.

Smoke: PNBuffTests in EveRunSmokeTests. Native Edit Mode: PNBuffEditorChecks.Run.
Actual unpaused Ready Play Mode: PNBuffPlayReview.Run. Evidence under
.utmp/BuffReview. Checks cover factories/JSON/seal/dependencies, identity/ownership,
stack/instance shrink, clocks/ticks, callbacks/cycles, dynamic tags/immunity,
override priority/migration/reset, stat/combat/Control, actual held input and
disable/death/unload. Player builds, CoreCLR and network replication remain separate.
Shield, production Stun/Silence tuning and legacy migration remain separate work.
Timescale reduction and Buff UI are implemented in the later API sections.


## Timescale reduction (RulesVersion54)

`base:timescale_reduction` accepts required Apply input
`{reduction,minimumReduction,duration}`. `base:timescale_reduction_area` accepts
`{reduction,minimumReduction}` and has no expiration. Both use tags `debuff` and
`timescale`, independent instances (including same source), no stacks/reapply merge.
Timed instances use Game duration and CanDispel=true; area instances CanDispel=false.
Reduction/penetration must be nonnegative finite float-representable values (may
exceed1). Duration is positive finite Game seconds. Target needs active EveTimeScale.

Each behavior resource scope owns exact Scale=-reduction and
MinScaleReduction=minimumReduction modifier IDs on the target clock. Effective
scale remains max(max(0,MinScale-sum(penetration)),1+sum(scaleModifiers)). Source
loss does not remove target-owned effects. Suspend returns modifiers; resume
reacquires without resetting remaining time. Full behavior reset reads cloned
`PNBuffContext.InitialInput` from the original Apply; incompatible override input
fails through normal behavior cleanup. No global timeScale or Control lock.

Area instances are maintained/revoked by [registered areas](../../../Area/PNAreaREADME.md).
Admission immunity uses existing leases; bypass follows normal cleanup. Multiple
area instances may overlap and own independent grants. Damage still receives normally.


## Runtime performance contract (RulesVersion68 / Mod API6)

Gameplay authority, damage/tick ordering, three clocks and event-fed four nullable
UI numbers are unchanged. Advance reuses an ordered frame buffer, resolves event
ties explicitly and only refreshes countdown writers each frame; structural
presentation synchronization remains mutation-driven. Empty collections do no work
after validation. No tick dropping, spreading or per-frame gameplay quota is added.

DOT live source lookup validates the exact published presence. Preparing a new
generation hides bare directory lookup but does not invalidate the old published
source/receiver before commit. Commit invalidates the old presence, then DOT uses
its last valid capture; it never silently binds to the replacement object.
Combat copies are bulk-imported and isolated. Plain immutable stat entries can reuse
a private copy; source changes invalidate it. Current-only changes retain that copy
only for built-in formulas which cannot read Current. Custom sets/entries/calculators
keep their fresh-current path, and CaptureBuffCombatSource overrides still run.
See PNStatCalculationREADME and PNBuffCoopREADME for calculation/clock details.

RulesVersion69: stock DOT aggregates immutable value contributions on apply/resume
and OnContributionsChanged. Tick damage still reads current stacks and live source
stats, preserving same-frame changes and fallback attribution. Contributions are
automatically refreshed on grant/revoke and behavior lifecycle; Buff/mod authors
do not manage a cache. Canonical combat tags and private source wrappers may be
reused, without memoizing damage packets, RNG or hit outcomes. Tick schedule and
callback ordering are unchanged.
