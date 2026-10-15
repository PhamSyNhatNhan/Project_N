# Public base concepts / Khái niệm base công khai

Current naming (RulesVersion65 / Mod API4): ATK stat is base:attack_power,
canonical project_n:attack_power. base:attack now names the combat tag and skill
category in their separate registries. Skill/ultimate/dash/burst tags likewise
share their category spelling. Old attack stat and category *_damage tag IDs have
no aliases. Code uses AttackPower and AttackTag/SkillTag/UltimateTag/DashTag/BurstTag.
Other stat IDs and jump behavior are unchanged in this migration.

VI: `base:` là bộ tên công khai của Project_N, bao gồm nội dung từ EveEngine và
Game. Đây là binding có danh tính ổn định, không phải tìm theo tên gần giống.
Engine cung cấp cơ chế; module game sở hữu tên và hợp đồng. RulesVersion: project_n:15.

EN: `base:` is Project_N's public vocabulary spanning engine and game content.
Bindings preserve stable identity rather than searching similar names. The engine
supplies the mechanism; trusted game modules own names/contracts. RulesVersion: project_n:15.

```csharp
hub.Stats.Get(EveId.Parse("base:attack_power"));
hub.Actions().Get(EveId.Parse("base:slash_action")).Create();
stat.Evaluate(EveId.Parse("base:attack_power"));
stat.GetCurrent(EveId.Parse("base:health"));
```

| Registry name | Public names | Override contract |
| --- | --- | --- |
| stats | health, attack_power, attack_speed, cooldown_reduction, crit_rate, crit_damage, defense, move_speed, jump_speed | Same Number/Resource kind; resource policy may change |
| actions | slash_action, damage_action | Replacement inherits original action implementation |
| operations | slash_vfx, lock_movement | Replacement inherits original operation implementation |
| calculations | scale, mitigation | Concrete PNStatCalculation factory |
| contributions | before, after | Locked metadata; calculator owns interpretation |
| damage_types | physical, magic, true | Valid PNDamageTypeDefinition |
| elements | fire, ice, lightning | Locked |
| tags | physical, magic, true, fire, ice, lightning, heal, attack, skill, ultimate, dash, burst | Locked |
| enemy_types | normal, elite, boss | Locked; custom types may be registered |
| characters | eve, dummy, further project_n JSON characters | Locked |
| skills | eve_slash, further project_n JSON skills | Locked |

VI: Tất cả tên trong bảng dùng tiền tố `base:` và chỉ tra trong registry đã chọn.
Định nghĩa riêng của mod tiếp tục dùng namespace mod. `Count`, `Snapshot` và
`Definitions` chỉ chứa định nghĩa thật; `BindingSnapshot()` trả bảng ánh xạ công khai,
danh tính gốc, triển khai được chọn, owner/provider/version và quyền override.

EN: Prefix every public name above with `base:`. Resolution is registry-specific.
Mod definitions retain their own namespace. Count/Snapshot/Definitions contain real
definitions only; BindingSnapshot exposes public names, original identities,
selected implementations, owner/provider/version and override permissions.

## Identity and lookup / Danh tính và tra cứu

VI: `ResolveIdentity(base:attack_power)` luôn trả `project_n:attack_power`, kể cả khi mod được
chọn. Danh tính này là khóa nội bộ tương thích dữ liệu hiện có. `Get(base:attack_power)`
trả định nghĩa đã chọn; `Get(project_n:attack_power)` đọc đích danh bản gốc. Runtime stat
dùng `GetEffective` để áp triển khai được chọn lên cùng danh tính: ID cũ và base
không tạo hai resource/stat hoặc áp bonus hai lần. ID implementation của mod là
nội dung riêng, không tự đồng nhất với khái niệm base.

EN: ResolveIdentity always returns the original identity, including under an
override. Get(base:...) selects an implementation; Get(original ID) explicitly
reads the original definition. Runtime stats use GetEffective for selected metadata
on the same identity, so old/base references share values, resource state and
caches. A provider implementation ID remains separate content, not another alias.

