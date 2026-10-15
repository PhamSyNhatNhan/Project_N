# Entity control permissions

Implemented control foundation; Buff integration is available in RulesVersion44,
DLL Mod API2. See ../Buff/PNBuffREADME.md. Shield and production stun behavior
remain separate work.

## Registered vocabulary

`control_permissions` contains `PNControlPermissionDefinition`; access through
`hub.ControlPermissions()`. Project_N owns these locked public `base:` aliases:

| Original project_n ID path | Meaning |
| --- | --- |
| move | Ordinary movement and EveMove commands |
| jump | Starting a jump; does not freeze gravity |
| face_control | Ordinary controller/AI facing |
| face_skill | Skill-requested facing |
| skill_all | Every new skill activation, including new categories |
| skill_attack | Normal and repeat attack activation |
| skill_skill | Skill activation |
| skill_dash | Dash activation |
| skill_ultimate | Ultimate activation |
| skill_burst | Burst activation; does not end an existing Burst |
| motion_voluntary | Voluntary motion admission |
| motion_forced | Forced motion admission |

Code modules can register new definitions before seal. IDs and registry snapshots
participate in existing content fingerprints. No enum limits public vocabulary.

## State, leases and lifecycle

`PNControlManager` is an entity load step with order -20. Core Move and Skill use
the generic `EveIEntityLoadPreparation` hook to ensure one manager before the loader
captures its load-step dependency graph. Player skill depends on controls, stats
and passive Ability. Preparations must not load modules or recursively load an entity.
New preparation providers are not recursively discovered during this hook.

The manager owns a fresh `PNControlState` per load. Mutation requires active
offline/host authority. Queries are read-only; this is not network replication.
Unload invalidates every lease. Disabling one consumer does not clear other owners.
Action owners release on completion/cancel/dispose; future Buff owners must manage
their own suspend/resume/end semantics explicitly.

```csharp
var state = skill.Controls;
using (var lease = state.Acquire("my invocation", new[] {
    PNControlIds.SkillAll, PNControlIds.Move
})) {
    // Only this lease is returned by Dispose; other owners remain effective.
}
var exact = state.Acquire("specific skill", skillIds: new[] {
    EveId.Parse("base:eve_skill_entry")
});
exact.Dispose();
```

Owner is a diagnostic label; unique lease identity, not the label, determines
ownership. Requests validate all IDs before committing. Duplicate IDs and aliases
deduplicate. Dispose is idempotent. Snapshot entries are immutable and cannot
release locks. `Changed` reports effective transitions, with observer exceptions
isolated through `ObserverFailed`; nested collection mutations are permitted.

`SetBaseAllowed` changes the state base independently of leases. Move additionally
honors its existing engine base permissions: `BaseCanMove`, `BaseCanFlip`,
`BaseCanJump`. Existing setters update engine bases; getters report effective
permission. Do not save an effective getter and later restore it as a base.
Walking input no longer updates LastMoveDirection while walking is locked.

## Execution paths

Engine Move/Jump/Face execution reads virtual permission getters instead of bypassing
them through private flags. Core hooks compose engine bases with entity controls.
`FaceSkillDirection` uses `TryFaceSkill`; it never temporarily enables CanFlip.
Action movement locks include move/jump/face_control, allowing the skill to choose
facing and run explicit motion. `face_skill` remains independently blockable.
Gravity and entity clock are unchanged by control leases.

`PNLockMovementOperation` owns one lease per invocation. It does not save bools
or change input/menu flags. Nested sequence/motion locks may end in any order.
Walking locks stop ordinary movement; PNMotionAction position updates remain
independent of walking. EveMove scripted commands still use the ordinary move gate.

`PNSkill.CanActivateSkill(skillId)` resolves effective categories and checks skill_all,
ALL mapped permissions and exact
canonical skill ID; `LastControlDecision` exposes blocked rights/lease IDs.
Concrete `Try` entry points call it BEFORE consuming resources or granting effects.
RulesVersion46 removes skill_cast original/base alias; skill_skill replaces it.
The obsolete C# Cast field forwards to Skill only to retain Mod API2 binary shape;
it does not register any old ID. See ../Skill/PNSkillREADME.md for multi-category
definitions, dynamic modifiers and the independent Attack Speed flag.
Factories do not gate every Action creation. Custom skills must follow this contract;
the engine does not intercept arbitrary mod code. Input buttons do not own admission.

Eve activation identities are registered skills without action/tuning payloads:
`base:eve_attack_entry` (including repeat), `base:eve_skill_entry`,
`base:eve_ultimate_entry`, `base:eve_burst_entry`. Dash uses `base:eve_dash`;
shared players use `base:player_dash`. Internal action profiles retain separate IDs.
Exact entry gates do not implicitly gate every profile of an already-running cast.

Motion perform payload optionally declares `permissionId`. Legacy omission means
`project_n:motion_voluntary`; authored built-ins declare `base:motion_voluntary`.
The parser, selected-content validator and pack reference validator resolve it.
Changing classification requires selected registered content, not a per-cast bypass.
Motion checks admission before acquiring locks or changing Rigidbody interpolation.
Eve Try paths also preflight voluntary motion before resource commits where needed.

## Interruption and input

Locks deny NEW activations; they do not implicitly pause/cancel running actions or
end Burst. `TryInterrupt(reasonId)` is an explicit extensibility hook, default false.
Current shared Dash cancels its invocation; Eve cancels its sequence through existing
cleanup. Reason must be a valid ID, but is not yet an immunity or anti-interrupt policy.
No stun, anti-interruption phase rules or forced-motion skill is added here.

MenuInputBlocked and base CanInput retain existing behavior. Lease gates do not
reset InputActions, disable Mouse, change the pause UI or accumulate failed casts.
Repeat checks admission on each new activation. Input interaction release/cancel
continues through existing callbacks.

## Verification

Standalone: `PNControlTests` in EveRunSmokeTests. Native Edit Mode:
`PNControlEditorChecks.Run`, `PNSharedDashEditorChecks.Run`. Native Ready Play Mode:
`PNControlPlayReview.Run` with normal Input System/physics player-loop execution.
Helpers use disposable fixtures, preserve authored scenes, restore settings/devices
and write evidence under `.utmp/ControlReview`. Native Editor checks do not establish
Play input correctness; the separate Play review covers held movement and disable.
