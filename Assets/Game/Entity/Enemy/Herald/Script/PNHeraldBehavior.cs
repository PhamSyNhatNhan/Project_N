#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// The Herald's decisions (Docs/HeraldPlan.md H3, H4). It has no attack: every 0.25 s it looks at the living entities of its world.
//   - an ally within `castRange` (3 m) and the skill ready: it casts at the best targets PNHeraldTargets picks among the allies within `radius` (4 m, never itself);
//   - otherwise an ally within `seek`: it walks to the nearest one until within `approach` (1.5 m), whether the skill is ready or not, so it stays with the group;
//   - no ally at all: it follows the nearest player-side hostile and keeps `follow` metres from it.
// Nothing here keeps a combat number: cooldown, times, radius, target count and distances are registered data; only the scan interval is tuned here.
public sealed class PNHeraldBehavior : PNEnemyBehavior
{
    // The variant's skill (its id is data: the prefab names it, its character JSON defines it). Every Herald variant reuses this behavior.
    [SerializeField] private string skillId = "";
    public EveId SkillId => EveId.Parse(skillId);
    public override IEnumerable<EveId> SkillIds => new[] { SkillId };
    public override void ValidateConfiguration()
    { base.ValidateConfiguration(); if (!EveId.Parse(skillId).IsValid) throw new System.ArgumentException("A Herald behavior needs the skill id of its variant (skillId)."); }
    private PNEnemyNavigationRequest walk; private float scan;
    private readonly List<PNStat> everyone = new List<PNStat>();
    private readonly List<PNHeraldCandidate> candidates = new List<PNHeraldCandidate>();
    public override void Enter() { walk = null; scan = 0; }
    private JToken Payload => Context.Owner.GetData(SkillId).Action.Perform;
    public override void Tick(float dt)
    {
        scan -= dt;
        if (Context.Owner.IsExecutingEnemyAction) { CancelWalk(); return; }
        var self = Context.Stat; if (self == null || !self.IsReady || self.IsDead) { CancelWalk(); return; }
        if (scan > 0) return;
        scan = .25f;
        var world = Context.Owner.GetComponent<EveEntityIdentity>().World;
        everyone.Clear(); PNWorldEntityIndex.For(world).All(everyone);
        float radius = Payload["radius"].Value<float>(), approach = Payload["approach"].Value<float>(), castRange = Payload["castRange"].Value<float>(), seek = Payload["seek"].Value<float>(), follow = Payload["follow"].Value<float>();
        int max = Payload["targets"].Value<int>();
        var skill = Context.Abilities.GetCooldown(EveId.Parse(skillId + "_ready"));
        var near = PNHeraldSelection.Candidates(self, everyone, radius, candidates);
        bool close = false; foreach (var c in candidates) if (c.Distance <= castRange + 1e-4f) { close = true; break; }
        if (close && skill.CanConsume())
        {
            CancelWalk();
            var picked = PNHeraldTargets.Pick(candidates, radius, max); if (picked.Count == 0) return;
            var targets = new List<PNStat>(); foreach (int i in picked) targets.Add(near[i]);
            var delta = targets[0].transform.position - self.transform.position; delta.y = 0; Face(delta);
            Context.TryCast(SkillId, delta.sqrMagnitude > 1e-6f ? delta : Vector3.forward, PNEnemyActionExecution.AuxiliaryTimeline, out var receipt, targets, () => skill.TryConsume());
            return;
        }
        var ally = PNStarchanterSupport.NearestAlly(self, everyone, seek);
        if (ally != null && ally is PNEnemyStat) { Chase(ally.transform.position, approach * .8f, approach); return; }
        var player = PNStarchanterSupport.NearestHostile(self, everyone, 80f, s => PNWorldGameplay.CanTarget(Context.Owner, s));
        if (player == null) { CancelWalk(); return; }
        Chase(player.transform.position, follow, follow * 1.15f);
    }
    // Walks until within `stay` of the point, then stands; `goal` is where the navigation stops.
    private void Chase(Vector3 point, float goal, float stay)
    {
        var delta = point - Context.Stat.transform.position; delta.y = 0;
        if (delta.magnitude <= stay) { CancelWalk(); Face(delta); return; }
        if (walk == null || walk.IsTerminal) walk = Context.GoTo(point, goal); else Context.UpdateGoal(walk, point);
    }
    private void Face(Vector3 delta)
    {
        if (delta.sqrMagnitude < 1e-6f) return;
        Context.Move.TryFaceSkill(new Vector2(delta.x, delta.z));
        var actor = Context.Owner.GetComponent<PNVisualBody>()?.Actor;
        if (actor != null) { var camera = PNViewCamera.Current; actor.SetFacingLeft(Vector3.Dot(delta, camera != null ? camera.transform.right : Vector3.right) < 0); }
    }
    private void CancelWalk() { if (walk != null && !walk.IsTerminal) Context.CancelNavigation(walk); walk = null; }
    public override void Exit(PNEnemyBehaviorExit reason) { CancelWalk(); }
}
#endif
