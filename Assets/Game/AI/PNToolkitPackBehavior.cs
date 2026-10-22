#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// Same-kind encounter example. Explicit enrollment, observed focus signals and finite attack reservations.
public sealed class PNToolkitPackBehavior : PNEnemyBehavior
{
    private PNAgentGroupService groups;
    private EveId kind;
    private string instance;
    private PNAgentGroupMember member;
    private PNEnemyAgentContext agent;
    private PNAgentPerception sensor;
    private PNAgentOwnedSlot<PNMovementTask> movement;
    private PNAgentOwnedSlot<PNGroupReservation> reservation;
    private PNActorHandle currentTarget;
    private Vector3 lastKnown;
    private float messageAge = 100, sendAt;
    private bool followingObservation;
    public override IEnumerable<EveId> SkillIds => new[] { PNEnemyGameplayContent.Strike };
    public void Configure(PNAgentGroupService groups, EveId kind, string instance)
    {
        if (Context != null) throw new InvalidOperationException("Configure pack before selection.");
        this.groups = groups ?? throw new ArgumentNullException(nameof(groups)); this.kind = kind; this.instance = instance;
    }
    public override void Enter()
    {
        if (groups == null) throw new InvalidOperationException("Pack requires explicit group service and encounter identity.");
        agent = new PNEnemyAgentContext(Context, actor => actor.CombatTeam != Context.Stat.CombatTeam);
        sensor = Context.Own(new PNAgentPerception(agent, new PNPerceptionSettings()));
        movement = Context.Own(new PNAgentOwnedSlot<PNMovementTask>()); reservation = Context.Own(new PNAgentOwnedSlot<PNGroupReservation>());
        member = Context.Own(groups.Join(agent, kind, instance, (a, b) => a.CombatTeam == b.CombatTeam &&
            a.GetComponent<EveEntityIdentity>().CharacterId == b.GetComponent<EveEntityIdentity>().CharacterId));
        currentTarget = null; messageAge = 100; sendAt = 0;
    }
    public override void Tick(float delta)
    {
        sensor.Tick(delta); messageAge += delta; sendAt -= delta;
        member.Drain(signal =>
        {
            if (signal.Kind != PNAgentSignalKind.FocusTarget || member.Leader == null || !member.Leader.SameActor(signal.Sender) || signal.Target == null) return;
            if (currentTarget == null || !currentTarget.SameActor(signal.Target)) movement.Replace(() => null);
            currentTarget = signal.Target; lastKnown = signal.ObservedPosition; messageAge = 0;
        });
        var seen = sensor.NearestVisible();
        if (seen != null)
        {
            if (currentTarget == null || !currentTarget.SameActor(seen.Actor) || !followingObservation) movement.Replace(() => null);
            currentTarget = seen.Actor; lastKnown = seen.LastPosition; messageAge = 0; followingObservation = true;
            if (member.Leader != null && member.Leader.SameActor(agent.Self) && sendAt <= 0)
            { member.Send(PNAgentSignalKind.FocusTarget, lastKnown, currentTarget); sendAt = .5f; }
        }
        else if (followingObservation) { followingObservation = false; movement.Replace(() => null); }
        if (Context.Owner.IsExecutingEnemyAction) return;
        if (currentTarget == null || !currentTarget.TryResolve(out var target) || messageAge > 1)
        { movement.Value?.Cancel(); return; }
        var cooldown = Context.Abilities.GetCooldown(PNEnemyGameplayContent.Recovery);
        if (seen != null && PNAgentGeometry.CenterDistance(agent.Self.Position, seen.LastPosition) <= 1.6f && cooldown.CanConsume())
        {
            movement.Value?.Cancel();
            if (reservation.Value?.IsCurrent != true) reservation.Replace(() => member.Reserve("attack/" + currentTarget.Id.Value, currentTarget, .5f));
            if (reservation.Value == null) return;
            var direction = seen.LastPosition - agent.Self.Position; if (PNAgentGeometry.Planar(direction).sqrMagnitude < .0001f) direction = Vector3.right;
            Context.Move.TryFaceSkill(PNAgentGeometry.Planar(direction));
            Context.TryCast(PNEnemyGameplayContent.Strike, direction, PNEnemyActionExecution.Instant, new[] { target }, () => cooldown.TryConsume()); return;
        }
        if (movement.Value == null || movement.Value.IsTerminal)
        {
            var snapshot = lastKnown;
            movement.Replace(() => new PNMovementTask(agent,
                new PNMovementTaskSettings(seen != null ? PNMovementTaskKind.Approach : PNMovementTaskKind.Search,
                    new PNLocomotionOptions(seen != null ? 1.5f : .3f), timeout: 5, loop: seen != null), snapshot,
                seen != null ? currentTarget : null, seen));
        }
        else if (seen != null) movement.Value.UpdateObservation(seen);
        movement.Value.Tick(delta);
    }
    public override void Exit(PNEnemyBehaviorExit reason)
    {
        movement?.Dispose(); reservation?.Dispose(); sensor?.Dispose(); member?.Dispose();
        movement = null; reservation = null; sensor = null; member = null; currentTarget = null;
    }
}
#endif
