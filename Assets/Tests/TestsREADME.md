# Unity verification / Kiểm thử Unity

VI: Các script kiểm thử/debug bên trong Assets được tập trung tại đây. Di chuyển giữ nguyên GUID để scene/prefab tiếp tục resolve script. Comment giải thích phải có đủ cặp EN/VI.

EN: Unity-side verification and debug scripts live here. Original script GUIDs are preserved. Explanatory code comments must include both English and Vietnamese.

| Folder | Purpose / Mục đích |
| --- | --- |
| Core | Opt-in native damage/action and migrated dummy preview-scene checks / Kiểm thử damage/action và dummy bản mới bằng preview scene |
| EveEngine/Editor | Opt-in native Unity pooling and slash Play Mode checks / Kiểm thử Unity thật chạy tường minh |
| EveEngine/EveEasyPoolingProbe.cs | Editor-only attachable pooling probe; stays outside Editor so Unity can clone its component / Probe chỉ biên dịch trong Editor, đặt ngoài folder Editor để Unity clone component được |
| Legacy | Existing manual event/input/controller debugging scripts / Script debug thủ công cũ |

EN: `Assets/Game/Entity/Character/Eve` contains the new game's Eve character. Standalone smoke harness and Unity stubs remain outside Assets at `Tests/EveEngine`; importing those stubs into Unity would conflict with real Unity types.

VI: `Assets/Game/Entity/Character/Eve` chứa nhân vật Eve của bản game mới. Bộ smoke chạy ngoài Unity và các stub tiếp tục nằm riêng ở `Tests/EveEngine` ngoài Assets; đưa stub vào Unity sẽ xung đột kiểu dữ liệu Unity thật.

Pooling checks: `EveEasyPoolingEditorChecks.Run()` outside Play Mode. Current Eve input checks: `EveSlashPlayModeChecks.Begin()` in running Play Mode in ErMode. Neither runs automatically or saves gameplay scenes.

Dummy migration check: `PNDummyEditorChecks.Run()` outside Play Mode with ErMode
open. Uses a temporary clone, real BaseContent JSON, and checks loading, damage,
death and reload without mutating or saving the original scene.

Pitch billboard check: `PNBillboardEditorChecks.Run()` outside Play Mode verifies
Eve/dummy Visual attachments, multiple camera pitches, fixed feet, unchanged root
physics and flipX. Uses a disposable preview scene and never saves gameplay scenes.

Continuous slash check: `PNSlashDirectionEditorChecks.Run()` outside Play Mode
checks real pooled meshes at arbitrary skill-supplied angles independent of
movement, planar forward offsets and aim fixed for the duration of each cast.
`RunSkillFacing()` checks the actual no-argument Eve skill-to-VFX pipeline using
last movement directions25/155/-72, idle retention and explicit unload cleanup.

Eve JSON/sweep revision: EveConfigurationEditorChecks.Run() checks content-driven
states/tuning; EveMovesetEditorChecks.Run() checks native gameplay;
EveVfxEditorChecks.RenderPreview() checks moving ribbons/trails/particles and exports
.utmp/EveSweepSequence.png. EveSlashPlayModeChecks.Begin() delegates to the current
EveMovesetPlayModeChecks.Run() input test. No test runs automatically.

EN: EveSwordPlayReview.Run() is an opt-in real ErMode Play Mode review of dynamic Rigidbody Dash and Game View screenshots. Back up unsaved scene, run, read Status/.utmp/EveMeshBladeReview/Results.txt, then stop Play Mode to restore authored scene. It repositions live entities; do not save during review.

VI: EveSwordPlayReview.Run() chạy tường minh trong Play Mode của ErMode để đo Dash trên Rigidbody động và chụp Game View. Sao lưu scene chưa lưu, chạy, đọc Status/.utmp/EveMeshBladeReview/Results.txt rồi dừng Play Mode để khôi phục scene gốc. Nó đổi vị trí entity khi thử; không lưu scene trong lúc thử.
