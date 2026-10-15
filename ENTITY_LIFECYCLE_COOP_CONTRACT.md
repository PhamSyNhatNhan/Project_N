# Entity lifecycle coop — bản contract để rà soát

Ngày: 2026-10-11. Trạng thái: **bước1–4 đã triển khai cho actor/Buff; policy production và transport còn mở**.

Cập nhật triển khai: user đã cho phép bước3/4, policy chưa chọn. Loader Prepared/Commit,
directory theo generation, guard module, cleanup object thật và actor/Buff snapshot
đã nối. Xem `Assets/EveEngine/Networking/EveActorLifecycleREADME.md` để phân biệt API
đã triển khai với phần contract còn chờ. Cấu hình fixture chỉ là test input; không
coi là policy production hoặc bằng chứng đã nối transport/đồng bộ toàn bộ gameplay.

Tài liệu này hợp nhất các quyết định lifecycle sau khi giản lược về một luồng
reliable ordered. Nó thay thế các đề xuất trước về buffer đảo thứ tự giữa kênh,
sequence theo từng hệ và mốc chờ trước despawn trong phiên bản đầu.

## 1. Hiện trạng và phạm vi

Hiện tại RulesVersion67 / Mod API6. Các sửa Buff #3/#4/#7/#8/#16 và Buff
presentation publisher/replica/presenter đã triển khai. Không làm lại chúng.
Buff trên actor path dùng generation từ entity và envelope chung; ApplyLifecycle
không chạy recovery/sequence riêng theo target. Standalone Receive cũ vẫn có để
kiểm proxy độc lập, không trộn hai đường trên cùng replica.

EveEntityLoader PrepareNext chia step đồng bộ qua frame; CommitBatch công bố toàn tập
sau hook/swap trước notification. Directory reserve theo (EntityId,generation),
TryGet/Snapshot chỉ trả presence hiện hành đã publish, cleanup lỗi giữ tracking.
EveRegistrySession là singleton tĩnh. Chưa cài Unity Transport; chưa nối transport.

Gameplay do Offline/Host authoritative quyết định. Client gửi ý định qua handler
được kiểm tra và nhận trạng thái để trình bày; không chạy lại callback gameplay
Buff. Prediction gameplay và projectile client-authoritative chưa được chốt.

Phạm vi thiết kế: envelope, lifecycle, snapshot, prepare/commit, cleanup, policy,
mã lỗi, kế hoạch fixture và mô phỏng. Move/Combat/Skill/save run có contract riêng
về dữ liệu; không giả định đã đồng bộ chỉ vì có lifecycle.

Trong tài liệu này "entity" là **replicated actor**: người chơi, quái/boss và vật
thể sống lâu được khai báo tham gia actor replication. Có GameObject hoặc thậm chí
EveEntityLoader không tự làm object thành replicated actor. Fixture v1 đo player/
enemy và các long-lived actor mẫu đã chọn, không tính mọi object Unity vào N.
Projectile, pickup, Area, hiệu ứng thả và damage number nằm ngoài actor lifecycle
này. Chúng cần contract nhẹ/phù hợp riêng; projectile production vẫn chưa thiết kế.
Không chốt client gameplay simulation/authority cho đạn từ việc loại nó khỏi actor
lifecycle. Pickup vẫn là gameplay authoritative, không phải VFX có thể tùy ý bỏ.
Projectile network contract tạm gác. Hướng collision mới do user chọn là Collider
với trigger enter hoặc sweep tùy loại, thay cho lựa chọn grid trước; chưa triển khai.
Đó không phải quyết định enroll đạn vào actor lifecycle hoặc đồng bộ đạn trên mạng.
Đối tượng ngoài actor có budget/count và snapshot/event riêng khi được triển khai;
byte/rate của chúng vẫn tính vào tổng connection/world representation. Loại khỏi N
không phải miễn ngân sách mạng/memory. Không tuyên bố toàn bộ world đã sync khi các
contract này chưa có.

## 2. Envelope và identity

Envelope dự kiến:

| Trường | Quy tắc |
| --- | --- |
| ProtocolVersion | Version wire tường minh; compatibility trước gameplay |
| AuthoritativeSessionId | GUID phiên host; khác GUID session local của client |
| ConnectionIncarnation | Host cấp cho lần kết nối; reconnect nhận giá trị mới |
| StreamId | 0 = reliable ordered gameplay; giá trị khác dành cho mở rộng |
| Sequence | Int64 dương, theo từng kết nối/hướng gửi/stream, bắt đầu lại khi nối mới |
| MessageKind | Mã message ổn định, schema có version và giới hạn |
| EntityId / Generation | Với message entity: ID dương Int64 và generation dương Int64 |
| Payload | Dữ liệu đã kiểm tra schema, giới hạn byte/depth/count |

Adapter loại dữ liệu của kết nối đã đóng trước lớp protocol. Bắt tay mở kết nối
mới; thông điệp gameplay đầu tiên là snapshot đầy đủ. Packet cũ không được lọt vào
luồng mới dù sequence của nó trông hợp lệ. Player identity tách khỏi connection/peer
ID; cơ chế khôi phục quyền sở hữu player qua reconnect vẫn là mục mở.

Sequence chốt khi transport chấp nhận cả lô gửi. Gửi bị từ chối vì hàng đầy: giữ
nguyên lô và thứ tự để retry; không gửi lô sau vượt lên. Sequence thấp hơn kỳ vọng
được bỏ và đếm. Sequence nhảy cách trên luồng ordered là lỗi protocol: không đợi
reorder, phục hồi có giới hạn hoặc đóng. Tất cả counter tăng có kiểm tra overflow;
không quay vòng. Transport reliable vẫn có thể đóng trước khi giao xong.

EntityId mạng do host cấp, không tái sử dụng trong phiên; Offline local ID âm không
được dùng làm network ID. ID của host load thất bại cũng bị bỏ. Generation tăng theo
EntityId khi host mở lần hiện diện mới; client không tự nâng từ delta.

Client giữ sàn generation cho actor hiện hành/đang load/candidate riêng với vòng
đệm lịch sử đã kết thúc. Vòng đệm FIFO hữu hạn, đầy thì evict và tăng counter chẩn
đoán, không đóng kết nối. Commit snapshot reset lịch sử và dựng lại sàn generation
theo tập mới; không xóa sàn candidate trước commit. Spawn ID rất cũ đã evict/reset
có thể được nhận như mới: bất biến không tái sử dụng thuộc host. Message gameplay
khác cho key không tồn tại là lỗi protocol. Không suy luận kết thúc bằng max ID.
Mỗi lần load có token (connection incarnation, LoadId, key); LoadId tăng checked
theo kết nối, không reset khi snapshot commit. Callback chỉ nhận nếu khớp attempt
hiện hành, không tra lịch sử. Hủy token ngay khi load không còn cần; cùng key trong
snapshot thay thế có thể giữ attempt, còn attempt mới luôn dùng LoadId mới.

Dormant chỉ dùng cho logical actor cần quay lại có chủ đích, không phải nơi lưu mọi
quái của floor cũ. Game mode là owner retention: mỗi dormant có lý do, scope/owner
và điều kiện kết thúc hữu hạn (kết thúc chuyển floor, rời room/floor scope, hoặc
expiry được mode khai báo). Mode/owner kết thúc retention thì host phát terminal
Despawn kể cả không có object. Hết phiên kết thúc toàn bộ. Quái/vật thể tạm rời
phạm vi vĩnh viễn mặc định terminal, không tự chuyển Dormant; actor persistent được
khai báo rõ và vẫn có owner/count cap. Không chọn số floor TTL chung trong Engine.
Theo dõi count/ratio dormant theo cap và cảnh báo ở ngưỡng policy trước khi chạm cap;
không tự xóa actor còn được mode giữ chỉ để lấy slot.

Disable/chết/unload không tự đồng nghĩa identity kết thúc. Chuyển floor có thể giữ
EntityId nhưng tạo generation mới. Identity còn sống nhưng tạm không hiện diện phải
được mô tả riêng trong snapshot; không suy ra kết thúc từ mốc ID lớn nhất đã thấy.
Snapshot chỉ kết thúc generation/identity mà trạng thái authoritative của nó xác nhận.

