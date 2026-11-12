#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// The Starforger's decisions (Docs/StarforgerSkillsPlan.md). It looks for the hostile it sees (the shared aggro rule, walls block the sight), then every tick:
//   1. the slam when the target is within its registered `range` (3.5 m), it is ready and the jump has somewhere to land (the landing is the target's spot, shortened to the free length along the jump;
//      the floor and the navigation along the way are checked here and again by the trajectory),
//   2. otherwise the star when fewer than `maxStars` stars live, it is ready and the target is within its `range` (4 m) - but not while the slam is about to be ready (`holdForSlam` seconds) with the target in its reach,
//   3. otherwise it walks toward the target until it is within `standDistance` and then stands.
// Nothing here keeps a combat number: damage, cooldowns, times and ranges are registered data; only the sight, the stand distance and the pause between casts are tuned here.
public sealed class PNStarforgerBehavior : PNEnemyBehavior
{
    public override IEnumerable<EveId> SkillIds => new[] { PNStarforgerCombatContent.Slam, PNStarforgerCombatContent.Star };
    [SerializeField, Min(.1f)] private float standDistance = 2.5f, vision = 7f, pause = .35f, holdForSlam = 1.2f;
    private PNStat target; private PNEnemyNavigationRequest walk; private float rest, scan;
    private readonly RaycastHit[] hits = new RaycastHit[8];
    private PNEnemyTargeting targeting;
    // The sight range is the character's registered `body.vision`; `vision` is the default of an entity that has no role data.
    public override void Enter() { target = null; walk = null; rest = scan = 0; targeting = new PNEnemyTargeting(Context, vision); }
    private JToken Perform(EveId id) => Context.Owner.GetData(id).Action.Perform;
    public override void Tick(float dt)
    {
        rest -= dt; scan -= dt;
        if (Context.Owner.IsExecutingEnemyAction) { CancelWalk(); return; }
        var self = Context.Stat; if (self == null || !self.IsReady || self.IsDead) { CancelWalk(); return; }
        if (!targeting.Keep(target)) target = null;
        if (target == null && scan <= 0) { scan = .2f; target = targeting.Acquire(); }
        if (target == null) { CancelWalk(); return; }
        var delta = target.transform.position - self.transform.position; delta.y = 0; float distance = delta.magnitude;
        if (rest <= 0 && TryCasts(delta, distance)) return;
        if (distance > standDistance)
        { if (walk == null || walk.IsTerminal) walk = Context.GoTo(target.transform.position, standDistance * .9f); else Context.UpdateGoal(walk, target.transform.position); }
        else { CancelWalk(); Face(delta); }
    }
    private float Flat(Vector3 point) { var d = point - Context.Owner.transform.position; d.y = 0; return d.magnitude; }
    private PNAbilityCooldown Cooldown(EveId id) => Context.Abilities.GetCooldown(EveId.Parse(id + "_ready"));
    private float Range(EveId id) => Perform(id)["range"].Value<float>();

    private bool TryCasts(Vector3 delta, float distance)
    {
        var slam = Cooldown(PNStarforgerCombatContent.Slam); var star = Cooldown(PNStarforgerCombatContent.Star);
        if (slam.CanConsume() && distance <= Range(PNStarforgerCombatContent.Slam) && CastSlam(delta, distance, slam)) return true;
        // The slam comes first: a star cast (a second long) is not begun while the slam is about to be ready and the target is within its reach, so the jump is never kept waiting by a star.
        if (!slam.CanConsume() && slam.CooldownRemaining <= holdForSlam && distance <= Range(PNStarforgerCombatContent.Slam)) return false;
        var skill = Context.Owner.GetComponent<PNStarforgerSkill>();
        if (star.CanConsume() && skill != null && skill.AliveCount < Perform(PNStarforgerCombatContent.Star)["maxStars"].Value<int>() && distance <= Range(PNStarforgerCombatContent.Star))
        {
            CancelWalk(); Face(delta);
            if (Context.TryCast(PNStarforgerCombatContent.Star, delta.sqrMagnitude > 1e-5f ? delta : Vector3.forward, PNEnemyActionExecution.AuxiliaryTimeline, out var receipt, new[] { target }, () => star.TryConsume()) == PNEnemyCastAdmission.Started)
            { rest = pause; return true; }
        }
        return false;
    }
    // The landing: the target's spot, never farther than the registered range and never into a wall; the floor under it and the navigation along the jump must hold.
    private bool CastSlam(Vector3 delta, float distance, PNAbilityCooldown cooldown)
    {
        var perform = Perform(PNStarforgerCombatContent.Slam); float range = perform["range"].Value<float>();
        var direction = delta.sqrMagnitude > 1e-6f ? delta.normalized : Vector3.forward; var origin = Context.Owner.transform.position; var physics = Context.Owner.gameObject.scene.GetPhysicsScene();
        int n = physics.Raycast(origin + Vector3.up * .2f, direction, hits, Mathf.Min(distance, range) + .3f, LayerMask.GetMask("Wall"), QueryTriggerInteraction.Ignore);
        float free = Mathf.Min(distance, range) + .3f; for (int i = 0; i < n; i++) free = Mathf.Min(free, hits[i].distance);
        float jump = PNStarforgerPlanner.JumpDistance(distance, range, free);
        if (!PNStarforgerPlanner.CanJump(jump)) return false;
        var landing = origin + direction * jump;
        if (!physics.Raycast(landing + Vector3.up * .3f, Vector3.down, out RaycastHit ground, .5f, 1 << 13, QueryTriggerInteraction.Ignore)) return false;
        var lease = PNWorldNavigation.Find(Context.Owner.GetComponent<EveEntityIdentity>().World);
        if (lease == null) return false;
        for (int i = 0; i <= 16; i++) { var point = Vector3.Lerp(origin, landing, i / 16f); if (!lease.TryProject(point, .25f, out var projected) || Mathf.Abs(projected.y - origin.y) > .15f) return false; }
        CancelWalk(); Face(delta);
        if (Context.TryCast(PNStarforgerCombatContent.Slam, direction, PNEnemyActionExecution.AuxiliaryTimeline, out var receipt, new[] { target }, () => cooldown.TryConsume(), landing) != PNEnemyCastAdmission.Started) return false;
        rest = pause; return true;
    }
    private void Face(Vector3 delta)
    {
        if (delta.sqrMagnitude < 1e-6f) return;
        Context.Move.TryFaceSkill(new Vector2(delta.x, delta.z));
        var actor = Context.Owner.GetComponent<PNVisualBody>()?.Actor;
        if (actor != null) { var camera = PNViewCamera.Current; actor.SetFacingLeft(Vector3.Dot(delta, camera != null ? camera.transform.right : Vector3.right) < 0); }
    }
    private void CancelWalk() { if (walk != null && !walk.IsTerminal) Context.CancelNavigation(walk); walk = null; }
    public override void Exit(PNEnemyBehaviorExit reason) { CancelWalk(); target = null; }
}
#endif
