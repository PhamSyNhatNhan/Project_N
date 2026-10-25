# Summon (RulesVersion 101)

EN: A summon is an ordinary **entity** (role `project_n:summon`, `persist=false`) with the light component `PNSummon` added while it is configured. The component
keeps *who summoned it*, the owner's combat source captured once at that moment, a limit group and the creation order. It holds no behavior and no stats: what a
summon IS and DOES is its own character and components (a plain `PNStat`, or `PNEnemyStat` for a boss minion; never `PNPlayerStat`). Design record and decisions:
`Docs/SummonPlan.md`.

VI: Summon là một **entity** thường (role `project_n:summon`) có thêm component nhẹ `PNSummon`: ai triệu hồi, nguồn chiến đấu của chủ chụp một lần, nhóm giới hạn, thứ tự tạo.
Không có hành vi hay stat; summon là gì và làm gì do character và component riêng của nó quyết định.

## Parts

| Type | Where | Role |
|---|---|---|
| `PNSummon`, `IPNSummonOwnerGone`, `PNSummonOwnerGone` | Core | The component. `Begin` (while inactive) records owner, source, group, order and multiplies the local scale by `summon_scale`; `Clear` forgets them and gives the scale back (the factory calls it on return, `OnDisable` and `OnDestroy` too). Only the machine with authority watches the owner (`Tick`/`Update`): owner unloaded, dead or in another world means *retire*, unless a behavior on the object implements `IPNSummonOwnerGone` and returns true (asked once per reason). `Retire()` removes it through the session's host. |
| `PNSummons` | Core | Index of live summons by summoner and group, plus spawns the host accepted but that have not reached configure (so two casts in one frame cannot both pass a limit). `Count`, `Oldest`, `Of`, `All`. A summon being removed no longer counts. |
| `IPNEntityHost`, `PNEntityHosts` | Core | Who spawns and removes runtime entities for the machine with authority: `TrySpawn(request, configure, published, failed)` (null when the preparation queue is full) and `Retire(loader)`. Offline: `PNLocalEntityHost` (Game, registered by `PNGameSession`); coop host: `CoopEntityHost` (registered by `PNRoomPlay` over it); a client has none. |
| `PNEntityRequest.Summoner` | Core | Host-side only (not on the wire). |
| `PNCreateSummonOperation`, `PNSummonAction` | Game | Operation `project_n:create_summon` and action `project_n:summon_action` (payload `{operationId, data}` like the area and projectile actions). |
| `PNSummonCastBehavior` | Game | Skill binding behavior `project_n:summon_cast` for every "call a summon" skill: cooldown Ability check, `TryBegin`, run the action, spend the cooldown only if a summon was actually started, `Commit`, `Complete`. |
| `PNPetSettings`, `PNPetShooterBehavior` | Game | The `pet` block of a character and the hover-and-shoot behavior (Eve 2's pet). |

## create_summon payload

`character` (required), `entity` (default `project_n:summon`; a player role is rejected), `lifetime` (seconds > 0, or null/absent = until removed; **the summoning
skill decides it**, there is no summon_lifetime modifier), `group` (1..64 chars), `limit` (1..64, needs `group`), `whenFull` (`replaceOldest` default | `reject`,
needs `limit`), `offset` ([x,y,z] meters from the caster, world space). A limit belongs to the skill, not to summons in general: none declared = unlimited.
Status of a cast (`PNSummonStatus`): `Spawning`, `Rejected` (group full and `reject`), `QueueFull`, `NoAuthority` (a client).

## Modifiers of the owner

`summon_damage` and `summon_scale` are ordinary tags read from the owner's source snapshot (Scale route over 1, like `projectile_*`). `summon_scale` is applied by `PNSummon.Begin`;
`summon_damage` is read by the implementation that deals damage (the pet adds it, with `projectile_damage`, to its shot). A summon that shoots uses the owner's captured
source, so the owner's whole modifier set counts, **once, at summon time**; later changes to the owner do not reach it.

## Aggro

`body.aggro: "ignored"` on the summon's character makes the Aggro rule (`PNTargetSelector.Select`, `PNEnemyMeleeBehavior`, `PNAgentContext.CanTarget`) never choose it
(`PNTargetSelector.IsAggroEligible`). The aggro stat is still a plain weight, so a no-Health taunt stone keeps drawing enemies. The pet also has no collider and no hit
volume, so projectiles pass through it.

## Eve 2's pet (placeholder content)

Skill slot, 8 s cooldown (`project_n:eve2_pet_ready`, shown in the Ability UI), character `project_n:eve2_pet` (no Health, `aggro: ignored`, `healthBar: hidden`,
prefab `project_n:eve2_pet` with `ghost.png`), lives 7 s, group `eve2_pet` limit 1 (a new one replaces the old). It hovers beside its owner at (±0.9, 0.8, -0.8) (offset X is a distance: the pet keeps the side it is on and changes it only when more than 0.3 m past the other; the sprite is 40% of the first size, 0.17 x 0.20 m), moved by a Rigidbody velocity (no collider, no gravity, interpolated; `Drive` runs in `FixedUpdate`, zero while aiming; no `transform.position` writes); every 1.5 s of
entity time it stands still 0.3 s and fires `project_n:eve2_pet_shot` (Eve's homing shot at 75% speed and size) at a target within 1.5 m of itself: 10 magic damage
(`pet` block of `Eve2PetCharacter.json`). The binding is registered by `PNSummonGameplayContent.RegisterBindings`, the skill data is in `Eve2Character.json`.

## Coop

The coop host enrolls a summon like any host entity (replication shell with the metadata's `Template` = the role), so clients see it; removing it goes through the binding,
so the shell is destroyed on every client. Clients never run `PNSummon` or the pet behavior (no authority). The pet's movement and shots are **not** replicated yet (a client
sees the pet where it was created); Skill/projectile/summon networking is a later phase. A summon commanded by a client needs Skill networking.

## Not done

Summons that follow their owner across worlds (they are retired instead), `followsOwner`, a pet position/shot replication, summons commanded by a client, loot/clear flags for summons.
