# Project_N localization (Rules94, Mod API7)

Unity Localization1.5.13 xử lý table/provider và Smart Strings. Bản dịch chỉ lưu JSON;
settings/locale/table là đối tượng tạm trong RAM, không có bảng dịch `.asset`.
Game assembly sở hữu Unity adapter; Core chỉ có catalogue/parser/store và document policy.

## Dữ liệu

Base: `Assets/StreamingAssets/BaseContent/Localization/`.
Một `languages.json`, mỗi ngôn ngữ một file (`vi.json`, `en.json`).
Đường tương ứng của mod là `<selected pack>/Localization/`.

```json
{
  "schemaVersion": 1,
  "default": "vi",
  "fallback": "en",
  "languages": [
    { "code": "vi", "nativeName": "Tiếng Việt", "file": "vi.json", "fontProfile": "default" },
    { "code": "en", "nativeName": "English", "file": "en.json", "fontProfile": "default" }
  ],
  "overrides": []
}
```

Entry string là literal; entry object bật formatter rõ ràng:

```json
{
  "project_n:ui.start": "Bắt đầu",
  "project_n:example.damage": {
    "text": "Gây {damage} sát thương trong {duration} giây.",
    "smart": true
  }
}
```

Thêm ngôn ngữ: khai báo code/nativeName/file trong languages.json rồi thêm file đó.
Gỡ ngôn ngữ: bỏ declaration; nếu là default/fallback phải chọn mã khác hợp lệ.
Không tự biến file JSON lạ thành ngôn ngữ trong menu. Thay dữ liệu có hiệu lực sau
lần load content/app tiếp theo, không có watcher hot reload trong lúc chơi.

Parser kiểm UTF-8, duplicate/unknown fields, code/key, cycle fallback và namespace.
`file` chỉ là tên `.json` trong cùng Localization root, không cho URI/absolute/`..`/link.
Trần:256KiB manifest,4MiB/file,16KiB/entry,128 locale,50.000 locale-key entries/pack,
32MiB tổng JSON bản dịch. Font profile hiện chỉ `default`; profile khác bị từ chối.

## Mod

Mod chỉ bổ sung bản dịch vi/en có thể bỏ languages.json; loader thử `<code>.json` cho
các code đã được catalogue cuối khai báo. File convention vắng là hợp lệ; file hỏng là lỗi.
File được khai báo explicit phải có. Mod thêm code mới cần languages.json riêng:

```json
{
  "schemaVersion": 1,
  "languages": [
    { "code": "fr", "nativeName": "Français", "file": "fr.json", "fontProfile": "default" }
  ],
  "overrides": [
    { "locale": "fr", "keys": ["project_n:ui.start"] }
  ]
}
```

Pack namespace riêng được thêm key của mình. Bổ sung/ghi đè key của pack khác cần
dependency trực tiếp exact-version trong pack manifest và explicit override như trên;
key phải tồn tại ở owner. Hai sibling cùng ghi một locale/key bị từ chối; muốn nối
override thì pack sau phải phụ thuộc pack trước. Metadata locale trùng phải thống nhất.
Mod không đổi default/fallback toàn game; fallback riêng đặt trong locale declaration.
Ví dụ đầy đủ: `Examples/Mods/LocalizationExample/` (JSON-only, không cần DLL).

Pack selection và dependency order dùng loader content hiện hành; mobile không nạp mod.
`PNGameContentSource` bỏ đúng subtree Localization/ khỏi gameplay JSON/parser/hash.
Generic `EveDirectoryContentSource` vẫn giữ default policy để các ứng dụng Engine khác
không mất dữ liệu. Các fixture đọc BaseContent của Project_N phải dùng PNGameContentSource.

Lựa chọn locale và thay câu dịch trong cùng pack không đổi gameplay hash. Manifest và
DLL của pack vẫn theo admission hiện hành: một pack chỉ dịch cài riêng một peer **chưa**
được miễn parity. Không dùng localization để cung cấp luật gameplay.

## API Game

```csharp
PNLocalization.Text("project_n:ui.start");
PNLocalization.Text("project_n:example.damage",
    new Dictionary<string, object> { ["damage"] = damage, ["duration"] = seconds });
PNLocalization.Select("en"); // main thread, publish + lưu thiết lập máy
```

