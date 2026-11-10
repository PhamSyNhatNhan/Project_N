#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;

// EN: The registered role (`project_n:enemy`, `project_n:npc`, `project_n:prop`, a mod's own...) of an entity AUTHORED in a scene (Docs/EntityModulePlan.md step E2b). An entity the factory spawns gets its role from
//     its request; one placed in a scene has no request role, so it names its role here. The factory then applies the IDENTITY part of the role to it (`PNEntityRole`: is it a player, persist, body flags) exactly as for a
//     spawned entity, which lets everything that reads a role (entity modules, the world checkpoint, later systems) see an authored entity the same way. It does NOT run the role's placement, prefab or lifetime (the
//     object is already in the scene) and does not apply the role's spawn Buffs. An authored entity without this component keeps the old behaviour (no role; a `PNPlayerStat` still makes it a player).
// VI: Vai trò đã đăng ký của entity ĐẶT SẴN trong scene. Entity do factory sinh nhận vai trò từ request; entity đặt trong scene khai vai trò ở đây, factory áp phần ĐỊNH DANH của vai trò (PNEntityRole) như entity sinh ra.
//     Không chạy phần đặt vị trí/prefab/thời gian sống, không áp Buff khi sinh. Không có component này thì giữ hành vi cũ.
[DisallowMultipleComponent]
public sealed class PNSceneEntityRole : MonoBehaviour
{
    [SerializeField] private string role = "";
    public string Role => role ?? "";
    public bool HasRole => !string.IsNullOrWhiteSpace(role);
    public EveId Id => HasRole ? EveId.Parse(role.Trim()) : default;

    public void Configure(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || !EveId.Parse(value.Trim()).IsValid) throw new ArgumentException("A scene entity role is a registered entity role id of at most 128 characters.", nameof(value));
        role = value.Trim();
    }
}
#endif