## 3. Luồng message và outbox

Một reliable ordered pipeline chung chứa spawn, state update quan trọng, Buff/cue
quan trọng, combat result và despawn. Host không công bố message phụ của một entity
trước Spawn của chính EntityId/generation đó. Host công bố spawn sau load/commit
thành công, không phải theo thứ tự số ID được cấp.

Message cho A chỉ cần A đã sẵn sàng. Entity khác là attribution/reference bằng ID
hoặc dữ liệu đã chụp; không đòi object B đã load. Presenter bind theo EntityId nhưng
lease giữ object/generation cụ thể; khi B biến mất/đổi generation phải unbind, trả
tài nguyên rồi bind lại. Không chặn luồng để chờ B.

Updated chỉ được gộp trong đoạn liên tiếp của cùng instance, không vượt qua cue,
Added hoặc Removed. Gộp ở host trước outbox. Test đối chiếu trạng thái cuối và trạng
thái quan sát tại từng cue, không chỉ trạng thái cuối.

Outbox vượt giới hạn: bỏ phần chưa được transport chấp nhận, chuyển sang chờ chụp
snapshot. Không thu hồi phần đã được reliable pipeline nhận. Trước mốc chụp, không
xếp delta mới cho kết nối đó; chúng được bao gồm trong snapshot. Sau mốc chụp,
delta mới vào hàng chờ riêng có giới hạn.

SnapshotStart → các chunk → SnapshotEnd: không xen message gameplay/state delta.
Control loại được allowlist (IntentResult, lỗi/close và marker thay thế) được phép
xen trên **cùng stream0**, theo sequence chung, không làm thay đổi chunk count/hash.
Client xử lý IntentResult theo IntentId/incarnation ở pending-UI layer, không áp
inventory/HP/actor state hoặc yêu cầu object entity đã load. Accepted chỉ resolve
outcome; giá trị gameplay/UI authoritative lấy từ snapshot/delta sau gate. Result
được xử lý cả khi Lắp/Preparing/Phục hồi; Failed/Closed resolve pending bằng trạng
thái connection và bỏ dữ liệu incarnation cũ. Control có rate/byte/queue budget và
reserved bandwidth; ordered transport vẫn có HOL, không hứa result tới tức thì.
Marker của
snapshot mới là ngoại lệ để thay bản dở dang; nó cũng đi sau mọi phần đã được
transport chấp nhận. Client bỏ bộ lắp cũ khi thấy marker mới. Sau End host chờ
SnapshotApplied hợp lệ ở trạng thái Chờ SnapshotApplied rồi mới xả delta. Ack gồm connection incarnation, snapshot ID
và sequence của End; phải khớp bản hiện hành đã được host gửi. Ack cũ/trùng được bỏ
và đếm; ack cho bản chưa gửi/không hợp lệ không mở quyền nhận intent.
Hàng delta chờ vượt giới hạn thì chụp bản mới; deadline tổng không được reset.

## 4. Bảng host theo từng kết nối

| Trạng thái | Sự kiện/hành động | Chuyển trạng thái |
| --- | --- | --- |
| Bắt tay | Kiểm protocol/content/hash, policy chung và capability client; gắn identity, cấp incarnation; quá hạn hoặc không đáp ứng thì đóng | Thành công → Chờ chụp; lỗi → Đã đóng |
| Gửi bình thường | Gộp hợp lệ, đưa vào outbox, retry cả lô khi send queue đầy | Outbox vượt giới hạn/yêu cầu phục hồi → Chờ chụp |
| Chờ chụp | Gộp yêu cầu trùng; ngừng xếp delta; chụp ở commit boundary | Đã chụp → Đang gửi snapshot |
| Đang gửi snapshot | Gửi marker/chunk/End theo thứ tự; delta sau mốc chụp chờ riêng | End được chấp nhận → Chờ SnapshotApplied; vượt giới hạn → Chờ chụp mới |
| Chờ SnapshotApplied | Giữ delta ở host; xác minh ack đúng bản hiện hành; ack timeout nằm trong deadline mỗi lượt | Ack hợp lệ → Xả delta; timeout(3007)/vượt giới hạn → lượt Chờ chụp mới nếu còn ngân sách; exhausted → Đã đóng |
| Xả delta chờ | Delta mới nối sau hàng cũ; không vượt thứ tự | Hàng hết → Gửi bình thường; vượt giới hạn → Chờ chụp mới |
| Đã đóng | Hủy hàng/callback/tài nguyên kết nối, bỏ dữ liệu muộn | Không tự mở lại; reconnect bắt tay mới |
| Mọi trạng thái còn mở | Disconnect hoặc deadline/ngân sách toàn chu trình hết | Đã đóng |

Yêu cầu snapshot có ID/mốc và được rate-limit. Khi đang chờ/gửi, yêu cầu tương đương
được gộp; chỉ thay khi có lý do phục hồi hợp lệ. Host xác minh ID/mốc, không tin một
con số client tự khai lớn hơn. Deadline cả chu trình và số lần phục hồi trong cửa sổ
thời gian ngăn vòng chụp-gửi lại chạy mãi. Chu trình bắt đầu lần đầu vào Chờ chụp,
gồm các lượt có deadline riêng. Mỗi lần chụp lại tiêu thụ một lượt; yêu cầu trùng
được gộp không tiêu thụ lượt. Deadline toàn chu trình không reset; một lượt mới chỉ
bắt đầu nếu còn đủ ngân sách cho nó. Mọi thời gian chờ/backoff đều được tính vào
deadline lượt, không chèn khoảng chờ ngoài ngân sách. Đóng kết nối không tự despawn player.

Nhiều kết nối chờ tại cùng commit boundary dùng chung snapshot bất biến nếu cùng
scope/quyền nhìn thấy; sequence, chunk tiến độ và outbox vẫn riêng từng kết nối.

Hướng client→host được xử lý riêng với trạng thái gửi: bắt tay/control phục hồi/ack
chỉ được nhận ở trạng thái cho phép và có rate-limit. Gameplay intent chỉ được xét
khi SnapshotApplied của bản hiện hành đã được xác minh và kết nối ở Xả delta chờ
hoặc Gửi bình thường. Trước đó không thực thi và không backlog để áp sau: realtime
intent được bỏ/đếm, transactional intent trả Rejected(NotSynchronized) theo contract
intent riêng. Việc xét
intent vẫn kiểm ownership, generation, readiness và schema; ack không cấp authority.
Peer gửi malformed/oversize/spam bị giới hạn byte, messages/sec và inbound queue.
Lỗi một peer đóng riêng kết nối của peer đó, không làm dừng host.
Client đóng cổng nguồn phát gameplay intent khi không Live; chỉ bật sau commit và
đã xếp SnapshotApplied trước intent trên luồng gửi. Intent cũ đang bay được host bỏ
và đếm riêng, không tự tăng bộ đếm vi phạm rate chỉ vì đang unsynced. Byte/rate/inbound
budget thực vẫn bảo vệ host trước spam; control/ack phục hồi có nguồn phát riêng.
Intent có hệ quả giao dịch/UI (dùng vật phẩm, mua đồ...) sẽ có IntentId và kết quả
authoritative Accepted/Rejected, kể cả NotSynchronized. Client không commit UI trạng
thái gameplay giao dịch từ result; chờ state authoritative áp dụng. Disconnect/timeout
pending là Unknown/ConnectionLost, không tự kết luận host đã rollback. Dedup/idempotency,
giới hạn lưu kết quả và retry thuộc contract intent riêng, chưa triển khai ở đây.
Host không áp intent bị từ chối sau khi sync xong. Cổng nguồn vẫn cần để tránh phát
thao tác trong recovery; intent tần suất cao không mặc định ack từng message.

## 5. Snapshot và client

Snapshot chứa đủ trạng thái tương ứng với mốc chụp: identity còn sống/dormant,
generation hiện hành, descriptor khởi tạo và trạng thái của các subsystem bắt buộc.
Không dùng sequence mà thiếu trạng thái để bỏ lịch sử. Chụp sau khi các đợt mutation
liên quan đã drain ở ranh giới commit chung; không chụp giữa các lệnh Buff còn pending.
Serialize/chia chunk từ bộ bất biến, không đọc lại live state.
Hash snapshot chỉ kiểm dữ liệu khớp hash, không xác thực người gửi/chống giả mạo;
authentication thuộc adapter/admission, không suy ra từ SHA-256.
Chi phí chờ boundary và chụp đồng bộ có budget/nguồn đo riêng; snapshot capture vượt
ngưỡng phát cảnh báo có tên scope, content fingerprint và số entity/byte.

