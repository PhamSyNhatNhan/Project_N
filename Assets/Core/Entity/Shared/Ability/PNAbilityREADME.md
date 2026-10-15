# Entity Abilities

Current lifecycle (RulesVersion67 / Mod API6): initial passive definitions/CDR setup
may run in the loader's initialization scope. Manager.IsReady/Advance and cached
collection mutation require publication/authority afterward. A Prepared actor never
ticks or creates effects; invalidated context blocks stale handles before deferred
Unload. Actor replication currently carries Buff presentation, not Ability state.

Abilities are passive entity-owned counters, timers and flags. They never execute
an action, spawn a GameObject, change stats/time scale, deal damage or implement
combo/input logic. Skills read/mutate these states and implement every gameplay
effect. Buff implementation is separate work. Presentation reads immutable
`PNAbilityDisplay` snapshots and never advances gameplay timers.

## Ownership and configuration

Add `PNAbilityManager` to an entity loaded through `EveEntityLoader`. New `PNSkill`
components require the manager and expose the same collection through `Abilities`.
Existing scene objects should be authored with the manager explicitly; this change
does not edit/save gameplay scenes or assign a cooldown to Eve's slash.

Configure Inspector `Initial Abilities`, an optional standalone JSON TextAsset,
or call `Configure` / `ConfigureJson` before loading. Code configuration replaces
Inspector/asset inputs. JSON asset entries extend Inspector entries; duplicate IDs
fail loading. Definitions are validated immutable copies, independent of the
skill/action envelope. `Define` adds a passive runtime definition after loading.

Selected character skill envelopes may additionally carry a skill.abilities JSON
array. The manager reads it from selected content before loading Skill, independently
of whether a skill component exists. These are entity definitions in a content
container; they do not execute skill behavior. Inspector character-ID fallbacks from
EveSkill/EveStat support older scene entities. These entries extend manager inputs;
duplicate IDs fail instead of silently overriding JSON. Eve's seven definitions are
authored this way in EveCharacter.json; its skill only reads and validates them.

There is no global Ability registry requirement: valid `EveId`s name states within
each entity collection. Two entities have independent values for the same ID.
`PNAbilityHandle` identifies a particular instance and collection. Use handles for
targeted mutation/removal; ID queries for State return a list and never choose an
arbitrary instance. Removal, transient retirement and unload invalidate handles
and prevent stale runtime references from mutating state.

Every JSON definition requires boolean `showInUI`; false hides the ID from the
world panel. Optional `iconPath` is a Resources-relative Sprite path without
extension (empty means placeholder); optional integer `displayOrder` defaults to
zero with ID tie-break. Inspector/code `PNAbilitySettings` expose these fields too.
These metadata fields do not give Ability any presentation behavior. See
`Assets/Game/UI/PNUIREADME.md` for rendering and ownership.

```json
[
  {
    "id": "project_n:step_charges", "kind": "Cooldown", "showInUI": true,
    "maximum": 3, "initialAmount": 3, "cooldown": 10,
    "recovery": "PerStack", "autoRecover": true, "clock": "Game"
  },
  {
    "id": "project_n:empowered_stacks", "kind": "Counter", "showInUI": true,
    "maximum": 3, "initialAmount": 0, "duration": 10,
    "lifetime": "PerGrant", "clock": "EntityWithoutMinimum"
  },
  {
    "id": "project_n:ultimate_state", "kind": "State", "showInUI": true,
    "duration": 5, "cooldown": 10, "cooldownStart": "OnEnd",
    "reapply": "Independent", "clock": "Game"
  }
]
```

Fields/enums are strict and case-sensitive. JSON requires `id` and `kind`; unknown
or duplicate fields/IDs and fields from other kinds fail validation. State has no
quantity; Counter has no automatic cooldown; Cooldown has no duration. Omitted
duration is permanent, explicit duration must be positive/finite. Cooldown zero
recovers immediately. Quantity is integral, maximum positive, initial amount
explicit (default zero) and bounded. Runtime times use double.

## Cooldown

`TryConsume()` costs exactly one charge. `PerStack` starts recovery when below
maximum, recovers one per period and keeps progress when consumed again.
`AllAtOnce` starts only at zero and restores full capacity. Large elapsed steps
recover all crossed periods and preserve surplus. Zero cooldown restores without
waiting for a frame. Missing initial charges follow the same eligibility rules.

`AddCharges` returns the actual accepted quantity; a partial grant keeps running
progress, reaching full stops/clears the timer. `SetMaximum` clamps quantity and
stops recovery when full. `SetAutoRecovery(false)` stops/clears recovery; enabling
starts a new eligible cycle. No automatic recovery means gameplay supplies charges.

`SetCooldown` and `SetReduction(flat, percent)` affect NEXT periods, never rewrite
the running period. Actual cooldown = max(0, base*(1-min(100,percent)/100)-flat), with
validated nonnegative flat and finite signed percent. Negative percent lengthens
cooldown; 100+ produces zero. Overflow is rejected before changing parameters.
`RestartRecovery` explicitly restarts
using the current parameters. State also supports `SetReduction` and `ActualCooldown`;
`RestartCooldown` explicitly replaces its current cooldown without changing duration.
`ResetToFull` restores current capacity; `Reset`
restores definition quantity/capacity/parameters and notifies observers.

