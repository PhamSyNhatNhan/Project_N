#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;

// Disposable native rendering fixture; never edits a live player's Ability state.
public sealed class PNAbilityUIPlayReview : MonoBehaviour
{
    public static string Status="Idle";
    private PNUiRuntime ui;
    private GameObject fixture;
    private readonly List<string> evidence=new List<string>();
    public static string Run()
    {
        if(!Application.isPlaying||EditorApplication.isPaused)throw new InvalidOperationException("Unpaused Play Mode required");
        var review=new GameObject("Disposable Ability UI effects review").AddComponent<PNAbilityUIPlayReview>();
        review.ui=UnityEngine.Object.FindAnyObjectByType<PNUiRuntime>();
        Status="Running";EditorApplication.update+=review.Pump;return Status;
    }
    private void Pump()
    { var w=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));w.Focus();w.Repaint();EditorApplication.QueuePlayerLoopUpdate(); }
    private void Start()=>StartCoroutine(Guarded());
    private IEnumerator Guarded()
    {
        var sequence=Sequence();
        while(true)
        {
            object next;
            try{if(!sequence.MoveNext())break;next=sequence.Current;}
            catch(Exception e){Status="FAIL "+e;evidence.Add(Status);break;}
            yield return next;
        }
        Cleanup();File.WriteAllLines(".utmp/CombatUIReview/AbilityEffectsResults.txt",evidence.Concat(new[]{Status}));Destroy(gameObject);
    }
    private void Cleanup()
    {
        EditorApplication.update-=Pump;
        if(fixture!=null){fixture.GetComponent<EveEntityLoader>()?.Unload();Destroy(fixture);fixture=null;}
    }
    private void OnDestroy()=>Cleanup();
    private void Check(bool ok,string message){if(!ok)throw new Exception(message);evidence.Add("PASS "+message);}
    private void Capture(string name)=>ScreenCapture.CaptureScreenshot(".utmp/CombatUIReview/HudMinimal/AbilityEffects-"+name+".png");
    private IEnumerator Sequence()
    {
        fixture=new GameObject("Ability effects fixture");fixture.SetActive(false);
        fixture.transform.position=ui.HudPlayer.transform.position+new Vector3(1,0,0);
        fixture.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(998201),EveId.Parse("project_n:eve"),998201);
        fixture.AddComponent<PNPlayerStat>();var manager=fixture.AddComponent<PNAbilityManager>();
        var stateId=EveId.Parse("test:ui_fx_state");var counterId=EveId.Parse("test:ui_fx_counter");var cdId=EveId.Parse("test:ui_fx_cd");
        manager.Configure(new[]{
            new PNAbilitySettings{Id=stateId.ToString(),Kind=PNAbilityKind.State,ShowInUI=true,InitiallyActive=true,DisplayOrder=-30}.Create(),
            new PNAbilitySettings{Id=counterId.ToString(),Kind=PNAbilityKind.Counter,ShowInUI=true,Maximum=9,InitialAmount=2,DisplayOrder=-20}.Create(),
            new PNAbilitySettings{Id=cdId.ToString(),Kind=PNAbilityKind.Cooldown,ShowInUI=true,Maximum=2,InitialAmount=2,Cooldown=.4,DisplayOrder=-10}.Create()});
        fixture.AddComponent<EveEntityLoader>().Load(EveRegistrySession.Current);fixture.SetActive(true);
        yield return new WaitForSecondsRealtime(.3f);
        var doc=ui.GetComponentsInChildren<UIDocument>().Single(d=>d.name=="Combat UI 998201");
        var grid=doc.rootVisualElement.Q("ability-grid");
        var state=grid.Q(stateId.ToString());var counter=grid.Q(counterId.ToString());var cd=grid.Q(cdId.ToString());
        Check(state!=null&&counter!=null&&cd!=null,"Native fixture shows three Ability kinds");
        Check(state.Q("ability-flash").style.opacity.value==0,"Initial snapshot has no activation flash");
        manager.Abilities.GetCounter(counterId).SetAmount(3);yield return new WaitForSecondsRealtime(.06f);
        Check(ReferenceEquals(counter,grid.Q(counterId.ToString())),"Counter changes retain UI element identity");
        Check(counter.Q(className:"ability-counter").style.scale.value.value.x>1,"Stack increase bounces number badge");
        Check(counter.Q(className:"ability-counter").style.scale.value.value.x<=1.051f,"All counter pulses stay within five percent");
        Check(counter.Q("number-flash")==null,"Counter numbers have no flash glow");
        Check(counter.Q(className:"ability-counter").style.transformOrigin.value.x.value==0&&
            cd.Q(className:"ability-charge").style.transformOrigin.value.x.value==100,"Number bounce anchors at the icon-facing edge");
        Check(counter.Q("ability-flash").style.opacity.value==0,"Stack gain does not flash the whole icon");
        Capture("Stack");
        ui.RefreshUI();float ongoingScale=counter.Q(className:"ability-counter").style.scale.value.value.x;
        for(int amount=4;amount<=8;amount++)
        {manager.Abilities.GetCounter(counterId).SetAmount(amount);ui.RefreshUI();}
        Check(counter.Q<Label>(className:"ability-counter").text=="8","Rapid increments update the number immediately");
        Check(Mathf.Abs(counter.Q(className:"ability-counter").style.scale.value.value.x-ongoingScale)<.0001f,"Rapid increments do not restart the current bounce");
        yield return new WaitForSecondsRealtime(.25f);
        Check(counter.Q(className:"ability-counter").style.scale.value.value.x==1,"Number bounce finishes while retaining updated value");
        manager.Abilities.GetCooldown(cdId).TryConsume();yield return new WaitForSecondsRealtime(.48f);
        Check(cd.Q("ability-flash").style.opacity.value>0,"Recovery completion flashes ready icon");
        Check(cd.Q("ability-flash").style.opacity.value<=1,"Icon flash uses reduced intensity");
        Check(cd.Q(className:"ability-image").style.scale.value.value.x>1,"Ready icon briefly enlarges");
        Capture("Ready");yield return new WaitForSecondsRealtime(.25f);
        manager.Abilities.Find(stateId).OfType<PNAbilityState>().First().End();yield return new WaitForSecondsRealtime(.04f);
        Check(grid.Q(stateId.ToString())!=null&&state.style.opacity.value<1,"Inactive state fades while retaining its slot");
        Capture("Fade");yield return new WaitForSecondsRealtime(.09f);
        Check(grid.Q(stateId.ToString())==null,"Faded state retires from grid");
        Check(Mathf.Abs(counter.style.translate.value.x.value)>0,"Remaining icon smoothly compacts");
        Capture("Reflow");ui.RefreshUI();
        float beforeRetarget=counter.style.left.value.value+counter.style.translate.value.x.value;
        manager.Abilities.Find(stateId).OfType<PNAbilityState>().First().TryActivate();ui.RefreshUI();
        float afterRetarget=counter.style.left.value.value+counter.style.translate.value.x.value;
        Check(Mathf.Abs(beforeRetarget-afterRetarget)<.01f,"Interrupted reflow continues from its current visual position");
        yield return new WaitForSecondsRealtime(.07f);
        Check(grid.Q(stateId.ToString()).Q("ability-flash").style.opacity.value>0,"Reactivated state flashes once");
        Check(grid.Q(stateId.ToString()).style.opacity.value>0&&grid.Q(stateId.ToString()).style.opacity.value<1,"Newly visible icon fades in");
        Capture("Activate");yield return new WaitForSecondsRealtime(.3f);
        Check(grid.Q(stateId.ToString()).Q("ability-flash").style.opacity.value==0,"Activation effect stops without looping");
        Check(counter.style.translate.value.x.value==0,"Compaction settles after retargeting");
        // Add upper rows without shifting an existing bottom-row icon.
        var baseline=counter.layout.y-doc.worldSpaceSize.y;
        for(int i=0;i<4;i++)manager.Abilities.Define(new PNAbilitySettings{
            Id="test:ui_fx_extra_"+i,Kind=PNAbilityKind.State,ShowInUI=true,InitiallyActive=true,DisplayOrder=100+i}.Create());
        yield return new WaitForSecondsRealtime(.08f);
        Check(Mathf.Abs(counter.layout.y-doc.worldSpaceSize.y-baseline)<.1f&&
            Mathf.Abs(counter.style.translate.value.y.value)<.1f,"Adding rows keeps the bottom row anchored");
        Capture("Rows");yield return new WaitForSecondsRealtime(.25f);
        Status="PASS "+evidence.Count+" native Ability effects checks";
    }
}
#endif
