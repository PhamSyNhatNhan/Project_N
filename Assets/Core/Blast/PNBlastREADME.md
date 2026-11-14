# Blast

A **Blast** is an instant attack at a point that waits a delay on its own clock, detonates once, and ends. It is the fourth kind of attack
(Projectile, Area, Strike, Blast, Summon; see `Docs/StrikeBlastPlan.md` section 5). The Starweaver constellation (a triangle) and the
Starreaper rain (a chain of cylinders) are Blasts.

## Pieces

| Piece | Where | Role |
|---|---|---|
| `PNBlastDefinition` | `Core/Blast` | registry kind `blast` (an `PNInstanceDefinition`): an id, one implementation and a validated input schema; `Derive` for overrides; builds the pool template (implementation + `PNBlastClock`) |
| `PNBlastInstance` | `Core/Blast/PNBlastManager.cs` | pooled component: `Initialize`, `Advance(gameDelta)` (already scaled by the blast clock), `Cleanup`; reads `blast_scale` once (`ScaleFactor`), builds the combat capture (`CreateCapture`), checks the Owner dependency |
| `PNBlastManager` | `Core/Blast` | one per session on the runtime (same lifecycle as `PNAreaManager`): create/end commands, pools per (definition, scene), world leases, `ActorWorldChanging`, `ReleaseWorld`, `Shutdown`, scene unload |
| `PNBlastClock` | `Core/Area/PNTimeTargets.cs` | an `EveTimeScale` (minScale 0) driven by the manager |
| `PNTimedBlast` / `PNCylinderBlast` / `PNPolygonBlast` | `Game/Blast` | the game's behaviour: delay, one detonation, tail, hits; shapes |
| `PNBlastSchema`, `PNBlastGeometry` | `Game/Blast` | input schema and the pure planar shape tests (standalone-testable) |
| `PNCreateBlastOperation` | `Game/Skill/Operations` | registered `project_n:create_blast` (`blastId`, `dependency`, `input`); like `create_projectile` / `create_area` |

## Timeline and clock

A blast registers in the world's `PNTimeTargets` on the layer **Blast (18)**, so a time effect can slow or stop it. `delay` counts the blast's **own** clock:
the telegraph (drawn from `OnPhase(time, delay)`) and the detonation move together. After the detonation it holds `tail` seconds (visual), then ends.
It detonates **exactly once**; every living target of the creator's TargetLayers, in the same world, whose footprint meets the shape takes the prepared damage once
(prepared once at creation; each hit rolls its own critical).

## Dependency (`PNBlastDependency`)

- `Independent`: lives until its detonation and tail are over.
- `Owner`: also ends (`OwnerUnavailable`, no detonation) when the owner dies, is disabled, or leaves the world (the manager checks this every tick and on `ActorWorldChanging`).
- `Action`: the creating Action holds `PNCreateBlastOperation.Result.Handle` and ends it on cancel. A finished cast lets the blast run out its tail.

## Scale and damage tags (D7/D8)

The creator's snapshot is read **once** at creation: `blast_scale` (Scale route over 1) scales `radius` and `height` (and anything a subclass draws from them);
`blast_damage` joins the damage tags (`PNHitCapture`, built with `CreateCapture`). Delay, tail, sides and rotation do not scale.

## Shapes

- `PNCylinderBlast`: input `delay, tail, radius, height, value, canCrit, tags`; a disc on XZ against the target's body bounds plus a vertical range of `height` above and below the centre.
- `PNPolygonBlast`: the same plus `sides` (3..16) and `rotation` (degrees; the first vertex is at +z). `sides = 3` is the Starweaver triangle.

## Network note

Like Areas, blasts are created by the authority only; a client does not see the telegraph or the explosion of a Blast (same gap as every enemy cast, B1/B4 of `Docs/SkillNetworkingGaps.md`).

## Not done here

An Action that creates a Blast (a `blast_action` like `area_action`) is not registered; the Operation is enough for the two attacks above.
