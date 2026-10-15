#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using EveEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Disposable render review in the selected Game/Simulator view; no scene assets written.
public sealed class PNPauseVisualReview : MonoBehaviour
{
    public static string Status="Idle";
    private PNUiRuntime ui;
    private string folder;
    private bool initialized, previousTest;
    private readonly System.Collections.Generic.List<string> results=new System.Collections.Generic.List<string>();
    private int checks;
    public static string Run()
    {
        if(!Application.isPlaying || !EveRegistrySession.IsReady || EditorApplication.isPaused)throw new InvalidOperationException("Ready Play Mode required.");
        var review=new GameObject("Disposable pause visual review").AddComponent<PNPauseVisualReview>();EditorApplication.update+=review.Pump;EditorApplication.QueuePlayerLoopUpdate();Status="Running";return Status;
    }
    private void Start()=>StartCoroutine(Guarded());
    private void Pump(){EditorApplication.QueuePlayerLoopUpdate();EditorWindow.focusedWindow?.Repaint();}
    private IEnumerator Guarded()
    {
        var sequence=Review();
        while(true)
        {
            object next;
            try{if(!sequence.MoveNext())break;next=sequence.Current;}
            catch(Exception error){Status="FAIL "+error;Debug.LogException(error);break;}
            yield return next;
        }
        (sequence as IDisposable)?.Dispose();Cleanup();
        if(folder!=null){results.Add(Status);File.WriteAllLines(Path.Combine(folder,"VisualResults.txt"),results);}
        Destroy(gameObject);
    }
    private void Check(bool valid,string message){if(!valid)throw new Exception(message);checks++;results.Add("PASS "+message);}
    public static Texture2D ReadSelectedView()
    {
        Texture2D texture;
        {
            // ScreenCapture uses simulated pixel dimensions against the Editor backbuffer.
            // Read the selected view's actual player render target instead.
            var type=typeof(EditorWindow).Assembly.GetType("UnityEditor.PlayModeView");
            var view=type.GetMethod("GetMainPlayModeView",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            RenderTexture target=null;
            for(var current=view.GetType();current!=null && target==null;current=current.BaseType)
                foreach(var field in current.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
                    if(typeof(RenderTexture).IsAssignableFrom(field.FieldType) && field.GetValue(view) is RenderTexture candidate && candidate.IsCreated())
                    {target=candidate;break;}
            if(target==null)throw new Exception("Selected Play view render target unavailable.");
            var previous=RenderTexture.active;texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try
            {
                RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);
                // The Editor player target uses top-origin UVs on this graphics backend.
                if(SystemInfo.graphicsUVStartsAtTop)
                {
                    var pixels=texture.GetPixels32();int width=target.width,height=target.height;
                    for(int y=0;y<height/2;y++)for(int x=0;x<width;x++)
                    {int a=y*width+x,b=(height-1-y)*width+x;var pixel=pixels[a];pixels[a]=pixels[b];pixels[b]=pixel;}
                    texture.SetPixels32(pixels);
                }
                texture.Apply();
            }
            catch{UnityEngine.Object.Destroy(texture);throw;}
            finally{RenderTexture.active=previous;}
        }

        if(texture==null)throw new Exception("No rendered frame available in current Play view.");
        return texture;
    }
    private void Capture(string name)
    {
        var texture=ReadSelectedView();
        try{File.WriteAllBytes(Path.Combine(folder,name+".png"),texture.EncodeToPNG());}finally{Destroy(texture);}
    }
    private IEnumerator Review()
    {
        ui=FindAnyObjectByType<PNUiRuntime>();previousTest=ui.TestExpanded;initialized=true;
        folder=Path.GetFullPath(".utmp/PauseCelestialReview");Directory.CreateDirectory(folder);
        ui.SetTestExpanded(false);ui.OpenMenu();yield return new WaitForSecondsRealtime(1.1f);
        bool simulated=PNMobilePlatform.UsesSimulatedScreen;
        var area=simulated?UnityEngine.Device.Screen.safeArea:Screen.safeArea;
        float sw=simulated?UnityEngine.Device.Screen.width:Screen.width,sh=simulated?UnityEngine.Device.Screen.height:Screen.height;
        float w=ui.HudRoot.resolvedStyle.width,h=ui.HudRoot.resolvedStyle.height;
        var bounds=ui.HudRoot.Q("pause-card").worldBound;
        var safe=new Rect(w*area.x/sw,h*(1-area.yMax/sh),w*area.width/sw,h*area.height/sh);
        Check(bounds.xMin>=safe.xMin && bounds.xMax<=safe.xMax && bounds.yMin>=safe.yMin && bounds.yMax<=safe.yMax,"Menu fits actual selected-view safe area; simulated="+simulated);
        Capture(simulated?"SimulatorOpen":"GameOpen");
        var menu=typeof(PNUiRuntime).GetField("pause",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
        var clock=menu.GetType().GetField("orbitTime",BindingFlags.Instance|BindingFlags.NonPublic);float previous=(float)clock.GetValue(menu);
        try
        {
            clock.SetValue(menu,2.86f);ui.RefreshUI();yield return null;
            Check(ui.HudRoot.Q("pause-star-front").style.visibility.value==Visibility.Visible && ui.HudRoot.Q("pause-star-back").style.visibility.value==Visibility.Hidden,"Front crossing renders complete star above core");Capture(simulated?"SimulatorStarFront":"GameStarFront");
            clock.SetValue(menu,15.86f);ui.RefreshUI();yield return null;
            Check(ui.HudRoot.Q("pause-star-back").style.visibility.value==Visibility.Visible && ui.HudRoot.Q("pause-star-front").style.visibility.value==Visibility.Hidden,"Rear crossing uses layer behind core");Capture(simulated?"SimulatorStarBack":"GameStarBack");
            var secondFront=ui.HudRoot.Q("pause-star-second-front");var secondBack=ui.HudRoot.Q("pause-star-second-back");
            Check(secondFront.style.visibility.value==Visibility.Visible && secondBack.style.visibility.value==Visibility.Hidden,"Second orbit has independent phase and front star");
            Check(secondFront.parent==ui.HudRoot.Q("pause-core").parent && secondFront.parent.IndexOf(secondFront)>secondFront.parent.IndexOf(ui.HudRoot.Q("pause-core")),"Second front star is complete sibling above sphere");
            Capture(simulated?"SimulatorSecondStarFront":"GameSecondStarFront");
            clock.SetValue(menu,2.86f);ui.RefreshUI();yield return null;
            Check(secondBack.style.visibility.value==Visibility.Visible && secondFront.style.visibility.value==Visibility.Hidden,"Second orbit rear crossing uses its own rear layer");
            Capture(simulated?"SimulatorSecondStarBack":"GameSecondStarBack");
        }
        finally{clock.SetValue(menu,previous);}
        ui.CloseMenu();yield return new WaitForSecondsRealtime(.7f);
        Check(!ui.MenuOpen && Time.timeScale>0,"Close completes and restores time in current view");
        var toggle=ui.HudRoot.Q("toggle-pause");
        Check(toggle.worldBound.width>=55 && toggle.worldBound.xMax<=safe.xMax && toggle.worldBound.yMin>=safe.yMin,"Sparkle button fits selected safe area with large hit region");Capture(simulated?"SimulatorClosed":"GameClosed");
        ui.OpenMenu();yield return new WaitForSecondsRealtime(.12f);
        float start=ui.HudRoot.Q("pause-core").style.scale.value.value.x;
        ui.CloseMenu();yield return null;
        Check(ui.HudRoot.Q("pause-core").style.scale.value.value.x<=start+.001f,"Interrupted opening closes from current scale without jump");
        yield return new WaitForSecondsRealtime(.65f);Check(!ui.MenuOpen,"Interrupted opening cleans up overlay");
        Status="PASS "+checks+" visual/selected-view checks; simulated="+simulated;
    }
    private void Cleanup(){EditorApplication.update-=Pump;if(!initialized)return;initialized=false;if(ui!=null){ui.CloseMenu();ui.SetTestExpanded(previousTest);}}
    private void OnDestroy()=>Cleanup();
}
#endif
