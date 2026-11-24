# PNSkill

Rules75: [Skill binding override](PNSkillBindingREADME.md) adds entity-local slot selection,
scoped overrides and extensible behavior factories. Slot and Skill categories are independent.

Current lifecycle (RulesVersion67 / Mod API6): skill payloads initialize in Prepare,
but IsReady and activation require a valid published context. Dependency/initial
passive setup uses IsInitialized and the loader's narrow module scope. Public gameplay
must not use IsInitialized as permission to execute. See EveActorLifecycleREADME.

## Khung hiện tại

`ApplyCooldownReduction(PNAbility ability, double flat = 0)` cho skill kế thừa
chủ động áp stat `base:cooldown_reduction` lên Cooldown hoặc State thuộc entity.
Gọi trước khi dùng và khi stat thay đổi; không tự subscribe stat hay tự áp toàn bộ
Ability. Giá trị 20 nghĩa là giảm20%, -20 tăng20%; từ100 trở lên hồi chiêu bằng0.
Flat không âm được trừ sau phần trăm. Timer đang chạy giữ tiến độ; tham số mới áp
chu kỳ tiếp theo. State duration và counter lifetime không bị ảnh hưởng; helper
từ chối Counter, Ability ngoại entity hoặc stat chưa Ready. Reset Ability xóa các
giảm trừ runtime, skill cần áp lại nếu muốn tiếp tục hưởng CDR. Eve slash chưa có
cooldown nên chưa dùng helper này.

`PNSkill : EveSkill` override `LoadSkillData` để chuyển payload thành `PNSkillData`.
Engine không thay schema gameplay. Player/Enemy/Eve tiếp tục kế thừa PNSkill.

```json
{
  "id": "project_n:eve_shot",
  "data": {
    "skill": { "comboResetTime": 2 },
    "action": {
      "id": "project_n:projectile_cast",
      "start": { "windupDuration": 0.3 },
      "perform": { "projectile": "project_n:eve_bullet", "speed": 12 },
      "exit": {},
      "cancel": {}
    }
  }
}
```

Ví dụ minh họa, chưa đăng ký projectile_cast hoặc thêm skill này vào content Eve.

- Payload null được phép, giữ semantics Engine; không tạo Action từ skill thiếu data.
- Payload khác null phải là JObject hoặc PNSkillData đăng ký bằng code.
- `action` và ID hợp lệ là bắt buộc. `skill` và payload bốn hook có thể bỏ trống/null;
  parser chuẩn hóa về null. Hook vẫn thuộc hợp đồng Action kể cả khi không có dữ liệu.
- Các trường bao ngoài không biết bị từ chối; nội dung payload giữ nguyên JToken,
  không suy đoán schema của Skill/Action. Getter trả bản sao; thay đổi bản đọc không sửa cấu hình.
- Lớp Skill riêng kiểm tra `Skill`; Action riêng kiểm tra payload từng hook và tham chiếu
  Operation/projectile mà nó cần. Chưa có trình kiểm tra graph hoặc nội dung Action tự động.
- Nạp qua entity loader kiểm tra Action ID trong registry đã seal. `Initialize` trực tiếp chỉ
  phân tách dữ liệu vì API Engine không truyền registry hub; không dùng đường này để chạy Action.

## Registry và thực thi

Các định nghĩa factory nằm ở `Assets/Core/Content/Definitions/PNSkillExecutionDefinitions.cs`.
Truy cập ngắn qua `hub.Actions`, `hub.Operations`; các property này alias `Get<T>()`.
Xem `Assets/Core/Content/Registry/PNRegistryREADME.md` cho đủ registry Core.

`PNContentModule` tạo hai typed registry rỗng. `PNGameplayContentModule` trong
`Assets/Game/Content/Registry` đăng ký triển khai cụ thể tại `Assets/Game/Skill/Actions`
và `Assets/Game/Skill/Operations`; runtime nạp cả hai module. Module của feature đăng ký loại C# cụ thể:

```csharp
hub.GetOrCreate<PNSkillActionDefinition>().Register(
    new PNSkillActionDefinition(actionId, typeof(MyAction)));
hub.GetOrCreate<PNSkillOperationDefinition>().Register(
    new PNSkillOperationDefinition(operationId, typeof(MyOperation)));
```

Loại phải kế thừa đúng base, không abstract và có constructor public không tham số.
Definition giữ ID/Implementation cho hash, Create tạo instance mới. Không đăng ký instance
đang chạy. Không resolve tên type do JSON đưa vào; JSON chỉ chọn ID đã đăng ký bằng code.
RulesVersion hiện là `project_n:14`; tăng khi hợp đồng/luật thực thi thay đổi.

`CreateAction(skillId, out context)` yêu cầu entity Ready, tạo instance và context riêng,
không tự gọi hook. Context chứa Skill, SkillId, Data, registry, Targets và Direction.
Skill cụ thể quyết định gọi Start/Perform/Exit/Cancel.
Không có state machine hoặc tự động chuyển trạng thái trong nền này.

`CreateAction(skillId, out context, direction, targets)` nhận hướng world-space do
skill chọn. Context chụp vector chuẩn hóa bất biến; từ chối vector 0/NaN/Infinity.
Direction có thể bỏ trống với action không cần hướng, ví dụ heal/đổi dạng.
Slash yêu cầu hướng có thành phần XZ trước khi khóa movement; operation truyền
Direction xuống VFX, VFX chiếu lên XZ và không đọc movement hoặc tự tìm target.

