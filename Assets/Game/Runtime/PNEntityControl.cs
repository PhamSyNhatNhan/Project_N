#if UNITY_5_3_OR_NEWER
using UnityEngine;

// EN: Who drives an entity, kept apart from what it is. Identity ("is a player") is PNEntityRoles.IsPlayer, read from the role. Control ("a human's input
//     drives it on some machine") is asked here: today exactly the players are driven, so it answers from the role, but callers must ask this function so a
//     later pet or second form (driven without being a player, switchable at runtime) changes only this body. Where the driver sits is PNTargetRules.IsLocal.
// VI: Ai điều khiển entity, tách khỏi việc entity là gì. Danh tính là PNEntityRoles.IsPlayer; điều khiển hỏi ở đây.
public static class PNEntityControl
{
    // True when a human's input drives this entity (on this or another machine).
    public static bool IsDriven(Component entity) => PNEntityRoles.IsPlayer(entity);
}
#endif