| Trạng thái bộ đồng bộ | Hành động |
| --- | --- |
| Chờ snapshot đầu | Chỉ nhận giao thức bootstrap; không nhận gameplay trước bootstrap |
| Lắp snapshot | Nhận chunk/End đúng snapshot ID, Start thay thế và allowlisted control; gameplay xen chunk là SnapshotInvalid(3005); kiểm size/count/hash; có timeout riêng |
| Chuẩn bị/chờ commit | Giữ trạng thái cũ; host chưa xả delta vì chưa có ack. Gameplay sau End trước ack là vi phạm protocol, không tự áp. Commit xong mới gửi SnapshotApplied |
| Live | Dispatch theo entity; world/room/session state dispatch ngay theo thứ tự luồng, không phụ thuộc object entity. Entity Ready khác tiếp tục khi một entity đang load |
| Phục hồi | Ngừng áp trạng thái; tiêu thụ sequence nhưng bỏ gameplay tới SnapshotStart; xử lý IntentResult/control riêng; presenter cũ tiếp tục dữ liệu đã có; xin snapshot bounded |
| Failed/Closed | Khóa dispatch ngay, vô hiệu token, cleanup; không đợi transport đóng xong |

Snapshot mới thay bộ lắp cũ. Ack loại nhu cầu giữ hàng delta toàn cục suốt preparation.
SnapshotStart hợp lệ mới hơn được xử lý cả khi Live hoặc Chuẩn bị/chờ commit: khóa
intent/dispatch, vào Lắp S2, giữ presentation hiện hành; hủy phần chuẩn bị không còn
được yêu cầu và giữ candidate cùng khóa theo token. Ack S1 tới host sau khi S1 đã
bị thay không mở lại luồng. Start trùng/cũ được bỏ và đếm; Failed/Closed không mở lại.
Control message (close/lỗi nghiêm trọng/Start thay thế) vẫn xử lý trong phục hồi;
transport keepalive vẫn pump. Bỏ gameplay trong phục hồi không gây SequenceGap vì
sequence nhận vẫn được tiêu thụ. Quy tắc không drop message ở reliable stream áp dụng
cho phía gửi; phía nhận chỉ được bỏ trùng/cũ hoặc bỏ trạng thái khi đang phục hồi.
Snapshot commit đối soát: giữ object cùng khóa, cập nhật bằng state mới; generation
đổi hoặc entity biến mất thì cleanup lặng. Snapshot không phát cue lịch sử, không
đoán end reason cho entity/buff biến mất. Buffer vượt giới hạn chỉ phục hồi bằng
snapshot đầy đủ; snapshot theo entity là tối ưu chưa thiết kế.

| Trạng thái lần hiện diện | Spawn/delta/despawn và hết hạn |
| --- | --- |
| Chưa có spawn | Message gameplay cho khóa chưa công bố là lỗi protocol; không thêm buffer trước spawn |
| Preparing/Prepared | Spawn đã được chấp nhận; message của khóa này chờ riêng. Despawn/generation mới hủy nhu cầu load; kết quả muộn phải cleanup |
| Ready | Áp message theo thứ tự. Spawn cùng tuple trên sequence mới là InvalidEntityLifecycle(2004); packet trùng sequence đã lọc trước. Spawn generation cao hơn bỏ ánh xạ cũ và cleanup lặng |
| Dormant | Giữ descriptor identity/character/owner/generation cuối, không giữ GameObject hoặc presenter; TryGet không trả object. Gameplay delta bị từ chối(2004); spawn hiện diện mới phải có generation lớn hơn |
| Lỗi load | Ghi generation lỗi, báo mã lỗi, đóng kết nối client; không tự retry cùng generation |
| Đã đóng | Bỏ gói cùng/cũ hơn. Generation cao hơn chỉ mở presence khi logical identity còn sống theo lifecycle/snapshot; terminal identity không tái dùng EntityId |
| Cleanup/cách ly | Không nhận gameplay. Cleanup thành công mới về pool; lỗi giữ khóa cũ ngoài pool, retry có giới hạn |

Đối soát Ready→Dormant dọn lặng object và giữ descriptor. Despawn của identity Dormant
→ Đã đóng, không tạo object/cue; Dormant biến mất khỏi snapshot cũng → Đã đóng.
Nếu host chỉ tạm ngừng hiện diện nhưng giữ logical identity cho chuyển floor, phải
công bố trạng thái Dormant tường minh, không dùng terminal Despawn. Generation mới
thuộc cùng logical identity còn sống; entity mới sau kết thúc nhận EntityId mới.

Snapshot đang lắp chưa công bố các ID trong nó. Cache prepared dùng khóa
(EntityId,generation) trong một connection incarnation. S1 thay bằng S2 vẫn dùng lại
object cùng khóa; trạng thái phải cập nhật từ S2 trước commit. Cache inactive/chưa
publish không tick, không cue, không renderer. Cleanup khi không còn trong snapshot
mới, khi commit xong phần thừa, hoặc kết nối đóng. Khi nhận End của S2, evict các
object Prepared của S1 không còn khớp trước khi bắt đầu allocation/prepare mới;
không evict object Ready cũ đang giữ presentation tới commit. Cleanup cache lỗi vẫn
tính vào resident/quarantine budget, không được coi là đã giải phóng bộ nhớ.

Token tác vụ=(connection incarnation,EntityId,generation). Load muộn chỉ được commit
khi token còn hợp lệ và còn được yêu cầu. Token không phụ thuộc snapshot ID riêng,
để chuyển kết quả chuẩn bị cùng khóa cho snapshot mới.

## 6. Prepared/Commit và directory

API runtime phần này đã có: BeginPrepare/PrepareNext/Prepare/CommitBatch, IsInitialized
so với IsReady, context CanUse/CanMutate và AllowsModuleAccess chỉ do loader cấp.
Cleanup được cấu hình tường minh, có pump Unity độc lập Ready và cap theo test/profile.
Các phép đo và giá trị policy dưới đây chưa được chọn làm mặc định production.

Loader mục tiêu: Idle → Preparing → Prepared → Committing → Ready; hủy/lỗi đi qua
Unloading và tới Idle hoặc Failed. Không coi các state này là API đã triển khai.

Reserve khóa trước load, giữ trong tracking để chặn trùng và cleanup session end.
Directory có registry mọi presence theo tuple và một ánh xạ gameplay hiện hành.
TryGet(EntityId) chỉ trả presence hiện hành đã publish Ready, không fallback Ready
cũ khi generation mới đã thay nó. Object cách ly vẫn được tracking nhưng không
nhận gameplay. Remove reserve chỉ khi cleanup thành công.

Prepare chạy preparation/dependency-ordered Load step; ghi nhận step trước khi gọi.
Load convenience của host/solo là Prepare+Commit liền nhau. Load khi Prepared cùng
context trả về, không tự commit; context/session sai vẫn bị từ chối. OnEnable bỏ qua
Preparing/Prepared/Committing, và không lặp Load khi Ready.

Một context dùng cho một lần load. IsPublished chỉ chuyển sang true tại publish;
IsValid bị vô hiệu trước unload/hủy. Guard chung:
`context.IsValid && context.IsPublished && context.Session.IsActive`, cộng authority
khi API yêu cầu. Module initialized riêng phục vụ nội bộ preparation, không phải
quyền tick/mutation/presentation. Buff, Control, Ability và các step liên quan phải
đọc cùng tín hiệu; không tự suy luận từ collection khác.

