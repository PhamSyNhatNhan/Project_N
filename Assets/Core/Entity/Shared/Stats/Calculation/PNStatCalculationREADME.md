# Current flat contribution contract (RulesVersion65 / Mod API4)

`contributions` stores immutable PNStatContributionDefinition metadata; access with
hub.Contributions(). Built-ins base:before/after resolve project_n:before/after.
The definitions have no executable behavior; selected calculators interpret them.
Default bindings are locked. Mods may register their own definitions/calculators;
default grouped calculators reject unsupported IDs rather than silently using after.

Modifier calculationId is optional for every group (omission uses stat-set default,
normally Scale). contributionId is optional only for group0. Omission means after
for both Scale and Mitigation, whether the selector is a stat or combat tag.
Group1+ with this field, unknown IDs, empty/null authored IDs are rejected.
Base/resource declarations themselves have neither routing field.

Both default calculators use:
`(input + sum(before flats)) * product(max(0, 1 + sum(percent group))) + sum(after flats)`.
Clamp after all stages; Scale's signed DEF/CritRate/CDR policies remain. Mitigation
now accepts flat group0. Zero percentage factor followed by positive after flat
can produce a positive result, including received Heal. Damage applies receiver
Mitigation after DEF; Heal caps the computed incoming amount to missing HP and
still cannot revive. Neither route edits a stat just because its ID is selected.

Registered stat selectors still require an actual entity base/resource for group0.
Evaluate excludes non-stat flats; raw Calculate includes them. PNStatSet's virtual
calculator calls, selected overrides and evaluation-scope cleanup remain intact.
ApplyGroups/ApplyScaleGroups now honor this metadata; no math is appended outside
the selected calculator. Custom calculators own their support/validation/formula.

Eve modifier payloads were deliberately NOT migrated to before. For example base
ATK10 + Burst flat100 + Momentum10% now yields111, not121. Existing constructors
remain, the new five-argument constructor includes contributionId, and Clone,
combat captures/floor snapshots retain it. Registry/Buff data participate in hashes.
Buff stat template, Momentum entries and Vulnerability payloads parse routing IDs;
stat-template statId may also select registered combat tags. Authored external IDs
require dependencies. Other unsupported/new fields remain strict validation errors.

```json
{"statId":"base:fire","value":200,"group":0,
 "calculationId":"base:mitigation","contributionId":"base:before"}
```

Older formula/flat restrictions below are historical and superseded by this section.

# Modifier Calculations / Đường Tính Modifier

Numeric stat/modifier/calculation APIs now use double (DLL Mod API2). Use d literals
for fractional values to avoid converting already-rounded float literals. Crit
Rate and Crit Damage are direct percentage stats; percentage-group modifiers still
use the existing fractional convention. See ../../Combat/PNCriticalDamageREADME.md.

`base:attack_speed` is a direct percentage stat: 50 = 50%, 100 = 100%, 200 = 200%.
PNStatSet supplies a base value of 100 when a character omits it; explicit values
remain authoritative. Scale modifiers use the existing rules: group 0 adds percentage
points, group 1+ uses fractional bonuses (0.2 = +20%). Evaluation is bounded at zero.
The stat does not automatically change Action, Ability or entity time; Skills choose
which attack timings use Evaluate(base:attack_speed) / 100. Eve's current normal
slash now scales its entire shared VFX/hitbox timeline by this factor each frame.
Non-normal skills do not automatically inherit this behavior.

`base:cooldown_reduction` defaults to0 and stores a signed direct percentage.
20 reduces cooldown by20%, -20 lengthens it by20%; the stat retains values above100,
while the cooldown consumer clamps them to100. It uses Scale modifiers, with group0
adding percentage points and percentage groups retaining fractional conventions.
PNSkill's explicit ApplyCooldownReduction helper passes the stat to eligible entity
Abilities. Active periods are preserved; new periods use the updated parameters.

## Tiếng Việt

`EveStatModifier` có `CalculationId` kiểu EveId. Engine chỉ giữ ID, cung cấp hook
kiểm tra và tính toán cho lớp kế thừa; không đăng ký hoặc hiểu Scale/Mitigation.
Core cung cấp PNStatCalculationDefinition, PNStatCalculationContext, PNStatCalculation
và hai triển khai mặc định. Game/Content/Registry/PNContentModule đăng ký:

