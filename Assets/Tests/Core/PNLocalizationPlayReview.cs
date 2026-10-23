#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.Localization.Settings;
using UnityEngine.UIElements;

// Opt-in real Player verification; isolated Player settings belong to the clone build runner.
public sealed class PNLocalizationPlayReview : MonoBehaviour
{
    private string output;
    private int checks,errors;
    private readonly List<string> trace=new List<string>();
    private static string Arg(string prefix)=>Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith(prefix,StringComparison.Ordinal))?.Substring(prefix.Length);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install(){string path=Arg("-pn-localization-review=");if(path==null)return;var go=new GameObject("Localization Review");DontDestroyOnLoad(go);go.AddComponent<PNLocalizationPlayReview>().output=path;}
    private void OnEnable()=>Application.logMessageReceived+=Log;
    private void OnDisable()=>Application.logMessageReceived-=Log;
    private void Log(string c,string s,LogType t){if(t==LogType.Error||t==LogType.Exception){errors++;trace.Add(t+": "+c);}}
    private void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;trace.Add("PASS "+label);}
    private IEnumerator Until(Func<bool> ready,string label,float seconds=90)
    {float until=Time.realtimeSinceStartup+seconds;while(!ready()){if(Time.realtimeSinceStartup>until)throw new TimeoutException(label);yield return null;}Check(true,label);}
    private static UIDocument Title()=>FindObjectsByType<UIDocument>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(d=>d.rootVisualElement?.Q("pn-title-start")!=null);
    private static void Press(VisualElement root,string name)
    {var button=root.Q<Button>(name)??throw new Exception("Missing button "+name);using(var e=NavigationSubmitEvent.GetPooled()){e.target=button;button.SendEvent(e);}}
    private IEnumerator Shot(string label){yield return new WaitForSecondsRealtime(.8f);yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,label+".png"),image.EncodeToPNG());Destroy(image);}
    private IEnumerator Start()
    {
        Directory.CreateDirectory(output);Application.runInBackground=true;
        Exception failure=null;var run=Run();
        while(true){bool more;object next=null;try{more=run.MoveNext();if(more)next=run.Current;}catch(Exception e){failure=e;break;}if(!more)break;
            // Flatten nested iterators so every assertion/timeout reaches the result file.
            if(next is IEnumerator child){var stack=new Stack<IEnumerator>();stack.Push(child);while(stack.Count>0){try{if(!stack.Peek().MoveNext()){stack.Pop();continue;}var item=stack.Peek().Current;if(item is IEnumerator nested){stack.Push(nested);continue;}next=item;}catch(Exception e){failure=e;break;}yield return next;}if(failure!=null)break;}
            else yield return next;
        }
        if(failure!=null)trace.Add("FAIL "+failure);
        trace.Add("checks="+checks+" errors="+errors);
        File.WriteAllText(Path.Combine(output,"result.txt"),string.Join("\n",trace));Application.Quit(failure==null&&errors==0?0:1);
    }
    private IEnumerator Run()
    {
        yield return Until(()=>PNLocalization.IsReady&&Title()!=null,"Title base localization ready");
        PNLocalization.Select("vi",false);var root=Title().rootVisualElement;
        Check(root.Q<Button>("pn-title-start").text=="Bắt đầu","Title vi binding");yield return Shot("01_title_vi");
        Press(root,"pn-title-settings");yield return new WaitForSecondsRealtime(1.4f);Press(root,"pn-settings-language-trigger");yield return new WaitForSecondsRealtime(.3f);Press(root,"pn-settings-language-option-en");
        Check(PNLocalization.Selected=="en"&&root.Q<Button>("pn-title-start").text=="Start","language button updates attached Title");yield return Shot("02_languages_en");
        Press(root,"pn-menu-back");yield return new WaitForSecondsRealtime(1.2f);yield return Shot("03_title_en");
        string mods=Path.Combine(Application.persistentDataPath,"Mods"),pack=Path.Combine(mods,"localization_review"),loc=Path.Combine(pack,"Localization");Directory.CreateDirectory(loc);
        File.WriteAllText(Path.Combine(pack,"manifest.json"),"{\"schemaVersion\":1,\"namespace\":\"loc_review\",\"version\":\"1.0.0\",\"dependencies\":{\"project_n\":\"1.0.0\"}}");
        File.WriteAllText(Path.Combine(mods,"enabled.json"),"{\"schemaVersion\":1,\"enabledMods\":[\"localization_review\"]}");
        File.WriteAllText(Path.Combine(loc,"languages.json"),"{\"schemaVersion\":1,\"languages\":[{\"code\":\"fr\",\"nativeName\":\"Français\",\"fontProfile\":\"default\"}],\"overrides\":[{\"locale\":\"fr\",\"keys\":[\"project_n:ui.start\"]}]}");
        File.WriteAllText(Path.Combine(loc,"fr.json"),"{\"loc_review:greeting\":\"Bonjour\",\"project_n:ui.start\":\"Jouer\"}");
        File.WriteAllText(Path.Combine(loc,"en.json"),"{\"loc_review:plural\":{\"text\":\"{count} {count:plural:item|items}\",\"smart\":true}}");
        trace.Add("Isolated JSON mod fixture: "+pack);
        Check(LocalizationSettings.StringDatabase.GetLocalizedString(PNLocalization.CollectionName,"project_n:ui.start")=="Start","Unity database uses JSON provider");
        Check(PNLocalization.Text("project_n:example.damage",new Dictionary<string,object>{{"damage",12},{"duration",3}})=="Deal 12 damage over 3 seconds.","Smart Strings named parameters");
        Check(PNLocalization.Text("project_n:missing")=="[project_n:missing]","missing key marker");
        string urlText=null;Exception urlFailure=null;
        yield return PNLocalizationJsonReader.Read(new Uri(PNLocalization.BaseDirectory.TrimEnd('/','\\')+"/").AbsoluteUri,"vi.json",value=>urlText=value,error=>urlFailure=error);
        Check(urlFailure==null&&urlText!=null&&urlText.Contains("Bắt đầu"),"URL reader UTF-8 file path");
        Press(root,"pn-title-start");
        yield return Until(()=>EveRegistrySession.IsReady&&PNLocalWorldContext.Current?.Actor?.IsReady==true,"actual Hall boot and actor ready");
        Check(PNLocalization.Selected=="en","locale survives content publication");yield return Shot("04_hall_en");
        Check(PNLocalization.Languages.Any(l=>l.Code=="fr"),"JSON mod declares new language");PNLocalization.Select("fr",false);
        Check(PNLocalization.Text("project_n:ui.start")=="Jouer"&&PNLocalization.Text("loc_review:greeting")=="Bonjour","mod namespace and explicit override in Player");
        Check(PNLocalization.Text("project_n:ui.resume")=="Resume","mod locale fallback per key");PNLocalization.Select("en",false);
        PNLocalization.Select("fr",false);Check(PNLocalization.Text("loc_review:plural",new Dictionary<string,object>{{"count",0}})=="0 items","fallback uses source-language plural grammar");PNLocalization.Select("en",false);
        var ui=PNGameBootstrap.Runtime.GetComponent<PNUiRuntime>();ui.OpenMenu();yield return Shot("05_pause_en");
        Check(ui.HudRoot.Q<Button>("pause-continue").text=="Resume","pause English binding");
        Press(ui.HudRoot,"pause-settings");yield return new WaitForSecondsRealtime(1.4f);Press(ui.HudRoot,"pn-settings-language-trigger");yield return new WaitForSecondsRealtime(.3f);Press(ui.HudRoot,"pn-settings-language-option-vi");
        Check(Time.timeScale==0&&PNLocalization.Selected=="vi","switch while game paused");yield return Shot("06_pause_language_vi");
        Press(ui.HudRoot,"pn-menu-back");yield return new WaitForSecondsRealtime(1.2f);Check(ui.HudRoot.Q<Button>("pause-continue").text=="Tiếp tục","pause Vietnamese binding");yield return Shot("07_pause_vi");
        ui.CloseMenu();yield return new WaitForSecondsRealtime(1.2f);
        Check(Time.timeScale>0,"pause restores time");
        var controller=new PNMenuController(ui);Check(controller.OpenHall(),"open mode ring");yield return new WaitForSecondsRealtime(1.2f);yield return Shot("08_modes_vi");
        var flow=ui.MenuFlow;var mode=flow.Current as PNModeScreen;Check(mode!=null,"mode screen type");mode.Pick();yield return new WaitForSecondsRealtime(1.2f);
        PNLocalization.Select("en",false);yield return Shot("09_setup_en");var setup=flow.Current as PNSetupScreen;Check(setup!=null,"setup screen type");setup.Choose(0);yield return new WaitForSecondsRealtime(1.2f);yield return Shot("10_character_en");
        Check(ui.HudRoot.Q<Button>("pn-char-continue").text=="Continue","character continue English");
        PNLocalization.Select("vi",false);yield return Shot("11_character_vi");flow.Close();yield return new WaitForSecondsRealtime(1.2f);
        // Loaded content survives EndPlay, then a true unload drops mod/base generation resources safely.
        var coordinator=FindAnyObjectByType<PNGameFlow>();bool coordinatorEnabled=coordinator!=null&&coordinator.enabled;if(coordinator!=null)coordinator.enabled=false;
        var runtime=PNGameBootstrap.Runtime;runtime.EndSession();Check(PNLocalization.IsReady,"EndPlay retains localization");
        yield return Until(()=>{if(EveRegistrySession.State==EveSessionState.Stopping)EveRegistrySession.PumpCleanup();return EveRegistrySession.State==EveSessionState.Stopped;},"session cleanup drained");
        runtime.UnloadContent();Check(runtime.Content==null&&PNLocalization.IsReady&&PNLocalization.Selected=="vi"&&!PNLocalization.Languages.Any(l=>l.Code=="fr"),"content unload restores base and removes mod locale");
        runtime.EnsureContentLoaded();Check(runtime.IsContentReady,"content reload ready");Check(PNLocalization.Text("project_n:ui.start")=="Bắt đầu","content reload text valid");
        runtime.UnloadContent();
        for(int i=0;i<5;i++){PNLocalization.Shutdown();PNLocalization.EnsureBase();PNLocalization.Select(i%2==0?"en":"vi",false);Check(PNLocalization.Text("project_n:ui.start")== (i%2==0?"Start":"Bắt đầu"),"app reinitialize "+i);}
        yield return null;yield return null;
        Check(Resources.FindObjectsOfTypeAll<PNJsonLocalizationSettings>().Length==1,"one settings owner after reinitialize");
        Check(Resources.FindObjectsOfTypeAll<UnityEngine.Localization.Tables.StringTable>().Count(t=>t.hideFlags==HideFlags.HideAndDontSave)==2,"only base locale tables remain");
        if(coordinator!=null)coordinator.enabled=coordinatorEnabled;
        Check(errors==0,"no Player errors");
    }
}
#endif
