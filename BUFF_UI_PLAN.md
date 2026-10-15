# Kế hoạch Buff UI và hợp đồng bốn ô

Ngày: 2026-10-10. Trạng thái: đã được cho phép và triển khai RulesVersion61 / Mod API2.
Các phần dưới giữ kế hoạch gốc để đối chiếu. Hợp đồng thực tế xem PNBuffREADME,
PNUIREADME và mục HANDOFF mới nhất; evidence mới ở .utmp/BuffUIReview.
Nền hiện tại: RulesVersion60 / Mod API2. Getter StackCount đã đổi tên hoàn toàn;
DisplayStacks không còn. Kế hoạch này không yêu cầu làm lại nền Buff.

## 1. Các quyết định đã chốt

- Bốn góc là bốn ô số tùy chọn độc lập. Mỗi Buff quyết định giá trị, vị trí và
  điều kiện hiển thị. Chỉ số nguyên; null là không dùng, 0 có thể hiện.
- Mỗi lần Buff phát, gửi toàn bộ bốn ô. Đây là bản thay thế đầy đủ; null phải
  xóa số trước đó của ô tương ứng, không có nghĩa giữ giá trị cũ.
- Buff chủ động phát khi cần. Collection lưu bản cuối; UI không hỏi behavior
  mỗi frame, không tự suy luận nội dung và không tự trừ timer.
- Quy ước authoring Project_N: stack trên phải, duration dưới phải như Ability.
  Đây không phải ràng buộc API. Ngưỡng stack do Buff chọn; DOT hiện cả stack1.
- Không hiển thị damage/tick, countdown đến tick, stat tăng hay mức giảm timescale.
  Duration là số đếm như Ability, làm tròn lên; không có progress overlay.
- Một icon cho một instance thật. Cùng canonical Buff ID xếp liền nhau; tuyệt đối
  không cộng stack hoặc gộp timer giữa các instance/source.
- Nhóm ID theo thứ tự xuất hiện; instance mới vào cuối nhóm của nó. Nhóm mới nối
  cuối. Khi nhóm không còn instance, bỏ nhóm; xuất hiện lại thì là nhóm mới.
- Reapply, stack/timer thay đổi, suspend/resume và đổi profile giữ thứ tự instance.
- 5 icon/hàng, hiển thị tất cả, không overflow/+N. Nhóm được phép qua ranh giới hàng.
- Local player giữ vị trí dưới HP bên phải portrait, trái sang phải/trên xuống dưới.
- Enemy giữ vị trí trên HP world, trái sang phải/dưới lên trên như Ability.
- Icon30px, Ability35px. PNG nguyên alpha/hình dạng, không ép bo tròn. Fallback Burn
  ở Resources path Effects/Burn/Burn; artwork riêng người dùng sẽ bổ sung sau.
- Không click/tap/tooltip tương tác; icon và badge không chặn input gameplay.
- Badge, fade và reflow theo Ability. Có nhún icon/halo nhẹ khi instance mới xuất hiện.
  Không nhún/pulse số khi stack hoặc số khác thay đổi; resume không phát hiệu ứng mới.
- Suspended giữ vị trí/icon, làm mờ artwork, số vẫn rõ. Entity ẩn thì world UI ẩn.
- Không triển khai localization, bảng chi tiết, Stun, Shield gameplay, Character
  Visual, iframe, projectile hoặc UI khác. Không thêm code bật/tắt Mouse.

## 2. Hiện trạng đã đối chiếu

| Thành phần | Hiện có | Công việc cần thêm |
| --- | --- | --- |
| PNBuffBehavior | StackCount và callback lifecycle | Publisher presentation gắn với từng đời behavior |
| PNBuffSnapshot | Handle/Id/ProfileId/SourceId/Tags/Stacks/Remaining/Suspended và metadata | Giữ hợp đồng trạng thái, không gọi hàm UI trong constructor |
| PNBuffCollection | Changed không có payload; coordinator và snapshot theo instance | Cache presentation, event có payload, đọc cache lúc UI bind |
| PNBuffInstance | Time/Expiration/Clock và Revision dùng cho scheduler | Dữ liệu presentation và generation publisher riêng |
| PNUiRuntime | Refresh HP/Ability, icon Ability giữ theo ID | Buff view/subscription giữ theo handle |
| CombatHud/CombatWorld | Ba placeholder Shield/Burn/Poison cố định | Container Buff động |
| CombatUI.uss | Buff30px; enemy96x48; player wrap-reverse | 5 cột và chiều cao động đúng hướng |

