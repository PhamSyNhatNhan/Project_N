#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// Opt-in development fixture; never runs without an explicit command-line flag.
public sealed class PNPcControlsPlayReview : MonoBehaviour
{
    private string output;private int checks,errors,shot;private Keyboard keyboard;private Gamepad pad;
    private readonly List<string> trace=new List<string>();
    private static string Arg(string key)=>Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith(key,StringComparison.Ordinal))?.Substring(key.Length);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install(){if(!Application.isEditor&&!Debug.isDebugBuild)return;string path=Arg("-pn-pc-controls-review=");if(path==null)return;var go=new GameObject("PC controls review");DontDestroyOnLoad(go);go.AddComponent<PNPcControlsPlayReview>().output=path;}
    private void OnEnable()=>Application.logMessageReceived+=Log;
    private void OnDisable(){Application.logMessageReceived-=Log;if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);if(pad!=null&&pad.added)InputSystem.RemoveDevice(pad);}
    private void Log(string c,string s,LogType t){if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert){errors++;trace.Add(t+": "+c);}}
    private void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;trace.Add("PASS "+label);}
    private static void Press(VisualElement root,string name){var button=root.Q<Button>(name)??throw new Exception(name);using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}}
    private IEnumerator Until(Func<bool> test,string label){float end=Time.realtimeSinceStartup+100;while(!test()){if(Time.realtimeSinceStartup>end)throw new TimeoutException(label);yield return null;}Check(true,label);}
    private IEnumerator Shot(string label){yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,(shot++).ToString("00")+"_"+label+".png"),image.EncodeToPNG());Destroy(image);}
    private IEnumerator Start()
    {
        Directory.CreateDirectory(output);Application.runInBackground=true;Exception failure=null;var stack=new Stack<IEnumerator>();stack.Push(Run());
        while(stack.Count>0){object item;try{if(!stack.Peek().MoveNext()){stack.Pop();continue;}item=stack.Peek().Current;if(item is IEnumerator nested){stack.Push(nested);continue;}}catch(Exception e){failure=e;break;}yield return item;}
        if(failure!=null)trace.Add("FAIL "+failure);trace.Add("checks="+checks+" errors="+errors+" size="+Screen.width+"x"+Screen.height);File.WriteAllText(Path.Combine(output,"result.txt"),string.Join("\n",trace));Application.Quit(failure==null&&errors==0?0:1);
    }
    private IEnumerator Run()
    {
        Check(Arg("-pn-settings-fixture=")!=null,"isolated settings root");UIDocument title=null;
        yield return Until(()=>{title=FindObjectsByType<UIDocument>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(d=>d.rootVisualElement.Q("pn-title-start")!=null);return title!=null&&PNLocalization.IsReady;},"Title ready");
        var root=title.rootVisualElement;yield return Until(()=>root.Q<Button>("pn-title-settings").enabledInHierarchy,"Title Settings enabled");
        if(Arg("-pn-pc-restart=")!=null){Check(PNSettingsService.Current.ShowPcControls,"restart restores PC HUD preference");Check(!PNSettingsService.Current.ShowPcBindingLabels,"restart restores hidden labels");yield return Shot("restart_title");yield break;}
        Check(!PNSettingsService.Current.ShowPcControls,"default off");Press(root,"pn-title-settings");yield return new WaitForSecondsRealtime(.15f);yield return Shot("settings_in_early");Check(root.Q("pn-settings-language-row").resolvedStyle.opacity<1,"content animates on open");yield return new WaitForSecondsRealtime(.3f);yield return Shot("settings_in_mid");yield return new WaitForSecondsRealtime(1.15f);
        Check(root.Q("pn-settings-pc-controls-trigger")!=null&&root.Q("pn-settings-mobile-icons-trigger")!=null,"desktop toggle and shared style rows");yield return Shot("title_settings_vi");
        root.Q<ScrollView>("pn-settings-rows").ScrollTo(root.Q("pn-settings-pc-controls-trigger"));yield return new WaitForSecondsRealtime(.2f);Press(root,"pn-settings-pc-controls-trigger");yield return new WaitForSecondsRealtime(.3f);yield return Shot("toggle_dropdown");Press(root,"pn-settings-pc-controls-option-on");
        Check(PNSettingsService.Current.ShowPcControls&&new PNSettingsStore(Path.GetDirectoryName(PNSettingsService.FilePath)).Load().Value.ShowPcControls,"toggle writes preference");
        Check(root.Q("pn-settings-section-general")!=null&&root.Q("pn-settings-section-combat")!=null&&root.Q("pn-settings-section-interface")!=null,"three settings sections");
        Check(root.Q("pn-settings-pc-binding-labels-trigger")!=null,"independent label row");
        var langRow=root.Q("pn-settings-language-row");var fpsRow=root.Q("pn-settings-fps-row");float logicalWidth=root.Q("pn-settings-view").resolvedStyle.width;
        Check(logicalWidth<900?fpsRow.worldBound.yMin>langRow.worldBound.yMin+10:Mathf.Abs(fpsRow.worldBound.yMin-langRow.worldBound.yMin)<2,"responsive section columns");

        var page=root.Q<ScrollView>("pn-settings-rows");page.scrollOffset=new Vector2(0,1000);yield return new WaitForSecondsRealtime(.25f);yield return Shot("settings_scrolled");
        Check(page.verticalScroller.resolvedStyle.opacity<=.51f,"scrollbar alpha bounded at half");yield return new WaitForSecondsRealtime(.9f);Check(page.verticalScroller.resolvedStyle.opacity<.01f,"scrollbar hides after idle");
        page.scrollOffset=Vector2.zero;yield return new WaitForSecondsRealtime(.2f);
        PNSettingsService.SetLocale("en");yield return new WaitForSecondsRealtime(.2f);yield return Shot("title_settings_en");Press(root,"pn-menu-back");yield return new WaitForSecondsRealtime(1.3f);Press(root,"pn-title-start");
        yield return Until(()=>EveRegistrySession.IsReady&&PNGameFlow.Phase==PNFlowPhase.Hall,"Hall ready");var ui=PNGameBootstrap.Runtime.GetComponent<PNUiRuntime>();
        yield return Until(()=>ui.HudPlayer!=null&&ui.HudRoot.Q("pc-controls").resolvedStyle.display==DisplayStyle.Flex,"PC HUD visible");yield return new WaitForSecondsRealtime(.5f);
        var cluster=ui.HudRoot.Q("pc-action-cluster");Check(cluster.Children().Count(e=>e.ClassListContains("pc-action-button"))==5,"exactly five buttons");
        Check(cluster.Query<VisualElement>().ToList().All(e=>e.pickingMode==PickingMode.Ignore),"HUD passes all pointer picking through");yield return Shot("idle_shapes");
        var keycaps=cluster.Query<Label>().ToList();var boxes=keycaps.Select(k=>k.worldBound.size).ToArray();
        trace.Add("Binding pixel sizes: "+string.Join(", ",boxes.Select(b=>b.x.ToString("F3")+"x"+b.y.ToString("F3"))));
        // worldBound is in panel units. Convert one screen pixel before comparing
        // independently rounded edges, including narrow/portrait panel scales.
        var pixelUnits=RuntimePanelUtils.ScreenToPanel(cluster.panel,Vector2.one)-RuntimePanelUtils.ScreenToPanel(cluster.panel,Vector2.zero);
        float pixelTolerance=Mathf.Max(Mathf.Abs(pixelUnits.x),Mathf.Abs(pixelUnits.y))*1.1f;
        Check(keycaps.Count==5&&boxes.All(b=>Mathf.Abs(b.x-b.y)<=pixelTolerance)&&boxes.Max(b=>b.x)-boxes.Min(b=>b.x)<=pixelTolerance&&boxes.Max(b=>b.y)-boxes.Min(b=>b.y)<=pixelTolerance,"equal binding boxes on pixel grid");
        string[] channels={"attack","skill","ulti","dash","burst"};
        foreach(var channel in channels){var b=cluster.Q("pc-"+channel).Q(className:"mobile-outer-ring").worldBound;var k=cluster.Q("pc-"+channel+"-binding").worldBound;float dx=Mathf.Max(b.xMin-k.xMax,k.xMin-b.xMax);float dy=Mathf.Max(b.yMin-k.yMax,k.yMin-b.yMax);Check(Mathf.Max(dx,dy)>pixelTolerance*.5f,"binding separated from outer ring "+channel);}
        var centre=cluster.Q("pc-centre-star");Check(centre!=null&&centre.resolvedStyle.display==DisplayStyle.None,"centre star hidden with labels shown");
        Check(cluster.Q("pc-attack-binding").worldBound.xMin>cluster.Q("pc-ulti-binding").worldBound.xMax,"I and J labels do not overlap");
        PNSettingsService.SetShowPcBindingLabels(false);yield return new WaitForSecondsRealtime(.2f);
        Check(keycaps.All(k=>k.resolvedStyle.display==DisplayStyle.None)&&ui.HudRoot.Q("pc-controls").resolvedStyle.display==DisplayStyle.Flex,"labels hide independently of icons");Check(centre.resolvedStyle.display==DisplayStyle.Flex&&centre.resolvedStyle.height>centre.resolvedStyle.width,"tall pause star shown without labels");yield return Shot("labels_hidden");
        keyboard=InputSystem.AddDevice<Keyboard>("PcHudReviewKeyboard");pad=InputSystem.AddDevice<Gamepad>("PcHudReviewPad");
        var skill=ui.HudPlayer.GetComponent<PNPlayerSkill>();var actions=skill.PC.Controller;var list=new[]{actions.Attack,actions.Skill,actions.Ulti,actions.Dash,actions.Burst};string[] names={"attack","skill","ulti","dash","burst"};var keys=new Key[5];
        for(int i=0;i<list.Length;i++)
        {
            var action=list[i];int binding=-1;for(int b=0;b<action.bindings.Count;b++)if(action.bindings[b].effectivePath.StartsWith("<Keyboard>/",StringComparison.Ordinal)){binding=b;break;}
            Check(binding>=0,"keyboard binding "+names[i]);var control=InputSystem.FindControl(action.bindings[binding].effectivePath) as UnityEngine.InputSystem.Controls.KeyControl;
            Check(control!=null,"key resolved "+names[i]);keys[i]=control.keyCode;InputSystem.QueueStateEvent(keyboard,new KeyboardState(control.keyCode));yield return new WaitForSecondsRealtime(.16f);
            Check(ui.HudRoot.Q("pc-"+names[i]).ClassListContains("mobile-pressed"),"press "+names[i]);if(i==0){yield return new WaitForSecondsRealtime(.5f);yield return Shot("attack_held");}
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return new WaitForSecondsRealtime(.16f);Check(!ui.HudRoot.Q("pc-"+names[i]).ClassListContains("mobile-pressed"),"release "+names[i]);
        }
        PNSettingsService.SetShowPcBindingLabels(true);yield return new WaitForSecondsRealtime(.2f);Check(keycaps.All(k=>k.resolvedStyle.display==DisplayStyle.Flex),"labels restored live");Check(centre.resolvedStyle.display==DisplayStyle.None,"restoring labels hides centre decoration");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys[0],keys[1]));yield return new WaitForSecondsRealtime(.16f);
        Check(ui.HudRoot.Q("pc-attack").ClassListContains("mobile-pressed")&&ui.HudRoot.Q("pc-skill").ClassListContains("mobile-pressed"),"simultaneous key feedback");
        ui.SendMessage("OnApplicationFocus",false);Check(!ui.HudRoot.Q("pc-attack").ClassListContains("mobile-pressed")&&!ui.HudRoot.Q("pc-skill").ClassListContains("mobile-pressed"),"focus callback clears actively held visuals");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return new WaitForSecondsRealtime(.16f);
        int skillBinding=Enumerable.Range(0,actions.Skill.bindings.Count).First(b=>actions.Skill.bindings[b].effectivePath.StartsWith("<Keyboard>/",StringComparison.Ordinal));
        actions.Skill.ApplyBindingOverride(skillBinding,"<Keyboard>/space");yield return new WaitForSecondsRealtime(.2f);Check(ui.HudRoot.Q<Label>("pc-skill-binding").text=="Spc","binding override updates live label");yield return Shot("space_override");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Space));yield return new WaitForSecondsRealtime(.2f);Check(ui.HudRoot.Q("pc-skill").ClassListContains("mobile-pressed"),"override drives read-only feedback");
        Press(ui.HudRoot,"toggle-pause");yield return new WaitForSecondsRealtime(1.2f);Check(ui.HudRoot.Q("pc-controls").resolvedStyle.display==DisplayStyle.None&&!ui.HudRoot.Q("pc-skill").ClassListContains("mobile-pressed"),"menu clears held feedback");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());Press(ui.HudRoot,"pause-settings");yield return new WaitForSecondsRealtime(1.5f);Check(ui.HudRoot.Q("pn-settings-pc-controls-trigger")!=null,"shared pause setting");yield return Shot("pause_settings");
        var pausePage=ui.HudRoot.Q<ScrollView>("pn-settings-rows");pausePage.scrollOffset=new Vector2(0,1000);yield return new WaitForSecondsRealtime(.2f);Press(ui.HudRoot,"pn-settings-pc-binding-labels-trigger");yield return new WaitForSecondsRealtime(.3f);yield return Shot("binding_dropdown");Press(ui.HudRoot,"pn-settings-pc-binding-labels-option-off");Check(!PNSettingsService.Current.ShowPcBindingLabels,"pause dropdown saves hidden labels");Press(ui.HudRoot,"pn-menu-close");yield return new WaitForSecondsRealtime(.35f);Check(ui.HudRoot.Q("pn-settings-view")!=null,"out remains present halfway");yield return Shot("settings_out_mid");yield return new WaitForSecondsRealtime(.45f);Check(ui.HudRoot.Q("pn-settings-view")==null,"out completes within 0.8 seconds");yield return new WaitForSecondsRealtime(.6f);
        actions.Skill.RemoveBindingOverride(skillBinding);yield return new WaitForSecondsRealtime(.2f);Check(!ui.MenuOpen&&ui.HudRoot.Q("pc-controls").resolvedStyle.display==DisplayStyle.Flex,"resume restores HUD");
        InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.North));yield return new WaitForSecondsRealtime(.2f);Check(!ui.MenuOpen&&ui.HudRoot.Q("pc-controls").resolvedStyle.display==DisplayStyle.Flex&&!ui.HudRoot.Q("pc-ulti").ClassListContains("mobile-pressed"),"gamepad does not illuminate keyboard label");InputSystem.QueueStateEvent(pad,new GamepadState());
        PNSettingsService.SetMobileIconSet("project_n:mobile_icons_letters");yield return new WaitForSecondsRealtime(.25f);yield return Shot("letters");
        foreach(string palette in new[]{"d","a","k"}){PNSettingsService.SetMobilePalette("project_n:mobile_palette_"+palette);yield return new WaitForSecondsRealtime(.2f);yield return Shot("palette_"+palette);}PNSettingsService.SetMobilePalette(PNMobileStyleDefaults.Palette);
        ui.ShowMobileControls=true;ui.RefreshUI();yield return new WaitForSecondsRealtime(.25f);Check(ui.HudRoot.Q("pc-controls").resolvedStyle.display==DisplayStyle.None&&ui.HudRoot.Q("mobile-controls").resolvedStyle.display==DisplayStyle.Flex,"mobile preview takes priority");yield return Shot("mobile_regression");ui.ShowMobileControls=false;
        PNSettingsService.SetShowPcControls(false);yield return null;Check(ui.HudRoot.Q("pc-controls").resolvedStyle.display==DisplayStyle.None,"toggle hides immediately");PNSettingsService.SetShowPcControls(true);PNSettingsService.SetMobileIconSet(PNMobileStyleDefaults.IconSet);
        ui.Unbind();Check(ui.HudRoot==null,"UI unbind disposes HUD");ui.Bind(EveRegistrySession.Context);yield return Until(()=>ui.HudPlayer!=null,"rebind player");yield return new WaitForSecondsRealtime(.2f);Check(ui.HudRoot.Q("pc-action-cluster").childCount==11,"rebind creates five buttons and one centre star");yield return Shot("rebound");
        Check(ui.HudRoot.Query<VisualElement>("pc-centre-star").ToList().Count==1,"rebind has one centre decoration");
        Check(!new PNSettingsStore(Path.GetDirectoryName(PNSettingsService.FilePath)).Load().Value.ShowPcBindingLabels,"hidden labels persisted after other setters and rebind");
        Check(errors==0,"no Player errors/asserts");
    }
}
#endif
