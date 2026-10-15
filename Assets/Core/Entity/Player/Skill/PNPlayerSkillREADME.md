# PNPlayerSkill input

`PNPlayerSkill : PNSkill` giữ khung input PlayerSkill gốc cho Attack, Skill, Ulti,
Dash, Burst. Vòng đời input và vòng đời Action độc lập. Hook mặc định rỗng;
skill nhân vật tự override để nối tới Action. Không tự chuyển trạng thái Action,
không thêm combo/cooldown hoặc dependency Move.

## Control admission (2026-10-06)

Gameplay admission is checked in Try methods through entity control leases, not
in buttons. Global/category/exact skill gates and explicit interruption are
documented in `Assets/Core/Entity/Shared/Control/PNControlREADME.md`. Shared Dash
checks Dash and voluntary-motion gates before action start/charge consumption.
MenuInputBlocked remains independent; leases do not reset input interactions.

## Cấu hình

Mỗi nút có `PlayerSkillInputConfig`, serialize trực tiếp trong Inspector:

| Trường | Ý nghĩa |
| --- | --- |
| CanTap | Cho phép Tap, độc lập với HoldMode |
| HoldMode | None / Hold / SlowTap / Repeat |
| TapDuration | Thời gian giữ tối đa để tính Tap |
| HoldDuration | Ngưỡng giữ để Hold Perform |
| SlowTapDuration | Ngưỡng giữ tối thiểu để SlowTap Perform khi thả |
| RepeatDelay | Chờ trước lần Repeat đầu tiên |
| RepeatInterval | Khoảng cách các lần Repeat |

Thời gian phải hữu hạn và không âm. 0 dùng mặc định Input System cho Tap/Hold/SlowTap,
RepeatDelay dùng defaultHoldTime, RepeatInterval dùng 0.1 giây. Chỉ chế độ đang chọn
được áp dụng; các giá trị của chế độ khác vẫn lưu. Mặc định chỉ bật Tap.
CanTap=false và HoldMode=None sẽ tắt nút và xóa interaction override cũ.

```csharp
SkillInput.CanTap = true;
SkillInput.HoldMode = PlayerSkillHoldMode.SlowTap;
SkillInput.SlowTapDuration = 0.8f;
RefreshInput();
```

`RefreshInput` kiểm tra toàn bộ config trước, disable các nút để kết thúc tương tác
cũ rồi áp lại. Thay đổi config bằng code cần gọi RefreshInput; Inspector Play Mode
gọi lại qua OnValidate. Reapply có thể phát Canceled cho input đang giữ.

## Ownership và callback

`PC` là `PNPlayerSkillControls`, sở hữu năm InputAction riêng. Nó lấy bản sao binding
từ một PlayerControls tạm rồi dispose template, không lấy PC từ Move. Cần wrapper này
vì asset PlayerControls cũ có interaction cấp Action; override binding không xóa chúng.
Wrapper bỏ interaction cũ, giữ path/group/processors và áp config mới trên binding.
Nếu sau này dùng device filtering hoặc remap runtime, phải áp chúng lên bộ input này riêng.

Awake tạo controls và đăng ký handler có tên đúng một lần. OnEnable bật các nút hợp lệ;
OnDisable/Unload tắt chúng; OnDestroy gỡ callback và dispose. CanInput=false tắt input,
vẫn chuyển Canceled tới hook để code Skill xử lý. Load thành công bật lại nếu component
đang hoạt động và CanInput=true. Không bật input Move hoặc chia sẻ quyền enable với Move.
Các override lifecycle phải gọi base.

Menu pause dùng `MenuInputBlocked` riêng, không ghi đè khóa gameplay qua setter
`CanInput`. Getter `CanInput` chỉ trả true khi cả khóa gameplay lẫn khóa menu đều
cho phép. Đổi khóa menu reapply input để kết thúc hold/repeat; Action và Ability
vẫn giữ vòng đời/clock riêng. Khóa gameplay phát sinh trong lúc mở menu vẫn còn
khi đóng. UI giữ khóa menu đến khi các nút gameplay đã thả, tránh kích hoạt skill
từ phím còn giữ. Move có gate tương tự nhưng sở hữu bộ input độc lập.

Các hook giữ tên/cách dispatch gốc:

- StartedAttack/Skill/Ulti/Dash/Burst(ctx) phân loại interaction rồi gọi StartTap/Hold/SlowTap/Repeat.
- Performed gọi Enter -> Tap/Hold/SlowTap -> Exit liên tiếp; Repeat gọi Repeat trực tiếp.
- Canceled gọi CanceledTap/Hold/SlowTap/Repeat tương ứng.
- Các hook trên có đủ biến thể theo năm nút và virtual; AwakeSetUp/StartSetUp cũng giữ lại.

Input không tự kiểm tra entity Ready; code Skill quyết định xử lý hook. Tạo Action vẫn
được PNSkill kiểm tra entity Ready. CallbackContext chỉ dùng trong callback, không giữ
lại để sử dụng về sau. Có thể override handler nhận context để đọc duration/value.

`PNRepeatInteraction` dùng tên riêng PNRepeat, đăng ký trước khi bật input; không sửa
RepeatInteraction cũ. Nó kiểm tra nút còn được giữ trước khi xử lý timeout, tránh phát
thêm Repeat khi thả trùng thời điểm timer hết hạn. Thời gian nhận input dùng đồng hồ
Input System, không nối với EveTimeScale hoặc thời gian thi triển Action.

## Kiểm chứng

Smoke checks config/timing serialization, ownership không Move, callback không nhân đôi,
khóa/unload/destroy, xóa chế độ và Repeat timer. Mã runtime đã biên dịch với Unity
6000.6.3f1/Input System DLL thật. Eve slash đã được kiểm chứng Tap J qua Input System
và frame Play Mode thật; xem Assets/VFX/PNArcSlash/PNArcSlashREADME.md.
Chưa kiểm chứng thứ tự interaction Tap kết hợp Hold/SlowTap/Repeat hoặc gamepad vật lý.
