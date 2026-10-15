# Skill Values / Giá Trị Skill

VI: PNSkillValue là bộ đọc giá trị riêng cho action cần số, không thay schema chung
PNSkillData. Action chuyển dạng hoặc action không cần số vẫn không phải khai báo value.
Game đăng ký PNDamageAction qua project_n:damage_action trong PNGameplayContentModule.

EN: PNSkillValue is opt-in, action-owned numeric data, not a mandatory skill field.
Actions such as form changes need no value. Game registers PNDamageAction as
project_n:damage_action through PNGameplayContentModule.

```json
{
  "action": {
    "id": "project_n:damage_action",
    "perform": {
      "value": { "amount": 500, "statId": "project_n:attack_power" },
      "tags": ["project_n:physical", "project_n:fire"]
    }
  }
}
```

VI: Có statId: amount=500 là 500% stat đã Evaluate của người thi triển. Bỏ statId:
amount=500 là raw 500. Chỉ amount và statId được phép trong value. Amount phải là
số hữu hạn; statId khai báo phải là string ID hợp lệ, không được null. Khi Evaluate,
ID phải thuộc registry stat và có base/resource trên entity, không ngầm dùng 0.
Stat resource dùng giá trị Evaluate (maximum), không dùng Current. Kết quả signed
hợp lệ cho bộ đọc chung; action damage từ chối kết quả âm. Overflow bị từ chối.

EN: With statId, amount=500 means 500% of the caster's evaluated stat. Without
statId it is raw 500. Validate only amount/statId fields; amount must be finite and
an explicit statId must be a valid nonnull ID string. The stat must be registered
and present on the caster. Resources use evaluated maxima, not current values.
Generic values may be signed; damage rejects negative evaluated amounts and overflow.

## Invocation / Thi Triển

```csharp
// EN: Targets are explicit; no physics query or automatic action runner is involved.
// VI: Mục tiêu được truyền tường minh; không dò physics hoặc tự động chạy action.
using (var action = skill.CreateAction(skillId, out var context, new[] { defender }))
{
    action.Start(context);
    action.Perform(context);
    action.Exit(context);
}
```

VI: Start đọc schema và kiểm tra caster/tag, không chụp stat hoặc gây damage. Perform
lấy stat hiện tại, gọi CreateDamage đúng một lần để áp bonus và chụp nguồn/session,
rồi ReceiveDamage trên từng mục tiêu. Targets trong context được sao chép/read-only.
PNDamageAction khử trùng theo EntityId; cùng payload dùng cho mọi mục tiêu, dù bonus
phía gây thay đổi trong callback của hit đầu. DEF/kháng vẫn tính riêng từng receiver.

EN: Start validates schema/caster/tags without evaluating stats or applying damage.
Perform evaluates live values and calls CreateDamage once, then ReceiveDamage for
each unique target EntityId. Context copies targets read-only. Every target uses the
same outgoing packet; receiver DEF/resistance remain independent.

VI: Bỏ targets tạo packet để caller giữ lại cho projectile/hiệu ứng trì hoãn. Damage
và Results cho phép đọc kết quả. Packet vẫn dùng được sau Exit/Dispose. Perform chỉ
được gọi một lần, cùng context; Cancel trước Perform không gây damage. Các target
Ready/session/registry được kiểm tra trước khi gửi. Nếu receiver override lỗi sau
một số hit đã ghi, Results giữ các hit thành công; không rollback hay cho Perform lại.
Caller phải Cancel/Dispose action khi lỗi hoặc owner disable/unload, như action khác.

EN: Omitted targets prepare a packet for later delivery. Damage/Results expose output;
captured packets survive Exit/Dispose. Perform is single-use and context-bound.
Targets are prevalidated. A receiver override may fail after earlier targets commit:
successful results remain, no rollback or repeat Perform. Callers own cancellation
and disposal when invocations fail or their owner disables/unloads.

## Multiple components / Nhiều thành phần

RulesVersion project_n:41; DLL API2. `PNSkillValue` retains its single-component
contract. `PNSkillValues.Parse` accepts either that object or a nonempty array:

```json
"value": [
  { "amount": 500, "statId": "base:attack_power" },
  { "amount": 50, "statId": "base:defense" },
  { "amount": 100 }
]
```

VI: Collection giữ nguyên thứ tự và cả statId lặp, không tự cộng. Components là
read-only; EvaluateEach trả riêng từng kết quả theo nguồn được caller chọn.
Operation/Action quyết định nguồn stat và công thức. DamageAction, SlashHit,
EveSweep và HealOperation hiện chủ động cộng các thành phần theo stat caster,
rồi tạo đúng một packet. Ví dụ ATK100, DEF500 ở trên cho input850 trước Scale.
Mảng rỗng, phần tử null hoặc schema sai bị từ chối; ValidateStats kiểm tra mọi
statId, không chỉ phần tử đầu. Module kiểm tra dependency ở mọi phase.

EN: The collection never aggregates, sorts or deduplicates values. Consumers own
the formula and stat source. Current damage consumers and HealOperation explicitly
sum caster-based components before outgoing calculation. Custom operations can
evaluate them separately. Signed components remain valid in the general parser;
Slash/EveSweep/Heal settings require nonnegative coefficients. DamageAction keeps
its existing final nonnegative damage validation. Overflow fails before HP mutation.

Existing object payloads need no migration. `PNSlashHitSettings.Value` remains a
single-value accessor and throws on composites; use `Values` for arrays.
`EveSweepSettings.Value` retains its public field for legacy single-value mods;
it is null on parsed composites, which are available through `Values`.
`RequireSingle()` rejects a multi-value collection instead of dropping components.

Validation remains action-owned, not a universal value schema on arbitrary custom
payloads. Recognized built-ins validate arrays at content startup. Custom Action
modules must validate their own Operation payloads and referenced dependencies.
Numeric evaluation uses double. See [Heal API](../../Combat/PNHealREADME.md),
[crit](../../Combat/PNCriticalDamageREADME.md) and the existing iframe documentation.
