// EN: Initial team rule shared by Player/Enemy; characters/mods may override team and hostility.
// VI: Quy tắc phe ban đầu cho Player/Enemy; nhân vật/mod có thể override phe và quan hệ.
public enum PNCombatTeam { Neutral, Player, Enemy }

public partial class PNStat
{
    public virtual PNCombatTeam CombatTeam => PNCombatTeam.Neutral;
    public virtual bool IsHostileTo(PNStat other) => other != null && CombatTeam != PNCombatTeam.Neutral &&
        other.CombatTeam != PNCombatTeam.Neutral && CombatTeam != other.CombatTeam;
}
