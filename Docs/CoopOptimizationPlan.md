# Kế hoạch triển khai tối ưu co-op (sau fixture5) — bản 2

**Mốc chốt Step3b 2026-10-13:** đã sửa và đo lại; xem `.utmp/Step3b/Report.md`.
20 control A/B +8 diagnostic, cùng validator kiểm stack theo lịch và `DamageReceived`
thật, cả frame cuối; audit toàn cửa sổ hit==cue. Wave64/3 outbox popupON p99 wall
30,78/30,85 →23,93/23,50ms, message−~52%,bytes−~65%; chưa đạt8,33ms/120FPS.
Các mục hub/codec/UI/combat/network vẫn chờ người dùng giao việc riêng.

**Đính chính 2026-10-13:** phần AoE của bước 3 cũ chưa xác nhận quần thể xuyên suốt,
và cặp solo/network khác trạng thái popup, nên rút kết luận nguyên nhân network từ
cặp này. Raw lịch sử giữ nguyên. Người dùng đã giao Codex sửa provenance, gate
overflow và fixture, rồi đo lại bằng cohort hợp lệ: wave64 40 quái × 2 DOT = 80;
normal20 16 quái × 3 DOT = 48, có nhãn riêng. Chọn DOT đang sống, bỏ slot 1 và
slot churn cuối; thiếu depth thì lỗi, không tự giảm workload. Control solo/3 outbox
phải khớp popup, seed, thời lượng, cap và countdown flag. Kiểm loại, stack, population
và lịch hit ở mỗi frame trong cửa sổ đo. Kết quả mới nằm ở `.utmp/Step3b`.
Gate overflow dùng tập riêng kiểm mỗi `Advance`, không dùng công thức ngưỡng đóng.
Đây là sửa lỗi của countdown đã chốt; RulesVersion hiện tại 70 / Mod API 6 giữ nguyên.
Không coi cohort sửa này là bằng chứng đạt mục tiêu 64 actor/120FPS của toàn game.

