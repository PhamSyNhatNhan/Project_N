# Hitbox

VI: Hitbox mới dùng Physics 3D cho cả vùng phẳng có độ dày và khối. Dò giao hình
collider, không so tâm entity. Không dùng Physics2D và không tự gây damage.

EN: Both thin regions and volumes use 3D collider intersections. Detectors do not
apply damage or choose aim. Legacy Physics2D hitboxes remain unchanged.

## Shapes / Hình dạng

| Factory | Kích thước và trục / Dimensions and axes |
| --- | --- |
| Box(size) | Kích thước đầy đủ XYZ; Y nhỏ tạo vùng chữ nhật phẳng |
| Sphere(radius) | Khối cầu, không phải circle phẳng |
| Capsule(radius, height) | Trục Y; height tính cả hai nắp, ít nhất 2 × radius |
| Cylinder(radius, height, segments=48) | Trục Y; height nhỏ tạo circle phẳng |
| Sector(outerRadius, thickness, angle, innerRadius=0, segments=48) | XZ, giữa góc theo +X; góc độ 0..360, Y là độ dày |
| Ellipse(radiusX, radiusZ, thickness, segments=48) | Bán trục X/Z, extrude theo Y; không phải ellipsoid |
| Polygon(points, thickness) | Vector2(x,y) ánh xạ X/Z; đường bao đơn lồi hoặc lõm, extrude theo Y |
| Cone(baseRadius, length, segments=48) | Đỉnh ở pose origin, đáy tại +X × length, bán kính đáy trong YZ |

Mọi kích thước phải hữu hạn, dương; thickness không được bằng 0. Sector hỗ trợ
góc lớn hơn 180°, vòng đầy 360° và lỗ giữa. Pose Quaternion cho phép nghiêng vùng
phẳng hoặc khối theo bất kỳ hướng 3D nào. `EveHitboxPose.Planar(position, direction)`
dùng hướng liên tục trên XZ từ skill: 25° giữ đúng 25°. Không đọc PNMove/Visual.

Cylinder/Sector/Ellipse/Cone dùng mesh tessellation với 8..64 segments trên một vòng.
Đường tròn là xấp xỉ bằng dây cung: tăng segments giảm sai số. Với bán kính R và
bước góc Δ, sai lệch dây cung tối đa là R × (1 − cos(Δ/2)); viền lỗ trong cũng
là dây cung. Không xem đây là đường tròn toán học tuyệt đối. Sphere/Capsule/Box
dùng overlap primitive của PhysX. Non-convex mesh target chịu giới hạn triangle/
backface của Unity ComputePenetration; collider primitive/convex phù hợp cho hurtbox.

Polygon nhận 3..64 điểm theo thứ tự quanh đường bao; không lặp điểm đầu để đóng.
CW hoặc CCW đều được, input được copy và Points chỉ đọc. Không hỗ trợ lỗ polygon,
đường bao tự cắt/chạm, điểm trùng hoặc ba điểm liền kề thẳng hàng. Kiểm tra dùng
tolerance tương đối với kích thước đường bao để từ chối hình gần suy biến.
Polygon lõm chia bằng ear clipping rồi extrude từng tam giác; không biến đường bao
thành convex hull. Broadphase giữ đúng tâm/kích thước đường bao lệch khỏi origin.
Cone là khối nón hữu hạn có nắp đáy, không phải sector cầu hay tia vô hạn.

```csharp
var ellipse = EveHitboxShape.Ellipse(1.5f, .5f, .2f);
var cone = EveHitboxShape.Cone(1f, 2f); // aim along pose's local +X
var polygon = EveHitboxShape.Polygon(new[] {
    new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, .6f),
    new Vector2(.6f, .6f), new Vector2(.6f, 2f), new Vector2(0f, 2f)
}, .2f); // L-shaped region; notch stays empty
// All shapes use the same EveHitboxQuery / PNHitboxCast API.
```

## Layer ownership / Phân lớp

- Engine: `EveHitboxShape`, `EveHitboxPose`, `EveHitboxQuery`; trả Collider.
- Core: `PNHitboxTargets`; collider con tìm entity gần nhất ở cha, không vượt qua
  entity lồng nhau, gộp theo EntityId. Mặc định bỏ self, chết, inactive, chưa Ready,
  chưa đăng ký hoặc khác phiên. Layer/trigger và predicate lọc phe do caller cấp.
- Game: `PNHitboxCast`; mỗi invocation giữ lịch trúng riêng. Không truyền delay
  nghĩa là một lần/cast; delay dương cho trúng lặp. Action/projectile quyết định
  lúc scan, hit window, vị trí có theo caster hay không, clock và damage.

