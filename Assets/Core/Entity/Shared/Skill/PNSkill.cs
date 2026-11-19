using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;

// EN: Extension point for skill data and behavior shared by Project_N characters.
// VI: Điểm kế thừa cho dữ liệu và hành vi skill chung của nhân vật Project_N.
[RequireComponent(typeof(PNAbilityManager))]
public partial class PNSkill : EveSkill, EveIEntityLoadPreparation, EveIEntityCommitStep
{
    // EN: On the owner's client (no authority) a slot press is sent to the authority instead of run here (Docs/SkillNetworkingAPlan.md S2); null everywhere else.
    public IPNSkillInputForwarder InputForwarder { get; set; }
    // True on the machine with authority when an owner on another machine may drive this entity's skill input (PNSkillRemoteInput sets it): the local-player permission then does not apply, the entity's own state (ready, not held, network gate open) does.
    public bool AcceptsRemoteInput { get; set; }
    // Counts the activations this machine committed (an activation starts when a press really began a skill). The authority uses it to tell a press that cast from one that was declined.
    public long ActivationSerial { get; private set; }
    protected bool CanForwardInput => InputForwarder != null && InputForwarder.IsActive;
    protected bool HasGameplayAuthority=>(!EveRegistrySession.IsReady||EveRegistrySession.Context.HasAuthority)&&PNWorldGameplay.CanExecute(this);
    public void PrepareEntityLoad()
    { if (GetComponent<PNControlManager>() == null) gameObject.AddComponent<PNControlManager>(); }
    public override IEnumerable<Type> Dependencies => new[] { typeof(PNControlManager) };
    public PNControlState Controls => (GetComponent<PNControlManager>() ?? throw new InvalidOperationException("Entity control manager required.")).State;
    public PNControlDecision LastControlDecision { get; private set; }
    public PNSkillEffectState Effects { get; private set; }
    public PNSkillInvocation CurrentInvocation { get; private set; }
    private PNSkillInvocation prepared;
    public bool CanActivateSkill(EveId skillId)
    {
        if (ExecutingProfile.IsValid && (inputBehavior == null || !ReferenceEquals(inputBehavior, executingBehavior))) return false;
        if (!IsReady || Effects == null || !EveRegistrySession.IsReady) return false;
        // G6a: on the owner's client (no authority) the same admission opens a Predicted cast. What only the authority knows (a stun, a lock) counts as allowed there: if it refuses, the guess is taken back.
        bool predicted = !EveRegistrySession.Context.HasAuthority;
        if (predicted && !CanForwardInput) return false;
        prepared?.Dispose(); prepared = predicted ? Effects.Prepare(skillId, PNSkillMode.Predicted) : Effects.Prepare(skillId);
        var stat = GetComponent<PNStat>();
        if (predicted)
        {
            if (stat != null && stat.IsDead) { prepared.Dispose(); prepared = null; return false; }
            return true;
        }
        var categories = prepared.AdmissionCategories;
        LastControlDecision = Controls.CheckSkill(categories.Select(x => registry.SkillCategories().Get(x).ControlPermissionId), skillId);
        // A skill with no category is allowed; it bypasses category rights but never SkillAll or exact-skill leases.
        bool allowed = LastControlDecision.Allowed && (stat == null || !stat.IsDead) && (!prepared.Metadata.AffectedByAttackSpeed || stat != null && stat.IsReady && stat.Evaluate(PNContentIds.AttackSpeed) > 0);
        if (!allowed) { prepared.Dispose(); prepared = null; } return allowed;
    }
    public bool CanActivateSkill(EveId category, EveId skillId)
        => CanActivateSkill(skillId);
    protected void BeginActivation(EveId skillId)
    {
        if (prepared == null || prepared.SkillId != registry.Skills.ResolveIdentity(skillId))
            throw new InvalidOperationException("Prepare activation before beginning it.");
        var next = prepared; prepared = null;
        EndActivation(true, PNCastEnd.Replaced);
        // A guess writes the replica Abilities inside a scope the invocation owns (it ends with the cast; the owner can still roll it back).
        if (next.Mode == PNSkillMode.Predicted)
        {
            try { next.PredictionScope = Abilities.BeginPrediction(); }
            catch { next.Dispose(); throw; }
        }
        AssignCastKey(next);
        CurrentInvocation = next;
        if (next.Mode == PNSkillMode.Predicted) PredictedCastBegan?.Invoke(next);
    }
    // G6c: the key the guess and the authority's cast share (see PNSkillCastKey): the input being dispatched, else the previous key + 1.
    private void AssignCastKey(PNSkillInvocation cast)
    {
        cast.CastKey = lastCastKey = PendingCastKey > 0 ? Math.Max(lastCastKey + 1, PNSkillCastKey.FromInput(PendingCastKey)) : lastCastKey + 1;
        // Whether the owner's machine guessed this cast (the input said so); a cast with no input of its own (the next stroke of a held Repeat) keeps the answer of the press that began it.
        if (PendingCastKey > 0) stickyGuessed = PendingGuessed;
        cast.ShooterGuessed = stickyGuessed;
        if (cast.PredictionScope != null) cast.PredictionScope.CastKey = cast.CastKey;
    }
    private long lastCastKey; private bool stickyGuessed;
    public bool PendingGuessed { get; set; }
    // Set by the wire layer around the dispatch of one input (the key that input travels under); 0 when no input is being dispatched.
    public long PendingCastKey { get; set; }
    public long LastCastKey => lastCastKey;
    // G6a: raised on the owner's client when a guessed cast begins (the wire layer keeps it with the press that caused it, to take it back if the authority declines).
    public event Action<PNSkillInvocation> PredictedCastBegan;
    // Takes a guessed cast back: stops it if it still runs (the skill's own interrupt path) and puts its Ability writes back.
    public void RollbackPredictedCast(PNSkillInvocation invocation)
    {
        if (invocation == null || invocation.Mode != PNSkillMode.Predicted) return;
        // A refused guess is as if it never was: nothing more fires from it, not even the cancel triggers of the interrupt below.
        invocation.Refused = true;
        try
        {
            if (ReferenceEquals(CurrentInvocation, invocation) && !invocation.IsEnded && !TryInterrupt(EveId.Parse("project_n:prediction_refused"))) CancelCast(PNCastEnd.Interrupted);
        }
        finally
        {
            invocation.PredictionScope?.Rollback();
            Effects?.CancelAuxiliaries(invocation);
            // The projectiles this guess made go with it (spawned-object sync, plan 4.4a).
#if UNITY_5_3_OR_NEWER
            if (invocation.CastKey > 0 && EveRegistrySession.IsReady)
                try { PNProjectileManager.Resolve(EveRegistrySession.Context, gameObject.scene).DiscardPredicted(invocation.CastKey); } catch (InvalidOperationException) { }
#endif
        }
    }
    // G1: a cast that runs on a machine without authority, as a guess (Predicted) or a replay for its pictures (Observed). It is never committed: no activation serial, no OnActivated.
    // One invocation per skill at a time; end it with EndNonAuthorityInvocation. Nothing in Eve uses this yet (G6/G8 will).
    public PNSkillInvocation BeginNonAuthorityInvocation(EveId skillId, PNSkillMode mode)
    {
        if (mode == PNSkillMode.Authority) throw new ArgumentException("Authority casts begin through the activation path.", nameof(mode));
        if (!IsReady || Effects == null) throw new InvalidOperationException("Skill is not ready.");
        if (CurrentInvocation != null) throw new InvalidOperationException("A cast is already running.");
        var invocation = Effects.Prepare(skillId, mode);
        // A guess writes the replica Abilities inside a prediction scope that the invocation owns: it ends with the cast, and the caller can still roll it back.
        if (mode == PNSkillMode.Predicted)
        {
            try { invocation.PredictionScope = Abilities.BeginPrediction(); }
            catch { invocation.Dispose(); throw; }
        }
        if (mode == PNSkillMode.Predicted) AssignCastKey(invocation);
        return CurrentInvocation = invocation;
    }
    public void EndNonAuthorityInvocation(bool cancelled = false, string reason = null)
    {
        var value = CurrentInvocation;
        if (value == null || value.Mode == PNSkillMode.Authority) return;
        FinishInvocation(value, cancelled, reason);
    }
    // G3a: the end of a cast, one place for every way it can end. Order: the running things the cast registered are cancelled (cancelled only, last created first), the activation trigger fires
    // (committed casts), the skill's `OnCastEnded` runs once (the cast memory is still readable), then the invocation is disposed and the memory dropped.
    private void FinishInvocation(PNSkillInvocation value, bool cancelled, string reason)
    {
        if (value == null) return;
        if (value.IsEnded) { if (ReferenceEquals(CurrentInvocation, value)) CurrentInvocation = null; return; }
        if (value.Ending) return;
        value.Ending = true;
        try
        {
            if (cancelled) value.CancelTracked(reason ?? PNCastEnd.Interrupted);
            try { if (value.Committed || value.Activated) value.ActivationContext?.Fire(cancelled ? "OnInvocationCancel" : "OnInvocationComplete"); }
            finally
            {
                try { OnCastEnded(value, cancelled, reason); }
                finally { if (ReferenceEquals(CurrentInvocation, value)) CurrentInvocation = null; }
            }
        }
        finally { if (ReferenceEquals(CurrentInvocation, value)) CurrentInvocation = null; value.Dispose(); }
    }
    // The skill's one end hook: called exactly once for each invocation it ran, finished (`cancelled` false) or stopped (`reason` says why), while `CurrentInvocation` and its memory are still there.
    // Put the reset of the skill's state machine here (L6); do not start another cast from it.
    protected virtual void OnCastEnded(PNSkillInvocation invocation, bool cancelled, string reason) { }
    // Stops the running cast through the single cancel path. False when nothing ran.
    public bool CancelCast(string reason = null)
    {
        var value = CurrentInvocation; if (value == null || value.IsEnded) return false;
        // A cast that is cancelled also lets go of the input behavior that was driving it (as ending the activation always did).
        executingBehavior = null; executingSlot = default;
        FinishInvocation(value, true, reason); return true;
    }
    internal bool CancelInvocation(PNSkillInvocation invocation, string reason)
    {
        if (!ReferenceEquals(CurrentInvocation, invocation)) return false;
        FinishInvocation(invocation, true, reason); return true;
    }
    // G2: a stat source a cast sets or removes is the authority's state. In a guess or a replay the call is skipped (false) and the entity keeps the stats the authority sent; outside any cast it is the plain call.
    protected bool SetStatSource(PNStat stat, EveId sourceId, IEnumerable<EveStatModifier> modifiers)
    {
        if (CurrentInvocation != null && CurrentInvocation.Mode != PNSkillMode.Authority) return false;
        stat.SetSource(sourceId, modifiers); return true;
    }
    protected bool RemoveStatSource(PNStat stat, EveId sourceId)
    {
        if (CurrentInvocation != null && CurrentInvocation.Mode != PNSkillMode.Authority) return false;
        stat.RemoveSource(sourceId); return true;
    }
    // G2: a hit of one of this skill's actions was applied (authority only, called by `PNSkillActionContext.Hit`). The one place where a skill reacts to a hit or a kill: rewards, stacks, follow-up Buffs.
    public virtual void OnHitConfirmed(PNSkillActionContext context, PNStat target, PNDamageResult result) { }
    protected void CommitActivation(IEnumerable<PNStat> targets = null, Vector3? direction = null)
    {
        var value = CurrentInvocation;
        if (value == null || value.IsEnded || value.Committed || value.Activated || value.Mode == PNSkillMode.Observed) return;
        // A guess fires the activation triggers too (plan 4.6b) but never commits: no serial, the authority's cast is the real one.
        if (value.Mode == PNSkillMode.Authority) { value.Committed = true; ActivationSerial++; }
        value.Activated = true;
        var context = new PNSkillActionContext(this, value.SkillId, null, registry, targets, direction, value);
        context.ContributionSnapshot = value.ActivationSnapshot; value.ActivationContext = context;
        context.Fire("OnActivated");
    }
    protected void EndActivation(bool cancelled = false, string reason = null)
    {
        executingBehavior = null; executingSlot = default;
        FinishInvocation(CurrentInvocation, cancelled, reason);
    }
    protected float ExecutionSpeed => CurrentInvocation?.Metadata.AffectedByAttackSpeed == true ? (float)Math.Max(0, GetComponent<PNStat>().Evaluate(PNContentIds.AttackSpeed) / 100) : 1;
    protected void AdvanceAuxiliaryEffects(float delta) => Effects?.Advance(delta);
    // G3b. The idle clock: how long the skill has stood without a cast, on the clock `delta` comes from (the Game clock). The framework runs it only while no cast is running; the skill resets it when
    // the cast that should restart the count begins (Eve: a normal Attack). It is a plain float sum, so a skill that moves its timer here keeps the exact same values.
    // VI: Đồng hồ nghỉ: skill đã đứng không cast bao lâu. Framework chỉ chạy khi không có cast; skill đặt lại khi cast cần tính lại bắt đầu.
    public float IdleTime { get; private set; }
    protected void AdvanceIdle(float delta) { if (CurrentInvocation == null) IdleTime += delta; }
    protected void ResetIdle() => IdleTime = 0;
    protected virtual void Update()
    { if (!IsReady) return; if (!HasGameplayAuthority) { if (Effects != null && Effects.HasPredictedAuxiliaries) AdvanceAuxiliaryEffects(Time.deltaTime); return; } var clock = GetComponent<EveTimeScale>(); var delta=clock != null && clock.isActiveAndEnabled ? clock.DeltaTime : Time.deltaTime; AdvanceAuxiliaryEffects(delta);AdvanceBindings(delta); }
    protected virtual void OnDisable() { if(GetComponent<EveEntityIdentity>()?.IsChangingWorld==true)return;try{CancelBindingInputs();}finally{SuspendBindingSelections();prepared?.Dispose();prepared = null;EndActivation(true, PNCastEnd.Disabled);Effects?.CancelExecutions();} }
    protected virtual void OnDestroy() => Unload();
    public virtual void CancelWorldExecutions()
    {
        CancelBindingInputs();TryInterrupt(EveId.Parse("project_n:world_transfer"));prepared?.Dispose();prepared=null;EndActivation(true);Effects?.CancelExecutions();
    }
    public virtual bool TryInterrupt(EveId reason)
    { if (!reason.IsValid) throw new ArgumentException("Interruption reason required."); return false; }
    private EveRegistryHub registry;
    private readonly Dictionary<EveId,PNSkillData> builtInData=new Dictionary<EveId,PNSkillData>();
    public virtual IEnumerable<EveId> BuiltInSkillIds => Array.Empty<EveId>();

