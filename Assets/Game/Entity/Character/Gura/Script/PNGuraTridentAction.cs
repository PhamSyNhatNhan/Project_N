using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// EN: `project_n:gura_trident_action` (Docs/GuraCharacter.md 4.2): a thrown trident that flies on its own to the target and bursts in a blast where it ends (hit, wall or end of its time). Payload:
//       windup (s) before the throw, recovery (s) after the blast, maxFlight (s) the cast waits for the trident, projectile = a `create_projectile` payload, blast = a `create_blast` payload (its position is where the
//       trident ended). The Action creates the projectile through the registered Operation, then watches it through the projectile manager; the blast is the authority's (a guess draws nothing of it).
//     The cast stays open while the trident flies (at most maxFlight), so the skill cannot overlap itself; it has no network code.
// VI: Dinh ba bay tu dong toi muc tieu va no blast noi no ket thuc (trung, cham tuong, het gio). Action tao dan qua Operation dang ky roi theo doi qua projectile manager; blast la cua authority.
public sealed class PNGuraTridentAction : PNSkillAction
{
    public static void Validate(PNSkillActionData data, EveRegistryHub hub)
    {
        var p = PNBuffContentBatch.Object(data.Perform);
        PNBuffContentBatch.Fields(p, "windup", "recovery", "maxFlight", "projectile", "blast");
        foreach (var key in new[] { "windup", "recovery", "maxFlight" }) { double n = PNBuffContentBatch.Number(p, key); if (!(n >= 0d && n <= 10d) || key == "maxFlight" && n <= 0d) throw new FormatException("Invalid trident number: " + key); }
        PNCreateProjectileOperation.Validate(p["projectile"], hub); PNCreateBlastOperation.Validate(p["blast"], hub);
    }

#if UNITY_5_3_OR_NEWER
    private PNSkillActionContext context; private JObject payload;
    private float age, windup, recovery, maxFlight, thrownAt;
    private bool thrown, blasted, ended;
    private PNSkillOperation launch; private PNCreateProjectileOperation volley;
    private Vector3 lastPoint; private bool hasPoint;
    public override bool HasAuxiliaryTimeline => true;

    public override void Start(PNSkillActionContext c)
    {
        if (context != null || ended) throw new InvalidOperationException("Fresh cast required");
        Validate(c.Data.Action, c.Registry);
        if (!c.Direction.HasValue) throw new InvalidOperationException("Trident direction required");
        payload = (JObject)c.Data.Action.Perform; windup = payload["windup"].Value<float>(); recovery = payload["recovery"].Value<float>(); maxFlight = payload["maxFlight"].Value<float>();
        context = c; c.BeginAction();
    }
    public override void Perform(PNSkillActionContext c) { }

    public override bool AdvanceAuxiliary(float dt)
    {
        if (ended || context == null) return true;
        age += dt;
        if (!thrown && age >= windup) Throw();
        if (thrown && !blasted)
        {
            Track();
            if (volley == null || Ended() || age - thrownAt >= maxFlight) Burst();
        }
        return blasted && age >= thrownAt + recovery || thrown && volley == null && age >= windup + recovery;
    }

    private void Throw()
    {
        thrown = true; thrownAt = age;
        launch = CreateOperation(context, PNProjectileGameplayContent.CreateProjectile);
        launch.Execute(context, payload["projectile"]);
        volley = launch as PNCreateProjectileOperation;
        if (volley == null || volley.Results.Count == 0) volley = null;
        else { var self = context.Skill.transform.position; lastPoint = self; hasPoint = true; }
    }
    private bool Ended()
    {
        foreach (var receipt in volley.Results) if (receipt.Status == PNProjectileStatus.Created && receipt.Handle.IsAlive) return false;
        return true;
    }
    private void Track()
    {
        if (volley == null) return;
        var manager = PNProjectileManager.Resolve(EveRegistrySession.Context, context.Skill.gameObject.scene);
        foreach (var receipt in volley.Results)
        {
            if (receipt.Status != PNProjectileStatus.Created || !receipt.Handle.IsAlive) continue;
            var snapshot = manager.Get(receipt.Handle);
            if (snapshot != null && snapshot.IsRunning) { lastPoint = snapshot.Position; hasPoint = true; }
        }
    }
    // The trident ended (or ran out of waiting): the blast bursts where it was last seen, through the registered create_blast (it admits only the authority; a guess draws nothing here).
    private void Burst()
    {
        blasted = true; thrownAt = age;
        if (!hasPoint) return;
        var at = new PNSkillActionContext(context.Skill, context.SkillId, context.Data, context.Registry, null, context.Direction, context.Invocation, false, lastPoint);
        using (var create = CreateOperation(at, PNBlastGameplayContent.CreateBlast)) create.Execute(at, payload["blast"]);
    }
    public override void Exit(PNSkillActionContext c) { try { c.CompleteAction(); } finally { Dispose(); } }
    public override void Cancel(PNSkillActionContext c) { try { c.CancelEffects(); } finally { Dispose(); } }
    public override void Dispose()
    {
        if (ended) return; ended = true;
        launch?.Dispose(); launch = null; volley = null; context = null;
    }
#else
    public override void Start(PNSkillActionContext c) { Validate(c.Data.Action, c.Registry); }
    public override void Perform(PNSkillActionContext c) { }
    public override void Exit(PNSkillActionContext c) { }
    public override void Cancel(PNSkillActionContext c) { }
#endif
}