Commit hook chỉ khởi tạo owned state chưa công bố; không gây damage/network/world
side effect trước publish. Việc essential có thể lỗi không nằm trong OnEnable.
Mọi việc nặng (parse/validate, cấp tài nguyên, dựng candidate state cho object mới
và entity Ready được giữ lại) phải chạy ở Prepare. Commit chạy đồng bộ trong **một
main-thread turn/frame**, chủ yếu đổi tham chiếu/state đã chuẩn bị. Không chia commit
qua nhiều frame: entity Ready được giữ lại cũng không được lộ state nửa mới. Trong
turn đó khóa dispatch và trì hoãn observer/presentation notification tới khi toàn
tập publish; không render/callback ngoại vi giữa các lần đổi state. Hook custom phải
tuân thủ giới hạn này; mod là trusted code, không có preemption cho hook treo.
Đo commit riêng và cảnh báo quá ngưỡng; cộng một frame trong công thức không chứng
minh commit rẻ. Đo quá lớn thì giảm scope/bound hoặc đẩy việc sang Prepare.
CommitFrameBudgetMs là ngân sách mềm/tiền điều kiện đo, không preempt hook đồng bộ.
Profile cần đáp ứng bound tổng commit gồm cả entity Ready được giữ. Một lần elapsed
vượt soft budget: warning có step/profile/count/cost, **vẫn publish nếu commit hợp lệ**.
CommitHardLimitMs cao hơn rõ rệt và CommitOverrunConsecutiveLimit là policy riêng,
chọn từ fixture, không chốt multiplier/số lần ở đây. Vượt hard limit hoặc overrun
liên tiếp chạm ngưỡng là EntityCommitFailed trước publish; exception/logical commit
lỗi vẫn đóng ngay. Chụp lại snapshot không reset bộ đếm overrun; commit trong budget
mới reset consecutive count. Frame đã chặn không thể thu hồi; không phân loại một
GC/OS pause nhẹ thành lỗi entity chỉ vì vượt soft budget.
Observer/presentation flush đo riêng; trì hoãn không có nghĩa chi phí của nó bằng0.
Đặc biệt PNBuffReplica.PresentationChanged khi đối soát snapshot phải được xếp vào
hàng notification của commit và xả đúng thứ tự sau khi toàn tập publish. Observer
không được nhìn snapshot áp dở qua callback của replica.
Commit toàn tập giữ cổng publish đóng tới khi mọi hook thành công. Hook thứ k lỗi:
khóa kết nối/dispatch trước, cleanup toàn bộ object mới; không đòi đảo ngược nghiệp
vụ từng hook. Unload phải dọn được tài nguyên cấp trong load/commit dở.

Sau publish cả tập, Ready event dùng PublishSafely, là thông báo ngoài ranh giới
atomic. Observer lỗi không rollback. Mọi entity trong tập đã Ready trước observer;
entity ngoài tập vẫn có thể vắng và phải được xử lý như reference tùy chọn.

## 7. Cleanup, pool và hiệu ứng thả

Tách vô hiệu hóa/ẩn khỏi cleanup nặng: context/token và gameplay mapping mất hiệu
lực ngay, render cũng ẩn ngay. Object chờ dọn/đang dọn/quarantine vẫn tính vào resident.
Scheduler clock thực chạy độc lập active-session/authority, được pump cả khi Stopping
và disconnect. Mỗi frame ít nhất một runnable cleanup step dù ngân sách còn lại0;
step đồng bộ không bị preempt. Phần việc thêm chịu trần step/frame và thời gian.
Backlog có hard cap và ngưỡng backpressure chặn prepare mới, không bỏ Despawn/hủy load.
Một ticket cho mỗi resident; capacity phải đủ retirement toàn tập trước commit.
Prototype yêu cầu MaximumCleanupBacklog >= MaximumResident để mọi object đang giữ
đều có thể vào hàng dọn, không spill queue không được tính. Scheduler chia phần
ngân sách còn lại với prepare/serialize; cleanup có ưu tiên tiến triển, prepare có
thể hoãn. Chỉ trả pool sau cleanup thành công. Thử lại bước lỗi bounded, vẫn dọn các
bước khác; exhausted giữ quarantine/Blocked đến khi owner retry hoặc yêu cầu restart.

CleanupModel hiện kiểm điều này bằng fake clock và flags; chưa nối renderer/loader
production. Native Unity phải kiểm ẩn render thật và pooling khi bước tích hợp tới.

Despawn khóa generation và dọn lặng. Removed có end reason hiển thị/cue cần thiết
được xử lý trước nó trong cùng luồng. Unload/SessionEnded/snapshot disappearance
không tạo hiệu ứng kết thúc. Source grouping Buff tiếp tục dùng Source.EntityId
bất kể generation; giữ Buff qua floor/save vẫn cần policy riêng.

Hiệu ứng kết thúc có thời lượng dùng hệ hiệu ứng thả: chụp position/direction/state
ở Removed, sở hữu resources riêng, không giữ entity/presenter lease. Hết ngân sách
thì bỏ hiệu ứng mới. Runtime cưỡng chế thời lượng tối đa; disconnect/session end dọn
tất cả. Không trì hoãn trả entity về pool để chờ hiệu ứng này.

Cleanup ngược thứ tự, tiếp tục sau lỗi một resource. Object lỗi ngoài pool, giữ tuple,
retry bounded; báo ngay số object cách ly >0 cho UI/diagnostics và ngưỡng tăng cao.
Generation mới không dùng object cách ly của generation cũ.

Kết thúc phiên vẫn còn cleanup lỗi: giữ cơ chế Stopping của registry session, đánh
dấu CleanupBlocked/RestartRequired, ngừng retry tự động và báo UI. Không bỏ rơi object
để mở phiên mới. RetryCleanup có thể phục hồi khi nguyên nhân được giải quyết;
cleanup toàn phiên thành công mới bắt đầu phiên khác. Object lỗi giữa phiên không
tự dừng host, nhưng có thể chặn phiên kế tiếp khi EndSession.

## 8. Policy và validation

Không chốt con số mặc định trước lượt đo fixture đại diện. Tất cả deadline/timeout
đo bằng clock monotonic thực, không dùng Game/Entity time có thể bị TimeStop.

| Nhóm | Trường cần có | Ràng buộc |
| --- | --- | --- |
| Wire/outbox | max message/batch bytes, queue bytes/count, batch count, MaxOutboxAcceptedBytes | Dương, checked; accepted/in-flight bound do adapter bảo đảm, không chỉ app outbox |
| Snapshot | max entities/body+wire bytes/chunks, ChunkPayloadBytes, chunk envelope/encoding overhead, assembly timeout | Payload+overhead fit message/batch cap; không interleave gameplay, allowlisted control được xen |
| Delivery estimate | max wire snapshot bytes, MinimumAssumedEffectiveGoodputBytesPerSecond, pacing budget, host serialize budget, assembly-finalize budget | Prior drain suy ra từ accepted byte cap; goodput dùng cùng cơ sở wire byte, là giả định đo |
| Capture | capture wait-and-work budget, capture warning threshold, measured freeze cost at max world, host baseline/FPS profile | Freeze cùng turn phải fit frame host; snapshot bất biến chia sẻ khi cùng scope |
| Serialize | HostSerializeBudgetMs tổng, HostSerializePerFrameBudgetMs, measured work/slice overrun, host FPSmin | Serialize/chia chunk bất biến chia lát qua frame; không đọc Unity/live state lại |
| World representation | MaximumWorldIdentities, Buff/subsystem payload và global-state byte/count bounds | Host world + reserved new identity ≤ MaximumSnapshotEntities; mọi state hợp lệ biểu diễn được |
| Preparation | E entities/frame, L steps/frame, B ms/frame, FPSmin | Dương; scheduler tiến triển ít nhất một runnable step khi còn việc |
| Frame baseline | BaselineFrameCostMs, measured single-step overrun, peak notification/pump cost | Cùng hardware/content profile; chừa nền trước prepare/commit, không cộng timer chồng nhau |
| Preparation/cache | max prepared entities, per-entity timeout, snapshot prepare+commit deadline | Cache ≥ MaximumSnapshotEntities; chỉ evict object không còn cần |
| Commit | soft budget, CommitHardLimitMs, consecutive limit, measured fixed/entity/retained swap cost, notification budget | Estimate fit soft+baseline; một turn; mềm warning, lỗi cứng/repeated/exception mới đóng |
| Resident/pool | max Ready, max Prepared, quarantine reserve, max idle cached, max resident entities/estimated memory | Chịu ít nhất old N + new N; pool retention không đồng nghĩa resident allocation cap |
| Client queues | per-entity và tổng bytes/count, wait deadline | Vượt giới hạn → phục hồi toàn snapshot; không drop lẻ |
| Recovery | handshake timeout, RecoveryRoundDeadlineMs, MaximumRoundsPerRecovery, RecoveryCycleDeadlineMs, cycles/window, request rate | Một lượt có deadline riêng; cả chu trình bao các lượt, không reset tổng |
| Snapshot ack/delta | ack timeout/budget, max delta bytes/sec và messages/sec, burst headroom, host pending bytes/count, drain budget | Host giữ delta từ mốc chụp tới ack; capacity/goodput đủ cho max snapshot |
| Client→host | max intent bytes/batch, messages/sec, bytes/sec, inbound queue bytes/count, processing budget | Rate-limit trước dispatch; intent chỉ xét sau ack; đóng riêng kết nối vi phạm |
| Cleanup | max backlog, preparation pause threshold, steps/frame/lease, soft ms/frame, retry count/interval, quarantine warnings | Backlog ≥ resident; hide ngay, resident giữ tới dọn xong; tối thiểu1 runnable step/frame kể cả Stopping; exhausted giữ cách ly |
| Detached VFX | max active count, absolute max lifetime | Bỏ mới khi đầy; session/disconnect cleanup |
| Diagnostics | max error/step-name bytes, reports/window, log budget, slow-step/capture/commit thresholds | Unknown code log bounded; diagnostic text không quyết định gameplay |
| Dormant retention | owner/scope/expiry declaration, dormant count/ratio warning thresholds | Mode kết thúc retention bằng terminal Despawn; không tích lũy quái floor cũ vô hạn |
| Host aggregate | MaximumConnections, MaximumHostRetainedSnapshots, retained wire/memory byte budgets, MaximumHostConnectionBufferBytes | Preflight tổng outbox/accepted/pending/inbound/snapshot kể cả shared copies và temporary serialization |
| Host bandwidth | HostUplinkGoodputAssumptionBytesPerSecond, worst-case per-connection goodput, reserved control bandwidth | Per-peer floor khi MaximumConnections cùng recovery, không lấy throughput một peer độc lập |

