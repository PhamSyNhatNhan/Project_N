#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// The Starchanter's decisions (Docs/StarchanterSkillsPlan.md). Every 0.25 s it looks at the living entities of its world. With an ally in sight (the registered boost `seek`) it supports:
//   1. the heal, when it is ready and someone (an ally or the Starchanter itself) is hurt: the one with the lowest absolute health; the cast teleports next to that ally;
//   2. the boost, when it is ready: it walks to the nearest ally (stopping inside the registered `radius`), at most `timeout` seconds, then casts. The boost cooldown is spent when the approach begins.
// With no ally left in sight it fights, close up: the heal on itself when hurt, then the strike when a hostile is within `attackRange` and the strike is ready (after a short windup), then the boost
// on itself, then it walks up to the nearest hostile it can see. Otherwise it stands still. Which entities are allies is PNStarchanterSupport (the same layer).
// Nothing here keeps a combat number of its own: damage, cooldowns, times and percentages are registered data; only the scan interval, the windup and the strike reach are tuned here.
public sealed class PNStarchanterBehavior : PNEnemyBehavior
{
    public override IEnumerable<EveId> SkillIds => new[] { PNStarchanterCombatContent.Heal, PNStarchanterCombatContent.Boost, PNStarchanterCombatContent.Strike };
    [SerializeField, Min(.1f)] private float attackRange = 1.2f, strikeWindup = .25f;
    private PNEnemyNavigationRequest walk; private float scan, approach, windupLeft; private PNStat ally, strikeTarget; private bool approaching;
    private readonly RaycastHit[] hits = new RaycastHit[8];
    private PNEnemyTargeting targeting;
    private readonly List<PNStat> everyone = new List<PNStat>();
    // The shared targeting supplies the sight (cells in a grid world, the `Wall` ray elsewhere); the range here is the registered boost `seek`, asked per scan, so the object's own range is never used.
    public override void Enter() { walk = null; scan = 0; approach = 0; windupLeft = 0; ally = null; strikeTarget = null; approaching = false; targeting = new PNEnemyTargeting(Context, 8f); }
    // Every entity of the world from the world's chunk index (the heal picks among all allies of the world, so the list is not cut by range).
    private List<PNStat> Entities()
    {
        var world = Context.Owner.GetComponent<EveEntityIdentity>().World;
        everyone.Clear(); PNWorldEntityIndex.For(world).All(everyone);
        return everyone;
    }
    private JToken Perform(EveId id) => Context.Owner.GetData(id).Action.Perform;
    public override void Tick(float dt)
    {
        scan -= dt;
        if (Context.Owner.IsExecutingEnemyAction) { CancelWalk(); if(windupLeft>0)Context.Owner.GetComponent<PNStarchanterPresentation>()?.CancelStrike(); windupLeft = 0; return; }
        var self = Context.Stat; if (self == null || !self.IsReady || self.IsDead) { CancelWalk(); return; }
        var heal = Context.Abilities.GetCooldown(EveId.Parse(PNStarchanterCombatContent.Heal + "_ready"));
        var boost = Context.Abilities.GetCooldown(EveId.Parse(PNStarchanterCombatContent.Boost + "_ready"));
        var strike = Context.Abilities.GetCooldown(EveId.Parse(PNStarchanterCombatContent.Strike + "_ready"));
        if (approaching) { ContinueApproach(dt); return; }
        if (windupLeft > 0) { WindUp(dt, strike); return; }
        if (scan > 0) return;
        scan = .25f;
        var all = Entities();
        if (heal.CanConsume())
        {
            var target = PNStarchanterSupport.PickHealTarget(self, all);
            if (target != null) { CastHeal(target, heal); return; }
        }
        float seek = Perform(PNStarchanterCombatContent.Boost)["seek"].Value<float>(), radius = Radius();
        var near = PNStarchanterSupport.NearestAlly(self, all, seek);
        if (near != null)                                                  // support
        {
            if (!boost.CanConsume()) return;
            if (PNStarchanterSupport.Flat(near.transform.position, self.transform.position) <= radius) { CastBoost(boost, near); return; }
            if (!boost.TryConsume()) return;                               // spent now: the approach is part of the skill
            ally = near; approach = Perform(PNStarchanterCombatContent.Boost)["timeout"].Value<float>(); approaching = true;
            walk = Context.GoTo(near.transform.position, radius * .8f);
            return;
        }
        // no ally in sight: fight
        var hostile = PNStarchanterSupport.NearestHostile(self, all, seek, s => PNWorldGameplay.CanTarget(Context.Owner, s) && Sight(s));
        if (hostile != null && strike.CanConsume() && PNStarchanterSupport.Flat(hostile.transform.position, self.transform.position) <= attackRange)
        { CancelWalk(); strikeTarget = hostile; windupLeft = strikeWindup; FaceTo(hostile); Context.Owner.GetComponent<PNStarchanterPresentation>()?.BeginStrike(strikeWindup,hostile.transform.position-self.transform.position); return; }
        if (boost.CanConsume()) { CastBoost(boost, null); return; }
        if (hostile == null) { CancelWalk(); return; }
        if (PNStarchanterSupport.Flat(hostile.transform.position, self.transform.position) > attackRange * .8f)
        { if (walk == null || walk.IsTerminal) walk = Context.GoTo(hostile.transform.position, attackRange * .7f); else Context.UpdateGoal(walk, hostile.transform.position); }
        else CancelWalk();
    }
    private float Radius() => Perform(PNStarchanterCombatContent.Boost)["radius"].Value<float>();
    private bool Sight(PNStat s) => targeting.Sight(s);
    private void WindUp(float dt, PNAbilityCooldown strike)
    {
        windupLeft -= dt;
        if (!PNStarchanterSupport.IsHostileTo(Context.Stat, strikeTarget) || PNStarchanterSupport.Flat(strikeTarget.transform.position, Context.Stat.transform.position) > attackRange * 1.3f) { windupLeft = 0; strikeTarget = null; Context.Owner.GetComponent<PNStarchanterPresentation>()?.CancelStrike(); return; }
        FaceTo(strikeTarget);
        var presentation=Context.Owner.GetComponent<PNStarchanterPresentation>();
        presentation?.FaceStrike(strikeTarget.transform.position-Context.Stat.transform.position);
        if (windupLeft > 0) return;
        var contact = strikeTarget.transform.position;
        var delta = contact - Context.Stat.transform.position; delta.y = 0;
        if(Context.TryCast(PNStarchanterCombatContent.Strike, delta.sqrMagnitude > 1e-6f ? delta : Vector3.forward, PNEnemyActionExecution.Instant, out var receipt, new[] { strikeTarget }, () => strike.TryConsume())==PNEnemyCastAdmission.Started)presentation?.ReleaseStrike(contact);
        else presentation?.CancelStrike();
        strikeTarget = null; windupLeft = 0;
    }
    private void ContinueApproach(float dt)
    {
        approach -= dt;
        var self = Context.Stat;
        if (!PNStarchanterSupport.IsAlly(self, ally) || approach <= 0) { StopApproach(); return; }       // the ally died or the time ran out: the cooldown stays spent
        if (PNStarchanterSupport.Flat(ally.transform.position, self.transform.position) <= Radius()) { var a = ally; StopApproach(); CastBoost(null, a); return; }
        if (walk == null || walk.IsTerminal) walk = Context.GoTo(ally.transform.position, Radius() * .8f); else Context.UpdateGoal(walk, ally.transform.position);
    }
    private void StopApproach() { CancelWalk(); approaching = false; ally = null; }
    private void Face(Vector3 delta)
    {
        Context.Move.TryFaceSkill(new Vector2(delta.x, delta.z));
        var actor = Context.Owner.GetComponent<PNVisualBody>()?.Actor;
        if (actor != null) { var camera = PNViewCamera.Current; actor.SetFacingLeft(Vector3.Dot(delta, camera != null ? camera.transform.right : Vector3.right) < 0); }
    }
    private void FaceTo(PNStat target) { var d = target.transform.position - Context.Stat.transform.position; d.y = 0; Face(d); }
    private void CastHeal(PNStat target, PNAbilityCooldown cooldown)
    {
        CancelWalk(); var self = Context.Stat; var delta = target.transform.position - self.transform.position; delta.y = 0; Face(delta);
        var landing = ReferenceEquals(target, self) ? self.transform.position : Landing(target);
        Context.TryCast(PNStarchanterCombatContent.Heal, delta.sqrMagnitude > 1e-6f ? delta : Vector3.forward, PNEnemyActionExecution.AuxiliaryTimeline, out var receipt, new[] { target }, () => cooldown.TryConsume(), landing);
    }
    private void CastBoost(PNAbilityCooldown cooldown, PNStat near)
    {
        CancelWalk(); var self = Context.Stat; var delta = near != null ? near.transform.position - self.transform.position : Vector3.forward; delta.y = 0; if (near != null) Face(delta);
        Context.TryCast(PNStarchanterCombatContent.Boost, delta.sqrMagnitude > 1e-6f ? delta : Vector3.forward, PNEnemyActionExecution.AuxiliaryTimeline, out var receipt, near != null ? new[] { near } : new PNStat[0],
            () => cooldown == null || cooldown.TryConsume());
        approaching = false; ally = null;
    }
    // Next to the ally on the side the Starchanter comes from; a wall in between shortens the landing so it never ends up inside it.
    private Vector3 Landing(PNStat target)
    {
        float distance = Perform(PNStarchanterCombatContent.Heal)["landingDistance"].Value<float>();
        var from = Context.Stat.transform.position; var origin = target.transform.position; var point = PNStarchanterSupport.Landing(origin, from, distance);
        var ray = point - origin; float length = ray.magnitude; if (length < 1e-4f) return origin;
        int n = Context.Owner.gameObject.scene.GetPhysicsScene().Raycast(origin + Vector3.up * .2f, ray / length, hits, length, LayerMask.GetMask("Wall"), QueryTriggerInteraction.Ignore);
        if (n == 0) return point;
        float nearest = length; for (int i = 0; i < n; i++) nearest = Mathf.Min(nearest, hits[i].distance);
        return origin + ray / length * Mathf.Max(0f, nearest - .1f);
    }
    private void CancelWalk() { if (walk != null && !walk.IsTerminal) Context.CancelNavigation(walk); walk = null; }
    public override void Exit(PNEnemyBehaviorExit reason) { CancelWalk(); Context.Owner.GetComponent<PNStarchanterPresentation>()?.CancelStrike(); approaching = false; ally = null; strikeTarget = null; windupLeft = 0; }
}
#endif
