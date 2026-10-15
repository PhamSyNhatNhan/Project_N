# Eve

**Current:** Eve's full melee moveset is implemented. See `EveMovesetREADME.md`
for combo, Skill, Dash, Burst/stack rewards, Ulti finisher, input and tuning.
The prototype-only descriptions below are historical. No UI/projectile added.

## Tiếng Việt

Nhân vật Eve của bản game mới nằm tại `Assets/Game/Entity/Character/Eve`.
`EveCharacterStat : PNPlayerStat` và `EveCharacterSkill : PNPlayerSkill` được nạp
qua `EveEntityLoader` với định nghĩa `project_n:eve`. Script và thư mục được chuyển
từ EveTest, giữ nguyên GUID để scene ErMode tiếp tục tham chiếu.

ErMode dùng Rigidbody/Collider 3D, sprite và `PNPlayerMove` để thử di chuyển 2.5D.
Space/Gamepad North nhảy; Tap J/Gamepad buttonEast gọi đòn chém với hitbox Sector
và damage100% Attack physical trong window0.06..0.20s. Chưa có combo/cooldown.
Chi tiết cấu hình: Assets/Game/Skill/Actions/Slash/PNSlashCombatREADME.md.
Đòn chém hiện tại được tính là đánh thường: mỗi frame nhân thời gian action với
`base:attack_speed / 100`, sau khi áp EveTimeScale. 50% mất gấp đôi thời gian,
200% mất nửa thời gian. VFX và hitbox dùng chung timeline nên cửa sổ trúng đòn,
kết thúc đòn và mở khóa di chuyển cùng đổi nhịp; damage/Ability timer không đổi.
Thay đổi stat giữa đòn áp ngay cho phần còn lại. Tốc độ0 không cho bắt đầu đòn mới;
đòn đang chạy tạm dừng đến khi tốc độ tăng lại hoặc bị hủy theo lifecycle hiện có.
Skill không được phân loại đánh thường sẽ không tự hưởng ATK Speed.
Skill sở hữu Action đang chạy và pool VFX riêng;
Exit/Cancel trả VFX về pool, disable hủy Action, Unload/destroy dọn cả pool.

Input chém gọi `TryAttack()`: skill đọc `PNMove.LastMoveDirection` và truyền hướng
qua context. Đứng yên giữ hướng di chuyển cuối, không làm tròn 4/8 hướng.
`TryAttack(Vector3 direction)` vẫn cho phép truyền hướng riêng cho từng lần.
`AttackDirection` chỉ làm fallback khi không có PNMove. VFX không tự đọc movement;
chưa có target hoặc tự tìm enemy.

Các triển khai dùng chung nằm tại `Assets/Game/Skill/Actions` và `Assets/Game/Skill/Operations`.
`PNGameplayContentModule` đăng ký chúng; Core giữ các hợp đồng Skill/Action/Operation
và `PNContentModule` đăng ký chỉ số cùng registry nền. Runtime nạp cả hai module.

Dữ liệu Eve tiếp tục nằm ở `Assets/StreamingAssets/BaseContent/characters/EveCharacter.json`
để loader tìm dưới BaseContent. HP bắt đầu 40/100, Attack 10, Defense 5,
move speed 3, jump speed 3.2. VFX/shader và prefab/material vẫn ở
`Assets/VFX/PNArcSlash` và `Assets/Resources/PNVfx`.

Kiểm thử Unity nằm riêng trong `Assets/Tests`; smoke harness nằm ngoài Assets
tại `Tests/EveEngine`. Xem README VFX để biết vòng đời và cách kiểm thử đòn chém.

## English

Eve lives under `Assets/Game/Entity/Character/Eve`. Its Stat and Skill derive from
Core's player classes and load through `EveEntityLoader` using `project_n:eve`.
Original asset GUIDs are preserved, including existing ErMode script references.

ErMode tests 2.5D movement with 3D physics. Space/Gamepad North jumps;
Tap J/Gamepad buttonEast starts a sector slash with a data-defined hit window and
100% Attack physical damage to hostile entities. The Skill owns its Action and
reusable VFX pool, cancels on disable, and disposes the pool on unload/destruction.
Combo/cooldown are not implemented. See PNSlashCombatREADME for configuration.

Concrete shared implementations live in `Assets/Game/Skill/Actions` and
`Assets/Game/Skill/Operations`, registered by `PNGameplayContentModule`. Core retains
Skill/Action/Operation contracts and the stat/empty execution registries provided
by `PNContentModule`; the runtime loads both modules.

Character JSON remains under `StreamingAssets/BaseContent` for content discovery.
VFX code/shader and resource prefab/material retain their existing locations.
Unity verification lives in `Assets/Tests`; standalone smoke checks live in `Tests/EveEngine`.
