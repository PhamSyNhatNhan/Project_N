#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Diem sao: a star is marked on the floor at the target's place and stays there; the Starkeeper stands still while it blinks (windup), then lunges along the line to the star
// (the shared contact-motion trajectory: stops at a wall, one hit per target along the swept path), then stands for the recovery. The lunge schema is the "motion" part of the
// registered payload; the distance is clamped to the marked point by MotionDistance. Nothing here keeps a number of its own.
public sealed class PNStarkeeperStarPointAction : PNAgentContactMotionAction
{
    private float age, windup, recovery, recoverAge, lunge; private bool lunging, motionDone, finished, disposed;
    private PNStarkeeperTelegraph telegraph; private PNStarkeeperPresentation presentation; private PNVisualBody body;
    protected override JToken MotionPayload(PNSkillActionContext value) => value.Data.Action.Perform["motion"];
    protected override float MotionDistance(float configured, Vector3 origin, Vector3 anchor) { var d = anchor - origin; d.y = 0; return Mathf.Min(configured, d.magnitude); }
    public override void Start(PNSkillActionContext c)
    {
        if (disposed) throw new InvalidOperationException("Fresh cast required");
        PNStarkeeperCombatContent.ValidateStarPoint(c.Data.Action, c.Registry);
        if (!c.Position.HasValue || !c.Direction.HasValue) throw new InvalidOperationException("Star point and direction required");
        var p = c.Data.Action.Perform;
        windup = p["windup"].Value<float>(); recovery = p["recovery"].Value<float>(); lunge = p["motion"]["duration"].Value<float>();
        base.Start(c);                                           // reads the motion schema, clamps the distance to the star, locks movement, checks the floor along the path
        try
        {
            body = c.Skill.GetComponent<PNVisualBody>(); body?.PlayAction("star_point", windup + lunge + recovery);
            var origin = c.Skill.transform.position; var direction = c.Position.Value - origin; direction.y = 0;
            presentation = c.Skill.GetComponent<PNStarkeeperPresentation>(); presentation?.BeginPoint(windup, lunge, direction.sqrMagnitude > 1e-6f ? direction.normalized : c.Direction.Value);
            telegraph = PNStarkeeperTelegraph.Point(origin, c.Position.Value, p["markRadius"].Value<float>(), c.Skill.gameObject.scene);
        }
        catch { Dispose(); throw; }
    }
    public override bool AdvanceAuxiliary(float dt)
    {
        if (disposed) return true;
        if (Outcome != PNMotionOutcome.Running && !lunging) return true;          // the floor check failed at Start: no lunge, end the cast
        age += dt; presentation?.SetAge(age);
        if (age < windup) { telegraph?.SetPhase(age / windup); return false; }
        if (!lunging) { lunging = true; telegraph?.Hide(); }
        if (!motionDone) { motionDone = base.AdvanceAuxiliary(dt); return false; }
        recoverAge += dt;
        return recoverAge >= recovery;
    }
    public override void Dispose()
    {
        if (!disposed) { disposed = true; telegraph?.Dispose(); telegraph = null; presentation?.EndPose(); presentation = null; body?.EndAction(); body = null; }
        base.Dispose();
    }
}
#endif
