# First slash prototype

**2026-10-03:** VFX now uses golden energy (generated alpha ribbon texture), five
HDR crescent layers and12 sparks. Eve supplies right/down, left/up and horizontal
per-hit tracks; empowered/Repeat/Ulti enlarge both hitbox and VFX. See
`Assets/Game/Entity/Character/Eve/EveMovesetREADME.md` and
`Assets/Resources/PNVfx/EveEnergy/README.md`. Cyan/white prototype details below are
historical. Preview: `.utmp/EveGoldenEnergy.png`; shader/texture/URP render verified.

**Cập nhật:** Eve hiện dùng prefab chiêu `Assets/Resources/PNAttacks/EveSlash.prefab`
chứa VFX và Hitbox timeline. Hướng dẫn hiện hành:
`Assets/Game/Skill/Actions/Slash/PNSlashAuthoringREADME.md`. Geometry chỉnh trên
prefab bằng Inspector/Scene handles; JSON giữ damage/window/duration/prefab path.
RulesVersion project_n:20. Phần dưới ghi lại prototype VFX trước khi chuyển sang prefab.

Trong ErMode, Tap **J** hoặc Gamepad **buttonEast** để Eve chém một lần. Không có
combo/cooldown. Action đã có hitbox/damage riêng; VFX vẫn không xử lý combat.
Xem Assets/Game/Skill/Actions/Slash/PNSlashCombatREADME.md. Input Exit không kết thúc Action; EveCharacterSkill
Update chờ VFX đủ thời gian rồi gọi Action.Exit.

Luồng: `EveCharacterSkill` -> `PNSlashAction` -> `PNLockMovementOperation` và
`PNSlashVfxOperation` -> `PNArcSlashVisual`; operation `base:slash_hit` chạy song song.

PNGameplayContentModule trong Assets/Game/Content/Registry đăng ký Action/Operation.
Các triển khai nằm ở Assets/Game/Skill/Actions và Assets/Game/Skill/Operations;
Eve nằm ở Assets/Game/Entity/Character/Eve. EveCharacter.json khai báo skill
`project_n:eve_slash`, dùng `base:slash_action` và dữ liệu bốn hook. Perform
hiện duration=0.32, radius=0.6, height=0.18, forwardOffset=0.15. Sửa JSON để chỉnh VFX.
Thời gian chiêu theo EveTimeScale nếu component đang hoạt động, nếu không dùng Time.deltaTime.

Prefab mới: `Assets/Resources/PNVfx/PNArcSlash.prefab`, cùng material mới.
EveCharacterSkill tạo EveEasyPooling riêng khi cần và giữ qua nhiều lần Attack/disable.
PNSlashVfxOperation lấy pool qua PNSlashVfxPoolOwner nên không phụ thuộc lớp Eve cụ thể;
skill enemy có thể cung cấp pool riêng bằng cùng interface. Spawn lấy GameObject inactive,
đặt vị trí/hướng/radius và Render(0), rồi bật. Exit/Cancel trả instance về pool; mesh được
giữ lại và tái sử dụng. Unload/destroy hủy pool cùng toàn bộ VFX/mesh. RulesVersion project_n:19.
Mesh runtime có ba vệt vòng cung cyan/trắng/tím và tám tia nhỏ, shader URP additive
mới; không dùng tài nguyên chém cũ. Skill truyền vector world-space liên tục qua
`PNSkillActionContext.Direction` -> Action -> Operation -> Spawn. VFX chiếu hướng
lên XZ, dùng cho góc và forwardOffset, không tự đọc PNMove hoặc tìm mục tiêu.
Hướng 25 độ thì chém đúng 25 độ, không làm tròn 4/8 hướng. Mỗi context chụp hướng
riêng, đổi aim hoặc movement không xoay lại VFX đã tạo.
Input Eve gọi `TryAttack()`: skill chọn `PNMove.LastMoveDirection` rồi truyền xuống.
Đứng yên giữ hướng cuối. `TryAttack(direction)` cho phép override từng lần;
`AttackDirection` là fallback khi không có PNMove. Chưa có target/lock-on enemy.
`PNMove.LastMoveDirection` dùng chung cho dash/các skill cần hướng di chuyển;
chỉ skill quyết định có dùng nó, VFX không tự đọc. Không có animation hoặc collider.

EN: Skill supplies continuous world-space aim through the invocation context.
Slash projects it onto XZ without cardinal snapping. VFX never reads movement;
active casts retain their captured aim. Eve's no-argument attack selects last
movement direction in the skill; idle retains facing. Explicit TryAttack(direction)
overrides a cast. AttackDirection is only a no-movement fallback. No targeting yet.

Start khóa CanMove/CanFlip/CanJump và PNPlayerMove.CanInput; tắt đi ngang ngay.
Exit/Cancel/Dispose gỡ VFX và khôi phục quyền trước khi chém. Skill hủy khi disable,
destroy, unload hoặc entity không còn Ready. Nếu tạo VFX lỗi, khóa được hoàn tác.
Gravity/vận tốc Y khi đã ở trên không vẫn thuộc physics bình thường; không tạo time-stop.
Lock này chụp quyền trước chiêu; chưa phải hệ token tổng quát để phối hợp nhiều nguồn
khóa đồng thời thay đổi quyền trong khi đang chém.

Smoke PASS 615 checks, gồm input chém chọn hướng movement ở skill, context chụp/chuẩn hóa
vector, dữ liệu sai và các kiểm tra hướng di chuyển dùng riêng cho dash.
`PNSlashDirectionEditorChecks.Run()` kiểm tra mesh/pool thật tại các góc bất kỳ,
forwardOffset trên XZ và góc cố định trong một lần chém ở preview scene dùng tạm.
`RunSkillFacing()` kiểm tra cả luồng `TryAttack()` không tham số qua skill/action
tới VFX thật ở 25/155/-72 độ, idle giữ hướng và unload dọn pool.
Kiểm tra pooling của chiêu dùng stub chỉ cho renderer VFX. Unity import/compile và render
shader/mesh thật đã kiểm tra. `Assets/Tests/EveEngine/Editor/EveSlashPlayModeChecks.cs` có bài
test tường minh Begin() khi Play Mode đang chạy: bàn phím tạm QueueStateEvent J,
kiểm chứng khóa/không chồng/Exit/Cancel/HP, cùng object và mesh được dùng lại, unload hủy
pool/mesh qua frame thật. Timestamp input cố định Tap 60ms tránh Editor stall; khôi phục input
settings và background mode rồi thoát Play Mode. Không tự chạy khi chơi game.

Editor scene đang dirty trước khi triển khai; không lưu scene đó. Bản sao dự phòng
trước Play test ở `.utmp/ErMode-before-slash-playtest.unity`. Test integration PASS
ghi ở `.utmp/EveSlashIntegrationResult.txt`.
