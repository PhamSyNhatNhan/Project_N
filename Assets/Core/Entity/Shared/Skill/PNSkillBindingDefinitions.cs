using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class PNSkillSlots
{
    public static readonly EveId Attack=EveId.Parse("project_n:slot_attack"), Skill=EveId.Parse("project_n:slot_skill"),
        Ultimate=EveId.Parse("project_n:slot_ultimate"), Dash=EveId.Parse("project_n:slot_dash"), Burst=EveId.Parse("project_n:slot_burst");
    public static IReadOnlyList<EveId> Player => new[]{Attack,Skill,Ultimate,Dash,Burst};
}
public enum PNSkillInputPhase { Started, Performed, Canceled }
public enum PNSkillInteraction { Tap, Hold, SlowTap, Repeat }
public enum PNSkillTransition { FinishCurrent, CancelCurrent }
public readonly struct PNSkillInputEvent
{
    public PNSkillInputPhase Phase {get;}
    public PNSkillInteraction Interaction {get;}
    public bool BindingChanged {get;}
    public PNSkillInputEvent(PNSkillInputPhase phase,PNSkillInteraction interaction,bool bindingChanged=false)
    {Phase=phase;Interaction=interaction;BindingChanged=bindingChanged;}
}
public sealed class PNSkillBehaviorDefinition : EveIIdentified
{
    public EveId Id {get;}
    public EveId ImplementationId {get;}
    private readonly Func<PNSkillBehaviorContext,PNSkillBehavior> factory;
    private readonly Action<JToken,EveRegistryHub> validate;
    private readonly Action<PNContentReferenceContext,JToken> references;
    public PNSkillBehaviorDefinition(EveId id,EveId implementationId,Func<PNSkillBehaviorContext,PNSkillBehavior> factory,
        Action<JToken,EveRegistryHub> validate=null,Action<PNContentReferenceContext,JToken> validateReferences=null)
    {if(!id.IsValid||!implementationId.IsValid)throw new ArgumentException("Behavior identity required.");Id=id;ImplementationId=implementationId;this.factory=factory??throw new ArgumentNullException(nameof(factory));this.validate=validate;references=validateReferences;}
    public void Validate(PNSkillBindingProfile profile,EveRegistryHub hub,PNContentReferenceContext context=null)
    {validate?.Invoke(profile.Data,hub);references?.Invoke(context??new PNContentReferenceContext(hub),profile.Data);}
    internal void ValidatePayload(PNSkillBindingProfile profile,EveRegistryHub hub)=>validate?.Invoke(profile.Data,hub);
    public PNSkillBehavior Create(PNSkillBehaviorContext context)=>factory(context)??throw new InvalidOperationException("Skill behavior factory returned null.");
}
public sealed class PNSkillBindingProfile : EveIIdentified
{
    public EveId Id {get;}
    public EveId EntrySkillId {get;}
    public EveId BehaviorId {get;}
    private readonly JToken data;
    private readonly PlayerSkillInputConfig input;
    public JToken Data=>data?.DeepClone();
    public PlayerSkillInputConfig Input=>Copy(input);
    public IReadOnlyList<EveId> ActionDataIds {get;}
    public IReadOnlyList<PNAbilityDefinition> Abilities {get;}
    public IReadOnlyList<EveId> DisplayAbilityIds {get;}
    public bool ResetAbilitiesOnDeath {get;}
    [JsonIgnore] internal EveContentExtensionDocument SourceDocument {get;set;}
    public PNSkillBindingProfile(EveId id,EveId entrySkillId,EveId behaviorId,JToken data=null,PlayerSkillInputConfig input=null,
        IEnumerable<EveId> actionDataIds=null,IEnumerable<PNAbilityDefinition> abilities=null,bool resetAbilitiesOnDeath=true,IEnumerable<EveId> displayAbilityIds=null)
    {
        if(!id.IsValid||!entrySkillId.IsValid||!behaviorId.IsValid)throw new ArgumentException("Binding profile identities required.");
        Id=id;EntrySkillId=entrySkillId;BehaviorId=behaviorId;this.data=data?.DeepClone();this.input=Copy(input??new PlayerSkillInputConfig());this.input.Validate();
        ActionDataIds=Array.AsReadOnly((actionDataIds??Array.Empty<EveId>()).Distinct().ToArray());
        Abilities=Array.AsReadOnly((abilities??Array.Empty<PNAbilityDefinition>()).ToArray());ResetAbilitiesOnDeath=resetAbilitiesOnDeath;
        DisplayAbilityIds=Array.AsReadOnly(Abilities.Select(x=>x.Id).Concat(displayAbilityIds??Array.Empty<EveId>()).Distinct().ToArray());
        if(Abilities.Any(x=>x==null)||Abilities.Select(x=>x.Id).Distinct().Count()!=Abilities.Count)throw new ArgumentException("Duplicate/null profile Ability.");
    }
    public static PlayerSkillInputConfig Copy(PlayerSkillInputConfig x)=>new PlayerSkillInputConfig{CanTap=x.CanTap,PressImmediately=x.PressImmediately,HoldMode=x.HoldMode,TapDuration=x.TapDuration,HoldDuration=x.HoldDuration,SlowTapDuration=x.SlowTapDuration,RepeatDelay=x.RepeatDelay,RepeatInterval=x.RepeatInterval};
    public void Validate(EveRegistryHub hub)
    {
        var refs=new PNContentReferenceContext(hub,SourceDocument);
        refs.Require<EveSkillDefinition>(EntrySkillId,"entrySkillId");refs.Require<PNSkillBehaviorDefinition>(BehaviorId,"behaviorId");foreach(var id in ActionDataIds)refs.Require<EveSkillDefinition>(id,"actionDataIds");
        PNSkillMetadata.Parse(hub.Skills.Get(EntrySkillId).Data,hub);
        foreach(var id in ActionDataIds){var parsed=PNSkillData.Parse(hub.Skills.Get(id).Data);if(parsed==null)throw new FormatException("Action data required: "+id);hub.Actions().Get(parsed.Action.Id).ValidatePayload(parsed.Action,hub);}
        hub.Get<PNSkillBehaviorDefinition>().Get(BehaviorId).Validate(this,hub,refs);
    }
    internal static string AbilitySchema(PNAbilityDefinition definition)=>JsonConvert.SerializeObject(definition);
}
public sealed class PNSkillBaseBindings : EveIIdentified
{
    public EveId Id {get;}
    public IReadOnlyDictionary<EveId,EveId> Slots {get;}
    public PNSkillBaseBindings(EveId character,IEnumerable<KeyValuePair<EveId,EveId>> slots)
    {if(!character.IsValid)throw new ArgumentException("Character required.");Id=character;var values=new Dictionary<EveId,EveId>();foreach(var pair in slots){if(!pair.Key.IsValid||!pair.Value.IsValid)throw new ArgumentException("Slot/profile required.");values.Add(pair.Key,pair.Value);}Slots=new System.Collections.ObjectModel.ReadOnlyDictionary<EveId,EveId>(values);}
}
