#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public sealed class PNElementalBuffPlayReview : MonoBehaviour
{
    public static string Status="Idle";
    private readonly List<GameObject> fixtures=new List<GameObject>();
    private readonly List<string> evidence=new List<string>();
    private bool background,cleaned;
    public static string Run()
    {
        if(!Application.isPlaying || EditorApplication.isPaused || !EveRegistrySession.IsReady)throw new InvalidOperationException("Ready unpaused Play required.");
        Status="Running";new GameObject("Disposable elemental Play review").AddComponent<PNElementalBuffPlayReview>();return Status;
    }
    private void Check(bool value,string message){if(!value)throw new Exception(message);evidence.Add("PASS "+message);}
    private void Pump()=>EditorApplication.QueuePlayerLoopUpdate();
    private void Start(){background=Application.runInBackground;Application.runInBackground=true;EditorApplication.update+=Pump;StartCoroutine(Guarded());}
    private IEnumerator Guarded()
    {
        var iterator=Review();
        try
        {
            while(true){object current;try{if(!iterator.MoveNext())break;current=iterator.Current;}catch(Exception error){Status="FAIL "+error;evidence.Add(Status);yield break;}yield return current;}
            Status="PASS "+evidence.Count+" elemental Play checks";evidence.Add(Status);
        }
        finally{(iterator as IDisposable)?.Dispose();Cleanup();Directory.CreateDirectory(".utmp/ElementalVfxCompact");File.WriteAllLines(".utmp/ElementalVfxCompact/PlayResults.txt",evidence);Destroy(gameObject);}
    }
    private PNStat Entity(long id)
    {
        var root=new GameObject("Elemental Play fixture "+id);fixtures.Add(root);root.SetActive(false);root.transform.position=new Vector3(25+(id%10),0,25);
        root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id),EveId.Parse("project_n:dummy"),0);root.AddComponent<EveTimeScale>();
        var body=root.AddComponent<CapsuleCollider>();body.radius=.18f;body.height=.9f;body.center=Vector3.up*.45f;
        var stat=root.AddComponent<PNEnemyStat>();root.AddComponent<EveEntityLoader>().Load(EveRegistrySession.Current);root.SetActive(true);return stat;
    }
    private IEnumerator Review()
    {
        var source=Entity(989101);source.SetSource(EveId.Parse("test:play_atk"),new[]{new EveStatModifier(PNContentIds.AttackPower,100,0)});
        var lightning=Entity(989102);var buffs=lightning.GetComponent<PNBuffManager>().Buffs;
        lightning.GetComponent<EveTimeScale>().AddOwnedModifier(EveId.Parse("test:play_slow"),EveTimeModifierKind.Scale,-.5f,lightning);
        int ticks=0;PNDamageResult last=null;lightning.DamageReceived+=r=>{ticks++;last=r;};
        var grant=buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:electrocution"),PNBuffSource.Capture(source),JObject.Parse("{'stacks':10}")));
        float start=Time.realtimeSinceStartup;yield return new WaitForSecondsRealtime(.6f);
        double consumed=5-buffs.Get(grant.Handle).Remaining.Value;
        Check(Math.Abs(lightning.GetComponent<EveTimeScale>().Scale-.5)<1e-8 && ticks==(int)Math.Floor((consumed+1e-6)/.4),"Actual Update Lightning cadence matches target half-scale clock");
        yield return new WaitForSecondsRealtime(.3f);
        Check(ticks>=1 && Math.Abs(last.Damage.Amount-262)<1e-8,"Actual Update Lightning cap damage and .4 Entity cadence");
        var visual=lightning.GetComponentInChildren<PNBuffElementVisual>();float elapsed=Time.realtimeSinceStartup-start;
        Check(visual!=null && visual.VisualTime>0 && visual.GetComponent<MeshFilter>().sharedMesh.vertexCount>0,"Actual LateUpdate generates animated Lightning geometry");
        float oldHeight=visual.BodyHeight;lightning.transform.localScale=Vector3.one*1.7f;yield return null;yield return null;
        Check(Math.Abs(visual.BodyHeight-oldHeight*1.7f)<1e-5,"Actual LateUpdate follows live entity collider scaling");
        var shape=lightning.GetComponent<CapsuleCollider>();shape.height=1.2f;shape.center=Vector3.up*.6f;yield return null;yield return null;
        float projectedHeight=(.36f+(1.2f-.36f)*Mathf.Abs(Vector3.Dot(lightning.transform.up,Camera.main.transform.up)))*1.7f;
        Check(Math.Abs(visual.BodyHeight-projectedHeight)<1e-5 && Vector3.Distance(visual.BodyCenter,lightning.transform.TransformPoint(shape.center))<1e-5,"Actual LateUpdate follows live collider resizing and center");
        var ownedMesh=visual.GetComponent<MeshFilter>().sharedMesh;var ownedMaterials=visual.GetComponent<MeshRenderer>().sharedMaterials;
        double remaining=buffs.Get(grant.Handle).Remaining.Value;lightning.gameObject.SetActive(false);yield return new WaitForSecondsRealtime(.2f);
        Check(buffs.Get(grant.Handle).Suspended && buffs.Get(grant.Handle).Remaining==remaining && lightning.GetComponentsInChildren<PNBuffElementVisual>(true).Length==0,"Actual entity OnDisable retains Buff time and returns visual");
        Check(ownedMesh==null && ownedMaterials.All(m=>m==null),"Actual OnDisable releases both body/glow materials and mesh");
        lightning.gameObject.SetActive(true);yield return null;
        Check(!buffs.Get(grant.Handle).Suspended && lightning.GetComponentsInChildren<PNBuffElementVisual>().Length==1 && buffs.Get(grant.Handle).Stacks==10,"Actual OnEnable resumes stack and visual");
        buffs.Remove(grant.Handle);yield return null;Check(lightning.GetComponentsInChildren<PNBuffElementVisual>(true).Length==0,"Play dispel destroys last DOT renderer");
        var target=Entity(989103);var targetBuffs=target.GetComponent<PNBuffManager>().Buffs;var captured=PNBuffSource.Capture(source);
        var vulnerability=targetBuffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:ice_vulnerability"),captured));
        Check(Math.Abs(target.CalculateDamage(100,PNContentIds.Magic,PNContentIds.Ice)-175)<1e-8,"Play vulnerability applies group2 mitigation");
        var frost=targetBuffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:frostbite"),captured,JObject.Parse("{'stacks':5}")));
        int frostTicks=0;double dealt=0;target.DamageReceived+=r=>{frostTicks++;dealt=r.AppliedDamage;};
        yield return new WaitForSecondsRealtime(1.6f);
        Check(frostTicks>=1 && Math.Abs(dealt-612.5)<1e-8,"Actual Frostbite tick receives corresponding 75 percent vulnerability");
        int beforeTrigger=frostTicks;
        source.GetComponent<EveEntityLoader>().Unload();targetBuffs.Trigger(frost.Handle);
        Check(frostTicks==beforeTrigger+1 && Math.Abs(dealt-612.5)<1e-8,"New DOT unloaded-source capture remains valid in Play");
        targetBuffs.Remove(vulnerability.Handle);targetBuffs.ChangeStacks(frost.Handle,-4);targetBuffs.Trigger(frost.Handle);
        Check(Math.Abs(dealt-150)<1e-8,"Play stack reduction recomputes damage and vulnerability removal returns own effect");
        var burn=targetBuffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:burn"),captured));
        var old=frost.Handle;target.GetComponent<EveEntityLoader>().Unload();yield return null;
        Check(!old.IsAlive && !burn.Handle.IsAlive && target.GetComponentsInChildren<PNBuffElementVisual>(true).Length==0,"Play target unload invalidates DOT handles and returns all visuals");
    }
    private void Cleanup()
    {
        if(cleaned)return;cleaned=true;EditorApplication.update-=Pump;
        foreach(var root in fixtures)if(root!=null){root.GetComponent<EveEntityLoader>()?.Unload();Destroy(root);}fixtures.Clear();Application.runInBackground=background;
    }
    private void OnDestroy()=>Cleanup();
}
#endif
