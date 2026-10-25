#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

// A small authoring example: decisions in C#, execution/tuning in selected registered data.
public class PNEnemyMeleeBehavior : PNEnemyStateMachineBehavior
{
    [SerializeField] private float aggroRange = 20, stopDistance = 1.5f, attackRange = 1.6f, resumeDistance = 1.9f;
    [SerializeField] private float windup = .2f, recovery = .25f, retryDelay = .5f;
    [SerializeField] private LayerMask sightBlockers = ~(1 << 13);
    private PNStat target;
    private PNEnemyNavigationRequest walk;
    private float timer;
    private readonly RaycastHit[] sightHits = new RaycastHit[32];
    protected virtual EveId AttackSkillId => PNEnemyGameplayContent.Strike;
    protected virtual PNEnemyActionExecution Execution => PNEnemyActionExecution.Instant;
    public override IEnumerable<EveId> SkillIds => new[] { AttackSkillId };
    public PNStat Target => target;
    public override void ValidateConfiguration()
    {
        base.ValidateConfiguration();
        foreach (var value in new[] { aggroRange, stopDistance, attackRange, resumeDistance, windup, recovery, retryDelay }) PNNavigationSettings.Positive(value, "Enemy behavior tuning");
        if (resumeDistance <= attackRange || attackRange < stopDistance || aggroRange < resumeDistance) throw new ArgumentException("Require aggro >= resume > attackRange >= stop.");
    }
    public override void Enter()
    {
        ClearStates(); target = null; walk = null; timer = 0;
        AddState("Acquire", () => timer = 0, Acquire);
        AddState("Chase", null, Chase, CancelWalk);
        AddState("Windup", () => timer = windup, Windup);
        AddState("Execute", null, delta => { if (!Context.Owner.IsExecutingEnemyAction) ChangeState("Recover"); });
        AddState("Recover", () => timer = recovery, delta => { timer -= delta; if (timer <= 0) ChangeState(ValidTarget() ? "Chase" : "Acquire"); });
        AddState("Backoff", () => timer = retryDelay, delta => { timer -= delta; if (timer <= 0) ChangeState("Acquire"); });
        ChangeState("Acquire");
    }
    private void Acquire(float delta)
    {
        timer -= delta; if (timer > 0) return; timer = retryDelay;
        float range = aggroRange * aggroRange; target = null; var best = default(PNTargetScore);
        var world = GetComponentInParent<EveEntityIdentity>()?.World;
        if (world == null) return;
        // Aggro rule (PNTargetRule.Aggro): within aggroRange, the smallest distance minus the candidate's aggro stat; ties go to the lower entity ID.
        foreach (var candidate in world.Session.Entities.Snapshot(world))
        {
            var stat = candidate.GetComponent<PNStat>();
            if (!CanTarget(stat)) continue;
            float distance = (stat.transform.position - Context.Owner.transform.position).sqrMagnitude;
            if (distance >= range) continue;
            var score = new PNTargetScore { Distance = Mathf.Sqrt(distance), Aggro = PNTargetSelector.AggroOf(stat), EntityId = stat.EntityId.Value };
            if (target == null || PNTargetRanking.Better(score, best, PNTargetRule.Aggro)) { best = score; target = stat; }
        }
        if (target != null) ChangeState("Chase");
    }
    private bool CanTarget(PNStat value) => value != null && value != Context.Stat && value.IsReady && !value.IsDead &&
        Context.Stat.Targets(value) && PNWorldGameplay.CanTarget(Context.Owner, value);
    private bool ValidTarget() => CanTarget(target) && (target.transform.position - Context.Owner.transform.position).sqrMagnitude <= aggroRange * aggroRange;
    private float Distance() => new Vector2(target.transform.position.x - Context.Owner.transform.position.x, target.transform.position.z - Context.Owner.transform.position.z).magnitude;
    private bool HasSight()
    {
        var start = Context.Owner.transform.position + Vector3.up * .6f; var end = target.transform.position + Vector3.up * .6f; var d = end - start;
        int count = Context.Owner.gameObject.scene.GetPhysicsScene().Raycast(start, d.normalized, sightHits, d.magnitude, sightBlockers, QueryTriggerInteraction.Ignore);
        if (count == sightHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            var hit = sightHits[i].collider; if (hit == null || hit.transform.IsChildOf(Context.Owner.transform)) continue;
            if (hit.GetComponentInParent<PNStat>() != null) continue;
            return false;
        }
        return true;
    }
    private PNAbilityCooldown Cooldown => Context.Abilities.GetCooldown(PNEnemyGameplayContent.Recovery);
    private void Chase(float delta)
    {
        if (!ValidTarget()) { ChangeState("Acquire"); return; }
        if (Distance() <= attackRange && HasSight() && Cooldown.CanConsume()) { ChangeState("Windup"); return; }
        if (walk != null && walk.IsTerminal && walk.Status != PNEnemyNavigationStatus.Arrived) { ChangeState("Backoff"); return; }
        if (walk == null || walk.IsTerminal && Distance() > resumeDistance)
            walk = Context.GoTo(target.transform.position, stopDistance);
        else if (!walk.IsTerminal) Context.UpdateGoal(walk, target.transform.position);
    }
    private void Windup(float delta)
    {
        if (!ValidTarget() || Distance() > attackRange || !HasSight()) { ChangeState("Chase"); return; }
        timer -= delta; if (timer > 0) return;
        var direction = target.transform.position - Context.Owner.transform.position; direction.y = 0;
        if (direction.sqrMagnitude < .0001f) direction = Vector3.right;
        Context.Move.TryFaceSkill(new Vector2(direction.x, direction.z));
        if (Context.TryCast(AttackSkillId, direction, Execution, new[] { target }, () => Cooldown.TryConsume())) ChangeState("Execute");
        else ChangeState("Backoff");
    }
    private void CancelWalk()
    { if (walk != null && !walk.IsTerminal) Context.Move.CancelNavigation(walk); walk = null; }
    public override void Exit(PNEnemyBehaviorExit reason)
    { try { CancelWalk(); base.Exit(reason); } finally { target = null; walk = null; } }
}
#endif