```csharp
Vector3 aim = enemy.transform.position - transform.position;
var action = CreateAction(skillId, out var context, aim);
action.Start(context);
action.Perform(context);
// Caller owns completion, cancellation and disposal.
```

EN: Skills choose and snapshot world-space aim through CreateAction. Direction
is optional for generic actions, finite/nonzero when supplied, and normalized
without quantization. Slash requires planar aim; visuals do not consult movement.

Action có thể dùng protected `CreateOperation(context, id)`. Operation có Execute,
Cancel và Dispose; chỉ sử dụng thông qua Action. Action cụ thể quyết định cách tick/chờ
kết quả của thao tác kéo dài, sở hữu và dọn Operation khi kết thúc hoặc lỗi giữa chừng.
Chưa có runner, scheduler hoặc giao thức hoàn tất chung cho Operation.

Skill cụ thể phải giữ Action/context của lần thi triển, hủy/dọn Action còn sống khi
disable/unload, và Dispose sau Exit/Cancel, kể cả khi hook ném lỗi. PNSkill nền chỉ giữ
dữ liệu/registry; nó không giữ các Action trả cho caller và không tự gọi Cancel khi Unload.
Không giữ context cũ để chạy sau khi entity unload hoặc session kết thúc.

## Giới hạn chủ động

Tags tùy chọn nằm trong payload mỗi hook, ví dụ perform.tags. PNSkillActionData có
StartTags/PerformTags/ExitTags/CancelTags read-only, khử trùng và kiểm tra ID đã đăng ký
qua hub.Tags khi nạp bằng loader hoặc tạo context. Không yêu cầu value; action chuyển
dạng có thể chỉ mang formId và tags. Xem Core/Content/Registry/PNTagREADME.md.

Đã có input PNPlayerSkill; đòn đánh của Eve là EveSweepAction (slash cũ đã bỏ ở RulesVersion 159).
PNSkillValue đã hỗ trợ raw hoặc phần trăm stat, đọc riêng bởi action cần giá trị.
PNDamageAction đăng ký project_n:damage_action, chụp value/bonus tại Perform và gửi
đến Targets tường minh trong context; chưa có hitbox. Overload CreateAction nhận danh
sách PNStat targets tùy chọn. Xem Value/PNSkillValueREADME.md.
Chưa có cooldown, action buff, AI/graph, Editor/Blueprint hoặc cú pháp
JSON khai báo Action/Operation mới. Mod dữ liệu chỉ chọn implementation đã được module code đăng ký.

## English

Core parses the engine payload into PNSkillData: a flexible skill payload and one
action ID with Start/Perform/Exit/Cancel payloads. Null skill data remains supported.
Envelopes are validated, hook payloads are copied and interpreted by concrete classes.
Core provides empty execution registries; Game's PNGameplayContentModule registers
concrete actions and operations from Assets/Game. Each Create returns
a fresh instance. The skill drives transitions, actions own their operations. Callers
must cancel/dispose live invocations during teardown. No gameplay runner or editor is
implemented. See the Vietnamese contract above for loading and ownership details.

Optional per-hook tags are parsed into read-only, deduplicated metadata and validated
through hub.Tags at loader/context boundaries. No value is required for actions.

PNSkillValue provides opt-in raw/stat scaling. Game PNDamageAction evaluates at
Perform and uses one outgoing packet for explicit context targets. No hitbox is
implemented; see Value/PNSkillValueREADME.md.
# Registered categories, invocation and effects (RulesVersion46)

## Category damage tags (RulesVersion64 / Mod API4)

PNSkillCategoryDefinition optionally links one registered combat tag through
DamageTagId. The original two-argument constructor remains available and creates
an unmapped category. The three-argument constructor accepts the optional link.
Default attack/skill/ultimate/dash/burst categories link respectively to
base:attack/skill/ultimate/dash/burst;
Canonical tags are project_n:attack/skill/ultimate/dash/burst. Stat ATK is
base:attack_power (project_n:attack_power). Old attack stat IDs and the five
category *_damage tag IDs have no compatibility aliases.
Public constants are PNContentIds.AttackPower / PNContentModule.AttackPower
for the stat; AttackTag/SkillTag/UltimateTag/DashTag/BurstTag expose the tags.
Recompile code mods for API4.
These are tags, not stats or damage types.

Effective Perform tags combine authored tags, linked tags of effective categories
and matching addTags, then apply suppressTags last. All IDs are canonicalized,
deduplicated and sorted. Suppressing a category removes its automatic tag grant;
the same tag can still remain from authored/addTags sources. suppressTags removes
it from all sources. Multiple categories can share one tag. Distinct tags use the
existing modifier grouping rules; category bonuses do not introduce another formula.

Mapping follows existing BeginAction Snapshot and per-hit Live contribution capture.
Auxiliary contexts use their inherited frozen categories without replaying the
parent contribution chain. Contexts without categories infer none. ReceiveHit
reclassifies from raw amount and captured source calculation, preserving the original
crit roll. The final receiver packet/events contain these tags for incoming Mitigation.
Custom Actions must use EffectiveTags/ReceiveHit to participate in this pipeline.
DOTs do not automatically inherit the applying Skill's categories/tags.

