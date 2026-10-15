#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EveEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Native touch and real stat changes in a disposable entity; never saves authored scenes.
public sealed class PNSparklePlayReview : MonoBehaviour
{
    public static string Status="Idle";
    private readonly List<string> evidence=new List<string>();
    private readonly List<InputAction> enabledActions=new List<InputAction>();
    private PNUiRuntime ui;private PNPlayerStat stat;private Scene fixture;
    private long previousOwner;private EveEntityId previousPlayer;private bool previousMobile,previousTest,initialized;
    private float previousTime;private InputSettings oldSettings,settings;private HideFlags oldFlags;private Touchscreen touch;
    private int checks;private const string Folder=".utmp/StarShapeReview";
    public static string Run()
    {
        if(!Application.isPlaying || !EveRegistrySession.IsReady || EditorApplication.isPaused)throw new Exception("Ready Play Mode required");
        var review=new GameObject("Disposable shimmer review").AddComponent<PNSparklePlayReview>();EditorApplication.update+=review.Pump;EditorApplication.QueuePlayerLoopUpdate();Status="Running";return Status;
    }
    private void Start()=>StartCoroutine(Guarded());
    private void Pump(){EditorApplication.QueuePlayerLoopUpdate();EditorWindow.focusedWindow?.Repaint();}
    private void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;evidence.Add("PASS "+text);}
    private object Fx(string field)=>typeof(PNUiRuntime).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
    private int Count(string field)=>(int)Fx(field).GetType().GetProperty("ActiveCount",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Fx(field));
    private Color Glow(string field)=>(Color)Fx(field).GetType().GetProperty("LastGlow",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Fx(field));
    private int Visible(string layer)=>ui.HudRoot.Q(layer).Children().Count(s=>s.style.opacity.value>.03f);
    private void Capture(string name){var texture=PNPauseVisualReview.ReadSelectedView();try{File.WriteAllBytes(Folder+"/"+name+".png",texture.EncodeToPNG());}finally{Destroy(texture);}}
    private Vector2 Center(string name){var p=ui.HudRoot.Q(name).worldBound.center;return new Vector2(p.x*Screen.width/ui.HudRoot.resolvedStyle.width,Screen.height-p.y*Screen.height/ui.HudRoot.resolvedStyle.height);}
    private void Touch(int id,UnityEngine.InputSystem.TouchPhase phase,Vector2 point)=>InputSystem.QueueStateEvent(touch,new TouchState {touchId=id,phase=phase,position=point});
    private IEnumerator Guarded()
    {
        Directory.CreateDirectory(Folder);var sequence=Review();
        while(true){object next;try{if(!sequence.MoveNext())break;next=sequence.Current;}catch(Exception e){Status="FAIL "+e;Debug.LogException(e);break;}yield return next;}
        (sequence as IDisposable)?.Dispose();Cleanup();evidence.Add(Status);File.WriteAllLines(Folder+"/Results.txt",evidence);Destroy(gameObject);
    }
    private IEnumerator Review()
    {
        ui=FindAnyObjectByType<PNUiRuntime>();previousOwner=ui.LocalOwnerId;previousPlayer=ui.LocalPlayerId;
        previousMobile=ui.ShowMobileControls;previousTest=ui.TestExpanded;previousTime=Time.timeScale;initialized=true;
        oldSettings=InputSystem.settings;oldFlags=oldSettings.hideFlags;if(oldFlags==HideFlags.HideAndDontSave)oldSettings.hideFlags=HideFlags.DontSave;
        settings=Instantiate(oldSettings);InputSystem.settings=settings;settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        foreach(var skill in FindObjectsByType<PNPlayerSkill>(FindObjectsSortMode.None))
            foreach(var action in new[]{skill.PC.Controller.Attack,skill.PC.Controller.Skill,skill.PC.Controller.Ulti,skill.PC.Controller.Dash,skill.PC.Controller.Burst})
                if(action.enabled){enabledActions.Add(action);action.Disable();}
        fixture=SceneManager.CreateScene("Disposable shimmer fixture");var obj=new GameObject("Shimmer fixture player");obj.SetActive(false);SceneManager.MoveGameObjectToScene(obj,fixture);
        obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(998771),EveId.Parse("project_n:eve"),90771);
        obj.AddComponent<PNAbilityManager>();stat=obj.AddComponent<PNPlayerStat>();obj.AddComponent<EveEntityLoader>().Load(EveRegistrySession.Current);obj.SetActive(true);
        stat.SetCurrent(PNContentIds.Health,stat.GetMax(PNContentIds.Health));
        ui.LocalOwnerId=90771;ui.LocalPlayerId=stat.EntityId;ui.ShowMobileControls=true;ui.SetTestExpanded(false);Time.timeScale=1;ui.RefreshUI();
        touch=InputSystem.AddDevice<Touchscreen>("ShimmerReviewTouch");yield return new WaitForSecondsRealtime(.3f);
        Check(Count("avatarSparkles")==0 && Count("healthSparkles")==0,"Initial player snapshot has no HP burst");
        var glyph=ui.HudRoot.Q("hud-avatar-sparkles").Children().First();
        Check(glyph.childCount==2 && glyph.Q("sparkle-soft-halo")!=null,"Filled glyph keeps white vector core separate from soft textured halo");
        Check(Resources.FindObjectsOfTypeAll<Texture2D>().Count(t=>t.name=="Sparkle filled blur" || t.name=="Sparkle hollow blur")==2,"UI owns exactly two shared cached blur masks");
        double max=stat.GetMax(PNContentIds.Health);stat.SetCurrent(PNContentIds.Health,max*.4);ui.RefreshUI();
        Check(Count("avatarSparkles")==7 && Count("healthSparkles")==10,"HP loss schedules demo 7 avatar +10 health glints");
        Check(Mathf.Abs(ui.HudRoot.Q<ProgressBar>("player-hp").value-40)<.01f,"HP bar updates immediately before effect");
        var color=Glow("healthSparkles");Check(color.r>color.g && color.r>color.b,"HP loss glow shifts red at low HP");
        yield return new WaitForSecondsRealtime(.4f);Check(Visible("hud-avatar-sparkles")>0 && Visible("hud-health-sparkles")>0,"Real rendered HP loss glints visible on both hosts");Capture("HealthLoss");
        yield return new WaitForSecondsRealtime(1.5f);Check(Count("avatarSparkles")==0 && Count("healthSparkles")==0,"HP burst ends without idle shimmer");
        stat.SendHeal(stat,max*.25);ui.RefreshUI();Check(Count("avatarSparkles")==7 && Count("healthSparkles")==10,"Real Heal API HP commit triggers demo burst");
        color=Glow("healthSparkles");Check(color.g>color.r && color.g>color.b,"Healing glow is green");
        yield return new WaitForSecondsRealtime(.4f);Capture("HealthHeal");
        stat.SetCurrent(PNContentIds.Health,max*.2);ui.RefreshUI();
        Check(Count("avatarSparkles")==7 && Count("healthSparkles")==10 && Glow("healthSparkles").r>Glow("healthSparkles").g,"Rapid HP change replaces old burst and glow without accumulation");
        yield return new WaitForSecondsRealtime(1.8f);
        var button=Center("mobile-skill");Touch(1,UnityEngine.InputSystem.TouchPhase.Began,button);yield return new WaitForSecondsRealtime(.3f);
        Check(ui.HudRoot.Q("mobile-skill").ClassListContains("mobile-pressed"),"Native Touch holds existing skill button");
        Check(Visible("mobile-skill-sparkles")>0,"Held skill renders small glints");
        yield return new WaitForSecondsRealtime(.65f);Check(Visible("mobile-skill-sparkles")>3,"Repeated 430ms batches overlap beyond three glints");Capture("SkillHeld");
        Check(ui.HudRoot.Q("mobile-skill-sparkles").Children().Where(s=>s.style.opacity.value>.1f).Max(s=>s.worldBound.width)>4.5f,"Held glints retain a readable core after mobile cluster scaling");
        Touch(1,UnityEngine.InputSystem.TouchPhase.Moved,new Vector2(Screen.width*.5f,Screen.height*.7f));yield return new WaitForSecondsRealtime(.1f);
        Check(ui.HudRoot.Q("mobile-skill").ClassListContains("mobile-pressed"),"Held sparkle follows captured pointer outside button");
        Touch(1,UnityEngine.InputSystem.TouchPhase.Ended,button);yield return new WaitForSecondsRealtime(1.3f);
        Check(Visible("mobile-skill-sparkles")==0 && !ui.HudRoot.Q("mobile-skill").ClassListContains("mobile-pressed"),"Release stops spawning and lets active glints finish");
        Touch(2,UnityEngine.InputSystem.TouchPhase.Began,button);yield return new WaitForSecondsRealtime(.3f);ui.OpenMenu();yield return new WaitForSecondsRealtime(1.1f);
        Check(Visible("mobile-skill-sparkles")==0 && Count("healthSparkles")==0,"Pause clears held and HUD sparkle pools");
        var sky=ui.HudRoot.Q("pause-sky");var first=sky.Children().First();float x=first.style.left.value.value,y=first.style.top.value.value;
        Capture("PauseOpen");yield return new WaitForSecondsRealtime(6.6f);
        Check(Mathf.Abs(first.style.left.value.value-x)>.001f || Mathf.Abs(first.style.top.value.value-y)>.001f,"Decorative pause star changes location after a cycle");
        Check(Mathf.Abs(first.style.left.value.value-13)<=6 && Mathf.Abs(first.style.top.value.value-17)<=6,"Random position remains inside authored star region");Capture("PauseNextCycle");
        stat.SetCurrent(PNContentIds.Health,max*.2);ui.RefreshUI();ui.CloseMenu();yield return new WaitForSecondsRealtime(.7f);
        Check(Count("healthSparkles")==0,"HP changes under menu do not replay burst after close");
        ui.LocalPlayerId=previousPlayer;ui.LocalOwnerId=previousOwner;ui.RefreshUI();Check(Count("healthSparkles")==0,"Switching HUD entity establishes baseline without burst");
        ui.enabled=false;yield return null;ui.enabled=true;ui.RefreshUI();yield return new WaitForSecondsRealtime(.2f);
        Check(ui.HudRoot.Q("hud-avatar-sparkles").childCount==28 && Count("avatarSparkles")==0,"Disable/rebuild creates one clean fixed pool");
        Check(Resources.FindObjectsOfTypeAll<Texture2D>().Count(t=>t.name=="Sparkle filled blur" || t.name=="Sparkle hollow blur")==2,"Disable/rebuild releases old blur masks without texture leaks");
        Status="PASS "+checks+" native UI sparkle checks";
    }
    private void Cleanup()
    {
        EditorApplication.update-=Pump;if(!initialized)return;initialized=false;
        if(ui!=null){ui.CloseMenu();ui.LocalOwnerId=previousOwner;ui.LocalPlayerId=previousPlayer;ui.ShowMobileControls=previousMobile;ui.SetTestExpanded(previousTest);ui.RefreshUI();}
        if(touch!=null)InputSystem.RemoveDevice(touch);foreach(var action in enabledActions)action.Enable();
        if(oldSettings!=null){InputSystem.settings=oldSettings;oldSettings.hideFlags=oldFlags;}if(settings!=null)Destroy(settings);
        if(fixture.IsValid()){foreach(var root in fixture.GetRootGameObjects())root.GetComponent<EveEntityLoader>()?.Unload();SceneManager.UnloadSceneAsync(fixture);}
        Time.timeScale=previousTime;
    }
    private void OnDestroy()=>Cleanup();
}
#endif