Bắt tay có contract policy chung: host quy định immutable WirePolicyProfile (count/
byte/chunk/deadline/rate bounds), có version và fingerprint; client kiểm scheduler,
FPS/cost profile/cache của mình đáp ứng profile rồi mới chấp nhận. E/L/B local có
thể khác nếu vẫn đáp ứng bound chung; host không dùng deadline local tự suy đoán.
Hai phía dùng cùng deadline prepare+commit/ack và giới hạn snapshot đã chấp nhận để
kiểm các công thức. Không đáp ứng → PolicyMismatch(1004), không vào phục hồi.
Không đổi profile âm thầm giữa kết nối; phải bắt tay lại hoặc dùng protocol đổi
profile riêng chưa thiết kế. Capability client là lời khai phục vụ admission, không
cho host bỏ các hard cap của mình hoặc tin client có authority.

Hai giai đoạn validation:

1. Khi nạp policy: miền giá trị, quan hệ cache/snapshot, wire/batch/chunk, arithmetic.
2. Sau khi dựng/validate toàn bộ content và code mod đã chọn, trước Ready: kiểm lại
   bound N/S, fingerprint và dữ liệu đo W. Snapshot runtime cụ thể kiểm lại count,
   bước và deadline trước khi bắt đầu preparation.

Registry session hiện seal sau registration; không có hot-load mod tùy ý sau Ready.
Thay content/mod theo session mới phải chạy validation lại. Nếu tương lai thêm hot-load,
nó phải đi qua cùng validation trước khi công bố; không tự cho phép bằng contract này.

N=count entity cần chuẩn bị; S=tổng synchronous load step liên quan; W=tổng ms đo
ước lượng synchronous work; E/L/B là các giới hạn trên mỗi frame.

```text
framesHardMin = max(ceil(N/E), ceil(S/L)) + 1 frame commit
framesEstimate = max(ceil(N/E), ceil(S/L), ceil(W/B)) + 1
deadlineHardMinMs = ceil(1000 * framesHardMin / FPSmin)
```

Deadline bằng hard minimum hợp lệ về cấu hình, thấp hơn1ms bị từ chối. Estimate
không phải cận dưới hay bảo đảm tiến độ: step có thể vượt B trong một frame, packing
và dependency có thể kéo dài hơn. Deadline vận hành thêm dư địa theo đo thực tế.
Deadline prepare+commit bắt đầu khi đã nhận đủ và xác minh chunk cuối; assembly có
deadline riêng. Đo step vượt ngưỡng và cảnh báo tên step/entity/generation.

Ràng buộc chu trình và delta, đơn vị thời gian đồng nhất, arithmetic checked:

```text
PriorDrainMs = ceil(1000 * MaxOutboxAcceptedBytes /
                    MinimumAssumedEffectiveGoodputBytesPerSecond)
TwirePlanMs = ceil(1000 * MaximumSnapshotWireBytes /
                   MinimumAssumedEffectiveGoodputBytesPerSecond)
AssemblyTimeoutMs >= TwirePlanMs + AssemblyFinalizeBudgetMs
SnapshotAppliedTimeoutMs >= PriorDrainMs + AssemblyFinalizeBudgetMs
                            + PrepareCommitOperationalDeadlineMs + AckRoundTripBudgetMs
TdeltaHoldMs = PriorDrainMs + HostSerializeBudgetMs + SnapshotPacingBudgetMs
               + TwirePlanMs + SnapshotAppliedTimeoutMs
HostPendingDeltaBytes >= DeltaBurstBytes
                        + ceil(MaximumDeltaBytesPerSecond * TdeltaHoldMs / 1000)
HostPendingDeltaCount >= DeltaBurstCount
                        + ceil(MaximumDeltaMessagesPerSecond * TdeltaHoldMs / 1000)
MinimumAssumedEffectiveGoodputBytesPerSecond > MaximumDeltaBytesPerSecond
TdrainPlanMs = ceil(1000 * HostPendingDeltaBytes /
                   (MinimumAssumedEffectiveGoodputBytesPerSecond - MaximumDeltaBytesPerSecond))
DeltaDrainBudgetMs >= TdrainPlanMs
RecoveryRoundDeadlineMs >= CaptureWaitAndWorkBudgetMs + TdeltaHoldMs + DeltaDrainBudgetMs
RecoveryCycleDeadlineMs >= MaximumRoundsPerRecovery * RecoveryRoundDeadlineMs
```

AssemblyTimeout tính từ Start nhận được và bao truyền/lắp, không dùng làm ack bound.
Ack timer bắt đầu khi transport chấp nhận End; PriorDrain bao outstanding accepted
wire bytes trước/bao gồm End. Cap phải bao app/driver/pipeline, không chỉ hàng app;
AckRoundTripBudget bao propagation/receive-pump/ack-return không thuộc byte drain.
Twire trong TdeltaHold
bound thời gian host từ bắt đầu gửi tới End được chấp nhận; ack timer chỉ bắt đầu
sau đó. Đây là hai khoảng chờ tuần tự được bound bảo thủ, không giả định ack timer
bắt đầu lúc chụp. HostSerializeBudget bao serialize/chia chunk sau mốc chụp. PriorDrain
suy ra từ cap accepted/in-flight; adapter phải xác minh/áp được cap này khi triển khai.
Wire bytes là byte tại biên adapter, gồm serialized envelope/batch/chunk headers.
Snapshot/delta dùng cùng cơ sở; overhead transport/retransmit được phản ánh trong
effective delivered goodput đo. Mọi rate byte dùng BytesPerSecond, không bit/sec.
Goodput thực dưới giả định có thể làm deadline thất bại; runtime cảnh báo/phục hồi
bounded, không tuyên bố policy hợp lệ bảo đảm mọi mạng. Bounds delta/burst có nguồn
đo và giám sát; không giả định cue/Added/Removed đều coalesce được. Capacity trên là
cận bảo thủ trước coalescing. Rate vượt budget dùng recovery bounded, không drop lẻ.
Goodput phải lớn hơn rate delta để Xả delta chờ có khả năng kết thúc; không đủ dư địa
thì policy không hợp lệ, không đợi một vòng phục hồi chắc chắn không xả được.

