# EveTimeScale

## English

Optional entity clock: attach `EveTimeScale` only where the game uses local time. It works without a loader; when an `EveEntityLoader` is present it participates in Load/Unload. It never changes Unity's global time scale, Animator, Stat, Skill or Rigidbody directly. Missing/disabled clocks resolve to rate 1 in the Core movement adapter.

Each insertion creates a unique modifier ID, including repeated insertions from the same `EveId` source. `Scale` values are signed additive deltas (-0.3 reduces the rate by 0.3; +0.5 increases it by 0.5). `MinScaleReduction` values are nonnegative and reduce the protection floor. They are independent entries with independent lifetimes in the same map.

```text
EffectiveMinScale = Max(0, MinScale - sum(MinScaleReduction))
RawScale          = 1 + sum(Scale)
Scale             = Max(EffectiveMinScale, RawScale)
```

`MinScale` defaults to 0 and accepts [0, 1]. Effective Scale can exceed 1; zero means time has stopped. Excess negative contributions remain in the map. A configured floor of 1, a reduction of 0.7 and a delta of -0.8 produce Scale 0.3. Expiring the floor reduction restores the original floor without deleting the masked delta.

`AddTimedModifier` takes a strictly positive duration in **game seconds**, before the entity's local scale. Unity time scale 2 makes five game seconds take roughly 2.5 real seconds; global pause suspends expiry. Local scale zero does not suspend modifier expiry. Update drives expiry once per rendered frame; do not also call `Advance` unless overriding Update to supply the clock yourself.

`AddOwnedModifier` requires an active Component or GameObject and has no countdown. Remove its ID or source when the owning effect ends. Inactive, disabled or destroyed owners are pruned on the next clock update. Disable, Unload and `ClearModifiers` clear all entries. IDs are never reused within a component's lifetime. Modifiers are not automatically saved across scene transfers. `Modifiers` returns immutable snapshot entries; `ScaleChanged` reports effective-rate changes with listener exception isolation.

```csharp
var clock = GetComponent<EveTimeScale>();
var source = EveId.Parse("project_n:boss/time_field");
long rate = clock.AddTimedModifier(source, EveTimeModifierKind.Scale, -0.8f, 7f);
long penetration = clock.AddTimedModifier(source, EveTimeModifierKind.MinScaleReduction, 0.7f, 5f);
// Remove one entry early, or remove every entry from this source:
clock.RemoveModifier(penetration);
clock.RemoveSource(source);
```

Core integration: `PNMove : EveMove3D` discovers the optional clock during Load. `PNPlayerMove` configures its floor from `minTimeScale` (default 1); `PNEnemyMove` uses the attached clock's floor (default 0). ErMode's Eve has a clock with floor 1. Pure EveMove variants have no dependency on EveTimeScale; the virtual `ResolveTimeScale` hook defaults to 1. Normal and special movement velocities scale once, and command timers use locally scaled fixed time. Zero freezes commands, including an immediate teleport requested while stopped. Full movement lock/disable still cancels commands as before.

PNMove scales vertical velocity and compensates gravity by scale squared, preserving the intended free-flight arc at different rates. At zero it saves motion and constraints, disables gravity and freezes all Rigidbody constraints; on resume it restores settings and motion at the current rate. It owns these temporary physics settings while frozen; other systems should not overwrite them. This is a movement adapter, not a separate PhysX simulation per entity: collision impulses, damping and external force producers still use Unity's shared simulation. Unity Play Mode collision validation remains necessary.

Animation is deliberately not connected yet; the future Core Visual system will own it. Buff/debuff duration integration is future work and should use entity time where intended. Cooldown clock policy is deferred until Skill implementation. No network replication is introduced; authoritative modifier operations and replication will be designed with Skill/Move coop integration.

## Tiếng Việt

`EveTimeScale` là đồng hồ tùy chọn của entity, mặc định MinScale = 0. Khi gắn loader thì tham gia Load/Unload, cũng dùng độc lập được. Component không tự sửa thời gian Unity, Animator, Stat, Skill hay Rigidbody. Phần nối nằm trong Core; thiếu hoặc tắt đồng hồ thì PNMove dùng hệ số 1.

Map giữ từng modifier bằng ID riêng, cùng nguồn vẫn cộng dồn. Loại `Scale` dùng giá trị có dấu; loại `MinScaleReduction` dùng lượng giảm ngưỡng không âm. Hai loại dùng chung cấu trúc nhưng là entry độc lập, không bắt buộc cùng thời hạn. Công thức phía trên tính sàn hiệu lực trước, rồi chặn kết quả Scale theo sàn. Hệ số có thể lớn hơn 1, bằng 0 nghĩa là dừng; mọi đóng góp vượt sàn vẫn được giữ để tính lại khi từng entry hết hạn.

Modifier có hạn đếm theo giây game, chịu Unity time scale nhưng không chịu hệ số entity. Vì vậy entity bị dừng vẫn có thể hết hiệu ứng, còn pause toàn game thì bộ đếm dừng. Update cập nhật bộ đếm; không gọi Advance thêm nếu chưa ghi đè Update. Modifier không thời hạn cần owner đang hoạt động, được gỡ khi owner bị tắt/hủy hoặc gỡ chủ động theo ID/nguồn. Disable/Unload/reset xóa toàn bộ. Không tái sử dụng ID và không tự lưu modifier qua scene.

`PNMove` nối thời gian vào vận tốc, bộ đếm lệnh, nhảy và rơi. `PNPlayerMove` có `minTimeScale` mặc định 1; quái dùng MinScale của component. Eve trong ErMode đã gắn component với MinScale = 1. Engine chỉ có điểm mở hệ số, không yêu cầu TimeScale. Khi hệ số 0, lệnh đang chạy được giữ, vận tốc được lưu và Rigidbody khóa toàn bộ constraints/tắt gravity; khi tiếp tục thì khôi phục trạng thái. Disable/Unload khôi phục cấu hình vật lý và hủy lệnh.

Gravity được bù theo bình phương hệ số để giữ quỹ đạo nhảy trong chuyển động tự do. PhysX vẫn mô phỏng chung: va chạm, damping và lực do hệ thống khác tạo chưa có đồng hồ riêng. Không sửa cấu hình Rigidbody từ hệ thống khác trong lúc PNMove đang giữ trạng thái dừng. Cần kiểm chứng va chạm bằng Unity Play Mode.

Chưa nối animation; Visual trong Core sẽ đảm nhiệm sau. Đồng hồ cooldown để đến Skill mới chốt. Thời hạn buff/debuff sẽ nối đồng hồ entity khi triển khai hệ thống đó. Chưa bổ sung đồng bộ modifier qua mạng.
