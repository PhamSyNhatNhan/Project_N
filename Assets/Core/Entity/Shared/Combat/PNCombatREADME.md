# Damage Flow / Luồng Damage

Separate healing now uses [PNHeal API](PNHealREADME.md) (RulesVersion41/API2),
with its own packets/results/events and shared Scale/Mitigation calculators.

Current numeric API uses double throughout stats/combat. Crit Rate/Crit Damage
use direct percentage values (25=25%, 150=critical multiplier1.5). See
PNCriticalDamageREADME.md for signed rate, snapshots, canCrit and API2 migration.

VI: Core thêm luồng damage cục bộ có thẩm quyền vào PNStat, tách phần combat sang
PNStatCombat.cs qua partial class. Payload và kết quả nằm trong PNDamage.cs. Engine
không chứa luật game; PNPlayerStat/EveCharacterStat kế thừa API này.

EN: Core PNStat now owns a local authoritative damage flow, organized in its
PNStatCombat partial class. PNDamage.cs defines the immutable payload and result.
Engine supplies numeric stat infrastructure; crit and combat rules stay in Core.
Player/character subclasses inherit this API.

## API

```csharp
// EN: The amount is already evaluated, but has not yet received outgoing tag bonuses.
// VI: Amount đã được tính giá trị, nhưng chưa áp bonus phía gây theo tag.
PNDamageResult result = attacker.SendDamage(defender, amount,
    PNContentModule.Physical, PNContentModule.Fire);

// EN: Projectiles and delayed effects capture bonuses once and apply later on impact.
// VI: Projectile và hiệu ứng trì hoãn chụp bonus một lần rồi áp khi chạm mục tiêu.
PNDamage packet = attacker.CreateDamage(amount, PNContentModule.Physical);
PNDamageResult hit = defender.ReceiveDamage(packet);
```

VI: CreateDamage kiểm tra entity sống/Ready, session và tag, rồi áp Scale của phía
gây. PNDamage giữ Amount sau bonus, Tags read-only đã khử trùng, SourceId và SessionId.
Không đưa payload này trở lại SendDamage để tránh áp bonus lần hai. Payload có thể
đến nhiều mục tiêu và còn hợp lệ sau khi nguồn chết hoặc unload trong cùng session.

EN: CreateDamage requires a living Ready source and valid tags, applies sender Scale
once, and snapshots the amount, deduplicated read-only tags, source and session ID.
ReceiveDamage consumes the snapshot directly. Do not feed its amount back through
SendDamage. Delayed packets may hit multiple targets after source death/unload in
the same session, without rereading source bonuses.

VI: Scale phía gây cộng flat của base/resource trước các hệ số; flat của tag ngoài
stat cộng cuối Scale, trước crit. Ví dụ input900, bonus cùng group .2 và .4,
flat hỏa100: Scale1540, crit200% -> packet3080. Sau đó áp DEF, Mitigation, iframe
và HP như hiện tại. Flat chỉ chọn theo tag thực sự có trong packet, không tự suy ra
tag; snapshot giữ cả phần flat khi source thay đổi/unload. Evaluate không nhận flat tag.

EN: Sender Scale adds entity base/resource flats before multipliers and non-stat
tag flats after them, before crit. Input900 with same-group .2/.4 and Fire flat100
produces Scale1540, or packet3080 at 200% crit. DEF, Mitigation, iframe and HP follow.
Only selected tags contribute; the immutable packet retains flats after source
changes/unload. Evaluate excludes non-stat tag flats.

VI: ReceiveDamage kiểm tra receiver Ready và session của packet, tính DEF K=500 rồi
Mitigation theo tags, sau đó trừ Current của project_n:health và chặn tại 0. Damage
âm, không hữu hạn, type/tag sai, overflow bị từ chối trước khi sửa HP. Không đổi Max
hoặc modifier của hai entity. Calculator override qua PNStat vẫn được gọi.

EN: ReceiveDamage validates a Ready receiver and matching packet session, applies
DEF K=500 and tagged Mitigation, then subtracts current health with a zero floor.
Invalid tags/types, negative/non-finite damage and overflow fail before HP mutation.
Maxima and modifiers are unchanged. Component calculation overrides are honored.

## Results And Death / Kết Quả Và Chết

