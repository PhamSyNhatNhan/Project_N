#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

public enum PNEnemyBehaviorExit { Switch, Disabled, Death, WorldChanged, Unload, Fault }
public enum PNEnemySwitchPolicy { FinishCurrent, CancelCurrent }
public enum PNEnemyActionExecution { Instant, AuxiliaryTimeline }
public enum PNEnemySwitchStatus { Pending, Applied, Cancelled, Failed, Busy }
public sealed class PNEnemySwitchReceipt
{
    public PNEnemySwitchStatus Status { get; internal set; }
    public Exception Error { get; internal set; }
}

// Authoring components have no autonomous gameplay Update. Only their owner drives them.
public abstract class PNEnemyBehavior : MonoBehaviour
{
    [SerializeField] private string behaviorId = "default";
    public string BehaviorId => behaviorId;
    public PNEnemyBehaviorContext Context { get; private set; }
    public virtual IEnumerable<EveId> SkillIds => Array.Empty<EveId>();
    public void ConfigureId(string id)
    { if (Context != null) throw new InvalidOperationException("Configure behavior before selection."); behaviorId = id; }
    public virtual void ValidateConfiguration()
    { if (string.IsNullOrWhiteSpace(behaviorId) || behaviorId.Length > 128) throw new ArgumentException("Local behavior ID required."); }
    internal void Attach(PNEnemyBehaviorContext context)
    { if (Context != null) throw new InvalidOperationException("Behavior already attached."); Context = context; }
    internal void Detach() { Context = null; }
    public virtual void Enter() { }
    public abstract void Tick(float delta);
    public virtual void Exit(PNEnemyBehaviorExit reason) { }
    protected virtual void OnDisable() { Context?.Owner.BehaviorDisabled(this); }
}

public sealed class PNEnemyBehaviorContext
{
    public PNEnemySkill Owner { get; }
    public PNEnemyMove Move => Owner.GetComponent<PNEnemyMove>();
    public PNStat Stat => Owner.GetComponent<PNStat>();
    public PNAbilityCollection Abilities => Owner.Abilities;
    internal readonly Guid Selection;
    internal bool Retired;
    internal readonly PNEnemyResourceScope Resources = new PNEnemyResourceScope();
    public bool IsCurrent => !Retired && Owner != null && Owner.IsCurrent(this);
    internal PNEnemyBehaviorContext(PNEnemySkill owner, Guid selection) { Owner = owner; Selection = selection; }
    public PNEnemyNavigationRequest GoTo(Vector3 goal, float stopDistance)
    { Require(); return Move.GoTo(goal, stopDistance, () => IsCurrent); }
    public PNEnemyNavigationRequest GoTo(Vector3 goal, PNLocomotionOptions options)
    { Require(); return Move.GoTo(goal, options, () => IsCurrent); }
    public PNEnemyLookIntent LookAt(Vector3 point, PNEnemyLookPriority priority = PNEnemyLookPriority.Task)
    { Require(); return Own(Move.LookAt(point, priority, () => IsCurrent)); }
    public bool UpdateGoal(PNEnemyNavigationRequest request, Vector3 goal) { Require(); return Move.UpdateGoal(request, goal); }
    public void CancelNavigation(PNEnemyNavigationRequest request) { Require(); Move.CancelNavigation(request); }
    public bool TryCast(EveId id, Vector3 direction, PNEnemyActionExecution execution, IEnumerable<PNStat> targets = null, Func<bool> pay = null)
    { Require(); return Owner.TryCast(this, id, direction, execution, targets, pay); }
    public PNEnemyCastAdmission TryCast(EveId id, Vector3 direction, PNEnemyActionExecution execution, out PNEnemyCastReceipt receipt,
        IEnumerable<PNStat> targets = null, Func<bool> pay = null, Vector3? position = null)
    { Require(); return Owner.TryCastWithReceipt(this, id, direction, execution, targets, pay, out receipt, position); }
    public PNEnemySwitchReceipt Switch(string id, PNEnemySwitchPolicy policy) { Require(); return Owner.Switch(id, policy); }
    public T Own<T>(T resource) where T : class, IDisposable { Require(); Resources.Add(resource); return resource; }
    private void Require() { if (!IsCurrent || !Owner.CanExecuteBehavior) throw new InvalidOperationException("Inactive or suspended enemy behavior context."); }
}

internal sealed class PNEnemyResourceScope : IDisposable
{
    private readonly List<IDisposable> resources = new List<IDisposable>();
    internal bool IsEmpty => resources.Count == 0;
    internal void Add(IDisposable resource)
    {
        if (resource == null) throw new ArgumentNullException(nameof(resource));
        if (resources.Contains(resource)) throw new InvalidOperationException("Resource already owned."); resources.Add(resource);
    }
    public void Dispose()
    {
        var errors = new List<Exception>();
        for (int i = resources.Count - 1; i >= 0; i--)
            try
            {
                if (resources[i] is IPNSkillDeferredCleanup deferred && deferred.CleanupPending) continue;
                resources[i].Dispose(); resources.RemoveAt(i);
            }
            catch (Exception error) { errors.Add(error); }
        if (errors.Count > 0) throw new AggregateException("Enemy resources remain owned for retry.", errors);
    }
}
#endif