Không có auto-target, cooldown hoặc lịch hit toàn cục. `base:` vẫn là API registry
cho stat/action; hình học không đăng ký definition dưới namespace game.

## Usage / Cách gọi

So với `detectObject(layerMask, index)` cũ: giữ một query hoặc cast cho invocation,
truyền pose rõ ràng và list output tái dùng, rồi Dispose khi Exit/Cancel/unload.

```csharp
// Create once in action Start; angle/dimensions are examples, not Eve defaults.
cast = new PNHitboxCast(EveHitboxShape.Sector(1.2f, .2f, 100f, .1f));
direction = context.Direction.Value; // capture once; skill chooses aim

// Scan only while the action's hit window is open.
var pose = EveHitboxPose.Planar(caster.transform.position, direction);
cast.Query(pose, elapsed, targets, caster, hurtboxMask,
    QueryTriggerInteraction.Ignore, filter: IsEnemy);
var damage = caster.CreateDamage(amount, tags);
foreach (var target in targets)
{
    target.ReceiveDamage(damage);
    cast.CommitHit(target, elapsed); // consume after success, not merely querying
}

// Exit / Cancel / partial Start failure:
cast?.Dispose();
```

`targets` là `List<PNStat>` riêng của caller. Commit dùng target của query mới nhất
và đúng timestamp query; query không ghi nhận hit. Clock phải hữu hạn, không âm,
không lùi; dùng thời gian action đã áp dụng time scale nếu muốn repeat theo game.
Thiết kế xử lý damage/callback lỗi và dừng cast thuộc action, không tự retry damage
đã commit. Cast không được tái dùng qua session khác.

Nếu chỉ cần collider, dùng `EveHitboxQuery.Query(pose, List<Collider>, mask, triggers)`.
Nếu cần entity nhưng tự quản lý hit history, dùng `PNHitboxTargets.Query`.
Output được thay mới mỗi scan, buffer tự tăng khi đầy, entity sort theo EntityId.
Query/resolver/cast có state và không được gọi reentrant hoặc từ worker thread.

Constructor nhận `PhysicsScene` tùy chọn để dùng local physics. Query không tự gọi
SyncTransforms/Simulate toàn cục. Nếu caller vừa sửa Transform và cần dò ngay,
đồng bộ physics theo lịch simulation của game trước scan. Dò là snapshot overlap,
không sweep: projectile nhanh cần thêm sweep hoặc substep ở cơ chế projectile.

## Inspector

`EveHitboxRegion` là component cấu hình tùy chọn, có Gizmos khi chọn object và
`Query(List<Collider>)` hoặc `Query(parentPose, results)`. Offset/rotation là local
so với pose; kích thước/offset dùng world unit và không nhân transform scale.
Không gắn region vào Visual xoay billboard để lấy hướng slash.
Ellipse dùng trường Ellipse Radii; Polygon dùng Polygon Points; Cone dùng Radius
cho bán kính đáy và Height cho chiều dài +X. Component query theo PhysicsScene
của GameObject, bao gồm local physics/preview scene.
Component không tự scan Update, không tự gây damage. OnDisable/OnDestroy dọn query;
đổi Inspector được áp dụng ở query sau. Để dùng Game cast, lấy `CreateShape()` và
`GetPose(parentPose)` rồi chuyển vào cast/resolver.

## Validation / Kiểm chứng

`PNHitboxEditorChecks.Run()` qua 82 kiểm tra PhysX thật trong preview scene, không save/reload
gameplay scene. Bao gồm 8 hình, tâm collider ngoài vùng nhưng vẫn giao, lỗ sector,
góc rộng/25°, vùng nghiêng 3D, collider scale, buffer80+, layer/trigger, entity child
dedupe, self/dead/unloaded, filter và once/repeat/session lifetime. Kiểm tra mới
bao gồm hai bán trục ellipse, cone taper/nắp đáy/rotation, polygon lõm/CW-CCW/
immutability/offset/giới hạn64 điểm và cấu hình Inspector của cả ba hình mới.
Chưa kiểm tra build player, Play Mode performance hoặc sweep. Suite .NET stub không mô phỏng PhysX; runner loại các thư mục Hitbox khỏi smoke
compile và dùng native checks cho phần này.

Eve slash đã dùng API này qua base:slash_hit: Sector, window và damage trong JSON.
Xem Assets/Game/Skill/Actions/Slash/PNSlashCombatREADME.md cho cấu hình cụ thể.
