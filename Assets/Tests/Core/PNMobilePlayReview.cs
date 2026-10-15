#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UIElements;

// Run only in Ready Play Mode. Checks execute in the player loop, not Editor input state.
public sealed class PNMobilePlayReview : MonoBehaviour
{
    public static string Status="Idle";
    private readonly List<string> evidence=new List<string>();
    private PNUiRuntime ui;
    private InputSettings previousSettings, settings;
    private HideFlags previousSettingsFlags;
    private Touchscreen touchscreen;
    private Keyboard keyboard;
    private PNPlayerMove reviewedMove;
    private Action jumpListener;
    private int jumps;
    private bool previousPreview, previousEnabled;
    private float previousTime;
    private int checks;
    private bool initialized;
    public static string Run()
    {
        if(!Application.isPlaying || EditorApplication.isPaused)throw new InvalidOperationException("Unpaused Play Mode required.");
        var review=new GameObject("Disposable mobile review").AddComponent<PNMobilePlayReview>();
        EditorApplication.update+=review.Pump;
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        Status="Running";return Status;
    }
    private void Start()=>StartCoroutine(Guarded());
    private void Pump()
    {
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Repaint();
        EditorApplication.QueuePlayerLoopUpdate();
    }
    private IEnumerator Guarded()
    {
        ui=UnityEngine.Object.FindAnyObjectByType<PNUiRuntime>();previousPreview=ui.ShowMobileControls;previousEnabled=ui.enabled;previousTime=Time.timeScale;
        initialized=true;
        previousSettings=InputSystem.settings;previousSettingsFlags=previousSettings.hideFlags;
        settings=Instantiate(previousSettings);
        // InputManager deletes default HideAndDontSave settings when replacing them.
        // Keep the original alive for both our cleanup and its cached Edit Mode state.
        if(previousSettingsFlags==HideFlags.HideAndDontSave)previousSettings.hideFlags=HideFlags.DontSave;
        InputSystem.settings=settings;
        settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        var sequence=Sequence();
        while(true)
        {
            object next;
            try { if(!sequence.MoveNext())break; next=sequence.Current; }
            catch(Exception e) { Status="FAIL "+e; evidence.Add(Status);break; }
            yield return next;
        }
        (sequence as IDisposable)?.Dispose();Cleanup();
        File.WriteAllLines(".utmp/CombatUIReview/MobileResults.txt",evidence.Concat(new[]{Status}));Destroy(gameObject);
    }
    private void Cleanup()
    {
        EditorApplication.update-=Pump;
        if(!initialized)return;
        if(ui!=null){ui.ShowMobileControls=previousPreview;ui.enabled=previousEnabled;if(ui.enabled)ui.RefreshUI();}
        Time.timeScale=previousTime;
        if(touchscreen!=null&&InputSystem.devices.Contains(touchscreen))InputSystem.RemoveDevice(touchscreen);
        if(keyboard!=null&&InputSystem.devices.Contains(keyboard))InputSystem.RemoveDevice(keyboard);
        if(reviewedMove!=null&&jumpListener!=null){reviewedMove.JumpStarted-=jumpListener;jumpListener=null;}
        if(previousSettings!=null){InputSystem.settings=previousSettings;previousSettings.hideFlags=previousSettingsFlags;}
        if(settings!=null){Destroy(settings);settings=null;}
    }
    private void OnDestroy()=>Cleanup();
    private void Check(bool ok,string message)
    { if(!ok)throw new Exception(message);checks++;evidence.Add("PASS "+message); }
    private Vector2 Center(string name)
    {
        var p=ui.HudRoot.Q(name).worldBound.center;float scale=Screen.width/ui.HudRoot.resolvedStyle.width;
        return new Vector2(p.x*scale,Screen.height-p.y*scale);
    }
    private void Touch(int id,UnityEngine.InputSystem.TouchPhase phase,Vector2 position)
        =>InputSystem.QueueStateEvent(touchscreen,new TouchState {touchId=id,phase=phase,position=position});
    private IEnumerator Sequence()
    {
        Time.timeScale=1;ui.ShowMobileControls=true;ui.RefreshUI();
        touchscreen=InputSystem.AddDevice<Touchscreen>("PNMobileReviewTouch");
        yield return new WaitForSecondsRealtime(.25f);
        var pad=(Gamepad)UnityEngine.Object.FindObjectsByType<PNMobileVirtualControl>().First().control.device;
        Check(ui.HudRoot.Q("mobile-buttons").childCount==5,"Five fixed placeholder buttons");
        foreach(var b in ui.HudRoot.Q("mobile-buttons").Children())
            Check(b.worldBound.xMin>=0&&b.worldBound.xMax<=ui.HudRoot.layout.width,"Button within screen: "+b.name);
        var buttonList=ui.HudRoot.Q("mobile-buttons").Children().ToArray();
        for(int i=0;i<buttonList.Length;i++)for(int j=i+1;j<buttonList.Length;j++)
        {
            var a=buttonList[i].worldBound;var b=buttonList[j].worldBound;
            float gap=Vector2.Distance(a.center,b.center)-(a.width+b.width)/2-11;
            Check(gap>=12,"Outer rings separated: "+buttonList[i].name+" / "+buttonList[j].name);
        }
        var start=ui.HudPlayer.transform.position;
        reviewedMove=ui.HudPlayer.GetComponent<PNPlayerMove>();jumpListener=()=>jumps++;reviewedMove.JumpStarted+=jumpListener;
        var origin=new Vector2(Screen.width*.20f,Screen.height*.22f);
        Touch(1,UnityEngine.InputSystem.TouchPhase.Began,origin);yield return new WaitForSecondsRealtime(.1f);
        var rim=ui.HudRoot.Q<PNMobileDirectionRing>("mobile-direction-ring");
        Check(rim.resolvedStyle.display==DisplayStyle.Flex&&!rim.HasDirection,"Outer direction rim visible without nub at center");
        Check(ui.HudRoot.Q("mobile-stick").Q(className:"mobile-outer-ring")==null,"Joystick has no extra fixed outer ring");
        ScreenCapture.CaptureScreenshot(".utmp/CombatUIReview/HudMinimal/MobileJoystickCenter.png");yield return null;
        float touchPixelsPerUi=Screen.width/ui.HudRoot.resolvedStyle.width;
        foreach(float distance in new[]{3f,15f,27.5f,50f})
        {
            Touch(1,UnityEngine.InputSystem.TouchPhase.Moved,origin+new Vector2(distance*touchPixelsPerUi,0));
            yield return new WaitForSecondsRealtime(.1f);
            float expected=distance<=5?0:distance==15?8f*10/45:distance==27.5f?4:8;
            Check(Mathf.Abs(rim.NubLength-expected)<.05f,"Direction protrusion scales with pull: "+distance+"px");
            string level=distance==3?"Deadzone":distance==15?"Light":distance==27.5f?"Mid":"Full";
            ScreenCapture.CaptureScreenshot(".utmp/CombatUIReview/HudMinimal/MobileJoystick"+level+".png");yield return null;
        }
        Touch(1,UnityEngine.InputSystem.TouchPhase.Moved,origin+new Vector2(25,0));yield return new WaitForSecondsRealtime(.25f);
        Check(pad.leftStick.ReadValue().x>.1f,"Native touchscreen drives virtual stick");
        Check(Vector3.Distance(start,ui.HudPlayer.transform.position)>.02f,"Actual player movement");
        Touch(1,UnityEngine.InputSystem.TouchPhase.Moved,origin+new Vector2(-25,0));yield return new WaitForSecondsRealtime(.15f);
        Check(pad.leftStick.ReadValue().x<-.1f,"Analog left input");
        Touch(1,UnityEngine.InputSystem.TouchPhase.Moved,origin+new Vector2(0,25));yield return new WaitForSecondsRealtime(.15f);
        Check(pad.leftStick.ReadValue().y>.1f,"Analog upward touch gives positive Y");
        Check(Mathf.Abs(ui.HudRoot.Q("mobile-direction-ring").style.rotate.value.angle.value+90)<1,"Direction rim points upward");
        ScreenCapture.CaptureScreenshot(".utmp/CombatUIReview/HudMinimal/MobileJoystickUp.png");yield return null;
        Touch(1,UnityEngine.InputSystem.TouchPhase.Moved,origin+new Vector2(0,-25));yield return new WaitForSecondsRealtime(.15f);
        Check(pad.leftStick.ReadValue().y<-.1f,"Analog downward touch gives negative Y");
        Check(Mathf.Abs(ui.HudRoot.Q("mobile-direction-ring").style.rotate.value.angle.value-90)<1,"Direction rim points downward");
        ScreenCapture.CaptureScreenshot(".utmp/CombatUIReview/HudMinimal/MobileJoystickDown.png");yield return null;
        Touch(1,UnityEngine.InputSystem.TouchPhase.Moved,origin+new Vector2(25,0));yield return new WaitForSecondsRealtime(.1f);
        Touch(2,UnityEngine.InputSystem.TouchPhase.Began,Center("mobile-attack"));yield return new WaitForSecondsRealtime(.15f);
        Check(pad.buttonEast.isPressed&&pad.leftStick.ReadValue().sqrMagnitude>.01f,"Move and Attack simultaneously");
        Touch(2,UnityEngine.InputSystem.TouchPhase.Moved,new Vector2(Screen.width*.5f,Screen.height*.75f));yield return new WaitForSecondsRealtime(.15f);
        Check(pad.buttonEast.isPressed,"Attack remains held outside button");
        Touch(3,UnityEngine.InputSystem.TouchPhase.Began,Center("mobile-dash"));yield return new WaitForSecondsRealtime(.15f);
        Check(pad.buttonSouth.isPressed&&pad.buttonEast.isPressed&&pad.leftStick.ReadValue().sqrMagnitude>.01f,"Three independent pointers");
        Touch(2,UnityEngine.InputSystem.TouchPhase.Ended,new Vector2(Screen.width*.5f,Screen.height*.75f));
        Touch(3,UnityEngine.InputSystem.TouchPhase.Ended,Center("mobile-dash"));yield return new WaitForSecondsRealtime(.1f);
        Check(!pad.buttonEast.isPressed&&!pad.buttonSouth.isPressed&&pad.leftStick.ReadValue().sqrMagnitude>.01f,"Release buttons without releasing joystick");
        ScreenCapture.CaptureScreenshot(".utmp/CombatUIReview/HudMinimal/MobileControls.png");yield return null;
        Time.timeScale=0;yield return new WaitForSecondsRealtime(.15f);
        Check(pad.leftStick.ReadValue()==Vector2.zero,"Pause resets joystick");
        Time.timeScale=1;Touch(1,UnityEngine.InputSystem.TouchPhase.Ended,origin);yield return new WaitForSecondsRealtime(.1f);
        var eve=ui.HudPlayer.GetComponent<EveCharacterSkill>();
        if(eve!=null){float deadline=Time.realtimeSinceStartup+3;while(eve.IsBusy&&Time.realtimeSinceStartup<deadline)yield return null;}
        Touch(4,UnityEngine.InputSystem.TouchPhase.Began,Center("mobile-ulti"));yield return new WaitForSecondsRealtime(.15f);
        var ultiControl=UnityEngine.Object.FindObjectsByType<PNMobileVirtualControl>().First(c=>c.controlPath==ui.HudPlayer.GetComponent<PNPlayerSkill>().PC.Controller.Ulti.bindings.First(b=>b.effectivePath.StartsWith("<Gamepad>/")).effectivePath);
        Check(((UnityEngine.InputSystem.Controls.ButtonControl)ultiControl.control).isPressed,"Ulti uses character effective gamepad binding");
        Check(jumps==0,"Ulti input does not trigger physical Jump");
        Touch(4,UnityEngine.InputSystem.TouchPhase.Ended,Center("mobile-ulti"));yield return new WaitForSecondsRealtime(.1f);
        if(eve!=null)Check(eve.CurrentCast==EveCast.Ultimate,"Mobile tap starts actual Eve Ultimate");
        Touch(5,UnityEngine.InputSystem.TouchPhase.Began,Center("mobile-skill"));yield return new WaitForSecondsRealtime(.1f);
        Check(pad.buttonWest.isPressed,"Skill retains buttonWest channel");
        ui.SendMessage("OnApplicationFocus",false);yield return new WaitForSecondsRealtime(.1f);
        Check(!pad.buttonWest.isPressed,"Focus loss resets held button");ui.SendMessage("OnApplicationFocus",true);
        Touch(5,UnityEngine.InputSystem.TouchPhase.Ended,Center("mobile-skill"));yield return new WaitForSecondsRealtime(.1f);
        Touch(6,UnityEngine.InputSystem.TouchPhase.Began,Center("mobile-burst"));yield return new WaitForSecondsRealtime(.1f);
        Check(pad.dpad.up.isPressed,"Burst retains dpad/up channel");
        Touch(6,UnityEngine.InputSystem.TouchPhase.Canceled,Center("mobile-burst"));yield return new WaitForSecondsRealtime(.1f);
        Check(!pad.dpad.up.isPressed,"Touch cancel resets held button");
        keyboard=InputSystem.AddDevice<Keyboard>("PNMobileReviewKeyboard");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Space));yield return new WaitForSecondsRealtime(.15f);
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return new WaitForSecondsRealtime(.1f);
        Check(jumps==0,"Space no longer triggers Jump");
        ui.enabled=false;yield return null;
        Check(!InputSystem.devices.Contains(pad),"Disabling UI releases virtual gamepad");
        // Exercise an immediate refresh before the newly rebuilt panel has laid out.
        ui.enabled=true;ui.RefreshUI();
        foreach(var document in UnityEngine.Object.FindObjectsByType<UIDocument>())
            if(document.name.StartsWith("Combat UI "))
            {
                var p=document.transform.position;
                Check(!float.IsNaN(p.x)&&!float.IsNaN(p.y)&&!float.IsNaN(p.z)&&
                    !float.IsInfinity(p.x)&&!float.IsInfinity(p.y)&&!float.IsInfinity(p.z),
                    "Immediate reenable keeps finite world position: "+document.name);
            }
        float layoutDeadline=Time.realtimeSinceStartup+5;
        while((float.IsNaN(ui.HudRoot.resolvedStyle.height)||float.IsInfinity(ui.HudRoot.resolvedStyle.height)||
            ui.HudRoot.resolvedStyle.height<=0)&&Time.realtimeSinceStartup<layoutDeadline)yield return null;
        float readyHeight=ui.HudRoot.resolvedStyle.height;
        Check(!float.IsNaN(readyHeight)&&!float.IsInfinity(readyHeight)&&readyHeight>0,"Reenabled HUD layout becomes ready");
        ui.RefreshUI();yield return null;
        Check(UnityEngine.Object.FindObjectsByType<UIDocument>().Any(d=>d.name.StartsWith("Combat UI ")&&
            d.rootVisualElement.resolvedStyle.display==DisplayStyle.Flex),"World UI resumes after layout");
        Check(UnityEngine.Object.FindObjectsByType<PNMobileVirtualControl>().Count(c=>c.control!=null)==6,"Reenable binds six virtual controls");
        Status="PASS "+checks+" native mobile checks";
    }
}
#endif
