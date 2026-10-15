# Optional area/timescale API examples

`CombatMod.cs` now includes `AreaExamples.CreateSlowField` and
`AreaExamples.ApplyTimedSlow`. They are caller helpers only; registering the example
mod does not apply Buffs, spawn regions or change any character.

Use the runtime-owned PNAreaManager in a Ready authoritative session, keep the
returned receipt/handle and inspect Pending versus Created. A created region belongs
to the manager until its own lifetime ends or its handle is ended. LayerMask and
duration/reduction/minimumReduction are supplied by the caller. The standalone Buff
accepts the same reductions with an explicit Game duration and independent instances.

For registering a custom area component/schema or deriving a profile through JSON,
see `Assets/Core/Area/PNAreaREADME.md`. Use the selected registered factory and existing
pool hooks; do not spawn a concrete implementation directly from a Skill.