Ràng buộc uplink dùng cùng cơ sở delivered adapter wire bytes:

```text
MaximumConnections * MinimumAssumedEffectiveGoodputBytesPerSecond
    + HostReservedControlGoodputBytesPerSecond <= HostUplinkGoodputAssumptionBytesPerSecond
```

Per-peer floor chỉ hợp lệ khi scheduling/pacing của host và profile đường lên hỗ trợ
nó lúc mọi peer cùng recovery. Shared snapshot không giảm byte gửi mỗi peer. Goodput
được xác minh bằng adapter nhiều peer; mô phỏng chỉ kiểm ràng buộc theo giả định.
Dòng uplink delta riêng không cần: nó đã được suy ra từ bound uplink goodput và
MinimumAssumedEffectiveGoodputBytesPerSecond > MaximumDeltaBytesPerSecond.

Ràng buộc capture/serialize trên host và chunk capacity:

```text
HostBaselineFrameCostMs + MeasuredCaptureFreezeMsAtMaximumWorld <= 1000 / HostFPSmin
HostBaselineFrameCostMs + HostSerializePerFrameBudgetMs
    + MeasuredSerializeSliceOverrunBoundMs <= 1000 / HostFPSmin
HostSerializeBudgetMs >= ceil(1000 * ceil(MaximumSerializeWorkAheadAndOwnMs /
                           HostSerializePerFrameBudgetMs) / HostFPSmin)
MaximumSnapshotChunks >= ceil(MaximumSnapshotWireBytes / ChunkPayloadBytes)
ChunkPayloadBytes + MaximumChunkEnvelopeOverheadBytes <= MaximumMessageWireBytes
MaximumSnapshotEncodedBodyBytes + MaximumSnapshotChunks * MaximumChunkEnvelopeOverheadBytes
    + SnapshotStartEndOverheadBytes <= MaximumSnapshotWireBytes
```

Capture freeze tại commit boundary vẫn là một turn đồng bộ; bộ immutable dùng chung
cùng scope. Đo ở max world và kiểm baseline host. Không fit thì giảm trần world/lượng
state hoặc cải thiện capture, không nới ngân sách frame để che lỗi. Serialize/chia
chunk chia lát sau freeze với budget mỗi frame/deadline tổng; không giữ live Unity
reference để đọc ở frame sau.
V1 chỉ freeze một unique scope snapshot mỗi frame; peer cùng scope share kết quả,
scope khác chờ FIFO/fair trong CaptureWaitAndWorkBudget. Serialize bắt đầu từ frame
sau freeze để không cộng hai việc vào một frame vượt bound riêng. Budget serialize
là tổng **toàn host**, không cấp nguyên budget này cho mỗi connection. Queue bounded
và scheduler không starvation; MaximumSerializeWorkAheadAndOwnMs bao job đang chạy,
job ahead và job của snapshot này ở max connection/scope, không chỉ W của một peer.
Serialize work/B là estimate theo profile, không hard bound cho slice; đo pacing/
overrun và chừa dư địa tổng. ChunkPayloadBytes là byte body đã encode mang trong
chunk; overhead envelope/further encoding được sizer tính riêng. WireBytes trong
bound chunk count là bảo thủ; exact sizer vẫn kiểm body+headers và count trước gửi.
IntentResult không thuộc snapshot hash/body/count, nhưng tính vào reserved control
bandwidth và connection bytes.

Tổng host dùng bound worst-case, không dựa vào giả định mọi peer chia sẻ snapshot:

```text
MaximumHostConnectionBufferBytes >= MaximumConnections *
    (MaxOutboxQueuedBytes + MaxOutboxAcceptedBytes + HostPendingDeltaBytes
     + MaximumInboundQueuedBytes + MaximumPendingEncodedBatchBytes)
MaximumHostRetainedSnapshots >= MaximumConnections * MaximumSnapshotsRetainedPerConnection
MaximumHostRetainedSnapshotWireBytes >= MaximumHostRetainedSnapshots * MaximumSnapshotWireBytes
MaximumHostRetainedSnapshotMemoryBytes >= MaximumHostRetainedSnapshots * MeasuredSnapshotMemoryBoundBytes
```

MaximumSnapshotsRetainedPerConnection tính cả candidate cũ chưa nhả reference,
candidate mới và serialization temporary theo implementation, không mặc định là1.
Shared immutable snapshot tính memory một lần thực tế, nhưng admission/preflight
phải vẫn chịu trường hợp scope/mốc khác nhau không share được. Byte buffer cap không
thay bound native transport allocations; fixture/adapter ghi và tính chúng vào
ngân sách memory tổng host. Quá capacity → không nhận thêm connection/preparation,
báo mã limit; không cố cấp tài nguyên rồi lặp recovery vô ích.

Trần commit theo số liệu measured/profile, kể cả retained entity:

```text
MeasuredCommitFixedMs + MaximumSnapshotEntities * MeasuredCommitPerEntityMs
    + MeasuredRetainedStateSwapMs <= CommitFrameBudgetMs
BaselineFrameCostMs + B <= 1000 / FPSmin
BaselineFrameCostMs + CommitFrameBudgetMs <= 1000 / FPSmin
BaselineFrameCostMs + B + MeasuredSingleStepOverrunBoundMs <= 1000 / FPSmin
```

Không tính trùng retained cost nếu nó đã nằm trong per-entity bound. Bounds phải
từ fixture đúng content/backend/hardware profile; thiếu hoặc cũ thì chưa xác minh.
Trần này giữ một frame commit trong công thức; overrun thực có thể do GC/OS/mod
hook, được đo trước publish: soft overrun warning, hard/repeated hoặc logical failure
mới fault theo mục6, không phải bảo đảm hard real-time. CommitHardLimitMs phải lớn
hơn soft budget, consecutive limit dương; ngưỡng/ratio cụ thể chọn sau đo.
Baseline là active critical-path frame work có gameplay/render/pump và peak
notification profile, loại thời gian chờ frame cap/vsync; không cộng CPU/GPU timers
chồng nhau. Hai bound nền đầu là tối thiểu; dòng thứ ba chừa dư địa vì B chỉ chặn
bắt đầu step, một step vẫn có thể vượt B. Fixture kiểm actual frame dưới profile;
không hứa chống OS pause hoặc step mod treo.

Ràng buộc resident tính cả cache chưa publish và cleanup lỗi:

```text
MaximumPreparedEntities >= MaximumSnapshotEntities
MaximumReadyEntities >= MaximumSnapshotEntities
MaximumResidentEntities >= MaximumReadyEntities + MaximumPreparedEntities
                          + QuarantineReserve + MaximumIdleCachedEntities
```

Snapshot tối đa toàn present có thể giữ N Ready cũ và N Prepared mới cùng lúc.
Memory budget có ước lượng footprint entity/content mix từ fixture, không chỉ đếm
object hoặc nhìn pool rảnh. Preflight/cấp object không vượt hard resident cap;
quarantine làm thiếu dung lượng thì dừng preparation và báo lỗi resource/prepare
rõ ràng, không lặp resync không thể hoàn tất. Cache S1 không còn cần được evict
trước prepare S2; Ready cũ vẫn giữ tới commit, nên không trừ chúng khỏi cao điểm2N.

Trần resident client không thay trần world host. Với v1 snapshot toàn scope:

```text
MaximumWorldIdentities <= MaximumSnapshotEntities
LiveWorldIdentities + ReservedNewIdentitySlots <= MaximumWorldIdentities
MaximumSnapshotWireBytes >= MaximumMeasuredSnapshotWireBytesAtConfiguredStateBounds
```

Count gồm present+dormant và slot identity mới reserve trước load; thay generation
cùng identity hoặc Ready→Dormant không tăng logical count. Chặn identity mới trước
spawn/publication khi hết slot; không xóa dormant còn sống để lấy chỗ. Failed host
load không công bố entity; slot được giải phóng theo cleanup lifecycle, EntityId
đã cấp vẫn không tái sử dụng.