Đường mã cần đọc: Assets/Core/Entity/Shared/Buff/PNBuffDefinition.cs,
PNBuffRuntime.cs, PNBuffOwnership.cs, PNBuffManager.cs; Assets/Game/Buff/
PNBuffGameplayContent.cs, EveBurstBuffs.cs, PNBuffElementVisualSettings.cs,
PNTimeScaleBuffs.cs; Assets/Game/UI/PNUiRuntime.cs và PNHudNumberBadge.cs.
API: PNBuffREADME.md, PNUIREADME.md; các skill gameplay/buff/core và references
verification, time-control-state, registry-content theo phạm vi thay đổi.

## 3. Hợp đồng dữ liệu đề xuất

Tên dưới đây là tên dự kiến khi triển khai, chưa phải API tồn tại.

### Bốn số

PNBuffNumbers là readonly value type có TopLeft, TopRight, BottomLeft, BottomRight.
Đề xuất mỗi ô là long? để giữ số nguyên có dấu, hỗ trợ 0 và không giới hạn về Int32.
Không đưa text, hậu tố, phần trăm, màu hay kiểu widget vào bốn giá trị này.
Empty tương đương bốn null. Equality so sánh cả bốn ô.
Behavior chủ động chuyển dữ liệu gameplay sang số nguyên trước khi Publish;
không ép StackCount/Remaining thật thành số nguyên trong gameplay snapshot.

Duration helper dùng ceil khi remaining>0, null khi không có expiration. Instance
hết hạn thì Remove; không tạo một frame icon0 giả trước Remove. Trường hợp số
ngoài phạm vi long phải được xử lý rõ trong helper: báo diagnostic và bỏ giá trị
không biểu diễn được, không clamp gameplay duration và không kết thúc Buff.

### Bản đọc presentation

PNBuffPresentationSnapshot chứa handle, canonical ID, profile ID, showInUI,
iconPath/glow, suspended, bốn ô và sequence/revision presentation.
Chỉ chứa dữ liệu; không có delegate, Sprite, VisualElement hay tham chiếu behavior.
Được collection tạo từ cache và metadata hiện hành, không gọi behavior khi đọc.
Cache bốn ô là kết quả presentation được phát, không được dùng để tính gameplay.

### Sự kiện

Đề xuất PresentationChanged với Added/Updated/Removed/Reset và sequence tăng dần
trong collection. Added/Updated mang trạng thái presentation đầy đủ, gồm cả bốn ô.
Removed mang handle và thông tin nhận diện cần thiết; không bắt UI đọc lại instance
đã chết. Reset dùng khi collection kết thúc, để view xóa và tháo binding.
Giữ event Changed hiện hữu cho consumer hiện tại; UI Buff mới không nghe cả hai
rồi refresh lặp. Không tăng tần suất Changed cũ chỉ để đếm duration UI.

Đọc cache ban đầu và subscribe phải có trình tự rõ: subscribe trước, lấy snapshot
có sequence, áp snapshot rồi áp các event mới hơn. Có thể gói thành một hàm attach
trên main thread để caller không tự xử lý sai. Bind tuyệt đối không tick Buff,
không gọi Apply/Resume và không tạo hiệu ứng.

## 4. Publisher thuộc behavior, collection làm trung gian

Mỗi đời behavior có một PNBuffPresentationWriter riêng, do collection gắn trước
OnApply hoặc OnBehaviorStarted. Behavior gọi Presentation.Publish(numbers).
Publish luôn nhận cả bộ bốn ô. Base behavior không tự tạo stack hoặc duration UI.
Không có yêu cầu Buff phải biết UI đang mở, entity được chọn hay UIDocument nào.

Writer mang handle/load generation và behavior generation. Generation presentation
không được dùng chung với Revision scheduler: Revision hiện còn đổi khi suspend
hoặc đổi clock, trong khi publisher hợp lệ phải sống qua suspend/resume.
Khi override/retire/unload, writer cũ mất hiệu lực. Callback giữ writer cũ không
được cập nhật behavior mới dù canonical ID và instance handle vẫn giữ nguyên.
Không chỉ dựa vào context.Presentation trả writer hiện hành: context được giữ qua
override nên callback cũ có thể vô tình lấy publisher mới. Phải bind publisher
với chính behavior incarnation và kiểm tra quyền sở hữu lúc Publish.