Text/Select/Languages/Selected/IsReady/Changed là API Game. Args chỉ nhận scalar;
không truyền entity/GameObject. Chỉ refresh khi args/locale thay, không format mỗi frame.
`Select(code, false)` không ghi preference, dùng cho fixture hoặc preview.
Fallback từng key: selected → chain đã khai báo → en; thiếu hẳn trả `[key]`.
Smart String dùng culture của câu dịch thực sự được chọn: câu fallback tiếng Anh vẫn
dùng quy tắc plural tiếng Anh kể cả locale người chơi là tiếng Pháp. Dùng PNLocalization.Text
cho contract này; format trực tiếp merged StringTable bằng Unity API có thể dùng culture
của locale đích theo hành vi package.
String rỗng là cố ý để trống. Lỗi format trả migration fallback hoặc marker và diagnostic
được giới hạn một lần/key/generation; lỗi cú pháp Smart String bị chặn lúc stage candidate.

`PNLocalization.ContentText(id, kind, field, literalFallback)` dùng convention:
`project_n:character.eve.name`, `project_n:character.eve.description`,
`project_n:mode.roguelite.start.solo`. Resolve effective/canonical content identity ở caller;
display text không làm ID logic. Literal metadata cũ là fallback cho content chưa có key;
không nhét key dài vào `PNPlayableSettings.Name` hoặc đổi public constructor cũ.

UI nội bộ dùng `PNLocalizationUI.Bind/Content/Watch`: subscribe lúc attach, unsubscribe
lúc detach. View tạo sau khi switch đọc ngay locale hiện hành. Title và pause mở cùng
view ngôn ngữ; các nút/ring/style/motion gốc được giữ. Debug/test panels và arbitrary
external error strings không được tự suy đoán key bằng so khớp câu.

Nếu dùng Unity API trực tiếp, collection duy nhất là `PNLocalization.CollectionName`
(`project_n.localization`), entry là toàn bộ key có namespace. Shared key IDs chỉ thuộc
generation trong RAM, không lưu vào save/wire. Không dùng GetAllTables/Addressables
catalogue API để dò các bảng JSON; provider trả các table đã stage.

## Boot, persistence và cleanup

- Base localization trước Title; pack layer chuẩn bị từ đúng selected pack snapshot.
  OnContentPublished là gate thật trước completion/content-ready event; candidate hỏng
  không thay current. Runtime override documents trong bộ nhớ vẫn dùng base presentation.
- Text được stage toàn bộ trong giới hạn tổng32MiB; switch không đợi I/O, commit trên
  main thread và refresh các view. Chưa có lazy loading theo table/locale.
- JSON-only settings override public GetInitializationOperation vì default package luôn
  initialize Addressables kể cả NoPreloading. Không cần aa/settings.json/built catalogue.
- Preference ở `persistentDataPath/Settings/localization.json`, mã locale, schema1;
  temp+replace+backup; file hỏng thử backup. Ngôn ngữ không sync từ host/save run.
- Saved locale từ mod có thể tạm dùng base default trên Title; sau khi pack sẵn thì thử
  lại mã đã lưu, không xóa preference chỉ vì base phase chưa thấy code đó.
- EndPlay giữ text. Content unload tháo mod layer và trở lại base; app shutdown/reset
  release cache/operation handles trước khi destroy SO do mình sở hữu. Không hot unload DLL.
- Android đọc declared files bằng coroutine UnityWebRequest qua jar:file://. Reader URL
  có test PC; gameplay loader Android và full device/render acceptance vẫn cần kiểm riêng.

Font vi/en giữ nguyên profile hiện tại; Philosopher Regular/Bold đã đối chiếu corpus
tiếng Việt và Player đã chụp chữ thật. Thêm locale CJK/RTL/font khác cần bổ sung và kiểm
profile/font tương ứng; không tự nạp TTF bất kỳ từ mod hoặc mặc định dựa OS font.

## Kiểm chứng

`PNLocalizationChecks` dùng chung smoke/native (schema, pack, preference, hash).
`PNLocalizationNativeChecks` thêm provider/Smart String rollback/resource cleanup.
`PNLocalizationPlayReview` là fixture opt-in với `-pn-localization-review=<artifact dir>`,
chạy trên Player clone/profile riêng; không dùng flag này trên save/profile thật.
Kết quả và giới hạn thực tế ở `Docs/LocalizationImplementationReport.md`.
