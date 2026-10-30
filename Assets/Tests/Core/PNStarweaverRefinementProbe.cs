#if UNITY_5_3_OR_NEWER
using System;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;
public sealed class PNStarweaverRefinementProbe:MonoBehaviour
{
    private PNStarweaverSkill enemy;private PNPlayerStat player;private bool fly,cast,broken;private float start,breakAt;private long count;private int starsMask;private bool oldBackground;private string output="F:/unity/Project_N/.utmp/StarweaverRefinement/Play";
    private void Awake(){oldBackground=Application.runInBackground;Application.runInBackground=true;Directory.CreateDirectory(output);start=Time.realtimeSinceStartup;}
    private void Update()
    {
        if(Time.realtimeSinceStartup-start>60){Report("TIMEOUT");enabled=false;return;}
        if(enemy==null)enemy=FindFirstObjectByType<PNStarweaverSkill>();if(player==null)player=FindFirstObjectByType<PNPlayerStat>();
        if(enemy==null||player==null||!enemy.IsReady||!player.IsReady)return;
        var visual=enemy.GetComponent<PNVisualBody>();
        if(count==0){count=enemy.CastCount+1;player.transform.position=new Vector3(1,player.transform.position.y,0);}
        if(visual.Actor.Animator.BaseState=="walk"||visual.Actor.Animator.BaseState=="run")fly=true;
        var marker=GameObject.Find("Starweaver constellation");
        if(marker!=null)
        {
            int n=0;foreach(Transform c in marker.transform)if(c.name.StartsWith("Star ")&&c.gameObject.activeSelf)n++;
            int bit=1<<n;if((starsMask&bit)==0){starsMask|=bit;ScreenCapture.CaptureScreenshot(output+"/stars"+n+".png");}
        }
        if(!cast&&enemy.CastCount>=count)
        {
            cast=true;float distance=Vector2.Distance(new Vector2(enemy.transform.position.x,enemy.transform.position.z),new Vector2(player.transform.position.x,player.transform.position.z));
            File.WriteAllText(output+"/cast.txt","distance="+distance+" facingLeft="+visual.Actor.FacingLeft+" flySeen="+fly+" gold="+(enemy.transform.Find("Golden needle")!=null));
        }
        if(cast&&!broken&&starsMask==14&&!enemy.IsExecutingEnemyAction)
        {
            broken=true;breakAt=Time.time;
            var stat=enemy.GetComponent<PNEnemyStat>();stat.SetCurrent(PNContentIds.Lorica,1);stat.ReceiveDamage(player.CreateDamage(100,false,0,EveId.Parse("base:magic")));
        }
        if(broken&&Time.time-breakAt>.3f)
        {
            Report("break state="+visual.Actor.Animator.BaseState+" broken="+visual.LoricaBroken+" Lorica="+enemy.GetComponent<PNStat>().GetCurrent(PNContentIds.Lorica));
            ScreenCapture.CaptureScreenshot(output+"/stun.png");enabled=false;
        }
    }
    private void Report(string reason){File.WriteAllText(output+"/result.txt",reason+"\nfly="+fly+" cast="+cast+" starsMask="+starsMask+" failure="+enemy?.Failure);}
    private void OnDestroy(){Application.runInBackground=oldBackground;}
}
#endif

#if UNITY_EDITOR
// Opt-in, attached only by the disposable Play review command; no startup hook.
public sealed class PNStarweaverConstellationProbe:UnityEngine.MonoBehaviour
{
    private PNStarweaverSkill enemy;private PNPlayerStat player;private UnityEngine.GameObject marker;
    private float first=-1,nextShot;private bool pulsed,finished,oldBackground;private double initial,before,after;private int frames;
    private UnityEngine.Vector3 preShake;private float peakShake;private bool havePreShake;
    private string output="F:/unity/Project_N/.utmp/StarweaverImpactV6/Play";
    private void Awake(){oldBackground=UnityEngine.Application.runInBackground;UnityEngine.Application.runInBackground=true;System.IO.Directory.CreateDirectory(output);}
    private void LateUpdate()
    {
        if(finished)return;
        if(enemy==null)enemy=UnityEngine.Object.FindFirstObjectByType<PNStarweaverSkill>();
        if(player==null)player=UnityEngine.Object.FindFirstObjectByType<PNPlayerStat>();
        if(enemy==null||player==null||!enemy.IsReady||!player.IsReady)return;
        if(first<0)
        {
            marker=UnityEngine.GameObject.Find("Starweaver constellation");if(marker==null)return;
            first=UnityEngine.Time.time;initial=before=player.GetCurrent(PNContentIds.Health);
        }
        float age=UnityEngine.Time.time-first;
        if(marker!=null)
        {
            bool now=marker.transform.Find("Rising constellation burst").gameObject.activeSelf;
            if(!pulsed&&now)
            {
                pulsed=true;after=player.GetCurrent(PNContentIds.Health);
                System.IO.File.WriteAllText(output+"/pulse.txt","observedAge="+age+" initialHP="+initial+" lastPrePulseHP="+before+" firstPulseHP="+after+" motion="+enemy.GetComponent<PNVisualBody>().Actor.Animator.BaseState);
            }
            var camera=PNViewCamera.Current;
            if(!now){before=player.GetCurrent(PNContentIds.Health);if(camera!=null){preShake=camera.transform.position;havePreShake=true;}}
            else if(camera!=null&&havePreShake)peakShake=UnityEngine.Mathf.Max(peakShake,UnityEngine.Vector3.Distance(camera.transform.position,preShake));
        }
        if(age>=nextShot&&frames<33){UnityEngine.ScreenCapture.CaptureScreenshot(output+"/"+(frames++).ToString("D2")+".png");nextShot=age+.10f;}
        if(age>3.45f)
        {
            finished=true;System.IO.File.WriteAllText(output+"/result.txt","pulsed="+pulsed+" observedDuration="+age+" markerDisposed="+(marker==null)+" failure="+enemy.Failure+" captures="+frames+" prePulseLoss="+(initial-before)+" pulseLoss="+(before-after)+" peakCameraDisplacement="+peakShake);
        }
    }
    private void OnDestroy(){UnityEngine.Application.runInBackground=oldBackground;}
}
#endif

