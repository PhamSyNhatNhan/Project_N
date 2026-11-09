using System;
using EveEngine;
using UnityEngine;

// EN: The ENVIRONMENT half of "may this player entity receive local input now": the one definition both PNPlayerMove and PNPlayerSkill read (it wraps PNLocalPlayerInput.Allows: the entity is
//     ready and executable, not held by a world change or a transfer, the network input gate is open, and it is the local player's entity). The explicit switches (`CanInput`, the menu block) stay
//     on each component. `Allowed` is evaluated live, so a handler always sees the current answer; `Update` compares it with the last answer and raises `Changed`, so a consumer that keeps
//     a copy of it (the skill arms its InputActions from it) learns of every change from every source (a gameplay hold released after a room change, the world's state, the network gate) and
//     never has to remember to refresh. Added at run time by whichever component needs it first (`For`), never part of a prefab.
// VI: Nửa MÔI TRƯỜNG của "thực thể người chơi này có nhận input cục bộ lúc này không": một định nghĩa duy nhất cho Move và Skill. `Allowed` tính trực tiếp; `Update` so với lần trước và bắn `Changed`
//     để nơi giữ bản sao (skill bật các InputAction theo nó) biết mọi thay đổi từ mọi nguồn.
[DisallowMultipleComponent]
public sealed class PNPlayerInputGate : MonoBehaviour
{
    private EveEntityLoader loader;
    private bool last, primed;

    // Raised from Update with the new answer whenever it differs from the last one seen (also when it goes from false to true).
    public event Action<bool> Changed;

    public static PNPlayerInputGate For(MonoBehaviour owner)
    {
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        var gate = owner.GetComponent<PNPlayerInputGate>();
        return gate != null ? gate : owner.gameObject.AddComponent<PNPlayerInputGate>();
    }

    public bool Allowed
    {
        get { if (loader == null) loader = GetComponent<EveEntityLoader>(); return PNLocalPlayerInput.Allows(loader); }
    }

    private void OnEnable() { primed = false; }

    private void Update()
    {
        bool now = Allowed;
        if (!primed) { primed = true; last = now; return; }
        if (now == last) return;
        last = now;
        var handler = Changed;
        if (handler != null) handler(now);
    }
}
