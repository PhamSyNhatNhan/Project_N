#if UNITY_5_3_OR_NEWER
using EveEngine;

// EN: The default target rule of a chooser. The local player's characters follow this machine's Settings (auto target mode); a remote
//     player's character on the host uses Nearest until Skill networking carries its owner's choice; every other chooser (enemies,
//     bosses, plain entities, system sources) uses Aggro. Skills with a rule of their own pass it instead (PNAutoTarget.Resolve).
// VI: Luật mặc định của bên chọn. Nhân vật của người chơi máy này theo Settings; nhân vật người chơi máy khác trên host dùng Nearest
//     tới khi có Skill networking; mọi bên chọn khác (quái, boss, entity thường, nguồn hệ thống) dùng Aggro.
public static class PNTargetRules
{
    // Verification fixtures pin the local mode so checks do not depend on the machine's Settings; null in play.
    public static PNTargetMode? FixedLocalMode { get; set; }
    public static PNTargetMode LocalMode => FixedLocalMode ?? PNSettingsService.Current.AutoTargetMode;

    public static PNTargetRule For(PNStat chooser)
    {
        if (!(chooser is PNPlayerStat)) return PNTargetRule.Aggro;
        return IsLocal(chooser) ? PNTargetRule.Autotarget(LocalMode) : PNTargetRule.Autotarget(PNTargetMode.Nearest);
    }
    // Same ownership test as PNPlayerFactory.Published: offline every player is local; host owns owner 0; a client owns its LocalOwnerId.
    public static bool IsLocal(PNStat stat)
    {
        var identity = stat != null ? stat.GetComponent<EveEntityIdentity>() : null;
        var session = EveRegistrySession.IsReady ? EveRegistrySession.Context : null;
        if (identity == null || session == null) return true;
        if (session.Role == EveSessionRole.Offline) return true;
        return identity.OwnerId == (session.Role == EveSessionRole.Host ? 0 : session.LocalOwnerId.GetValueOrDefault(-1));
    }
}
#endif
