using System;
using EveEngine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Interactions;

// EN: Owns skill input independently of Move; concrete skills map input hooks to action hooks.
// VI: Sở hữu input skill độc lập với Move; skill cụ thể nối hook input với hook action.
[DisallowMultipleComponent]
public partial class PNPlayerSkill : PNSkill, IPNEntityResourceOwner
{
    [SerializeField] private PlayerSkillInputConfig attackInput = new PlayerSkillInputConfig();
    [SerializeField] private PlayerSkillInputConfig skillInput = new PlayerSkillInputConfig();
    [SerializeField] private PlayerSkillInputConfig ultiInput = new PlayerSkillInputConfig();
    [SerializeField] private PlayerSkillInputConfig dashInput = new PlayerSkillInputConfig();
    [SerializeField] private PlayerSkillInputConfig burstInput = new PlayerSkillInputConfig();
    protected PNPlayerSkillControls pc;
    protected bool canInput = true;
    private bool menuInputBlocked;
    public bool MenuInputBlocked
    {
        get => menuInputBlocked;
        set { if(menuInputBlocked==value)return; menuInputBlocked=value; if(pc!=null)RefreshInput(); }
    }
    public virtual PNPlayerSkillControls PC => pc;
    public virtual PlayerSkillInputConfig AttackInput => attackInput;
    public virtual PlayerSkillInputConfig SkillInput => skillInput;
    public virtual PlayerSkillInputConfig UltiInput => ultiInput;
    public virtual PlayerSkillInputConfig DashInput => dashInput;
    public virtual PlayerSkillInputConfig BurstInput => burstInput;
    public virtual bool CanInput
    {
        get => canInput && !menuInputBlocked && HasGameplayAuthority && PNLocalPlayerInput.Allows(GetComponent<EveEntityLoader>());
        set
        {
            if (canInput == value) return;
            canInput = value;
            if (pc != null) RefreshInput();
        }
    }

    protected virtual void Awake()
        => EnsureInputInitialized();
    public void EnsureInputInitialized()
    {
        if (pc != null) return;
        DashInput.CanTap=true;DashInput.PressImmediately=true;DashInput.HoldMode=PlayerSkillHoldMode.None;
        PNRepeatInteraction.Register();
        pc = new PNPlayerSkillControls();
        Subscribe();
        AwakeSetUp();
    }
    protected virtual void Start() => StartSetUp();
    protected virtual void AwakeSetUp() { }
    protected virtual void StartSetUp() { }
    protected virtual void OnEnable() { if (pc != null) RefreshInput(); }
    protected override void OnDisable() { try { CancelPlayerDash(); DisableButtons(); } finally { base.OnDisable(); } }
    protected override void OnDestroy()
    {
        try
        {
            DisposeEntityResources();
        }
        finally { base.OnDestroy(); }
    }
    public void DisposeEntityResources()
    {
        UnloadPlayerDash();
        if(pc==null)return;
        DisableButtons();Unsubscribe();pc.Dispose();pc=null;
    }
    protected virtual void OnValidate()
    {
        if (Application.isPlaying && pc != null) RefreshInput();
    }
    public override void Load(EveEntityLoadContext context)
    {
        base.Load(context);
        LoadPlayerDash();
        if (pc != null) RefreshInput();
    }
    public override void Unload()
    {
        UnloadPlayerDash();
        DisableButtons();
        base.Unload();
    }