Publish cập nhật cache qua mutation boundary hiện tại, không gọi event ngay giữa
lúc đang sửa stack/resources. Dùng coordinator có sẵn; không tạo coordinator thứ hai.
Coalesce nhiều bản Publish của cùng instance trong một callback/command thành bản
cuối nếu cần, nhưng không đổi thứ tự gameplay hay làm mất sự kiện Removed/Reset.
Đây là đẩy dữ liệu đầy đủ từ nguồn, không phải gửi dirty rồi để UI gọi behavior.

Nếu cả bốn số giống cache thì không phát Updated vì số. Metadata/suspended thay
đổi vẫn phải phát event dù numbers không đổi. Event subscriber ném lỗi phải được
cô lập và báo qua diagnostic; không rollback gameplay đã commit hoặc kết thúc Buff.
Subscriber gọi mutation phải đi qua coordinator; không dispatch event lồng vô hạn.

## 5. Lifecycle và thời điểm publication

| Đường chạy | Hành vi presentation |
| --- | --- |
| Apply thành công | Bind writer, nhận Publish trong callback, phát một Added sau commit |
| Apply thất bại | Hủy cache staged; không để lại icon hoặc Added giả |
| Reapply/ChangeStacks | Behavior phát nếu nội dung đổi; giữ handle/vị trí |
| Contribution đổi | Behavior cập nhật gameplay/clamp xong rồi phát giá trị mới nếu cần |
| Trigger DOT | Không tự phát chỉ vì tick damage nếu bốn số không đổi |
| Suspend | Collection phát suspended=true; giữ cache, dừng helper clock |
| Resume | Collection phát suspended=false; helper đối chiếu timer hiện hành, không replay Apply |
| Full override | Retire writer/helper cũ, xóa số cũ, bind writer mới, publish theo behavior mới |
| Override khi suspended | Metadata đổi đúng lúc; không để số profile cũ dưới icon mới; để trống tới khi behavior mới được khởi tạo trên resume |
| End/dispel/death | Ngăn publish mới, cleanup bình thường, Removed đúng một lần |
| Unload/session end | Invalidate publisher, reset cache và subscriptions, UI detach |

Helper/publisher cần tồn tại qua suspend nên không gắn toàn bộ lifetime vào
resource scope vốn bị dispose khi suspend. Chúng thuộc đời behavior; gameplay
lease vẫn theo resource scope hiện có. Không thay đổi chính sách death của Buff.
Profile showInUI=false vẫn có cache; renderer ẩn. Metadata bật lại cho cùng handle
không được diễn giải thành gameplay instance mới.

## 6. Duration: nguồn chủ động cập nhật, UI không hỏi lại

Behavior muốn timer số thì đăng ký helper tùy chọn, ví dụ
Presentation.WatchRemainingSeconds(callback). Helper thuộc publisher incarnation.
Callback nhận số giây nguyên hoặc null; behavior kết hợp với các ô khác rồi
Publish cả bộ. Callback không nhận quyền thực thi gameplay; tách lỗi presentation
khỏi Invoke gameplay đang có chính sách RuntimeError/end instance.

Helper quan sát Time/Expiration sẵn có trên đường advance của collection. Không
tạo coroutine, MonoBehaviour.Update hay đồng hồ thứ hai. Không gọi một hàm build
presentation của tất cả Buff mỗi frame. Chỉ helper đã đăng ký mới tham gia, chỉ
gọi callback khi số ceil thay đổi. Không có helper thì Buff không bị hỏi số.

Refresh bằng SetDuration, đổi clock, migration, resume phải đối chiếu lại helper.
Đọc sau mutation đã ổn định để không gửi timer mới cùng stack cũ trong một lần reapply.
Nếu một frame vượt nhiều giây, chỉ cần phát số cuối cho UI, không replay tất cả
các chữ số bị bỏ qua. Các tick gameplay vẫn phải xử lý đầy đủ theo scheduler cũ.
Khi timer hết và instance bị xóa trong frame, Remove thắng update timer.
Không dùng lịch gameplay để thêm presentation event làm thay đổi thứ tự damage/tick.