Bản 2 tích hợp review Codex (tin #26): hub 3A phải generic ở EveEngine và đổi thứ tự dispatch;
codec 3B cần layout đủ xác định; không có registry part ID; pha tự nhiên cần warmup và lịch
seeded; popup/solo bớt đề xuất đã tối ưu; kênh mạng thiết kế sớm; version/song song bớt cứng.
**3A và 3B chưa sẵn sàng triển khai**: cần tài liệu contract riêng (hub, codec) trước khi giao việc.

Ngày: 2026-10-12. Trạng thái: **kế hoạch; bước 1 (Countdown), bước 2 (đo lại, fixture6 pha tự nhiên) và hoàn thiện bước 3 (AoE,
cặp đo chạy lại, owner cấp phát, trễ mạng) đã triển khai** (xem HANDOFF.md, Docs/BuffCountdownSpec.md,
.utmp/Step2/Findings.md, .utmp/Step3/Findings.md), các bước còn lại chưa triển khai. Mỗi bước cần người dùng giao
việc riêng và chỉ định bên thực hiện (AGENTS.md: không vai trò mặc định). Nền: RulesVersion69 /
Mod API6, số liệu `.utmp/MeasureStep1` (fixture5). Lộ trình đã thống nhất Claude/Codex.

Mục tiêu giữ nguyên: 64 actor/120FPS (bắt buộc), 128 (kỳ vọng), bài đồng pha giữ làm kiểm tra
burst; case normal20/normal6 và pha tự nhiên là dữ liệu bổ sung. Không giảm workload/cap,
không dời/bỏ tick gameplay, không thay 60FPS.

## 0. Quy ước chung cho mọi bước

**Trước khi sửa**
- Snapshot các file trong phạm vi vào `.utmp/<Bước>/Before` (không có git) + hash.
- Kiểm actual concurrent work (Unity đang compile/Play, benchmark đang chạy), báo bên kia.
- Đọc mục HANDOFF mới nhất và skill liên quan (`project-n-buff`, `project-n-core`).

**Khi sửa**
- Giữ hook/override/factory, thứ tự gameplay, defensive copy công khai, byte retry.
- Không sửa scene gốc/meta, Visual/iframe/UI/Mouse/projectile ngoài phạm vi được giao.
- Đổi contract thì cập nhật cùng lúc: payload/parser/validation/README/skill/HANDOFF.

**Version**
- RulesVersion tăng một nấc ở mỗi bước đổi hành vi stock hoặc ngữ nghĩa wire.
- Mod API giữ 6 nếu chỉ thêm member; kiểm bằng DLL mod biên dịch trước thay đổi.

**Kiểm chứng tối thiểu mỗi bước**
- Compile, `Tests/EveEngine/EveRunSmokeTests.ps1`, native liên quan (Buff, damage flow,
  lifecycle, elemental), test mới cho contract đổi.
- Editor cuối: scene không dirty, Stopped, timeScale/vSync/FPS khôi phục, Console 0 lỗi.

**Đo**
- Tập lặp nhanh (Q): 64 solo popup OFF, 64 ba outbox popup ON, normal20.
- Ma trận đầy đủ (F): 64 solo OFF, 64 three OFF, 64 three ON, 128 matched, normal20, normal6
  (+ pha tự nhiên từ bước 2).
- Control tắt diagnostic là số nghiệm thu; timing/CountOnly/allocation chạy riêng, chỉ để giải
  thích. Trong lúc benchmark không compile/build/profiler/việc nặng.
- So sánh với fixture5 (và bước trước): message theo kind, byte, allocation, p99, late, max.

**Kết thúc bước**: báo cáo `.utmp/<Bước>/Report.md`, cập nhật HANDOFF (quyết định, contract
đổi, kiểm tra thực chạy, artifact, việc còn lại). Không mô tả PASS cũ như mới chạy.

---

## Bước 1. Countdown do replica tự suy

Đặc tả: `Docs/BuffCountdownSpec.md` (bản 2, đã chốt). RulesVersion 69 → 70.

### 1.1 Core: descriptor
Files: `Assets/Core/Entity/Shared/Buff/PNBuffDefinition.cs`, `PNBuffContent.cs`.
- Thêm `public sealed class PNBuffCountdown` bất biến: `Slot` (enum 4 ô), `Rounding` (chỉ
  `Ceiling`), `Zero` (chỉ `Hide`). Constructor validate enum; serialize ổn định cho fingerprint.
- `PNBuffDefinition`: thuộc tính `public PNBuffCountdown Countdown { get; }`; method
  `public PNBuffDefinition WithCountdown(PNBuffCountdown descriptor)` dựng definition mới bằng
  constructor private sao chép **mọi** field (factory, payload, tags, instance settings, scope,
  canDispel, validate, validateInput, validateReferences, presenterId, icon/glow/showInUI) + descriptor.
  Không đổi chữ ký constructor/`Derive` hiện có; `Derive` không mang descriptor.
- `PNBuffContent`: thêm `"countdown"` vào danh sách `Fields(...)`; parse object
  `{slot, rounding, zero}` bằng helper `Fields/String` sẵn có; sau `factory.Derive(...)` gọi
  `.WithCountdown(...)` khi có trường.

### 1.2 Core: helper và tracker host
File: `PNBuffPresentation.cs`.
- `internal static long? PNBuffCountdownMath.Compute(double? expiration, double time, out bool overflow)`
  theo đặc tả 3.3 (kiểm `>= 9223372036854775808d` trước cast).
- `PNBuffPresentationWriter`:
  - field `countdown` (descriptor của `Instance.Profile` lúc bind), `trackedSeconds`, `overflowReported`.
  - `RefreshSeconds()`: tách hai nhánh:
    - descriptor ≠ null: nếu không `Suspended`/`PendingBehaviorStart` thì
      `trackedSeconds = Compute(...)`; overflow → `Report` một lần/writer; **không gọi** `remainingChanged`.
    - descriptor == null: giữ nguyên code legacy (kể cả `remainingChanged = null` khi overflow).
  - `internal PNBuffNumbers ReplicatedNumbers` = `Numbers` với ô descriptor ghi đè:
    `Instance.Suspended ? trackedSeconds : null`.
- `BindPresentation`: writer mới đọc `instance.Profile.Countdown`; tracker reset.

### 1.3 Core: publisher
File: `PNBuffReplication.cs` (`PNBuffPublisher.Capture`).
- Thay `writer.Numbers` bằng `writer.ReplicatedNumbers` ở **cả** điều kiện so sánh lẫn
  `new PNBuffWireState(...)`. Đường guaranteed cue dựng lại từ state cũ, không đổi.
- `diagnostic.state-diff`: thêm reason theo từng ô Numbers (4 bit) để đo bước 2.

### 1.4 Core: replica
File: `PNBuffReplication.cs` (`PNBuffReplica`).
- Cache: `Dictionary<long, Projected>` cho instance có descriptor (`Projected` = descriptor,
  profileId, numbers đã trình bày, overflowReported). Tập này cập nhật ở apply/reconcile/remove.
- `Project(PNBuffWireState s, double now)`: chạy → `Compute(s.Expiration, s.VisualTime(now))`;
  suspended → ô từ wire; các ô khác giữ wire.
- `Ui(s)` / `PresentationSnapshot()`: dùng numbers trong cache, **không** tính lại trong getter.
- Apply/reconcile/snapshot: với mỗi state: lấy bản cache cũ → tính projected mới với `now` truyền
  vào → so (`UiChanged` trên projected) → thay cache → `Emit`. Remove: chụp snapshot UI cũ trước
  khi xóa cache để `Removed` mang số cuối. Đổi profile/epoch: giữ bản cũ để so rồi reset.
- `Advance(now)`: chỉ duyệt tập instance có descriptor; tính → so cache → thay → `Emit(Updated)`
  qua `publicationNotices` khi `publicationDepth > 0`. Không đụng Revision/Sequence/cue/`Rebuild`.

### 1.5 Game: bật stock
Files: `Assets/Game/Buff/PNBuffGameplayContent.cs`, `PNBuffElementVisualSettings.cs`,
`PNTimeScaleBuffs.cs`, fixture `Assets/Tests/Core/PNLifecyclePolicyPlayer.cs` (`PNPolicyContent`).
- `Add/Dot` (và nơi đăng ký Vulnerability, TimeScale) gọi `.WithCountdown(BottomRight)` cho
  từng định nghĩa hiển thị countdown. Lập bảng: định nghĩa → bật/legacy → lý do.
- TimeScale area (không watch) giữ legacy. Fixture: profile dẫn xuất truyền descriptor tường minh.
- `EveGameRuntime.RulesVersion` → `project_n:70`; fixture `policy_fixture:70`.

### 1.6 Tài liệu
`PNBuffREADME.md` (contract presentation, raw/projected, migration collection), skill
`project-n-buff` (quy ước UI: countdown nên opt-in), HANDOFF (danh sách stock đã bật).

### 1.7 Kiểm chứng (test mới)
Native/EditMode trong `Assets/Tests/Core` (tên gợi ý `PNBuffCountdownEditorChecks`):
- Parser: thiếu/sai trường, giá trị chưa hỗ trợ; `Derive` không kế thừa; `WithCountdown` giữ đủ
  thành phần (gọi factory/validator qua definition mới cho cùng kết quả); override registry giữ `Countdown`.
- Chuỗi hiển thị legacy vs mới tại cùng mốc authoritative: chạy, scale/clock, suspend/resume,
  gia hạn khi chạy/suspended, override (chạy/suspended, migrate/không, có↔không descriptor),
  snapshot chạy/suspended/pending, hết hạn. Quy tắc mốc nguyên: tolerance `1e-8×Max(1,|t|)`,
  ngoài vùng khớp chính xác, trong vùng chỉ hai giá trị liền kề, biên `1 ↔ null`.
- Message: profile không descriptor giống hệt luồng cũ (kind/seq/revision/bytes); profile có
  descriptor không có `Updated` nào chỉ do giây, ô wire `null` khi chạy.
- Tracker khi mod opt-in không gọi `WatchRemainingSeconds`; callback không chạy khi opt-in.
- Overflow: max hợp lệ < 2^63, ≥ 2^63, không expiration, phục hồi, log không lặp, snapshot frozen.
- Cache: không mất/thừa thông báo khi reconcile, đổi profile, callback lồng; `Removed` mang số cuối.
- ABI: load DLL mod example biên dịch trên 69 (`Examples/` + SDK hiện có).
- Fingerprint đổi khi descriptor đổi (stock và mod).
Smoke + native liên quan. Render thực: chụp HUD Buff host và client replica, so số hiển thị.

### 1.8 Tiêu chí xong
- Tất cả test trên PASS; build normal/dev 0 lỗi.
- Đo Q (thêm reason theo ô) cho thấy `Updated` Numbers-only do countdown ≈ 0 ở profile opt-in.

---

## Bước 2. Đo lại + kịch bản pha tự nhiên

### 2.1 Fixture
File: `Assets/Tests/Core/PNLifecyclePolicyPlayer.cs`.
- Hiện `Apply` gán toàn bộ Buff trong một frame (`apply-burst`), churn
  (`stack-burst` mỗi .5s, `remove-apply-burst` mỗi 1s) áp lên mọi actor cùng frame.
- Thêm pattern `natural` (mô hình **bổ sung**, chưa đại diện chắc chắn cho gameplay thật):
  - Lịch tạo **trước** khi chạy từ seed cố định, không phụ thuộc thứ tự callback hay FPS:
    mỗi actor có offset spawn `U(0, waveWindow)`; mỗi instance có offset apply `U(0, period)`;
    mỗi actor có pha riêng cho stack và remove/reapply (cùng chu kỳ .5s/1s).
  - Apply thực hiện khi `gameElapsed` vượt offset; tick DOT tự lệch pha vì lịch
    `Schedule("tick", period, period)` tính từ lúc `Begin` (không dời tick gameplay).
  - **Warmup**: chỉ bắt đầu cửa sổ lấy số khi đủ actor/Buff của case đã được apply và warmup xong;
    không đo giai đoạn đang tăng population. Ghi rõ điểm bắt đầu cửa sổ đo.
  - **Bộ đếm stack riêng từng actor**: không dùng `stackBurst` toàn cục. Tránh quái nhận `−1`
    trước khi có DOT rồi mất DOT.
  - Provenance ghi: seed, lịch dự kiến, thời điểm apply thực tế, độ lệch, số instance sống theo thời gian.
- Giữ nguyên pattern đồng pha hiện có; Provenance ghi `fixture = 6`, seed, pattern. Kiểm tra pattern
  mới không đổi hợp đồng/Provenance của các case cũ ngoài trường mới.
- Thêm case: `natural64` (64 ba outbox ON) và `naturalNormal20`.

### 2.2 Chạy
- Q + natural trước; F ở mốc chốt bước 1.
- Báo cáo: so fixture5 theo kind/byte/alloc/p99; tách tracker countdown, reconcile, UI update;
  phân bố DOT frame vs non-DOT frame cho cả đồng pha và tự nhiên.

### 2.3 Tiêu chí xong
- Biết phần Numbers-only còn lại (theo từng ô) và lợi thực tế của bước 1.
- Có số pha tự nhiên để chọn ưu tiên bước 3–5.

---

## Bước 3. Actor delta chuẩn bị một lần + codec gọn

**Chưa sẵn sàng triển khai.** Trước khi giao việc cần hai tài liệu contract riêng: (i) hub fan-out
(thứ tự dispatch, exception, subscription), (ii) codec (layout byte đầy đủ). Hai phần 3A/3B
giao riêng, version riêng; 3A **không đổi wire nên không tăng protocol**.

### 3A. Actor delta chuẩn bị một lần (không đổi wire)
Files: `Assets/EveEngine/Networking/EveActorReplication.cs` (+ interface part trong EveEngine),
`PNBuffManager.cs` (chỉ là một part hỗ trợ kênh nội bộ).
- Hiện: mỗi connection subscribe `part.ReplicationDelta`; mỗi peer chạy `Capture` (GetComponent,
  `new EveActorData`, `CharacterId.ToString`, `ConditionalWeakTable`, `SequenceEqual`, dict) —
  fixture5 normal20: `actor.cache` 414ms, `actor.meta` 122ms exclusive.
- Ràng buộc kiến trúc: hub nằm ở **EveEngine**, contract generic (không tham chiếu `PNBuffManager`
  hay kiểu Project_N). Part nào chưa hỗ trợ kênh nội bộ vẫn đi **đường fallback hiện tại**
  (subscribe `ReplicationDelta` công khai) với hành vi không đổi.
- Thiết kế:
  - Part hỗ trợ nội bộ khai báo qua interface tùy chọn (tên gợi ý `EveIPreparedReplicationPart`)
    cung cấp bytes delta cho hub; hub encode actor delta **một lần** thành
    `PreparedDelta { PresenceKey, byte[] bất biến }` và fan-out cho từng connection.
  - Mỗi connection vẫn kiểm: session active, loader ready, actor hiện hành, generation, cap
    `MaximumPayloadBytes` theo policy riêng, rồi `DeltaOwned`; sequence/capacity/recovery riêng.
  - `ActorState` bất biến dùng chung khi `PresenceKey` trùng.
  - Cache identity string (`CharacterId`) theo loader/generation; bỏ `SequenceEqual`/`ConditionalWeakTable`.
- **Phải chốt trong tài liệu contract hub trước khi viết code** (điểm Codex #1):
  - Thứ tự dispatch mới giữa fan-out nội bộ và observer công khai (trước/sau), và hệ quả khi
    observer công khai unload actor, đóng session hoặc thay subscription mà không sửa mảng:
    peer nhận hay không nhận message đó.
  - Xử lý exception theo từng peer (một peer lỗi không chặn peer khác; cách báo lỗi).
  - Mutation subscription/connection giữa lúc fan-out (snapshot danh sách như hiện tại).
  - Observer công khai nhận bản sao riêng; ghi vào README là contract mới.
  - Tiêu chí parity chỉ áp dụng cho trường hợp **không có** observer unload/đóng/đổi subscription
    giữa chừng; các trường hợp đó có test riêng với kết quả mong đợi được quyết định.
- Test: nhiều connection nhận cùng bytes; observer công khai sửa mảng/unload/đóng session/đổi
  subscription; actor đổi generation/dormant/despawn giữa chừng; cap khác nhau theo connection;
  retry byte-identical; part không hỗ trợ nội bộ đi đường fallback không đổi.

### 3B. Codec nhị phân Buff + actor delta
Files: `PNBuffWireCodec.cs`, `EveActorReplication.cs` (`EveActorData`), `EveContentCompatibility.cs`.
**Điều kiện bắt đầu**: sau bước 1–2, đo lại; chỉ làm khi `buff.codec`/`actor.encode`/allocation
còn đủ lớn để đáng độ phức tạp (countdown dự kiến giảm khoảng một nửa số message).
- Bảng chỉ số ID:
  - Chỉ cho các ID có nguồn registry đã seal: Buff ID, profile ID, character ID. Sắp ordinal theo
    chuỗi canonical (chốt quy tắc canonical và alias `base:` trước). Hash bảng đưa vào
    `EveContentCompatibility`; hai đầu phải trùng hash mới kết nối. Không dựa thứ tự đăng ký ngầm.
  - **Replication part ID giữ dạng chuỗi** ở bước đầu: không có registry part ID chung
    (`ReplicationId` do từng component trả về); mỗi actor chỉ vài part nên chi phí nhỏ. Cơ chế
    khai báo/đàm phán part ID là thiết kế riêng nếu sau này cần.
- Bắt buộc có trong **tài liệu layout đủ để viết decoder** (điểm Codex #2) trước khi giao việc:
  - Header: version, cờ phân biệt `embedded` (trong actor delta) và `standalone`; bản embedded
    khôi phục identity session/target/epoch từ context đã xác thực (envelope + presence key), bản
    standalone mang identity đầy đủ.
  - Số nguyên: `sourceId` (`EveEntityId`, **cho phép âm**: ID cục bộ âm, ID mạng dương) dùng
    zigzag varint; các trường không âm dùng varint không dấu; giới hạn độ dài varint tối đa 10
    byte, từ chối biểu diễn không tối thiểu nếu quyết định như vậy.
  - Trường tùy chọn: `stacks`, `expiration`, `maxAge`, `endReason`, `dynamic` có flag/mask rõ;
    bit dự trữ phải bằng 0 (từ chối nếu khác).
  - Độ dài mọi mảng/chuỗi/payload kiểm trước cấp phát; từ chối byte thừa cuối buffer.
  - API: giữ nguyên `Encode/Decode` hiện có (ngữ nghĩa v1); thêm API mới nhận context/bảng
    chỉ số tường minh (API standalone hiện không nhận registry để giải index).
- Layout gợi ý (sẽ được chốt trong tài liệu riêng): `kind:u8`, `sequence`, `stateCount`,
  `removedId`, `endReason`, `cueCount`; state: `instanceId`, `revision`, `buffIdx`, `profileIdx`,
  `sourceId(zigzag)`, `flags`, `numbers(mask+zigzag)`, `stacks?`, `clock`, `buffTime/anchorTime/
  rate`, `expiration?`, `payloadVersion`, `dynamic(len+JSON)`; cue: `id`, `instanceId`, `name`,
  `at`, `maxAge?`, `payload(len+JSON)`. Actor delta v3: `version`, `characterIdx`, `owner`,
  `partId(string)`, `deltaLen`, `delta` — không JSON, không Base64; snapshot đầy đủ giữ v1.
- Encode vào buffer tái dùng, trả mảng đúng kích thước như hiện tại (giữ hợp đồng owned buffer).
  Decode một lượt; lỗi → `FormatException` như cũ.
- Phiên bản: version **riêng** cho 3B (không gộp "71/72" mơ hồ): codec protocol 1 → 2, actor
  version 3; compatibility từ chối lệch.
- Test: round-trip mọi field (null/không null, giá trị biên, âm/dương cho `sourceId`, NaN/Inf bị
  từ chối), chỉ số ngoài bảng, độ dài vượt cap, cắt cụt, byte thừa, bit dự trữ, fuzz có seed, hash
  bảng khác → từ chối kết nối, so ngữ nghĩa decode v2 với v1 trên cùng luồng sự kiện fixture,
  đường Buff standalone `Receive` không bị ảnh hưởng.

### 3C. Đo
Q + natural: byte/peer, `buff.codec`, `actor.encode`, `actor.cache`, `host.trysend.*`, allocation.

---

## Bước 4. Damage popup (phạm vi UI/Visual riêng — cần duyệt riêng)

Fixture5 normal20 (popup ON): `combat.damage-popup` 723ms exclusive (~52µs/popup), scope lớn nhất.

### 4.1 Đo tách (trước khi sửa)
File: `Assets/Core/Entity/Shared/Combat/Popup/PNDamagePopup.cs`, `PNDamagePopupPool.cs`.
Scope con trong `Show/Begin`: pool pop vs `Instantiate`, dựng chuỗi rich text, `ForceMeshUpdate`,
`GetComponentsInChildren`, `Render`; cộng `Advance` mỗi frame. Allocation theo stack.

### 4.2 Tối ưu không đổi hình ảnh (thứ tự theo số đo; mỗi cái có điều kiện an toàn)
- Cache `renderers` (thay `GetComponentsInChildren` mỗi `Begin`): **không cache vĩnh viễn**. TMP
  có thể tạo submesh/renderer mới khi đổi sprite hoặc material; chỉ tái dùng khi số lượng/danh sách
  con không đổi (kiểm bằng version/đếm con), hoặc làm mới khi `spriteAsset`/material đổi.
- Bỏ LINQ (`Distinct/OrderBy/ToArray`, `FirstOrDefault`) trong `Begin`; tra style bằng dictionary.
  Giữ thứ tự và loại trùng element đúng như hiện tại.
- Cache phần rich text theo (critical, tập element): khóa phải gồm tint, sprite asset, style và
  scale liên quan, không chỉ critical/element. **Giữ nguyên cách format damage hiện tại**
  (`FormatDamage` theo `double`); không đổi sang overload float làm mất chính xác. Dựng chuỗi
  bằng buffer tái dùng.
- Pre-warm pool: cần ngân sách bộ nhớ tổng (không chỉ cap idle 32/enemy).
- `ForceMeshUpdate`: chỉ bỏ nếu kiểm cả frame spawn và offscreen render cho cùng kết quả.
- Kiểm chứng bằng frame render thật (AGENTS: VFX phải chụp và xem frame), so ảnh trước–sau
  với `PNDamagePopupRenderChecks`, gồm critical, nhiều element, element mod chưa ánh xạ.

### 4.3 Tối ưu đổi thông tin hiển thị (duyệt riêng nữa)
Gộp popup DOT theo (target, cửa sổ thời gian), hoặc tách icon element ra renderer riêng thay vì
rich-text sprite. Chỉ làm khi người dùng duyệt thay đổi trải nghiệm.

---

## Bước 5. Solo: combat/source và local presentation

Chỉ chọn mục theo số đo bước 2 (exclusive + allocation stack). Ứng viên từ fixture5 solo
(chia cho 26 400 hit, xấp xỉ): capture-source ~7µs, create-snapshot ~7,5µs, receive ~6µs,
calculate ~3,7µs, dot.source ~4,9µs; local-presentation ~8,5µs/lần apply; so sánh publisher ~1,4µs/instance.

Một phần đã được tối ưu ở 69 và **không làm lại**: `CaptureCopy` và wrapper source đã tái dùng khi
hợp lệ; không thêm cache theo revision bao ngoài virtual hook. `(EveId[])damageTags.Clone()` mỗi hit
khi source không phải snapshot chủ yếu bảo vệ đường custom source: bỏ nó cho phép mod sửa dữ liệu
dùng chung, nên **giữ** trừ khi chứng minh được source là snapshot/bất biến.

Ứng viên còn lại (mỗi cái giữ hook calculator, thay đổi cùng frame, tự invalidate cache):
- Replica: `Advance` dùng `presenters.Values.ToArray()` mỗi frame; apply dùng LINQ — danh sách tái
  dùng **giữ semantics snapshot** khi callback lồng nhau (iterate trên bản chụp tại thời điểm vào).
- Publisher: `ReplicationInstances()` LINQ `Where/OrderBy/ToArray` mỗi Capture → danh sách sắp sẵn
  duy trì khi add/remove, cùng yêu cầu snapshot khi callback lồng nhau.
- Dirty-instance publisher: ứng viên sau (fixture5: không có Updated lặp; so sánh rẻ hơn mạng).
- Mọi mục khác chỉ chọn theo số exclusive/allocation sau bước 1–2.
Test: parity luồng message/damage/callback trước–sau; getter count; mod hook; callback lồng nhau.

---

## Bước 6. Thiết kế mạng trước production (tài liệu + prototype, chưa nối UTP)

**Phần thiết kế nên bắt đầu sớm (song song bước 1–2)**; phần triển khai transport để sau.

### 6.1 Lớp kênh
- Reliable ordered: lifecycle, Added/Removed, guaranteed cue, state chuyển trạng thái.
- Pose actor: unreliable sequenced, giữ bản mới nhất (chưa có đường đồng bộ pose ngoài spawn/snapshot).
- Optional cue: là **sự kiện có thể mất**, không phải trạng thái "giữ giá trị mới nhất"; được bỏ
  khi quá `MaximumAge`.
- Projectile: spawn/despawn + tham số chuyển động qua reliable; không gửi pose từng viên (đề xuất,
  cần đo).
- **Hiện không chọn được kênh chỉ bằng `MessageKind`**: optional cue và state Buff đều là outer
  `MessageKind.Updated`. Cần thêm phân loại ở lớp thông điệp (ví dụ trường lớp kênh trong
  envelope hoặc kind riêng cho optional cue) — thay đổi wire, thiết kế trong tài liệu riêng.
- Kênh unreliable cần **sequence và gap handling riêng**; mất gói ở kênh này không được kích hoạt
  recovery hiện tại (vốn gắn với reliable ordered).
- Định nghĩa trong `EveLifecycleData` (loại message → channel), adapter transport chọn pipeline.

### 6.2 Quá tải
- Thứ tự giảm tải trước recovery: bỏ optional cue quá `MaximumAge` → (nếu chứng minh an toàn)
  gộp Updated cùng instance chưa gửi → mới tới `BeginRecovery`.
- Ngân sách: byte/tick theo connection, backlog tối đa theo thời gian (deadline) thay vì chỉ số message.
- Recovery: giữ giới hạn vòng; thêm chỉ số cho client yếu (thời gian tới khi bị đóng).

### 6.3 Đo
- Client decode/apply/render thật chạy đồng thời (process riêng), transport giả lập
  (delay/jitter/loss/bandwidth cap) trước khi nối UTP.
- Kịch bản client băng thông thấp: thời gian tới recovery/đóng kết nối, trước–sau bước 3 và 6.2.

---

## Thứ tự, phụ thuộc, version

| Bước | Phụ thuộc | Đổi wire | Version | Ghi chú song song |
|---|---|---|---|---|
| 1 Countdown | — | Không | RulesVersion 70 | Thiết kế contract 3A/3B/6 song song (chỉ tài liệu) |
| 2 Đo + pha tự nhiên | 1 | Không | fixture 6 | Chỉ test/fixture |
| 3A Delta một lần | 2 (số) + tài liệu contract hub | Không | RulesVersion theo thay đổi (không tăng protocol) | — |
| 3B Codec nhị phân | 3A + đo lại + tài liệu layout | Có | codec/actor version riêng; RulesVersion theo thay đổi | — |
| 4 Popup | duyệt UI | Không | — | 4.1 (đo) song song được |
| 5 Solo | 2 (số) | Không | theo thay đổi | Không cùng lúc 1 (chung `PNBuffReplication.cs`) |
| 6 Thiết kế mạng | — (thiết kế); 3 (triển khai) | Có (sau) | — | Thiết kế sớm |

Song song **không chỉ** là khác file: Unity (compile/Play), build và benchmark là tài nguyên dùng
chung; hai việc cùng cần Unity hoặc benchmark thì tuần tự. Việc chỉ sửa tài liệu không chạy toàn
bộ gameplay suite và không bump RulesVersion. Benchmark của bước nào thì bên kia dừng việc nặng.

## Rủi ro chính

- Bước 1: ô countdown không luôn `null` khi chạy → mất lợi ích; cache replica sai thứ tự → UI
  mất/thừa thông báo; quên một định nghĩa stock → đo không thấy lợi.
- Bước 3A: thay đổi contract observer công khai (bản sao riêng) — cần ghi rõ.
- Bước 3B: lệch bảng chỉ số giữa hai đầu → phải chặn bằng hash compatibility; fuzz decode.
- Bước 4: thay đổi hình ảnh ngoài ý muốn → bắt buộc so frame render thật.
- Chung: số đo Development/instrumented không dùng làm bằng chứng FPS; chỉ control.

## Điểm bàn giao cho các contract riêng (từ review Codex vòng 3)

Không mở rộng plan; các điểm sau **phải được chốt trong tài liệu contract** tương ứng trước khi
triển khai bước đó, để không thất lạc.

**Hub fan-out (3A)**
- Hub dùng **giới hạn chuẩn bị riêng** khi encode payload chung; không dùng cap của peer đầu tiên
  (code hiện tại encode theo `policy.MaximumPayloadBytes` của peer đầu rồi cache cho các peer sau).
  Sau đó mới kiểm cap từng connection.
- Đây là **ràng buộc thiết kế**, không phải lỗi mất message đã chứng minh: hiện nếu peer đầu cap
  nhỏ mà encode thất bại thì kết quả không được cache, peer sau encode lại và vẫn kiểm cap theo
  từng peer. Rủi ro còn lại là encode lặp/phụ thuộc thứ tự peer.
- Một peer cap nhỏ hoặc lỗi không được khiến các peer hợp lệ mất message.
- Cleanup hub/subscription khi peer cuối rời, unload actor hoặc đổi generation.

**Codec (3B)**
- Tách tên hằng version legacy (JSON v1) và binary; API cũ không được ghi nhãn v2 lên payload JSON.
- Bảng routing decode rõ ràng: snapshot đầy đủ, Buff standalone, Buff embedded trong actor delta.

**Kênh và quá tải (6)**
- Backlog theo thời gian là **lớp thêm vào**, không thay `MaximumQueuedMessages/Bytes`: một burst
  lớn vẫn có thể chiếm quá nhiều bộ nhớ trước khi quá deadline.
- Bỏ/gom message phải làm **trước** khi dựng `pending` trong `TrySend` (lúc gán sequence và khóa
  byte retry); không sửa byte đã giao hoặc đang chờ retry. Giữ barrier lifecycle và guaranteed cue.
- Đó là điều kiện cần, **chưa đủ**: Buff có sequence riêng do publisher cấp lúc dựng `Packet`
  (trước bước gửi), và replica standalone có xử lý gap (`gapAt` → `Request` snapshot sau
  `ReorderWindow`). Gom/bỏ `Updated` rồi đưa qua đường Buff standalone sẽ tạo lỗ hổng sequence và
  kích hoạt recovery dù sequence của envelope vẫn đúng. Contract phải **phân biệt đường actor
  integration** (không chạy recovery/sequence riêng theo target) **với đường standalone**: ở
  standalone phải cấp sequence liền mạch sau khi gom hoặc không áp dụng gom.

**Đo (bước 2)**
- Thêm case **64 solo pha tự nhiên** để tách lợi từ pha đối với combat/local presentation khỏi
  lợi đối với mạng. Chỉ bổ sung, không thay bài đồng pha.
