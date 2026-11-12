#if UNITY_5_3_OR_NEWER
using UnityEngine;

// EN: Marks an enemy that counts for the clear condition of a room (Docs/SandboxPlan.md G6). It is attached by the entity module `project_n:room_clear_enemy` to enemies of a world that has the feature `project_n:room_clear`
//     (every room of a run), so a world that does not declare the feature (the sandbox) neither counts these enemies nor owes anyone a clear. PNRoomDirector counts only marked enemies; the despawn service never removes one.
// VI: Đánh dấu quái được tính vào điều kiện dọn phòng. Gắn bởi module `project_n:room_clear_enemy` ở world có feature `project_n:room_clear`; world không khai feature (sandbox) thì không đếm.
public sealed class PNRoomClearMarker : MonoBehaviour { }
#endif
