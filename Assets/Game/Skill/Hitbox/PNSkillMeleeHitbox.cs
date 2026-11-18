#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

// One melee shape and hit history, shared by local sweeps and accepted owner reports.
internal sealed class PNSkillMeleeHitbox : IPNSkillHitReceiver, IDisposable
{
    private readonly PNSkillActionContext context;
    private readonly PNStat caster;
    private readonly PNSkillDamageCapture damage;
    private readonly List<PNStat> targets = new List<PNStat>();
    private PNHitboxCast cast;
    private IDisposable reportLease;
    private Action<Vector3, PNHitOutcome> onContact;
    private Vector3 reportOrigin;
    private float reportRadius;
    private int extraLayers;
    private bool disposed;
    public double Scale => damage.Scale;
    public PNDamage Damage { get; private set; }
    public double ReportTime { get; set; }
    public bool CanSample => cast != null;

    public PNSkillMeleeHitbox(PNSkillActionContext context, PNStat caster, PNSkillValues values, double? iframe)
    {
        this.context = context;
        this.caster = caster;
        damage = context.PrepareMeleeDamage(values, iframe,
            () => context.Skill.GetComponent<PNSkillScaleState>()?.ReadScale(PNContentIds.MeleeScale) ?? 1);
    }

    public void Open(EveHitboxShape shape, Vector3 origin, float radius, int layers, Action<Vector3, PNHitOutcome> contact)
    {
        if (context.Mode == PNSkillMode.Observed) return;
        reportOrigin = origin;
        reportRadius = radius;
        extraLayers = layers;
        onContact = contact;
        cast = new PNHitboxCast(shape, caster.gameObject.scene.GetPhysicsScene());
        if (context.Mode == PNSkillMode.Authority)
        {
            Damage = damage.CreatePacket();
            reportLease = context.Skill.RegisterHitReceiver(this);
        }
    }

    private bool Allows(PNStat target)
        => !ReferenceEquals(target, caster) && PNLayers.Contains(caster.TargetLayers.Mask | extraLayers, target.Layer);

    public void Sample(EveHitboxPose pose, double time)
    {
        if (cast == null) return;
        cast.Query(pose, time, targets, caster, filter: Allows);
        foreach (var target in targets)
        {
            if (disposed) break;
            if (!target.IsReady || target.IsDead) continue;
            cast.CommitHit(target, time);
            Contact(target);
        }
    }

    public bool AcceptReportedHit(PNStat target, int step, float tolerance)
    {
        if (cast == null || target == null || !target.IsReady || target.IsDead ||
            !caster.IsReady || caster.IsDead || !Allows(target) || target.gameObject.scene != caster.gameObject.scene)
            return false;
        var offset = target.transform.position - reportOrigin;
        offset.y = 0;
        if (offset.magnitude > reportRadius + tolerance || !cast.TryCommitReported(target, ReportTime))
            return false;
        Contact(target);
        return true;
    }

    private void Contact(PNStat target)
    {
        var point = target.transform.position;
        var notify = onContact;
        var outcome = context.Hit(target, point, Damage, damage);
        notify?.Invoke(point, outcome);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        reportLease?.Dispose();
        cast?.Dispose();
        reportLease = null;
        cast = null;
        onContact = null;
    }
}
#endif
