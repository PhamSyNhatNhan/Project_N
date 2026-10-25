#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// Ally/leader and group affinity are explicitly supplied by Game, never inferred from player/enemy CLR type.
public sealed class PNToolkitSupportBehavior : PNEnemyBehavior
{
    private PNActorHandle ally;
    private PNAgentGroupService groups;
    private string groupInstance;
    private PNAgentGroupMember member;
    private PNEnemyAgentContext agent;
    private PNAgentPerception sensor;
    private PNUtilitySelector utility;
    private PNAgentOwnedSlot<PNMovementTask> movementSlot;
    private PNMovementTask movement => movementSlot?.Value;
    private PNPerceptionRecord threat;
    private PNActorHandle movingTarget;
    private string previousDecision;
    private readonly RaycastHit[] sight = new RaycastHit[32];
    public string Decision => utility?.CurrentId;
    public override IEnumerable<EveId> SkillIds => new[] { PNEnemyGameplayContent.Strike, PNAgentToolkitContent.Support };
    public void Configure(PNActorHandle ally, PNAgentGroupService groups = null, string groupInstance = null)
    {
        if (Context != null) throw new InvalidOperationException("Configure support before selection.");
        this.ally = ally; this.groups = groups; this.groupInstance = groupInstance;
    }
    public override void Enter()
    {
        agent = new PNEnemyAgentContext(Context, actor => Context.Stat.Targets(actor));
        movementSlot = Context.Own(new PNAgentOwnedSlot<PNMovementTask>());
        sensor = Context.Own(new PNAgentPerception(agent, new PNPerceptionSettings()));
        utility = new PNUtilitySelector(new[] {
            new PNUtilityCandidate("support", CanSupport, () => .95f, priority: 3),
            new PNUtilityCandidate("attack", () => threat != null && threat.Actor.TryResolve(out _) && Context.Abilities.GetCooldown(PNEnemyGameplayContent.Recovery).CanConsume(), () => .7f, priority: 2),
            new PNUtilityCandidate("follow", () => ally != null && ally.TryResolve(out _), () => .4f, priority: 1)
        });
        if (groups != null)
            member = Context.Own(groups.Join(agent, EveId.Parse("project_n:support_example"), groupInstance,
                (a, b) => a.Layer == b.Layer));
    }
    private bool CanSupport() => ally != null && ally.TryResolve(out var actor) && actor.Layer == Context.Stat.Layer && actor.HasHealth &&
        actor.GetCurrent(PNContentIds.Health) < actor.Evaluate(PNContentIds.Health) * .8 && Context.Abilities.GetCooldown(PNAgentToolkitContent.SupportReady).CanConsume();
    public override void Tick(float delta)
    {
        sensor.Tick(delta); threat = sensor.NearestVisible();
        member?.Drain(_ => { });
        if (Context.Owner.IsExecutingEnemyAction) return;
        string decision = utility.Tick(delta);
        PNActorHandle target = decision == "attack" ? threat?.Actor : ally;
        if (decision != previousDecision || target != null && (movingTarget == null || !target.SameActor(movingTarget)))
        { movementSlot.Replace(() => null); previousDecision = decision; movingTarget = target; }
        if (decision == null || target == null || !target.TryResolve(out var actor)) { movement?.Cancel(); return; }
        float range = decision == "support" ? 5 : decision == "attack" ? 1.6f : 2;
        float distance = PNAgentGeometry.CenterDistance(agent.Self.Position, target.Position);
        bool clear = decision == "follow" || PNAgentSpatial.HasSight(Context.Stat, agent.Self.Position + Vector3.up * .6f, actor, target.Position + Vector3.up * .6f, sight);
        if (distance <= range && decision != "follow" && clear)
        {
            movement?.Cancel();
            var id = decision == "support" ? PNAgentToolkitContent.Support : PNEnemyGameplayContent.Strike;
            var cooldown = Context.Abilities.GetCooldown(decision == "support" ? PNAgentToolkitContent.SupportReady : PNEnemyGameplayContent.Recovery);
            using (var reservation = member?.Reserve(decision + "/" + target.Id.Value, target, .5f))
            {
                if (member != null && reservation == null) return;
                var direction = target.Position - agent.Self.Position; if (PNAgentGeometry.Planar(direction).sqrMagnitude < .0001f) direction = Vector3.right;
                Context.TryCast(id, direction, PNEnemyActionExecution.Instant, new[] { actor }, () => cooldown.TryConsume());
            }
            return;
        }
        if (movement == null || movement.IsTerminal)
            movementSlot.Replace(() => new PNMovementTask(agent, new PNMovementTaskSettings(clear ? PNMovementTaskKind.Approach : PNMovementTaskKind.CombatPosition,
                new PNLocomotionOptions(range * .8f), minimumRange: range * .4f, maximumRange: range * .8f, timeout: 15), agent.Self.Position, target));
        movement.Tick(delta);
    }
    public override void Exit(PNEnemyBehaviorExit reason)
    { movementSlot?.Dispose(); sensor?.Dispose(); member?.Dispose(); movementSlot = null; sensor = null; member = null; utility = null; previousDecision = null; movingTarget = null; }
}
#endif
