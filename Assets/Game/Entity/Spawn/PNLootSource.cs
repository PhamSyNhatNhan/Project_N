#if UNITY_5_3_OR_NEWER
using UnityEngine;

// EN: Marks an enemy that drops loot when it dies (Docs/SandboxPlan.md G6). Attached by the entity module `project_n:loot_enemy` to enemies of a world that has the feature `project_n:loot`; a world without the feature
//     drops nothing. There is no loot system yet: this is the single place it will read (the id comes from the character's `body.loot`, `PNEntityRole.Loot`), so nothing consumes it today.
// VI: Đánh dấu quái rơi vật phẩm khi chết. Gắn bởi module `project_n:loot_enemy` ở world có feature `project_n:loot`. Chưa có hệ loot nên chưa có gì đọc nó.
public sealed class PNLootSource : MonoBehaviour { }
#endif
