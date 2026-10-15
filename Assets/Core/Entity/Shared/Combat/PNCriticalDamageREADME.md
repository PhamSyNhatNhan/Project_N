# Double stats and critical damage

VI: Toàn bộ stat nền, Current/Max, modifier, cache, snapshot chuyển ải, value skill,
Scale/Mitigation, DEF và packet/result damage dùng double. JSON đọc trực tiếp double.
Không ép ngược về float ở giữa luồng combat. Float vẫn dùng cho hình học, vật lý,
timeline và hiệu ứng. Move/jump chuyển tốc độ sang float tại ranh giới vật lý, có
kiểm tra khoảng hợp lệ. NaN, Infinity và overflow bị từ chối.

EN: Double precision is used end to end for numeric stats and combat, including
content parsing, resource state, modifiers, calculations and popup damage values.
It is not arbitrary precision: representable spacing still increases with magnitude.
At 200M HP, damage10 now removes exactly10; tested .25 damage at 1E12 HP. Choose
gameplay magnitudes with the minimum meaningful damage in mind.

## Crit stat conventions

| Public ID | Default | Meaning |
| --- | --- | --- |
| base:crit_rate | 0 | 25 means25%; 25.75 means25.75% |
| base:crit_damage | 150 | Total critical damage150% = multiplier1.5 |

PNContentModule registers project_n:crit_rate/crit_damage and public base bindings.
Eve/Dummy JSON declares both defaults. PNStatSet.LoadCharacter supplies missing
defaults to older documents. Crit Rate is signed through base/Scale/modifiers;
negative values are retained. Only the roll probability clamps rate to0..100 and
divides by100. Crit Damage follows ordinary nonnegative stat evaluation.

Group0 modifiers add percentage points to these two stats: +10 takes rate25 to35.
Existing percentage-group modifiers retain their fractional convention: group1
value0.2 means a20% relative increase, taking rate25 to30. They do not become25=25%.

```csharp
stat.SetSource(EveId.Parse("game:crit_buff"), new[] {
    new EveStatModifier(EveId.Parse("base:crit_rate"), 25.75d, 0),
    new EveStatModifier(EveId.Parse("base:crit_damage"), 50d, 0)
}); // Starting from defaults: rate25.75%, critical damage200%.
```

## Damage snapshot

CreateDamage(amount, tags) enables crit by default. The overload
CreateDamage(amount, canCrit, tags) permits disabling it. Slash and damage_action
perform JSON accept optional boolean canCrit (defaulttrue). A missing/legacy raw
PNDamage constructor creates a noncritical packet; it does not roll automatically.

Offline/Host validates the source/tags, applies outgoing Scale, captures evaluated
crit stats, rolls once per packet, then multiplies by CritDamage/100. Each receiver
applies DEF and mitigation afterwards. Shared slash packets give all targets the
same crit outcome; creating another packet permits another roll. ReceiveDamage
never rerolls. Snapshot fields are CanCrit, IsCritical, CritRate and CritDamage;
PNDamageResult.IsCritical forwards the flag. Changes to source buffs or later source
unload do not change an existing packet.

The default draw uses a separate System.Random stream, without consuming Unity
random state. PNStat.NextCriticalRoll is a protected virtual hook for a seeded
game/session generator or testing. It must return a finite value in[0,1). Rates
<=0, >=100 and canCrit=false do not consume a draw. There is no new client transport,
deterministic replay system or multi-hit policy in this change. Popup now uses a
outlined four-point critical prefix matching the damage number, brighter number/glow, base scale.45 versus normal.4, and all
elemental sprites at the end; see Popup/PNDamagePopupREADME.md.

## Display and compatibility

EveStatNumber.Format is used by the stat Inspector; it shows at most two decimal
places without rounding gameplay calculations. It preserves G17 digits before
decimal-only display formatting, avoiding Mono's custom double formatter hiding
fractional changes near1E15. Values outside decimal's formatting range use scientific
notation with at most two mantissa decimals. Decimal is not used for gameplay math.
Popup damage now rounds to an integer and formats with BigInteger for display only.

RulesVersion: project_n:22. DLL Mod API:2 because public float signatures changed
to double. Rebuild mods using the exported SDK and set code.apiVersion="2"; API1
manifests are rejected before DLL loading. Content schemaVersion remains1.

Verification: 708 standalone checks +25 real DLL checks; native slash46, popup14,
Inspector25 and actual HDR/Bloom render. Crit checks include negative/over100 rates,
fractional thresholds, snapshot reuse, defense order, explicit opt-out and overflow.
