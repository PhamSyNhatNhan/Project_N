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
    protected bool HasGameplayAuthority=>!EveRegistrySession.IsReady||EveRegistrySession.Context.HasAuthority;
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
        if (!IsReady || Effects == null || !EveRegistrySession.IsReady || !EveRegistrySession.Context.HasAuthority) return false;
        prepared?.Dispose(); prepared = Effects.Prepare(skillId);
        var categories = prepared.AdmissionCategories;
        LastControlDecision = Controls.CheckSkill(categories.Select(x => registry.SkillCategories().Get(x).ControlPermissionId), skillId);
        var stat = GetComponent<PNStat>();
        bool allowed = categories.Count > 0 && LastControlDecision.Allowed && (stat == null || !stat.IsDead) && (!prepared.Metadata.AffectedByAttackSpeed || stat != null && stat.IsReady && stat.Evaluate(PNContentIds.AttackSpeed) > 0);
        if (!allowed) { prepared.Dispose(); prepared = null; } return allowed;
    }
    public bool CanActivateSkill(EveId category, EveId skillId)
        => CanActivateSkill(skillId);
    protected void BeginActivation(EveId skillId)
    {
        if (prepared == null || prepared.SkillId != registry.Skills.ResolveIdentity(skillId))
            throw new InvalidOperationException("Prepare activation before beginning it.");
        EndActivation(true); CurrentInvocation = prepared; prepared = null;
    }
    protected void CommitActivation(IEnumerable<PNStat> targets = null, Vector3? direction = null)
    {
        var value = CurrentInvocation;
        if (value == null || value.IsEnded || value.Committed) return;
        value.Committed = true;
        var context = new PNSkillActionContext(this, value.SkillId, null, registry, targets, direction, value);
        context.ContributionSnapshot = value.ActivationSnapshot; value.ActivationContext = context;
        context.Fire("OnActivated");
    }
    protected void EndActivation(bool cancelled = false)
    {
        executingBehavior = null; executingSlot = default;
        var value = CurrentInvocation; CurrentInvocation = null;
        try { if (value != null && !value.IsEnded && value.Committed) value.ActivationContext?.Fire(cancelled ? "OnInvocationCancel" : "OnInvocationComplete"); }
        finally { value?.Dispose(); }
    }
    protected float ExecutionSpeed => CurrentInvocation?.Metadata.AffectedByAttackSpeed == true ? (float)Math.Max(0, GetComponent<PNStat>().Evaluate(PNContentIds.AttackSpeed) / 100) : 1;
    protected void AdvanceAuxiliaryEffects(float delta) => Effects?.Advance(delta);
    protected virtual void Update()
    { if (!IsReady||!HasGameplayAuthority) return; var clock = GetComponent<EveTimeScale>(); var delta=clock != null && clock.isActiveAndEnabled ? clock.DeltaTime : Time.deltaTime; AdvanceAuxiliaryEffects(delta);AdvanceBindings(delta); }
    protected virtual void OnDisable() { try{CancelBindingInputs();}finally{SuspendBindingSelections();prepared?.Dispose();prepared = null;EndActivation(true);Effects?.CancelExecutions();} }
    protected virtual void OnDestroy() => Unload();
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
    {
        var loader = GetComponent<EveEntityLoader>();
        if (!IsReady || registry == null || loader == null || !loader.IsReady)
            throw new InvalidOperationException("Action execution requires a Ready entity loaded through its registry.");
        PNSkillData data = GetData(skillId);
        if (data == null) throw new InvalidOperationException("This skill has no action data.");
        context = new PNSkillActionContext(this, registry.Skills.ResolveIdentity(skillId), data, registry, targets, direction);
        return registry.Actions().Get(data.Action.Id).Create();
    }

    public override void Unload()
    {
        try { UnloadBindings(); }
        finally {
        prepared?.Dispose(); prepared = null; EndActivation(true); Effects?.Dispose(); Effects = null;
        registry = null;builtInData.Clear(); LastControlDecision = null;
        base.Unload();
        }
    }
}