| ID | Logic |
| --- | --- |
| project_n:scale | PNScaleCalculation: flat base cộng trước hệ số; flat ngoài stat cộng cuối Scale |
| project_n:mitigation | PNMitigationCalculation: điều chỉnh giá trị nhận bằng phần trăm theo nhóm |

Modifier không khai báo CalculationId dùng đường tính mặc định của PNStatSet,
ban đầu là project_n:scale. Không đặt ID mặc định của Project_N trong Engine.
`PNStatSet` nhận registry hub đã Seal; `PNStat.Initialize(character, hub)` dùng khi
khởi tạo độc lập. Loader tự truyền đầy đủ hub. Overload chỉ nhận registry stat
chỉ dùng với đúng registry của phiên đang hoạt động.

Truy cập registry ngắn qua `hub.Calculations`, `hub.DamageTypes`, `hub.Elements`.
Các property này nằm trong Core/Content/Registry/PNRegistryDefinitions.cs và chỉ alias Get<T>().

```csharp
// EN: Percentages retain the existing fractional convention: 0.2 means 20 percent.
// VI: Phần trăm giữ quy ước tỷ lệ hiện có: 0.2 nghĩa là 20 phần trăm.
stat.SetSource(sourceId, new[] {
    new EveStatModifier(PNContentModule.AttackPower, 50d, 0, PNContentModule.Scale),
    new EveStatModifier(PNContentModule.AttackPower, 0.2d, 1, PNContentModule.Scale),
    new EveStatModifier(PNContentModule.Fire, 0.2d, 1, PNContentModule.Scale),
    new EveStatModifier(PNContentModule.Fire, -0.2d, 1, PNContentModule.Mitigation)
});

// EN: Evaluate uses only the default Scale route and the selected base values.
// VI: Evaluate chỉ dùng đường Scale mặc định và giá trị nền của các ID được chọn.
double attack = stat.Evaluate(PNContentModule.AttackPower);

// EN: Supply a raw value when applying bonuses or incoming modifiers without a base stat.
// VI: Truyền giá trị đầu vào khi áp bonus hoặc modifier nhận mà không cần stat nền.
double outgoing = stat.Calculate(PNContentModule.Scale, 100d, PNContentModule.Fire);
double received = stat.Calculate(PNContentModule.Mitigation, outgoing, PNContentModule.Fire);
```

Các đường tính lưu chung trong map sourceId -> modifier. Lọc CalculationId trước
khi chọn ID và gộp group; cùng group trên các ID được chọn cộng nhau, khác group nhân.
Giá trị mặc định được chặn không âm, riêng biểu thức chỉ chọn DEF giữ giá trị có dấu.
Hệ số nhóm phần trăm vẫn được chặn tại 0. ID có thể thuộc registry stat, damage type,
element hoặc tag tổng quát. Group 0 của ID thuộc stat registry chỉ hợp lệ khi entity
có base/resource tương ứng (giá trị 0 vẫn hợp lệ), và cộng trước các hệ số. ID stat
thiếu base bị từ chối, không chuyển thành flat cộng cuối. Group 0 của ID ngoài stat
registry chỉ nhận đường Scale và cộng sau toàn bộ hệ số phần trăm. Mitigation
không nhận group 0. Chưa có modifier thì giá trị đầu vào giữ nguyên.

RulesVersion project_n:40, DLL API2. Công thức Scale mặc định:
`(input + flat base/resource) * product(max(0, 1 + sum(percent cùng group))) + flat ngoài stat`.
Ví dụ `900 * (1 + .2 + .4) + 100 = 1540`. Chặn không âm sau flat cuối,
giữ luật stat có dấu hiện tại. Flat damage vẫn chịu crit, DEF, Mitigation và iframe.

Evaluate loại group 0 ngoài stat khỏi context, kể cả biểu thức nhiều ID; phần trăm
vẫn tham gia như trước. Calculate nhận input thô thì giữ flat này. PNStatSet giữ
lời gọi virtual Calculate khi Evaluate; scope được khôi phục bằng finally. Calculator
gọi Calculate lồng nhau nhận ngữ cảnh raw, không kế thừa bộ lọc Evaluate.
Selected implementation vẫn được tạo qua registry. ApplyGroups giữ công thức cũ
cho mod; ApplyScaleGroups là helper mới cho công thức Scale mặc định. Calculator
thay thế tự quyết định công thức của mình; không có phép cộng flat ngoài calculator.

