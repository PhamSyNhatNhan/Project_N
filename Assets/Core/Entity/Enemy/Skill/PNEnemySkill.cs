// EN: Extension point for skill behavior shared by enemies.
// VI: Điểm kế thừa cho hành vi skill chung của các nhân vật địch.
public class PNEnemySkill : PNSkill
{
#if UNITY_5_3_OR_NEWER
    [UnityEngine.SerializeField] private string initialBehaviorId = "default";
    private readonly System.Collections.Generic.Dictionary<string, PNEnemyBehavior> behaviors = new System.Collections.Generic.Dictionary<string, PNEnemyBehavior>(System.StringComparer.Ordinal);
    private EveEngine.EveEntityLoadContext enemyContext;
    private EveEngine.EveWorldContext selectedWorld;
    private PNEnemyBehavior active;
    private PNEnemyBehaviorContext selection;
    private string nextBehavior;
    private PNEnemySwitchPolicy nextPolicy;
    private PNEnemySwitchReceipt pendingSwitch;
    private bool dispatching, switching, endingAction, entering;
    private readonly System.Collections.Generic.List<PNEnemyResourceScope> cleanupScopes = new System.Collections.Generic.List<PNEnemyResourceScope>();
    private PNSkillAction running;
    private PNSkillActionContext runningContext;
    private PNEnemyCastReceipt runningReceipt;
    private System.IDisposable navigationSuspension;
    private PNStat enemyStat;
    public string ActiveBehaviorId => active != null ? active.BehaviorId : null;
    public bool IsExecutingEnemyAction => running != null;
    public bool IsSwitchPending => pendingSwitch?.Status == PNEnemySwitchStatus.Pending;
    public System.Exception Failure { get; private set; }
    public long CastCount { get; private set; }
    public int PendingCleanupScopes => cleanupScopes.Count;
    public override System.Collections.Generic.IEnumerable<EveEngine.EveId> BuiltInSkillIds
    {
        get
        {
            var ids = new System.Collections.Generic.HashSet<EveEngine.EveId>();
            foreach (var behavior in GetComponentsInChildren<PNEnemyBehavior>(true)) foreach (var id in behavior.SkillIds) ids.Add(id);
            return ids;
        }
    }
    public override System.Collections.Generic.IEnumerable<System.Type> Dependencies => GetComponentsInChildren<PNEnemyBehavior>(true).Length == 0
        ? base.Dependencies : new[] { typeof(PNControlManager), typeof(PNAbilityManager), typeof(PNEnemyMove), typeof(PNStat) };
    public void ConfigureInitialBehavior(string id)
    { if (enemyContext != null) throw new System.InvalidOperationException("Configure enemy before load."); initialBehaviorId = id; }
    public override void Load(EveEngine.EveEntityLoadContext context)
    {
        enemyContext = context; behaviors.Clear(); Failure = null;
        foreach (var behavior in GetComponentsInChildren<PNEnemyBehavior>(true))
        {
            behavior.ValidateConfiguration();
            if (behavior.GetComponentInParent<PNEnemySkill>(true) != this || behaviors.ContainsKey(behavior.BehaviorId)) throw new System.InvalidOperationException("Foreign/duplicate enemy behavior.");
            behaviors.Add(behavior.BehaviorId, behavior);
        }
        // Empty PNEnemySkill remains a valid extension point on existing passive enemies.
        if (behaviors.Count > 0 && !behaviors.ContainsKey(initialBehaviorId)) throw new System.InvalidOperationException("Initial enemy behavior is missing.");
        if (behaviors.Count > 0 && context.Session.HasAuthority && context.Identity.World?.CanExecute == true && PNWorldNavigation.Find(context.Identity.World) == null)
            throw new System.InvalidOperationException("Enemy behavior requires prepared world navigation before publication.");
        base.Load(context); enemyStat = GetComponent<PNStat>(); if (enemyStat != null) enemyStat.Died += Died;
    }
    internal bool IsCurrent(PNEnemyBehaviorContext value) => ReferenceEquals(value, selection) && !value.Retired &&
        enemyContext != null && enemyContext.CanUse && enemyContext.Session.HasAuthority && isActiveAndEnabled && Failure == null &&
        (enemyStat == null || !enemyStat.IsDead) && ReferenceEquals(selectedWorld, GetComponent<EveEngine.EveEntityIdentity>()?.World) &&
        (!IsSwitchPending || entering) && (!switching || entering) && !endingAction;
    internal bool CanExecuteBehavior => CanThink;
    private bool CanThink => enemyContext != null && enemyContext.CanExecute && enemyContext.Session.HasAuthority &&
        IsReady && GetComponent<EveEngine.EveEntityLoader>()?.IsReady == true && isActiveAndEnabled && Failure == null && cleanupScopes.Count == 0 && (enemyStat == null || !enemyStat.IsDead);
    protected override void Update()
    {
        if (enemyContext == null) return;
        if (Failure == null && cleanupScopes.Count > 0)
        { try { DrainCleanup(); } catch (System.Exception error) { Fault(error); return; } }
        if (!enemyContext.CanUse || enemyStat != null && enemyStat.IsDead)
        { StopEnemy(enemyStat != null && enemyStat.IsDead ? PNEnemyBehaviorExit.Death : PNEnemyBehaviorExit.Unload); return; }
        if (active != null && !ReferenceEquals(selectedWorld, GetComponent<EveEngine.EveEntityIdentity>()?.World)) StopEnemy(PNEnemyBehaviorExit.WorldChanged);
        if (!CanThink) return;
        try
        {
            if (active == null && behaviors.Count > 0 && GetComponent<PNEnemyMove>().NavigationReady) Select(initialBehaviorId);
            base.Update();
            var clock = GetComponent<EveEngine.EveTimeScale>(); float delta = clock != null && clock.isActiveAndEnabled ? clock.DeltaTime : UnityEngine.Time.deltaTime;
            if (delta <= 0) return;
            if (!IsSwitchPending && active != null)
            { dispatching = true; try { active.Tick(delta); } finally { dispatching = false; } }
            dispatching = true; try { AdvanceAction(delta); } finally { dispatching = false; } ApplySwitch();
        }
        catch (System.Exception error) { Fault(error); }
    }
    public PNEnemySwitchReceipt Switch(string id, PNEnemySwitchPolicy policy)
    {
        if (!System.Enum.IsDefined(typeof(PNEnemySwitchPolicy), policy) || id == null || !behaviors.ContainsKey(id)) throw new System.ArgumentException("Unknown behavior or switch policy.");
        if (!CanThink) throw new System.InvalidOperationException("Enemy switch requires active authority.");
        if (IsSwitchPending || switching || endingAction || entering) return new PNEnemySwitchReceipt { Status = PNEnemySwitchStatus.Busy };
        if (active != null && active.BehaviorId == id) return new PNEnemySwitchReceipt { Status = PNEnemySwitchStatus.Applied };
        pendingSwitch = new PNEnemySwitchReceipt { Status = PNEnemySwitchStatus.Pending }; nextBehavior = id; nextPolicy = policy;
        if (!dispatching) try { ApplySwitch(); } catch (System.Exception error) { Fault(error); } return pendingSwitch;
    }
    private void ApplySwitch()
    {
        if (!IsSwitchPending || dispatching || switching) return;
        if (nextPolicy == PNEnemySwitchPolicy.FinishCurrent && running != null && CurrentInvocation?.Committed == true) return;
        var receipt = pendingSwitch; var next = nextBehavior;
        switching = true;
        try { StopSelection(PNEnemyBehaviorExit.Switch); Select(next); receipt.Status = PNEnemySwitchStatus.Applied; }
        catch (System.Exception error) { receipt.Error = error; receipt.Status = PNEnemySwitchStatus.Failed; throw; }
        finally { switching = false; }
    }
    private void Select(string id)
    {
        var behavior = behaviors[id]; if (!behavior.isActiveAndEnabled) throw new System.InvalidOperationException("Selected enemy behavior is disabled.");
        active = behavior; selection = new PNEnemyBehaviorContext(this, System.Guid.NewGuid()); selectedWorld = GetComponent<EveEngine.EveEntityIdentity>()?.World;
        behavior.Attach(selection); entering = true; try { behavior.Enter(); } finally { entering = false; }
    }
    internal bool TryCast(PNEnemyBehaviorContext owner, EveEngine.EveId id, UnityEngine.Vector3 direction, PNEnemyActionExecution execution,
        System.Collections.Generic.IEnumerable<PNStat> targets, System.Func<bool> pay)
        => TryCastCore(owner, id, direction, execution, targets, pay, false, out _, null);
    internal PNEnemyCastAdmission TryCastWithReceipt(PNEnemyBehaviorContext owner, EveEngine.EveId id, UnityEngine.Vector3 direction, PNEnemyActionExecution execution,
        System.Collections.Generic.IEnumerable<PNStat> targets, System.Func<bool> pay, out PNEnemyCastReceipt receipt, UnityEngine.Vector3? position = null)
    {
        receipt = null;
        if (running != null) return PNEnemyCastAdmission.Busy;
        if (!TryCastCore(owner, id, direction, execution, targets, pay, true, out receipt, position)) { receipt = null; return PNEnemyCastAdmission.Denied; }
        return PNEnemyCastAdmission.Started;
    }
    private bool TryCastCore(PNEnemyBehaviorContext owner, EveEngine.EveId id, UnityEngine.Vector3 direction, PNEnemyActionExecution execution,
        System.Collections.Generic.IEnumerable<PNStat> targets, System.Func<bool> pay, bool wantsReceipt, out PNEnemyCastReceipt receipt, UnityEngine.Vector3? position)
    {
        receipt = null;
        if (!IsCurrent(owner) || !CanThink || running != null || endingAction || entering) return false;
        if (!System.Enum.IsDefined(typeof(PNEnemyActionExecution), execution)) throw new System.ArgumentOutOfRangeException(nameof(execution));
        bool declared = false; foreach (var candidate in active.SkillIds)
            if (enemyContext.Registry.Skills.ResolveIdentity(candidate) == enemyContext.Registry.Skills.ResolveIdentity(id)) declared = true;
        if (!declared) throw new System.InvalidOperationException("Behavior did not declare this skill.");
        if (!CanActivateSkill(id)) return false;
        if (position.HasValue) PNNavigationSettings.Position(position.Value);
        BeginActivation(id); bool wasDispatching = dispatching; dispatching = true;
        runningReceipt = receipt = wantsReceipt ? new PNEnemyCastReceipt(CurrentInvocation.Id) : null;
        try
        {
            running = position.HasValue ? CreateAction(id, out runningContext, targets, direction, position) : CreateAction(id, out runningContext, targets, direction);
            if ((execution == PNEnemyActionExecution.AuxiliaryTimeline) != running.HasAuxiliaryTimeline)
                throw new System.InvalidOperationException("Selected action does not support the explicit enemy execution mode.");
            navigationSuspension = GetComponent<PNEnemyMove>().SuspendNavigation();
            if (running is IPNSkillMotionAction motion) motion.BindMovementOwner(() => EndAction(true));
            var action = running; action.Start(runningContext);
            if (!ReferenceEquals(action, running)) return false;
            if (pay != null && !pay()) { EndAction(true); return false; }
            if (!ReferenceEquals(action, running)) return false;
            if (IsSwitchPending) { EndAction(true); return false; }
            var executionContext = runningContext;
            action.Perform(executionContext); CommitActivation(executionContext.Targets, direction); CastCount++;
            if (!ReferenceEquals(action, running)) return true;
            if (execution == PNEnemyActionExecution.Instant) EndAction(false);
            return true;
        }
        catch (System.Exception error) { receipt?.Finish(PNEnemyCastStatus.Failed, "ExecutionError", error); EndAction(true); throw; }
        finally { dispatching = wasDispatching; }
    }
    private void AdvanceAction(float delta)
    {
        var action = running; if (action == null) return;
        if (action.AdvanceAuxiliary(delta * ExecutionSpeed) && ReferenceEquals(action, running)) EndAction(false);
    }
    private void EndAction(bool cancelled)
    {
        if (endingAction) return;
        var action = running; var context = runningContext; var suspension = navigationSuspension;
        var receipt = runningReceipt; runningReceipt = null;
        running = null; runningContext = null; navigationSuspension = null;
        if (action == null && suspension == null) return;
        endingAction = true; var errors = new System.Collections.Generic.List<System.Exception>();
        var resources = new PNEnemyResourceScope();
        if (suspension != null) resources.Add(suspension); if (action != null) resources.Add(action); cleanupScopes.Add(resources);
        try
        {
            try { if (action != null) { if (cancelled) action.Cancel(context); else action.Exit(context); } } catch (System.Exception e) { errors.Add(e); }
            try { DrainCleanup(); } catch (System.Exception e) { errors.Add(e); }
            try { EndActivation(cancelled); } catch (System.Exception e) { errors.Add(e); }
        }
        finally { endingAction = false; }
        if (errors.Count > 0)
        { var error = new System.AggregateException("Enemy execution cleanup failed.", errors); receipt?.Finish(PNEnemyCastStatus.Failed, "CleanupError", error); throw error; }
        var outcome = action is IPNSkillActionOutcome result ? result.Outcome : PNMotionOutcome.Completed;
        receipt?.Finish(cancelled ? PNEnemyCastStatus.Cancelled : outcome == PNMotionOutcome.Completed ? PNEnemyCastStatus.Completed : PNEnemyCastStatus.Failed, outcome.ToString());
    }
    private void StopSelection(PNEnemyBehaviorExit reason)
    {
        var previous = active; var previousContext = selection; active = null; selection = null; selectedWorld = null;
        if (previousContext != null) { previousContext.Retired = true; cleanupScopes.Add(previousContext.Resources); }
        var errors = new System.Collections.Generic.List<System.Exception>();
        try { EndAction(true); } catch (System.Exception e) { errors.Add(e); }
        var move = GetComponent<PNEnemyMove>();
        try { if (move?.NavigationRequest != null) move.CancelNavigation(move.NavigationRequest); } catch (System.Exception e) { errors.Add(e); }
        try { previous?.Exit(reason); } catch (System.Exception e) { errors.Add(e); }
        finally { previous?.Detach(); }
        try { DrainCleanup(); } catch (System.Exception e) { errors.Add(e); }
        if (errors.Count > 0) throw new System.AggregateException("Enemy selection cleanup failed.", errors);
    }
    private void StopEnemy(PNEnemyBehaviorExit reason)
    {
        if (pendingSwitch?.Status == PNEnemySwitchStatus.Pending) pendingSwitch.Status = PNEnemySwitchStatus.Cancelled;
        if (active != null || running != null) StopSelection(reason);
    }
    private void Fault(System.Exception error)
    {
        // Preserve execution failure before StopEnemy cancels the owned Action for cleanup.
        runningReceipt?.Finish(PNEnemyCastStatus.Failed, "ExecutionError", error);
        Failure = error;
        try { StopEnemy(PNEnemyBehaviorExit.Fault); } catch (System.Exception cleanup) { Failure = new System.AggregateException(error, cleanup); }
        UnityEngine.Debug.LogException(Failure, this);
    }
    internal void BehaviorDisabled(PNEnemyBehavior behavior)
    { if (ReferenceEquals(active, behavior)) StopEnemy(PNEnemyBehaviorExit.Disabled); }
    private void Died(PNDamageResult result) { StopEnemy(PNEnemyBehaviorExit.Death); }
    private void DrainCleanup()
    {
        var errors = new System.Collections.Generic.List<System.Exception>();
        for (int i = cleanupScopes.Count - 1; i >= 0; i--)
            try { cleanupScopes[i].Dispose(); if (cleanupScopes[i].IsEmpty) cleanupScopes.RemoveAt(i); } catch (System.Exception e) { errors.Add(e); }
        if (errors.Count > 0) throw new System.AggregateException("Enemy cleanup scopes remain pending.", errors);
    }
    public override bool TryInterrupt(EveEngine.EveId reason)
    {
        if (!reason.IsValid) throw new System.ArgumentException("Interruption reason required.");
        if (running == null) return false; EndAction(true); return true;
    }
    public override void CancelWorldExecutions()
    { try { StopEnemy(PNEnemyBehaviorExit.WorldChanged); } finally { base.CancelWorldExecutions(); } }
    protected override void OnDisable()
    { try { StopEnemy(PNEnemyBehaviorExit.Disabled); } finally { base.OnDisable(); } }
    public override void Unload()
    {
        try { StopEnemy(PNEnemyBehaviorExit.Unload); DrainCleanup(); if (cleanupScopes.Count != 0) throw new EveEngine.EveWorldCleanupPendingException("Enemy execution cleanup is waiting for its scan safe point."); }
        finally { if (enemyStat != null) enemyStat.Died -= Died; enemyStat = null; enemyContext = null; behaviors.Clear(); base.Unload(); }
    }
#endif
}
