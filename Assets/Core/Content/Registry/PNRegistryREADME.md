
## Current: Buff reliability and presentation coop (RulesVersion66 / Mod API5)

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
Move/Skill/HP/Area replication is implied. See Assets/Core/Entity/Shared/Buff/PNBuffCoopREADME.md for epochs/sequences,
resync/cues/presenter scope and trust limits. Earlier no-replication statements refer
to the former local contract; the new transport-independent proxy is implemented.

# Numeric contributions (RulesVersion65 / Mod API4)

The new contributions registry stores PNStatContributionDefinition metadata.
base:before/after map to project_n:before/after, with locked public bindings.
hub.Contributions() accesses it. Register custom definitions in a content module;
they have no behavior or enum. Selected calculators decide supported definitions.
The immutable definitions/bindings and modifier payload IDs affect content hashes.
See current PNStatCalculationREADME. Existing calculator registry is unchanged.

# Core Registry Access / Truy Cập Registry Core

VI: PNRegistryDefinitions.cs cung cấp extension method cho EveRegistryHub từ Core,
giữ Core/Game tách assembly khỏi Engine. Các definition nằm trong Core/Content/Definitions.

EN: PNRegistryDefinitions.cs supplies Core extension methods for EveRegistryHub,
keeping Core/Game assemblies separate from Engine. Definition types live in
Core/Content/Definitions; the engine has no Project_N dependencies.

| Extension method | Definition / Định nghĩa |
| --- | --- |
| Actions | PNSkillActionDefinition |
| Operations | PNSkillOperationDefinition |
| Calculations | PNStatCalculationDefinition |
| DamageTypes | PNDamageTypeDefinition |
| Elements | PNElementDefinition |
| Tags | PNTagDefinition |
| EnemyTypes | PNEnemyTypeDefinition |
| ControlPermissions | PNControlPermissionDefinition |

```csharp
// EN: Read registered definitions from the current ready session.
// VI: Đọc định nghĩa đã đăng ký từ phiên hiện tại sẵn sàng.
var hub = EveRegistrySession.Current;
var action = hub.Actions().Get(PNGameplayContentModule.SlashAction).Create();
var calculation = hub.Calculations().Get(PNContentModule.Scale).Create();
var element = hub.Elements().Get(PNContentModule.Fire);
var enemyType = hub.EnemyTypes().Get(EveId.Parse("base:boss"));
```

VI: Accessor tương đương Get<T>(), không tạo registry, không đăng ký hoặc thay đổi
Seal/hash. Module vẫn dùng GetOrCreate<T>() để đăng ký. Get<T>() tiếp tục dùng cho
kiểu tùy chỉnh của mod. Nếu chưa đăng ký registry, accessor báo lỗi như Get<T>().
Các accessor là extension method nên không cần cùng assembly với EveRegistryHub.

EN: Accessors are aliases for Get<T>(); they do not create registries, register
content or alter sealing/hashes. Modules still use GetOrCreate<T>() during registration.
Custom mod registries can still use Get<T>(). Access before registry registration
fails just like Get<T>(). Extension methods work across the existing assembly split.

`PNContentModule` registers `enemy_types`: Normal/Elite/Boss and locked public aliases.
See `Assets/Core/Entity/Enemy/Stats/PNEnemyTypeREADME.md` for metadata and load contracts.

# Buff registry extension (RulesVersion46)

`buffs` and `buff_tags` use the existing sealed/session/base-binding pipeline.
PNContentModule owns Buff tags; PNGameplayContentModule owns registered factories
and template payloads. JSON uses Engine's generic `extensions` batch interface,
with Core parsing `extensions.project_n.buffTags/buffs` before atomic commit.
Runtime full override keeps instance identity independently of content selection.
See Assets/Core/Entity/Shared/Buff/PNBuffREADME.md for the contract and schema.

skill_categories is an independent Core registry: initial attack/skill/ultimate/dash/
burst with locked base aliases and registered Control permission mappings. Concrete
activation JSON declares categoryIds (required, may be empty) plus affectedByAttackSpeed, with optional
effects/triggerIds. Game owns built-in effect validators/factories and templates;
Core owns the runtime/context contracts. Category/effect references participate in
pack dependency validation. skill_cast is removed; skill_skill is the new permission.
See Assets/Core/Entity/Shared/Skill/PNSkillREADME.md for the full contract.
# Category damage tag links (RulesVersion64 / Mod API4)

PNSkillCategoryDefinition.DamageTagId optionally references a separately registered
combat tag. Defaults link to base:attack/skill/ultimate/dash/burst.
The ATK stat is base:attack_power (canonical project_n:attack_power), exposed by
AttackPower constants. AttackTag exposes canonical project_n:attack. Old attack
stat and all five category *_damage tag IDs have no aliases.
SkillTag/UltimateTag/DashTag/BurstTag replace the old *Damage constants;
code mods must rebuild for API4.
The original two-argument constructor remains compatible. Content validation rejects
unknown tags and links colliding with stat/damage-type identities, including aliases.
Definitions remain immutable and mapping is fingerprinted. Default public category
and tag bindings remain locked; mod-owned categories/tags register in code before seal.
This adds no JSON category/tag schema. Runtime uses effective categories and final
tag suppression/dedup through shared Skill Action context, including auxiliaries.