Clock Game/Entity/EntityWithoutMinimum theo đúng instance. Entity clock dừng thì
timer UI dừng; Entity có minimum không nhất thiết dừng khi raw scale=0. Buff timer
Game vẫn chạy khi entity đứng thời gian nếu gameDelta>0. Global pause/suspend không
tiến đồng hồ. Không suy ra đồng hồ từ tên Buff như TimeStop.

## 7. Cập nhật các behavior hiện tại

Tạo helper authoring nhỏ trong Game nếu cần để tránh lặp compose stack/duration;
không đưa tên Buff hoặc quy ước góc vào renderer/Core như luật bắt buộc.

| Behavior/profile | Đề xuất publication ban đầu và cập nhật |
| --- | --- |
| DOT/Burn/Electrocution/Frostbite | Stack trên phải từ1; duration dưới phải. Publish sau apply/reapply/direct stack change/cap clamp/migration; helper cho duration |
| Stat template | Stack và duration; đề xuất hiện từ1, điều kiện nằm trong behavior, không là luật nền |
| Bomb/Control/Stack Limit/Override/Immunity/Skill Effect | Duration; không biến value/amount/cap của Buff khác thành stack |
| Vulnerability | Chỉ duration; reapply refresh |
| Rising Momentum | Stack; đề xuất hiện từ1. Không tự lấy timer Burst của Ability |
| Blazing Assault | Bốn ô trống; Skill sở hữu lifetime |
| Timed timescale reduction | Chỉ duration theo Game clock |
| Area timescale reduction | Bốn ô trống, không đăng ký helper duration |

Chỉ DOT hiện stack1 đã được chốt rõ; ngưỡng cho Stat/Momentum ở bảng là đề xuất
triển khai, phải được trình bày là lựa chọn từng behavior. Không áp ngầm >=2 toàn cục.
Không sửa registered tuning, damage formula, stacking scope hay nguồn timer.
Registry vẫn tạo behavior; không new behavior trong UI hoặc binding.

## 8. Tích hợp UI mà không làm PNUiRuntime phình thêm

Đề xuất lớp PNBuffView quản lý binding,
danh sách card và layout. PNBuffIcon quản lý artwork/four badges/animation.
PNUiRuntime chỉ tạo/đổi binding local HUD và enemy world view theo entity đang có.
Tái sử dụng PNHudNumberBadge/PNHudGlow nếu phù hợp; không refactor Ability ngoài phạm vi.

Giữ dictionary theo PNBuffHandle đầy đủ, không chỉ InstanceId vì số có thể trùng
ở collection khác. Mỗi ID có order riêng khi group xuất hiện; mỗi instance có
thứ tự trong group. Rebuild binding lấy thứ tự từ dữ liệu instance đã lưu, không
lấy thứ tự dictionary hoặc tên đã dịch. Duy trì group ordinal qua việc instance
đầu nhóm bị xóa; không để nhóm nhảy sau nhóm khác khi UI gắn lại.
Do đó collection cần lưu first-admission order của nhóm ID còn sống hoặc cung cấp
thông tin tương đương trong cache, độc lập UI có đang mở hay không.

Subscribe một lần mỗi binding. Có thể mỗi frame kiểm tra binding đang trỏ đúng
collection trong vòng entity refresh hiện tại, nhưng không gọi Snapshot()/Get()
hay hàm Buff presentation cho các instance không đổi. Camera follow và animation
vẫn có thể chạy mỗi frame vì đó là công việc hình ảnh, không đọc số gameplay.

Added -> tạo card; Updated -> chỉ thay trường khác; Removed -> fade rồi dispose.
Bootstrapping các Buff có sẵn không pulse hàng loạt, theo Ability. UI ẩn do pause/
offscreen vẫn nhận cache hoặc resync khi hiện lại; không replay hàng loạt hiệu ứng
nhận Buff trong thời gian ẩn. Same-handle resume/profile update không pulse như Added.
ShowInUI=false, hidden entity, local-owner đổi và session replacement có đường detach
rõ. Pending fade/reflow không giữ reference behavior hoặc collection đã unload.

