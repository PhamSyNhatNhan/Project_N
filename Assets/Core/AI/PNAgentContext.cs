#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;

// A handle pins a load and world membership, not merely a reusable EntityId.
public sealed class PNActorHandle
{
    private readonly EveEntityLoader loader;
    internal readonly EveEntityLoadContext Load;
    private readonly EveWorldContext world;
    private readonly ulong revision;
    public EveEntityId Id => Load.Identity.Id;
    public EveWorldContext World => world;
    public float FootprintRadius { get { if (!TryResolve(out var actor)) throw new InvalidOperationException("Actor retired."); return PNAgentGeometry.FootprintRadius(actor.GetComponent<CapsuleCollider>()); } }
    public bool SameActor(PNActorHandle other) => other != null && ReferenceEquals(Load, other.Load) && ReferenceEquals(world, other.world) && revision == other.revision;
    private PNActorHandle(EveEntityLoader loader)
    { this.loader = loader; Load = loader.LoadContext; world = Load.Identity.World; revision = Load.Identity.MembershipRevision; }
    public static PNActorHandle Capture(PNStat actor)
    {
        var loader = actor != null ? actor.GetComponent<EveEntityLoader>() : null;
        if (loader?.LoadContext?.CanUse != true || !loader.IsReady || actor.IsDead) throw new InvalidOperationException("Capture a published live actor.");
        return new PNActorHandle(loader);
    }
    public bool TryResolve(out PNStat actor)
    {
        actor = null;
        if (loader == null || !ReferenceEquals(loader.LoadContext, Load) || !Load.CanUse || !loader.IsReady ||
            !ReferenceEquals(Load.Identity.World, world) || Load.Identity.MembershipRevision != revision ||
            world == null || world.State == EveWorldState.Unloading || world.State == EveWorldState.Disposed || world.State == EveWorldState.Failed ||
            !Load.Session.Entities.TryGet(Load.Identity.Id, out var current) || !ReferenceEquals(current, loader)) return false;
        var stat = loader.GetComponent<PNStat>();
        if (stat == null || !stat.IsReady || stat.IsDead) return false;
        actor = stat; return true;
    }
    public Vector3 Position { get { if (!TryResolve(out var actor)) throw new InvalidOperationException("Actor handle retired."); return Pose(actor); } }
    public static Vector3 Pose(PNStat actor) => actor.TryGetComponent<Rigidbody>(out var body) ? body.position : actor.transform.position;
}

public interface IPNAgentContext
{
    PNActorHandle Self { get; }
    bool IsCurrent { get; }
    bool CanExecute { get; }
    Vector2 Heading { get; }
    bool CanTarget(PNStat target);
    PNEnemyNavigationRequest GoTo(Vector3 goal, PNLocomotionOptions options);
    bool UpdateGoal(PNEnemyNavigationRequest request, Vector3 goal);
    void Cancel(PNEnemyNavigationRequest request);
    PNAgentPointQuery FindPoint(PNPointQuerySettings settings);
}

// Adapter only; the toolkit contracts themselves do not require an enemy stat/type.
public sealed class PNEnemyAgentContext : IPNAgentContext
{
    private readonly PNEnemyBehaviorContext context;
    private readonly Predicate<PNStat> relation;
    private PNAgentPointQuery activeQuery;
    public PNActorHandle Self { get; }
    public bool IsCurrent => context.IsCurrent && Self.TryResolve(out _);
    public bool CanExecute => IsCurrent && context.Owner.CanExecuteBehavior && context.Move.MovementTimeScale > 0;
    public Vector2 Heading => context.Move.LookDirection;
    public PNEnemyAgentContext(PNEnemyBehaviorContext context, Predicate<PNStat> relation)
    { this.context = context ?? throw new ArgumentNullException(nameof(context)); this.relation = relation ?? throw new ArgumentNullException(nameof(relation)); Self = PNActorHandle.Capture(context.Stat); }
    public bool CanTarget(PNStat target) => IsCurrent && target != null && target != context.Stat && target.IsReady && !target.IsDead && PNTargetSelector.IsAggroEligible(target) &&
        PNWorldGameplay.CanTarget(context.Owner, target) && relation(target);
    public PNEnemyNavigationRequest GoTo(Vector3 goal, PNLocomotionOptions options) { Require(); return context.GoTo(goal, options); }
    public bool UpdateGoal(PNEnemyNavigationRequest request, Vector3 goal) { Require(); return context.UpdateGoal(request, goal); }
    public void Cancel(PNEnemyNavigationRequest request) { if (request != null) context.Move.CancelNavigation(request); }
    public PNAgentPointQuery FindPoint(PNPointQuerySettings settings)
    {
        Require(); if (activeQuery != null && !activeQuery.IsTerminal) throw new InvalidOperationException("One pending point query per agent adapter.");
        activeQuery = new PNAgentPointQuery(this, settings); return activeQuery;
    }
    private void Require() { if (!CanExecute) throw new InvalidOperationException("Agent cannot execute in this selection/world."); }
}
#endif
