# EveSkill

## Tiếng Việt

`EveSkill` nạp toàn bộ dữ liệu skill của entity qua `EveEntityLoader`. Engine quy định ID, phần bao ngoài dữ liệu và vòng đời nạp; Core quyết định mẫu dữ liệu và cách sử dụng. Không có phụ thuộc Stat bắt buộc hoặc logic kích hoạt, cooldown, graph hay SkillAction trong lớp gốc.

JSON schema 1 thêm trường `skill` tùy chọn vào mỗi character:

```json
{
  "schemaVersion": 1,
  "characters": [
    {
      "id": "example:hero",
      "stats": [],
      "skill": [
        { "id": "example:attack", "data": { "customValue": 3, "steps": [1, 2] } },
        { "id": "example:special", "data": { "customRules": { "enabled": true } } }
      ]
    }
  ]
}
```

Mỗi entry khai báo `EveSkillDefinition` trong `EveRegistry<EveSkillDefinition>`. Character giữ danh sách `SkillIds` theo thứ tự JSON. ID skill phải hợp lệ, thuộc namespace pack và duy nhất trong registry skill của phiên, kể cả giữa các character. Entry inline là định nghĩa, chưa có cú pháp tham chiếu `skillId` trong JSON. Code có thể đăng ký định nghĩa dùng chung rồi truyền danh sách skill ID cho constructor `EveCharacterDefinition`.

`data` có thể bỏ trống, null, object, array hoặc giá trị JSON khác. Entry chỉ có `id` và entry có `data: null` đều tạo `Data = null` trong C#. Dữ liệu khác giữ dưới dạng `JToken` trong thuộc tính `object Data`, không thêm `kind` hoặc diễn giải các ID bên trong. Trường bao ngoài không biết, property trùng kể cả trong dữ liệu lồng nhau và ID trùng/sai đều báo lỗi. Toàn bộ batch phải hợp lệ trước khi ghi định nghĩa. Core quyết định dữ liệu có bắt buộc hay không và kiểm tra schema/tham chiếu riêng.

`Load(EveEntityLoadContext)` ưu tiên CharacterId từ danh tính spawn; khi chưa có mới dùng `CharacterDefinitionId` trong Inspector. Có thể override `Load`, `LoadDefinition`, `Initialize` hoặc `LoadSkillData(EveId, object)`. Hook `LoadSkillData` mặc định trả nguyên dữ liệu, có thể chuyển thành mẫu của game hoặc báo lỗi. Lỗi nạp entity chặn Ready và gọi Unload để rollback. Lớp kế thừa phải dọn trạng thái riêng trong Unload, kể cả khi mới nạp một phần.

`Skills`, `GetSkillData` và `TryGetSkillData` cung cấp dữ liệu sau khi nạp. Hook `LoadSkillData` có thể nhận và trả null. `TryGetSkillData` trả true khi ID có trong map dù dữ liệu null; false khi thiếu ID. `IsReady` báo đã hoàn tất nạp, kể cả dữ liệu null; gameplay vẫn phải kiểm tra `EveEntityLoader.IsReady`. Bỏ `skill` hoặc dùng mảng rỗng đều nạp thành tập dữ liệu rỗng. Unload bỏ dữ liệu và cho phép nạp lại.

Định nghĩa sao chép JSON khi được tạo và khi được đọc; mỗi entity nhận một bản riêng. Map ID của component và danh sách ID của character chỉ đọc. Object C# đăng ký từ code phải bất biến, serialize được bởi Newtonsoft.Json để tham gia hash coop, và do game sao chép/chuyển thành trạng thái riêng qua hook khi cần. Engine không suy đoán cách clone object tùy ý.

## English

`EveSkill` loads every skill payload through `EveEntityLoader`. The engine owns IDs, the envelope and loading lifecycle; games own data schemas and execution. The base does not require Stat or implement activation, cooldowns, graphs or SkillAction.

Schema 1 accepts optional `characters[].skill` as shown above. Each inline entry declares an `EveSkillDefinition`. The character retains ordered, immutable `SkillIds`. IDs must be valid, belong to their pack namespace and be globally unique within the session skill registry, including across characters. JSON reference-only `skillId` entries are not implemented. Code modules may register shared definitions and pass their IDs to `EveCharacterDefinition`.

`data` is optional and may be null or have any JSON shape. Omitted and explicit JSON null payloads both become C# `Data = null`. Other payloads remain `JToken` behind `object Data`. Unknown envelope fields, duplicate properties (including nested payload properties) and invalid/duplicate IDs fail the batch before definitions are committed. Games decide whether data is required and validate their payload fields and references; the engine does not interpret them or require a `kind` field.

Loading requires sealed content and selects spawn CharacterId before the Inspector fallback. Override `Load`, `LoadDefinition`, `Initialize` or `LoadSkillData(EveId, object)` to customize loading. The per-entry hook returns data to retain and may throw when game validation fails. Entity load failure blocks Ready and invokes Unload on the failed step and prior steps. Derived Unload must clean up private state and partial loads.

`Skills`, `GetSkillData` and `TryGetSkillData` expose loaded payloads. `LoadSkillData` permits null input/output. `TryGetSkillData` returns true for a present ID even if its data is null, and false for an absent ID. Component `IsReady` reports completed loading, including null payloads; gameplay must also gate on `EveEntityLoader.IsReady`. Missing/empty skill lists load as empty maps. Unload clears the base state for reloading.

Definitions copy JSON on construction and access, giving entities independent payloads. Code-supplied objects must remain immutable and Newtonsoft.Json-serializable for coop fingerprints. Games can clone/convert them through the hook for mutable per-entity state. The engine does not guess how to clone arbitrary objects.