UI cần hủy đăng ký trước khi hủy document. Collection dispose vẫn phải giải phóng
subscriber kể cả UI chưa detach. Event đã queue của generation cũ bị bỏ qua.

## 9. Layout, số và animation

- Icon30px, năm cột; khoảng cách khởi đầu theo Ability (10px ngang, 4px dọc), sau đó
  kiểm tra frame thật để số hai góc của icon cạnh nhau không đè nhau.
- Với 30px và gap10, năm icon chiếm190px chưa tính phần badge nhô; phù hợp vùng
  vitals210px nhưng phải kiểm tra bounds thật. Trên trái/phải dài ra phía tương ứng.
- Enemy document96x48 hiện tại phải tăng chiều rộng/chiều cao theo Buff grid; không
  kéo HP90x8 theo document. HP vẫn căn giữa, giữ ring/gap và sprite-top anchor.
- Player HP/portrait không được dịch dọc mỗi khi số hàng Buff tăng. Container Buff
  phát triển xuống dưới từ vị trí hiện tại; enemy phát triển lên trên.
- Number badge theo Ability: white16px, capsule xám mềm, không outline. Kích thước
  chữ/offset có thể cần tinh chỉnh sau xem frame30px; không tự giảm icon Ability.
- Appearance fade .12s, removal fade .1s, reflow .2s như Ability; nhún icon và halo
  .2s khi Added. Đề xuất giữ tỷ lệ nhún3/35 trên icon30px, không phóng thành35px.
- Số đổi trực tiếp, không bounce/pulse. Suspended artwork .45 như unavailable Ability,
  badge opacity giữ rõ; resume không pulse. Các thông số chưa được người dùng chốt
  bằng số ở đoạn này là baseline từ Ability để kiểm chứng hình ảnh.
- UI animation dùng unscaled time. Buff timer dùng clock của Buff. PNG không clip
  tròn và không phủ progress. Không thêm tooltip/collider tương tác/pointer handler.
- Giá trị số rất dài phải được thử với nhiều icon; không tự dùng ký hiệu K/M hoặc
  cắt số như một quyết định ngầm. Báo trường hợp chưa đọc được trước khi chốt layout.

## 10. Thứ tự triển khai và điểm hoàn thành

1. Đọc lại AGENTS/HANDOFF mới nhất và kiểm tra Unity status. Ghi baseline scene,
   dirty state/Play mode, Game/Simulator và Play Maximized. Không đổi scene gốc.
2. Thêm value types, cache, writer và typed events trong Core. Test publish đầy đủ,
   equality, initial attach, stale generation, observer error và reentrancy trước UI.
3. Tích hợp Apply/reapply/end/override/suspend vào presentation lifecycle, giữ nguyên
   callback và cleanup gameplay. Test các đường lỗi/failure để không tạo ghost icon.
4. Thêm helper duration opt-in dùng clock hiện có. Test refresh, freeze, minimum,
   long frame, exact expiry và migration trước khi chuyển behavior.
5. Cho từng behavior phát đúng các số cần. Existing smoke kiểm stack/damage/timer
   vẫn phải pass. Không sửa snapshot state để phục vụ định dạng.
6. Thêm Buff view/icon độc lập và nối vào local HUD/enemy. Thay ba placeholder bằng
   dữ liệu thật, fallback Burn. Hoàn thành grouping/layout/animation/input transparency.
7. Native checks và Play review bằng fixture tạm; chụp/đọc frame Game và Simulator.
8. Sửa lỗi theo kết quả; chạy lại đúng phần bị ảnh hưởng. Hoàn tất docs/skill/handoff,
   compatibility assessment, SDK export và example build sau Unity compile thành công.

Không có bước nào trong tài liệu này tự cấp quyền triển khai. Khi người dùng cho
phép triển khai, thực hiện xuyên suốt phạm vi được phép thay vì dừng ở khung API.

## 11. Ma trận kiểm tra bắt buộc

### Core/presentation

- Bốn ô độc lập, đủ bốn trong mỗi Publish; âm/0/null; bản cũ không đổi khi bản mới tới.
- Publish giống nhau không event số; metadata đổi vẫn event; Buff không phát thì
  counter hàm đọc presentation không tăng theo frame.
