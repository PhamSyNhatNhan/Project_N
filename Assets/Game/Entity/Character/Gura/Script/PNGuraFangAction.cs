using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// EN: `project_n:gura_fang_action` (Docs/GuraCharacter.md 4.4, the Ultimate inside Burst): one projectile per Fang, fired one after the other, then the water shark bursts (a blast) whose damage grows with every Fang.
//     Payload {fangCounterId, windup, interval, maxFlight, recovery, perFang, projectile (a `create_projectile` payload, count 1), blast (a `create_blast` payload; `perFang` is added to its input value amount per Fang)}.
//     The Fang count is read from the caster's Ability counter when the Action starts (the behavior spends them right after). At least one projectile is fired. The shark bursts where the target stands (the
//     last tracked projectile point when there is none); it is the authority's (create_blast admits only it). The Action has no network code.
// VI: Moi Rang mot vien dan ban lan luot, roi ca map no (blast) co sat thuong tang theo so Rang. So Rang doc tu Ability luc Action bat dau.
public sealed class PNGuraFangAction : PNSkillAction
{
    public static void Validate(PNSkillActionData data, EveRegistryHub hub)
    {
        var p = PNBuffContentBatch.Object(data.Perform);
        PNBuffContentBatch.Fields(p, "fangCounterId", "windup", "interval", "maxFlight", "recovery", "perFang", "projectile", "blast");
        EveId.Parse(PNBuffContentBatch.String(p, "fangCounterId"));
        foreach (var key in new[] { "windup", "interval", "maxFlight", "recovery", "perFang" }) { double n = PNBuffContentBatch.Number(p, key); if (!(n >= 0d && n <= 100d) || key == "maxFlight" && n <= 0d) throw new FormatException("Invalid fang number: " + key); }
        PNCreateProjectileOperation.Validate(p["projectile"], hub); PNCreateBlastOperation.Validate(p["blast"], hub);
        if (!(PNBuffContentBatch.Object(p["blast"])["input"] is JObject input) || !(input["value"] is JObject value) || value["amount"] == null) throw new FormatException("Fang blast needs an input value with an amount.");
    }

#if UNITY_5_3_OR_NEWER
    private PNSkillActionContext context; private JObject payload;
    private float age, windup, interval, maxFlight, recovery, perFang, lastFiredAt, burstAt;
    private int fangs, fired; private bool blasted, ended;
    private readonly List<PNCreateProjectileOperation> volleys = new List<PNCreateProjectileOperation>(); private readonly List<PNSkillOperation> operations = new List<PNSkillOperation>();
    private Vector3 lastPoint; private bool hasPoint;
    public int Fangs => fangs;
    public override bool HasAuxiliaryTimeline => true;

    public override void Start(PNSkillActionContext c)
    {
        if (context != null || ended) throw new InvalidOperationException("Fresh cast required");
        Validate(c.Data.Action, c.Registry);
        if (!c.Direction.HasValue) throw new InvalidOperationException("Fang direction required");
        payload = (JObject)c.Data.Action.Perform; windup = payload["windup"].Value<float>(); interval = payload["interval"].Value<float>(); maxFlight = payload["maxFlight"].Value<float>();
        recovery = payload["recovery"].Value<float>(); perFang = payload["perFang"].Value<float>();
        var counter = c.Skill.Abilities.GetCounter(EveId.Parse(payload["fangCounterId"].Value<string>())); fangs = Math.Max(1, counter.Current);
        context = c; c.BeginAction();
    }
    public override void Perform(PNSkillActionContext c) { }

    public override bool AdvanceAuxiliary(float dt)
    {
        if (ended || context == null) return true;
        age += dt;
        while (fired < fangs && age >= windup + fired * interval) Fire();
        if (fired >= fangs && !blasted)
        {
            Track();
            if (AllEnded() || age - lastFiredAt >= maxFlight) Burst();
        }
        return blasted && age >= burstAt + recovery;
    }
    private void Fire()
    {
        var launch = CreateOperation(context, PNProjectileGameplayContent.CreateProjectile); operations.Add(launch);
        launch.Execute(context, payload["projectile"]);
        if (launch is PNCreateProjectileOperation volley) volleys.Add(volley);
        lastFiredAt = age; fired++;
        if (!hasPoint) { lastPoint = context.Skill.transform.position; hasPoint = true; }
    }
    private bool AllEnded()
    {
        foreach (var volley in volleys) foreach (var receipt in volley.Results) if (receipt.Status == PNProjectileStatus.Created && receipt.Handle.IsAlive) return false;
        return true;
    }
    private void Track()
    {
        var manager = PNProjectileManager.Resolve(EveRegistrySession.Context, context.Skill.gameObject.scene);
        foreach (var volley in volleys) foreach (var receipt in volley.Results)
        {
            if (receipt.Status != PNProjectileStatus.Created || !receipt.Handle.IsAlive) continue;
            var snapshot = manager.Get(receipt.Handle); if (snapshot != null && snapshot.IsRunning) { lastPoint = snapshot.Position; hasPoint = true; }
        }
    }
    private void Burst()
    {
        blasted = true; burstAt = age;
        Vector3 at;
        if (context.Targets.Count > 0 && context.Targets[0] != null && context.Targets[0].isActiveAndEnabled) at = context.Targets[0].transform.position;
        else if (hasPoint) at = lastPoint; else return;
        var blast = (JObject)payload["blast"].DeepClone(); var value = (JObject)blast["input"]["value"]; value["amount"] = value["amount"].Value<double>() + perFang * fangs;
        var place = new PNSkillActionContext(context.Skill, context.SkillId, context.Data, context.Registry, null, context.Direction, context.Invocation, false, at);
        using (var create = CreateOperation(place, PNBlastGameplayContent.CreateBlast)) create.Execute(place, blast);
    }
    public override void Exit(PNSkillActionContext c) { try { c.CompleteAction(); } finally { Dispose(); } }
    public override void Cancel(PNSkillActionContext c) { try { c.CancelEffects(); } finally { Dispose(); } }
    public override void Dispose()
    {
        if (ended) return; ended = true;
        foreach (var operation in operations) operation.Dispose();
        operations.Clear(); volleys.Clear(); context = null;
    }
#else
    public override void Start(PNSkillActionContext c) { Validate(c.Data.Action, c.Registry); }
    public override void Perform(PNSkillActionContext c) { }
    public override void Exit(PNSkillActionContext c) { }
    public override void Cancel(PNSkillActionContext c) { }
#endif
}
