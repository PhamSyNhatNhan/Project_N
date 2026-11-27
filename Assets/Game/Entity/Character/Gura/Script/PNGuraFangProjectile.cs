#if UNITY_5_3_OR_NEWER
using UnityEngine;

// EN: A Fang of the Ultimate inside Burst: the homing projectile with the water burst of the former Skill drawn small where it hits (picture only: the damage is the projectile's own).
//     The burst plays in the projectile's Ending, so it lives on after the hit on every machine that shows the projectile. A Fang that ends without a target hit shows nothing.
// VI: Rang cua Ultimate trong Burst: dan tu dan, ve cu no nuoc nho tai cho trung (chi la hinh).
public sealed class PNGuraFangProjectile : PNHomingProjectile
{
    public const float BurstSeconds = .6f, BurstRadius = 1f;
    private PNGuraWaterParticles burst;
    private Vector3 at;
    private float age;
    private bool hit;

    public override void OnPoolCreated()
    {
        base.OnPoolCreated();
        burst = PNGuraWaterParticles.Create(transform, "skill_explosion");
    }

    protected override void OnStart(PNProjectileSpawn spawn)
    {
        base.OnStart(spawn);
        hit = false; age = 0f; burst?.Hide();
    }

    protected override void OnImpact(PNImpact impact)
    {
        base.OnImpact(impact);
        if (impact.Kind != PNImpactKind.Target) return;
        hit = true; at = impact.Point; age = 0f;
    }

    public override float EndingDuration => hit ? BurstSeconds : 0f;

    protected override void OnEnding(float delta)
    {
        if (!hit) return;
        age += delta;
        // The same moments of the pack's explosion the former Skill played (PNGuraTridentSplash), scaled to a small radius.
        float progress = Mathf.Clamp01(age / BurstSeconds);
        burst?.Sample(at, Vector3.zero, Vector3.one * (BurstRadius / 2.35f), Mathf.Lerp(.125f, .815f, progress));
    }

    protected override void Cleanup()
    {
        burst?.Hide();
        hit = false;
        base.Cleanup();
    }

    public override void DisposePooledResources()
    {
        burst?.Dispose();
        burst = null;
        base.DisposePooledResources();
    }
}
#else
public sealed class PNGuraFangProjectile { }
#endif
