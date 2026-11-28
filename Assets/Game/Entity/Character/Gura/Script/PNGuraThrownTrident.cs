#if UNITY_5_3_OR_NEWER
using UnityEngine;

// EN: The Skill's thrown trident: the homing projectile wrapped in a coat of water with a tail of it, and a low ring of water with a mark where it strikes (picture only: the
//     damage is the projectile's own). The strike plays in the projectile's Ending, so every machine that shows the projectile shows it; it is the place the rush goes to.
// VI: Dinh ba nem cua Skill: ao nuoc boc ca cay, duoi nuoc, va vong nuoc tai cho trung (chi la hinh).
public sealed class PNGuraThrownTrident : PNHomingProjectile
{
    public const float StrikeSeconds = .38f;
        private PNGuraWaterFx water;
    private PNGuraAttackWeapon weapon, sheath;
    private Vector3 struckAt;
    private float struckAge;
    private bool struck;

    public override void OnPoolCreated()
    {
        base.OnPoolCreated();
        water = new PNGuraWaterFx(transform);
        weapon = new PNGuraAttackWeapon(transform, "Gura thrown trident");
        sheath = new PNGuraAttackWeapon(transform, "Gura thrown trident water", true);
    }

    protected override void OnStart(PNProjectileSpawn spawn)
    {
        base.OnStart(spawn);
        struck = false; struckAge = 0f;
    }

    private void LateUpdate()
    {
        if (!IsRunning || weapon == null || struck) return;
        Vector3 axis = Forward;
        Vector3 across = Vector3.Cross(Vector3.up, axis);
        if (across.sqrMagnitude < .001f) across = Vector3.right;
        across.Normalize();
        Vector3 tail = Position - axis * .49f;
        var pose = new PNGuraAttackWeapon.Pose(tail, axis, across, .67f, .23f, 0f);
        weapon.Draw(pose);
        // The same water that covers her: a thick coat of it around the whole trident.
        sheath.Draw(new PNGuraAttackWeapon.Pose(tail, axis, across, .67f, .23f * (1.65f + .08f * Mathf.Sin(Age * 31f)), 0f), .9f);
        water.Begin();
        water.Flight(pose, Age);
        water.TridentStream(pose, Age, 1.9f, .75f);
        // Round beads shed from the water coat, drifting aside and falling.
        Eye(out var right, out var up);
        for (int i = 0; i < 18; i++)
        {
            float seed = Mathf.Repeat(i * .618034f + .15f, 1f), variation = Mathf.Repeat(i * .414214f + .28f, 1f);
            float old = Mathf.Repeat(Age * Mathf.Lerp(3f, 4.5f, variation) + seed, 1f);
            Vector3 at = Position - axis * (.15f + 1.35f * old) + across * ((i % 2 == 0 ? 1f : -1f) * (.07f + .22f * old * seed)) + Vector3.up * (.1f * seed * old - .2f * old * old);
            water.Bead(at, (.016f + .034f * variation * variation) * (1f - .6f * old), Mathf.Clamp01((1f - old) * 1.4f) * Mathf.Clamp01(Age * 12f), right, up);
        }
        water.Draw();
    }

    private static void Eye(out Vector3 right, out Vector3 up)
    {
        var camera = PNViewCamera.Current;
        right = camera != null ? camera.transform.right : Vector3.right;
        up = camera != null ? camera.transform.up : Vector3.up;
    }

    protected override void OnImpact(PNImpact impact)
    {
        base.OnImpact(impact);
        if (impact.Kind != PNImpactKind.Target) return;
        struck = true; struckAt = impact.Point; struckAge = 0f;
        weapon?.Hide(); sheath?.Hide(); water?.Hide();
    }

    public override float EndingDuration => struck ? StrikeSeconds : 0f;

    protected override void OnEnding(float delta)
    {
        if (!struck || water == null) return;
        struckAge += delta;
        float p = Mathf.Clamp01(struckAge / StrikeSeconds);
        float fade = 1f - p * p;
        Eye(out var right, out var up);
        Vector3 viewer = -Vector3.Cross(right, up); viewer.y = 0f; viewer = viewer.sqrMagnitude < 1e-6f ? Vector3.back : viewer.normalized;
        Vector3 ground = new Vector3(struckAt.x, struckAt.y - .14f, struckAt.z);
        water.Begin();
        // A low ring of water thrown out from where it struck.
        water.Surge(ground, Mathf.Lerp(.18f, .62f, Mathf.Sqrt(p)), .16f * (1f - p), struckAge, fade, viewer, 0f);
        water.BeadBurst(struckAt, .6f, .5f, p, 22, .065f, right, up);
        // The mark: a ring closing in on the struck body, where the rush will arrive.
        water.Ripple(ground + Vector3.up * .01f, Mathf.Lerp(.65f, .26f, p), .05f, fade);
        water.Draw();
    }

    protected override void Cleanup()
    {
        water?.Hide();
        weapon?.Hide();
        sheath?.Hide();
        struck = false;
        base.Cleanup();
    }

    public override void DisposePooledResources()
    {
        water?.Dispose();
        weapon?.Dispose();
        sheath?.Dispose();
        water = null;
        weapon = sheath = null;
        base.DisposePooledResources();
    }
}
#else
public sealed class PNGuraThrownTrident { }
#endif
