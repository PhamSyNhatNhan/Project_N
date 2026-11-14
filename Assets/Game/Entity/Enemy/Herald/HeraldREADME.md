# Herald

A **generic** support enemy: it walks to allies and gives them a **Sidus** Buff. The code is shared; a variant (the **Starherald** is the first) is **data**: a character JSON (stats, skill, Buff package), a visual and a cloned prefab. Design and decisions: `Docs/StarheraldPlan.md` (H1-H14).

## Making a variant

1. Character JSON (`characters/<Name>Character.json`): stats, one skill (Action `project_n:herald_sidus_action`, numbers in `action.perform`), and its Buff package(s) under `extensions.project_n.buffs` (factory `project_n:buff_sidus_template`, tag `project_n:sidus`).
2. Visual (`visual/<name>.json`) exported from the rig tool; it needs a `cast` state.
3. Clone `Starherald.prefab`: change `characterId`, `behaviorId` and the behavior's serialized `skillId`; add the catalog key in `PNEntityPrefabs.asset`.

A variant that only changes numbers (windup, radius, targets, Buff modifiers, duration) touches JSON only.

## What the Starherald does

- **Skill** `project_n:starherald_sidus`, cooldown **12 s**. Movement is locked for 1.8 s (windup 1.0, release 0.25, recovery 0.55; the `cast` motion runs over the same time). A `block_hits` guard (3 hits, 1.5 s) is put on itself (losing it never stops the cast). At the end of the windup it gives `project_n:sidus_star` (15 s) to up to **2** monsters within **4 m**. It never targets itself and has no attack.
- **Buff** `project_n:sidus_star`: health / attack power / defense +20 % (group 1), move speed +0.2 (group 0), `base:magic` +50 %, `base:physical` +10 % (group 1). It lives in `StarheraldCharacter.json`, not in code.
- **Targets** (`PNHeraldTargets.Pick`, pure): no Sidus and a different kind first, then no Sidus, then a different kind with Sidus, then the rest; inside a tier less time left, nearer, lower entity id. A boss is a candidate only when the zone holds at most two candidates (a Sidus Buff on a boss is inert).
- **Skipped targets** (H13): dead, no longer allied or beyond the radius when the windup ends: skipped, cooldown spent.
- **Behavior** (`PNHeraldBehavior`, scan 0.25 s): an ally within `castRange` (3 m) and skill ready: cast (Buff radius stays 4 m). Otherwise an ally within `seek`: walk to it until within `approach` (1.5 m), ready or not. No ally: follow the nearest hostile at `follow` (3 m).
- Stats: health 1200, Lorica 300, defense 5, attack power 50 (unused), move speed 1.2, attack speed 100.

## Where the numbers live

The skill JSON `action.perform` (`windup`, `release`, `recovery`, `radius`, `approach`, `castRange`, `targets`, `seek`, `follow`, `buffId`, `guard`) is the only source; `PNHeraldCombatContent.Validate` requires every field, refuses unknown ones, and checks `approach <= castRange <= radius` and the referenced Buffs. The behavior's own number is the 0.25 s scan.

## Files

- `Script/PNHeraldCombatContent.cs` (Action registration + validator, in `PNGameplayContentModule`), `PNHeraldTargets.cs`, `PNHeraldSidusAction.cs` (Action and `PNHeraldSelection`), `PNHeraldBehavior.cs`, `PNHeraldStat.cs`, `PNHeraldSkill.cs`, `PNHeraldPresentation.cs`.
- `../Starherald/Prefab/Starherald.prefab` (key `project_n:starherald`).
- Visual: `StreamingAssets/BaseContent/visual/starherald.json` + `starherald/*.png` (source `ArtSource/VisualDemo/work/starherald.json`).
- Only in the test arena (group `starherald` in `PNArenaMaps`); no run room uses it yet (H14).

## Checks

Smoke `Tests/EveEngine/PNStarheraldTests.cs` (shipped JSON, validator, `Pick`); native `PNStarheraldChecks`; opt-in Play review `PNArenaPlayReview`. RulesVersion 155.

## Not covered

Coop client view of the cast (no skill networking yet), Android / IL2CPP, Player build, a run room that spawns it.