    // EN: Entity-owned passive state; no action creation, ticking or consumption is implicit.
    // VI: Trạng thái thụ động thuộc entity; không ngầm tạo action, cập nhật hoặc tiêu lượng.
    public PNAbilityCollection Abilities => (GetComponent<PNAbilityManager>()
        ?? throw new InvalidOperationException("This entity has no PNAbilityManager.")).Abilities;

    // EN: Explicit opt-in. Call before use and when CDR changes; never restarts a running timer.
    // VI: Chủ động chọn; gọi trước khi dùng và khi CDR đổi, không khởi động lại timer đang chạy.
    protected void ApplyCooldownReduction(PNAbility ability, double flat = 0d)
    {
        if (ability == null) throw new ArgumentNullException(nameof(ability));
        if (!Abilities.TryGet(ability.Handle, out var owned) || !ReferenceEquals(owned, ability))
            throw new ArgumentException("Ability must belong to this skill's entity.", nameof(ability));
        if (!(ability is PNAbilityCooldown) && !(ability is PNAbilityState))
            throw new ArgumentException("Only Cooldown and State abilities have cooldowns.", nameof(ability));
        // The reduction is the authority's number (an Ability definition change): a guess or a replica keeps what the authority sent (G2).
        if (EveRegistrySession.IsReady && !EveRegistrySession.Context.HasAuthority) return;
        var stat = GetComponent<PNStat>();
        if (stat == null || !stat.IsInitialized)
            throw new InvalidOperationException("Cooldown reduction requires ready entity stats.");
        double percent = stat.Evaluate(PNContentIds.CooldownReduction);
        if (ability is PNAbilityCooldown charges) charges.SetReduction(flat, percent);
        else ((PNAbilityState)ability).SetReduction(flat, percent);
    }

