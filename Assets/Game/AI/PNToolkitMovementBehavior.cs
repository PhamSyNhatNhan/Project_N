#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

// Optional authoring wrapper. Add explicitly; ordinary C#/FSM enemies never create these modules.
public sealed class PNToolkitMovementBehavior : PNEnemyBehavior
{
    [SerializeField] private PNMovementTaskKind kind;
    [SerializeField] private float stopDistance = .5f, minimumRange = 2, maximumRange = 3, radius = 2, timeout = 30;
    [SerializeField] private Vector3 offset;
    [SerializeField] private Vector3[] patrolPoints = Array.Empty<Vector3>();
    private PNEnemyAgentContext agent;
    private PNAgentPerception sensor;
    private PNAgentOwnedSlot<PNMovementTask> taskSlot;
    private PNMovementTask task => taskSlot?.Value;
    private PNActorHandle configuredTarget, currentTarget;
    public PNAgentTaskStatus? TaskStatus => task?.Status;
    public void ConfigureTarget(PNActorHandle target)
    { if (Context != null) throw new InvalidOperationException("Configure target before selecting behavior."); configuredTarget = target; }
    private PNMovementTaskSettings Settings() => new PNMovementTaskSettings(kind, new PNLocomotionOptions(stopDistance, slowDistance: .8f),
        minimumRange, maximumRange, timeout: timeout, radius: radius, offset: offset, patrolPoints: patrolPoints);
    public override void ValidateConfiguration() { base.ValidateConfiguration(); _ = Settings(); }
    public override void Enter()
    {
        agent = new PNEnemyAgentContext(Context, actor => Context.Stat.Targets(actor));
        taskSlot = Context.Own(new PNAgentOwnedSlot<PNMovementTask>());
        if (configuredTarget == null && kind != PNMovementTaskKind.Patrol && kind != PNMovementTaskKind.Guard && kind != PNMovementTaskKind.Search)
            sensor = Context.Own(new PNAgentPerception(agent, new PNPerceptionSettings()));
        if (sensor == null) StartTask(configuredTarget, null);
    }
    private void StartTask(PNActorHandle target, PNPerceptionRecord observed)
    {
        currentTarget = target;
        taskSlot.Replace(() => new PNMovementTask(agent, Settings(), agent.Self.Position, target, observed));
    }
    public override void Tick(float delta)
    {
        if (Context.Owner.IsExecutingEnemyAction) return;
        if (sensor != null)
        {
            sensor.Tick(delta); var seen = sensor.NearestVisible();
            if (seen == null) { task?.Cancel(); return; }
            if (currentTarget == null || !currentTarget.SameActor(seen.Actor) || task == null || task.IsTerminal) StartTask(seen.Actor, seen);
            else task.UpdateObservation(seen);
        }
        task?.Tick(delta);
    }
    public override void Exit(PNEnemyBehaviorExit reason)
    { taskSlot?.Dispose(); sensor?.Dispose(); taskSlot = null; sensor = null; agent = null; currentTarget = null; }
}
#endif