VI: PNDamageResult gồm Damage, TargetId, CalculatedDamage sau DEF/kháng,
AppliedDamage là HP thực mất, HealthBefore/HealthAfter và Killed. Overkill có thể
CalculatedDamage > AppliedDamage. IsDead đọc Current HP <= 0, không giữ cờ chết
riêng; hồi HP qua API resource hiện có làm entity sống lại.

EN: Results distinguish calculated damage from actual HP loss, preserve before/after
health, source/target identity and the positive-to-zero Killed transition. IsDead is
derived from current health, so restoring HP permits another death transition.

VI: DamageReceived và Died phát sau khi ghi HP; hook kế thừa OnDamageReceived/OnDied
có thể bổ sung xử lý. Listener lỗi được log riêng, không ngăn listener khác hoặc làm
caller thử lại hit đã ghi. Died chỉ phát khi hit đưa HP từ dương về 0. Hit hợp lệ vào
entity đã chết không mất HP và không phát lại hai event. Hit 0/miễn damage vào entity
sống vẫn phát DamageReceived. Unload xóa listener; gameplay phải đăng ký lại sau nạp.
Thay HP trực tiếp bằng SetCurrent không tự phát event combat.

EN: Notifications and virtual hooks run after the HP commit. Listener exceptions
are logged without aborting committed damage or remaining notifications. Dead
targets do not emit repeated hit/death events; valid zero/immune hits on living
targets still emit DamageReceived. Unload clears listeners. Direct SetCurrent calls
do not emit combat events. Core does not destroy/disable entities on death.

## Authority And Scope / Thẩm Quyền Và Phạm Vi

VI: Chỉ Offline/Host được dùng Create/Send/Receive. Entity phải nạp qua loader Ready,
thuộc directory, registry và session đang hoạt động. Packet session cũ bị từ chối.
Đây là API nội bộ tin cậy, không phải handler mạng: chưa xác thực message, deduplicate
hit/replay, truyền damage hoặc đồng bộ HP cho client. Hitbox/projectile caller phải
quyết định mỗi hit được áp vào mục tiêu bao nhiêu lần.

EN: Offline/Host authority only; require Ready loaders and matching directory,
registry/session ownership. Stale packets are rejected. This trusted local API is
not a network handler and has no message authentication, replay deduplication,
damage transport or client HP synchronization. Hit detection owns per-target hit counts.

VI: PNSkillValue và action project_n:damage_action nối value/statId với luồng này.
Slash hiện dùng hitbox PhysX theo timeline để chọn targets. PNStat tạo popup sau
khi ghi HP, hiển thị CalculatedDamage; PNPlayerStat tắt popup khi player nhận damage.
Xem Popup/PNDamagePopupREADME.md và Game/Skill/Actions/Slash/PNSlashAuthoringREADME.md.
Crit dùng snapshot; iframe Gradual đã triển khai bên receiver. Heal/shield còn để sau.
RulesVersion project_n:37; DLL API2.

EN: PNSkillValue and damage_action connect raw/stat values to this flow. Slash
selects targets using timeline-driven PhysX hitboxes. PNStat presents calculated
damage after committing HP; PNPlayerStat suppresses popups on player receivers.
See Popup/PNDamagePopupREADME.md and the slash authoring guide. Critical hits use
sender snapshots; Gradual iframes run on receivers. Healing/shields remain future work.
RulesVersion is project_n:37; DLL API2.

## Gradual iframe

Every living receiver shares ONE timer across all damage sources and types, including
True, self and friendly damage. Core does not filter damage by hostility; hit selection
still belongs to callers. No Legacy mode or invulnerability flag is added.

Optional `action.perform.iframeDuration` is a finite signed number for DamageAction,
Slash and EveSweep. Omit the field to use the receiver fallback: PNStat/PNEnemyStat
0.2 seconds, PNPlayerStat 1.0 second. Explicit zero is distinct from omission; JSON
null/string/non-finite values are rejected before execution/content-session startup.
Current authored Eve sweeps omit the optional field intentionally and use fallback.

```json
"perform": {
  "value": { "amount": 100 },
  "canCrit": false,
  "iframeDuration": -0.2,
  "tags": ["base:true"]
}
```

Let T be the pre-hit remaining time and d the packet duration or receiver fallback:

- T=0: full incoming damage; new timer abs(d).
- T>0: incoming damage after DEF/mitigation multiplied by FIXED 0.3;
  new timer max(0,T-d). The hit that breaks iframe still takes the reduced damage.
