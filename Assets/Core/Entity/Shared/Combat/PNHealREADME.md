# Current incoming/outgoing flats (RulesVersion65 / Mod API4)

Heal uses Scale on base:heal at the sender and Mitigation on base:heal at the
receiver. Both accept group0 with contributionId base:before/after; omission is
after. Scale/Mitigation apply the shared registered flat contract, not a Heal-only
formula. Percentages remain grouped. Incoming after flat may turn a zero Heal
packet into positive computed healing. Missing-HP cap and no-revival remain;
crit, DEF and iframe still do not apply. Earlier group0 Mitigation restrictions
below are history. See PNStatCalculationREADME for the full current contract.

# Heal API / Luồng hồi máu

RulesVersion project_n:41; DLL Mod API2. PNStat owns a separate authoritative heal
flow in PNStatHeal.cs. PNHeal and PNHealResult are immutable numeric packets/results.

```csharp
PNHealResult result = source.SendHeal(target, 500);
PNHeal packet = source.CreateHeal(500);
PNHealResult later = target.ReceiveHeal(packet);
```

VI: Amount là lượng hồi cơ sở đã được operation tính. CreateHeal chọn duy nhất
`project_n:heal` (`base:heal` alias), gọi Scale phía gây và chụp nguồn/session.
ReceiveHeal gọi Mitigation phía nhận rồi cộng vào Current HP, giới hạn MaxHP.
Không qua crit, DEF hay iframe. Target chết không được hồi sinh; nguồn phải sống.
Packet cùng session vẫn dùng được sau khi nguồn chết/unload. Không đưa Amount
trong packet trở lại SendHeal vì sẽ tính bonus phía gây lần nữa.

EN: Sender Scale -> immutable packet -> receiver Mitigation -> HP cap. Both
calculations remain registry-selected and PNStat overrides remain authoritative.
No separate HealOutgoing/HealIncoming calculators or fixed entity heal base exist.
The Heal selector is registered in the existing Tags vocabulary; it does not
dispatch an action. Calling the Heal API establishes the flow.

For a non-stat Heal selector, Scale group0 is a late flat and group1+ percentages
follow existing grouped rules: input1000, +20% and flat500 produce1700. Receiver
Mitigation with -10% produces1530; +10% increases received healing. Mitigation
group0 remains invalid. Damage/fire bonuses do not apply to Heal.

Create/Receive require Ready entities in the current authoritative session;
Client mode rejects mutation. Amounts must be finite and nonnegative, including
calculated outputs. Invalid sessions/amounts fail before HP mutation. Health at
zero yields DeadTarget without running receiver calculation or reviving.

PNHealResult exposes Heal, TargetId, CalculatedHeal, AppliedHeal, Overheal,
HealthBefore, HealthAfter and Status (Applied, NoMissingHealth, DeadTarget,
ZeroAmount). DeadTarget reports calculated/applied/overheal0. Other results report
unused calculated healing as Overheal. HealReceived fires after HP commits,
including zero/full/dead results. Callback exceptions are logged independently;
they do not undo HP or prevent later listeners. Unload clears subscriptions.
HUD can use AppliedHeal and this event later; this change adds no UI animation.

## Registered operation

PNGameplayContentModule registers `project_n:heal` / `base:heal` in Operations.
Its only payload field is value, accepting an object or nonempty component array:

```json
{
  "value": [
    { "amount": 500, "statId": "base:attack_power" },
    { "amount": 50, "statId": "base:defense" },
    { "amount": 100 }
  ]
}
```

PNHealOperation explicitly sums caster-based components, snapshots one sender
packet and delivers it to unique explicit context targets. It validates all
targets before delivery. No targets produces a packet available via Heal for
later delivery. Heal and Results remain readable after Dispose. Execute is
single-use; Cancel prevents execution. If a receiver override fails after earlier
commits, Results preserves successful deliveries and retry is forbidden.

The owning Action creates the selected operation through
its protected `CreateOperation(context, EveId.Parse("base:heal"))` factory, executes it
with the registered Action-owned payload, and disposes it on completion/cancel/
failure/unload. Operation instances must not be stored in shared content data.
There is no automatic Operation list runner or mandatory HealAction.

The owning content module calls `PNHealSettings.Parse(payload).ValidateContent(hub)`
at startup, and declares dependencies for referenced external stats. Runtime
Execute validates the payload too. The generic registry cannot infer a custom
Action's nested Operation schema. Existing skills acquire no healing automatically.

For healing based on missing/current HP or another special formula, implement
the formula in an Operation, then call CreateHeal/SendHeal with the evaluated
numeric amount. PNSkillValue with health uses evaluated maximum health, not Current.
Generic PNSkillValues never sums automatically: other operations may use the
components independently or select another stat source.
