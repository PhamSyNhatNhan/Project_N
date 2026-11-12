#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PNStarweaverBehavior:PNEnemyBehavior
{
    public override IEnumerable<EveId> SkillIds=>new[]{PNStarweaverCombatContent.Weave,PNStarweaverCombatContent.Triangle};
    [SerializeField,Min(.1f)] private float attackRange=1.5f, vision=6f;
    private PNStat target;private PNEnemyNavigationRequest walk;private float rest,scan,retreat;
    private PNEnemyTargeting targeting;
    // The sight range is the character's registered `body.vision`; `vision` is the default of an entity that has no role data.
    public override void Enter(){target=null;walk=null;rest=scan=retreat=0;targeting=new PNEnemyTargeting(Context,vision){Planar=false};}
    public override void Tick(float dt)
    {
        rest-=dt;retreat-=dt;scan-=dt;
        if(Context.Owner.IsExecutingEnemyAction){CancelWalk();return;}
        if(!targeting.Keep(target))target=null;
        if(target==null&&scan<=0){scan=.2f;target=targeting.Acquire();}
        if(target==null){CancelWalk();return;}
        var delta=target.transform.position-Context.Owner.transform.position;delta.y=0;float dist=delta.magnitude;
        if(dist<.7f&&retreat<=0){CancelWalk();walk=Context.GoTo(Context.Owner.transform.position-delta.normalized*.8f,.1f);retreat=2;return;}
        if(dist>attackRange){if(walk==null||walk.IsTerminal)walk=Context.GoTo(target.transform.position,attackRange*.9f);else Context.UpdateGoal(walk,target.transform.position);return;}
        if(walk!=null&&!walk.IsTerminal&&retreat>.8f)return;
        CancelWalk();if(rest>0)return;
        var triangle=Context.Abilities.GetCooldown(EveId.Parse(PNStarweaverCombatContent.Triangle+"_ready"));var weave=Context.Abilities.GetCooldown(EveId.Parse(PNStarweaverCombatContent.Weave+"_ready"));
        bool useTriangle=triangle.CanConsume()&&dist<=attackRange;var cd=useTriangle?triangle:weave;if(!cd.CanConsume())return;
        var id=useTriangle?PNStarweaverCombatContent.Triangle:PNStarweaverCombatContent.Weave;
        Context.Move.TryFaceSkill(new Vector2(delta.x,delta.z));
        var actor=Context.Owner.GetComponent<PNVisualBody>()?.Actor;
        if(actor!=null){var camera=PNViewCamera.Current;actor.SetFacingLeft(Vector3.Dot(delta,camera!=null?camera.transform.right:Vector3.right)<0);}
        Vector3 position=useTriangle?target.transform.position:Context.Owner.transform.position+Vector3.up*.2f;
        if(Context.TryCast(id,delta.sqrMagnitude>.00001f?delta:Vector3.forward,PNEnemyActionExecution.AuxiliaryTimeline,out var receipt,new[]{target},()=>cd.TryConsume(),position)==PNEnemyCastAdmission.Started)rest=useTriangle?1.9f:1.25f;
    }
    private void CancelWalk(){if(walk!=null&&!walk.IsTerminal)Context.CancelNavigation(walk);walk=null;}
    public override void Exit(PNEnemyBehaviorExit reason){CancelWalk();target=null;}
}
#endif
