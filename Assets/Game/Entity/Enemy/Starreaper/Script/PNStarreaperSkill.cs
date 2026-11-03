#if UNITY_5_3_OR_NEWER
using UnityEngine;
using System;
using System.Collections.Generic;
using EveEngine;

// Starreaper's enemy-skill owner. Phase policy (HP thresholds, behavior switches) belongs here, as PNSentinelSkill does; the shared owner knows none.
// It also keeps the small per-entity state of the afterimage: which side the next shadow's second arrow leans to (it alternates between uses) and the live shadow (at most one).
[DisallowMultipleComponent]
public class PNStarreaperSkill : PNEnemySkill
{
    public override PNSkillAction CreateAction(EveId id,out PNSkillActionContext context,IEnumerable<PNStat> targets,Vector3? direction,Vector3? position)
    {
        var loader=GetComponent<EveEntityLoader>();
        if(!IsReady||loader==null||!loader.IsReady||loader.LoadContext==null)throw new InvalidOperationException("Ready Starreaper required");
        var hub=loader.LoadContext.Registry;var selected=hub.Skills.ResolveIdentity(id);
        if(selected!=PNStarreaperCombatContent.Pierce)return base.CreateAction(id,out context,targets,direction,position);
        var weapon=GetComponent<PNStarreaperPresentation>();
        if(weapon==null)throw new InvalidOperationException("Starreaper requires bow presentation");
        var data=GetData(id);context=new BowContext(this,selected,data,hub,targets,direction,position,weapon);
        return hub.Actions().Get(data.Action.Id).Create();
    }
    private sealed class BowContext:PNSkillActionContext
    {
        private readonly PNStarreaperPresentation weapon;
        public override Vector3? Position=>weapon!=null?weapon.ProjectilePosition:throw new InvalidOperationException("Starreaper bow lost before launch");
        public BowContext(PNSkill skill,EveId id,PNSkillData data,EveRegistryHub hub,IEnumerable<PNStat> targets,Vector3? direction,Vector3? position,PNStarreaperPresentation weapon)
            :base(skill,id,data,hub,targets,direction,null,true,position){this.weapon=weapon;}
    }
    public int NextShadowSide { get; set; } = 1;
    public PNAreaHandle ShadowHandle { get; set; }
}
#endif