- Positive d consumes time; negative d extends it; zero neither creates nor consumes.
- Valid zero-damage hits on living receivers use the same timer rules as the original.
  A dead receiver changes neither HP nor timer and emits no hit/death notifications.
- No timer cap or automatic reset on death has been added. Non-finite accumulated
  time is rejected before HP/timer commit. Restoring HP retains remaining time.

PNDamage snapshots optional IFrameDuration without selecting a receiver fallback.
`CreateDamage(amount,canCrit,iframeDuration,tags)` preserves existing virtual outgoing
implementations; existing overloads and the original packet constructor remain available.
No additional crit roll or outgoing bonus calculation is performed when attaching duration.

Prepared damage (`PNCombatSourceSnapshot`, projectile P0-A): `PrepareDamage(amount,canCrit,iframe,tags)`
returns an immutable `PNPreparedDamage` holding the outgoing amount BEFORE the critical roll (sender Scale
applied), the captured crit rate/damage, iframe and canonical tags. `CreateDamage(prepared)` rolls with
`RollCritical(prepared)` and builds the packet; `CreateDamage(prepared, critical)` takes the caller's roll so
packets can share it. `CreateDamage(amount,canCrit,iframe,tags)` is now `PrepareDamage` +
`CreateDamage(prepared)`, so one formula serves both and a subclass overriding `PrepareDamage` also changes
the four-argument overload (subclasses overriding only the four-argument overload behave as before). A prepared
damage no longer reads the source, so later modifier changes do not apply; it is rejected by a snapshot of
another source/session. Metrics: `combat.create-snapshot` (wrapper), `combat.prepare-damage`,
`combat.create-prepared`.

`PNCombatSourceSnapshot.Calculate(input, ids)` (projectile P0-A2, virtual, read only) runs the Scale route of the
captured modifiers selected by `ids` (stats or tags; no damage-type tag needed) on a caller-supplied value:
`(input + Σbefore) × Π(1 + Σ group ≥ 1) + Σafter`, clamped nonnegative, same as `PNStat.Calculate(Scale, ...)` at
capture time. Requires the active authoritative session; unknown ids, no ids, null ids and non-finite input are
rejected. Later modifier changes on the source do not reach an existing capture.
PNDamageResult reports IFrameMultiplier, IFrameBefore, IFrameAfter and
ResolvedIFrameDuration; CalculatedDamage/popup includes iframe reduction. Reduced hits
remain successful hits and still emit DamageReceived/rewards normally.

PNStat owns a private PNAbilityCollection containing passive `project_n:iframe` State.
It uses the existing Ability timers, without requiring a skill/PNAbilityManager on
receivers or changing authored scenes. Its immutable definition is shared, runtime
instance is per stat. PNStat.Update advances it with Time.deltaTime (Game clock),
ignoring EveTimeScale including its minimum. Unity pause stops it; global slow scales
it. Disable pauses automatic updates, reenable resumes without catchup; unload disposes
the collection, reload starts empty. AdvanceCombatTime accepts an explicit Game delta
for native/standalone checks. SessionStopped blocks timer advancement and damage.

Dash adds no iframe or invulnerability here; future Buff integration remains separate.
Authority is still local Offline/Host: no network damage transport/replay/deduplication
has been added. Calls are committed sequentially by the existing receive path.

Verification: Tests/EveEngine/PNIFrameTests.cs and PNIFrameEditorChecks.Run() cover
signed/zero/missing duration, defaults, two sources, clock, rejection and lifecycle.

## Receiver absorption hook (RulesVersion89)

`ReceiveDamage` calls two virtual hooks around the health commit: `AbsorbDamage(damage, damageType, calculated, out absorption)` (calculation only, returns the health
loss, default = the whole final amount) and `CommitAbsorption(result, absorption)` (after health is written). `PNEnemyStat` uses them for lorica (`Docs/EnemyLoricaDesign.md`).
`PNDamageResult` gained `LoricaBefore/LoricaAfter/LoricaAbsorbed/LoricaBroke` (zero for receivers without lorica); `AppliedDamage` is still the health lost, so popups show it.

## Penetration in packets (RulesVersion95)

Every packet created from a source (`PNStatCombat.CreateDamage`, `PNCombatSourceSnapshot.PrepareDamage`/`CreateDamage`, therefore projectiles and DOT) carries
the source's penetration-route modifiers captured at creation (`PNDamage.Penetration`, immutable, shared array). `ReclassifyDamage` keeps the new packet's
copy; `WithIFrameDuration` preserves it. The receiver applies them inside `CalculateDamage` (see PNStatCalculationREADME, "Penetration route").
Public-constructor packets and `PNHeal` carry none. Design: `Docs/PenetrationDesign.md`.

