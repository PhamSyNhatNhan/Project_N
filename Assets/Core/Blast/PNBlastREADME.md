# Blast

A **Blast** is an instant attack at a point that waits a delay on its own clock, detonates once, and ends. It is the fourth kind of attack
(Projectile, Area, Strike, Blast, Summon; see `Docs/StrikeBlastPlan.md` section 5). The Starweaver constellation (a triangle) and the
Starreaper rain (a chain of cylinders) are Blasts.

## Pieces

| Piece | Where | Role |
|---|---|---|
| `PNBlastDefinition` | `Core/Blast` | registry kind `blast` (an `PNInstanceDefinition`): an id, one implementation and a validated input schema; `Derive` for overrides; builds the pool template (implementation + `PNBlastClock`) |
| `PNBlastInstance` | `Core/Blast/PNBlastManager.cs` | pooled component with the shared lifecycle hooks (`OnStart`, `OnPerform(gameDelta)` already scaled by the blast clock, `OnEnd`, `EndingDuration`/`OnEnding`, `OnCancel`, `Cleanup`) and the `Strike` door; reads `blast_scale` once (`ScaleFactor`), builds the combat capture (`CreateCapture`), checks the Owner dependency |
| `PNBlastManager` | `Core/Blast` | one per session on the runtime (same lifecycle as `PNAreaManager`): create/end commands, pools per (definition, scene), world leases, `ActorWorldChanging`, `ReleaseWorld`, `Shutdown`, scene unload |
| `PNBlastClock` | `Core/Area/PNTimeTargets.cs` | an `EveTimeScale` (minScale 0) driven by the manager |
| `PNTimedBlast` / `PNCylinderBlast` / `PNPolygonBlast` | `Game/Blast` | the game's behaviour: delay, one detonation, tail, hits; shapes |
| `PNBlastSchema`, `PNBlastGeometry` | `Game/Blast` | input schema and the pure planar shape tests (standalone-testable) |
| `PNCreateBlastOperation` | `Game/Skill/Operations` | registered `project_n:create_blast` (`blastId`, `dependency`, `input`); like `create_projectile` / `create_area` |

## Timeline and clock

A blast registers in the world's `PNTimeTargets` on the layer **Blast (18)**, so a time effect can slow or stop it. `delay` counts the blast's **own** clock:
the telegraph (drawn from `OnPhase(time, delay)`) and the detonation move together. At the detonation `PNTimedBlast` strikes once and finishes (`Expired`); the `tail` seconds
are its **Ending** (visual only, on the same clock). Its one `Strike` hurts every living target of the creator's TargetLayers, in the same world, whose footprint meets the shape
once (prepared once at creation; each hit rolls its own critical).

## Lifecycle and the Strike door (IL2, RulesVersion 178, `Docs/InstanceLifecyclePlan.md`)

Same state machine as the Area (`PNInstanceLifecycle`): `Start -> Perform* -> End -> Ending* -> Cleanup`, or `Cancel -> Cleanup`; Cleanup runs exactly once, last.
`Requested`, `Expired` and `OwnerUnavailable` are END reasons (`OnEnd`, then an Ending of `EndingDuration` seconds); `Disabled`, `SceneUnloaded`, `SessionEnded` and `RuntimeError`
are CANCEL reasons (`OnCancel`, no Ending). In the Ending the blast is **not running**: its handle is not alive, a late joiner does not get it, only a CANCEL reason is taken.
The sync End record is sent when the Ending starts, with the reason that really took effect. There is no retirement any more: the old `RetirementDuration` is the Ending.

`Strike(in PNBlastStrike)` is how a blast deals damage; the author decides **when** (any number of times, also in `OnEnd` as an aftershock), the engine decides **how** by the role of the machine:

- Authority: tests every living target of the creator's layers in this world against the shape and deals the prepared damage; opens a claim ticket (one per strike) for the players whose own machine decides.
- Copy (view-only, other machine): tests only the body of this machine's player and claims that strike once, named by the strike index (`PNHitReport.TakenFromObject(Blast, id, strike)`; hit name `o<id>:s<index>:<victim>`).
  A copy built late (`Seeking`) counts the strikes that already passed but does not claim them. A copy told "ended" before its own clock struck makes the catch-up strike in `OnEnd` (`PNTimedBlast`, reason `Expired`).
- Guess (the shooter's own drawing): nothing but the author's own drawing.

A target is hit once per strike; the next strike may hit it again. A strike after a cancel does nothing. Claims are bounded: strike index 0..63 (`PNHurtClaimLimits.MaxHit`), later strikes are not claimable.

## Dependency (`PNBlastDependency`)

- `Independent`: lives until its detonation and tail are over.
- `Owner`: also ends (`OwnerUnavailable`, no detonation, no tail) when the owner dies, is disabled, or leaves the world (the manager checks this every tick while running and on `ActorWorldChanging`).
- `Action`: the creating Action holds `PNCreateBlastOperation.Result.Handle` and ends it on cancel. A finished cast lets the blast run out its tail.

## Scale and damage tags (D7/D8)

The creator's snapshot is read **once** at creation: `blast_scale` (Scale route over 1) scales `radius` and `height` (and anything a subclass draws from them);
`blast_damage` joins the damage tags (`PNHitCapture`, built with `CreateCapture`). Delay, tail, sides and rotation do not scale.

## Shapes

- `PNCylinderBlast`: input `delay, tail, radius, height, value, canCrit, tags`; a disc on XZ against the target's body bounds plus a vertical range of `height` above and below the centre.
- `PNPolygonBlast`: the same plus `sides` (3..16) and `rotation` (degrees; the first vertex is at +z). `sides = 3` is the Starweaver triangle.

## Network note

Blasts are created by the authority; other machines build view-only copies from the Spawn record (plan O5) and a player's own machine claims his hits per strike (plan 4.4c). Nothing here is written by the author of a blast: it only calls `Strike`.

## Not done here

An Action that creates a Blast (a `blast_action` like `area_action`) is not registered; the Operation is enough for the two attacks above.
