#if UNITY_5_3_OR_NEWER
using EveEngine;
using UnityEngine;

// EN: The character's held auto target, shared by all of its skills (not one per skill). Resolve with the default rule (PNTargetRules)
//     keeps the held target while it stays valid and the rule does not switch (PNTargetRanking.ShouldSwitch); Resolve with a rule of the
//     skill's own neither reads nor changes the held target. Candidates come from the session's PNTargetIndex (the projectile manager's),
//     or a private index when the scene has no projectile manager. Only state; it never acts on the target.
// VI: Mục tiêu đang giữ của nhân vật, dùng chung cho mọi skill. Resolve theo luật mặc định giữ mục tiêu khi còn hợp lệ; Resolve với luật
//     riêng của skill không đọc cũng không đổi mục tiêu đang giữ. Chỉ là trạng thái, không tác động lên mục tiêu.
[DisallowMultipleComponent]
public sealed class PNAutoTarget : MonoBehaviour
{
    private PNProjectileTarget held;
    private PNProjectileManager manager;
    private PNTargetIndex privateIndex;
    // Held target if still valid (same loaded generation, alive, in this character's world); null otherwise.
    public PNStat Held => held.HasValue && held.TryGet(World, out var target) ? target : null;
    private EveWorldContext World => GetComponent<EveEntityIdentity>()?.World;

    public static PNAutoTarget For(PNStat stat)
    {
        if (stat == null) throw new System.ArgumentNullException(nameof(stat));
        return stat.TryGetComponent<PNAutoTarget>(out var existing) ? existing : stat.gameObject.AddComponent<PNAutoTarget>();
    }
    // Default rule of this character; updates the held target.
    public PNStat Resolve(float range, int layerMask, bool requireSight = false)
    {
        var stat = GetComponent<PNStat>();
        var target = Select(stat, range, layerMask, PNTargetRules.For(stat), requireSight, Held);
        held = new PNProjectileTarget(target);
        return target;
    }
    // A skill's own rule (for example Eve's lowest-health ultimate); the held target is untouched.
    public PNStat Resolve(float range, int layerMask, PNTargetRule ownRule, bool requireSight = false)
        => Select(GetComponent<PNStat>(), range, layerMask, ownRule, requireSight, null);
    public void Clear() => held = default;

    private PNStat Select(PNStat stat, float range, int layerMask, PNTargetRule rule, bool requireSight, PNStat current)
    {
        if (stat == null || !stat.IsReady || layerMask == 0 || !EveRegistrySession.IsReady) return null;
        var candidates = Index(EveRegistrySession.Context).Acquire(World);
        var query = new PNTargetQuery
        {
            Origin = transform.position, Range = range, LayerMask = layerMask, Rule = rule, Held = current, Exclude = stat.EntityId,
            Planar = true, RequireSight = requireSight, Chooser = stat
        };
        return PNTargetSelector.Select(candidates, query);
    }
    private PNTargetIndex Index(EveSessionContext session)
    {
        if (manager == null || !manager.isActiveAndEnabled || !ReferenceEquals(manager.Session, session)) manager = PNProjectileManager.Find(session, gameObject.scene);
        if (manager != null && ReferenceEquals(manager.Session, session)) return manager.Targets;
        if (privateIndex == null) privateIndex = new PNTargetIndex();
        if (!ReferenceEquals(privateIndex.Session, session)) privateIndex.Initialize(session, .1f);
        return privateIndex;
    }
    private void OnDisable() { held = default; privateIndex?.Clear(); }
}
#endif
