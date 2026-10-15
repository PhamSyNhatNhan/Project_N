#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

// Disposable Play fixture: no authored scene/configuration modifications.
public sealed class PNBuffUIPlayReview : MonoBehaviour
{
    public static string Status="Idle";
    private PNUiRuntime ui;
    private Camera oldCamera,camera;
    private long oldOwner;
    private EveEntityId oldPlayer;
    private bool oldMobile,oldExpanded;
    private Scene scene;
    private readonly List<GameObject> roots=new List<GameObject>();
    private readonly List<string> evidence=new List<string>();
    private readonly string folder="F:/unity/Project_N/.utmp/BuffCoopReview/BuffUI";
    private int checks;
    private string mode;
    public static string Run()
    {
        if(!Application.isPlaying || !EveRegistrySession.IsReady || EditorApplication.isPaused)throw new InvalidOperationException("Ready unpaused Play required");
        var host=new GameObject("Buff UI review host");var review=host.AddComponent<PNBuffUIPlayReview>();
        review.ui=FindObjectsByType<PNUiRuntime>(FindObjectsSortMode.None).First();review.oldCamera=review.ui.ViewCamera;
        review.oldOwner=review.ui.LocalOwnerId;review.oldPlayer=review.ui.LocalPlayerId;review.oldMobile=review.ui.ShowMobileControls;review.oldExpanded=review.ui.TestExpanded;
        review.scene=SceneManager.CreateScene("Disposable Buff UI review");Directory.CreateDirectory(review.folder);
        review.mode=PNMobilePlatform.UsesSimulatedScreen?"Simulator":"Game";
        Status="Running";EditorApplication.update+=review.Pump;review.StartCoroutine(review.Guarded(review.Sequence()));return Status;
    }
    private void Pump(){if(Application.isPlaying){EditorApplication.QueuePlayerLoopUpdate();UnityEditorInternal.InternalEditorUtility.RepaintAllViews();}}
    private void OnDestroy()=>EditorApplication.update-=Pump;
    private GameObject Root(string name){var root=new GameObject(name);root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);roots.Add(root);return root;}
    private PNStat Entity(long id,bool player,Vector3 position)
    {
        var root=Root("Buff UI fixture "+id);root.transform.position=position;
        root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id),EveId.Parse(player?"project_n:eve":"project_n:dummy"),id);
        root.AddComponent<EveTimeScale>();PNStat stat=player?(PNStat)root.AddComponent<PNPlayerStat>():root.AddComponent<PNEnemyStat>();
        if(player)root.AddComponent<PNAbilityManager>();root.AddComponent<EveEntityLoader>().Load(EveRegistrySession.Current);root.SetActive(true);
        return stat;
    }
    private void Check(bool value,string message){if(!value)throw new Exception(message);checks++;evidence.Add("PASS "+message);}
    private VisualElement Enemy(PNStat stat)=>ui.GetComponentsInChildren<UIDocument>().Single(d=>d.name=="Combat UI "+stat.EntityId).rootVisualElement.Q("enemy-buffs");
    private static PNBuffPresentationWriter Writer(PNBuffCollection collection,PNBuffHandle handle)
    {
        var instances=(IDictionary)typeof(PNBuffCollection).GetField("instances",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(collection);
        var instance=instances[handle.InstanceId];var behavior=(PNBuffBehavior)typeof(PNBuffInstance).GetField("Behavior",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(instance);
        return behavior.Presentation;
    }
    private IEnumerator Capture(string name)
    {
        yield return null;var texture=PNPauseVisualReview.ReadSelectedView();
        File.WriteAllBytes(Path.Combine(folder,mode+"-"+name+".png"),texture.EncodeToPNG());Destroy(texture);
    }
    private IEnumerator Sequence()
    {
        var cameraRoot=Root("Buff UI review camera");camera=cameraRoot.AddComponent<Camera>();camera.CopyFrom(Camera.main);
        camera.GetUniversalAdditionalCameraData().renderType=CameraRenderType.Base;camera.depth=100;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.17f,.19f);
        camera.transform.position=new Vector3(100,6,-6);camera.transform.LookAt(new Vector3(100,0,0));camera.orthographic=true;camera.orthographicSize=4;cameraRoot.SetActive(true);
        var player=Entity(998101,true,new Vector3(100,0,0));var enemy=Entity(998102,false,new Vector3(99,0,2));
        ui.ViewCamera=camera;ui.LocalOwnerId=998101;ui.LocalPlayerId=player.EntityId;ui.SetTestExpanded(false);
        var source=PNBuffSource.Capture(player);var p=player.GetComponent<PNBuffManager>().Buffs;var e=enemy.GetComponent<PNBuffManager>().Buffs;
        PNBuffHandle Area(PNBuffCollection buffs)=>buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:timescale_reduction_area"),source,new JObject{{"reduction",0},{"minimumReduction",0}})).Handle;
        var ph=new List<PNBuffHandle>();var eh=new List<PNBuffHandle>();
        for(int i=0;i<11;i++){ph.Add(Area(p));eh.Add(Area(e));}
        Writer(p,ph[0]).Publish(new PNBuffNumbers(0,12,-2,33));Writer(e,eh[0]).Publish(new PNBuffNumbers(0,12,-2,33));
        ui.RefreshUI();yield return new WaitForSeconds(.3f);ui.RefreshUI();yield return null;
        var hud=ui.HudRoot.Q("player-buffs");var world=Enemy(enemy);
        Check(hud.childCount==11 && world.childCount==11,"All eleven independent same-ID Buffs display separately");
        var playerPresentation=player.GetComponent<PNBuffManager>().Presentation.PresentationSnapshot();
        var enemyPresentation=enemy.GetComponent<PNBuffManager>().Presentation.PresentationSnapshot();
        var h0=hud.Q("buff-"+playerPresentation[0].InstanceId);var h5=hud.Q("buff-"+playerPresentation[5].InstanceId);
        Check(h5.resolvedStyle.top>h0.resolvedStyle.top,"Player five columns expand downwards");
        var w0=world.Q("buff-"+enemyPresentation[0].InstanceId);var w5=world.Q("buff-"+enemyPresentation[5].InstanceId);
        Check(w5.resolvedStyle.top<w0.resolvedStyle.top,"Enemy five columns expand upwards");
        Check(h0.Query<Label>().ToList().Select(x=>x.text).SequenceEqual(new[]{"0","12","-2","33"}),"Four corner integer values reach native labels");
        Check(Mathf.Abs(h0.resolvedStyle.width-30)<1f && Mathf.Abs(h0.resolvedStyle.height-30)<1f,"Buff icon remains30 pixels within one layout pixel of panel snapping ("+h0.resolvedStyle.width+"x"+h0.resolvedStyle.height+")");
        Check(h0.pickingMode==PickingMode.Ignore && h0.Query<VisualElement>().ToList().All(x=>x.pickingMode==PickingMode.Ignore),"Icon and all four badges ignore pointer picking");
        Check(h0.Q(className:"buff-image").resolvedStyle.backgroundImage.sprite==Resources.Load<Sprite>("Effects/Burn/Burn"),"Burn PNG fallback preserves original artwork");
        yield return Capture("Eleven");
        var same=h0;Writer(p,ph[0]).Publish(new PNBuffNumbers(topRight:1));ui.RefreshUI();yield return null;
        Check(ReferenceEquals(same,hud.Q(same.name)) && same.Query<Label>().ToList()[1].text=="1","Number changes update retained card without re-creation");
        player.GetComponent<PNBuffManager>().enabled=false;ui.RefreshUI();yield return null;
        Check(h0.Q(className:"buff-image").resolvedStyle.opacity<.5f && h0.Query<Label>().ToList()[1].parent.resolvedStyle.opacity==1,"Suspended artwork dims while number badge stays clear");
        yield return Capture("Suspended");player.GetComponent<PNBuffManager>().enabled=true;
        var eventCount=0;var displayed=player.GetComponent<PNBuffManager>().Presentation;displayed.PresentationChanged+=CountEvent;void CountEvent(PNBuffPresentationChange change){eventCount++;}
        yield return new WaitForSeconds(.25f);Check(eventCount==0,"Untimed Buffs emit no frame-based value updates");displayed.PresentationChanged-=CountEvent;
        var before=ui.HudRoot.Q("portrait-host").worldBound;for(int i=10;i>=5;i--){p.Remove(ph[i],PNBuffEndReason.Bypass,true);e.Remove(eh[i],PNBuffEndReason.Bypass,true);}
        yield return new WaitForSeconds(.35f);ui.RefreshUI();yield return null;
        Check(hud.childCount==5 && world.childCount==5,"Fade retirement compacts cards back to one row");
        Check(ui.HudRoot.Q("portrait-host").worldBound.position==before.position,"Additional Buff rows do not shift portrait");
        yield return Capture("Five");
        ui.ShowMobileControls=true;ui.RefreshUI();yield return new WaitForSeconds(.2f);yield return Capture("MobilePreview-Five");ui.ShowMobileControls=oldMobile;
        foreach(var handle in ph)p.Remove(handle,PNBuffEndReason.Bypass,true);foreach(var handle in eh)e.Remove(handle,PNBuffEndReason.Bypass,true);
        yield return new WaitForSeconds(.25f);ui.RefreshUI();Check(hud.childCount==0 && world.childCount==0,"Ended Buffs leave no placeholder slots");
        Status="PASS "+checks;
    }
    private IEnumerator Guarded(IEnumerator sequence)
    {
        while(true){object next;try{if(!sequence.MoveNext())break;next=sequence.Current;}catch(Exception error){Status="FAIL "+error;Debug.LogException(error);break;}yield return next;}
        (sequence as IDisposable)?.Dispose();EditorApplication.update-=Pump;
        ui.ViewCamera=oldCamera;ui.LocalOwnerId=oldOwner;ui.LocalPlayerId=oldPlayer;ui.ShowMobileControls=oldMobile;ui.SetTestExpanded(oldExpanded);ui.RefreshUI();
        foreach(var root in roots)if(root!=null){root.GetComponent<EveEntityLoader>()?.Unload();Destroy(root);}
        if(scene.IsValid())SceneManager.UnloadSceneAsync(scene);File.WriteAllLines(Path.Combine(folder,mode+"Results.txt"),evidence.Concat(new[]{Status}));Destroy(gameObject);
    }
}
#endif

