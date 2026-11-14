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
    private PNSkillOperation operation; private PNVisualBody body; private PNStarweaverCastVisual castVisual;
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
            // The blast owns the telegraph and the detonation on its own clock; this cast only ends it if the cast is cancelled (Action dependency).
            fired=true;operation=CreateOperation(c,EveId.Parse(p["operationId"].Value<string>()));operation.Execute(c,p["data"]);
        }
    }
    public override void Perform(PNSkillActionContext c) { }
    public override bool AdvanceAuxiliary(float dt)
    {
        if(ended)return true;age+=dt;
        castVisual?.SetTime(age);
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
    public override void Cancel(PNSkillActionContext c){try{c.CancelEffects();EndBlast();operation?.Cancel();}finally{Dispose();}}
    // Only a cancelled cast ends its blast; a finished one lets the blast run out its own tail.
    private void EndBlast(){var handle=(operation as PNCreateBlastOperation)?.Result?.Handle;if(handle.HasValue&&handle.Value.IsAlive)handle.Value.End();}
    public override void Dispose(){if(ended)return;ended=true;castVisual?.Dispose();castVisual=null;body?.EndAction();body=null;operation?.Dispose();operation=null;context=null;}
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
public sealed class PNStarweaverTriangleBlast {}
public sealed class PNStarweaverProjectile {}
#endif
