#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.UIElements;

// Opt-in Player harness, always launched with -pn-settings-fixture=<isolated root>.
public sealed class PNSettingsPlayReview : MonoBehaviour
{
    private string output;private int checks,errors,shot;
    private readonly List<string> trace=new List<string>();
    private static string Arg(string name)=>Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith(name,StringComparison.Ordinal))?.Substring(name.Length);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install(){string path=Arg("-pn-settings-review=");if(path==null)return;var go=new GameObject("Settings Player Review");DontDestroyOnLoad(go);go.AddComponent<PNSettingsPlayReview>().output=path;}
    private void OnEnable()=>Application.logMessageReceived+=Log;
    private void OnDisable()=>Application.logMessageReceived-=Log;
    private void Log(string c,string s,LogType t){if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert){errors++;trace.Add(t+": "+c);}}
    private void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;trace.Add("PASS "+label);}
    private static void Press(VisualElement root,string name){var button=root.Q<Button>(name)??throw new Exception("Missing "+name);using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}}
    private IEnumerator Until(Func<bool> condition,string label,float seconds=100){float end=Time.realtimeSinceStartup+seconds;while(!condition()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(label);yield return null;}Check(true,label);}
    private IEnumerator Shot(string label){yield return new WaitForEndOfFrame();var tex=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,(shot++).ToString("00")+"_"+label+".png"),tex.EncodeToPNG());Destroy(tex);}
    private IEnumerator Start()
    {
        Directory.CreateDirectory(output);Application.runInBackground=true;Exception failure=null;
        var stack=new Stack<IEnumerator>();stack.Push(Run());
        while(stack.Count>0){object next;try{if(!stack.Peek().MoveNext()){stack.Pop();continue;}next=stack.Peek().Current;if(next is IEnumerator nested){stack.Push(nested);continue;}}catch(Exception e){failure=e;break;}yield return next;}
        if(failure!=null)trace.Add("FAIL "+failure);trace.Add("checks="+checks+" errors="+errors+" size="+Screen.width+"x"+Screen.height);File.WriteAllText(Path.Combine(output,"result.txt"),string.Join("\n",trace));Application.Quit(failure==null&&errors==0?0:1);
    }
    private IEnumerator Run()
    {
        Check(Arg("-pn-settings-fixture=")!=null,"isolated settings root");
        UIDocument title=null;
        yield return Until(()=>{title=FindObjectsByType<UIDocument>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(d=>d.rootVisualElement.Q("pn-title-start")!=null);return title!=null&&PNLocalization.IsReady;},"localized Title");
        var root=title.rootVisualElement;yield return new WaitForSecondsRealtime(.5f);
        if(Arg("-pn-settings-restart=")!=null)
        {Check(PNSettingsService.Current.Locale=="en"&&PNSettingsService.Current.TargetFrameRate==120&&PNLocalization.Selected=="en","restart restores saved locale/FPS");Check(PNSettingsService.Current.AutoTargetMode==PNTargetMode.LowestHealth&&PNTargetRules.LocalMode==PNTargetMode.LowestHealth,"restart restores the auto target mode");Check(Application.targetFrameRate==120&&QualitySettings.vSyncCount==0,"restart frame policy");yield return Shot("restart_title");yield break;}
        Press(root,"pn-title-settings");yield return new WaitForSecondsRealtime(.18f);yield return Shot("enter_early");yield return new WaitForSecondsRealtime(.4f);yield return Shot("enter_middle");yield return new WaitForSecondsRealtime(.9f);yield return Shot("settings_open");
        Check(root.Q("pn-settings-view")!=null,"shared Settings at Title");
        string settingsDir=Path.GetDirectoryName(PNSettingsService.FilePath);
        Check(root.Q<Button>("pn-settings-save")!=null&&!root.Q<Button>("pn-settings-save").enabledSelf,"Save sits at the end of the list and is greyed while nothing changed");
        Press(root,"pn-settings-language-trigger");yield return new WaitForSecondsRealtime(.3f);yield return Shot("language_dropdown");
        string localeBefore=PNLocalization.Selected;
        Press(root,"pn-settings-language-option-en");yield return new WaitForSecondsRealtime(.25f);Check(PNLocalization.Selected==localeBefore,"language waits for Save");
        Check(root.Q<Button>("pn-settings-save").enabledSelf,"Save turns on after a change");
        Press(root,"pn-settings-fps-trigger");yield return new WaitForSecondsRealtime(.3f);yield return Shot("fps_dropdown");
        Press(root,"pn-settings-fps-option-120");yield return new WaitForSecondsRealtime(.2f);
        Check(Application.targetFrameRate!=120&&new PNSettingsStore(settingsDir).Load().Value.TargetFrameRate!=120,"FPS waits for Save, the file is untouched");
        Check(root.Q("pn-settings-auto-target-trigger")!=null&&PNSettingsService.Current.AutoTargetMode==PNTargetMode.Nearest,"auto target row present, default Nearest");
        Press(root,"pn-settings-auto-target-trigger");yield return new WaitForSecondsRealtime(.3f);yield return Shot("auto_target_dropdown");
        Press(root,"pn-settings-auto-target-option-lowest_health");yield return new WaitForSecondsRealtime(.25f);
        Check(PNSettingsService.Current.AutoTargetMode==PNTargetMode.Nearest&&PNTargetRules.LocalMode==PNTargetMode.Nearest,"auto target waits for Save");
        Check(root.Q("pn-settings-paper-shadow-trigger")!=null&&PNSettingsService.Current.PaperShadow==PNPaperShadowMode.Actors&&PNVisualPaperSettings.Mode==PNPaperShadowMode.Actors,"paper shadow row present, default every character, applied to the visual look");
        Press(root,"pn-settings-paper-shadow-trigger");yield return new WaitForSecondsRealtime(.3f);yield return Shot("paper_shadow_dropdown");
        Check(root.Q("pn-settings-paper-shadow-option-baked")!=null&&root.Q("pn-settings-paper-shadow-option-off")!=null&&root.Q("pn-settings-paper-shadow-option-key_actors")!=null&&root.Q("pn-settings-paper-shadow-option-actors")!=null&&root.Q("pn-settings-paper-shadow-option-all")!=null,"paper shadow offers five choices");
        Press(root,"pn-settings-paper-shadow-option-baked");yield return new WaitForSecondsRealtime(.25f);
        Check(root.Q("pn-settings-ink-trigger")!=null&&PNSettingsService.Current.InkMode==PNInkMode.Baked,"ink row present, default baked");
        Press(root,"pn-settings-ink-trigger");yield return new WaitForSecondsRealtime(.3f);yield return Shot("ink_dropdown");
        Check(new[]{"baked","live_key_actors","live_actors","live_all"}.All(id=>root.Q("pn-settings-ink-option-"+id)!=null),"ink offers four choices");
        Press(root,"pn-settings-ink-option-live_actors");yield return new WaitForSecondsRealtime(.25f);
        Check(root.Q<Button>("pn-settings-paper-shadow-option-baked")!=null&&root.Q("pn-settings-paper-shadow-trigger").enabledSelf,"shadow and ink are independent: baked shadow stays chosen with live ink");
        Check(PNSettingsService.Current.PaperShadow==PNPaperShadowMode.Actors&&PNSettingsService.Current.InkMode==PNInkMode.Baked,"nothing was applied before Save");
        yield return Shot("draft_before_save");
        Press(root,"pn-settings-save");yield return new WaitForSecondsRealtime(.3f);
        Check(PNLocalization.Selected=="en"&&Application.targetFrameRate==120&&QualitySettings.vSyncCount==0,"Save applies language and FPS");
        Check(PNSettingsService.Current.AutoTargetMode==PNTargetMode.LowestHealth&&PNTargetRules.LocalMode==PNTargetMode.LowestHealth,"Save applies the auto target mode");
        Check(PNSettingsService.Current.PaperShadow==PNPaperShadowMode.Baked&&PNVisualPaperSettings.Mode==PNPaperShadowMode.Baked&&PNSettingsService.Current.InkMode==PNInkMode.LiveActors&&PNVisualPaperSettings.Ink==PNInkMode.LiveActors,"Save applies shadow and ink to the visual look");
        var saved=new PNSettingsStore(settingsDir).Load().Value;
        Check(saved.Locale=="en"&&saved.TargetFrameRate==120&&saved.AutoTargetMode==PNTargetMode.LowestHealth&&saved.PaperShadow==PNPaperShadowMode.Baked&&saved.InkMode==PNInkMode.LiveActors,"Save wrote everything in one file");
        Check(!root.Q<Button>("pn-settings-save").enabledSelf,"Save is greyed again after saving");
        yield return Shot("after_save");
        // Unsaved changes: Back asks, Keep editing returns to the list, Discard leaves without changing anything.
        Press(root,"pn-settings-fps-trigger");yield return new WaitForSecondsRealtime(.3f);Press(root,"pn-settings-fps-option-60");yield return new WaitForSecondsRealtime(.2f);
        Press(root,"pn-menu-back");yield return new WaitForSecondsRealtime(.3f);
        Check(root.Q("pn-settings-view")!=null&&root.Q("pn-settings-unsaved").resolvedStyle.display==DisplayStyle.Flex,"Back with an unsaved change asks first");yield return Shot("unsaved_confirm");
        Press(root,"pn-settings-keep");yield return new WaitForSecondsRealtime(.2f);Check(root.Q("pn-settings-unsaved").resolvedStyle.display==DisplayStyle.None&&root.Q<Button>("pn-settings-save").enabledSelf,"Keep editing keeps the draft");
        Press(root,"pn-settings-fps-trigger");yield return new WaitForSecondsRealtime(.3f);Press(root,"pn-settings-fps-option-120");yield return new WaitForSecondsRealtime(.2f);
        Check(!root.Q<Button>("pn-settings-save").enabledSelf,"changing a value back greys Save");
        Press(root,"pn-settings-fps-trigger");yield return new WaitForSecondsRealtime(.3f);Press(root,"pn-settings-fps-option-30");yield return new WaitForSecondsRealtime(.2f);
        Press(root,"pn-menu-back");yield return new WaitForSecondsRealtime(.3f);Press(root,"pn-settings-discard");yield return new WaitForSecondsRealtime(1.1f);
        Check(root.Q("pn-settings-view")==null&&Application.targetFrameRate==120&&PNSettingsService.Current.TargetFrameRate==120&&new PNSettingsStore(settingsDir).Load().Value.TargetFrameRate==120,"Discard leaves without changing anything");
        Press(root,"pn-title-settings");yield return new WaitForSecondsRealtime(1.2f);
        foreach(int cap in new[]{30,60,90,120,144,0})
        {PNSettingsService.SetFrameRate(cap);yield return new WaitForSecondsRealtime(.2f);Check(Application.targetFrameRate==(cap==0?-1:cap)&&QualitySettings.vSyncCount==0,"frame cap "+cap);}
        PNSettingsService.SetFrameRate(120);
        PNSettingsService.SetFrameRate(120);
        yield return new WaitForSecondsRealtime(.3f);yield return Shot("idle_a");yield return new WaitForSecondsRealtime(.65f);yield return Shot("idle_b");
        Press(root,"pn-menu-back");yield return new WaitForSecondsRealtime(.1f);yield return Shot("exit_0_1s");yield return new WaitForSecondsRealtime(.15f);yield return Shot("exit_0_25s");yield return new WaitForSecondsRealtime(.15f);yield return Shot("exit_0_4s");yield return new WaitForSecondsRealtime(.2f);yield return Shot("exit_0_6s");yield return new WaitForSecondsRealtime(.6f);Check(root.Q("pn-settings-view")==null,"Back restores Title");
        Press(root,"pn-title-settings");yield return new WaitForSecondsRealtime(.12f);Press(root,"pn-menu-close");yield return new WaitForSecondsRealtime(1.3f);Check(root.Q("pn-title-column").resolvedStyle.display==DisplayStyle.Flex&&!PNGameFlow.Booting,"early Close returns Title without start");
        for(int i=0;i<3;i++){Press(root,"pn-title-settings");yield return new WaitForSecondsRealtime(.08f);Press(root,"pn-menu-back");yield return new WaitForSecondsRealtime(1.2f);Check(root.Q("pn-settings-view")==null,"reentry cycle "+i);}
        Press(root,"pn-title-start");yield return Until(()=>EveRegistrySession.IsReady&&PNGameFlow.Phase==PNFlowPhase.Hall,"Hall ready");
        var ui=PNGameBootstrap.Runtime.GetComponent<PNUiRuntime>();yield return new WaitForSecondsRealtime(.5f);
        string role=Arg("-pn-settings-role=");
        if(role=="host"||role=="join")
        {
            PNGameFlow.StartRoguelite(role=="host"?PNFlowMode.Host:PNFlowMode.Join,"localhost");
            yield return Until(()=>EveRegistrySession.IsReady&&PNGameFlow.Phase==PNFlowPhase.Lobby&&!PNGameFlow.Busy,"network lobby",120);
            ui=PNGameBootstrap.Runtime.GetComponent<PNUiRuntime>();
            yield return Until(()=>PNGameBootstrap.LobbyDriver?.Lobby?.Peers.Count>=2,"two peers",120);
        }
        yield return new WaitForSecondsRealtime(1);Press(ui.HudRoot,"toggle-pause");yield return new WaitForSecondsRealtime(1.15f);
        bool offline=EveRegistrySession.Context.Role==EveSessionRole.Offline;Check(offline?Time.timeScale==0:Time.timeScale>0,"pause role time policy");
        Press(ui.HudRoot,"pause-settings");yield return new WaitForSecondsRealtime(1.4f);yield return Shot("pause_settings");
        bool lobby=role=="host"||role=="join";
        Check(ui.HudRoot.Q("pn-settings-ink-trigger").enabledSelf==!lobby&&ui.HudRoot.Q("pn-settings-paper-shadow-trigger").enabledSelf==!lobby&&ui.HudRoot.Q("pn-settings-fps-trigger").enabledSelf,lobby?"look rows are locked in the lobby, the others are not":"look rows are enabled in the Hall pause menu");
        Press(ui.HudRoot,"pn-settings-language-trigger");yield return new WaitForSecondsRealtime(.3f);Press(ui.HudRoot,"pn-settings-language-option-vi");Check(PNLocalization.Selected!="vi","paused Settings: language waits for Save");
        Press(ui.HudRoot,"pn-settings-fps-trigger");yield return new WaitForSecondsRealtime(.3f);Press(ui.HudRoot,"pn-settings-fps-option-0");Check(Application.targetFrameRate!=-1,"paused Settings: FPS waits for Save");
        var inkBefore=PNVisualPaperSettings.Ink;int requestsBefore=PNVisualLookApply.Requests,epochBefore=PNVisualRuntime.Epoch,coversBefore=PNTransitionController.Current!=null?PNTransitionController.Current.ShowCount:0;
        if(!lobby)
        {
            Press(ui.HudRoot,"pn-settings-ink-trigger");yield return new WaitForSecondsRealtime(.3f);Press(ui.HudRoot,"pn-settings-ink-option-live_all");yield return new WaitForSecondsRealtime(.2f);
            Press(ui.HudRoot,"pn-settings-paper-shadow-trigger");yield return new WaitForSecondsRealtime(.3f);Press(ui.HudRoot,"pn-settings-paper-shadow-option-baked");yield return new WaitForSecondsRealtime(.2f);
            Check(PNVisualPaperSettings.Ink==inkBefore&&PNVisualRuntime.Epoch==epochBefore&&PNVisualLookApply.Requests==requestsBefore,"the look waits for Save in the Hall too");
        }
        Press(ui.HudRoot,"pn-settings-save");yield return new WaitForSecondsRealtime(.6f);Check(PNLocalization.Selected=="vi"&&Application.targetFrameRate==-1,"change locale and unlimited FPS with Save while paused");
        if(!lobby)
        {
            Check(PNVisualLookApply.Requests==requestsBefore+1&&PNVisualPaperSettings.Ink==PNInkMode.LiveAll&&PNVisualPaperSettings.Mode==PNPaperShadowMode.Baked,"Save in the Hall asked for a look rebuild");
            yield return Shot("look_cover_0");yield return new WaitForSecondsRealtime(.25f);yield return Shot("look_cover_1");
            yield return Until(()=>!PNVisualRuntime.RebuildActive&&PNVisualRuntime.Epoch>epochBefore,"the atlas set was rebuilt and the actors re-attached",60);
            Check(PNTransitionController.Current!=null&&PNTransitionController.Current.ShowCount>coversBefore,"the loading cover was shown for the rebuild");
            yield return Until(()=>!PNTransitionController.Current.IsShowing,"the cover lifted after the rebuild",60);
            var puppets=FindObjectsByType<PNVisualActor>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            Check(puppets.Length>0&&puppets.All(a=>a.LiveInk&&a.AtlasEpoch==PNVisualRuntime.Epoch),"every Hall puppet draws live ink on the new epoch ("+puppets.Length+")");
            yield return Shot("look_live_hall");
        }
        // Escape dismisses popup only; then Back restores pause, preserving its lock.
        Press(ui.HudRoot,"pn-settings-fps-trigger");yield return new WaitForSecondsRealtime(.3f);
        using(var e=KeyDownEvent.GetPooled('\0',KeyCode.Escape,EventModifiers.None)){e.target=ui.HudRoot.Q("pn-settings-view");ui.HudRoot.Q("pn-settings-view").SendEvent(e);}
        yield return new WaitForSecondsRealtime(.2f);Check(ui.HudRoot.Q("pn-settings-view")!=null,"Escape closes only dropdown");
        Press(ui.HudRoot,"pn-menu-back");yield return new WaitForSecondsRealtime(1.3f);Check(ui.HudRoot.Q("pn-settings-view")==null&&ui.MenuOpen,"Back returns pause without resume");Check(offline?Time.timeScale==0:Time.timeScale>0,"Back retains time policy");yield return Shot("pause_return");
        Press(ui.HudRoot,"pause-settings");yield return new WaitForSecondsRealtime(1.4f);Press(ui.HudRoot,"pn-menu-close");yield return new WaitForSecondsRealtime(.35f);yield return Shot("close_all_middle");yield return new WaitForSecondsRealtime(.6f);yield return Shot("close_all_fade");yield return new WaitForSecondsRealtime(1);Check(!ui.MenuOpen&&Time.timeScale>0,"CloseAll resumes");yield return Shot("resumed");
        PNSettingsService.SetLocale(role=="join"?"vi":"en");PNSettingsService.SetFrameRate(role=="join"?90:120);
        if(role=="host"||role=="join")
        {
            string pair=Arg("-pn-settings-pair=");if(pair!=null){Directory.CreateDirectory(pair);File.WriteAllText(Path.Combine(pair,role+".ready"),"ready");yield return Until(()=>File.Exists(Path.Combine(pair,role=="host"?"join.ready":"host.ready")),"both peers completed Settings");}
            Check(PNLocalization.Selected==(role=="join"?"vi":"en")&&PNSettingsService.Current.TargetFrameRate==(role=="join"?90:120),"peer-local language and FPS");
            yield return new WaitForSecondsRealtime(3);
        }
        else
        {
            Press(ui.HudRoot,"toggle-pause");yield return new WaitForSecondsRealtime(1.1f);Press(ui.HudRoot,"pause-settings");yield return new WaitForSecondsRealtime(.3f);
            PNGameFlow.StartRoguelite(PNFlowMode.Offline);
            yield return Until(()=>EveRegistrySession.IsReady&&PNGameFlow.Phase==PNFlowPhase.Run&&!PNGameFlow.Busy,"session transfer while Settings open");
            yield return new WaitForSecondsRealtime(.5f);Check(Time.timeScale>0&&PNGameBootstrap.Runtime.GetComponent<PNUiRuntime>().HudRoot.Q("pn-settings-view")==null,"transfer abort clears view and time lock");
        }
        Check(errors==0,"no Player errors");
    }
}
#endif
