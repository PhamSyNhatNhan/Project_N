#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EveEngine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// Opt-in development fixture. All preferences are isolated, virtual devices removed at exit.
public sealed class PNMobileStylePlayReview : MonoBehaviour
{
    private string output;private int checks,errors,shot;private Touchscreen touch;private EveContentContext modContent;
    private readonly List<string> trace=new List<string>();
    private static string Arg(string key)=>Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith(key,StringComparison.Ordinal))?.Substring(key.Length);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install(){string path=Arg("-pn-mobile-style-review=");if(path==null)return;var go=new GameObject("Mobile style review");DontDestroyOnLoad(go);go.AddComponent<PNMobileStylePlayReview>().output=path;}
    private void OnEnable()=>Application.logMessageReceived+=Log;
    private void OnDisable(){Application.logMessageReceived-=Log;if(touch!=null&&touch.added)InputSystem.RemoveDevice(touch);modContent?.Dispose();modContent=null;}
    private void Log(string c,string s,LogType t){if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert){errors++;trace.Add(t+": "+c);}}
    private void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;trace.Add("PASS "+label);}
    private static void Press(VisualElement root,string name){var b=root.Q<Button>(name)??throw new Exception(name);using(var e=NavigationSubmitEvent.GetPooled()){e.target=b;b.SendEvent(e);}}
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
        Check(Arg("-pn-settings-fixture=")!=null,"isolated settings");UIDocument doc=null;
        yield return Until(()=>{doc=FindObjectsByType<UIDocument>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(x=>x.rootVisualElement.Q("pn-title-start")!=null);return doc!=null&&PNLocalization.IsReady;},"Title ready");
        var root=doc.rootVisualElement;yield return Until(()=>root.Q<Button>("pn-title-settings").enabledInHierarchy,"Title Settings enabled");yield return new WaitForSecondsRealtime(.5f);Press(root,"pn-title-settings");yield return new WaitForSecondsRealtime(1.5f);yield return Shot("title_settings");
        Check(root.Q("pn-settings-mobile-icons-trigger")!=null&&root.Q("pn-settings-mobile-palette-trigger")!=null,"four rows before content");
        Press(root,"pn-settings-mobile-icons-trigger");yield return new WaitForSecondsRealtime(.3f);yield return Shot("icons_dropdown");Press(root,"pn-settings-mobile-icons-option-project_n:mobile_icons_letters");
        Press(root,"pn-settings-mobile-palette-trigger");yield return new WaitForSecondsRealtime(.3f);yield return Shot("palette_dropdown");Press(root,"pn-settings-mobile-palette-option-project_n:mobile_palette_k");
        Check(PNSettingsService.Current.MobileIconSet.EndsWith("letters")&&PNSettingsService.Current.MobilePalette.EndsWith("_k"),"dropdown selections apply");
        PNSettingsService.SetLocale("en");PNSettingsService.SetFrameRate(90);Check(PNSettingsService.Current.MobileIconSet.EndsWith("letters"),"language/FPS retain styles");
        Press(root,"pn-menu-back");yield return new WaitForSecondsRealtime(1.3f);Press(root,"pn-title-start");
        yield return Until(()=>EveRegistrySession.IsReady&&PNGameFlow.Phase==PNFlowPhase.Hall,"Hall ready");
        var ui=PNGameBootstrap.Runtime.GetComponent<PNUiRuntime>();ui.ShowMobileControls=true;ui.RefreshUI();
        yield return Until(()=>ui.HudPlayer!=null&&ui.HudRoot.Q("mobile-buttons").resolvedStyle.display==DisplayStyle.Flex,"mobile HUD visible");yield return new WaitForSecondsRealtime(.5f);
        Check(ui.HudRoot.Q("mobile-buttons").childCount==5,"five input buttons");
        foreach(var set in PNMobileStyleDefaults.IconSets)foreach(var palette in PNMobileStyleDefaults.Palettes)
        {PNSettingsService.SetMobileIconSet(set.Id.ToString());PNSettingsService.SetMobilePalette(palette.Id.ToString());yield return new WaitForSecondsRealtime(.2f);yield return Shot(set.Id.Path+"_"+palette.Id.Path);Check(true,"render "+set.Id+" "+palette.Id);}
        touch=InputSystem.AddDevice<Touchscreen>("MobileStyleTouch");
        var target=ui.HudRoot.Q("mobile-skill");var center=target.worldBound.center;float scale=Screen.width/ui.HudRoot.resolvedStyle.width;var pos=new Vector2(center.x*scale,Screen.height-center.y*scale);
        var control=FindObjectsByType<PNMobileVirtualControl>(FindObjectsSortMode.None).First(x=>x.controlPath=="<Gamepad>/buttonWest");
        InputSystem.QueueStateEvent(touch,new TouchState{touchId=1,phase=UnityEngine.InputSystem.TouchPhase.Began,position=pos});yield return new WaitForSecondsRealtime(.15f);
        Check(((UnityEngine.InputSystem.Controls.ButtonControl)control.control).isPressed,"touch drives skill");
        PNSettingsService.SetMobileIconSet(PNMobileStyleDefaults.IconSet);Check(((UnityEngine.InputSystem.Controls.ButtonControl)control.control).isPressed,"style change retains held control");yield return Shot("held");
        InputSystem.QueueStateEvent(touch,new TouchState{touchId=1,phase=UnityEngine.InputSystem.TouchPhase.Ended,position=pos});yield return new WaitForSecondsRealtime(.15f);Check(!((UnityEngine.InputSystem.Controls.ButtonControl)control.control).isPressed,"release clears input");
        Press(ui.HudRoot,"toggle-pause");yield return new WaitForSecondsRealtime(1.2f);Press(ui.HudRoot,"pause-settings");yield return new WaitForSecondsRealtime(1.5f);yield return Shot("pause_settings");
        Check(ui.HudRoot.Q("pn-settings-mobile-icons-trigger")!=null,"pause has style rows");
        Press(ui.HudRoot,"pn-settings-mobile-palette-trigger");yield return new WaitForSecondsRealtime(.3f);using(var e=KeyDownEvent.GetPooled('\0',KeyCode.Escape,EventModifiers.None)){e.target=ui.HudRoot.Q("pn-settings-view");ui.HudRoot.Q("pn-settings-view").SendEvent(e);}
        Check(ui.HudRoot.Q("pn-settings-view")!=null,"Escape dismisses popup only");
        Press(ui.HudRoot,"pn-menu-close");yield return new WaitForSecondsRealtime(1.3f);Check(!ui.MenuOpen&&Time.timeScale>0,"CloseAll resumes");
        string modPath=Arg("-pn-style-mods=");if(modPath!=null)
        {
            var source=new PNGameContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),modPath,new[]{"mobile_style_sample"});var packs=source.ReadPacks();
            var mods=new EveCodeModLoader("project_n").Load(packs);modContent=(EveContentContext)Activator.CreateInstance(typeof(EveContentContext),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{mods},null);
            var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNMobileUiContentModule()}.Concat(mods.ContentModules),Array.Empty<EveContentDocument>());
            typeof(EveContentContext).GetMethod("Publish",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(modContent,new object[]{hub,EveContentCompatibility.Create(hub,Array.Empty<EveContentDocument>(),"style-review",mods.Fingerprints)});
            typeof(PNMobileStyleCatalog).GetMethod("Publish",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{modContent});
            Check(PNMobileStyleCatalog.HasIconSet("mobile_style_sample:diamonds"),"native DLL registration");
            PNSettingsService.SetMobileIconSet("mobile_style_sample:diamonds");PNSettingsService.SetMobilePalette("mobile_style_sample:mint");yield return new WaitForSecondsRealtime(.3f);yield return Shot("mod_diamonds");
            modContent.Dispose();modContent=null;Check(PNSettingsService.Current.MobileIconSet=="mobile_style_sample:diamonds"&&PNMobileStyleCatalog.ResolveIconSet(PNSettingsService.Current.MobileIconSet).Id.ToString()==PNMobileStyleDefaults.IconSet,"logical unload fallback preserves requested ID");
            typeof(PNMobileStyleCatalog).GetMethod("Publish",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{PNGameBootstrap.Runtime.Content});
        }
        Check(errors==0,"no errors/assertions");
    }
}
#endif
