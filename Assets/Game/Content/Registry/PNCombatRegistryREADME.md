# Damage Types And Elements / Loại Sát Thương Và Nguyên Tố

VI: PNContentModule đăng ký hai registry độc lập qua EveRegistry<T> hiện có.
Kiểu định nghĩa nằm ở Assets/Core/Content/Definitions; ID và nội dung thuộc Game.

EN: PNContentModule registers two independent registries using the existing
EveRegistry<T>. Definition types live in Assets/Core/Content/Definitions;
IDs and concrete content belong to Game.

| Registry | Registered IDs / ID đã đăng ký |
| --- | --- |
| PNDamageTypeDefinition | project_n:physical, project_n:magic, project_n:true |
| PNElementDefinition | project_n:fire, project_n:ice, project_n:lightning |

```csharp
// EN: Read definitions from the ready session's sealed registries.
// VI: Đọc định nghĩa từ registry đã khóa của phiên sẵn sàng.
var hub = EveRegistrySession.Current;
var damageType = hub.DamageTypes.Get(PNContentModule.Physical);
var element = hub.Elements.Get(PNContentModule.Fire);
```

VI: Một hit dự kiến chọn loại damage và nguyên tố tùy chọn. Không khai báo nguyên tố
nghĩa là đòn thường; không đăng ký ID None. ID đã khai báo phải tra được trong đúng
registry. Heal/khiên không cần loại damage. Damage type có DefenseFactor: Physical 1,
Magic 0.5, True 0. Element chỉ chứa ID, không tự áp status hay phản ứng. Core tính
damage nhận với DEF có dấu và K=500 qua CalculateDamage(damage, tags), rồi áp kháng
Mitigation. PNStat.CreateDamage/SendDamage/ReceiveDamage đã nối bonus/kháng và trừ HP.
Payload JSON value/statId đã nối qua project_n:damage_action với targets tường minh;
chưa có hitbox. Xem Shared/Combat/PNCombatREADME.md và Shared/Skill/Value/PNSkillValueREADME.md.

EN: A future hit selects a damage type and an optional element. Omitted elements
mean ordinary hits; there is no None definition. Declared IDs must resolve in the
correct registry. Healing/shields do not require a damage type. Damage definitions
have DefenseFactor (Physical 1, Magic 0.5, True 0); elements remain ID-only without
automatic statuses/reactions. Core CalculateDamage(damage, tags) applies signed DEF
with K=500, then tagged Mitigation. PNStat.CreateDamage/SendDamage/ReceiveDamage now
connect bonuses, resistance and HP application. project_n:damage_action supports
JSON raw/stat values with explicit targets; no hitbox yet. See Shared/Combat/PNCombatREADME.md.

VI: Module code có thể đăng ký thêm định nghĩa trước Seal. Registry chặn ID không
hợp lệ, trùng ID trong cùng loại và đăng ký sau Seal. Các định nghĩa tự tham gia
hash kiểm tra nội dung coop, bao gồm DefenseFactor. Factor mặc định 1 khi mod không
khai báo; có thể chọn factor hữu hạn không âm khác. JSON loader chưa có schema thêm loại damage/nguyên tố;
không sửa schema Engine hoặc code gameplay cũ trong lần này.

EN: Code modules can add definitions before Seal. Registries reject invalid IDs,
duplicates within a type and registration after Seal. Definitions automatically
participate in coop content hashes, including DefenseFactor. Mod types default to
factor 1 and can choose other finite nonnegative factors. The JSON loader has no damage/element schema yet;
this change does not modify the engine schema or legacy gameplay.

VI: Modifier hiện chọn đường tính bằng CalculationId: project_n:scale hoặc
project_n:mitigation. Các ID damage/nguyên tố có thể dùng để chọn modifier khi Calculate.
Xem Assets/Core/Entity/Shared/Stats/Calculation/PNStatCalculationREADME.md.

EN: Modifiers select a calculation via CalculationId: project_n:scale or
project_n:mitigation. Damage/element IDs can select modifiers for Calculate.
See Assets/Core/Entity/Shared/Stats/Calculation/PNStatCalculationREADME.md.

VI: Đã bổ sung hub.Tags và PNTagDefinition cho tag tổng quát. JSON hook payload có thể
khai báo tags không cần value; bộ đọc và loader kiểm tra ID đã đăng ký. Payload PNDamage
và áp HP đã có qua PNStat. Xem Core/Content/Registry/PNTagREADME.md, RulesVersion project_n:10.

EN: hub.Tags/PNTagDefinition now support generic classifications. JSON hook payloads
accept optional tags without values, validated during parsing/loading. PNDamage payloads
and HP application are available through PNStat. See Core/Content/Registry/PNTagREADME.md;
RulesVersion is project_n:10.
