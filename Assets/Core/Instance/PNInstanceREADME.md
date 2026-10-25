# PNInstanceDefinition: definitions of things spawned into a world / Định nghĩa thứ sinh vào world

EN: `PNInstanceDefinition` is the shared immutable definition of something spawned into a world. Today the kinds are **area**
(`PNAreaDefinition`, managed by `PNAreaManager`), **projectile** (`PNProjectileDefinition`, managed by `PNProjectileManager`) and **entity**
(`PNEntityDefinition`, the role of a spawned character: player, enemy, NPC, summon, prop; see `Core/Entity/Shared/Spawn/PNEntitySpawnREADME.md`); Summon, items and
kinds added by mods are further subclasses. All kinds live in **one registry named `instances`** (RulesVersion 97). The kind is the subclass; there is no
separate kind registry.

VI: `PNInstanceDefinition` là định nghĩa bất biến dùng chung của thứ sinh vào world. Hiện có kind **area** và **projectile**; Summon, item và kind do mod
thêm là các lớp con khác. Mọi kind nằm trong **một registry tên `instances`**. Kind là lớp con, không có registry kind riêng.

## What it is not

- Not a spawn system: no spawn rules, random tables, waves, drops or `Spawn(id)` gateway. Whatever uses a definition (a skill, a chest, mod code, a
  terrain-generation definition) owns its rules. Each kind's manager keeps its own request type, pools and lifetime.
- Not Buff, the world-level provider registries, Action/Operation/Calculation, the small vocabulary registries or mobile style registries: those stay separate.
- A definition is not a spawned entity. Area and projectile are built by their managers from a pool template; kind `entity` is built by `PNEntityFactory`
  from a prefab in the entity prefab catalog, so it has no pool template (`HasPoolTemplate` is false and `CreatePoolTemplate()` throws).

## Parts

| Type | Role |
|---|---|
| `PNInstanceDefinition` | Abstract base: `Id`, `Kind`, `ImplementationType` (a component for area/projectile, a recipe class for entity), `Payload`, validators, `EntryFields`, `Derive(id, data, entry)`; under Unity `HasPoolTemplate` and `CreatePoolTemplate()` (optional: kinds not built from one component keep the default). |
| `PNEntityDefinition` | Kind `entity` (RulesVersion 100): `Settings` from the payload (owner, persist, followsOwner, lifetime, countsForClear, dropsLoot, prefab, spawnBuffs). |
| `PNAreaDefinition` / `PNProjectileDefinition` | Kinds `area` / `projectile`. The projectile adds `Destroyable` (JSON entry field `destroyable`) and `Settings<T>()`. |
| `PNInstances` | `Create(hub)` (once, in `PNGameplayContentModule`), `hub.Instances()`, kind-checked `GetKind<T>` / `GetEffectiveKind<T>` / `TryGetKind<T>`, `RegisterBase<T>` (override must be the same kind). |

Registry family (EveEngine): `hub.GetOrCreateFamily<PNInstanceDefinition>()` makes the registry hold the base and every subclass. `GetOrCreate<PNAreaDefinition>()`
and `Get<PNProjectileDefinition>()` throw and name the family; a mod's `Register`/`Override` routes to it. See `EveModsREADME.md`.

## Access

```csharp
var instances = hub.Instances();                                   // EveRegistry<PNInstanceDefinition>
var area = instances.GetEffectiveKind<PNAreaDefinition>(id);       // resolves base aliases; throws "x is a projectile definition, not PNAreaDefinition"
instances.RegisterBase<PNAreaDefinition>(baseId, identity, "project_n", (original, replacement) => ...); // same kind required
```

References in payloads: `context.Require<PNProjectileDefinition>(token, path)` checks that the id exists **and is a projectile** (a pending definition of the same batch counts).
`PNContentReferenceContext.Require<T>` treats any subclass of `PNInstanceDefinition` that way.

## JSON

```json
{"extensions":{"project_n":{"instances":[
  {"id":"addon:slow_field","factoryId":"base:timescale_area","data":{"buffId":"base:timescale_reduction_area","pollPeriod":0.05,"fadeIn":0.1,"fadeOut":0.3}},
  {"id":"addon:orb","factoryId":"project_n:directional","destroyable":false,"data":{ ... }}
]}}}
```

The kind is the kind of `factoryId` (no `kind` field). Beside `id`, `factoryId` and `data`, an entry accepts only the fields its kind lists in `EntryFields`
(projectile: `destroyable`); any other field is rejected. Ids are unique across all kinds (one registry). The old keys `areas` and `projectiles` are gone.
`factoryId` references a code-registered definition (not one from the same batch).

## Adding a kind (code mod or game code)

1. Subclass `PNInstanceDefinition`: `Kind` (constant lowercase name), constructor calling `base(...)` plus the required implementation check
   (`RequireImplementation(implementation, typeof(MyInstance), "MyKind")` under `UNITY_5_3_OR_NEWER`), `EntryFields` if it has JSON fields, and
   `Derive(id, data, entry)` reading them; under Unity override `HasPoolTemplate` and `CreatePoolTemplate()` when the kind is built from a pool template.
2. Register definitions with `hub.Instances().Register(...)` (game code) or `context.Register(...)` (mod).
3. Write the kind's manager (request type, pools, lifetime). Nothing in `PNBuffContent` changes.

## Compatibility

Per-kind coop hash entries became one `registry:PNInstanceDefinition` entry (`Kind` and each kind's public fields such as `Destroyable` are part of it).
Plan and rationale: `Docs/InstanceDefinitionPlan.md`.
