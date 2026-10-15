# Chỉnh prefab và timeline của slash

Combat `action.perform` accepts optional finite signed `iframeDuration`; omission uses
receiver fallback (enemy .2s/player 1s), explicit zero leaves the timer unchanged.
Negative values extend active iframe; positive values consume it. Damage during iframe
is multiplied by .3, including the hit that breaks its timer. This field is registered
action data, not a prefab/VFX parameter. See Core/Entity/Shared/Combat/PNCombatREADME.md.

Prefab chiêu: `Assets/Resources/PNAttacks/EveSlash.prefab`.
Root có `PNSlashPrefab`, child `Visual` dùng prefab VFX, child `Hitbox` có
`EveHitboxTrack`. Có thể thêm nhiều child Hitbox; mỗi đòn chỉ damage một lần/entity
dù các vùng chồng nhau. `PNVfx/PNArcSlash.prefab` vẫn là VFX dùng lại riêng.

## Chỉnh trong Unity

1. Mở EveSlash bằng Prefab Mode, bật Gizmos trong Scene.
2. Chọn root hoặc child Hitbox, kéo **Preview Time** từ 0 đến 1 để xem VFX và hình học cùng thời điểm.
   Preview không dò collider hoặc gây damage. Đây là tiến độ chuẩn hóa, không phải giây.
3. Chọn child Hitbox. Inspector và các tay nắm Scene cùng sửa `Data.Geometry`,
   `Offset`, `Euler`; hỗ trợ Undo. Các kích thước theo world unit, không dùng
   Transform scale để phóng hitbox. Nếu preview scale bằng 0 thì không có handle;
   hãy kéo thời gian sang phần hình đã mở.
   **Layer Mask** là field trực tiếp của script EveHitboxTrack, chọn được nhiều
   layer. Chỉ collider trên các layer đã chọn được quét (layer của GameObject
   chứa collider, kể cả collider ở child). Nothing không quét, Everything quét
   mọi layer; mặc định Everything giữ hành vi hiện tại. Phe/Ready vẫn lọc sau đó.
4. Chọn Kind: Box, Sphere, Capsule, Cylinder, Sector, Ellipse, Polygon hoặc Cone.
   Đòn phẳng dùng Sector/Ellipse/Polygon/Box có Height/Size.Y nhỏ và dương;
   khối dùng độ dày lớn, Sphere, Capsule hoặc Cone. Capsule cần Height >= 2*Radius.
5. Chỉnh curve Radial (XZ), Axial (độ dày/chiều dài), Angular (góc sector),
   OffsetX/Y/Z và Pitch/Yaw/Roll. Curve cộng vào Offset/Euler gốc.
   Enable From/To giới hạn hoạt động của từng vùng trong tiến độ 0..1.
6. Khi Play, chọn root chiêu đang active hoặc child Hitbox để xem hình thực tế, gồm thay đổi
   riêng của cast bằng code. Xanh lá: track và damage window cùng mở; xám: đóng.
   Nhân vật không vẽ Gizmo hitbox; Gizmo chỉ nằm trên prefab/instance chiêu.

Slash mặc định mở bán kính từ 20% lên 100% tại tiến độ .22 rồi lên 112% ở cuối.
Góc mở từ 1% lên 100% tại .22. Outer arc của VFX dùng cùng Radius/Angular/pose
với track đầu tiên. Vệt trong, màu, fade và sparks là trang trí; Gizmo thể hiện
vùng damage. Renderer PNArcSlashVisual là hiệu ứng vòng cung, không tự đổi thành
mesh Box/Cone khi đổi Kind; cần VFX phù hợp nếu tạo một loại đòn khác.

## Dữ liệu skill và runtime

