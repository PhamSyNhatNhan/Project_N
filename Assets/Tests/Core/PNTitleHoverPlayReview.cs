#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// Opt-in Player review: virtual pointer drives the real UI input module and :hover/:active.
public sealed class PNTitleHoverPlayReview : MonoBehaviour
{
    private string output;private int checks,errors,shot;private Mouse mouse;private Keyboard keyboard;
    private readonly List<string> trace=new List<string>();
    static string Arg(string p)=>Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith(p,StringComparison.Ordinal))?.Substring(p.Length);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install(){string path=Arg("-pn-title-hover-review=");if(path==null)return;var go=new GameObject("Title hover review");DontDestroyOnLoad(go);go.AddComponent<PNTitleHoverPlayReview>().output=path;}
    void OnEnable()=>Application.logMessageReceived+=Log;
    void OnDisable(){Application.logMessageReceived-=Log;if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);}
    void Log(string c,string s,LogType t){if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert){errors++;trace.Add(t+": "+c);}}
    void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;trace.Add("PASS "+label);}
    IEnumerator Shot(string label){yield return new WaitForEndOfFrame();var t=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,(shot++).ToString("00")+"_"+label+".png"),t.EncodeToPNG());Destroy(t);}
    void Point(Vector2 position,bool down=false){var state=new MouseState{position=position};if(down)state=state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left);InputSystem.QueueStateEvent(mouse,state);}
    static Vector2 Pixel(VisualElement root,VisualElement element){var c=root.WorldToLocal(element.worldBound.center);return new Vector2(c.x/root.resolvedStyle.width*Screen.width,Screen.height-c.y/root.resolvedStyle.height*Screen.height);}
    static void Press(VisualElement root,string name){var b=root.Q<Button>(name)??throw new Exception(name);using(var e=NavigationSubmitEvent.GetPooled()){e.target=b;b.SendEvent(e);}}
    IEnumerator Start()
    {
        Directory.CreateDirectory(output);Application.runInBackground=true;Exception failure=null;var stack=new Stack<IEnumerator>();stack.Push(Run());
        while(stack.Count>0){object item;try{if(!stack.Peek().MoveNext()){stack.Pop();continue;}item=stack.Peek().Current;if(item is IEnumerator child){stack.Push(child);continue;}}catch(Exception e){failure=e;break;}yield return item;}
        if(failure!=null)trace.Add("FAIL "+failure);trace.Add("checks="+checks+" errors="+errors+" size="+Screen.width+"x"+Screen.height);File.WriteAllText(Path.Combine(output,"result.txt"),string.Join("\n",trace));Application.Quit(failure==null&&errors==0?0:1);
    }
    IEnumerator Run()
    {
        mouse=InputSystem.AddDevice<Mouse>("TitleHoverReviewMouse");keyboard=InputSystem.AddDevice<Keyboard>("TitleHoverReviewKeyboard");Point(new Vector2(20,20));
        float end=Time.realtimeSinceStartup+60;UIDocument doc=null;
        while(doc==null||!PNLocalization.IsReady){doc=FindObjectsByType<UIDocument>(FindObjectsInactive.Include,FindObjectsSortMode.None).FirstOrDefault(d=>d.rootVisualElement.Q("pn-title-start")!=null);if(Time.realtimeSinceStartup>end)throw new TimeoutException("Title");yield return null;}
        var root=doc.rootVisualElement;yield return new WaitForSecondsRealtime(.6f);yield return Shot("idle");
        var first=root.Q<Button>("pn-title-start");Check(first.Q(className:"pn-title-hover-star").resolvedStyle.opacity<.01f,"initial programmatic focus is not hover");
        foreach(string name in new[]{"pn-title-start","pn-title-settings","pn-title-quit"})
        {
            var button=root.Q<Button>(name);var star=button.Q(className:"pn-title-hover-star");var underline=button.Q(className:"pn-title-underline");Vector2 pos=Pixel(root,button);
            Point(pos);yield return new WaitForSecondsRealtime(.08f);yield return Shot(name+"_hover_early");yield return new WaitForSecondsRealtime(.35f);yield return Shot(name+"_hover");
            Check(star.resolvedStyle.opacity>.95f&&underline.resolvedStyle.opacity>.95f,name+" hover star/underline visible");
            float fullWidth=button.worldBound.width;Point(pos,true);yield return new WaitForSecondsRealtime(.23f);yield return Shot(name+"_held");Check(button.worldBound.width<fullWidth*.995f,name+" held shrink");
            Point(new Vector2(20,20),true);yield return null;Point(new Vector2(20,20));yield return new WaitForSecondsRealtime(.4f);Check(!PNGameFlow.Booting,name+" cancelled press does not activate");
            Check(star.resolvedStyle.opacity<.01f&&underline.resolvedStyle.opacity<.01f,name+" leave clears hover");
        }
        first.Focus();InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Tab));yield return null;InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return new WaitForSecondsRealtime(.45f);yield return Shot("keyboard_focus");
        var focused=root.focusController.focusedElement as Button;Check(focused!=null&&focused.ClassListContains("pn-title-keyboard-focus")&&focused.Q(className:"pn-title-hover-star").resolvedStyle.opacity>.95f,"keyboard focus matches hover");
        // An actual pointer click opens the shared Settings; return preserves callbacks/focus.
        var settings=root.Q<Button>("pn-title-settings");Vector2 target=Pixel(root,settings);Point(target);yield return new WaitForSecondsRealtime(.4f);Point(target,true);yield return null;Point(target);yield return new WaitForSecondsRealtime(1.5f);
        Check(root.Q("pn-settings-view")!=null,"pointer click opens Settings");Press(root,"pn-menu-back");yield return new WaitForSecondsRealtime(1.3f);Check(root.Q("pn-settings-view")==null&&root.Q("pn-title-column").resolvedStyle.display==DisplayStyle.Flex,"Settings Back returns Title");
        yield return Shot("returned_title");Check(errors==0,"no errors or assertions");
        if(Arg("-pn-title-hover-start=")!=null){Press(root,"pn-title-start");float until=Time.realtimeSinceStartup+90;while(PNGameFlow.Phase!=PNFlowPhase.Hall||!EveEngine.EveRegistrySession.IsReady){if(Time.realtimeSinceStartup>until)throw new TimeoutException("Start callback");yield return null;}Check(true,"Start still boots Hall");}
    }
}
#endif
