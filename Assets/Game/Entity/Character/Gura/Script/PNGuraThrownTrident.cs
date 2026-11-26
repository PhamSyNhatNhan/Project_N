#if UNITY_5_3_OR_NEWER
using UnityEngine;

public sealed class PNGuraThrownTrident : PNHomingProjectile
{
    private PNGuraWaterFx water;
    private PNGuraAttackWeapon weapon;

    public override void OnPoolCreated()
    {
        base.OnPoolCreated();
        water = new PNGuraWaterFx(transform);
        weapon = new PNGuraAttackWeapon(transform, "Gura thrown trident");
    }

    private void LateUpdate()
    {
        if (!IsRunning || weapon == null) return;
        Vector3 axis = Forward;
        Vector3 across = Vector3.Cross(Vector3.up, axis);
        if (across.sqrMagnitude < .001f) across = Vector3.right;
        var pose = new PNGuraAttackWeapon.Pose(Position - axis * .49f, axis, across.normalized, .67f, .23f, 0f);
        weapon.Draw(pose);
        water.Begin();
        water.Flight(pose, Age);
        water.Draw();
    }

    protected override void Cleanup()
    {
        water?.Hide();
        weapon?.Hide();
        base.Cleanup();
    }

    public override void DisposePooledResources()
    {
        water?.Dispose();
        weapon?.Dispose();
        water = null;
        weapon = null;
        base.DisposePooledResources();
    }
}
#else
public sealed class PNGuraThrownTrident { }
#endif