Content startup validates every category link after registration: tag must exist
and its referenced/canonical ID must not also resolve as a stat or damage type.
Errors identify the category and link; no silent deletion or automatic tag creation.
Default category/tag public bindings remain locked. Mods register their own
PNTagDefinition and PNSkillCategoryDefinition in code before seal, then refer to
the category from Skill categoryIds. No new JSON category/tag registration schema
is provided. Category links participate in compatibility hashes.

Example: register my_mod:charged_damage as PNTagDefinition, then register
my_mod:charged with permission base:skill_skill and DamageTagId
my_mod:charged_damage. Source Scale group1 +0.2 on that tag increases outgoing
damage20%; receiver Mitigation uses the same final tag. Buff modifier ownership and
Attack Speed flags retain their existing contracts.

Activation definitions require root categoryIds (an ID array; empty is allowed) and
affectedByAttackSpeed (explicit bool). Registry skill_categories has locked public
base:attack/skill/ultimate/dash/burst aliases; each definition maps a registered
Control permission. Code modules may add categories before seal. Category is not an
input button or an Action implementation. Category, tags and buffs use separate
registries. Internal profiles still carry Action tuning; metadata-only activation
definitions can omit action. Legacy typed/null Action data is not inferred into
activation metadata; use an explicit JSON-backed definition for activation.

```json
{
  "categoryIds": ["base:attack", "base:skill"],
  "affectedByAttackSpeed": true,
  "effects": [
    {
      "trigger": "OnHit", "kind": "Operation", "id": "base:apply_buff",
      "data": {"buffId": "base:burn", "input": {"stacks": 2}},
      "target": "HitTarget", "missing": "Skip", "hitScope": "PerTarget"
    }
  ]
}
```

CanActivateSkill(skillId) prepares one entity-local invocation, captures Snapshot
contributions and computes effective categories once. It checks ALL mapped rights,
skill_all and exact canonical entry ID, and checks positive Attack Speed for flagged
definitions before resource commits. An empty effective category set is allowed: such
a skill bypasses every category right (for example a silence that removes Skill/Attack/
Dash) but is still gated by skill_all and exact-skill leases, and receives no
category-linked damage tag. Dynamic category add/suppress still changes which category
rights are checked, but suppressing every category no longer blocks activation by
itself; use a Control lease to block. No separate admission list. The old two-argument CanActivateSkill overload
delegates to definition-based admission, not its legacy category argument.

Skill logic calls BeginActivation after its preconditions, then CommitActivation
after successful execution/resource commit. Only commit emits OnActivated, once.
Failed startup cancels/ends through normal owned cleanup. Action context carries
EntrySkillId, InvocationId and InvocationGeneration (IDs are local to the load
generation), categories and optional Position. Main logic ends the invocation with
normal completion or cancellation. Custom logic must follow this contract; trusted
mod code is not intercepted globally.

Affected execution timing reads LIVE Attack Speed; categories never select speed.
Only one owner scales the timeline (Eve orchestrator/shared Dash, not each child).
Nonpositive speed is zero: flagged activation rejects, running timeline pauses but
can still cancel. Motion distance stays fixed; cooldowns/DOT clocks do not scale.
Authored Eve Attack/Repeat flags true; Skill/Dash/Ultimate/Burst flags false. Burst
Ultimate finishers remain unaffected. Auxiliary effects have their own explicit
speed flag, never infer it from inherited categories.

## Effect contribution contract

Effects.Contribute(owner, new PNSkillContribution(json, registry)) returns an
independent disposable lease. JSON is cloned/validated; each grant copies the value
so distinct owners never collapse merely because they share an input object.
Buff context.ModifySkill owns that lease through its resource scope. Source Buff
suspend/end returns its lease; resume reacquires without firing activation. Skill
disable cancels executions but retains external contributions; entity unload ends
state/leases and reload creates another generation. Source death cancels auxiliary
execution; independent lifetime here means independent of parent invocation, not
independent of source entity/session. No projectile lifetime model is introduced.

```json
{
  "readMode": "Snapshot", "categoryIds": ["base:attack"], "match": "Any",
  "effects": [
    {"trigger": "OnHit", "kind": "Operation", "id": "base:apply_buff",
     "data": {"buffId": "base:burn"}, "target": "HitTarget"}
  ]
}
```

readMode must be Snapshot or Live. Optional skillIds filters exact ENTRY IDs;
categoryIds/match Any or All filter EFFECT/tag contributions. One entry executes
once even if several categories match; distinct entries/owners execute separately.
addCategories/suppressCategories modifiers filter only by exact skill IDs (or all),
preventing circular category conditions. Resolve categories first, then conditional
addTags/suppressTags, then effects. Tags resolve in combat tags; suppress wins.

Snapshot for activation is taken during preparation; Action/hit Snapshot is taken
at each actual BeginAction. Later Actions take new snapshots. Removing the granting
Buff does not revoke already-captured data or keep its lease alive. Live is captured
once per trigger; ReceiveHit freezes contributions/tags before ReceiveDamage and
executes OnHit effects afterward, even if callbacks revoke an owner. A dead target
still reaches normal Buff admission rejection; contact does not require positive
damage. Valid collider contacts are selected/deduplicated by the hit implementation.
Live tag changes create/reclassify the hit packet with captured source calculation
and the original crit roll; they never mutate a prior packet or change the existing
outgoing-stat snapshot contract. Invalid effective damage type rejects before HP.

