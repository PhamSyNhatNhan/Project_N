// EN: Initial team rule shared by Player/Enemy; characters/mods may override team and hostility.
// VI: Quy tắc phe ban đầu cho Player/Enemy; nhân vật/mod có thể override phe và quan hệ.
public enum PNCombatTeam { Neutral, Player, Enemy }

public partial class PNStat
{
    public virtual PNCombatTeam CombatTeam => PNCombatTeam.Neutral;
    public virtual bool IsHostileTo(PNStat other) => other != null && CombatTeam != PNCombatTeam.Neutral &&
        other.CombatTeam != PNCombatTeam.Neutral && CombatTeam != other.CombatTeam;
    // EN: Hostility against a team captured earlier, for sources that may be gone (a projectile whose shooter died). Same rule as IsHostileTo; a character or mod
    //     that overrides IsHostileTo should override this as well.
    // VI: Quan hệ thù địch với một phe đã chụp trước, cho nguồn có thể đã mất (đạn mà người bắn đã chết). Cùng quy tắc IsHostileTo; nhân vật/mod override
    //     IsHostileTo nên override cả hàm này.
    public virtual bool IsHostileToTeam(PNCombatTeam team) => team != PNCombatTeam.Neutral &&
        CombatTeam != PNCombatTeam.Neutral && CombatTeam != team;
}
