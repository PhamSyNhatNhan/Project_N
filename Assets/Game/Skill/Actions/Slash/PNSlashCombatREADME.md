# Slash combat / Combat chém

RulesVersion41: value accepts a single object or nonempty component array. Slash
explicitly sums caster-based components before CreateDamage once; each component's
amount must be nonnegative. Use PNSlashHitSettings.Values for array-aware consumers;
the legacy Value accessor requires exactly one component. See
`Assets/Core/Entity/Shared/Skill/Value/PNSkillValueREADME.md`.

**Cập nhật hiện hành:** xem `PNSlashAuthoringREADME.md`. Eve dùng prefab chiêu
`PNAttacks/EveSlash` với hitbox timeline, geometry không còn ở JSON. Temporal
substeps mặc định .004s, nhiều track chung hit history, followCaster di chuyển cả
VFX. Layer Mask nằm trên script Hitbox. Native combat hiện PASS46 + prefab preview PASS11. Phần dưới là cấu hình
sector JSON cũ, vẫn được hỗ trợ cho payload không khai prefab.

Eve input J/Gamepad East gọi skill chọn hướng, action khóa movement, tạo VFX và
operation `base:slash_hit`. Hit operation dùng Sector -> PNHitboxTargets ->
PNStat.ReceiveDamage. VFX không tự dò collider hoặc quyết định aim.

Cấu hình hiện tại nằm trong
`Assets/StreamingAssets/BaseContent/characters/EveCharacter.json`, action.perform:

```json
{
  "tags": ["base:physical"],
  "duration": 0.32, "radius": 0.6, "height": 0.18, "forwardOffset": 0.15,
  "value": { "amount": 100, "statId": "base:attack_power" },
  "hitbox": {
    "radius": 0.6, "innerRadius": 0, "angle": 100, "thickness": 0.4,
    "forwardOffset": 0.15, "verticalOffset": 0.18,
    "windowStart": 0.06, "windowEnd": 0.20,
    "targetLayers": ["Projectile"], "followCaster": false, "hitTriggers": false
  }
}
```

| Thông số | Ý nghĩa |
| --- | --- |
| value.amount/statId | 100% Attack Evaluate lúc Perform; bỏ statId để dùng amount thô |
| perform.tags | Đúng một damage type; bản Eve là physical |
| perform.duration/radius/height/forwardOffset | Thông số VFX hiện có |
| hitbox.radius/innerRadius/angle/thickness | Sector XZ, độ dày Y; giữa góc theo hướng skill |
| hitbox.forwardOffset/verticalOffset | Offset theo hướng chém và world Y |
| hitbox.windowStart/windowEnd | Giây kể từ Perform, trong duration; Start < End |
| hitbox.targetLayers | Tùy chọn: mảng tên layer có thứ tự, được **thêm** vào `TargetLayers` của người chém (ví dụ `["Projectile"]`); vắng = chỉ `TargetLayers` của người chém. Truy vấn collider luôn dò mọi layer rồi lọc theo layer của entity (`layerMask` cũ bị từ chối) |
| hitbox.hitTriggers | false bỏ trigger, true gồm trigger |
| hitbox.followCaster | false giữ vị trí lúc Perform; true theo vị trí caster nhưng giữ aim lúc Perform |

Với followCaster=true chỉ hitbox theo vị trí; VFX hiện vẫn ở vị trí lúc Perform.
Mỗi cast trúng một lần/entity dù có nhiều collider hoặc scan nhiều frame. Mục tiêu
đi vào trong hit window vẫn được đánh. Đồng hồ Advance lấy EveTimeScale nếu đang
hoạt động, nếu không dùng Time.deltaTime; delta0 không dò hit mới. Frame nhảy qua
cả window vẫn scan một lần ở vị trí physics hiện tại, không sweep/quay ngược thời gian.

Damage và outgoing bonus chụp một lần lúc Perform; buff sau đó không đổi damage
của cast đang chạy. DEF/mitigation của receiver áp tại thời điểm nhận. Results và
Damage có thể đọc từ PNSlashAction sau Dispose để kiểm tra kết quả.

Không còn phe: một entity bị trúng khi layer của nó nằm trong `TargetLayers` của người chém
(mặc định PNPlayerStat → Enemy, PNEnemyStat → Player, PNStat → không layer nào; có thể ghi đè bằng
`combat.targetLayers` trong dữ liệu character và Buff chồng thêm layer bằng lease) hoặc trong
`hitbox.targetLayers`; layer của người chém nằm trong tập thì trúng đồng minh nhưng không trúng chính nó.
Operation có CanHit virtual. Self, chết, inactive, chưa Ready, khác session và collider không thuộc entity đều bị lọc.

Host/offline mới tạo/query cast và áp damage. Client chạy VFX/lock nhưng không tự
gây damage; hệ transport/replication damage chưa được thêm trong thay đổi này.

Cancel/Exit/disable/unload dọn cast, trả VFX, gỡ khóa. Receiver lỗi hủy cast và
không retry các hit đã commit. Hit được đánh dấu trước delivery; callback hủy
action được xử lý bằng cleanup hoãn đến khi delivery hiện tại trả về, không tiếp
tục đánh receiver sau. Dead caster không bắt đầu cast mới; skill dừng cast khi chết.

Payload thiếu cả value/hitbox vẫn là slash VFX-only để tương thích dữ liệu cũ.
Nếu khai một trường phải khai cả hai; cấu hình sai, value âm, stat/tag/dependency
không hợp lệ hoặc hit window vượt duration bị chặn trước công bố registry.
Operation đăng ký `project_n:slash_hit` và public binding `base:slash_hit`; override
phải kế thừa PNSlashHitOperation theo hợp đồng hiện có.

Kiểm chứng: PNSlashCombatEditorChecks.Run(), PASS20 checks với JSON Eve, pool VFX,
PhysX và HP Dummy thật trong preview scene. Dummy HP giữ nguyên200M; fixture dùng
Attack1024 để kiểm tra HP loss biểu diễn chính xác ở độ phân giải float này.
PASS82 native hình học và PASS680 smoke + PASS25 DLL. Smoke runner thay operation
PhysX bằng stub không gây damage; combat không được chứng minh bằng stub.
Chưa kiểm chứng build player/Play Mode performance hoặc network replication.

EN: Slash now has a data-defined sector hit window and once-per-entity damage.
Skill supplies captured aim, Perform snapshots damage, Core resolves entity/target-layer
eligibility, and host/offline applies HP changes. Visuals remain independent.