Built-in triggers: OnActivated; OnActionStart; OnHit; OnComplete/OnCancel (Action);
OnInvocationComplete/OnInvocationCancel (whole activation). Metadata triggerIds may
declare custom phase triggers. Custom-trigger contributions require exact skillIds
and support in those selected definitions. Custom Actions call BeginAction,
CompleteAction/CancelEffects and ReceiveHit/Fire explicitly; effects do not fabricate
a hit for a Buff-only skill. No authored effect entry is needed to emit a trigger.

Effect fields:
- trigger, kind (Operation/Action), id and registered data; factory creation always
  honors the selected registry implementation. Definition payload validators and
  required-context flags reject unsupported selectors before execution.
- target: None/Self/InvocationTargets/HitTarget; position: None/Self/Target/Hit;
  direction: None/Invocation/ToTarget. These independent selectors can coexist.
  Missing context skips by default; missing=Error reports a failure. Never fallback
  to self. Position/direction per target execute separately when required.
- hitScope: PerTarget default, or PerHit. Action supplies hitIndex (default0 for a
  single-hit Action); PerHit self effects run once for that group, across targets.
- Action data contains start/perform/exit/cancel. ownership must be Parent or
  Independent, with explicit affectedByAttackSpeed. Timed Actions declare
  HasAuxiliaryTimeline/AdvanceAuxiliary; instant Actions exit after Perform.

Stable order: entry-authored effects, profile-authored effects, Snapshot contributions,
then current Live contributions, preserving entry/grant order. Auxiliary context
inherits categories captured at creation and has no contribution-chain execution by
default. It has no extra OnActivated and does not recheck main skill admission.
Trigger recursion is bounded at8; failures report through Failed/logging without
rolling back committed damage/resources or prior independent effects. Parent end,
disable, death, unload and session teardown return owned Actions/Operations/locks.
No resource/clock advancement from a Buff callback is implied.

Registered buff_skill_effect_template demonstrates Attack OnHit -> Burn, duration5,
Game clock, Snapshot. It is not attached to authored gameplay. The factory payload
is duration/clock/contribution; derive a registered profile for another rule.


## Registered area creation (RulesVersion54)

`base:area_action` executes selected `base:create_area` using perform
`{operationId,data:{areaId,input,followCaster}}`. Both require Position context.
Area input is validated by its selected definition. Action/Operation disposal does
not end the manager-owned region; the returned receipt/handle controls that lifetime.
Use [PNAreaREADME](../../../Area/PNAreaREADME.md) for pooling, authority, JSON and
owner/source/follow contracts. Generic Buff Action intentionally remains Buff-only.

## Context without a live skill, source-independent Operations (projectile P0-C)

`PNSkillActionContext` has two virtual accessors: `Source` (a `PNCombatSource`) and `World` (`EveWorldContext`). A
context built with a live `PNSkill` behaves exactly as before: `Source` captures the skill entity's stat on each read
(it throws when that stat is not ready or is dead, like the former direct capture) and `World` is that entity's world.
A subclass may use the new `protected` constructor (no `PNSkill`; `Skill == null`, no invocation, no contributions) and
override `Source`/`World` to carry a frozen source, e.g. a projectile after its shooter died or unloaded.
`PNWorldGameplay.RequireTargets(EveWorldContext world, targets)` validates targets for such a context: they must be in
that world (null = no world), executable, and the world must be runnable; the source entity is not consulted.

`PNSkillOperationDefinition.SourceIndependent` marks an Operation that reads attribution/world only from the context.
Registered `true`: `apply_buff`, `remove_buff`, `bypass_remove_buff`, `trigger_buff`, `change_buff_stacks`.
Everything else (heal, slash hit, create area, motion, VFX) still needs a live skill and must not be run from a
detached context. The migrated Operations keep their former behavior for skill contexts; `trigger_buff` reads only
the caster identity (no combat capture) so a dead caster still works. The check that a frozen plan only runs
`SourceIndependent` Operations belongs to the code that executes it (P0-B).

## Frozen OnHit plan (projectile P0-B)

`PNSkillEffectState.FreezeOnHit(context)` fixes, at launch time, every effect an `OnHit` would run: authored effects of the
entry skill and of the profile, Snapshot contributions, and the Live contributions as they are at that moment. It uses the same
gathering code as `Fire` (one private helper), so the frozen set equals what `Fire("OnHit")` would run now. Call it after
`BeginAction` of a context that allows contributions, while the invocation is alive and the session is authoritative; otherwise it
throws. The result `PNSkillHitSnapshot` keeps no skill, invocation or source entity: `EntrySkillId`, `Categories` (may be empty),
`Tags`, `Effects` (OnHit, Operation kind, definition `SourceIndependent`) and `Skipped` (Action kind, or an Operation that needs
the live skill). Only `OnHit` is supported; other triggers are the Operation's own business.

