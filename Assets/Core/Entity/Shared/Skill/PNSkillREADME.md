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

Đã có input PNPlayerSkill và prototype Eve slash: PNSlashAction ghép khóa di chuyển
với VFX mới, không damage. Xem Assets/VFX/PNArcSlash/PNArcSlashREADME.md.
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