    protected override void LoadDefinition(EveRegistryHub registry, EveId id)
    {
        if (IsInitialized) return;
        this.registry = registry;
        Effects = new PNSkillEffectState(this, registry);
        base.LoadDefinition(registry, id);
        foreach(var skillId in BuiltInSkillIds)
        {
            var identity=registry.Skills.ResolveIdentity(skillId);
            if(TryGetSkillData(identity,out _) || builtInData.ContainsKey(identity))continue;
            builtInData.Add(identity,(PNSkillData)LoadSkillData(identity,registry.Skills.GetEffective(identity).Data));
        }
        LoadBindings(id);
    }

    protected override object LoadSkillData(EveId skillId, object data)
    {
        PNSkillData parsed = PNSkillData.Parse(data);
        if (parsed != null && registry != null)
        {
            registry.Actions().Get(parsed.Action.Id);
            parsed.Action.ValidateTags(registry);
        }
        return parsed;
    }

    public virtual PNSkillData GetData(EveId skillId)
    {
        if(TryGetSkillData(skillId,out var data))return (PNSkillData)data;
        var identity=registry.Skills.ResolveIdentity(skillId);
        if(builtInData.TryGetValue(identity,out var shared))return shared;
        throw new KeyNotFoundException("Skill is not selected by this entity: "+skillId);
    }
    public PNMotionAction CreateMotionAction(EveId skillId,Vector3 direction,out PNSkillActionContext context,float? distance=null)
    {
        var created=CreateAction(skillId,out context,direction);
        if(!(created is PNMotionAction motion)){created.Dispose();throw new InvalidOperationException("Expected a motion-compatible action.");}
        if(distance.HasValue)context=new PNMotionActionContext(context,distance.Value);
        return motion;
    }