EN: Registered stat IDs require an actual entity base/resource for group 0, even
when the base is zero. Missing bases are rejected. Non-stat IDs accept group 0 only
on Scale, where the default calculator adds them after all percentage groups.
Evaluate filters those damage flats before invoking the selected calculator, while
retaining percentage modifiers. Raw Calculate includes them. The existing virtual
Calculate call remains; evaluation scope is restored on exceptions and suspended
inside calculators for nested raw calculations. ApplyGroups retains its legacy
formula; ApplyScaleGroups supplies the new default Scale formula. Selected custom
calculators remain authoritative. No modifier schema or DLL API2 signature is removed.

Scale và Mitigation của hai entity tính riêng. Ví dụ +20% Fire bên gây và -20% Fire
bên nhận: 100 -> 120 -> 96. Không cộng hai phía trước khi tính.

Resource maxima/currents chỉ chịu Scale. SetSource kiểm tra cả batch trước khi thay
nguồn; modifier sai không làm mất nguồn cũ. Clone/snapshot chuyển ải giữ CalculationId.
PNStatSet(definitions) đã được thay bằng PNStatSet(hub) để không mất registry của mod.

## Code-Mod Extension / Mở Rộng Mod Code

```csharp
// EN: Register code already available to the game before the content hub is sealed.
// VI: Đăng ký code đã có trong game trước khi hub nội dung bị khóa.
hub.GetOrCreate<PNStatCalculationDefinition>().Register(
    new PNStatCalculationDefinition(EveId.Parse("my_mod:conversion"), typeof(MyCalculation)));
```

VI: MyCalculation kế thừa PNStatCalculation, có constructor public không tham số,
override Calculate(context) và có thể override ValidateModifier. Context cung cấp
giá trị đầu vào, PNStatSet, ID và modifier đã lọc; không bắt buộc dùng ApplyGroups.
Mỗi lần chạy/validate tạo instance mới, không giữ trạng thái chung trong registry.
Logic tính nên chỉ đọc đầu vào, không thay đổi nguồn/stat trong khi đang tính.
Mod dùng ID stat/type/element đã đăng ký; loại ID mới có thể mở rộng hook
ValidateEvaluationId của PNStatSet. Definition gồm Id/Implementation để kiểm tra hash coop.
JSON-only mod chưa có schema đăng ký hoặc nạp code tính mới; module code phải được
game nạp trước Seal. Chưa có runtime loader cho DLL mod.

EN: MyCalculation derives from PNStatCalculation with a public parameterless
constructor. Override Calculate(context), optionally ValidateModifier. Context
provides the input value, stat set and filtered IDs/modifiers; ApplyGroups is optional.
Each invocation/validation creates a fresh instance. Calculators should read inputs
without mutating stats/sources during evaluation. Registered stat/damage/element IDs
are accepted; additional ID kinds can extend PNStatSet.ValidateEvaluationId.
Definition ID and implementation identity participate in coop hashes. JSON-only
mods cannot register/load new code; executable modules must be loaded before Seal.
No runtime DLL-mod loader is implemented.

EN: Named Core access is available through hub.Calculations, hub.DamageTypes and
hub.Elements. These Core-defined properties alias Get<T>() without creating registries.

## Signed Defense / DEF Có Dấu

VI: PNDamageCalculation dùng K cố định 500, D = DEF cuối sau Scale nhân với
DefenseFactor của damage type. Hệ số DEF = (2 / PI) * atan(D / 500).
Damage sau DEF = damage - damage * hệ số DEF; sau đó mới áp các nhóm Mitigation.
Physical có factor 1, Magic 0.5, True 0. DEF âm tăng damage nhận; DEF dương giảm
damage tăng chậm dần. K là lượng DEF hiệu dụng để giảm 50 phần trăm damage.

EN: PNDamageCalculation fixes K at 500. D is evaluated DEF times the damage type's
DefenseFactor. The coefficient is (2 / PI) * atan(D / 500). Subtract damage times
this signed coefficient, then apply Mitigation groups. Physical/Magic/True factors
are 1/0.5/0. Negative DEF amplifies damage; positive DEF has diminishing reduction.

```csharp
// EN: Input damage already includes outgoing bonuses. Exactly one type tag is required.
// VI: Damage đầu vào đã gồm bonus phía gây. Bắt buộc đúng một tag loại damage.
double received = defender.CalculateDamage(outgoing, PNContentModule.Physical, PNContentModule.Fire);
```

