using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Interactions;
#if UNITY_5_3_OR_NEWER
using UnityEngine.InputSystem.Controls;
#endif

public partial class PNPlayerSkill
{
    protected virtual EveId DefaultDashBindingProfile=>EveId.Parse("project_n:binding_player_dash");
    protected override bool AllowsBindingInput=>CanInput&&isActiveAndEnabled;
    private readonly HashSet<EveId> bindingNeutral=new HashSet<EveId>();
    private InputAction SlotAction(EveId slot)=>slot==PNSkillSlots.Attack?pc.Controller.Attack:slot==PNSkillSlots.Skill?pc.Controller.Skill:slot==PNSkillSlots.Ultimate?pc.Controller.Ulti:slot==PNSkillSlots.Dash?pc.Controller.Dash:slot==PNSkillSlots.Burst?pc.Controller.Burst:null;
    private PlayerSkillInputConfig SlotConfig(EveId slot)=>slot==PNSkillSlots.Attack?AttackInput:slot==PNSkillSlots.Skill?SkillInput:slot==PNSkillSlots.Ultimate?UltiInput:slot==PNSkillSlots.Dash?DashInput:BurstInput;
    private PlayerSkillInputConfig BindingInput(EveId slot,PlayerSkillInputConfig fallback)
        =>Bindings!=null&&Bindings.TryGet(slot,out var profile)?BindingProfile(profile).Input:fallback;
    private void RefreshBindingInput(PNSkillBindingChange change)
    {
        if(pc==null)return;
        foreach(var slot in change.Previous.Keys.Where(x=>change.Previous[x]!=change.Current[x]))
        {
            var action=SlotAction(slot);if(action==null)continue;
#if UNITY_5_3_OR_NEWER
            if(action.controls.OfType<ButtonControl>().Any(x=>x.isPressed))bindingNeutral.Add(slot);else bindingNeutral.Remove(slot);
#else
            bindingNeutral.Remove(slot);
#endif
            action.Disable();Apply(action,BindingInput(slot,SlotConfig(slot)));
        }
    }
    protected override void BeforeBindingTick()
    {
#if UNITY_5_3_OR_NEWER
        if(pc!=null)foreach(var slot in bindingNeutral.ToArray()){var action=SlotAction(slot);if(action==null||!action.controls.OfType<ButtonControl>().Any(x=>x.isPressed))bindingNeutral.Remove(slot);}
#else
        bindingNeutral.Clear();
#endif
    }
    private bool RouteBindingInput(EveId slot,InputAction.CallbackContext ctx,PNSkillInputPhase phase)
    {
        if(Bindings==null||!Bindings.TryGet(slot,out _))return false;
        if(phase!=PNSkillInputPhase.Canceled&&(!CanInput||!isActiveAndEnabled||bindingNeutral.Contains(slot)))return true;
        var interaction=ctx.interaction is PNRepeatInteraction?PNSkillInteraction.Repeat:ctx.interaction is HoldInteraction?PNSkillInteraction.Hold:ctx.interaction is SlowTapInteraction?PNSkillInteraction.SlowTap:PNSkillInteraction.Tap;
        RequestSlot(slot,new PNSkillInputEvent(phase,interaction));return true;
    }
    // Legacy adapter invokes semantic hooks, never calls the InputAction callbacks again.
    public void DispatchLegacyBindingInput(EveId slot,PNSkillInputEvent input)
    {
        if(!IsBindingInputDispatch)throw new InvalidOperationException("Legacy dispatch requires a binding input owner.");
        bool start=input.Phase==PNSkillInputPhase.Started,cancel=input.Phase==PNSkillInputPhase.Canceled;
        if(slot==PNSkillSlots.Attack){switch(input.Interaction){case PNSkillInteraction.Tap:if(start)StartTapAttack();else if(cancel)CanceledTapAttack();else{EnterTapAttack();TapAttack();ExitTapAttack();}break;case PNSkillInteraction.Hold:if(start)StartHoldAttack();else if(cancel)CanceledHoldAttack();else{EnterHoldAttack();HoldAttack();ExitHoldAttack();}break;case PNSkillInteraction.SlowTap:if(start)StartSlowTapAttack();else if(cancel)CanceledSlowTapAttack();else{EnterSlowTapAttack();SlowTapAttack();ExitSlowTapAttack();}break;default:if(start)StartRepeatAttack();else if(cancel)CanceledRepeatAttack();else RepeatAttack();break;}}
        else if(slot==PNSkillSlots.Skill){switch(input.Interaction){case PNSkillInteraction.Tap:if(start)StartTapSkill();else if(cancel)CanceledTapSkill();else{EnterTapSkill();TapSkill();ExitTapSkill();}break;case PNSkillInteraction.Hold:if(start)StartHoldSkill();else if(cancel)CanceledHoldSkill();else{EnterHoldSkill();HoldSkill();ExitHoldSkill();}break;case PNSkillInteraction.SlowTap:if(start)StartSlowTapSkill();else if(cancel)CanceledSlowTapSkill();else{EnterSlowTapSkill();SlowTapSkill();ExitSlowTapSkill();}break;default:if(start)StartRepeatSkill();else if(cancel)CanceledRepeatSkill();else RepeatSkill();break;}}
        else if(slot==PNSkillSlots.Ultimate){switch(input.Interaction){case PNSkillInteraction.Tap:if(start)StartTapUlti();else if(cancel)CanceledTapUlti();else{EnterTapUlti();TapUlti();ExitTapUlti();}break;case PNSkillInteraction.Hold:if(start)StartHoldUlti();else if(cancel)CanceledHoldUlti();else{EnterHoldUlti();HoldUlti();ExitHoldUlti();}break;case PNSkillInteraction.SlowTap:if(start)StartSlowTapUlti();else if(cancel)CanceledSlowTapUlti();else{EnterSlowTapUlti();SlowTapUlti();ExitSlowTapUlti();}break;default:if(start)StartRepeatUlti();else if(cancel)CanceledRepeatUlti();else RepeatUlti();break;}}
        else if(slot==PNSkillSlots.Dash){switch(input.Interaction){case PNSkillInteraction.Tap:if(start)StartTapDash();else if(cancel)CanceledTapDash();else{EnterTapDash();TapDash();ExitTapDash();}break;case PNSkillInteraction.Hold:if(start)StartHoldDash();else if(cancel)CanceledHoldDash();else{EnterHoldDash();HoldDash();ExitHoldDash();}break;case PNSkillInteraction.SlowTap:if(start)StartSlowTapDash();else if(cancel)CanceledSlowTapDash();else{EnterSlowTapDash();SlowTapDash();ExitSlowTapDash();}break;default:if(start)StartRepeatDash();else if(cancel)CanceledRepeatDash();else RepeatDash();break;}}
        else if(slot==PNSkillSlots.Burst){switch(input.Interaction){case PNSkillInteraction.Tap:if(start)StartTapBurst();else if(cancel)CanceledTapBurst();else{EnterTapBurst();TapBurst();ExitTapBurst();}break;case PNSkillInteraction.Hold:if(start)StartHoldBurst();else if(cancel)CanceledHoldBurst();else{EnterHoldBurst();HoldBurst();ExitHoldBurst();}break;case PNSkillInteraction.SlowTap:if(start)StartSlowTapBurst();else if(cancel)CanceledSlowTapBurst();else{EnterSlowTapBurst();SlowTapBurst();ExitSlowTapBurst();}break;default:if(start)StartRepeatBurst();else if(cancel)CanceledRepeatBurst();else RepeatBurst();break;}}
        TrackLegacyBindingExecution();
    }
    protected override bool LegacyBindingBusy=>IsDashing;
}