    // EN: Creates a fresh invocation without calling any hook; the derived skill controls transitions.
    // VI: Tạo lần thi triển mới mà không gọi hook; skill kế thừa quyết định chuyển trạng thái.
    public virtual PNSkillAction CreateAction(EveId skillId, out PNSkillActionContext context)
        => CreateAction(skillId, out context, null);

    public virtual PNSkillAction CreateAction(EveId skillId, out PNSkillActionContext context, IEnumerable<PNStat> targets)
        => CreateAction(skillId, out context, targets, null);

    public virtual PNSkillAction CreateAction(EveId skillId, out PNSkillActionContext context,
        Vector3 direction, IEnumerable<PNStat> targets = null)
        => CreateAction(skillId, out context, targets, direction);

    public virtual PNSkillAction CreateAction(EveId skillId, out PNSkillActionContext context,
        IEnumerable<PNStat> targets, Vector3? direction)
        => CreateAction(skillId, out context, targets, direction, null);
    public virtual PNSkillAction CreateAction(EveId skillId, out PNSkillActionContext context,
        IEnumerable<PNStat> targets, Vector3? direction, Vector3? position)
    {
        var loader = GetComponent<EveEntityLoader>();
        if (!IsReady || registry == null || loader == null || !loader.IsReady)
            throw new InvalidOperationException("Action execution requires a Ready entity loaded through its registry.");
        PNSkillData data = GetData(skillId);
        if (data == null) throw new InvalidOperationException("This skill has no action data.");
        context = new PNSkillActionContext(this, registry.Skills.ResolveIdentity(skillId), data, registry, targets, direction, null, true, position);
        return registry.Actions().Get(data.Action.Id).Create();
    }

    public override void Unload()
    {
        try { EndObservedActions(); UnloadBindings(); }
        finally {
        try { prepared?.Dispose(); prepared = null; EndActivation(true, PNCastEnd.Unloaded); Effects?.Dispose(); Effects = null; }
        finally { ReleaseActionRuns(); registry = null;builtInData.Clear(); LastControlDecision = null; base.Unload(); }
        }
    }
}