## Sources: PNSource and PNCombatSource

`PNSource` (`Assets/Core/Entity/Shared/PNSource.cs`) is the simple base: `SessionId` + `EntityId`, nothing else. Use it directly for things that need no combat (items, plain
identity records such as the owner/follow identity of `PNTimeScaleArea`). `PNCombatSource` (this folder) is its combat branch and is what Buff, DOT, areas, projectiles and
skill effects carry: it adds `TargetLayers`, `Snapshot` (`PNCombatSourceSnapshot`), `Live` and `LoadedSource`. It has no public constructor:
- `PNCombatSource.Capture(stat)` for an entity: target layers, snapshot and weak live access are captured once.
- `PNCombatSource.System(session, entityId, targetLayers = null)` for world/system effects with no stats: `Snapshot`/`Live`/`LoadedSource` are `null`, so paths that need stats
  reject it (`PNProjectileLaunch.FromSource`, DOT, snapshot-mode Damage Buff).
A `PNCombatSource` is a `PNSource`, so it can be passed where only identity is needed. `TargetLayers` is combat-only and is not on `PNSource`. Plan: `Docs/CombatSourceSplitPlan.md`.

## Targeting by layer (RulesVersion98; replaces teams)

There is no team and no "hostile" relation. **`PNStat.TargetLayers`** (`PNTargetLayers`, `PNStatTargeting.cs`) is the ordered set of layers an entity attacks and targets:
- Base: the character's `data.combat.targetLayers` (`PNCombatSettings`, an ordered array of layer names, `[]` = nothing) when present, otherwise the class default
  (`PNPlayerStat` → `Enemy`, `PNEnemyStat` → `Player`, `PNStat` → none). Layer names are the table in `PNLayers` (`Player`, `Enemy`, `Projectile`, `Wall`, `Boundary`, `Void`, `Ground`;
  `Npc` joins with the first NPC) and must match ProjectSettings/TagManager.
- Buffs add layers with `stat.GrantTargetLayers(layers)`: a lease with counts per layer (two grants of one layer keep it until both end; disposing twice does nothing), layers not already
  in the base go after it in first-grant order, and the Buff releases only what it added. Nothing is stored in the stat to "restore" later; a charm will set and revert its own changes.
- `PNTargetLayers` keeps `Mask` (an `int` for hot loops) and `Order` (the stored order; it is **not** a priority: lease order depends on when Buffs were granted, so target selection treats
  every layer of the set alike). `Union` returns an operand when the result equals it.
