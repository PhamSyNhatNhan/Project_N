# EveSkill handoff / Bàn giao EveSkill

## Tiếng Việt

**Trạng thái:** Đã triển khai phần nạp dữ liệu trong EveEngine. Core và gameplay skill cũ chưa chuyển. Thiết kế này thay các giả định trước đây về lớp gốc phải phụ thuộc Stat, kiểm tra kích hoạt hoặc điều phối Action.

- Registry giữ `EveSkillDefinition` gồm `EveId` và `object Data`. JSON schema 1 có `characters[].skill[]` với `id` và `data`; nội dung `data` do Core quy định. Character giữ danh sách skill ID; engine nạp toàn bộ danh sách cho entity.
- `data` có thể null hoặc bỏ trống; hook nạp cũng có thể trả null. Core quyết định skill cụ thể có cần dữ liệu hay không. Ready dựa trên hoàn tất luồng nạp, không dựa trên giá trị payload.
- `EveSkill` tham gia `EveEntityLoader`, có các điểm override cách nạp và phân tách. Core có thể override `LoadSkillData(EveId, object)` để kiểm tra/chuyển dữ liệu thành mẫu riêng. JSON đầu vào là `JToken`; mỗi entity nhận bản sao riêng. Object đăng ký bằng code phải bất biến, serialize được và do Core sao chép khi cần.
- Core có các lớp khung `PNSkill`, `PNPlayerSkill`, `PNEnemySkill`; `EveCharacterSkill : PNPlayerSkill` đã gắn vào Eve trong ErMode với `project_n:eve`. Chưa có schema hoặc gameplay riêng. Cooldown, buff/debuff, graph, kích hoạt và SkillAction chưa triển khai. SkillAction là lựa chọn để tách hành động dùng lại; vị trí và hợp đồng chưa chốt. Vòng đời thực thi và luật game thuộc Core. Không chuyển gameplay cũ chỉ vì hoàn tất phần nạp Engine.
- Chưa có cú pháp tham chiếu skill trong JSON; entry khai báo định nghĩa mới và ID không được trùng giữa character. Đánh giá nhu cầu dùng lại định nghĩa ở Enemy khi bàn Core.
- Tiếp theo: thiết kế mẫu dữ liệu và điều phối Core qua ví dụ Player/Enemy thực tế; xác định kiểm tra tham chiếu trong payload và trạng thái riêng cho entity. Graph kế thừa gì và cách lưu node/Action chưa chốt.

API, ví dụ JSON và giới hạn ở [EveSkillREADME](../../Assets/EveEngine/Entity/Skill/EveSkillREADME.md).

## English

**Status:** Engine data loading is implemented. Core and legacy gameplay migration remain pending. This design supersedes earlier assumptions that the base must depend on Stat, check activation or coordinate Actions.

- Registry entries contain an `EveId` and `object Data`. Schema 1 accepts `characters[].skill[]` with `id` and `data`; games own the payload schema. Characters retain ordered skill IDs, and the engine loads the complete list for each entity.
- Data may be null or omitted, and loading hooks may return null. Games decide whether data is required. Ready depends on successful loading rather than a non-null payload.
- `EveSkill` participates in `EveEntityLoader` and exposes overridable loading and payload conversion. Core may validate/convert through `LoadSkillData(EveId, object)`. JSON arrives as `JToken` and is copied per entity. Code definitions must be immutable, serializable and cloned by games when needed.
- Core has empty extension classes `PNSkill`, `PNPlayerSkill` and `PNEnemySkill`. `EveCharacterSkill : PNPlayerSkill` is attached to Eve in ErMode with `project_n:eve`. Game schemas, cooldowns, buffs, graphs, activation and SkillAction remain pending. Actions are optional reusable execution units; placement and contract remain open. Engine loading completion alone does not authorize legacy gameplay migration.
- JSON reference-only entries are not implemented. Inline entries declare new definitions and cannot reuse IDs across characters. Revisit reuse during Enemy/Core design.
- Next: real Player/Enemy data and orchestration examples, payload reference validation and per-entity state. Graph inheritance and node/Action storage remain undecided.

See [EveSkillREADME](../../Assets/EveEngine/Entity/Skill/EveSkillREADME.md) for API details, examples and limitations.
