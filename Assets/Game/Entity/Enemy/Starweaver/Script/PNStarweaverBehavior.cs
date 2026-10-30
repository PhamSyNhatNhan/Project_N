#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PNStarweaverBehavior:PNEnemyBehavior
{
    public override IEnumerable<EveId> SkillIds=>new[]{PNStarweaverCombatContent.Weave,PNStarweaverCombatContent.Triangle};
    [SerializeField,Min(.1f)] private float attackRange=1.5f;
    private PNStat target;private PNEnemyNavigationRequest walk;private float rest,scan,retreat;
    private readonly RaycastHit[] hits=new RaycastHit[16];
    public override void Enter(){target=null;walk=null;rest=scan=retreat=0;}
    private bool Valid(PNStat s)=>s!=null&&s.IsReady&&!s.IsDead&&Context.Stat.Targets(s)&&PNTargetSelector.IsAggroEligible(s)&&PNWorldGameplay.CanTarget(Context.Owner,s);
    private bool Sight(PNStat s)
    {
        var from=Context.Owner.transform.position+Vector3.up*.2f;var d=s.transform.position+Vector3.up*.2f-from;
        int n=Context.Owner.gameObject.scene.GetPhysicsScene().Raycast(from,d.normalized,hits,d.magnitude,LayerMask.GetMask("Wall"),QueryTriggerInteraction.Ignore);return n==0;
    }
    public override void Tick(float dt)
    {
        rest-=dt;retreat-=dt;scan-=dt;
        if(Context.Owner.IsExecutingEnemyAction){CancelWalk();return;}
        if(!Valid(target)||!Sight(target)||Vector3.Distance(target.transform.position,Context.Owner.transform.position)>6)target=null;
        if(target==null&&scan<=0)
        {
            scan=.2f;var world=Context.Owner.GetComponent<EveEntityIdentity>().World;var best=default(PNTargetScore);
            foreach(var e in world.Session.Entities.Snapshot(world))
            {
                var s=e.GetComponent<PNStat>();if(!Valid(s))continue;float distance=Vector3.Distance(s.transform.position,Context.Owner.transform.position);if(distance>6||!Sight(s))continue;
                var score=new PNTargetScore{Distance=distance,Aggro=PNTargetSelector.AggroOf(s),EntityId=s.EntityId.Value};if(target==null||PNTargetRanking.Better(score,best,PNTargetRule.Aggro)){target=s;best=score;}
            }
        }
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