VI: CalculateDamage trên PNStat hoặc PNStatSet chỉ tính damage nhận, không đổi HP.
Type được chọn từ tags, không dùng cặp trường typeId/elementId cố định. Tag lặp chỉ
tính một lần; thiếu hoặc nhiều damage type bị từ chối. Mọi tag damage phải được đăng ký
qua hub.Tags; không giới hạn ở type/element. True bỏ qua cả hai dấu
DEF nhưng vẫn áp kháng theo tags. Thiếu base DEF được xem là 0.

EN: PNStat/PNStatSet.CalculateDamage calculates incoming damage without changing HP.
Resolve exactly one type from tags, deduplicate tags and reject missing/conflicting
types. Damage tags must be registered through hub.Tags and need not be types/elements.
True bypasses signed DEF, not tagged resistance. Missing
base DEF evaluates to zero. Non-finite inputs and double overflow are rejected.

## Scope / Phạm Vi

VI: Luồng PNStat nối bonus, crit snapshot, DEF, kháng và trừ HP bằng double.
PNSkillValue/PNDamageAction nối giá trị skill; slash dùng hitbox PhysX theo timeline.
Xem Shared/Combat/PNCriticalDamageREADME.md. RulesVersion project_n:22; DLL API2.

EN: Pure calculations feed the double damage pipeline, including sender crit
snapshots and receiver DEF/mitigation. Slash uses timeline-driven PhysX hitboxes.
See Shared/Combat/PNCriticalDamageREADME.md. RulesVersion project_n:22; DLL API2.


## Bounded built-in calculation cache (RulesVersion68 / Mod API6)

PNStatSet caches only exact PNScaleCalculation/PNMitigationCalculation results on
plain PNStatSet with plain immutable base/resource/modifier entries. The key includes
calculation, raw amount, canonical selector set and stat-evaluation mode. At128 entries
it clears rather than growing without bound. Source/base invalidation clears results.
Resource Current does not affect these built-in formulas. Custom calculators,
derived sets/entries and nested custom formulas keep the uncached path. Selected
factories still create a fresh calculator each invocation; no calculator instance,
RNG roll, packet, target result or gameplay callback is memoized.

Combat snapshot construction bulk-imports validated sources instead of recalculating
resource maxima after every source insertion. Captures remain isolated from later
source writes. Private copy reuse on Current-only writes requires that all involved
formulas (including outgoing Scale) are built-in and independent of Current; custom
formulas refresh their capture and retain current-dependent behavior.

Selected built-in calculator factories avoid reflection but still create fresh
instances. Sealed registry bindings cache selected definition references, preserving
mutable registration/selection checks before Seal and selected override semantics.
PNStatSet canonical selector caches are bounded512; they cache sealed ID resolution,
not gameplay results. Stock Stat Buffs parse immutable modifier tuning once and retain
an existing modifier lease when effective value and owning resource scope are unchanged.
Resume with a new scope reacquires effects; no transient remove/add is introduced for
an unchanged contribution refresh.

One/two-selector built-in calls use a canonical tuple key before allocating a set
or formatted key. Small and general numeric caches share the128-entry bound.
Damage classification uses its own128-entry canonical-selector cache. Validated
immutable entries can be shared inside private snapshot copies; resource Current
is copied separately. Custom formulas/entries/sets use ordinary full construction
on every capture, including restoration of Current above the raw base maximum.

## Prepared combat inputs (RulesVersion69 / Mod API6)

Evaluate bypasses numeric memoization for custom calculators, derived sets and
mutable/custom entries. Current or external formula state therefore takes effect
without a mod manually clearing caches. Only exact built-in definitions with plain
immutable entries reuse evaluated numbers. Selected factories still run per call.

Modifier preparation is independent of the input amount: bounded128-entry caches
retain canonical selectors and ordered, read-only modifier lists on plain immutable
sets. Source/base invalidation clears preparation. Custom calculators still receive
a fresh context and execute their formula each time; mutable entries and derived
sets use fresh preparation. One/two selectors use tuple keys. Public context inputs
are defensively copied; private prepared lists are immutable.

Combat tags share validated immutable internal lists within a sealed stat registry;
public packet construction still copies caller tags. A different selector set selects
different preparation. Stateless defense calculation is reused. Private source
snapshot wrappers can reuse the same isolated stat copy, while capture overrides,
authority and exact published-presence checks still execute. Unload releases retained
wrapper references; delayed packets/captures preserve their original lifetime rules.
