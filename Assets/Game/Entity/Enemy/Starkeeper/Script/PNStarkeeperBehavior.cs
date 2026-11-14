#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// The Starkeeper's decisions (the plan's section 4): find a target, walk up to it, and choose between the two registered skills by distance and by their own cooldown Abilities.
// Quet sao when close, Diem sao from a little further (the marked point is clamped to the skill's registered maxRange). Cooldown, windup, recovery and damage are registered data;
// the ranges below only choose WHICH skill to try. Not a permission to hit: the Actions do their own hit tests.
public sealed class PNStarkeeperBehavior : PNEnemyBehavior
{
    public override IEnumerable<EveId> SkillIds => new[] { PNStarkeeperCombatContent.StarSweep, PNStarkeeperCombatContent.StarPoint };
    [SerializeField, Min(.1f)] private float aggroRange = 6f, sweepRange = 1.4f, pointMin = 1.5f, pointMax = 4.5f;
    private PNStat target; private PNEnemyNavigationRequest walk; private float scan; private int last;
    private PNEnemyTargeting targeting;
    // The sight range is the character's registered `body.vision`; `aggroRange` is the default of an entity that has no role data.
    public override void Enter() { target = null; walk = null; scan = 0; last = 0; targeting = new PNEnemyTargeting(Context, aggroRange) { Planar = false }; }
    public override void Tick(float dt)
    {
        scan -= dt;
        if (Context.Owner.IsExecutingEnemyAction) { CancelWalk(); return; }
        if (!targeting.Keep(target)) target = null;
        if (target == null && scan <= 0) { scan = .2f; target = targeting.Acquire(); }
        if (target == null) { CancelWalk(); return; }
        var delta = target.transform.position - Context.Owner.transform.position; delta.y = 0; float dist = delta.magnitude;
        var sweep = Context.Abilities.GetCooldown(EveId.Parse(PNStarkeeperCombatContent.StarSweep + "_ready"));
        var point = Context.Abilities.GetCooldown(EveId.Parse(PNStarkeeperCombatContent.StarPoint + "_ready"));
        bool sweepReady = sweep.CanConsume(), pointReady = point.CanConsume();
        // the same skill is not repeated while the other one is ready and in its range
        if (dist <= sweepRange && sweepReady && !(last == 1 && pointReady && dist >= pointMin)) { Cast(PNStarkeeperCombatContent.StarSweep, delta, sweep, 1, null); return; }
        if (dist >= pointMin && dist <= pointMax && pointReady) { Cast(PNStarkeeperCombatContent.StarPoint, delta, point, 2, MarkedPoint(delta)); return; }
        if (dist > sweepRange * .9f) { if (walk == null || walk.IsTerminal) walk = Context.GoTo(target.transform.position, sweepRange * .8f); else Context.UpdateGoal(walk, target.transform.position); }
        else CancelWalk();
    }
    // The star is placed at the target's feet, no further than the registered maxRange along the line to it.
    private Vector3 MarkedPoint(Vector3 delta)
    {
        float range = Context.Owner.GetData(PNStarkeeperCombatContent.StarPoint).Action.Perform["maxRange"].Value<float>();
        var origin = Context.Owner.transform.position; var flat = delta.magnitude > range ? delta.normalized * range : delta;
        return new Vector3(origin.x + flat.x, origin.y, origin.z + flat.z);
    }
    private void Cast(EveId id, Vector3 delta, PNAbilityCooldown cooldown, int which, Vector3? position)
    {
        CancelWalk(); Context.Move.TryFaceSkill(new Vector2(delta.x, delta.z));
        var actor = Context.Owner.GetComponent<PNVisualBody>()?.Actor;
        if (actor != null) { var camera = PNViewCamera.Current; actor.SetFacingLeft(Vector3.Dot(delta, camera != null ? camera.transform.right : Vector3.right) < 0); }
        var direction = position.HasValue ? position.Value - Context.Owner.transform.position : delta; direction.y = 0; if (direction.sqrMagnitude < 1e-6f) direction = Vector3.forward;
        if (Context.TryCast(id, direction, PNEnemyActionExecution.AuxiliaryTimeline, out var receipt, new[] { target }, () => cooldown.TryConsume(), position) == PNEnemyCastAdmission.Started) last = which;
    }
    private void CancelWalk() { if (walk != null && !walk.IsTerminal) Context.CancelNavigation(walk); walk = null; }
    public override void Exit(PNEnemyBehaviorExit reason) { CancelWalk(); target = null; }
}
#endif