    // EN: Explicitly reapply after changing configuration; Disable ends the old input interaction first.
    // VI: Gọi sau khi đổi cấu hình; Disable kết thúc tương tác input cũ trước khi áp cấu hình mới.
    public virtual void RefreshInput()
    {
        if (pc == null) return;
        AttackInput.Validate(); SkillInput.Validate(); UltiInput.Validate(); DashInput.Validate(); BurstInput.Validate();
        DisableButtons();
        Apply(pc.Controller.Attack, AttackInput);
        Apply(pc.Controller.Skill, SkillInput);
        Apply(pc.Controller.Ulti, UltiInput);
        Apply(pc.Controller.Dash, DashInput);
        Apply(pc.Controller.Burst, BurstInput);
    }
    private void Apply(InputAction action, PlayerSkillInputConfig config)
    {
        string interactions = config.BuildInteractions();
        for (int i = 0; i < action.bindings.Count; i++)
        {
            var binding = action.bindings[i];
            if (binding.isPartOfComposite) continue;
            binding.overrideInteractions = interactions;
            action.ApplyBindingOverride(i, binding);
        }
        if (isActiveAndEnabled && CanInput && config.HasInteraction) action.Enable();
    }
    private void DisableButtons()
    {
        if (pc == null) return;
        pc.Controller.Attack.Disable(); pc.Controller.Skill.Disable(); pc.Controller.Ulti.Disable();
        pc.Controller.Dash.Disable(); pc.Controller.Burst.Disable();
    }
    private void Subscribe()
    {
        pc.Controller.Attack.started += StartedAttack;
        pc.Controller.Attack.performed += PerformedAttack;
        pc.Controller.Attack.canceled += CanceledAttack;
        pc.Controller.Skill.started += StartedSkill;
        pc.Controller.Skill.performed += PerformedSkill;
        pc.Controller.Skill.canceled += CanceledSkill;
        pc.Controller.Ulti.started += StartedUlti;
        pc.Controller.Ulti.performed += PerformedUlti;
        pc.Controller.Ulti.canceled += CanceledUlti;
        pc.Controller.Dash.started += StartedDash;
        pc.Controller.Dash.performed += PerformedDash;
        pc.Controller.Dash.canceled += CanceledDash;
        pc.Controller.Burst.started += StartedBurst;
        pc.Controller.Burst.performed += PerformedBurst;
        pc.Controller.Burst.canceled += CanceledBurst;
    }
    private void Unsubscribe()
    {
        pc.Controller.Attack.started -= StartedAttack;
        pc.Controller.Attack.performed -= PerformedAttack;
        pc.Controller.Attack.canceled -= CanceledAttack;
        pc.Controller.Skill.started -= StartedSkill;
        pc.Controller.Skill.performed -= PerformedSkill;
        pc.Controller.Skill.canceled -= CanceledSkill;
        pc.Controller.Ulti.started -= StartedUlti;
        pc.Controller.Ulti.performed -= PerformedUlti;
        pc.Controller.Ulti.canceled -= CanceledUlti;
        pc.Controller.Dash.started -= StartedDash;
        pc.Controller.Dash.performed -= PerformedDash;
        pc.Controller.Dash.canceled -= CanceledDash;
        pc.Controller.Burst.started -= StartedBurst;
        pc.Controller.Burst.performed -= PerformedBurst;
        pc.Controller.Burst.canceled -= CanceledBurst;
    }
    protected virtual void StartedAttack(InputAction.CallbackContext ctx)
    {
        if (!CanInput || !isActiveAndEnabled) return;
        if (ctx.interaction is TapInteraction || ctx.interaction is PressInteraction)
        { StartTapAttack(); }
        else if (ctx.interaction is HoldInteraction)
        { StartHoldAttack(); }
        else if (ctx.interaction is SlowTapInteraction)
        { StartSlowTapAttack(); }
        else if (ctx.interaction is PNRepeatInteraction)
        { StartRepeatAttack(); }
    }
    protected virtual void PerformedAttack(InputAction.CallbackContext ctx)
    {
        if (!CanInput || !isActiveAndEnabled) return;
        if (ctx.interaction is TapInteraction || ctx.interaction is PressInteraction)
        { EnterTapAttack(); TapAttack(); ExitTapAttack(); }
        else if (ctx.interaction is HoldInteraction)
        { EnterHoldAttack(); HoldAttack(); ExitHoldAttack(); }
        else if (ctx.interaction is SlowTapInteraction)
        { EnterSlowTapAttack(); SlowTapAttack(); ExitSlowTapAttack(); }
        else if (ctx.interaction is PNRepeatInteraction)
        { RepeatAttack(); }
    }
    protected virtual void CanceledAttack(InputAction.CallbackContext ctx)
    {
        if (ctx.interaction is TapInteraction || ctx.interaction is PressInteraction)
        { CanceledTapAttack(); }
        else if (ctx.interaction is HoldInteraction)
        { CanceledHoldAttack(); }
        else if (ctx.interaction is SlowTapInteraction)
        { CanceledSlowTapAttack(); }
        else if (ctx.interaction is PNRepeatInteraction)
        { CanceledRepeatAttack(); }
    }
    protected virtual void StartTapAttack() { }
    protected virtual void TapAttack() { }
    protected virtual void CanceledTapAttack() { }
    protected virtual void EnterTapAttack() { }
    protected virtual void ExitTapAttack() { }
    protected virtual void StartHoldAttack() { }
    protected virtual void HoldAttack() { }
    protected virtual void CanceledHoldAttack() { }
    protected virtual void EnterHoldAttack() { }
    protected virtual void ExitHoldAttack() { }
    protected virtual void StartSlowTapAttack() { }
    protected virtual void SlowTapAttack() { }
    protected virtual void CanceledSlowTapAttack() { }
    protected virtual void EnterSlowTapAttack() { }
    protected virtual void ExitSlowTapAttack() { }
    protected virtual void StartRepeatAttack() { }
    protected virtual void RepeatAttack() { }
    protected virtual void CanceledRepeatAttack() { }
    protected virtual void StartedSkill(InputAction.CallbackContext ctx)
    {
        if (!CanInput || !isActiveAndEnabled) return;
        if (ctx.interaction is TapInteraction || ctx.interaction is PressInteraction)
        { StartTapSkill(); }
        else if (ctx.interaction is HoldInteraction)
        { StartHoldSkill(); }
        else if (ctx.interaction is SlowTapInteraction)
        { StartSlowTapSkill(); }
        else if (ctx.interaction is PNRepeatInteraction)
        { StartRepeatSkill(); }
    }
    protected virtual void PerformedSkill(InputAction.CallbackContext ctx)
    {
        if (!CanInput || !isActiveAndEnabled) return;
        if (ctx.interaction is TapInteraction || ctx.interaction is PressInteraction)
        { EnterTapSkill(); TapSkill(); ExitTapSkill(); }
        else if (ctx.interaction is HoldInteraction)
        { EnterHoldSkill(); HoldSkill(); ExitHoldSkill(); }
        else if (ctx.interaction is SlowTapInteraction)
        { EnterSlowTapSkill(); SlowTapSkill(); ExitSlowTapSkill(); }
        else if (ctx.interaction is PNRepeatInteraction)
        { RepeatSkill(); }
    }
    protected virtual void CanceledSkill(InputAction.CallbackContext ctx)
    {
        if (ctx.interaction is TapInteraction || ctx.interaction is PressInteraction)
        { CanceledTapSkill(); }
        else if (ctx.interaction is HoldInteraction)
        { CanceledHoldSkill(); }
        else if (ctx.interaction is SlowTapInteraction)
        { CanceledSlowTapSkill(); }
        else if (ctx.interaction is PNRepeatInteraction)
        { CanceledRepeatSkill(); }
    }
    protected virtual void StartTapSkill() { }
    protected virtual void TapSkill() { }
    protected virtual void CanceledTapSkill() { }
    protected virtual void EnterTapSkill() { }
    protected virtual void ExitTapSkill() { }
    protected virtual void StartHoldSkill() { }
    protected virtual void HoldSkill() { }
    protected virtual void CanceledHoldSkill() { }
    protected virtual void EnterHoldSkill() { }
    protected virtual void ExitHoldSkill() { }
    protected virtual void StartSlowTapSkill() { }
    protected virtual void SlowTapSkill() { }
    protected virtual void CanceledSlowTapSkill() { }
    protected virtual void EnterSlowTapSkill() { }
    protected virtual void ExitSlowTapSkill() { }
    protected virtual void StartRepeatSkill() { }
    protected virtual void RepeatSkill() { }
    protected virtual void CanceledRepeatSkill() { }
    protected virtual void StartedUlti(InputAction.CallbackContext ctx)
    {
        if (!CanInput || !isActiveAndEnabled) return;
        if (ctx.interaction is TapInteraction || ctx.interaction is PressInteraction)
        { StartTapUlti(); }
        else if (ctx.interaction is HoldInteraction)
        { StartHoldUlti(); }
        else if (ctx.interaction is SlowTapInteraction)
        { StartSlowTapUlti(); }
        else if (ctx.interaction is PNRepeatInteraction)
        { StartRepeatUlti(); }
    }
    protected virtual void PerformedUlti(InputAction.CallbackContext ctx)
    {
        if (!CanInput || !isActiveAndEnabled) return;
        if (ctx.interaction is TapInteraction || ctx.interaction is PressInteraction)
        { EnterTapUlti(); TapUlti(); ExitTapUlti(); }
        else if (ctx.interaction is HoldInteraction)
        { EnterHoldUlti(); HoldUlti(); ExitHoldUlti(); }
        else if (ctx.interaction is SlowTapInteraction)
        { EnterSlowTapUlti(); SlowTapUlti(); ExitSlowTapUlti(); }
        else if (ctx.interaction is PNRepeatInteraction)
        { RepeatUlti(); }
    }
    protected virtual void CanceledUlti(InputAction.CallbackContext ctx)
    {
        if (ctx.interaction is TapInteraction || ctx.interaction is PressInteraction)
        { CanceledTapUlti(); }
        else if (ctx.interaction is HoldInteraction)
        { CanceledHoldUlti(); }
        else if (ctx.interaction is SlowTapInteraction)
        { CanceledSlowTapUlti(); }
        else if (ctx.interaction is PNRepeatInteraction)
        { CanceledRepeatUlti(); }
    }
    protected virtual void StartTapUlti() { }
    protected virtual void TapUlti() { }
    protected virtual void CanceledTapUlti() { }
    protected virtual void EnterTapUlti() { }
    protected virtual void ExitTapUlti() { }
    protected virtual void StartHoldUlti() { }
    protected virtual void HoldUlti() { }
    protected virtual void CanceledHoldUlti() { }
    protected virtual void EnterHoldUlti() { }
    protected virtual void ExitHoldUlti() { }
    protected virtual void StartSlowTapUlti() { }
    protected virtual void SlowTapUlti() { }
    protected virtual void CanceledSlowTapUlti() { }
    protected virtual void EnterSlowTapUlti() { }
    protected virtual void ExitSlowTapUlti() { }
    protected virtual void StartRepeatUlti() { }
    protected virtual void RepeatUlti() { }
    protected virtual void CanceledRepeatUlti() { }
    protected virtual void StartedDash(InputAction.CallbackContext ctx)
    {
        if (!CanInput || !isActiveAndEnabled) return;
        if (ctx.interaction is TapInteraction || ctx.interaction is PressInteraction)
        { StartTapDash(); }
        else if (ctx.interaction is HoldInteraction)
        { StartHoldDash(); }
        else if (ctx.interaction is SlowTapInteraction)
        { StartSlowTapDash(); }
        else if (ctx.interaction is PNRepeatInteraction)
        { StartRepeatDash(); }
    }
    protected virtual void PerformedDash(InputAction.CallbackContext ctx)
    {
        if (!CanInput || !isActiveAndEnabled) return;
        if (ctx.interaction is TapInteraction || ctx.interaction is PressInteraction)
        { EnterTapDash(); TapDash(); ExitTapDash(); }
        else if (ctx.interaction is HoldInteraction)
        { EnterHoldDash(); HoldDash(); ExitHoldDash(); }
        else if (ctx.interaction is SlowTapInteraction)
        { EnterSlowTapDash(); SlowTapDash(); ExitSlowTapDash(); }
        else if (ctx.interaction is PNRepeatInteraction)
        { RepeatDash(); }
    }
    protected virtual void CanceledDash(InputAction.CallbackContext ctx)
    {
        if (ctx.interaction is TapInteraction || ctx.interaction is PressInteraction)
        { CanceledTapDash(); }
        else if (ctx.interaction is HoldInteraction)
        { CanceledHoldDash(); }
        else if (ctx.interaction is SlowTapInteraction)
        { CanceledSlowTapDash(); }
        else if (ctx.interaction is PNRepeatInteraction)
        { CanceledRepeatDash(); }
    }
    protected virtual void StartTapDash() { }
    protected virtual void TapDash() => TryDash();
    protected virtual void CanceledTapDash() { }
    protected virtual void EnterTapDash() { }
    protected virtual void ExitTapDash() { }
    protected virtual void StartHoldDash() { }
    protected virtual void HoldDash() { }
    protected virtual void CanceledHoldDash() { }
    protected virtual void EnterHoldDash() { }
    protected virtual void ExitHoldDash() { }
    protected virtual void StartSlowTapDash() { }
    protected virtual void SlowTapDash() { }
    protected virtual void CanceledSlowTapDash() { }
    protected virtual void EnterSlowTapDash() { }
    protected virtual void ExitSlowTapDash() { }
    protected virtual void StartRepeatDash() { }
    protected virtual void RepeatDash() { }
    protected virtual void CanceledRepeatDash() { }
    protected virtual void StartedBurst(InputAction.CallbackContext ctx)
    {
        if (!CanInput || !isActiveAndEnabled) return;
        if (ctx.interaction is TapInteraction || ctx.interaction is PressInteraction)
        { StartTapBurst(); }
        else if (ctx.interaction is HoldInteraction)
        { StartHoldBurst(); }
        else if (ctx.interaction is SlowTapInteraction)
        { StartSlowTapBurst(); }
        else if (ctx.interaction is PNRepeatInteraction)
        { StartRepeatBurst(); }
    }
    protected virtual void PerformedBurst(InputAction.CallbackContext ctx)
    {
        if (!CanInput || !isActiveAndEnabled) return;
        if (ctx.interaction is TapInteraction || ctx.interaction is PressInteraction)
        { EnterTapBurst(); TapBurst(); ExitTapBurst(); }
        else if (ctx.interaction is HoldInteraction)
        { EnterHoldBurst(); HoldBurst(); ExitHoldBurst(); }
        else if (ctx.interaction is SlowTapInteraction)
        { EnterSlowTapBurst(); SlowTapBurst(); ExitSlowTapBurst(); }
        else if (ctx.interaction is PNRepeatInteraction)
        { RepeatBurst(); }
    }
    protected virtual void CanceledBurst(InputAction.CallbackContext ctx)
    {
        if (ctx.interaction is TapInteraction || ctx.interaction is PressInteraction)
        { CanceledTapBurst(); }
        else if (ctx.interaction is HoldInteraction)
        { CanceledHoldBurst(); }
        else if (ctx.interaction is SlowTapInteraction)
        { CanceledSlowTapBurst(); }
        else if (ctx.interaction is PNRepeatInteraction)
        { CanceledRepeatBurst(); }
    }
    protected virtual void StartTapBurst() { }
    protected virtual void TapBurst() { }
    protected virtual void CanceledTapBurst() { }
    protected virtual void EnterTapBurst() { }
    protected virtual void ExitTapBurst() { }
    protected virtual void StartHoldBurst() { }
    protected virtual void HoldBurst() { }
    protected virtual void CanceledHoldBurst() { }
    protected virtual void EnterHoldBurst() { }
    protected virtual void ExitHoldBurst() { }
    protected virtual void StartSlowTapBurst() { }
    protected virtual void SlowTapBurst() { }
    protected virtual void CanceledSlowTapBurst() { }
    protected virtual void EnterSlowTapBurst() { }
    protected virtual void ExitSlowTapBurst() { }
    protected virtual void StartRepeatBurst() { }
    protected virtual void RepeatBurst() { }
    protected virtual void CanceledRepeatBurst() { }
}

