
## Current: Buff reliability and presentation coop (RulesVersion66 / Mod API5)

Buff registration requires explicit ImplementationId; Derive preserves it. Buff and
Area reference schemas use validateReferences/PNContentReferenceContext, including
dependency/alias/staged batch checks; property-name inference was removed. Nested
Action/Operation schemas may delegate their registered reference callbacks.
Stat resource max callbacks run only when max actually changes. Buff stat-source
keys use checked instance+sequence, retained through behavior changes/resume.
Automatic Buff manager clock/runtime failures retire only its collection and cleanup;
caller argument errors and per-behavior callback errors retain separate handling.

Buff gameplay is authoritative. Host/solo and client UI/VFX use a common replica
presentation source; UI Numbers and VFX clock data are separate. Elemental VFX is
presenter-owned/grouped, not acquired by gameplay behavior. No production transport,
Move/Skill/HP/Area replication is implied. See Assets/Core/Entity/Shared/Buff/PNBuffCoopREADME.md for epochs/sequences,
resync/cues/presenter scope and trust limits. Earlier no-replication statements refer
to the former local contract; the new transport-independent proxy is implemented.

# DLL mods / Mod DLL

VI: Bộ nạp hiện hỗ trợ Windows Editor/Standalone với Mono. Không hỗ trợ IL2CPP,
mobile, Linux hoặc macOS. CoreCLR sẽ dùng adapter `EveIModAssemblyLoader` khi
nâng Unity; chưa triển khai hoặc xác nhận CoreCLR trên Unity 6.7.

EN: The current backend supports Windows Editor/Standalone Mono. CoreCLR is a
future adapter behind `EveIModAssemblyLoader`; Unity 6.7 support is unverified.

## Pack and entry point / Pack và điểm vào

Đặt pack dưới `Application.persistentDataPath/Mods/<namespace>/`. Cơ chế chọn
pack và dependency JSON hiện có vẫn áp dụng. Manifest ví dụ:

```json
{
  "schemaVersion": 1,
  "namespace": "example_combat",
  "version": "1.0.0",
  "dependencies": { "project_n": "1.0.0" },
  "code": {
    "apiVersion": "4",
    "assembly": "bin/ExampleCombatMod.dll",
    "entryPoint": "ExampleCombatMod.CombatMod",
    "libraries": []
  }
}
```

`entryPoint` phải là class public, cụ thể, có constructor public không tham số,
triển khai `EveICodeMod` hoặc kế thừa `EveCodeMod`. Constructor không giữ tài nguyên.
Đường dẫn DLL phải nằm trong pack, không có junction/symlink hoặc `..`.
Mỗi DLL phụ phải khai báo trong `libraries`; dependency sang pack khác phải khai
báo trong manifest. Version dependency hiện so khớp chính xác.

## SDK and example / SDK và ví dụ

Chạy từ gốc dự án với PowerShell và .NET SDK, sau khi Unity biên dịch thành công:

```powershell
./Tools/Mods/ExportModSdk.ps1
./Tools/Mods/BuildExampleMod.ps1
```

SDK tham chiếu xuất vào `.utmp/ModSdk`, pack mẫu vào `.utmp/ExampleCombatMod`.
Source mẫu: `Examples/Mods/ExampleCombatMod/CombatMod.cs`. Copy pack mẫu vào Mods
và chọn bằng cơ chế enabled.json hiện có. Không copy DLL từ SDK vào pack.

Assembly API: `EveEngine`, `ProjectN.Core`, `ProjectN.Game`, `ProjectN.Input`.
Core dùng `hub.Actions()`, `hub.Operations()`, `hub.Calculations()` và các extension
tương tự. `PNContentIds` nằm trong Core; các module triển khai nằm trong Game.
`EveModApi.Version` hiện là `2`: stat/modifier/damage/calculation APIs dùng `double`.
DLL xây bằng API1 phải biên dịch lại với SDK mới; manifest API1 bị từ chối trước load.
Khi thay đổi hợp đồng API phải tăng version và
xây lại SDK; API version không thay thế kiểm tra dependency/assembly identity.

```csharp
public sealed class CombatMod : EveCodeMod
{
    public override void Register(EveModRegistrationContext context)
    {
        var id = new EveId(context.Namespace, "scale");
        context.Register(new PNStatCalculationDefinition(id, typeof(CombatScale)));
        context.Override<PNStatCalculationDefinition>(EveId.Parse("base:scale"), id);
    }
}
```

Override vẫn chịu hợp đồng `base:` và lựa chọn provider; xem
`Assets/Core/Content/Registry/PNBaseRegistryREADME.md`.

## Lifecycle and loading rules / Vòng đời và quy tắc nạp

- Register chạy theo thứ tự dependency trong bootstrap, trước khi khóa registry.
- OnSessionStarted nhận context đã dựng và registry đã khóa, trước công bố Ready.
  Không dựa vào `EveRegistrySession.IsReady` trong hook này; dùng context nhận được.
- Dọn entity trước, rồi OnSessionEnded và Dispose theo thứ tự dependency ngược.
  Context ở OnSessionEnded không còn active. Đăng ký lỗi vẫn được Dispose;
  start lỗi vẫn gọi end để hoàn tác tài nguyên đã tạo một phần.
- Lỗi register/start hủy phiên. Cleanup lỗi giữ trạng thái Stopping và tài nguyên
  chờ RetryCleanup; hook đã thành công không chạy lại. Hook lỗi cần hỗ trợ retry.
- DLL host/API không được đóng gói lại. DLL cùng simple name khác nội dung hoặc
  assembly identity bị từ chối; bản sao byte giống nhau của thư viện mod được dùng chung.
- Hash SHA256 của main DLL và tất cả thư viện khai báo tham gia kiểm tra coop,
  cùng manifest và registry bindings. Tăng version pack khi thay đổi hành vi.
- DLL lưu trong process. Đổi nội dung/bộ DLL đang bật hoặc entry point/version
  của code pack yêu cầu khởi động lại game; không có hot reload/unload assembly.
  Sau khi bắt đầu nạp DLL, sửa một lần nạp lỗi cũng có thể cần restart.
- Đây là code tin cậy chạy toàn quyền trong process, không có sandbox. Loader
  kiểm tra cấu hình/identity, không chứng minh DLL an toàn. Native DLL/PInvoke
  không nằm trong cơ chế quản lý thư viện và compatibility này.

EN: Code mods are trusted in-process extensions. Lifecycle failure aborts the
session; failed cleanup is retained for retry. Shared host API copies and conflicting
private assembly identities are rejected. All declared managed DLL bytes enter coop
hashes. Changed DLL selections require a process restart; there is no hot reload.

## Verification / Kiểm chứng

`Tests/EveEngine/EveRunSmokeTests.ps1`: 671 gameplay/registry checks and 25 DLL
checks using separately compiled main/dependency assemblies. SDK export and sample
compilation passed. `BuildNativeModFixture.ps1` then `EveDllModEditorChecks.Run()`
verified real DLL loading, library execution, base override, fingerprints and
cleanup in Unity 6000.6.3f1 Windows Mono Editor. Windows player build and CoreCLR
remain unverified.
