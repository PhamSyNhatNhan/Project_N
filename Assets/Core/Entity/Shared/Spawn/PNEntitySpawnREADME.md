# Entity spawning / Sinh entity (RulesVersion 100)

EN: Everything that is spawned as a character-like thing (player, enemy, NPC, summon, prop) goes through one path. The **character** says what the
thing is (stats, skills, enemy type, layers, prefab key, Health or not, damage mode, loot id). The **entity** (kind `entity` of the `instances`
registry) says what role it plays when spawned (how it is built and owned, whether it is saved, how long it lives). One spawn =
(entity, character, position, options). Design record and decisions: `Docs/EntitySpawnPlan.md`.

VI: Mọi thứ sinh ra như một nhân vật (người chơi, quái, NPC, triệu hồi, vật) đi qua một đường. **Nhân vật** là "thứ đó là gì"; **entity**
(kind `entity` của registry `instances`) là "vai trò khi sinh". Một lần sinh = (entity, nhân vật, vị trí, tuỳ chọn).

## Parts

| Type | Where | Role |
|---|---|---|
| `PNEntityDefinition` | Core | Kind `entity`. `Settings` (`PNEntitySettings`) from the payload; `ImplementationType` is a recipe class (`IPNEntityRecipe`), not a component; `Recipe` is its one shared instance and `IsPlayer` comes from it. |
| `PNEntitySettings` | Core | Payload `data`. Required: `owner` (`peer`/`host`), `persist`, `followsOwner`, `lifetime` (seconds > 0 or `null`), `countsForClear`, `dropsLoot`. Optional: `prefab` (catalog key overriding the character's), `spawnBuffs` (up to 16 Buff ids). Unknown fields are rejected. |
| `PNBodySettings` | Core | The `body` block of a character: `prefab`, `damageMode` (`normal`/`hits`), `healthBar` (`auto`/`hidden`), `loot` (id; no consumer yet). `Read(character)` also checks the rules that need stats. |
| `PNEntityRecipe`, `PNPlayerEntityRecipe`, `PNHostEntityRecipe` | Game | What the factory runs while the object is inactive (`Configure`: input owners, ground binding) and after publication (`Published`: local-player binding). |
| `PNEntityGameplayContent` | Game | The built-in roles: `project_n:player` (alias `base:scene_spawn`, the old template name), `enemy`, `npc`, `summon`, `prop` (aliases `base:<name>`). |
| `PNEntityFactory` | Game | `Get` / `Configure` / `Published` / `Return`, pools per (world, prefab), spawn point cache per (world, slot), `ResolvePrefab`, `Scheduler`. |
| `PNEntitySpawner` | Core | The one way to start a spawn: `Enqueue(scheduler, factory, request, configure, published, failed, ...)`; `AssignNew` gives a host/offline runtime entity its id and character. |
| `PNEntityPrefabCatalog`, `PNEntityPrefabs` | Game | Prefab by namespaced key: the asset `Resources/PNEntities/PNEntityPrefabs` plus what code mods register. |
| `PNEntityPrefabReferences`, `IPNEntityPrefabSource` | Core | Keys content refers to, existence check at content load, one coop hash entry per key. |
| `PNEntityRole`, `PNEntityLifetime` | Core | Components set by the factory: the role flags on the object; removal after N seconds of entity time. |

## Roles and flags

| Flag | Effect today |
|---|---|
| recipe `IsPlayer` | The one place that says a role is a player (`PNPlayerEntityRecipe`): placed at a spawn point, gets input and ground binding, local-player binding on its machine, treated as a player by the coop layer (`PNEntityRoles.IsPlayer`). A recipe keeps no state; the definition is registered with a factory (`new PNEntityDefinition(id, typeof(MyRecipe), () => new MyRecipe(), data)`), never created by reflection (IL2CPP stripping). |
| `owner` | `peer` / `host` = who controls the entity. **No effect yet**, and independent of `IsPlayer` (a later peer-controlled pet would be `peer` without being a player). Control today is the identity's `OwnerId` set at spawn. |
| `persist` | `false` = left out of a world checkpoint (summon). |
| `lifetime` / request `Lifetime` | Removed through `scheduler.RetirePublished` after that many seconds of entity time (authority only). |
| `spawnBuffs` | Applied by the authority right after publication (the Buff manager resolves them on its next advance). |
| `prefab` | Overrides the character's prefab key. |
| `followsOwner`, `countsForClear`, `dropsLoot`, `loot` | **No effect yet.** Kept on `PNEntityRole` for summons, floor clear and loot, which do not exist. |

## Prefab resolution

1. The entity's `prefab`, else the character's `body.prefab`, else `playable.prefab` (the older name; both may be set only if they name the same prefab),
   else the spawn point's own prefab (only a request that has a spawn point), else an error. A key that is not in the catalog fails loudly.
2. Keys are `namespace:name`; a key without a namespace (`eve_2`) is read as `project_n:eve_2`.
3. Content load fails when a referenced key is missing. The coop hash carries `entity_prefab:<key>` = a fingerprint of the prefab's **gameplay
   structure** (child paths, component types, layers, collider shapes, Rigidbody settings), not its looks; mobile skips it (no mods, no crossplay).
4. A code mod registers a prefab in its own namespace with `PNEntityPrefabs.Register("mymod:golem", prefab, "mymod")` from its `Register`; duplicates
   and other namespaces are rejected; a new content build clears the previous build's mod prefabs.

## Health and damage mode

- Damageable <=> the character declares a `project_n:health` stat (`PNStat.HasHealth`). Without it: `IsDead` is false, `ReceiveDamage` returns a result with
  `Ignored == true` (no iframe, popup or event), `ReceiveHeal` returns `PNHealStatus.NoHealth`, the Autotarget rule skips it (the enemy Aggro rule still sees it).
- `damageMode: "hits"`: N = the declared maximum Health (an integer >= 1; no Lorica). A damaging hit outside iframe removes exactly 1 whatever its amount,
  DEF or critical; a hit inside iframe removes 0; damage over time and area ticks count; healing returns `Blocked`.
- `healthBar: "hidden"` hides the world health bar.

## Spawning an entity at runtime (host or offline)

```csharp
var request = new PNEntityRequest { Entity = "project_n:enemy", Character = "project_n:dummy", World = world, ContentSlot = slot, Position = p, SceneKey = "...", EntityKey = "" };
PNEntitySpawner.Enqueue(scheduler, factory, request, loader => PNEntitySpawner.AssignNew(session, loader, request), failed: Debug.LogException);
```
A prefab that does not fix its character (an enemy prefab authored in a scene) needs `AssignNew`; a player prefab has its character built in. An enemy
with skills and movement also needs the world's prepared navigation. Players keep using `factory.Player(owner, world, slot, spawn)` for the request.

## Save and wire

Checkpoints and the coop actor metadata still carry the role id in the field named `template`/`Template` (`PNEntityRequest.Entity` and `.Template` are
kept equal); `base:scene_spawn` is an alias of `project_n:player`, so old checkpoints import. The old `Player` flag is gone from the request and the metadata:
it is derived from the role's recipe (`PNEntityRoles.IsPlayer`).

## Not done

Summon (`PNSummon`, owner relation), real enemies spawned at runtime from the roguelite loop, loot, floor clear, the work/tool system (DST/Terraria style),
Settings option for health numbers on the bar, coop with a mod prefab on two machines.
