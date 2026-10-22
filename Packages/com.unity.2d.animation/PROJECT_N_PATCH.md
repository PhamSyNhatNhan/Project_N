# Project_N patch — 2D Animation 16.0.1

Patch pn1, 2026-10-23. Package embedded giữ version16.0.1, metadata, GUID và license
của upstream revision `72c620dc8c96ad4620a18b1795884f670b7f5794`.

Chỉ thay `Runtime/BatchedDeformation/GpuDeformationSystem.cs`:

- Fallback tạo từ AfterSceneLoad có cleanup riêng qua Application.quitting.
- Editor dọn trước assembly reload, lúc quit và sau Play nếu không còn GPU system.
- Đếm GPU system đang sở hữu fallback để không dọn lúc Edit-mode skin còn dùng.
- Clear global binding trước Release; các hook và cleanup lặp lại là idempotent.

Không tạo SpriteSkin giả, sửa PackageCache hoặc đổi số phần tử/stride buffer.
Source gốc SHA256 `A6CA34B2F20E1997F73EEF46D0F81E3CED98989FD225B0BE4868CD9CC625F2F6`.
Source patch SHA256 `6CB8EE860387FA7DE7383F1E45AC892C749D10AC8367BDFE13E5D742B0B855C3`.

Đọc `Docs/EnemyToolkitFollowupReport.md` trong project để biết evidence và giới hạn
hiện hành. Đã kiểm CPU Player empty/real skin/Title→Hall, GPU Player với
SKINNED_SPRITE thực sự bật qua ba vòng pose/unload, cùng năm Play/Stop cho mỗi
chế độ domain reload on/off: ComputeBuffer finalizer/D3D4096 diagnostic đều0.
Native Editor TextLib/JobTempAlloc khi thoát ngay sau Stop vẫn tái hiện; patch này
không giải quyết hoặc suppress diagnostic đó.
Khi đổi package/Editor, đối chiếu lại lifetime upstream và kiểm empty/real skin/game
flow + repeated Play/domain reload trước khi giữ hoặc bỏ patch này.