`Cooldown.ReduceRemaining(seconds)` is distinct from parameter reduction: it advances
the CURRENT recovery by a finite nonnegative number of seconds, completing it when
appropriate. PerStack can retain surplus into further periods. It does not change
the configured cooldown or reduction and does nothing when no recovery is active.
Skill owns any gameplay trigger (e.g. Eve Attack hit -1s, Skill activation -2s Ulti).

## Counter

`Add(amount, optionalDuration)` returns the accepted clamped quantity; excess is
discarded and cannot produce phantom expiry groups. `TryConsume(amount)` permits
positive arbitrary costs (one by default). It never automatically refills.

`Shared`: first positive quantity starts one duration, and later additions preserve
that deadline. An optional duration passed with a later shared addition does not
rewrite the existing deadline. `PerGrant`: every accepted addition creates a group
with its own duration; consumption takes earliest expiry first, permanent groups
last, and ties follow grant order. All groups/snapshots exposed to callers are
immutable copies. Group IDs are not reused after reset.

Addition/consumption never refresh existing deadlines, even at full quantity.
`RefreshDuration(seconds, optionalGrantId)` explicitly refreshes all/current group
deadlines. Consuming the last quantity removes its timer. Expiry removes ONLY the
remaining quantity and reports `Expired`, never `Used`. `SetAmount`, `SetMaximum`,
`Clear` and `Reset` have distinct reconfiguration/reset notifications.

```csharp
var stacks = skill.Abilities.GetCounter(EveId.Parse("project_n:empowered_stacks"));
stacks.Add(3); // three stacks lasting ten seconds, no automatic refill
if (stacks.TryConsume())
{
    // The SKILL chooses how to empower its attack. The Ability has no effect hook.
}
```

## State

`IsActive` is a flag, not casting readiness. `CanActivate` requires inactive AND
cooldown complete. `TryActivate`, `End`, `TryConsume`, `RefreshDuration` and
`ExtendDuration` have explicit separate meanings. Consumption requires active and
cost one; it ends the flag. Initial active states initialize their timer correctly.
Optional cooldown can start on activation or on end (expiry, consumption or early
explicit end). Completing cooldown never activates a flag. A still-active duration
prevents activation even if cooldown already finished. Same-tick duration-to-cooldown
surplus is preserved. Parameters change next cycles; `RestartCooldown` is explicit.

`GrantState` applies the configured reapply policy:

- `Keep`: leave the active instance unchanged.
- `Refresh`: replace remaining duration with the supplied/default duration.
- `Extend`: add the supplied/default positive duration to remaining timed duration.
- `Independent`: create an additional active instance with its own handle/timers.

Refresh/Extend require a meaningful timed duration and do not restart cooldown.
An inactive named instance must complete cooldown before another activation.
The first named instance remains reusable when inactive. Independent additional
instances retire automatically once inactive AND cooldown complete; handle is
invalidated. Untimed instances require explicit End/Remove. `CreateInstance`
explicitly creates a retained State instance; use a handle if several coexist.

## Clocks, lifecycle and notifications

Every definition selects one source (default `Game`):

- `Game`: `Time.deltaTime`, including global game pause, ignoring entity scale.
- `Entity`: game delta times final `EveTimeScale.Scale`, including its minimum.
- `EntityWithoutMinimum`: game delta times max(0, `RawScale`), ignoring entity minimum.

Thus player minimum 1/raw scale 0 leaves Game/Entity timers running while the raw
clock stops. Missing/disabled clock uses scale 1. Modifier expiry itself stays owned
by `EveTimeScale`; Ability never creates/removes a time modifier. Manager advances
independently of skill/action Update. Inputs/products must be finite, elapsed time
nonnegative. A zero local delta preserves state.

Disable skill/input: keep ticking manager. Disable whole entity (or manager): retain
state and pause; reenabling resumes with no catchup. Death: no automatic HP/death
subscription; entity/skill logic chooses retention, reset (`ResetAll`) or removal
(`Remove`/`ClearInstances`). Reset operates on retained instances; after explicit
removal, recreate a known definition with `CreateInstance` if needed. Unload/destroy:
dispose runtime and observers. A new load restores definitions and a new handle
scope. Session end makes manager inaccessible until unload/reload.

`Changed` is post-commit notification only. Immutable before/after snapshots,
reason and actual `Quantity` distinguish Used/Added/Recovered/Expired/Reset/etc.
Creation/removal are explicit notifications; created instances are attached and
queryable before observers run. `Before` is null on creation; `After` is null on removal.
For zero cooldown `Used.Quantity=1` even if immediate recovery leaves net quantity
unchanged. No cancellation/before-effect hooks. Time/group updates finish before
dispatch; observer-driven new states start after that update and do not retroactively
consume its elapsed time. Observer failures are isolated (manager logs them).
Recursive clock advancement from observers is rejected.

`TryConsume(IEnumerable<PNAbilityCost>)` validates distinct handles and all costs
before committing any consumption, then publishes notifications. Skill chooses the
commit point and cancellation policy. This helper neither creates nor performs actions.

## Verification

`Tests/EveEngine/PNAbilityTests.cs` exercises actual Core runtime with standalone
Unity test substitutes. Run `Tests/EveEngine/EveRunSmokeTests.ps1` for regression.
`PNAbilityEditorChecks.Run()` verifies real Unity component/load/disable/reload and
clock behavior in a disposable preview scene outside Play Mode. It preserves the
active scene and never saves/reloads it. UI rendering and a player build are outside
these checks. Executable gameplay RulesVersion is `project_n:23`; Mod API stays 2
(additive API, no existing signatures replaced).