#if UNITY_EDITOR
// Opt-in only: observe real AI volleys and their pool reuse, capture the first visible volley.
public sealed class PNStarweaverNeedleProbe:UnityEngine.MonoBehaviour
{
    private readonly System.Collections.Generic.HashSet<string> ids=new System.Collections.Generic.HashSet<string>();
    private readonly System.Collections.Generic.HashSet<long> handles=new System.Collections.Generic.HashSet<long>();
    private float began=-1,next;private int captures,maxRunning,contacts,reuses;private bool finished;
    private string output="F:/unity/Project_N/.utmp/StarweaverSoftThreadsV3/Play";
    private void Awake(){System.IO.Directory.CreateDirectory(output);}
    private void LateUpdate()
    {
        if(finished)return;
        var all=UnityEngine.Resources.FindObjectsOfTypeAll<PNStarweaverProjectile>();int count=0;
        foreach(var p in all)if(p.IsRunning){count++;string id=p.GetEntityId().ToString();if(handles.Add(p.Handle.InstanceId)&&!ids.Add(id))reuses++;}
        if(began<0){if(count==0)return;began=UnityEngine.Time.time;}
        float age=UnityEngine.Time.time-began;maxRunning=UnityEngine.Mathf.Max(maxRunning,count);
        foreach(var v in UnityEngine.Object.FindObjectsByType<PNStarweaverNeedleVisual>())if(v.gameObject.name=="Starweaver needle contact")contacts++;
        if(age<.75f&&age>=next&&captures<45){UnityEngine.ScreenCapture.CaptureScreenshot(output+"/"+(captures++).ToString("D2")+".png");next=age+.025f;}
        if(age>10)
        {
            finished=true;System.IO.File.WriteAllText(output+"/result.txt","maxRunning="+maxRunning+" pooledIdentities="+ids.Count+" reuseObservations="+reuses+" impactFrameObservations="+contacts+" captures="+captures);
        }
    }
}
#endif

#if UNITY_EDITOR
// Explicitly attached in a disposable Play fixture; never starts automatically.
public sealed class PNStarweaverCasterProbe:UnityEngine.MonoBehaviour
{
    private readonly PNStarweaverCastVisual[] tracked=new PNStarweaverCastVisual[2];
    private readonly bool[] seen=new bool[2],done=new bool[2];
    private readonly float[] next=new float[2];private readonly int[] frames=new int[2];
    private bool cancelled,finished;private int cancelFrame;
    private string output="F:/unity/Project_N/.utmp/StarweaverMuzzleV2/Play";
    private void Awake(){System.IO.Directory.CreateDirectory(output);}
    private void LateUpdate()
    {
        if(finished)return;
        var effects=UnityEngine.Object.FindObjectsByType<PNStarweaverCastVisual>();
        foreach(var v in effects)
        {
            int i=v.name.Contains("constellation")?1:0;if(!seen[i]){seen[i]=true;tracked[i]=v;}
            if(tracked[i]==v&&v.VisualAge>=next[i]&&frames[i]<(i==0?18:34))
            {UnityEngine.ScreenCapture.CaptureScreenshot(output+"/skill"+(i+1)+"_"+(frames[i]++).ToString("D2")+".png");next[i]=v.VisualAge+(i==0?.05f:.1f);}
        }
        for(int i=0;i<2;i++)if(seen[i]&&tracked[i]==null)done[i]=true;
        if(done[0]&&done[1]&&!cancelled&&effects.Length>0&&effects[0].VisualAge>.1f)
        {var enemy=UnityEngine.Object.FindAnyObjectByType<PNStarweaverSkill>();enemy.CancelWorldExecutions();cancelled=true;cancelFrame=UnityEngine.Time.frameCount;}
        if(cancelled&&UnityEngine.Time.frameCount>cancelFrame+1)
        {
            finished=true;var enemy=UnityEngine.Object.FindAnyObjectByType<PNStarweaverSkill>();
            System.IO.File.WriteAllText(output+"/result.txt","completedSkill1="+done[0]+" completedSkill2="+done[1]+" frames1="+frames[0]+" frames2="+frames[1]+" cancelRemainingEffects="+effects.Length+" failure="+enemy.Failure);
        }
    }
}
#endif
