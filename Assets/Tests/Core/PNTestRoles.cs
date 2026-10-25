#if UNITY_5_3_OR_NEWER
// EN: A verification fixture builds its entities by hand, not through the factory, so nothing applies a role to them. Being a player is the role's
//     recipe (PNEntityRoles.IsPlayer), not the stat class: a fixture that stands for a player declares it here.
// VI: Fixture dựng entity bằng tay nên không có role; fixture đại diện cho người chơi khai báo ở đây.
public static class PNTestRoles
{
    public static T AsPlayer<T>(this T stat) where T : PNStat
    {
        (stat.GetComponent<PNEntityRole>() ?? stat.gameObject.AddComponent<PNEntityRole>()).Declare("project_n:player", true);
        return stat;
    }
    // For fixtures that pick the stat class by a type argument: a player stat stands for a player.
    public static T Declared<T>(this T stat) where T : PNStat => stat is PNPlayerStat ? stat.AsPlayer() : stat;
}
#endif