Đo peak ở trần entity chưa chứng minh upper bound với Buff/mod payload không giới
hạn. Content/schema cần count/byte bounds cho Buff, per-entity subsystem và global
state; host từ chối mutation tăng replicated state vượt bound trước commit. Offline
không có snapshot không tự nhận giới hạn gameplay này; chuyển world sang host cần
preflight representation trước admission.
Sizer/capture kiểm exact count/wire bytes trước serialize/send. Nếu vẫn vượt là
SnapshotTooLarge(3009) phía host, không phải SnapshotInvalid của client: từ chối join,
dừng recovery và đóng peer đang cần snapshot không thể tạo. Báo lỗi host để sửa
state/policy, không chụp lại cùng world bất khả biểu diễn tới exhausted.

EveIEntityLoadStep.Load hiện đồng bộ. Bản đầu chia các step qua frame, **chưa hỗ trợ
step prepare bất đồng bộ/I/O**. Không gọi LoadAsync/Addressables rồi giả định đã được
scheduler quản lý. Khi thêm async: bắt buộc timeout từng step, cancellation/token,
và deadline vận hành cộng bound thời gian chờ của đường dependency tương ứng;
công thức frame chỉ dùng synchronous work.

W phải ghi fixture, scene/content mix, máy/editor/player/backend, số mẫu, phân phối
thời gian, fingerprint content/mod và version fixture. Fingerprint đổi làm dữ liệu
cũ thành chưa xác minh; không âm thầm dùng nó như bound đã kiểm chứng.

## 9. Mã lỗi và hành động

Các giá trị dưới đây là phân bổ **đề xuất cho protocol mới**, chưa phải enum API hiện
tại. Không đổi số hoặc tái dùng mã đã phát hành. UInt16; unknown numeric code hợp lệ
được log mã thô và xử lý báo lỗi chung, không từ chối riêng vì chưa biết tên.

| Khoảng/mã | Tên | Phát hiện và xử lý |
| --- | --- | --- |
| 1000–1999 | Handshake | Host/client kết thúc kết nối bị lỗi, không phục hồi content |
| 1001 | UnsupportedProtocol | Bên nhận handshake từ chối |
| 1002 | ContentMismatch | Bên kiểm compatibility từ chối |
| 1003 | HandshakeTimeout | Host đóng riêng kết nối; client đóng kết nối của mình |
| 1004 | PolicyMismatch | Client không đáp ứng profile chung hoặc negotiation không khớp; từ chối ở handshake, không recovery |
| 1005 | ConnectionLimitExceeded | Host hết connection/admission capacity; từ chối kết nối mới |
| 2000–2999 | Entity | Có EntityId/generation/step nếu liên quan |
| 2001 | EntityPrepareFailed | Client báo host rồi đóng; host load lỗi không công bố spawn |
| 2002 | EntityPreparationTimeout | Client phục hồi có giới hạn hoặc đóng |
| 2003 | EntityCommitFailed | Client khóa dispatch ngay, cleanup rồi đóng |
| 2004 | InvalidEntityLifecycle | Bên nhận xử lý protocol fault có giới hạn |
| 3000–3999 | Recovery/flow | Theo từng kết nối |
| 3001 | QueueLimitExceeded | Host chụp lại snapshot; client xin snapshot đầy đủ |
| 3002 | SnapshotTimeout | Phục hồi trong deadline/retry budget |
| 3003 | RecoveryExhausted | Host đóng riêng connection; client FailConnection |
| 3004 | SequenceGap | Không chờ reorder; phục hồi có giới hạn hoặc đóng |
| 3005 | SnapshotInvalid | Client phát hiện size/count/hash/schema sai; bỏ candidate, phục hồi bounded hoặc đóng; content fingerprint mismatch dùng1002 |
| 3006 | InboundRateExceeded | Host phát hiện intent/report vượt rate hoặc inbound budget; giới hạn log và đóng riêng connection theo policy |
| 3007 | SnapshotAppliedTimeout | Host chờ ack quá hạn; lượt recovery mới nếu đủ budget, exhausted thì đóng |
| 3008 | ResidentBudgetExceeded | Bên prepare/admission không đủ resident hoặc tổng buffer/snapshot memory; dừng allocation, báo limit và đóng/từ chối, không retry snapshot cùng capacity vô ích |
| 3009 | SnapshotTooLarge | Host capture/sizer vượt snapshot count/wire cap; từ chối join hoặc kết thúc recovery của peer, báo lỗi host; không buộc client retry cùng world |
| 4000–4999 | Cleanup | Bên sở hữu object quyết định, không theo lời client |
| 4001 | EntityCleanupFailed | Cách ly và báo owner |
| 4002 | CleanupBlocked | Stopping + RestartRequired khi cleanup toàn phiên thất bại |
| 4003 | QuarantineLimitExceeded | Owner giữ object lỗi ngoài pool, chặn allocation mới khi hết reserve, báo UI; không bỏ rơi resource |

Report gồm code, vị trí lỗi, tên step có giới hạn và diagnostic text có giới hạn byte.
Host kiểm kết nối/identity/generation trước hành động, rate-limit và giới hạn log.
Diagnostic text chỉ để log; unknown code không mặc nhiên yêu cầu hành động nào ngoài
báo lỗi chung. Client report không khiến host đổi generation, cleanup object host
hoặc kết thúc phiên chung. Host cần đường đóng từng connection dùng chung schema
lỗi; FailConnection hiện có chỉ dùng cho session client.
2003 cho exception/logical failure, hard limit hoặc repeated overrun theo policy;
không dùng cho một lần vượt soft CommitFrameBudgetMs đơn lẻ. Warning mềm không phải
error wire khiến host loại peer hoặc mở generation mới.

## 10. Bất biến và test

Kiểm sau mỗi bước, không chỉ cuối kịch bản:

- Mỗi EntityId có tối đa một presence hiện hành được phép nhận gameplay.
- Generation đã công bố không giảm; delta không mở/nâng generation.
- Context chưa published/đã invalid không tick, mutate gameplay hoặc tạo presenter.
- Không entity mới trong snapshot được gameplay quan sát trước commit toàn tập.
- Cue quan trọng phát tối đa một lần cho sự kiện đã áp; snapshot không phát lịch sử.
- Phía gửi không drop message giữa reliable stream; retry giữ nguyên lô/thứ tự.
- Client chỉ bỏ gameplay trong phục hồi, tiếp tục kiểm/tiêu thụ sequence nhận.
- Host không gửi delta sau End trước SnapshotApplied của đúng bản hiện hành.
- Max snapshot có đủ buffer/resident/deadline theo giả định policy đã kiểm tra.
- Intent trước ack không thực thi; dormant identity không có object gameplay.
- Entity chậm không chặn entity Ready khác; snapshot commit là global barrier.
- Object chỉ về pool sau cleanup thành công; khóa/token cũ không nhận object tái dùng.
- Remove hết: mapping/resources/subscriptions về0, ngoại lệ là quarantine được báo.
- Fault client khóa dispatch trước cleanup/close; host và kết nối khác vẫn sống.
- Client không Live không phát intent; snapshot ack phải đi trước intent mới.
- Round/cycle timer và hàng delta dùng cùng immutable policy đã chấp nhận ở handshake.
- Commit notification, gồm Buff PresentationChanged, chỉ ra ngoài sau publication toàn tập.
- Actor fixture/count không bao projectile/pickup/Area/VFX; các loại đó không tự được coi là đã sync.
- Dormant có owner/retention condition; game mode kết thúc bằng terminal Despawn.
- IntentResult control chỉ resolve pending, không bypass state snapshot gate.
- Host world/state admission giữ khả năng tạo snapshot đầy đủ; capture quá lớn là lỗi host.
- Goodput mỗi peer tính dưới worst-case MaximumConnections, không chiếm cùng uplink nhiều lần.

