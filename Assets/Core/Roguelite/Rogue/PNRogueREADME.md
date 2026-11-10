# Roguelite Buff / Chòm sao, Tia sáng, Sao dẫn

Plan và quyết định: `Docs/RogueBuffPlan.md` (R0-R9) và `Docs/RogueBuffDesignDraft.md`. File này là **hợp đồng cho người viết nội dung và lớp hiện thực**; số, tên và icon của nội dung mẫu là tạm.
Khung run (run/tầng/phòng, lưu, đồng bộ run) ở `Assets/Core/Roguelite/PNRogueliteREADME.md`.

## Ba loại định nghĩa

| Loại | Vai trò |
| --- | --- |
| Chòm sao (`PNRogueConstellationDefinition`) | Nhóm. Có thể có cơ chế chung (lớp `PNRogueConstellationLogic`, ví dụ một bộ đếm) và trạng thái dùng chung cho các mục của chòm trên thực thể. |
| Tia sáng (Spark, `PNRogueSparkDefinition`) | Mục nhỏ, nhiều cái được cùng lúc. Có thể `requires` một Sao dẫn **cùng chòm**. Bán được ở cửa hàng. |
| Sao dẫn (Lodestar, `PNRogueLodestarDefinition`) | Mục lớn: **tối đa một Sao dẫn mỗi chòm**. Không bán được, đổi được sang Sao dẫn khác của chòm. |

Khai báo trong `extensions.project_n`: `rogueConstellations`, `rogueSparks`, `rogueLodestars` (mẫu: `Assets/StreamingAssets/BaseContent/roguelite/rogue_sample.json`). Run có khối `rogue`
(`offer`, `emptyOfferShards`, `shop`) và phòng có `reward.rogue: true` thì có Roguelite Buff; không có khối `rogue` = run không có (khung chạy như trước).
Trường lạ bị từ chối, mọi số có giới hạn, id Spark và Lodestar không trùng nhau. `availability`: `default` xuất hiện, `locked` không xuất hiện (chưa có nguồn mở khoá), `forbidden` chỉ xuất hiện với cờ thử
`--pn-allow-forbidden`; chỉ chặn việc xuất hiện, mục đã sở hữu vẫn chạy.

## Viết một lớp hiện thực

Định nghĩa trỏ tới `factoryId`. Có sẵn `project_n:rogue_stats_only` (Spark/Lodestar chỉ có khối `stats`) và `project_n:rogue_passive` (chòm không cơ chế); `data` của chúng phải rỗng.
Muốn làm gì thêm: viết lớp kế thừa `PNRogueConstellationLogic` hoặc `PNRogueItemLogic` (Spark và Lodestar dùng chung `PNRogueItemLogic`, có `Constellation`) và đăng ký một
`PNRogueFactoryDefinition` (id, loại phục vụ, kiểu, bộ tạo, bộ kiểm `data`) trong content module (`PNGameplayContentModule`), **không** đặt tên lớp CLR trong JSON.

Khung làm: dựng/dỡ theo thứ tự cố định (chòm, rồi Sao dẫn, rồi Tia sáng, mỗi nhóm theo id), gọi `Initialize()` một lần, `Tick(dt)` mỗi khung (theo đồng hồ thực thể), `Dispose()` khi dỡ; áp khối `stats`
thành **một nguồn modifier** `rogue:<loại>:<id>` trên `PNStat` (không phải Buff nên quái không tẩy được); cô lập lỗi theo mục (log, gỡ mục đó, người chơi vẫn nạp).
Lớp có sẵn: `Entity`, `Stat`, `Skill` (có thể null), `Session`, `Floor`, `Definition`, `Data`, `Set` (ảnh chụp tập: `Has`, `CountIn`), và `Display`.

Luật cho lớp hiện thực:
- không có trạng thái tĩnh; chỉ chạy ở authority; không sửa skill, không gắn vào nút;
- số lấy từ `Data`, không hằng số trong mã; trạng thái dùng chung thuộc chòm;
- mọi thứ đã đăng ký (sự kiện, hẹn giờ) phải huỷ trong `Dispose()`; không cấp phát mỗi khung trong `Tick`;
- thêm Buff chỉ khi người dùng yêu cầu rõ; chọn đồng hồ rõ ràng.

**Hiển thị số lên HUD:** gán `Display` (`PNRogueDisplay`: tối đa bốn số nguyên ở bốn góc; không gán = không hiện; số 0 đã gán thì hiện) khi số đổi. Manager chỉ đẩy khi có đổi.

## Vòng đời: tập đổi, hiệu lực từ tầng sau (D6)

Tập mục của đội là `PNRunState.Loadout` (id theo thứ tự nhận). `PNRogueBuffManager` (Game, bước nạp `LoadOrder` 20; gắn cùng `PNRogueEntityView` và `PNSyncEntity` bởi mô-đun thực thể `project_n:rogue_buff_player` cho người chơi ở world có feature `project_n:rogue_buff`, tức phòng của run có khối `rogue`; không phải prefab, không ở Hall hay sảnh) đọc tập **một lần** khi
thực thể được dựng. Nhận, mua, bán hay đổi mục chỉ đổi tập và ví; **hiệu lực chỉ bắt đầu từ lần dựng thực thể kế (tầng sau)**, không có API thêm/gỡ trên thực thể đang chạy.
Manager không làm gì khi không có authority, ngoài run, hoặc run không có khối `rogue`. Thực thể gom pool nên `Unload` trả về sạch.