VI: Action gọi ID gốc sẽ bỏ qua override của action; JSON Eve đã dùng base:slash_action.
Operation slash dùng base:lock_movement/base:slash_vfx. API stat dùng ID gốc vẫn áp
luật đã chọn vì danh tính runtime không chia đôi. Không dùng ID implementation mod
để tham chiếu HP/ATK nền trong dữ liệu nhân vật.

EN: Explicit original action IDs bypass action overrides; Eve's JSON now uses
base:slash_action, and slash operations use public IDs. Legacy stat APIs still apply
selected runtime rules because entity identity is not split. Use public concepts,
not provider implementation IDs, for built-in character HP/ATK references.

## Mod declaration / Khai báo mod

VI: Manifest mod phải khai báo dependency Project_N đúng phiên bản. Ví dụ JSON trong
pack `combat_mod` thay policy HP; không tạo định nghĩa `base:health`:

EN: The manifest must declare the exact Project_N dependency version. This JSON
inside combat_mod replaces HP metadata without registering a base definition:

```json
{
  "schemaVersion": 1,
  "statDefinitions": [
    { "id": "combat_mod:health", "kind": "resource", "maxChangePolicy": "preserveCurrent" }
  ],
  "overrides": [
    {
      "registry": "stats",
      "id": "base:health",
      "implementationId": "combat_mod:health",
      "version": "1.0.0"
    }
  ]
}
```

VI: Một provider hợp lệ được chọn tự động; nhiều provider cùng thay một mục gây
lỗi bootstrap. Cấu hình người dùng tại `Application.persistentDataPath/Mods/overrides.json`
chọn provider rõ ràng (dùng project_n để chọn bản gốc):

EN: One valid provider is selected automatically. Multiple providers cause a
bootstrap conflict. The user resolves it in persistentDataPath/Mods/overrides.json
(project_n selects the original):

```json
{
  "schemaVersion": 1,
  "selections": [
    { "registry": "stats", "id": "base:health", "provider": "combat_mod" }
  ]
}
```

VI: Provider thiếu/tắt, lựa chọn trùng, target thiếu, sai namespace/dependency hoặc
sai hợp đồng đều báo lỗi. Mọi candidate phải hợp lệ, kể cả candidate không được chọn.
Không fallback âm thầm khi chạy lỗi. Registry khóa theo session; đổi cấu hình áp
ở session kế tiếp. Bảng đã chọn, contract version và implementation version tham gia
hash coop. Tăng version khi đổi code; tên class không chứng minh code hai máy giống nhau.

EN: Missing/disabled providers, duplicate choices, missing targets, namespace or
dependency violations and contract mismatches fail explicitly. All candidates must
be valid, even unselected ones. There is no runtime fallback. Registries seal per
session; configuration changes apply next session. Selected bindings and contract/
implementation versions enter coop hashes. Bump versions for code changes; matching
class names alone do not prove identical executable code.

## Code extension / Mở rộng bằng code

```csharp
hub.Actions().Register(new PNSkillActionDefinition(
    EveId.Parse("combat_mod:slash"), typeof(CustomSlashAction)));
hub.Actions().RequestOverride(
    EveId.Parse("base:slash_action"), EveId.Parse("combat_mod:slash"),
    "combat_mod", "1.0.0", new[] { "project_n" });
// CustomSlashAction inherits PNSlashAction / kế thừa PNSlashAction.
```

VI: Bộ nạp DLL Windows Mono và SDK được mô tả trong
`Assets/EveEngine/Mods/EveModsREADME.md`. JSON chỉ khai báo override đến định nghĩa đã đăng ký;
không tạo factory action/calculation bằng tên class. Skill/character/tag/element còn
khóa vì chưa có hợp đồng thay thế đầy đủ. Mở thêm hợp đồng khi triển khai cơ chế đó.
Payload Core kiểm tra dependency/action/tags và cấu hình slash/value trước công bố phiên;
schema bổ sung của code mod cần validator game tương ứng.

EN: Windows Mono DLL loading and SDK usage are documented in
`Assets/EveEngine/Mods/EveModsREADME.md`. JSON can request overrides of registered definitions,
not instantiate factories from class names. Skills/characters/tags/elements remain
locked until replacement contracts exist. Core validates dependencies, action/tag
references and built-in slash/value payloads before publication; custom code-mod
payload schemas need corresponding game validators.
