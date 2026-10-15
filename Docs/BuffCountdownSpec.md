# Đặc tả: Countdown Buff do replica tự suy (opt-in) — bản 2

Ngày: 2026-10-12. Trạng thái: **đã triển khai (RulesVersion 70)**; xem mục 'Kết quả triển khai' cuối file.
Bản đặc tả gốc giữ nguyên bên dưới. Nền: RulesVersion69 / Mod API6, fixture5
(`.utmp/MeasureStep1`). Bản 2 tích hợp review Codex (#22 và các vòng sau).

## 1. Mục tiêu

Ngừng phát `Updated` chỉ vì số giây hiển thị giảm. Replica (host lẫn client) tự suy số
đếm ngược từ clock đã có trong state.

Cơ sở: fixture5 cho thấy mọi `Updated` ở case 64/128 chỉ có reason Numbers. Code xác nhận
nguồn: `PNBuffTimedBehavior.ObserveDuration` gọi `WatchRemainingSeconds`, callback gọi
`PublishNumbers` mỗi khi số giây đổi (`Assets/Game/Buff/PNBuffGameplayContent.cs:146-152`).
Mặt nạ reason chưa tách từng ô, nên đây là suy luận mạnh, chưa phải chứng minh; lượt đo
lại phải xác nhận.

Cận ứng viên theo số message state/cue (không phải mức giảm CPU/byte đã chứng minh, không
phải cam kết đạt 120FPS): 64 ba outbox 54,8%; normal20 38,3%; normal6 52,4%.

Phi mục tiêu:
- Không đổi wire format, codec, envelope, actor delta.
- Không đổi gameplay: expiry, removal, tick, clock vẫn do host quyết định.
- Không áp "chỉ giá trị cuối" cho Numbers tùy ý của mod.
- Không đổi UI/Visual ngoài nguồn dữ liệu của ô countdown.

## 2. Hiện trạng (đối chiếu code)

| Thành phần | Vị trí | Hành vi hiện tại |
|---|---|---|
| Đồng hồ host | `PNBuffPresentation.cs:97-110` `RefreshSeconds` | `Ceiling(Max(0, Expiration - Time))`; `null` khi ≤ 0 hoặc không có expiration; thoát sớm khi `Suspended`, `PendingBehaviorStart` hoặc `remainingChanged == null`; chỉ gọi callback khi số đổi; overflow (≥ 2^63) thì Report và tắt callback vĩnh viễn |
| Gọi refresh | `PNBuffPresentation.cs:158,175` | Mỗi frame cho mọi writer |
| Behavior stock | `PNBuffGameplayContent.cs:141-161` | `PublishNumbers` xuất `topRight = stacks`, `bottomRight = displaySeconds` |
| Publisher | `PNBuffReplication.cs` `Capture` | So `old.Numbers.Equals(writer.Numbers)`; khác thì phát `Updated` đủ state |
| Replica UI | `PNBuffReplication.cs` `Ui`, `UiChanged`, `PresentationSnapshot` | Ô số lấy thẳng từ `PNBuffWireState.Numbers` |
| Thời gian hiển thị | `PNBuffWireState.VisualTime` | `BuffTime + Max(0, now - AnchorTime) * (Suspended ? 0 : Rate)` |
| HUD | `PNUiRuntime.cs:173,342` → `PNBuffManager.Presentation` | Bind vào replica, cả host lẫn client |
| Override khi suspended | `RecomputeAll` → replacement + `BindPresentation` | Writer mới, `PendingBehaviorStart = true`; instance vẫn trong `ReplicationInstances` (chỉ lọc `!Ending && Behavior != null`) với profile mới, `Suspended = true`; resume xóa pending trước `OnBehaviorStarted` |
| Fingerprint | `EveContentCompatibility` | Registry hash dùng `JToken.FromObject(definition)` |

## 3. Descriptor

### 3.1 Schema content

Trường tùy chọn `countdown` trong object Buff của `project_n.buffs`, cùng cấp với
`iconPath`/`glow`/`showInUI`:

```json
"countdown": { "slot": "BottomRight", "rounding": "Ceiling", "zero": "Hide" }
```

- Vắng mặt hoặc `null`: tắt (mặc định).
- `slot`: `TopLeft` | `TopRight` | `BottomLeft` | `BottomRight`. Bắt buộc.
- `rounding`: chỉ `Ceiling` ở v1. Bắt buộc (khai báo rõ, mở rộng sau).
- `zero`: chỉ `Hide` ở v1. Bắt buộc.
- Trường/giá trị lạ, sai kiểu: `FormatException` khi load, như các trường hiện có.
- Không suy bật/tắt từ việc behavior có duration.

### 3.2 Mô hình và ABI

- Kiểu bất biến `PNBuffCountdown` (slot, rounding, zero), serialize ổn định.
- `PNBuffDefinition` thêm thuộc tính public `Countdown` (null khi tắt).
- **Không đổi chữ ký constructor hay `Derive`.** Thêm method mới
  `PNBuffDefinition WithCountdown(PNBuffCountdown descriptor)` trả về definition mới:
  - sao chép đủ: id, implementation, factory, payload, tags, instance settings, scope,
    canDispel, validator, input validator, reference validator, presenter, icon/glow/showInUI;
  - không đăng ký, không chạy factory hay validator bên trong.
- `Derive` giữ nguyên và **bỏ descriptor có chủ đích** → quy tắc không kế thừa.
- Parser content: `factory.Derive(...)` rồi `.WithCountdown(...)` nếu có trường.
- Stock (`PNBuffGameplayContent.Add/Dot`): bật bằng `.WithCountdown(...)` tại nơi đăng ký.
- Đường chọn override registry giữ nguyên definition đã đăng ký (kể cả `Countdown`).
- Tra descriptor theo **profile đang hiệu lực** (`instance.Profile` ở host,
  `PNBuffWireState.ProfileId` ở replica), kể cả sau override.

### 3.3 Helper tính chung

Một helper duy nhất dùng cho cả host và replica:

```
Countdown(expiration, time):
  nếu expiration == null              → null
  value = Ceiling(Max(0, expiration - time))
  nếu value <= 0                      → null
  nếu value >= 2^63 (9223372036854775808d) → null, kèm cờ overflow
  ngược lại                           → (long)value
```

- Kiểm giới hạn trước khi cast. Giá trị hợp lệ lớn nhất dưới 2^63 phải được hỗ trợ.
- Hàm thuần: không có trạng thái fault. Vượt giới hạn thì ẩn cho lần tính đó; trở lại hợp
  lệ thì tự hiện. Vì không còn sticky fault, host và replica không lệch **trạng thái fault**;
  **giá trị** vẫn có thể lệch quanh mốc nguyên do clock/sai số làm tròn (xem 9.1).
- Khác legacy (tắt callback vĩnh viễn sau overflow) là **thay đổi có chủ đích của mode
  opt-in**, có test riêng; không gọi là parity tuyệt đối trong tình huống overflow rồi phục hồi.

## 4. Host

### 4.1 Tracker độc lập callback

- Tracker bật theo descriptor khi `BindPresentation` tạo writer (dựa `instance.Profile.Countdown`),
  **không phụ thuộc** `remainingChanged`. Mod opt-in không cần gọi `WatchRemainingSeconds`.
- Mỗi frame (đúng chỗ đang gọi `RefreshSeconds`): nếu không `Suspended` và không
  `PendingBehaviorStart`, cập nhật `lastSeconds = Countdown(Expiration, Time)`.
- Profile opt-in: **không gọi** callback `WatchRemainingSeconds` dù đã đăng ký. Behavior stock
  vì vậy giữ `displaySeconds = null`, `PublishNumbers` không chạy theo nhịp giây: không
  `Publish`, không `ReplicationChanged`, không `Notify`, không `Capture`. Đây là điểm cắt ở gốc.
- Profile không opt-in: giữ nguyên hoàn toàn đường legacy (kể cả tắt callback sau overflow).
- Watch/reset/lỗi của callback không chạm trạng thái tracker, và ngược lại.
- Chống spam log: Report overflow tối đa một lần cho mỗi writer (writer/profile hiện tại);
  writer mới thì reset. Tách khỏi giá trị hiển thị.
- Writer mới trước `OnBehaviorStarted` (pending): chưa refresh nên `lastSeconds = null`.
  Không kéo số từ writer/profile cũ.

### 4.2 Giá trị ô trên wire

Writer cung cấp `ReplicatedNumbers` = `Numbers` behavior đã publish, với ô countdown ghi đè:
- không suspended: `null`;
- suspended (kể cả pending): `lastSeconds` của writer hiện tại, tức số của **lần refresh cuối
  trước suspend**, không tính lại lúc suspend; chưa refresh thì `null`; duration đổi trong lúc
  suspended vẫn giữ số đó tới resume (giống legacy).

Publisher dùng đúng `ReplicatedNumbers` **cho cả so sánh lẫn dựng state**. Nếu so một giá trị
mà gửi giá trị khác, hoặc ô không luôn `null` khi chạy, `Updated` sẽ phát mỗi giây.
Suspend/resume vốn đã đổi `Suspended`/`Rate` nên đã phát `Updated`; số đóng băng đi kèm,
không cần publication riêng.

### 4.3 Collection: raw và projected

- `PNBuffCollection.PresentationSnapshot/PresentationChanged` = **raw** Numbers behavior đã
  publish. Cách đọc raw không đổi.
- Replication dùng **projected** (`ReplicatedNumbers`).
- Hệ quả thực tế, phải ghi là thay đổi hành vi: Buff stock opt-in không còn publish số giây,
  nên ô countdown của raw là `null` khi chạy. Consumer đọc collection trực tiếp mất countdown
  stock. Migration: đọc qua replica (`PNBuffManager.Presentation`), như HUD hiện tại. Ghi vào
  `PNBuffREADME.md`.
- Raw **không tự nhận số đóng băng**: với stock giữ `displaySeconds = null`, ô countdown của raw
  vẫn `null` cả khi suspended. Chỉ `ReplicatedNumbers`/replica mang số freeze. Migration phải
  nói rõ để consumer không tưởng collection có countdown lúc suspend.

## 5. Replica

### 5.1 Giá trị

Với state có profile mang descriptor:
- không suspended: `Countdown(Expiration, VisualTime(now))`;
- suspended: giá trị ô từ wire (số đóng băng của host);
- `now` ở mọi đường vào:
  - apply/reconcile/snapshot: dùng mốc presentation được truyền vào lời gọi đó;
  - `Advance(now)`: cập nhật từ cùng nguồn clock presentation;
  - `PresentationSnapshot()`: chỉ đọc cache, không tự lấy clock khác, không tính lại, không
    phát event trong getter.
- Không tự expire hoặc xóa Buff khi số về 0; chỉ ẩn ô. Removal vẫn chờ host.

### 5.2 Cache và thứ tự

- Cache **projected Numbers đã trình bày** theo instance; dùng nhất quán cho `Advance`,
  apply/reconcile và `PresentationSnapshot` ban đầu.
- Thứ tự bắt buộc: tính projected mới → **so với bản cache cũ đang giữ** → thay cache → `Emit`.
  Không ghi đè cache trước khi so (sẽ mất thông báo).
- Reconcile/apply/snapshot và đổi profile: giữ bản cũ để so, rồi mới reset/thay, rồi `Emit`
  Added/Updated.
- `Emit` đi qua hàng đợi `publicationNotices` khi `publicationDepth > 0` để giữ thứ tự với
  callback lồng nhau.
- Thay đổi chỉ ở UI: không tăng Revision/Sequence, không replay cue, không chạy lại
  reconcile/VFX `Rebuild`.
- Reset cache khi remove, đổi epoch, đổi profile. Khi remove: **chụp snapshot presentation cũ
  trước khi xóa cache**, để event `Removed` mang Numbers cuối đã hiển thị. Đổi profile: giữ dữ
  liệu cũ đủ lâu để so sánh rồi mới thay.
- Hiệu năng: giữ tập instance có countdown cùng descriptor đã cache; không tra registry hay
  quét state legacy mỗi frame.
- Gate overflow: giữ tập riêng chỉ gồm countdown đang overflow, không suspend và có
  `Rate > 0`. Kiểm tập này bằng helper chung mỗi `Advance` để phục hồi chỉ nhờ clock,
  không cần message mới và không đánh thức vòng quét countdown thông thường. Không
  tính thời điểm phục hồi bằng công thức đóng ở ngưỡng double 2^63. Rate/clock/profile,
  suspend, remove và đổi epoch cập nhật hoặc xóa membership qua đường state hiện có.
- Chống spam log overflow phía replica: theo instance + phiên profile hiện tại; reset khi
  override/remove. Snapshot lại cùng instance/profile không báo lại.

## 6. Bảng chuyển trạng thái

| Sự kiện | Host | Wire | Replica |
|---|---|---|---|
| Apply | Không publish số giây | `Added`, ô `null` | Suy từ clock |
| Số giây giảm | Cập nhật `lastSeconds` | Không có message | Tự đổi số, phát UI change |
| Gia hạn (`SetDuration`) khi chạy | Expiration đổi | `Updated` (Expiration) | Suy theo expiration mới |
| Gia hạn (`Apply` lại) khi suspended | Engine **từ chối** (`Rejected`, "Target suspended"); `lastSeconds` và Expiration không đổi | Không có message | Giữ số đóng băng tới resume |
| Đổi clock / scale | Rate/Clock đổi | `Updated` | Suy theo rate mới |
| Suspend | `lastSeconds` đứng yên | `Updated`, ô = `lastSeconds` | Hiện số đóng băng |
| Resume | Refresh lại | `Updated`, ô `null` | Suy lại từ clock |
| Override khi chạy, migrate | Writer mới, tracker theo profile mới | `Updated` (ProfileId) | Giữ cache cũ để so, tra descriptor mới |
| Override khi chạy, không migrate | Như trên, behavior bắt đầu lại | `Updated` | Như trên |
| Override khi suspended (pending) | Writer mới, `lastSeconds = null` | `Updated`, `Suspended = true`, ô `null` nếu profile mới opt-in | Ô ẩn; không mang số profile cũ |
| Snapshot lúc pending | Như trên | Snapshot mang ô theo 4.2 | Như Added, suspended |
| Resume sau pending | Xóa pending → `OnBehaviorStarted` → refresh | `Updated`, ô `null` | Suy từ clock |
| Override sang profile không opt-in | Đường legacy của profile mới | Ô theo legacy (có thể có số khi chạy) | Dùng số từ wire |
| Snapshot / join khi chạy | Không đổi | Ô `null` | Như Added |
| Snapshot / join khi suspended | Không đổi | Ô = `lastSeconds` | Như Added, suspended |
| Overflow | Ô ẩn lần tính đó, log một lần/writer | Không message thêm | Ô ẩn, log một lần/instance-profile |
| Hết hạn / remove | Host quyết định | `Removed` | Gỡ, reset cache |

## 7. Tương thích và version

- Wire format, codec, envelope không đổi.
- **RulesVersion 69 → 70**: stock đăng ký trong code đổi descriptor và ngữ nghĩa ô.
- **Mod API giữ 6** nếu chỉ thêm member (`WithCountdown`, `Countdown`, `PNBuffCountdown`) và
  trường content tùy chọn; kiểm bằng DLL mod biên dịch trước thay đổi. Nếu buộc phải phá
  ABI/contract công khai thì bump API.
- Fingerprint: `Countdown` public nên được `JToken.FromObject(definition)` đưa vào hash, kể cả
  stock đăng ký bằng code. Vì `null` cũng được serialize, hash của mọi definition đổi so với 69;
  chấp nhận được vì RulesVersion70 đã không tương thích. Ghi rõ.
- Cập nhật cùng lúc: parser, validation, `PNBuffDefinition`/`PNBuffCountdown`, đăng ký stock,
  `PNBuffREADME.md` (contract presentation + migration collection), skill `project-n-buff`
  (quy ước UI), HANDOFF.

## 8. Bật cho stock

- Bật cho từng định nghĩa Buff stock thực sự hiển thị countdown ở `BottomRight`. Kiểm từng
  định nghĩa, không bật hàng loạt.
- Nguồn dùng `WatchRemainingSeconds` hiện có (phải liệt kê định nghĩa nào bật, định nghĩa nào
  giữ legacy):
  - `PNBuffTimedBehavior` (`PNBuffGameplayContent.cs:146-152`) và các lớp con: Stat, Control,
    SkillEffect, Dot, Damage, StackGrant, Override, Immunity (`PNBuffGameplayContent.cs`),
    Vulnerability (`PNBuffElementVisualSettings.cs:39`);
  - `PNTimeScaleBuffs.cs:47`: chỉ khi không phải area, publish riêng `bottomRight` qua callback
    của chính nó.
- Kiểm cả profile dẫn xuất, override profile và content fixture (`PNPolicyContent`) truyền
  descriptor tường minh, để lượt đo thực sự dùng nó.
- Liệt kê danh sách đã bật trong HANDOFF.

## 9. Kiểm chứng

### 9.1 Chuỗi hiển thị

So giá trị ô giữa đường legacy và đường mới **tại cùng mốc thời gian authoritative**, qua:
chạy thường, đổi scale/clock (gồm clock dừng), suspend/resume, gia hạn khi chạy và việc bị từ chối khi
suspended, override
(chạy/suspended, migrate/không, có ↔ không descriptor), snapshot khi chạy/suspended/pending,
hết hạn.

Quy tắc quanh mốc nguyên (không thêm epsilon vào production):
- Vùng sai số dùng tolerance publisher đang dùng: `1e-8 × Max(1, |t|)`.
- Ngoài vùng: khớp chính xác.
- Trong vùng: chỉ chấp nhận hai giá trị liền kề mà sai số làm tròn có thể gây ra.
- Kiểm trước và sau mỗi mốc để bảo đảm không lệch kéo dài.
- Biên hết hạn là `1 ↔ null` (ẩn), không phải `1 ↔ 0`.
- Tolerance chỉ là tiêu chí kiểm chứng, không được dùng để che sai lệch clock kéo dài.

Client mạng thật: thêm delay/jitter; không yêu cầu khớp từng wall-clock frame.

### 9.2 Contract và biên

- Profile không descriptor: luồng message (kind/sequence/revision/bytes) giống hệt trước.
- Profile có descriptor: không có `Updated` nào chỉ vì số giây; ô luôn `null` khi chạy.
- Parser: thiếu/sai trường, giá trị chưa hỗ trợ; `Derive` không kế thừa; `WithCountdown` giữ đủ
  thành phần; override registry giữ `Countdown`.
- Tracker chạy khi mod opt-in không gọi `WatchRemainingSeconds`; callback không được gọi khi opt-in;
  legacy vẫn nhận callback.
- Overflow: giá trị lớn nhất hợp lệ dưới 2^63, giá trị ≥ 2^63, không expiration, phục hồi sau
  overflow (thay đổi có chủ đích), log không lặp, snapshot frozen.
- Cache: không mất/thừa thông báo khi reconcile, đổi profile, callback lồng.
- ABI: DLL mod biên dịch trước thay đổi vẫn load và chạy.
- Fingerprint gồm `Countdown` của stock và mod.
- Smoke suite, native Buff/lifecycle/damage flow liên quan.

## 10. Đo lại

- Tách scope: tracker host, reconcile state (replica apply), cập nhật số UI (replica `Advance` +
  emit). `buff.local-presentation` solo (~416ms trong fixture5) không kỳ vọng biến mất hết.
- Reason theo từng ô Numbers để xác nhận phần Numbers-only đã được loại.
- Tập lặp nhanh: 64 solo popup OFF, 64 ba outbox popup ON, normal20. Ma trận đầy đủ ở mốc chốt.
- Thêm kịch bản pha tự nhiên (pha từ thời điểm apply/spawn thực tế); giữ bài đồng pha.
- So với fixture5: message theo kind, byte, allocation, p99. Control tắt diagnostic là số nghiệm thu.

## 11. Đã chốt (thay câu hỏi mở bản 1)

1. Mod API: trường content tùy chọn không buộc bump; giữ API6 bằng cách chỉ thêm member
   (`WithCountdown`), không đổi chữ ký.
2. `PendingBehaviorStart`: instance vẫn được replicate với profile mới, suspended; ô `null`
   cho tới khi resume và tracker refresh.
3. API công khai đọc countdown phía host: không cần cho v1. Nếu thêm sau, là getter readonly,
   `null` trước init/pending, không tự refresh/advance.

## Kết quả triển khai (2026-10-12, RulesVersion 70)

Files: `PNBuffDefinition.cs` (`PNBuffCountdown`, `PNBuffCountdownMath`, `Countdown`,
`WithCountdown`), `PNBuffContent.cs` (parser `countdown`), `PNBuffPresentation.cs` (tracker độc lập
callback, `ReplicatedNumbers`), `PNBuffReplication.cs` (publisher dùng `ReplicatedNumbers`; replica
cache `shown` + `AdvanceCountdowns`), `PNBuffGameplayContent.cs`, `PNTimeScaleBuffs.cs`,
`EveGameRuntime.cs`, fixture `policy_fixture:70`, test `Tests/EveEngine/PNBuffCountdownTests.cs`.

Khác bản đặc tả:
- Cache replica là `Dictionary<long, ShownCountdown>` chỉ cho instance có descriptor (chứa luôn
  descriptor để không tra registry mỗi frame).
- `UiChanged` so với số đang hiển thị (cache) trước khi thay; `Removed` mang số cuối, cache xóa sau `Emit`.
- Tracker host chạy tại cùng chỗ gọi `RefreshSeconds` (mỗi frame qua `RefreshPresentationSeconds`).
- `timescale_reduction` (không phải area) opt-in; `timescale_reduction_area` giữ legacy.

Play review HUD (PNBuffCountdownPlayReview PASS 11, PNBuffUIPlayReview PASS 13, frame render đã xem tại
`.utmp/Countdown/PlayReview`) đã chạy. Chưa làm: đo lại fixture (bước 2); kiểm tra ABI bằng DLL mod
đã biên dịch trước (mod ví dụ hiện có không tham chiếu `PNBuffDefinition`; chữ ký công khai chỉ thêm).

### Đính chính sau khi viết test (2026-10-13)

- Gia hạn bằng `Apply` lại khi target đang suspended bị engine từ chối ("Target suspended"). Bảng chuyển
  trạng thái ở mục 6 đã sửa; countdown không có đường nào để Expiration đổi trong lúc suspended.
- Instance ID đơn điệu qua các epoch của một phiên (replica từ chối tái dùng), nên đổi epoch luôn kèm ID mới.
- Test bổ sung đã chạy (smoke PASS 1771): clock Entity đổi scale và dừng, join snapshot giữa chừng (chạy và
  suspended), gia hạn khi chạy, đổi epoch/profile, và trễ/jitter giao message qua harness độc lập.
