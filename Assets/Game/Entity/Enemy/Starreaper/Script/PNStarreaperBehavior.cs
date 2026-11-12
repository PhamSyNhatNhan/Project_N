#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// The Starreaper's decisions (Docs/StarreaperSkillsPlan.md). It looks for the hostile it sees (the shared aggro rule, walls block the sight), then every tick:
//   1. Tàn Ảnh when the target is within its registered `range` (1.5 m), it is ready and there is room to step back,
//   2. otherwise Mưa Tinh when the target is within its `range` (2.5 m) and it is ready,
//   3. otherwise Xuyên Tinh when the target is within its `range` (3 m) and it is ready,
//   4. otherwise it walks toward the target until it is within `standDistance` and then stands (it never kites; the afterimage is its answer to being rushed).
// Nothing here keeps a combat number: damage, cooldowns, times and ranges are registered data; only the sight, the stand distance and the pause between casts are tuned here.
public sealed class PNStarreaperBehavior : PNEnemyBehavior
{
    public override IEnumerable<EveId> SkillIds => new[] { PNStarreaperCombatContent.Afterimage, PNStarreaperCombatContent.Rain, PNStarreaperCombatContent.Pierce };
    [SerializeField, Min(.1f)] private float standDistance = 2.5f, vision = 7f, pause = .35f;
    private PNStat target; private PNEnemyNavigationRequest walk; private float rest, scan;
    private PNEnemyTargeting targeting;
    private readonly RaycastHit[] hits = new RaycastHit[8];
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
        var after = Cooldown(PNStarreaperCombatContent.Afterimage); var rain = Cooldown(PNStarreaperCombatContent.Rain); var pierce = Cooldown(PNStarreaperCombatContent.Pierce);
        if (after.CanConsume() && distance <= Range(PNStarreaperCombatContent.Afterimage) && CastAfterimage(delta, after)) return true;
        if (rain.CanConsume() && distance <= Range(PNStarreaperCombatContent.Rain) && Cast(PNStarreaperCombatContent.Rain, delta, target.transform.position, rain)) return true;
        if (pierce.CanConsume() && distance <= Range(PNStarreaperCombatContent.Pierce) && Cast(PNStarreaperCombatContent.Pierce, delta, Context.Owner.transform.position + Vector3.up * .2f, pierce)) return true;
        return false;
    }
    private bool Cast(EveId id, Vector3 delta, Vector3 position, PNAbilityCooldown cooldown)
    {
        CancelWalk(); Face(delta);
        if (Context.TryCast(id, delta.sqrMagnitude > 1e-5f ? delta : Vector3.forward, PNEnemyActionExecution.AuxiliaryTimeline, out var receipt, new[] { target }, () => cooldown.TryConsume(), position) != PNEnemyCastAdmission.Started) return false;
        rest = pause; return true;
    }
    // The step back goes straight away from the target; it is cast only when the whole step is clear of walls and ends on floor (the Action checks the floor again and does nothing if it fails).
    private bool CastAfterimage(Vector3 delta, PNAbilityCooldown cooldown)
    {
        var motion = Perform(PNStarreaperCombatContent.Afterimage)["motion"]; float step = motion["distance"].Value<float>();
        var away = delta.sqrMagnitude > 1e-6f ? -delta.normalized : -Vector3.forward;
        var origin = Context.Owner.transform.position; var physics = Context.Owner.gameObject.scene.GetPhysicsScene();
        int n = physics.Raycast(origin + Vector3.up * .2f, away, hits, step + .3f, LayerMask.GetMask("Wall"), QueryTriggerInteraction.Ignore);
        float free = step + .3f; for (int i = 0; i < n; i++) free = Mathf.Min(free, hits[i].distance);
        if (!PNStarreaperPlanner.HasRoom(free, step, .15f)) return false;
        var end = origin + away * step;
        if (!physics.Raycast(end + Vector3.up * .3f, Vector3.down, out RaycastHit ground, .5f, 1 << 13, QueryTriggerInteraction.Ignore)) return false;
        CancelWalk(); Face(delta);
        if (Context.TryCast(PNStarreaperCombatContent.Afterimage, away, PNEnemyActionExecution.AuxiliaryTimeline, out var receipt, new[] { target }, () => cooldown.TryConsume(), end + Vector3.up * .2f) != PNEnemyCastAdmission.Started) return false;
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