`Execute(hitContext, target, point, direction, hitIndex, admission)` runs the frozen effects for one hit and returns a
`PNSkillHitReport` (`Executed`, `Skipped`, `Errors`); it never throws for a failing effect and does not undo committed damage.
`hitContext` is the caller's detached context (supplies `Source`, `World`, `Registry`, `Data`). Selectors: target `HitTarget|None`,
position `Hit|Target|None`, direction `Invocation` (the `direction` argument) `|ToTarget` (target position minus `point`) `|None`;
`Self` and `InvocationTargets` follow the effect's `missing` policy (Skip = counted as skipped, Error = reported). `hitScope: PerHit`
effects run once per hit index per `PNSkillHitAdmission` (one admission per projectile); `PerTarget` effects are not deduplicated
here. The plan only runs in the session it was frozen in (otherwise a failure is reported), and nested execution is limited to
depth 8 like `Fire`.


## T5.1 Action run producer (Rules170, Network28)

A2 diagnostics add a `seal` measurement event before CaptureHolds, so test recorders
can reconstruct holds for refused runs too. No subscribers means no diagnostic
payload allocation. This is not a network event. A2 found constant-rate input can
still create numeric micro-holds/cuts; see Docs/NetworkTimeT5A2Report.md and C0
in the plan before using current anchor density to size a new history protocol.

T5.1b A/B follow-up: terminal charge now shrinks at first Finish to exact UTF8
row bytes plus one delimiter; entity/session release surplus once and retire only
the remaining charge. Whole terminal rows, including revision/Cut/history, are
immutable in successor validation. This supersedes the whole-lifetime reservation
description below for completed runs; active reservation remains conservative.
`PNActionRunStore.Measurement` is opt-in, unbuffered diagnostics before admission/
cut, including refused-run driver/finish attempts. Subscribers own cleanup;
recorder exceptions are isolated. No network message or gameplay policy changes.
See Docs/NetworkTimeT5_1bABReport.md for evidence and remaining capacity limits.

`PNSkillActionContext.ActionOrdinal` is allocated eagerly once at BeginAction, independently of lazy hit reporting and cast.Random. One context describes one run; duplicate BeginAction is idempotent and does not authorize sharing that context between Action instances. A completed/cancelled context cannot publish another start. Ordinal exhaustion returns0 and refuses presentation admission, without changing gameplay/hit/RNG. A positive CastKey uses the input namespace; an unkeyed invocation uses `-Invocation.Id`, scoped to the same life. Invalid/wrapped cast keys are not admitted.

`PNSkill.BeginActionExecutionFrame(delta)` is the driver contract. `Advance(context, step, speed, callback)` forwards exactly the already selected gameplay step; the engine maps the consumed driver budget to unscaled PNTimeService stamps and captures actual supplied progress. `Hold(context)` records zero progress/rate. It does not reread AttackSpeed or apply entity/global scale again. Eve and the framework auxiliary driver use it; order, decisions and authority gameplay clocks remain unchanged. Selected definitions opt into `WithDriverProgress()` only when that progress-unit contract is supported. Currently the registered Eve sweep is audited; this does not declare safe visual seek or enable arbitrary observer Actions.

`PNActionRunStore` is owned by Skill/life, not the Sync binding. Same-world/life rebind preserves keys and retained terminals; world/incarnation/generation changes discard the old scope. Unload and session cleanup release byte reservations. Start definition/context is immutable; first terminal wins. Full snapshots carry active admitted runs and retained terminals. Missing gameplay runs can have been refused admission: completeness is only for the admitted presentation set.

Limits are4096B block,4active,16slots including terminals,16anchors/run,3s minimum terminal retention,256KiB charged across a play. Admission reserves UTF8 definition IDs plus worst-case numeric history and terminal before accepting the run. Active holds have no TTL; pressure refuses new presentation only. Counters distinguish bytes/slots/active/session pressure. The transitional legacy event rows reserve1024B and envelope512B, so the current conservative reservation fits only ONE typical sweep run, despite higher count ceilings. This is an explicit capacity limit to resolve before replacing the legacy observer; it is not a guarantee of16deliverable Actions.

Rate anchors keep revisions, effective stamps, progress and rate; the image also holds actual progress-at-capture. Capacity or a same-stamp progress correction increments `Cut` and replaces history with a coherent anchor. A future consumer must recover/rebase at that cut, never interpolate through missing history. No delivery guarantee, ACK/recovery scheduler or visual seeking is implemented at T5.1.

Wire: `project_n:skill_cast` registration/root schema2,4096B; legacy `n/e` retained temporarily and new `a` envelope with schema1 (`v,e,w,n,i,g,t,r,f,b,d,a`). Epoch/world/incarnation/entity/generation are exact; `f=true` means full admitted set; `b=0` explicitly means ordinary image, NOT requested recovery baseline. Rows hold cast/ordinal, canonical entry/data/selected Action IDs, capability1, planar direction, start/sample/progress/rate, terminal kind/stamp, cut/revision/history. Reason kinds are Active/Completed/Cancelled; resource tail disposal is not a terminal event. Parser checks finite numbers, bounds, order, IDs, duplicate keys and immutable successor/terminal semantics. Transport revision rejects stale duplicates. T5.2 owns stream subscription nonce, applied-watermark ACK and requested baseline semantics; T5.3 owns the consumer. The validated received rows are shadow state and do not execute Actions.