Kịch bản bắt buộc: mỗi hàng/sự kiện hợp lệ trong bảng; vào muộn; disconnect giữa
handshake/chunk/prepare/commit/drain; load chậm; generation mới trong load cũ; despawn
trước load hoàn tất; S1 thay bởi S2 cùng khóa/khóa khác; chunk cũ còn retransmit; yêu
cầu snapshot trùng; ack trùng/cũ/sai/timeout; client prepare chậm mà host vẫn giữ
delta; max snapshot/max delta theo policy; outbox/batch bị từ chối; delta vượt hàng
chờ; frame dài; observer
Ready lỗi; commit entity thứ k lỗi; cleanup lỗi rồi retry/hết retry; pool load-unload-
load; reconnect khi tác vụ cũ chưa xong; detached VFX sống qua despawn và dọn khi close.
Thêm: retry snapshot gần cuối deadline lượt1 vẫn đủ lượt2 trong cycle; ack tới sát
timeout mà hàng delta chưa tràn; profile client không đáp ứng bị từ chối; Start S2
khi Preparing/Live và ack S1 tới muộn; Ready→Dormant→Despawn, Dormant biến mất;
gameplay xen chunk; resident/quarantine đầy; MaxConnections có snapshots khác scope
cùng lúc; retained-state commit overrun; Buff callback đọc tập chỉ sau publication.
Thêm: snapshot nhỏ nhưng End chờ accepted cap lớn, ack sát bound vẫn không sinh3007;
đạt world identity cap chặn spawn mới mà join vẫn nhận snapshot hợp lệ; world ở
trần entity+Buff+global payload nằm trong wire cap; capture vượt cap sinh3009 không
recovery loop; baseline+prepare ở đúng budget và commit ở trần vẫn fit frame dưới
profile đã đo, kể cả single-step overrun bound; mọi peer recovery cùng lúc vẫn được
phân bổ goodput giả định; transactional intent bị từ chối có kết quả UI rõ ràng.
Thêm: nhiều floor không tăng dormant vô hạn; hết retention giải phóng slot; control
result xen chunk/trước ack resolve pending mà không đổi actor/inventory state;
Accepted cũ trong recovery không hiển thị state trước snapshot; soft commit overrun
vẫn publish, hard/repeated mới fault; serialize chia lát đúng budget host; snapshot
max bytes chia được vào MaximumSnapshotChunks với overhead.

Mô phỏng lớp protocol nhận dữ liệu reliable ordered, chèn timing/load/disconnect và
backpressure; không chèn reorder/loss đã được transport che giấu. Test transport riêng
mới chèn packet loss/latency để kiểm retransmit/HOL.

Loader/session dùng singleton: test entity integration chạy Host ghi message, kết
thúc thành công, Client phát lại rồi so state/cue/resources. Pure replica/scheduler
test không cần dựng hai session sống đồng thời.

## 11. Fixture đo W — baseline đã có, phép đo cơ chế mới còn chờ

Bộ baseline cũ3 lượt/case chỉ báo first-touch n=1, reload n=2 và min/max; không dùng
median/p95/p99 để trình bày bộ này. Chưa chọn con số production. Lượt đo policy
cần Player Mono, tách startup/warmup, hàng trăm mẫu/case cho p99, phân phối/raw samples
và metadata. GC dùng ProfilerRecorder GC Allocated In Frame khi marker hợp lệ;
baseline là main-thread active work đã xác minh bằng marker/trace, giữ waiting riêng,
không wall time hoặc phép trừ timer chồng nhau. CoreCLR phải đo lại.

Bản baseline bước1 đã có ở PNLifecycleBaselineReview: đo loader/modules hiện tại,
cleanup, directory/Buff capture, serialization/body mẫu, resident control2N và raw
Play frames. Artifact `.utmp/LifecyclePrototype/BaselineReport.md` ghi provenance và
giới hạn. Những phép đo commit/swap/capture-boundary mới bên dưới phải đợi cơ chế thật;
không lấy timing Load hiện tại thay cho Commit, hoặc frame inclusive thay active baseline.

Fixture tạm, không save/reload scene gốc, không đổi Game/Simulator/Play Maximized.
Đo PrepareLoad steps theo Stopwatch monotonic; tách commit/publication/observer/
cleanup, warm/cold asset conditions, cached/replaced snapshot và content mix player/
enemy/modules. Record per-step thời gian, entity count/step count, allocation,
max pump gap, frame time và tiến độ từng frame. Đo không phải test biểu thức công thức.

Bao gồm content/mod đại diện, module preparation thêm dependency, step lỗi và
step chậm có kiểm soát. Đo thêm snapshot capture/wait boundary, commit của object mới
và entity Ready giữ lại, wire size/chunk overhead, tốc độ/burst delta thực tế trước
và sau coalescing, resident peak khi đổi toàn bộ generation. Chúng là input cho
capacity/deadline, không chỉ W. Không giả lập sleep rồi coi đó là W production.
Commit output có fixed/per-entity/retained swap costs và notification flush tách riêng;
snapshot output có serialization/temporary memory và nhiều connection khác scope.
Đo peak wire với present+dormant và Buff/subsystem/global state ở các bound cấu hình;
ghi cả measured maximum và schema/producer bounds, không gọi một sample maximum là
proof upper bound. Đo active baseline frame work, peak notification/pump và step
overrun trên cùng profile; đối chiếu actual frames với các ngân sách. Ghi riêng tình
huống world/state không thể biểu diễn để test host admission/capture fault.
Content mix actor phải được ghi rõ; projectile/pickup/Area đo riêng khi có contract.
Capture freeze host, serialize work/slice overrun, commit soft/hard/consecutive
policy cần số liệu riêng, không dùng một timing chung.
Goodput/accepted native buffer bounds cần adapter test thực; mô phỏng chỉ kiểm đại
số/timer theo giả định, không được báo là đã đo transport chưa cài.
Output có raw samples, fingerprint/build/backend, estimator và caveat. Editor đo
không chứng minh Player/CoreCLR. Chọn policy sau khi có số liệu; kiểm đúng biên và
thấp hơn1ms cho hard-min validation, đúng cache limit và vượt limit.

## 12. Mục mở và xác minh adapter

Player disconnect: chưa chọn grace giữ nguyên, trạng thái freeze riêng hay despawn.
Không dùng disable/Suspend để freeze vì nó gỡ modifier và có thể đổi Max/Current.
Mục này chặn reconnect giữ player/entity, không chặn lifecycle cơ bản hoặc fresh join.

Contract transactional intent riêng còn phải thiết kế IntentId/dedup/kết quả, quota
và timeout pending UI; lựa chọn ID+result đã ghi, chưa coi là API được triển khai.

Save run: PersistentId riêng, schema không mang session runtime; quyết định buff nào
lưu nằm ở đường save. Giữ nguyên RestoreFloorSnapshot kiểm SessionId/EntityId.

UTP dự kiến là adapter trực tiếp, không đưa NetworkObject/NGO vào foundation. Cần
xác minh phiên bản phù hợp Unity6000.6.3f1/Mono, reliable/fragmentation/window, pacing,
send-full semantics, timeout/heartbeat, simulator stage, old connection callbacks.
EveEngine giữ interface byte[]/buffer C# độc lập UTP/native collections. CoreCLR là
lượt kiểm riêng; Steam adapter và projectile latency/authority là quyết định sau.

## 13. Thứ tự công việc sau khi được yêu cầu triển khai

Plan sáu chặng: bước1/2 baseline/prototype đã có; bước3/4 đã được user ủy quyền và
nối runtime actor/Buff. Test native/replay/Play xác minh lifecycle/cleanup, chưa
đo profile Player để chọn policy hoặc chứng minh transport.

1. Baseline fixture trên loader hiện tại — đã đo, chưa chọn policy production.
2. Pure world/host/client state machine, codec, lỗi và mô phỏng — đã có prototype.
3. Directory theo generation, Prepared/Commit, guard module/event và cleanup thật — đã nối.
4. Gắn snapshot/Buff vào lifecycle mới, giữ lựa chọn factory và UI/VFX — đã nối actor/Buff.
5. Đo lại cơ chế mới để chọn policy; replay Host→Client, pool/cleanup và native presentation.
6. Adapter UTP sau các subsystem cần thiết; hai process/LAN thực là bằng chứng riêng.

Lượt bước3/4 đổi RulesVersion67 / Mod API6 cho lifecycle/authority contract mới.
SDK/example cập nhật theo compile Unity thật. Xem HANDOFF mới nhất và README runtime
để biết kiểm tra thực tế. Policy production, transport và các hệ gameplay khác vẫn
là việc riêng; không suy ra full-world replication từ actor/Buff.
