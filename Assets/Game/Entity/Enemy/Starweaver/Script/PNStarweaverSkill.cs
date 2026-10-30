#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

[DisallowMultipleComponent]
public class PNStarweaverSkill : PNEnemySkill
{
    // Same selected registry Action/data and one invocation context; only evaluated Position follows the weapon.
    public override PNSkillAction CreateAction(EveId id,out PNSkillActionContext context,IEnumerable<PNStat> targets,Vector3? direction,Vector3? position)
    {
        var loader=GetComponent<EveEntityLoader>();
        if(!IsReady||loader==null||!loader.IsReady||loader.LoadContext==null)throw new InvalidOperationException("Ready loaded Starweaver required");
        var hub=loader.LoadContext.Registry;var selected=hub.Skills.ResolveIdentity(id);
        if(selected!=PNStarweaverCombatContent.Weave)return base.CreateAction(id,out context,targets,direction,position);
        var weapon=GetComponent<PNStarweaverPresentation>();
        if(weapon==null||!weapon.HasWeapon)throw new InvalidOperationException("Starweaver weave requires its golden needle");
        var data=GetData(id)??throw new InvalidOperationException("Starweaver weave action data missing");
        context=new NeedleContext(this,selected,data,hub,targets,direction,position,weapon);
        return hub.Actions().Get(data.Action.Id).Create();
    }
    private sealed class NeedleContext:PNSkillActionContext
    {
        private readonly PNStarweaverPresentation weapon;
        public override Vector3? Position=>weapon!=null?weapon.TipPosition:throw new InvalidOperationException("Starweaver needle lost before launch");
        public NeedleContext(PNSkill skill,EveId id,PNSkillData data,EveRegistryHub hub,IEnumerable<PNStat> targets,Vector3? direction,Vector3? position,PNStarweaverPresentation weapon)
            :base(skill,id,data,hub,targets,direction,null,true,position){this.weapon=weapon;}
    }
}
#endif