- Hai source DOT hai handle hai icon; hai target không dùng chung cache.
- UI gắn sau Apply nhận bản cuối; subscribe/snapshot không mất update; rebind không
  nhận event cũ; group order giữ đúng khi instance đầu nhóm hết và bind lại.
- Apply lỗi không Added; end/remove đúng một lần; listener ném lỗi không phá cleanup.
- Override giữ handle/canonical ID, dùng metadata và số mới; writer cũ vô hiệu.
- Override khi suspended không replay Apply hoặc lộ số profile cũ; migration đúng state.
- Suspend giữ số/cache; clamp stack khi suspended vẫn có thể phát số mới theo behavior.
- Dispose cả collection/manager/UI, session thay, target chết và source unload không
  giữ subscription/helper. Replication chưa triển khai, không tự nhận là đã hỗ trợ.

### Clock/helper

- Game/Entity/EntityWithoutMinimum; Entity minimum; raw0/scale0; global pause.
- Duration15 -> số15; thay đổi trong cùng bucket không phát; chuyển bucket mới phát.
- Refresh ngay tại boundary, refresh ở cap, SetDuration(null), đổi clock và resume.
- Long frame chỉ cập nhật số cuối; vẫn giữ đủ gameplay tick, exact-expiry tick trước end.
- Không gọi Advance từ callback, không thêm timestep, không làm lệch hit/expiry order.
- Area-owned untimed không đăng ký timer observer; không có polling behavior vì UI.

### Native/UI thực tế

- Player 0/1/5/6/11 icon đi xuống; enemy tương tự đi lên; HP/portrait/anchor không nhảy.
- A1,B1,A2 -> A1,A2,B1; xóa A1 -> A2,B1; xóa hết A rồi thêm A -> B,A.
- Stack/timer/profile update giữ card identity và thứ tự; hết instance dồn mượt.
- Bốn góc cùng có số, số0/âm/nhiều chữ số; PNG nguyên shape; Burn fallback.
- Initial snapshot không pulse; new Added có pulse; stack update/resume không pulse.
- Suspend mờ artwork/số rõ; pause/offscreen/rebind không phát lại hiệu ứng cũ.
- Chạm/click vùng icon và số không bị nuốt bởi Buff UI; Game/Simulator/multi-touch
  dùng đường input hiện tại, không thêm Mouse reset/recovery.
- Không đổi icon Ability, HUD HP, pause/mobile UI ngoài điều chỉnh container Buff cần thiết.

Dùng standalone EveRunSmokeTests.ps1 cho contract; native/Play helper mới chỉ cho
những trường hợp cần Unity. Stubs không chứng minh rendering/input. Không báo lại
PASS của StackCount rename như evidence của tính năng mới.

## 12. Tài liệu, version và nghiệm thu

- PNBuffREADME: publisher/cache/events/helper/lifecycle; StackCount/Remaining vẫn là state.
- PNUIREADME: 5 cột, grouping, direction, fallback, bốn ô, event binding và animation.
- project-n-buff skill: source push, full payload, per-Buff visibility; sửa câu >=2
  đã lỗi thời. Không ghi API đề xuất như thể đã triển khai trước khi code tồn tại.
- Test README/HANDOFF: quyết định, actual checks, artifact paths, errors và giới hạn.
- Đánh giá RulesVersion theo thay đổi runtime/content và Mod API theo API thực tế;
  không tự lặp số60 thành trạng thái cuối nếu contract đã thay đổi. Không thêm alias
  legacy để bảo vệ mod giả định; dự án đang phát triển, chưa có mod thật theo người dùng.
- SDK chỉ export sau Unity compile đúng bản cuối; build example bằng SDK đó.
- Không save/reload scene authored; fixture tạm cleanup, giữ .meta GUID, khôi phục
  mọi editor/time/camera/input setting đã đổi trong kiểm tra. Console báo lỗi còn lại.
- Artifact đề xuất: .utmp/BuffUIReview/ gồm smoke/native/Play logs, Game/Simulator
  captures và final audit. Đây là đường dự kiến, chưa có evidence mới cho Buff UI.

Hoàn thành khi Buff tự phát đủ bốn ô; UI chỉ nhận event/cache bootstrap; gameplay
không phụ thuộc các số UI; tất cả layout/lifecycle/input checks liên quan đã qua.
