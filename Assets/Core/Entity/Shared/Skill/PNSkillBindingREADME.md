# Skill binding override — Rules75 / Mod API6

Slot giữ binding; Skill sở hữu input, targeting, phases, cost, category và speed policy.
Attack slot không thêm category Attack. Mirror Skill commands/replication chưa được thêm.

## Content

Registries: skill_behaviors, skill_binding_profiles, skill_base_bindings.
PNSkillBehaviorDefinition chứa Id/ImplementationId/factory/validators. Factory trả behavior
mới, chỉ chuẩn bị, không chạy effect. Helper logic không bắt buộc đăng ký. Profile chứa
EntrySkillId metadata, BehaviorId, Data/Input immutable, ActionDataIds, Abilities,
DisplayAbilityIds và ResetAbilitiesOnDeath. Không đặt runtime state trong definition.

Behavior gọi context.TryBegin trước cost; CreateAction dùng registry; Commit/Complete dùng
invocation hiện có. Advance nhận entity-scaled delta; behavior chọn clock và áp speed theo
metadata khi drive Action. Ability manager tick passive state một lần. Snapshot/Live và
Parent/Independent auxiliary giữ hợp đồng hiện tại.

SelectionChanged và context.SelectionResources dành cho subscription/effect chỉ sống khi
được chọn; deselect trả scope này trong khi cast FinishCurrent vẫn giữ owner riêng. SlotId
được cung cấp trong context; Resources giữ tài nguyên thuộc toàn lifetime behavior.

Mod không cần EveCharacterSkill. PNPlayerSkill có Dash fallback. Subclass chọn character
mapping bằng UsesCharacterBindings=true. Eve dùng adapter với moveset context chung.

Legacy shared-player Dash profile dành cho shared Dash implementation; Eve dùng profile
binding_eve_dash. Factory từ chối nhầm capability trước khi đổi binding. Template Buff
phải derive với profile phù hợp target, không mặc định mọi legacy profile dùng trên mọi actor.

DefaultDashBindingProfile là virtual fallback cho character chưa có base mapping. Eve
override để giữ Eve Dash cho lớp kế thừa; ô không được quản giữ legacy hooks/Repeat.

JSON schemaVersion1, extensions.project_n.skillBindingProfiles là array các object:
id, entrySkillId, behaviorId, data, input, actionDataIds, abilities, displayAbilityIds,
resetAbilitiesOnDeath. Input dùng tên CanTap/PressImmediately/HoldMode/TapDuration/
HoldDuration/SlowTapDuration/RepeatDelay/RepeatInterval. Unknown fields/types bị từ chối.
skillBaseBindings là array {characterId,slots:{"project_n:slot_attack":"addon:profile"}}.
ID thuộc namespace pack, foreign reference cần dependency. JSON không chỉ CLR class;
factory được code đăng ký. Validation hoàn tất trước bootstrap publication; skill aliases
tạo ở FinalizeContent được kiểm lại với document dependencies trước publication.

## Lease, input và state

AcquireOverride(owner,new PNSkillOverrideRequest(slotProfilePairs,priority,apply,release))
trả receipt/Lease. Một request nhiều ô publish atomically. Own lease ngay; check Status/Error;
Pending thì theo Completed. Priority cao thắng, bằng thì grant sequence mới hơn. Dispose
idempotent, gỡ đúng nguồn. Không chain SkillId hoặc sửa registry đã seal.

Queue256/drain64 chặn cycle; exhaustion fault resolver, retire leases, CancelCurrent,
complete pending receipts với lỗi, từ chối slot input đến reload. Observer lỗi không retry.
Behavior transient theo profile+slot; exact calls có context riêng. Ability cache theo entity,
chia sẻ khi cùng ID/schema/death policy; hidden cooldown vẫn tick, re-equip không reset.
Selected Ability tạo trước input đầu; HUD chỉ đọc selected profiles và state entity-wide.

RequestSlot resolve Started và pin gesture; RequestExact gọi profile cụ thể. Bool trả về
là dispatch, không phải cam kết activation. Legacy public TryAttack/TrySkill/TryUltimate/
TryDash/TryBurst giữ Skill cụ thể. Input dùng slot; phases giữ cast, repeat mới kiểm binding.

Đổi binding canceled về owner cũ, không performed giả; neutral trước input mới, chỉ refresh
action ô đổi. FinishCurrent mặc định cho committed cast tự chạy; charge chưa commit hủy
theo cancellation. CancelCurrent là opt-in cho grant/release có origin slot liên quan.
Không hoàn tiền cost đã commit; một main execution theo actor. Dash giữ luật interruption.

Eve empowerment chỉ Eve attack consume; replacement không tự hưởng/tiêu. Timer vẫn chạy;
restore còn hạn có thể dùng. Thay ô không unload moveset/reset Burst/gauge/passive khác.
Eve death reset đúng state Eve, profile mới theo policy riêng.

Template base:buff_skill_binding_template nhận duration, slot, profileId, priority,
applyTransition/releaseTransition (FinishCurrent/CancelCurrent). Derive qua registry.
Lease thuộc Buff resource scope: suspend trả, resume cấp lại không cast. Pending grant lỗi
kết thúc Buff RuntimeError. FinishCurrent giữ cast scope, không kéo dài toàn Buff scope.

Behavior/Action/Operation cleanup trên complete/cancel/disable/death/unload/failure.
Unload trả cache/gesture/locks/subscriptions; cleanup lỗi propagate tới entity lifecycle.
