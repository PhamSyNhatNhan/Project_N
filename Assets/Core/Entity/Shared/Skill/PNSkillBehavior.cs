using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

public abstract class PNSkillBehavior:IDisposable
{
    public virtual void SelectionChanged(bool selected) { }
    public virtual void Input(PNSkillInputEvent input) { }
    public virtual void Advance(float delta) { }
    public virtual void Cancel() { }
    public virtual void Dispose() { }
}
public sealed class PNSkillBehaviorContext
{
    public PNSkill Skill {get;}
    public PNSkillBindingProfile Profile {get;}
    public EveRegistryHub Registry {get;}
    public PNAbilityCollection Abilities=>Skill.Abilities;
    public PNBuffResourceScope Resources {get;}
    public EveId SlotId {get;}
    public PNBuffResourceScope SelectionResources {get;internal set;}
    public bool IsSelected {get;internal set;}
    internal PNSkillBehaviorContext(PNSkill skill,PNSkillBindingProfile profile,EveRegistryHub registry,PNBuffResourceScope resources,EveId slot=default)
    {Skill=skill;Profile=profile;Registry=registry;Resources=resources;SlotId=slot;}
    public bool TryBegin()=>Skill.BeginBindingExecution(this);
    public void Commit(IEnumerable<PNStat> targets=null,Vector3? direction=null)=>Skill.CommitBindingExecution(this,targets,direction);
    public void Complete(bool cancelled=false)=>Skill.EndBindingExecution(this,cancelled);
    public bool IsExecuting=>Skill.IsBindingContextExecuting(this);
    public PNSkillData Data(EveId id)
    {if(!Profile.ActionDataIds.ContainsId(id,Registry))throw new InvalidOperationException("Undeclared behavior action data: "+id);return PNSkillData.Parse(Registry.Skills.GetEffective(id).Data);}
    public PNSkillAction CreateAction(EveId id,out PNSkillActionContext context,IEnumerable<PNStat> targets=null,Vector3? direction=null)
    {if(!IsExecuting)throw new InvalidOperationException("Behavior must own execution.");var data=Data(id);context=new PNSkillActionContext(Skill,Registry.Skills.ResolveIdentity(id),data,Registry,targets,direction);return Registry.Actions().Get(data.Action.Id).Create();}
}
internal static class PNSkillBindingIds
{
    internal static bool ContainsId(this IReadOnlyList<EveId> values,EveId id,EveRegistryHub hub)
    {var canonical=hub.Skills.ResolveIdentity(id);foreach(var value in values)if(hub.Skills.ResolveIdentity(value)==canonical)return true;return false;}
}