- Tests: `stat.Targets(other)` (other's layer is in my `TargetLayers`; not myself, not null) and `stat.SharesLayer(other)` (same layer, the ally test). Both are asymmetric / non-virtual wrappers over
  `PNLayers.Contains(mask, layer)`.
- An attack hits `caster.TargetLayers` united with the attack's own `targetLayers` (`create_projectile`, Slash `hitbox.targetLayers`, Sweep `targetLayers`); a set that contains the attacker's own layer
  hits allies. Slash and Sweep never hit the attacker; a projectile can hit its shooter once it has left the shooter's box (RulesVersion99, see the projectile README). Projectiles capture the set at launch; Slash and Sweep read it when they hit. Areas use their own `targetLayers` as an absolute list.
- Not replicated: the host resolves hits and damage. A stat changing layer at run time (charm) is seen by projectile candidate lists after their next refresh (`TargetRefreshSeconds`).

## Choosing a target (RulesVersion99)

One selector for every automatic target choice (`Docs/AutotargetPlan.md`): **`PNTargetSelector.Select(candidates, query)`** over the shared per-world candidate list
(`PNTargetCandidates`, owned per world by `PNTargetIndex` = `PNProjectileManager.Targets`). Allocation-free; cheap filters first (layer bit, excluded chooser, range, inclusive); aggro and entity ID come from the candidate (aggro is read at membership
refresh, 0.1 s, and on every `Acquire`, so a query outside the tick sees a change at once and a homing search within 0.1 s), health is read only in the health modes, and liveness is checked only for a candidate that would be kept. Candidates are entities with a hit box (projectiles are never candidates).
- **Rules** (`PNTargetRule`, `PNTargetRanking`, portable and smoke-tested): **Autotarget** (player side) = higher `aggro` first (compared directly: any positive beats 0, negative loses), then the mode
  (`Nearest` | `LowestHealth` absolute | `LowestHealthPercent`), then the fixed tie-break (lower absolute health, nearer, lower entity ID). **Aggro** (enemy side, Terraria `TryTrackingTarget` style) =
  distance minus the candidate's `aggro`, smallest first, then lower entity ID. There is no "prefer boss" option: a boss that should be preferred gets a positive `aggro` in its data.
- **Held target** (`query.Held`): kept while valid unless `PNTargetRanking.ShouldSwitch`: a higher aggro takes over at once; at equal aggro `Nearest` switches only to a candidate at least 20% nearer
  (`SwitchRatio` .8) and the health modes keep it until it dies or leaves the query; the Aggro rule switches at a 20% lower positive score, or at once for a negative (taunting) score.
- **Line of sight** is a per-query (per-skill) option, `RequireSight` (off by default): the best `SightBudget` candidates (default 4, max 16) are tested in rank order with `PNAgentSpatial.HasSight`
  (walls block, entities do not) and the first visible one wins; a held target out of sight is dropped.
- **`aggro`** (`project_n:aggro`, Number, base 0 added by `PNStatSet.LoadCharacter`, **signed**: kept in the DEF/crit/CDR list that is not clamped at 0) in world meters. Buffs, gear and taunts change it
  with ordinary modifiers (flats default to the After contribution); a hard taunt is a very large value. Not replicated (host side); `PNTargetSelector.AggroOf(stat)` reads it.
- **Who uses what**: `PNTargetRules.For(chooser)` (Game) gives the local player's characters `Autotarget(Settings auto target mode)`, a remote player's character on the host `Nearest` (until Skill
  networking carries the owner's choice), and every other chooser `Aggro`. `PNAutoTarget` (component, `PNAutoTarget.For(stat)`) holds one target per character for all its skills; `Resolve(range, mask)`
  uses the default rule and updates the held target, `Resolve(range, mask, ownRule)` is a skill's own rule and leaves it alone. Eve's ordinary casts use the default rule, her ultimate its own
  `LowestHealth`. `create_projectile` `aimRadius` and homing are in the projectile README; `PNAgentPerception.NearestVisible` and `PNEnemyMeleeBehavior` rank by the Aggro rule.
- Skills whose rule is not one of these walk `PNTargetCandidates.RawItems` themselves; there is no interface per rule.

## Aggro eligibility (RulesVersion101)

`body.aggro: "ignored"` (`PNStat.AggroMode`) makes every Aggro-rule scan skip the entity: `PNTargetSelector.Select` (candidate flag `AggroIgnored`), `PNEnemyMeleeBehavior` and
`PNAgentContext.CanTarget` all ask `PNTargetSelector.IsAggroEligible`. The Autotarget rule does not read it. The aggro stat (`project_n:aggro`) is a separate weight: distance minus aggro, smallest first.

## Optional Health and hit counting (RulesVersion100)

- An entity is damageable only if its character declares a `project_n:health` stat (`PNStat.HasHealth`, cached when the stat set is initialized). Without it `IsDead` is
  false (it no longer throws), `ReceiveDamage` returns a result with `Ignored == true` and zeros (no iframe, popup, `DamageReceived`, `Died`), `ReceiveHeal` returns
  `PNHealStatus.NoHealth`, and the Autotarget rule skips the entity (`PNTargetCandidates.Candidate.Damageable`); the Aggro rule (enemies) still sees it, so it can be a decoy.
  A projectile that hits it ends on it (`hitLimit` counts the contact) and applies nothing. Temporary invulnerability stays a Buff or iframe.
- `body.damageMode: "hits"` (`PNStat.DamageMode`): N = the declared maximum Health (integer >= 1, no Lorica). In `ReceiveDamage`, after the iframe and instead of DEF / Mitigation /
  critical / `AbsorbDamage`: `calculated = (damage.Amount > 0 && iframeBefore <= 0) ? 1 : 0`. A hit inside iframe removes 0 and the iframe is still refreshed as usual; damage over time
  and area ticks go through the same path and count; `ReceiveHeal` returns `PNHealStatus.Blocked`. The popup shows `1`; the health bar is the normal one.
- `body.healthBar: "hidden"` (`PNStat.HealthBar`) hides the world health bar.