## Luồng trong run (host, `PNRogueRun`)

- Dọn phòng có `reward.rogue`: rút `offer` mục (trọng số, không lặp, loại Sao dẫn của chòm đã có Sao dẫn, loại Spark có `requires` chưa thoả, loại mục không được phép xuất hiện) từ luồng `Reward`.
  Rút rỗng thì trả `emptyOfferShards`. **Cổng giữ khoá khi lựa chọn đang chờ.**
- Vào phòng cửa hàng: rút kho `stock` mục từ luồng `Reward` (không trùng tập). Vật tương tác `shop_rogue` (host) mở màn cửa hàng: mua, bán Spark (hoàn `sellRatio`), đổi Sao dẫn (trả `exchangeRatio` giá mục mới),
  làm mới (`rerollBase + rerollStep * số lần`).
- **Không lưu lựa chọn/kho hàng:** bản lưu ghi lúc vào phòng kèm vị trí luồng; tiếp tục run vào lại phòng đó nên lựa chọn và kho rút ra giống hệt. Luồng `Reward` **chỉ** Roguelite Buff được dùng, theo thứ tự cố định:
  ai dùng thêm luồng này ở chỗ khác sẽ làm lựa chọn sau khi tiếp tục khác trước.
- `PNRogueRun.Execute` là chỗ duy nhất đổi tập; ví nằm trong `PNRunState` (`Shards`).

## Gắn vào người chơi: feature của world

Run có khối `rogue` cho các phòng nó tạo feature `project_n:rogue_buff` (`PNRunFeatures.Of`; **suy ra từ khối, không khai tay**). Mô-đun `project_n:rogue_buff_player` (`PNGameplayContentModule`) đòi feature đó và gắn ba component; `PNEntityFactory` chỉ áp mô-đun khớp, không biết Roguelite Buff. Host gửi tập feature trong `PNWorldOffer`, client không tự tính. Chi tiết và cách viết mô-đun mới: `Docs/EntityModulePlan.md`.

## Đồng bộ (coop)

| Khối/lệnh | Đích | Nội dung |
| --- | --- | --- |
| `project_n:rogue_state` | Session, 4 KiB | tập, lựa chọn đang chờ, kho hàng (id + đã bán), giá và số lần làm mới, cửa hàng đang mở. Host ghi, **mọi máy đọc qua cùng một sink** (`PNRogueStateSync`, `PNRogueStateSyncs.Current()`). |
| `project_n:rogue_view` | Entity, 1 KiB | các mục có `Display` của **một người chơi**: id + bốn số. Host ghi (`PNRogueEntityView`), mọi máy đọc, HUD đọc của người chơi cục bộ. Một thực thể dựng lại là generation mới nên số của tầng cũ không tới tầng mới. |
| `project_n:rogue_command` | Session, 256 B | `{ "op": "pick" \| "buy" \| "sell" \| "exchange" \| "reroll" \| "open" \| "close", ... }`. **Chỉ host (owner 0)** gửi được; mọi thứ khác (client, op lạ, chỉ số sai, ngoài cửa hàng) bị bỏ im lặng. |

Chỉ có id, không có chữ đã dịch. `NetworkVersion` giữ 15; việc thêm hai khối và lệnh nằm trong `RulesVersion` (137).
Giới hạn đã biết: đạn do lớp hiện thực bắn chưa được nhân bản cho client (chung với mọi đạn, làm cùng networking gameplay).

## Giao diện

`Assets/Game/UI/Rogue/PNRogueViews.cs`: `PNRogueOfferView` (màn chọn), `PNRogueShopView`, `PNRogueHudView`, `PNRogueListView` (danh sách trong pause). Chỉ đọc khối đồng bộ, không đọc `PNRogueRun`;
chỉ host có điều khiển, client xem cùng nội dung. Khoá chữ `project_n:ui.rogue.*` (vi/en), icon tạm `Assets/Resources/UI/Icons/Rogue/<id>.png`. Chữ của định nghĩa là khoá localization có namespace.

## Kiểm tra

- Smoke (`Tests/EveEngine`): định nghĩa, parser, tập/lựa chọn/cửa hàng, runtime, save.
- `PNSyncWorldChecks` (phòng mẫu có `shop_rogue`); `PNRunFlowPlayReview` kịch bản `rogue` (lựa chọn, cổng khoá, lệnh chỉ host, cửa hàng, hiệu lực từ tầng sau, số HUD) và `ui`.
- Hai Player thật host + client (`RunCoopFixture`): client thấy màn chọn chỉ xem, lệnh của client bị bỏ, tập và số HUD của chính nó đến qua khối.
