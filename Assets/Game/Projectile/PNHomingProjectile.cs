using System;
using EveEngine;
using Newtonsoft.Json.Linq;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// Payload of project_n:homing = every project_n:directional field plus (all required):
//   turnRate: degrees per second the direction can turn toward the target (> 0),
//   homeAfter: seconds of the projectile clock flown straight before it starts to turn (>= 0),
//   lostTarget: "Reacquire" (default behavior: pick the nearest live target again) or "KeepHeading" (fly on in the last direction),
//   searchRadius: how far a search looks (> 0), searchInterval: minimum seconds between two searches of one projectile (> 0).
public sealed class PNHomingSettings
{
    public readonly PNDirectionalSettings Directional;
    public readonly double TurnRate, HomeAfter, SearchRadius, SearchInterval;
    public readonly bool Reacquire;
    public static void ValidateDefinition(JToken token, EveRegistryHub hub) => PNDirectionalSettings.ValidateLayers(new PNHomingSettings(token).Directional);
    public PNHomingSettings(JToken token)
    {
        var obj = (JObject)PNBuffContentBatch.Object(token).DeepClone();
        double Take(string name, bool positive)
        {
            double value = PNBuffContentBatch.Number(obj, name); obj.Remove(name);
            if (value < 0 || positive && value <= 0 || value > 1e9) throw new FormatException("Homing " + name + " is out of range.");
            return value;
        }
        TurnRate = Take("turnRate", true); HomeAfter = Take("homeAfter", false); SearchRadius = Take("searchRadius", true); SearchInterval = Take("searchInterval", true);
        string policy = PNBuffContentBatch.String(obj, "lostTarget"); obj.Remove("lostTarget");
        if (policy != "Reacquire" && policy != "KeepHeading") throw new FormatException("Homing lostTarget must be Reacquire or KeepHeading.");
        Reacquire = policy == "Reacquire";
        Directional = new PNDirectionalSettings(obj);
    }
}

#if UNITY_5_3_OR_NEWER
// EN: A directional projectile that turns toward a target. The target comes from the spawn request or, when it is missing or lost
//     and the policy is Reacquire, from the nearest live target on the launch layers within searchRadius (searched at most every
//     searchInterval). Turning is limited by turnRate and starts after homeAfter seconds.
// VI: Đạn directional bẻ về phía mục tiêu. Mục tiêu từ yêu cầu bắn, hoặc khi thiếu/mất và chính sách là Reacquire thì tìm lại mục
//     tiêu gần nhất; tốc độ bẻ giới hạn bởi turnRate và bắt đầu sau homeAfter giây.
public class PNHomingProjectile : PNDirectionalProjectile
{
    private PNHomingSettings homing;
    private float nextSearch;
    protected PNHomingSettings Homing => homing;
    protected override PNDirectionalSettings ParseSettings()
    {
        homing = Definition.Settings(payload => new PNHomingSettings(payload));
        return homing.Directional;
    }
    protected override void Initialize(PNProjectileSpawn spawn) { base.Initialize(spawn); nextSearch = 0f; steeredTick = -1; haveCenter = false; }
    protected override void Move(float dt)
    {
        if (Age >= homing.HomeAfter) Steer(dt);
        base.Move(dt);
    }
    // Target validation, search and the target's center are resolved once per manager tick; the turn itself happens in every sub-step.
    private int steeredTick = -1; private bool haveCenter; private Vector3 targetCenter;
    private void Steer(float dt)
    {
        if (steeredTick != TickIndex)
        {
            steeredTick = TickIndex; haveCenter = false;
            if (!TryGetTarget(out var target) && homing.Reacquire && Age >= nextSearch)
            {
                nextSearch = Age + (float)homing.SearchInterval;
                target = FindNearestTarget((float)homing.SearchRadius);
                if (target != null) SetTarget(target);
            }
            if (target != null) haveCenter = PNProjectileTargets.TryGetHitVolume(target, out targetCenter, out _);
        }
        if (!haveCenter) return;
        var desired = targetCenter - Position;
        if (desired.sqrMagnitude < 1e-8f) return;
        Direction = Vector3.RotateTowards(Direction, desired.normalized, (float)homing.TurnRate * Mathf.Deg2Rad * dt, 0f);
    }
}
#else
// Placeholder so the standalone (non-Unity) build can register the implementation, like PNTimeScaleArea.
public class PNHomingProjectile { }
#endif