EveCharacter.json chỉ giữ prefab Resources path `PNAttacks/EveSlash`, duration .32s,
damage/tags và hitbox.windowStart .06 / windowEnd .20s, triggers, followCaster,
sampleInterval .004s. Geometry và Layer Mask thuộc script Hitbox; khai các trường
này trong JSON cùng prefab bị từ chối. Payload legacy không có prefab vẫn dùng mask JSON.
Ví dụ preview .35 tương ứng .112s. Cửa sổ damage thực tế còn phụ thuộc JSON của skill.

Action chụp aim/damage lúc Perform. VFX và hitbox lấy mẫu cùng clock EveTimeScale.
Mỗi Advance scan các mốc .004s và biên window/track, kể cả frame vượt cả window.
followCaster=true nội suy vị trí caster giữa hai frame cho hitbox và di chuyển
VFX theo caster; aim gốc vẫn giữ. Delta=0 không phát sinh hit mới.

Đây là lấy mẫu theo thời gian, không phải continuous collision detection:
collider mục tiêu dùng vị trí physics hiện tại, không tái dựng đường đi của mục tiêu.
Đòn quá nhanh/vùng quá mỏng có thể cần sampleInterval nhỏ hơn. Cấu hình giới hạn
4096 bước/window và 64 track. Hình mesh đổi kích thước phải cook lại probe; chưa
profile player build hoặc nhiều chiêu chạy đồng thời.

## Chỉnh bằng code/mod

`PNSlashPrefab.Capture()` clone geometry, points và mọi curve cho từng invocation.
`PNArcSlashVisual.Tracks` giữ các bản sao dùng chung với hit operation.
Action kế thừa `PNSlashAction` có thể override trước scan đầu tiên:

```csharp
protected override void ConfigureAttack(PNArcSlashVisual visual)
{
    var hitbox = visual.Tracks[0];
    hitbox.Geometry.Radius *= 1.5f;
    hitbox.Yaw = UnityEngine.AnimationCurve.Linear(0f, -20f, 1f, 40f);
}
```

Cũng có thể đổi các field/curve của bản sao khi đang chạy; hình được lấy mẫu lại
mà không reset lịch đã trúng. Chỉ thay danh sách track trong ConfigureAttack,
trước Bind/Execute; sau đó sửa nội dung track hiện có. Không chỉnh asset Resources.
Đặt mask của component bằng `hitbox.LayerMask = LayerMask.GetMask("Enemy", "Projectile");`.
Đổi riêng đòn đang chạy bằng `visual.Tracks[0].LayerMask = LayerMask.GetMask("Enemy");`.
Đổi component áp dụng khi Capture lần tới; bản sao đang chạy độc lập với component.
`PNHitboxCast.SetShape()` hỗ trợ chiêu tự tạo hình bằng code và giữ history.
Payload cũ không có prefab tiếp tục dùng VFX/sector JSON cũ.

Bootstrap kiểm tra prefab/curve; runtime kiểm tra các mẫu thực sự và hủy action nếu
cấu hình sai. Hash gameplay gồm geometry/curve/anchor prefab để coop phát hiện khác
nội dung, gồm cả Layer Mask. RulesVersion `project_n:22`. SDK DLL API2 đã export lại trong `.utmp/ModSdk`.

Kiểm chứng: PASS11 prefab preview, PASS46 combat thật (gồm quét giữa frame, nhiều
track và isolation), PASS82 hình học, PASS708 smoke + PASS25 DLL. Preview/PhysX
chạy trong scene tạm; không lưu hoặc sửa scene gameplay. Chưa kiểm chứng player
build/performance hay damage replication qua mạng.

Luồng stat/damage nay dùng double: ởHP200M,10 đòn damage10 trừ đúng100HP và còn
199999900. Inspector/popup dùng EveStatNumber (tối đa2 số lẻ), PASS25 formatting
checks. Crit được chụp/roll một lần cho mỗi đòn; các mục tiêu chung packet dùng
chung kết quả. Có thể thêm perform.canCrit=false trong JSON để tắt crit riêng đòn.
Xem Core/Entity/Shared/Combat/PNCriticalDamageREADME.md.