Checks: `PNActionRunContractChecks`, `EveCastViewChecks`, existing owner/input/prediction/chain/report suites, golden trace, smoke; `--pn-action-time --pn-action-producer` logs actual sweep elapsed and producer/received records in a Player. Historical PreT5 traces remain Before evidence, not the new schema's acceptance. See Docs/NetworkTimeT5_1Report.md for actual runs and limitations.
## C0 driver interval (2026-11-17)

The execution frame supplies `AdvanceInterval(key, begin, end, step, rate)` to the
run store. Adjacent Unity frames share the previous exact source end; skipped
frames retain their gap. Source placement uses that interval, while effective rate
still uses the supplied gameplay delta/raw ratio. A step equal to the remaining
float gameplay budget consumes the exact frame tail for timestamp mapping only.
Gameplay callbacks receive the original step unchanged. Explicit gaps, including
very short holds, are retained without a blanket epsilon.

The older `Advance(key, stamp, step, rate)` infers its interval and is retained for
compatibility/low-level tests; it cannot distinguish float rounding from an intended
gap. New framework drivers use explicit intervals. Diagnostic driver events include
`begin`; replay accepts older traces without it. This adds no wire field or consumer.

### C3 publication shadow (accepted shadow, 2026-11-17)

PNActionPublication is an opt-in engine service source, independent of legacy
PNActionRunStore admission. PNSkill.ActionPublicationEnabled defaults false; engine
fixtures may enable it, and disabling disposes registered sources. Characters,
Actions and Operations use the same gameplay path. Begin/driver/finish/Seal hooks
preserve actual progress and isolate shadow failures from gameplay. No new consumer
or Action transport is active. See Docs/NetworkTimeC3Report.md for accepted C3 workload and limits. ReadRequired/
ReadLatest do not imply submission: Commit*Submitted confirms bounded local queue
acceptance with a matching generation. Remote apply ACK remains T5.2. These interfaces
are additive experimental APIs, not a final SDK/wire compatibility promise. Initial retrospective intervals can require R at
original activation stamp; do not shift activation to remove those corrections.


### T5.2-A authoritative recovery (2026-11-17)

PNSkill owns a bounded PNActionPublicationRecovery live census before legacy
admission, independent of publication success. RecoverActionPublication(stamp,
originalDeadline) runs only after execution frames; it builds complete current
state with original keys/context/start and actual driver progress under a new
source GUID. It invents no past lifecycle/history. Resource staging failure is
Pending with a fixed deadline; incomplete census/invariant/expiry is Failed.
Scope/session cleanup releases census and candidate resources. A does not change
readiness or send a baseline: Coop B/C must pump recovery and gate on actual apply.
See Docs/NetworkTimeT5_2Report.md for evidence, budgets and remaining work.


### T5.2-C production adapter (2026-11-17)

PNNetworkManager owns Action publication preparation and network subscription
lifecycle. PrepareActionPublication(stamp, originalDeadline) can establish a
complete empty authority source before the first attack, or use A's full census
for a rebuild after drivers. Character/Action authors do not invoke this API.
PublicationCut seals at the supplied unscaled capture stamp and allocates
an owned capture revision without copying a full baseline. Coop queues, actual
baseline ACK, readiness, shared budgets and recovery are documented in
Docs/NetworkTimeT5_2Report.md. This does not enable observed seek/render.
### T5.2-D verification and allocation paths

Parallel drivers on one entity reuse the exact Unity-frame start boundary. Opening
another execution frame in that same Unity frame must not recompute its start by
subtracting a rounded float delta. Scope retirement clears this cache.

Publication records retain their compatible JSON view. Required records also expose
`ToArray()`, a fresh mutable copy of frozen primitives; mutating that copy cannot
change journal history. Latest samples freeze timestamp/progress/rate/history and
dependency values, with a lazy JSON view. Typed transports build arrays from these
values without stringify/parse round trips. Read/Commit and enqueue-before-Commit
contracts remain unchanged. Budgets are logical charges, not CLR heap measurements.

The sender fills the existing byte budget (up to256 required records, binary chunk
still3584 bytes); it no longer limits an otherwise valid batch to8 records. The
receiver validates each record once and prunes retention once per atomic batch.
Its candidate copies touched runs and changed anchor lists; immutable identity,
context and numeric anchors may be shared. A failed later record rolls back the
whole candidate. Effective history coverage is the run floor combined with the
decoder's global retention floor; future seek consumers must respect both.

`PNActionDeliveryPlayChecks` is an opt-in Player fixture. Registered inert Actions
start inside the framework frame, use actual Advance callbacks and multiple cast
keys, and keep the legacy drawing consumer. `--pn-action-delivery-quiet` buffers
fixed-size metric structs; `--pn-action-delivery-control` disables hot diagnostic
callbacks; `--pn-action-delivery-without-service` is a matched performance control
configured before admission on every peer. It is not delivery evidence. The
`--pn-action-delivery-boundary` supplement tests synthetic maximum-field baselines,
not registered Action capacity. Report/manifests in Docs/NetworkTimeT5_2Report.md
and `.utmp/NetworkTime/T5_2/D` decide acceptance; fixture assertion PASS alone does
not establish every latency/allocation target or presentation/Android acceptance.


## T5.2-D1 transport views and receiver validation

