// EN: Extension point for stat behavior shared by this game's player characters.
// VI: Điểm kế thừa cho hành vi chỉ số chung của các nhân vật người chơi trong game.
public class PNPlayerStat : PNStat
{
    public override double DefaultIFrameDuration => 1d;
    public override PNCombatTeam CombatTeam => PNCombatTeam.Player;
#if UNITY_5_3_OR_NEWER
    public override bool CanShowDamagePopup => false;
#endif
}
