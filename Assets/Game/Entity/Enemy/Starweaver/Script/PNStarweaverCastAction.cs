#if UNITY_5_3_OR_NEWER
using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class PNStarweaverCastAction : PNSkillAction
{
    private PNSkillActionContext context; private float age,windup,recovery; private bool fired,ended;
    private PNStarweaverPresentation presentation;
    private PNSkillOperation operation; private PNStarweaverTelegraph marker; private PNVisualBody body; private PNStarweaverCastVisual castVisual;
    public override bool HasAuxiliaryTimeline=>true;
    public override void Start(PNSkillActionContext c)
    {
        if(context!=null||ended)throw new InvalidOperationException("Fresh cast required");
        PNStarweaverCombatContent.ValidatePayload(c.Data.Action.Perform,c.Registry);
        context=c;var p=c.Data.Action.Perform;windup=p["windup"].Value<float>();recovery=p["recovery"].Value<float>();
        presentation=c.Skill.GetComponent<PNStarweaverPresentation>();
        c.BeginAction();
        // the puppet's cast motion (cast_weave / cast_constellation) lasts the whole cast; the body releases it at the end or on a Lorica break
        body=c.Skill.GetComponent<PNVisualBody>(); body?.PlayAction(c.SkillId==PNStarweaverCombatContent.Triangle?"cast_constellation":"cast_weave",windup+recovery);
        castVisual=PNStarweaverCastVisual.Create(c.Skill.transform,c.Skill.GetComponent<PNStarweaverPresentation>(),body!=null?body.Actor:null,c.SkillId==PNStarweaverCombatContent.Triangle,windup,c.Direction.Value);
        if(c.SkillId==PNStarweaverCombatContent.Triangle)
        {
            if(!c.Position.HasValue)throw new InvalidOperationException("Triangle center required");
            marker=new PNStarweaverTelegraph(c.Position.Value,p["data"]["radius"].Value<float>(),c.Skill.gameObject.scene,windup);
        }
    }
    public override void Perform(PNSkillActionContext c) { }
    public override bool AdvanceAuxiliary(float dt)
    {
        if(ended)return true;age+=dt;
        marker?.SetPhase(age/windup,age>=windup);castVisual?.SetTime(age);
        if(!fired&&age>=windup)
        {
            fired=true;var p=context.Data.Action.Perform;
            // Evaluate the same release pose that the caster flash and the context muzzle read.
            presentation?.TickWeapon(0);castVisual?.Align();
            operation=CreateOperation(context,EveId.Parse(p["operationId"].Value<string>()));
            operation.Execute(context,p["data"]);

        }
        return age>=windup+recovery;
    }
    public override void Exit(PNSkillActionContext c){try{c.CompleteAction();}finally{Dispose();}}
    public override void Cancel(PNSkillActionContext c){try{c.CancelEffects();operation?.Cancel();}finally{Dispose();}}
    public override void Dispose(){if(ended)return;ended=true;castVisual?.Dispose();castVisual=null;body?.EndAction();body=null;operation?.Dispose();operation=null;marker?.Dispose();marker=null;context=null;}
}

public sealed class PNStarweaverTriangleOperation:PNSkillOperation
{
    private bool executed;
    public override void Execute(PNSkillActionContext c,JToken data)
    {
        if(executed)throw new InvalidOperationException("Pulse already spent");executed=true;
        if(!EveRegistrySession.Context.HasAuthority)return;
        var center=c.Position.Value;float radius=data["radius"].Value<float>(),height=data["height"].Value<float>();
        var caster=c.Skill.GetComponent<PNStat>();double amount=0;foreach(var v in PNSkillValues.Parse(data["value"]).Components)amount+=v.Evaluate(caster);
        var capture=caster.CaptureCombatSource();var tags=c.EffectiveTags().Concat(data["tags"].Select(x=>EveId.Parse(x.Value<string>()))).Distinct().ToArray();
        var prepared=capture.PrepareDamage(amount,data["canCrit"].Value<bool>(),0,tags);
        foreach(var entity in c.World.Session.Entities.Snapshot(c.World))
        {
            var target=entity.GetComponent<PNStat>();if(target==null||!target.IsReady||target.IsDead||!caster.Targets(target)||!PNWorldGameplay.CanTarget(c.Skill,target))continue;
            var col=target.GetComponent<Collider>();if(col==null)continue;var b=col.bounds;
            if(b.max.y<center.y-height||b.min.y>center.y+height)continue;
            if(!PNStarweaverTriangleGeometry.Intersects(center.x,center.z,radius,b.min.x,b.min.z,b.max.x,b.max.z))continue;
            c.ReceiveHit(target,target.transform.position,capture.CreateDamage(prepared),amount,capture);
        }
    }
}
public sealed class PNStarweaverProjectile:PNDirectionalProjectile
{
    private PNStarweaverNeedleVisual visual; private Vector3 contactPoint,environmentPoint;
    public override void OnPoolCreated(){base.OnPoolCreated();visual=PNStarweaverNeedleVisual.Create(this);}
    protected override void Initialize(PNProjectileSpawn spawn){base.Initialize(spawn);contactPoint=environmentPoint=spawn.Position;visual?.Begin(spawn,(float)Launch.ScaleFactor);}
    protected override void OnContacts(PNProjectileContacts contacts)
    {if(contacts.HasEnvironment)environmentPoint=contacts.Environment.Point;base.OnContacts(contacts);}
    protected override void OnTargets(System.Collections.Generic.IReadOnlyList<PNProjectileContact> targets)
    {if(targets.Count>0)contactPoint=targets[0].Point;base.OnTargets(targets);}
    protected override void OnEnd(PNProjectileEndReason reason)
    {
        if(reason==PNProjectileEndReason.Hit||reason==PNProjectileEndReason.Environment)visual?.Contact(reason==PNProjectileEndReason.Environment?environmentPoint:contactPoint);
        base.OnEnd(reason);
    }
    protected override void Cleanup(){try{visual?.End();}finally{base.Cleanup();}}
    public override void DisposePooledResources(){visual?.Dispose();visual=null;base.DisposePooledResources();}
}
#else
// Standalone validates registered schemas; Unity-only rendering/physics execute in native checks.
public sealed class PNStarweaverCastAction:PNSkillAction
{
    public override void Start(PNSkillActionContext c){PNStarweaverCombatContent.ValidatePayload(c.Data.Action.Perform,c.Registry);}
    public override void Perform(PNSkillActionContext c){}
    public override void Exit(PNSkillActionContext c){}
    public override void Cancel(PNSkillActionContext c){}
}
public sealed class PNStarweaverTriangleOperation:PNSkillOperation
{public override void Execute(PNSkillActionContext c,Newtonsoft.Json.Linq.JToken p){PNStarweaverCombatContent.ValidateTriangle(p,c.Registry);}}
public sealed class PNStarweaverProjectile {}
#endif