Required exposes frozen read-only scalar getters for infrastructure encoders (RecordKind,RunKey,RunRevision,RunHistoryGeneration,selected IDs,direction/start,record time/progress/rate,reset/terminal kind). Json and fresh-copy ToArray remain compatible. Authors still use engine services; no character/Action/Operation network branches. This additive view preserves author data/callbacks and ModAPI8; SDK is re-exported.

Coop encodes required/latest directly with unchanged binary grammar/caps and enqueue-before-Commit. Homogeneous U decodes to bounded scalar rows before any apply; mixed rows retain full decoder. COW/needN/floors/full-packet rollback and baseline actual-apply ACK remain. Incremental logical charges and canonical duplicate text are checked against independent enumeration/original JSON formatting; logical bytes are not physical heap.

D1 initial PC matrix improves CPU/mean allocation, but client peak allocation,120ms apply-age and host-pending evidence still block acceptance. Legacy draws; seek/render/cutover remains T5.3. Current evidence: Docs/NetworkTimeT5_2Report.md and .utmp/NetworkTime/T5_2/D1/Candidates/Canonical.

## Shared combat preparation (O1, Rules172)

`PNSkillActionContext.PrepareDamage` prepares one explicit-target damage packet
and a combat snapshot for hit-time tag reclassification. It evaluates authored
values and uses the live Stat damage factory in Authority, preserving its
overrides, crit roll and iframe policy. `PNDamageAction` checks the whole target
list before preparation, deduplicates entity IDs and reuses the packet across
the list. It marks the action spent before delivering hits.

The melee hitbox service uses `PrepareMeleeDamage` to capture geometry scale and
prepare damage from the same snapshot. It rolls one packet when the shape opens.
The owner supplies its last prepared scale. Presentation uses the scale frozen
on the run. Both damage paths pass the packet and the capture to `context.Hit`.
Authority applies damage and dispatches `OnHitConfirmed`. Predicted reports a
target when the cast has a key and sink. Observed skips gameplay delivery.

## World work and motion (O1, Rules173)

The grid Work Action delegates placement changes to the grid work service. The
service requires Authority and a runnable authority world. The registered
payload still determines position offset, footprint distance, work type, power
and amount. The Action retains its `Worked` and `Broken` counts.

Planar motion advances the Action timeline separately from the Move pose writer.
A writable local body or predicted owner acquires the physics lease when ready.
The remote-owner host keeps its execution registration and authority control
lease, and advances time without writing pose or interpolation. An observed
Action advances without execution registration or body changes. Cancel, Exit
and invalidation release the active leases. The context and Move role select
execution (the old `IsPrediction` property was removed).

The locomotion Action advances its authored duration in all modes. Authority
pushes and releases traversal state. The owner mirrors effective locomotion
through Move, and predicted/observed runs skip the push.

## Spawned-object operations and entity factors (O1, Rules174)

The four registered create Operations validate their payloads and delegate
spawn admission and requests to `PNSkillObjectSpawn`. Managers retain their
receipts, caps, replacement, pooling and dependency rules. Predicted and
Observed skip instance creation in O1. The summon adapter returns the
existing `Predicted` status to the cast behavior without making a candidate.

`project_n:skill_scales` is a schema-1 Entity state for the existing player
skill-sync module. It publishes the current scale/pacing factors selected
from declared tags and Action presentation tags. It uses the world,
incarnation and membership scope and the Entity transport life. Sampling
uses a 125 ms unscaled interval, with a new publication only when values or
scope change. Missing data reads as 1. The seven built-in factors are
projectile speed, scale and lifetime, melee scale, area scale, blast scale,
and summon scale. Owners and observers use the same state. Enemy enrollment
belongs to O7.

Eve sweep prediction now reads this state. The Action run continues to
carry the frozen scale captured by the authority. Launches continue to use
authoritative snapshots. Network32 gates the new state and retirement of
the former owner-image field `m`.

## Cast lifecycle: how a cast ends (G3a, G9; 2026-11-25)

One cast is one `PNSkillInvocation`. Whatever stops it, it ends through one place, `PNSkill.FinishInvocation`:
what the cast registered (`invocation.Track`) is cancelled last-created-first, the activation trigger fires
(`OnInvocationComplete` or `OnInvocationCancel`), then the skill's `OnCastEnded(invocation, cancelled, reason)` runs
exactly once with the cast memory still readable, and the invocation is disposed. Put the reset of a state machine in
`OnCastEnded` (rule L6); do not start another cast from it. `CancelCast(reason)` is the public way in;
`EndActivation(cancelled, reason)` is the character's own way to end a cast it ran to the end.

`reason` is a `PNCastEnd` constant (`Interrupted`, `Died`, `Disabled`, `Unloaded`, `Rejected`, `Replaced`,
`Executions`, `Disconnected`) or, when the skill's own `TryInterrupt(id)` ended the cast (Eve), that interrupt id
(`project_n:world_transfer`, `project_n:binding_changed`, `project_n:disconnected`). Do not branch on the exact string
except to tell "finished" from "cancelled"; use `cancelled`.

Who ends a running cast, and when (all on the machine with gameplay authority):

