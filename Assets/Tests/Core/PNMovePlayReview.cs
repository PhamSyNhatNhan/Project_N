#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using EveEngine.Movement;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Run only in the disposable empty clone Play scene, using the actual player loop and input system.
public sealed class PNMovePlayReview : MonoBehaviour
{
    public static string Status="Idle";
    private readonly List<string> evidence=new List<string>();
    private readonly List<GameObject> objects=new List<GameObject>();
    private InputSettings oldSettings,newSettings;
    private Keyboard keyboard;
    private PNMoveClientConnection connection;
    private bool oldBackground;
    private SimulationMode oldSimulation;
    private int checks;
    private bool cleaned;
    public static void Run()
    { Status="Running";new GameObject("Disposable Move Play review").AddComponent<PNMovePlayReview>(); }
    private void Check(bool ok,string label) { if(!ok)throw new Exception(label);checks++;evidence.Add("PASS "+label); }
    private void Start() { EditorApplication.update+=Pump;StartCoroutine(Guarded()); }
    private void Pump()=>EditorApplication.QueuePlayerLoopUpdate();
    private IEnumerator Guarded()
    {
        oldBackground=Application.runInBackground;Application.runInBackground=true;
        oldSimulation=Physics.simulationMode;Physics.simulationMode=SimulationMode.FixedUpdate;
        oldSettings=InputSystem.settings;newSettings=Instantiate(oldSettings);
        newSettings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        newSettings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;InputSystem.settings=newSettings;
        keyboard=InputSystem.AddDevice<Keyboard>("Move fixture keyboard");
        var sequence=Sequence();
        while(true)
        {
            object next;
            try { if(!sequence.MoveNext()){Status="PASS "+checks+" Move Play checks";break;}next=sequence.Current; }
            catch(Exception error) { Status="FAIL "+error;break; }
            yield return next;
        }
        (sequence as IDisposable)?.Dispose();Cleanup();
        Directory.CreateDirectory("../NativeOut");File.WriteAllLines("../NativeOut/Play.txt",evidence.Concat(new[]{Status}));Destroy(gameObject);
    }
    private IEnumerator Sequence()
    {
        var docs=new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),Path.Combine(Application.persistentDataPath,"Mods"),Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);
        var compat=EveContentCompatibility.Create(hub,docs,"test:move_play");
        EveRegistrySession.Begin();EveRegistrySession.Start(hub,EveSessionRole.Client,compat);var session=EveRegistrySession.Context;var host=Guid.NewGuid();session.Coop.AdmitHost(host,compat,7);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);objects.Add(floor);floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(40,1,40);
        PNPlayerMove Actor(long id,long owner,Vector3 position)
        {
            var root=new GameObject("Move Play actor "+id);objects.Add(root);root.SetActive(false);root.transform.position=position;
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id),EveId.Parse("project_n:eve"),owner);
            root.AddComponent<CapsuleCollider>().height=2;root.AddComponent<Rigidbody>().constraints=RigidbodyConstraints.FreezeRotation;
            root.AddComponent<PNPlayerStat>();root.AddComponent<EveTimeScale>();root.AddComponent<PNBuffManager>().RenderPresentation=false;
            var move=root.AddComponent<PNPlayerMove>();var loader=root.AddComponent<EveEntityLoader>();loader.Load(hub);root.SetActive(true);move.PC.devices=new InputDevice[]{keyboard};return move;
        }
        var own=Actor(6201,7,new Vector3(0,1.02f,0));var other=Actor(6202,8,new Vector3(-2,1,4));
        connection=new PNMoveClientConnection(session,host);connection.Bind(own);connection.Bind(other);
        var id=own.NetworkIdentity(host);var observerId=other.NetworkIdentity(host);
        MoveMirror Mirror(uint revision,float scale) => new MoveMirror(id,revision,1,false,MoveRights.All,7,5,scale,scale,new[]{PNControlIds.VoluntaryMotion.ToString()});
        connection.Receive(MoveCodec.Encode(Mirror(1,1)));connection.Receive(MoveCodec.Encode(new MoveCorrection(id,1,1,1,new MovementState(new MoveVector(0,1.02f,0),default),default,MoveCorrectionReason.Baseline,baselineRequestId:1)));
        yield return new WaitForFixedUpdate();yield return new WaitForSecondsRealtime(.12f);
        var start=own.Body.position;InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.D));yield return new WaitForSecondsRealtime(.16f);
        Check(own.Body.position.x>start.x+.3f && own.MoveDirection.x>.9f,"real keyboard drives admitted client owner through normal Update/FixedUpdate start="+start+" body="+own.Body.position+" velocity="+own.Body.linearVelocity+" dir="+own.MoveDirection+" ready="+own.IsReady+" correction="+own.OwnerMovement.AppliedCorrection+" kinematic="+own.Body.isKinematic+" time="+Time.timeScale);
        Check(other.MoveDirection==Vector2.zero && !other.CanInput,"same keyboard cannot drive observer");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;
        var frozenAt=own.Body.position;
        connection.Receive(MoveCodec.Encode(Mirror(2,0)));connection.Receive(MoveCodec.Encode(new MoveCorrection(id,2,2,2,new MovementState(PNMoveVector(frozenAt),default),new MoveRecovery(new MoveVector(0,4,0),default,true),MoveCorrectionReason.Constraint)));
        yield return new WaitForFixedUpdate();yield return new WaitForSecondsRealtime(.1f);
        Check(Vector3.Distance(own.Body.position,frozenAt)<.01f && own.Body.linearVelocity==Vector3.zero,"actual loop frozen correction holds body");
        connection.Receive(MoveCodec.Encode(Mirror(3,1)));yield return new WaitForFixedUpdate();yield return new WaitForSecondsRealtime(.06f);
        Check(own.Body.position.y>frozenAt.y+.1f,"actual loop thaw resumes corrected airborne velocity");
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);objects.Add(wall);wall.transform.position=new Vector3(3,3,0);wall.transform.localScale=new Vector3(1,6,5);
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.D));yield return new WaitForSecondsRealtime(.7f);InputSystem.QueueStateEvent(keyboard,new KeyboardState());
        Check(own.Body.position.x<=2.02f,"owner locomotion respects real wall collision");
        var stateA=new MovementState(new MoveVector(-2,1,4),default);var stateB=new MovementState(new MoveVector(2,1,4),default);
        connection.Receive(MoveCodec.Encode(new MovePoseState(observerId,1,1,0,stateA)));connection.Receive(MoveCodec.Encode(new MovePoseState(observerId,2,1,.1,stateB)));connection.AdvanceObservers(.15);
        Check(Math.Abs(other.Body.position.x)<.01f,"observer interpolates position-driven movement with zero physical velocity");
        connection.Receive(MoveCodec.Encode(new MovePoseState(observerId,3,2,.2,new MovementState(new MoveVector(10,1,4),default))));connection.AdvanceObservers(.15);
        Check(Math.Abs(other.Body.position.x-10)<.01f,"observer teleport epoch cuts history in Play");
        // Real rendered evidence: one camera view at interpolated and cut positions is exported below.
        var cameraRoot=new GameObject("Move review camera");objects.Add(cameraRoot);var camera=cameraRoot.AddComponent<Camera>();camera.transform.position=new Vector3(6,14,-18);camera.transform.LookAt(new Vector3(5,1,1));camera.backgroundColor=new Color(.72f,.76f,.8f);camera.clearFlags=CameraClearFlags.SolidColor;
        var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.SetColor("_BaseColor",new Color(.1f,.3f,.9f));
        foreach(var actor in new[]{own,other}) { var visual=GameObject.CreatePrimitive(PrimitiveType.Capsule);objects.Add(visual);Destroy(visual.GetComponent<Collider>());visual.transform.SetParent(actor.transform,false);visual.GetComponent<Renderer>().sharedMaterial=material; }
        var texture=new RenderTexture(800,480,24);var pixels=new Texture2D(800,480,TextureFormat.RGB24,false);var previous=RenderTexture.active;
        try { RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=texture});RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,800,480),0,0);pixels.Apply();Directory.CreateDirectory("../NativeOut");File.WriteAllBytes("../NativeOut/MovePlay.png",pixels.EncodeToPNG());Check(pixels.GetPixels32().Count(p=>p.b>p.r*1.5f && p.b>p.g*1.3f)>30,"rendered capture contains fixture bodies"); }
        finally { camera.targetTexture=null;RenderTexture.active=previous;texture.Release();Destroy(texture);Destroy(pixels);Destroy(material); }
        byte[][] reports=null;Check(connection.SendReports(batch=>{reports=batch;return true;}) && reports.Length>0,"actual loop produces compact owner reports");
    }
    private static MoveVector PNMoveVector(Vector3 value)=>new MoveVector(value.x,value.y,value.z);
    private void Cleanup()
    {
        if(cleaned)return;cleaned=true;EditorApplication.update-=Pump;
        Physics.simulationMode=oldSimulation;
        connection?.Dispose();if(EveRegistrySession.State!=EveSessionState.Stopped)EveRegistrySession.End();
        foreach(var root in objects)if(root!=null)Destroy(root);objects.Clear();
        if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(oldSettings!=null)InputSystem.settings=oldSettings;if(newSettings!=null)Destroy(newSettings);Application.runInBackground=oldBackground;
    }
    private void OnDestroy()=>Cleanup();
}
#endif
