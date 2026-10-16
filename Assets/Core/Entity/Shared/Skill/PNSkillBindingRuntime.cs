using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;

public partial class PNSkill
{
    private sealed class BehaviorState:IDisposable
    {
        internal PNSkillBindingProfile Profile;internal PNSkillBehaviorContext Context;internal PNSkillBehavior Behavior;internal PNBuffResourceScope Scope;internal bool Selected,Faulted;
        public void Dispose(){try{if(Selected){Selected=false;Context.IsSelected=false;Behavior?.SelectionChanged(false);}}finally{try{Behavior?.Dispose();}finally{Context.SelectionResources?.Dispose();Scope?.Dispose();if(Scope?.CleanupFailed==true||Context.SelectionResources?.CleanupFailed==true)throw new InvalidOperationException("Skill behavior resource cleanup failed.");}}}
    }
    private readonly Dictionary<EveId,BehaviorState> bindingBehaviors=new Dictionary<EveId,BehaviorState>();
    private readonly Dictionary<(EveId Profile,EveId Slot),BehaviorState> slotBehaviors=new Dictionary<(EveId,EveId),BehaviorState>();
    private readonly Dictionary<EveId,BehaviorState> gestures=new Dictionary<EveId,BehaviorState>();
    private readonly Dictionary<EveId,PNSkillInteraction> gestureInteractions=new Dictionary<EveId,PNSkillInteraction>();
    private readonly Dictionary<EveId,string> stateSchemas=new Dictionary<EveId,string>();
    private readonly Dictionary<EveId,bool> stateDeathPolicies=new Dictionary<EveId,bool>();
    private readonly HashSet<EveId> bindingAbilityIds=new HashSet<EveId>();
    private readonly Queue<PNSkillBindingChange> bindingChanges=new Queue<PNSkillBindingChange>();
    private BehaviorState inputBehavior,executingBehavior;
    private EveId inputSlot,executingSlot;
    private PNSkillInputPhase inputPhase;
    protected bool IsBindingInputDispatch=>inputBehavior!=null;
    private bool bindingsTicking,bindingsDisposed,bindingsSuspended;
    public PNSkillBindingState Bindings {get;private set;}
    public PNSkillBindingProfile BindingProfile(EveId id)=>registry.Get<PNSkillBindingProfile>().GetEffective(id);
    public EveId ExecutingProfile=>executingBehavior?.Profile.Id??default;
    public event Action<PNSkillBindingChange> BindingChanged;
    private IReadOnlyCollection<EveId> bindingAbilityView=Array.Empty<EveId>(),selectedAbilityView=Array.Empty<EveId>();
    public IReadOnlyCollection<EveId> BindingAbilityIds=>bindingAbilityView;
    public IReadOnlyCollection<EveId> SelectedBindingAbilityIds=>selectedAbilityView;
    private void RefreshBindingView()
    {
        foreach(var id in bindingBehaviors.Values.SelectMany(x=>x.Profile.DisplayAbilityIds))bindingAbilityIds.Add(id);
        bindingAbilityView=Array.AsReadOnly(bindingAbilityIds.ToArray());
        selectedAbilityView=Bindings==null?Array.Empty<EveId>():Array.AsReadOnly(Bindings.Snapshot().Values.SelectMany(id=>BindingProfile(id).DisplayAbilityIds).Distinct().ToArray());
    }
    protected virtual bool CanBeginBindingExecution=>CurrentInvocation==null;
    protected virtual bool UsesCharacterBindings=>false;
    protected virtual bool AllowsBindingInput=>isActiveAndEnabled;
    protected virtual bool LegacyBindingBusy=>false;
    protected virtual void BeforeBindingTick() { }
    protected virtual void CancelLegacyBindingExecution()=>TryInterrupt(EveId.Parse("project_n:binding_changed"));
    public virtual void CommitEntity(EveEntityLoadContext context) { }
    public virtual void EntityPublished(EveEntityLoadContext context){EnsureSelectedBindingAbilities();UpdateBindingSelections();}
    private void UpdateBindingSelections()
    {
        if(Bindings==null||!IsReady||!HasGameplayAuthority||!isActiveAndEnabled||GetComponent<PNStat>()?.IsDead==true)return;
        bindingsSuspended=false;
        foreach(var pair in slotBehaviors.ToArray())
        {
            var value=pair.Value;bool selected=Bindings.TryGet(pair.Key.Slot,out var id)&&BindingProfile(id).Id==pair.Key.Profile;
            if(value.Selected==selected)continue;value.Selected=selected;value.Context.IsSelected=selected;
            try
            {
                if(selected)value.Context.SelectionResources=new PNBuffResourceScope(BindingError);
                try{if(!value.Faulted)value.Behavior.SelectionChanged(selected);}
                finally{if(!selected){value.Context.SelectionResources?.Dispose();value.Context.SelectionResources=null;}}
            }
            catch(Exception error){value.Faulted=true;value.Context.SelectionResources?.Dispose();value.Context.SelectionResources=null;if(ReferenceEquals(executingBehavior,value))CancelBindingExecution();BindingError(error);}
        }
    }
    private void SuspendBindingSelections()
    {
        bindingsSuspended=true;
        foreach(var value in slotBehaviors.Values.Where(x=>x.Selected).ToArray())
        {
            value.Selected=false;value.Context.IsSelected=false;
            try{value.Behavior.SelectionChanged(false);}catch(Exception e){value.Faulted=true;BindingError(e);}
            finally{value.Context.SelectionResources?.Dispose();value.Context.SelectionResources=null;}
        }
    }
    protected virtual void OnEnable()=>UpdateBindingSelections();
    private void EnsureSelectedBindingAbilities()
    {
        if(Bindings==null||!IsReady||!HasGameplayAuthority)return;
        foreach(var id in Bindings.Snapshot().Values.Distinct())
        {
            var profile=BindingProfile(id);
            foreach(var definition in profile.Abilities)
            {
                if(!Abilities.Instances.Any(x=>x.Id==definition.Id))Abilities.Define(definition);
                stateSchemas[definition.Id]=PNSkillBindingProfile.AbilitySchema(definition);bindingAbilityIds.Add(definition.Id);
            }
        }
        RefreshBindingView();
    }
    private void LoadBindings(EveId character)
    {
        bindingsDisposed=false;
        if(!UsesCharacterBindings||!registry.Get<PNSkillBaseBindings>().TryGet(character,out var selected))return;
        ConfigureBindings(selected.Slots);
    }
    protected void ConfigureBindings(IEnumerable<KeyValuePair<EveId,EveId>> slots)
    {
        if(Bindings!=null)throw new InvalidOperationException("Bindings already configured.");
        bindingsDisposed=false;
        Bindings=new PNSkillBindingState(slots,ValidateBindingProfile,()=>IsReady&&HasGameplayAuthority,PrepareBindingProfiles);
        Bindings.Changed+=BindingsChanged;Bindings.Failed+=BindingError;
        RefreshBindingView();
        var stat=GetComponent<PNStat>();if(stat!=null)stat.Died+=BindingDeath;
    }
    private void ValidateBindingProfile(EveId id)
    {
        var profile=registry.Get<PNSkillBindingProfile>().GetEffective(id);profile.Validate(registry);
        foreach(var display in profile.DisplayAbilityIds)if(!profile.Abilities.Any(x=>x.Id==display)&&!Abilities.Instances.Any(x=>x.Id==display))throw new InvalidOperationException("Missing displayed profile Ability: "+display);
        // Validate the complete Ability requirement set before defining any of them.
        foreach(var definition in profile.Abilities)
        {
            string schema=PNSkillBindingProfile.AbilitySchema(definition);
            if(stateDeathPolicies.TryGetValue(definition.Id,out var reset)&&reset!=profile.ResetAbilitiesOnDeath)throw new InvalidOperationException("Conflicting shared Ability death policy: "+definition.Id);
            var existing=Abilities.Instances.Where(x=>x.Id==definition.Id).ToArray();
            if(existing.Any(x=>PNSkillBindingProfile.AbilitySchema(x.Definition)!=schema)||stateSchemas.TryGetValue(definition.Id,out var cached)&&cached!=schema)
                throw new InvalidOperationException("Incompatible profile Ability: "+definition.Id);
        }
    }
    private void PrepareBindingProfiles(IReadOnlyDictionary<EveId,EveId> mappings)
    {
        var created=new List<BehaviorState>();
        var stagedSlots=new List<(EveId Profile,EveId Slot,BehaviorState State)>();
        var schemas=new Dictionary<EveId,string>(stateSchemas);
        var deathPolicies=new Dictionary<EveId,bool>(stateDeathPolicies);
        try
        {
            foreach(var id in mappings.Values.Distinct())
            {
                var profile=BindingProfile(id);
                foreach(var definition in profile.Abilities)
                {
                    var schema=PNSkillBindingProfile.AbilitySchema(definition);
                    if(deathPolicies.TryGetValue(definition.Id,out var reset)&&reset!=profile.ResetAbilitiesOnDeath)throw new InvalidOperationException("Conflicting shared Ability death policy: "+definition.Id);
                    deathPolicies[definition.Id]=profile.ResetAbilitiesOnDeath;
                    if(schemas.TryGetValue(definition.Id,out var previous)&&previous!=schema)throw new InvalidOperationException("Conflicting profile Ability: "+definition.Id);
                    schemas[definition.Id]=schema;
                }
                if(bindingBehaviors.ContainsKey(profile.Id)||created.Any(x=>x.Profile.Id==profile.Id))continue;
                var scope=new PNBuffResourceScope(BindingError);var context=new PNSkillBehaviorContext(this,profile,registry,scope);var value=new BehaviorState{Profile=profile,Context=context,Scope=scope};created.Add(value);
                value.Behavior=registry.Get<PNSkillBehaviorDefinition>().GetEffective(profile.BehaviorId).Create(context);
            }
            foreach(var mapping in mappings.Where(x=>x.Key.IsValid))
            {
                var profile=BindingProfile(mapping.Value);
                if(slotBehaviors.ContainsKey((profile.Id,mapping.Key)))continue;
                var scope=new PNBuffResourceScope(BindingError);var context=new PNSkillBehaviorContext(this,profile,registry,scope,mapping.Key);
                var value=new BehaviorState{Profile=profile,Context=context,Scope=scope};created.Add(value);
                value.Behavior=registry.Get<PNSkillBehaviorDefinition>().GetEffective(profile.BehaviorId).Create(context);
                stagedSlots.Add((profile.Id,mapping.Key,value));
            }
        }
        catch{foreach(var value in created)try{value.Dispose();}catch(Exception e){BindingError(e);}throw;}
        foreach(var value in created.Where(x=>!stagedSlots.Any(slot=>ReferenceEquals(slot.State,x))))bindingBehaviors.Add(value.Profile.Id,value);
        foreach(var slot in stagedSlots)slotBehaviors.Add((slot.Profile,slot.Slot),slot.State);
        foreach(var pair in schemas)stateSchemas[pair.Key]=pair.Value;
        foreach(var pair in deathPolicies)stateDeathPolicies[pair.Key]=pair.Value;
    }
    private BehaviorState Behavior(EveId id,EveId slot=default)
    {
        var profile=registry.Get<PNSkillBindingProfile>().GetEffective(id);ValidateBindingProfile(id);PrepareBindingProfiles(new Dictionary<EveId,EveId>{{slot,id}});
        foreach(var definition in profile.Abilities)
        {
            if(!Abilities.Instances.Any(x=>x.Id==definition.Id))Abilities.Define(definition);
            stateSchemas[definition.Id]=PNSkillBindingProfile.AbilitySchema(definition);bindingAbilityIds.Add(definition.Id);
        }
        if(!slot.IsValid)return bindingBehaviors[profile.Id];
        if(!slotBehaviors.TryGetValue((profile.Id,slot),out var value))
        {
            var scope=new PNBuffResourceScope(BindingError);var context=new PNSkillBehaviorContext(this,profile,registry,scope);value=new BehaviorState{Profile=profile,Context=context,Scope=scope};
            try{value.Behavior=registry.Get<PNSkillBehaviorDefinition>().GetEffective(profile.BehaviorId).Create(context);slotBehaviors.Add((profile.Id,slot),value);}catch{value.Dispose();throw;}
        }
        return value;
    }
    public bool RequestSlot(EveId slot,PNSkillInputEvent input)
    {
        if(Bindings==null||Bindings.IsFaulted||!IsReady||!HasGameplayAuthority||bindingsDisposed||input.Phase!=PNSkillInputPhase.Canceled&&!AllowsBindingInput)return false;
        if(!Bindings.TryGet(slot,out var profile))return false;
        BehaviorState value;
        if(input.Phase==PNSkillInputPhase.Started){if(gestures.ContainsKey(slot))CancelGesture(slot,false);value=Behavior(profile,slot);gestures[slot]=value;gestureInteractions[slot]=input.Interaction;}
        else if(!gestures.TryGetValue(slot,out value))return false;
        bool result=SendInput(value,slot,input);
        if(input.Phase==PNSkillInputPhase.Canceled){gestures.Remove(slot);gestureInteractions.Remove(slot);}return result;
    }
    public bool RequestExact(EveId profile,PNSkillInputEvent input)
    {if(!IsReady||!HasGameplayAuthority||!AllowsBindingInput) return false;return SendInput(Behavior(profile),default,input);}
    private bool SendInput(BehaviorState value,EveId slot,PNSkillInputEvent input)
    {
        if(value.Faulted)return false;
        if(inputBehavior!=null)throw new InvalidOperationException("Reentrant Skill input.");inputBehavior=value;inputSlot=slot;inputPhase=input.Phase;
        try{value.Behavior.Input(input);return true;}catch(Exception error){if(ReferenceEquals(executingBehavior,value))CancelBindingExecution();BindingError(error);return false;}finally{inputBehavior=null;inputSlot=default;while(bindingChanges.Count>0)ApplyBindingChange(bindingChanges.Dequeue());}
    }
    private void CancelGesture(EveId slot,bool changed)
    {if(!gestures.TryGetValue(slot,out var owner))return;var interaction=gestureInteractions[slot];gestures.Remove(slot);gestureInteractions.Remove(slot);SendInput(owner,slot,new PNSkillInputEvent(PNSkillInputPhase.Canceled,interaction,changed));}
    private void BindingsChanged(PNSkillBindingChange change)
    {if(inputBehavior!=null){bindingChanges.Enqueue(change);return;}ApplyBindingChange(change);}
    private void ApplyBindingChange(PNSkillBindingChange change)
    {
        EnsureSelectedBindingAbilities();
        RefreshBindingView();
        foreach(var slot in change.Previous.Keys.Where(slot=>change.Current[slot]!=change.Previous[slot]).ToArray())
        {
            CancelGesture(slot,true);
            if(executingSlot==slot&&change.Transition==PNSkillTransition.CancelCurrent)CancelBindingExecution();
        }
        UpdateBindingSelections();
        foreach(Action<PNSkillBindingChange> observer in BindingChanged?.GetInvocationList()??Array.Empty<Delegate>())try{observer(change);}catch(Exception e){BindingError(e);}
    }
    internal bool BeginBindingExecution(PNSkillBehaviorContext context)
    {
        if(inputBehavior==null||inputPhase==PNSkillInputPhase.Canceled||!ReferenceEquals(inputBehavior.Context,context)||executingBehavior!=null||!CanBeginBindingExecution||!AllowsBindingInput||inputSlot.IsValid&&(Bindings==null||!Bindings.TryGet(inputSlot,out var selected)||BindingProfile(selected).Id!=context.Profile.Id)||!CanActivateSkill(context.Profile.EntrySkillId))return false;
        BeginActivation(context.Profile.EntrySkillId);executingBehavior=inputBehavior;executingSlot=inputSlot;return true;
    }
    internal bool IsBindingContextExecuting(PNSkillBehaviorContext context)=>ReferenceEquals(executingBehavior?.Context,context);
    internal void CommitBindingExecution(PNSkillBehaviorContext context,IEnumerable<PNStat> targets,Vector3? direction)
    {if(executingBehavior==null||!ReferenceEquals(executingBehavior.Context,context))throw new InvalidOperationException("Execution owner mismatch.");CommitActivation(targets,direction);}
    internal void EndBindingExecution(PNSkillBehaviorContext context,bool cancelled)
    {if(executingBehavior==null||!ReferenceEquals(executingBehavior.Context,context))return;executingBehavior=null;executingSlot=default;EndActivation(cancelled);}
    protected void TrackLegacyBindingExecution()
    {if(inputBehavior!=null&&LegacyBindingBusy){executingBehavior=inputBehavior;executingSlot=inputSlot;}}
    public bool CancelBindingExecution()
    {
        var value=executingBehavior;if(value==null)return false;
        executingBehavior=null;executingSlot=default;
        try{value.Behavior.Cancel();}finally{EndActivation(true);}return true;
    }
    protected void AdvanceBindings(float delta)
    {
        BeforeBindingTick();if(bindingsTicking||bindingsDisposed||!IsReady||!HasGameplayAuthority)return;if(bindingsSuspended)UpdateBindingSelections();bindingsTicking=true;
        try{var value=executingBehavior;if(value!=null){value.Behavior.Advance(delta);if(ReferenceEquals(executingBehavior,value)&&CurrentInvocation==null&&!LegacyBindingBusy){executingBehavior=null;executingSlot=default;}}}
        catch(Exception error){CancelBindingExecution();BindingError(error);}finally{bindingsTicking=false;}
    }
    protected void CancelBindingInputs(){foreach(var slot in gestures.Keys.ToArray())CancelGesture(slot,false);CancelBindingExecution();}
    private void BindingDeath(PNDamageResult result)
    {
        try{CancelBindingInputs();}finally{SuspendBindingSelections();}
        foreach(var id in bindingBehaviors.Values.Where(x=>x.Profile.ResetAbilitiesOnDeath).SelectMany(x=>x.Profile.Abilities).Select(x=>x.Id).Distinct())foreach(var ability in Abilities.Instances.Where(x=>x.Id==id).ToArray())ability.Reset();
    }
    private void UnloadBindings()
    {
        var failures=new List<Exception>();
        bindingsDisposed=true;try{CancelBindingInputs();}finally{
            var stat=GetComponent<PNStat>();if(stat!=null)stat.Died-=BindingDeath;
            Bindings?.Dispose();Bindings=null;foreach(var value in bindingBehaviors.Values.Concat(slotBehaviors.Values))try{value.Dispose();}catch(Exception e){failures.Add(e);}bindingBehaviors.Clear();slotBehaviors.Clear();gestures.Clear();gestureInteractions.Clear();bindingChanges.Clear();stateSchemas.Clear();stateDeathPolicies.Clear();bindingAbilityIds.Clear();bindingAbilityView=selectedAbilityView=Array.Empty<EveId>();BindingChanged=null;
        }
        if(failures.Count>0)throw new AggregateException("Skill binding cleanup failed.",failures);
    }
    private void BindingError(Exception error)=>Debug.LogException(error,this);
}