| Event | Entry point | What ends | Reason |
| --- | --- | --- | --- |
| The owner dies | `PNSkillEffectState.SourceDied` | the cast and its effect executions (`CancelExecutions(PNCastEnd.Died)`) | `died` |
| The owner's machine goes into grace (paused, stalled, resuming) | `PNNetworkManager.Interrupt(peer)` -> `PNSkill.CancelForGrace()` | the running cast only; what it handed over (a flying projectile, a Buff) keeps running | `project_n:disconnected` for Eve, else `disconnected` |
| The owner moves to another world | `PNWorldGameplayTransferParticipant` -> `PNSkill.CancelWorldExecutions()` | the cast, binding inputs, effect executions | `project_n:world_transfer` for Eve |
| A binding is changed with `CancelCurrent` | `PNSkillBindingRuntime.ApplyBindingChange` -> `CancelBindingExecution` | the cast that runs through that slot | `project_n:binding_changed` for Eve |
| The skill is disabled or unloaded | `OnDisable`, `Unload` | the cast and its executions | `disabled`, `unloaded` |
| The owner's machine guessed a cast the authority refused | `RollbackPredictedCast` | the guess and what it started | `project_n:prediction_refused` for Eve, else `interrupted` |

A character does not write any of this: the death and grace paths need no code in the character. Eve's own death
handler (`OnEveDied`) only resets state the cast does not own (a Dash guess, the held gesture, the energy visual, her
Abilities); ending the cast was already done by the framework, with the same reason.

Evidence: Edit Mode `EveCastCancelChecks` (every Eve cast stopped at every frame by interrupt, by `CancelCast`, by
`CancelForGrace` and by the owner's death), `PNOwnerDeathCastChecks` (an enemy and a summon-like owner die mid-cast).
Player (host + two clients, `runskill.ps1 -Scenario Room -Character project_n:eve`): `--pn-object-sync-grace`,
`--pn-object-sync-binding`, `--pn-object-sync-world` (client1's Ultimate ends with the grace gate, the binding change
and the world transfer, and the entity casts again afterwards); `--pn-late-join` for a client that joins under way.

## Shared value store: world and session keys (G5, Rules176, Network45; 2026-11-25)

State that is not one entity's lives in a keyed store, not in a field and not in Ability (Ability is per entity;
declaring a key with `scope: "entity"` is refused with that message). Two scopes, both synced from the authority
(Authority / Predicted / Observed as for everything else), declared once in content:

```json
"extensions": { "project_n": { "sharedValues": [
  { "id": "pack:gate_open", "scope": "world",   "kind": "flag",   "initial": false },
  { "id": "pack:wave",      "scope": "world",   "kind": "number", "initial": 0, "min": 0, "max": 9, "persist": true },
  { "id": "pack:gold",      "scope": "session", "kind": "number", "initial": 0, "persist": true },
  { "id": "pack:seen",      "scope": "session", "kind": "list",   "item": "text", "initial": [] } ] } }
```

`kind` is `number` (a double; `min`/`max` optional), `flag`, `text` (at most 128 characters) or `list` (at most 16
entries, all of one `item` kind, never a list). A scope holds at most 64 keys and its changed values must fit one
4096-byte sync block; the first is checked when content loads, the second on every write (`TooLarge`). `persist`
defaults to false (K11): a key that does not persist is never written to a save or checkpoint.

From a skill, Action or Operation (logic only, no network branch, rule L1): `SetShared(id, value)` and
`GetShared(id)` on `PNSkillAction`. A write is applied only on the machine with gameplay authority and only for an
`Authority` cast; a `Predicted` or `Observed` cast gets `Ignored` (K12), and every machine's `Changed` event fires for
each value that changed, so reacting to a change is the same code everywhere. The write result is a
`PNSharedWrite` (`Applied`, `Unchanged`, `Ignored`, `Unknown`, `WrongScope`, `Invalid`, `TooLarge`); nothing is
written for any but `Applied`. Do not read a value back in the same frame to decide a hit (L2): read it on the next
step or react to `Changed`.

Where it lives: the session store is one block (`project_n:shared_session`, Session target); each world instance
that has world keys gets its own block (`project_n:shared_world`, WorldObject target, reserved slot `$shared`, key
`values`), made when the world is ready (or first used, because a checkpoint restore fills it before the world is
announced) and dropped with the world. Two worlds alive at once, including two copies of one map, never share a
value, and a client holds a world's store only while it has that world loaded (late join and world change use the
ordinary per-target baseline). `AddSlot` refuses a content slot id that starts with `$`.

Saving: a run save (schema 3) carries `shared`: the session keys that persist and differ from initial; an older
schema file is obsolete and deleted as before (D3). A world's persistent keys travel in its checkpoint through the
engine provider `project_n:shared_world_state`, which `PNSharedWorldStateProvider.WorldProviders` adds to the world's
provider list when content declares any persistent world key and the session has the shared value service (a fixture session without the service gets nothing added), so a world policy does not have to name it. A restore
is all or nothing and refuses a save that names an undeclared key, a key that does not persist, a key of the other
scope or a value outside its declaration.

Code: `Assets/Core/Sync/PNSharedValues.cs` (definition, store, facade), `PNSharedValueContent.cs` (JSON),
`Assets/Game/Sync/PNSharedValueService.cs` (session and world glue, checkpoint provider). Sample keys:
`BaseContent/shared/shared_sample.json` (used by the Player scenario).
