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

// Disposable native input/movement review; no authored scene is saved or reloaded.
public sealed class PNPausePlayReview : MonoBehaviour
{
    public static string Status="Idle";
    private readonly List<string> evidence=new List<string>();
    private readonly List<Action> restoreDevices=new List<Action>();
    private PNUiRuntime ui;
    private PNPlayerMove move;
    private PNPlayerSkill skill, remote;
    private Scene scene;
    private Keyboard keyboard;
    private Touchscreen touch;
    private Gamepad pad;
    private Mouse mouse;
    private bool mouseOnly;
    private InputSettings previousSettings, settings;
    private HideFlags previousFlags;
    private long previousOwner;
    private EveEntityId previousPlayer;
    private float previousTime;
    private bool previousMobile, previousTest;
    private EveSessionRole previousRole;
    private FieldInfo roleField;
    private int checks;
    private bool initialized;
    private string folder;
    public static string Run()
    {
        if(!Application.isPlaying || EditorApplication.isPaused || !EveRegistrySession.IsReady)throw new InvalidOperationException("Ready unpaused Play Mode required.");
        var review=new GameObject("Disposable pause review").AddComponent<PNPausePlayReview>();
        EditorApplication.update+=review.Pump; Status="Running"; return Status;
    }
    public static string RunMouse()
    {
        if(!Application.isPlaying || EditorApplication.isPaused || !EveRegistrySession.IsReady)throw new InvalidOperationException("Ready unpaused Play Mode required.");
        if(UnityEngine.Device.Application.isMobilePlatform)throw new InvalidOperationException("Select Game in the Game/Simulator toolbar before RunMouse; focusing Game View alone can leave Simulator active.");
        var review=new GameObject("Disposable PC mouse review").AddComponent<PNPausePlayReview>();review.mouseOnly=true;
        EditorApplication.update+=review.Pump;Status="Running mouse";return Status;
    }
    private void Start()=>StartCoroutine(Guarded());
    private void Pump()
    {
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Repaint();
        EditorApplication.QueuePlayerLoopUpdate();
    }
    private IEnumerator Guarded()
    {
        var sequence=mouseOnly?MouseSequence():Sequence();
        while(true)
        {
            object next;
            try { if(!sequence.MoveNext())break;next=sequence.Current; }
            catch(Exception error){Status="FAIL "+error;evidence.Add(Status);Debug.LogException(error);break;}
            yield return next;
        }
        (sequence as IDisposable)?.Dispose();Cleanup();
        Directory.CreateDirectory(folder??".utmp/PauseReview");File.WriteAllLines(Path.Combine(folder??".utmp/PauseReview","Results.txt"),evidence.Concat(new[]{Status}));Destroy(gameObject);
    }
    private void Check(bool ok,string message)
    {
        if(!ok)throw new Exception(message);checks++;evidence.Add("PASS "+message);
        File.AppendAllText(Path.Combine(folder,"Progress.txt"),"PASS "+message+Environment.NewLine);
    }
    private void Keys(params Key[] keys)=>InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys));
    private void Finger(int id,UnityEngine.InputSystem.TouchPhase phase,string element)
    {
        var center=ui.HudRoot.Q(element).worldBound.center;
        var p=new Vector2(center.x*Screen.width/ui.HudRoot.resolvedStyle.width,Screen.height-center.y*Screen.height/ui.HudRoot.resolvedStyle.height);
        InputSystem.QueueStateEvent(touch,new TouchState { touchId=id,phase=phase,position=p });
    }
    private IEnumerator Capture(string name)
    {
        // WaitForEndOfFrame can stall when the Editor's Game View loses focus.
        var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG());Destroy(image);yield return null;
    }
    private IEnumerator MouseClick(string element)
    {
        var center=ui.HudRoot.Q(element).worldBound.center;
        var position=new Vector2(center.x*Screen.width/ui.HudRoot.resolvedStyle.width,Screen.height-center.y*Screen.height/ui.HudRoot.resolvedStyle.height);
        InputSystem.QueueStateEvent(mouse,new MouseState { position=position });yield return new WaitForSecondsRealtime(.05f);
        InputSystem.QueueStateEvent(mouse,new MouseState { position=position }.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));yield return new WaitForSecondsRealtime(.05f);
        InputSystem.QueueStateEvent(mouse,new MouseState { position=position });yield return new WaitForSecondsRealtime(.7f);
    }
    private IEnumerator MouseSequence()
    {
        ui=UnityEngine.Object.FindAnyObjectByType<PNUiRuntime>();folder=Path.GetFullPath(".utmp/PauseMouseReview");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"Progress.txt"),"");
        previousOwner=ui.LocalOwnerId;previousPlayer=ui.LocalPlayerId;previousTime=Time.timeScale;previousMobile=ui.ShowMobileControls;previousTest=ui.TestExpanded;
        previousRole=EveRegistrySession.Context.Role;initialized=true;
        ui.enabled=false;
        previousSettings=InputSystem.settings;previousFlags=previousSettings.hideFlags;
        if(previousFlags==HideFlags.HideAndDontSave)previousSettings.hideFlags=HideFlags.DontSave;
        settings=Instantiate(previousSettings);InputSystem.settings=settings;
        settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        mouse=InputSystem.AddDevice<Mouse>("PauseReviewMouse");
        var gameView=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));gameView.Show();gameView.Focus();
        ui.ShowMobileControls=false;ui.enabled=true;ui.SetTestExpanded(false);Time.timeScale=1;
        yield return new WaitForSecondsRealtime(.3f);
        Check(!UnityEngine.Device.Application.isMobilePlatform && ui.HudRoot.Q("mobile-controls").resolvedStyle.display==DisplayStyle.None,"PC Game View mode; mobile touch layer hidden");
        yield return MouseClick("toggle-pause");
        Check(ui.MenuOpen && Time.timeScale==0,"Native Mouse position/leftButton opens menu");
        yield return MouseClick("pause-settings");
        Check(ui.MenuOpen && !ui.HudRoot.Q<Button>("pause-settings").enabledSelf,"Disabled Settings does not close menu");
        yield return MouseClick("pause-leave");
        Check(ui.MenuOpen && !ui.HudRoot.Q<Button>("pause-leave").enabledSelf,"Disabled Leave does not close menu");
        yield return MouseClick("pause-overlay");Check(ui.MenuOpen,"PC background click does not close menu");
        yield return MouseClick("pause-continue");Check(!ui.MenuOpen && Time.timeScale==1,"Native Mouse Continue closes menu while game time was zero");
        yield return MouseClick("toggle-test");Check(ui.TestExpanded,"Native Mouse opens existing test panel");
        yield return MouseClick("toggle-test");Check(!ui.TestExpanded,"Native Mouse closes existing test panel");
        Status="PASS "+checks+" native PC mouse checks; real Mouse InputSystem routing, no direct UI event injection.";
    }
    private PNPlayerSkill Player(long id,long owner,Vector3 position)
    {
        var obj=new GameObject("Pause fixture "+id);obj.SetActive(false);SceneManager.MoveGameObjectToScene(obj,scene);obj.transform.position=position;
        obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id),EveId.Parse("project_n:eve"),owner);
        obj.AddComponent<PNAbilityManager>();obj.AddComponent<PNPlayerStat>();
        var body=obj.AddComponent<Rigidbody>();body.useGravity=false;body.constraints=RigidbodyConstraints.FreezeRotation;
        var collider=obj.AddComponent<CapsuleCollider>();collider.radius=.12f;collider.height=.6f;
        obj.AddComponent<PNPlayerMove>();var s=obj.AddComponent<PNPlayerSkill>();
        obj.AddComponent<EveEntityLoader>().Load(EveRegistrySession.Current);obj.SetActive(true);return s;
    }
    private IEnumerator Sequence()
    {
        ui=UnityEngine.Object.FindAnyObjectByType<PNUiRuntime>();folder=Path.GetFullPath(".utmp/PauseCelestialReview");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"Progress.txt"),"");
        previousOwner=ui.LocalOwnerId;previousPlayer=ui.LocalPlayerId;previousTime=Time.timeScale;previousMobile=ui.ShowMobileControls;previousTest=ui.TestExpanded;
        ui.enabled=false;
        previousSettings=InputSystem.settings;previousFlags=previousSettings.hideFlags;
        if(previousFlags==HideFlags.HideAndDontSave)previousSettings.hideFlags=HideFlags.DontSave;
        settings=Instantiate(previousSettings);InputSystem.settings=settings;
        settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        roleField=typeof(EveSessionContext).GetField("<Role>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic);
        previousRole=EveRegistrySession.Context.Role;initialized=true;
        // Exclude synthetic devices from pre-existing gameplay input consumers.
        var realDevices=InputSystem.devices.ToArray();
        foreach(var m in UnityEngine.Object.FindObjectsByType<PNPlayerMove>())
        { var asset=m.PC.asset;var original=asset.devices;restoreDevices.Add(()=>{if(asset!=null)asset.devices=original;});asset.devices=realDevices; }
        foreach(var s in UnityEngine.Object.FindObjectsByType<PNPlayerSkill>())
            foreach(var a in new[]{s.PC.Controller.Attack,s.PC.Controller.Skill,s.PC.Controller.Ulti,s.PC.Controller.Dash,s.PC.Controller.Burst})
            { bool wasEnabled=a.enabled;restoreDevices.Add(()=>{if(wasEnabled)a.Enable();});a.Disable(); }
        keyboard=InputSystem.AddDevice<Keyboard>("PauseReviewKeyboard");touch=InputSystem.AddDevice<Touchscreen>("PauseReviewTouch");pad=InputSystem.AddDevice<Gamepad>("PauseReviewPad");
        scene=SceneManager.CreateScene("Disposable pause fixtures");
        skill=Player(998001,9001,new Vector3(1000,1,1000));remote=Player(998002,9002,new Vector3(1005,1,1000));move=skill.GetComponent<PNPlayerMove>();
        ui.LocalOwnerId=9001;ui.LocalPlayerId=skill.GetComponent<PNPlayerStat>().EntityId;ui.ShowMobileControls=true;ui.enabled=true;ui.SetTestExpanded(false);Time.timeScale=1;ui.RefreshUI();
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        yield return new WaitForSecondsRealtime(.3f);
        var start=move.transform.position;
        Keys(Key.D);yield return new WaitForSecondsRealtime(.15f);
        Check(move.transform.position.x>start.x+.01f,"Native keyboard moves actual fixture Rigidbody before menu");
        Keys();yield return new WaitForSecondsRealtime(.08f);
        Time.timeScale=.6f;
        Finger(1,UnityEngine.InputSystem.TouchPhase.Began,"toggle-pause");yield return new WaitForSecondsRealtime(.05f);
        Finger(1,UnityEngine.InputSystem.TouchPhase.Ended,"toggle-pause");yield return new WaitForSecondsRealtime(.08f);
        Check(ui.MenuOpen && Time.timeScale==0,"Native mobile menu button opens offline pause immediately");
        var ring=ui.HudRoot.Q("pause-ring-0");float openingScale=ring.style.scale.value.value.x;
        yield return Capture("Opening");yield return new WaitForSecondsRealtime(.3f);yield return Capture("OpeningMid");yield return new WaitForSecondsRealtime(.7f);
        Check(Mathf.Abs(ring.style.scale.value.value.x-1)<.001f && openingScale<1,"Decoration scales in and settles while game time is stopped");
        Check(ui.HudRoot.Q("pause-card").resolvedStyle.backgroundColor.a==0 && ui.HudRoot.Q("toggle-pause").resolvedStyle.backgroundColor.a==0,"Menu choices have no enclosing card; sparkle toggle has no background");
        Check(ui.HudRoot.Q("toggle-pause").style.width.value.value>=64,"Mobile pause hit target is at least64 reference pixels");
        var core=ui.HudRoot.Q("pause-core");var star=ui.HudRoot.Q("pause-star-front");
        Check(star.parent==core.parent && star.parent.IndexOf(star)>star.parent.IndexOf(core) && star.style.overflow.value==Overflow.Visible,"Front orbital star is a complete unclipped sibling above core");
        Check(ui.HudRoot.Q("pause-card").Children().Count()==3 && !ui.HudRoot.Q<Button>("pause-settings").enabledSelf && !ui.HudRoot.Q<Button>("pause-leave").enabledSelf,"Three labels; settings and leave disabled");
        Check(ui.HudRoot.Q("mobile-controls").resolvedStyle.display==DisplayStyle.None,"Mobile controls hidden under menu");
        Check(move.MenuInputBlocked && skill.MenuInputBlocked && !remote.MenuInputBlocked,"Only local owner's movement and skill are gated");
        var position=move.transform.position;int charges=skill.DashRecovery.Current;
        Keys(Key.D,Key.K);yield return new WaitForSecondsRealtime(.15f);
        Check(Vector3.Distance(move.transform.position,position)<.001f && skill.DashRecovery.Current==charges,"Paused input cannot move or consume Dash charge");
        yield return Capture("Open");
        Finger(2,UnityEngine.InputSystem.TouchPhase.Began,"pause-overlay");yield return new WaitForSecondsRealtime(.05f);
        Finger(2,UnityEngine.InputSystem.TouchPhase.Ended,"pause-overlay");yield return new WaitForSecondsRealtime(.08f);
        Check(ui.MenuOpen,"Background tap leaves menu open");
        Finger(3,UnityEngine.InputSystem.TouchPhase.Began,"pause-continue");yield return new WaitForSecondsRealtime(.05f);
        Finger(3,UnityEngine.InputSystem.TouchPhase.Ended,"pause-continue");yield return new WaitForSecondsRealtime(.08f);
        Check(ui.MenuOpen && Time.timeScale==0,"Continue retains pause during exit animation");yield return Capture("Closing");
        float coreEarly=core.style.scale.value.value.x;
        yield return new WaitForSecondsRealtime(.18f);yield return Capture("ClosingMid");
        Check(core.style.scale.value.value.x<coreEarly && core.style.scale.value.value.x>0,"Closing core shrinks continuously during unscaled pause");
        yield return new WaitForSecondsRealtime(.18f);yield return Capture("ClosingLate");
        yield return new WaitForSecondsRealtime(.25f);
        Check(!ui.MenuOpen && Mathf.Abs(Time.timeScale-.6f)<.001f,"Continue restores pre-pause speed after exit");
        Check(move.MenuInputBlocked && skill.MenuInputBlocked,"Held gameplay keys remain gated after closing");
        Keys();yield return new WaitForSecondsRealtime(.08f);
        Check(!move.MenuInputBlocked && !skill.MenuInputBlocked,"Neutral physical controls rearm input");
        Keys(Key.D);yield return new WaitForSecondsRealtime(.15f);
        Check(move.transform.position.x>position.x+.01f,"Fresh input resumes actual movement");Keys();
        Keys(Key.Escape);yield return new WaitForSecondsRealtime(.08f);Keys();yield return new WaitForSecondsRealtime(.4f);
        Check(ui.MenuOpen,"Escape opens menu");
        move.CanInput=false;skill.CanInput=false;
        Keys(Key.Escape);yield return new WaitForSecondsRealtime(.05f);Keys();yield return new WaitForSecondsRealtime(.65f);
        Check(!ui.MenuOpen && !move.CanInput && !skill.CanInput,"Escape closes; gameplay locks acquired under menu survive");move.CanInput=true;skill.CanInput=true;
        InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.Start));yield return new WaitForSecondsRealtime(.4f);
        Check(ui.MenuOpen,"Native gamepad Start opens menu");
        InputSystem.QueueStateEvent(pad,new GamepadState());yield return new WaitForSecondsRealtime(.05f);
        InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.Start));yield return new WaitForSecondsRealtime(.65f);
        Check(!ui.MenuOpen,"Native gamepad Start closes menu");InputSystem.QueueStateEvent(pad,new GamepadState());
        Keys(Key.K);yield return new WaitForSecondsRealtime(.04f);Keys();
        Check(skill.IsDashing,"Fresh native Dash input starts a running Action");
        ui.OpenMenu();var dashPosition=move.Body.position;yield return new WaitForSecondsRealtime(.4f);
        Check(skill.IsDashing && Vector3.Distance(move.Body.position,dashPosition)<.001f,"Offline pause retains running Dash and freezes real Rigidbody progress");
        ui.CloseMenu();yield return new WaitForSecondsRealtime(.95f);
        Check(!skill.IsDashing && Vector3.Distance(move.Body.position,dashPosition)>.01f,"Running Dash finishes after resume without restarting");
        // Simulated native role fixtures exercise menu policy, not a network transport.
        foreach(var role in new[]{EveSessionRole.Host,EveSessionRole.Client})
        {
            roleField.SetValue(EveRegistrySession.Context,role);Time.timeScale=1;
            skill.DashRecovery.TryConsume();double before=skill.DashRecovery.CooldownRemaining;
            ui.OpenMenu();yield return new WaitForSecondsRealtime(.4f);
            Check(ui.MenuOpen && Time.timeScale==1 && skill.MenuInputBlocked && !remote.MenuInputBlocked,role+" menu leaves global time and remote input active");
            Check(skill.DashRecovery.CooldownRemaining<before,role+" Ability recovery continues while menu is open");
            ui.CloseMenu();yield return new WaitForSecondsRealtime(.65f);
        }
        roleField.SetValue(EveRegistrySession.Context,previousRole);Time.timeScale=.7f;ui.OpenMenu();yield return new WaitForSecondsRealtime(.05f);
        ui.enabled=false;
        Check(!move.MenuInputBlocked && !skill.MenuInputBlocked && Mathf.Abs(Time.timeScale-.7f)<.001f,"Disable during opening releases time and owned input gates");
        ui.enabled=true;yield return new WaitForSecondsRealtime(.3f);
        Check(ui.HudRoot.Q("toggle-pause")!=null && !ui.MenuOpen,"Reenable rebuilds one closed menu");
        Status="PASS "+checks+" native pause checks; Host/Client roles simulated, no network transport tested.";
    }
    private void Cleanup()
    {
        EditorApplication.update-=Pump;if(!initialized)return;initialized=false;
        if(EveRegistrySession.IsReady)roleField?.SetValue(EveRegistrySession.Context,previousRole);
        if(ui!=null)
        {ui.enabled=false;ui.LocalOwnerId=previousOwner;ui.LocalPlayerId=previousPlayer;ui.ShowMobileControls=previousMobile;}
        Time.timeScale=previousTime;
        if(keyboard!=null && InputSystem.devices.Contains(keyboard))InputSystem.RemoveDevice(keyboard);
        if(touch!=null && InputSystem.devices.Contains(touch))InputSystem.RemoveDevice(touch);
        if(pad!=null && InputSystem.devices.Contains(pad))InputSystem.RemoveDevice(pad);
        if(mouse!=null && InputSystem.devices.Contains(mouse))InputSystem.RemoveDevice(mouse);
        foreach(var restore in restoreDevices)restore();restoreDevices.Clear();
        if(previousSettings!=null){InputSystem.settings=previousSettings;previousSettings.hideFlags=previousFlags;}
        if(settings!=null)Destroy(settings);
        if(ui!=null){ui.enabled=true;ui.SetTestExpanded(previousTest);}
        if(scene.IsValid())
        {foreach(var root in scene.GetRootGameObjects())root.GetComponent<EveEntityLoader>()?.Unload();SceneManager.UnloadSceneAsync(scene);}
    }
    private void OnDestroy()=>Cleanup();
}
#endif
