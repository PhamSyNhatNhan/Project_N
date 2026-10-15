# Tags / Nhãn Phân Loại

## Current category-linked tags (RulesVersion64 / Mod API4)

The tags registry additionally contains project_n:attack, skill,
ultimate, dash and burst, with matching base: aliases.
PNSkillCategoryDefinition.DamageTagId optionally links a category to one tag.
Effective Action Perform tags include effective-category links before final
suppressTags/dedup. Category attack and tag attack belong to different registries;
the ATK stat is attack_power. Old attack stat and all five category *_damage tag
IDs/aliases are removed. Code mods use AttackPower for the stat and
AttackTag/SkillTag/UltimateTag/DashTag/BurstTag for the tags; rebuild for API4.
The link cannot name a stat or damage type; regular authored damage tags still
include physical/magic/true. See PNSkillREADME's current category section for
Snapshot/Live, auxiliary, mod registration and receiver semantics. Older examples
below describe historical registration/API state; current access is hub.Tags().

VI: PNTagDefinition trong Core chỉ chứa EveId hợp lệ. Truy cập registry qua hub.Tags.
Game PNContentModule đăng ký tag Physical/Magic/True và Fire/Ice/Lightning bằng cùng
ID với các định nghĩa chuyên biệt. Tag không tự gây damage hoặc status, không yêu cầu
value, thời gian hoặc điều kiện. Damage type giữ DefenseFactor; element giữ phân loại.

EN: Core PNTagDefinition holds only a valid EveId; access it through hub.Tags. Game
registers Physical/Magic/True and Fire/Ice/Lightning with the same IDs as their typed
definitions. Tags do not execute behavior or require values, durations or conditions.
Damage types retain DefenseFactor; elements remain separately classified.

## Registration / Đăng Ký

```csharp
// EN: Add arbitrary classification tags from code modules before sealing content.
// VI: Thêm tag phân loại tùy ý qua module code trước khi khóa nội dung.
var star = EveId.Parse("my_mod:star");
hub.GetOrCreate<PNTagDefinition>().Register(new PNTagDefinition(star));
```

VI: Tag riêng không cần có định nghĩa damage type, element hoặc stat. Loại damage
mới cần đăng ký cả PNDamageTypeDefinition và PNTagDefinition cùng ID nếu dùng trong
CalculateDamage. ID trùng trong registry và đăng ký sau Seal bị từ chối. Tag tham gia
hash coop; JSON-only mod chưa có schema đăng ký tag mới.

EN: Generic tags need no corresponding damage type, element or stat. A new damage
category used by CalculateDamage must register both its type and tag with the same
ID. Duplicate IDs and post-Seal registration are rejected. Tag definitions participate
in coop hashes. JSON-only mods cannot register new tags yet.

## Action Payloads / Payload Action

```json
{
  "action": {
    "id": "my_mod:change_form",
    "perform": {
      "tags": ["my_mod:star"],
      "formId": "my_mod:second_form"
    }
  }
}
```

VI: Ví dụ chỉ minh họa, chưa có implementation change_form. Mỗi hook Start/Perform/
Exit/Cancel có trường tags tùy chọn trong payload object. Bỏ tags hoặc [] là không
có tag; tags:null, giá trị không phải mảng hoặc entry không phải ID string bị từ chối.
PNSkillActionData cung cấp StartTags/PerformTags/ExitTags/CancelTags read-only,
khử trùng và sắp theo ID. Payload vẫn được sao chép, giữ các trường riêng của action.
Không thêm value vào schema chung. Mọi tag được tra registry lúc nạp skill qua loader
và lúc tạo context; tag không biết gây rollback trước Ready.

EN: The form example is illustrative, not an implemented action. Object hook payloads
may contain tags. Omission and [] mean no tags; null, nonarrays and nonstring/invalid
IDs are rejected. PNSkillActionData exposes read-only, deduplicated, sorted per-hook
tag lists while retaining isolated action-owned payload fields. Value is not part of
the shared schema. Loader and context creation validate registrations; unknown tags
prevent Ready and roll back loading.

## Bonus And Resistance / Bonus Và Kháng

VI: PNStatSet.Calculate hỗ trợ ID tag tổng quát cho Scale/Mitigation, dùng cùng map
modifier hiện có. Tag ngoài stat registry nhận group 0 trên Scale, cộng cuối sau
các hệ số phần trăm; Evaluate không nhận flat này. ID thuộc stat registry phải có
base/resource trên entity để nhận group 0 và cộng trước hệ số. Mitigation vẫn chỉ
nhận phần trăm. CalculateDamage yêu cầu mọi ID đầu vào đăng ký tag và đúng một
tag thuộc DamageTypes; áp DEF trước, kháng theo toàn bộ tags sau. Quy tắc một damage
type chỉ áp khi tính damage, không áp lên action chuyển dạng hoặc action khác.

EN: PNStatSet.Calculate accepts generic tag IDs for Scale/Mitigation with existing
modifier source storage. Non-stat tags accept group 0 on Scale, added after grouped
percentages and excluded from Evaluate. Registered stat IDs require an actual entity
base/resource and add flat before multipliers. Mitigation accepts percentage groups
only. CalculateDamage requires registered tags and exactly one damage
type among them, then applies DEF and grouped tagged resistance. That type constraint
does not apply to arbitrary action metadata.

VI: Dùng ID riêng cho nhãn hành vi tấn công, ví dụ my_mod:attack_action, thay vì ID
stat ATK project_n:attack_power. Dùng cùng ID sẽ chọn lại modifier Scale của ATK khi áp bonus
theo tag sau Evaluate, gây tính buff hai lần. Hạ tầng không tự suy ra tag từ stat.

EN: Give an attack-behavior tag its own ID, e.g. my_mod:attack_action, not the ATK stat
ID project_n:attack_power. Reusing that ID can select ATK Scale modifiers again after stat
evaluation. Stat IDs are not implicitly registered as tags.

VI: Eve slash perform có tag physical để minh họa; vẫn chỉ VFX, không value hoặc
damage/HP application trong slash. Luồng damage dùng chung đã có trên PNStat;
xem Shared/Combat/PNCombatREADME.md. RulesVersion project_n:10.

EN: Eve slash demonstrates a physical perform tag but remains VFX-only without value
or damage/HP application on slash. PNStat provides the shared damage flow separately;
see Shared/Combat/PNCombatREADME.md. RulesVersion is project_n:10.
