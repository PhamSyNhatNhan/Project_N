#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Diem sao: a thrown gold spear marks the clamped destination; the Starkeeper stands still while it blinks (windup), then lunges along the line to the star
// (the shared contact-motion trajectory: stops at a wall, one hit per target along the swept path), then stands for the recovery. The lunge schema is the "motion" part of the
// registered payload; the distance is clamped to the marked point by MotionDistance. Nothing here keeps a number of its own. The picture is `PNStarkeeperPointView` (placed by the run's age on every
// machine); this Action presents the one physical result it needs, `finish`, and draws nothing.
public sealed class PNStarkeeperStarPointAction : PNAgentContactMotionAction
{
    private float age, windup, recovery, recoverAge; private bool lunging, motionDone, disposed;
    private PNSkillActionContext context;
    protected override JToken MotionPayload(PNSkillActionContext value) => value.Data.Action.Perform["motion"];
    protected override float MotionDistance(float configured, Vector3 origin, Vector3 anchor) { var d = anchor - origin; d.y = 0; return Mathf.Min(configured, d.magnitude); }
    public override void Start(PNSkillActionContext c)
    {
        if (disposed) throw new InvalidOperationException("Fresh cast required");
        PNStarkeeperCombatContent.ValidateStarPoint(c.Data.Action, c.Registry);
        if (!c.Position.HasValue || !c.Direction.HasValue) throw new InvalidOperationException("Star point and direction required");
        var p = c.Data.Action.Perform;
        windup = p["windup"].Value<float>(); recovery = p["recovery"].Value<float>();
        context = c;
        try { base.Start(c); }                                   // reads the motion schema, clamps the distance to the star, locks movement, checks the floor along the path
        catch { Dispose(); throw; }
    }
    public override bool AdvanceAuxiliary(float dt)
    {
        if (disposed) return true;
        if (Outcome != PNMotionOutcome.Running && !lunging) return true;          // the floor check failed at Start: no lunge, end the cast
        age += dt;
        if (age < windup) return false;
        if (!lunging) lunging = true;
        if (!motionDone)
        {
            motionDone=base.AdvanceAuxiliary(dt);
            if(motionDone) context?.Present(PNStarkeeperPointView.FinishFact, Outcome==PNMotionOutcome.Completed?1d:0d);
            return false;
        }
        recoverAge += dt;
        return recoverAge >= recovery;
    }
    public override void Dispose()
    {
        if (!disposed) { disposed = true; context = null; }
        base.Dispose();
    }
}
#endif
