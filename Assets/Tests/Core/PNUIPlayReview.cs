#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.Rendering.Universal;

// Opt-in disposable Play Mode fixtures. No authored scene/model is edited or saved.
public sealed class PNUIPlayReview : MonoBehaviour
{
    public static string Status="Idle";
    private readonly List<string> evidence=new List<string>();
    private readonly List<GameObject> fixtures=new List<GameObject>();
    private PNUiRuntime ui;
    private Camera previousCamera, camera;
    private long previousOwner;
    private EveEntityId previousPlayer;
    private bool expanded;
    private int checks;
    private string folder;
    private Scene scene;
    public static string Run()
    {
        if(!Application.isPlaying || !EveRegistrySession.IsReady) throw new InvalidOperationException("Ready Play Mode required.");
        var review=new GameObject("Disposable combat UI review").AddComponent<PNUIPlayReview>();
        review.ui=UnityEngine.Object.FindObjectsByType<PNUiRuntime>().First();
        review.previousCamera=review.ui.ViewCamera; review.previousOwner=review.ui.LocalOwnerId;
        review.previousPlayer=review.ui.LocalPlayerId; review.expanded=review.ui.TestExpanded;
        review.folder=Path.GetFullPath(".utmp/CombatUIReview"); Directory.CreateDirectory(review.folder);
        review.scene=SceneManager.CreateScene("Disposable combat UI fixtures");
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        Status="Running"; EditorApplication.update+=review.Pump;
        review.StartCoroutine(review.Guarded(review.Sequence())); return Status;
    }
    private void Pump()
    {
        if(!Application.isPlaying) return;
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Repaint();
        EditorApplication.QueuePlayerLoopUpdate();
    }
    private void OnDestroy()=>EditorApplication.update-=Pump;
    private IEnumerator Guarded(IEnumerator sequence)
    {
        while(true)
        {
            object next;
            try { if(!sequence.MoveNext()) break; next=sequence.Current; }
            catch(Exception error) { Status="FAIL "+error; Debug.LogException(error); break; }
            yield return next;
        }
        (sequence as IDisposable)?.Dispose();
        EditorApplication.update-=Pump;
        ui.ViewCamera=previousCamera; ui.LocalOwnerId=previousOwner; ui.LocalPlayerId=previousPlayer;
        ui.SetTestExpanded(expanded); ui.RefreshUI();
        foreach(var fixture in fixtures) if(fixture!=null) Destroy(fixture);
        if(scene.IsValid()) SceneManager.UnloadSceneAsync(scene);
        File.WriteAllLines(Path.Combine(folder,"Results.txt"),evidence.Concat(new[]{Status}));
        Destroy(gameObject);
    }
    private void Check(bool ok,string message) { if(!ok)throw new Exception(message); checks++; evidence.Add("PASS "+message); }
    private GameObject Root(string name)
    {
        var obj=new GameObject(name); obj.SetActive(false); SceneManager.MoveGameObjectToScene(obj,scene); fixtures.Add(obj); return obj;
    }
    private PNStat Entity(long id,bool player,Vector3 position)
    {
        var obj=Root("UI fixture "+id); obj.transform.position=position;
        obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id),EveId.Parse(player?"project_n:eve":"project_n:dummy"),id);
        PNStat stat=player?(PNStat)obj.AddComponent<PNPlayerStat>().AsPlayer():obj.AddComponent<PNEnemyStat>();
        if(player) obj.AddComponent<PNAbilityManager>();
        obj.AddComponent<EveEntityLoader>().Load(EveRegistrySession.Current); obj.SetActive(true);
        var marker=GameObject.CreatePrimitive(PrimitiveType.Capsule); marker.name="Disposable entity marker";
        marker.transform.SetParent(obj.transform,false); marker.transform.localScale=new Vector3(.35f,.35f,.35f);
        marker.transform.localPosition=new Vector3(0,.35f,0); Destroy(marker.GetComponent<Collider>());
        return stat;
    }
    private IEnumerator Capture(string name)
    {
        yield return new WaitForEndOfFrame();
        var image=ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG()); Destroy(image);
        evidence.Add("Rendered "+name);
    }
    private UIDocument World(PNStat stat)=>ui.GetComponentsInChildren<UIDocument>().Single(d=>d.name=="Combat UI "+stat.EntityId);
    private IEnumerator Sequence()
    {
        var cameraRoot=Root("UI fixture camera"); camera=cameraRoot.AddComponent<Camera>();
        camera.CopyFrom(Camera.main); camera.GetUniversalAdditionalCameraData().renderType=CameraRenderType.Base;
        camera.depth=100; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.1f,.15f,.19f);
        camera.transform.position=new Vector3(100,6,-6); camera.transform.LookAt(new Vector3(100,0,0));
        camera.orthographic=true; camera.orthographicSize=4; cameraRoot.SetActive(true);
        int mask=camera.cullingMask;
        var players=new[]{Entity(997001,true,new Vector3(98.7f,0,0)),Entity(997002,true,new Vector3(100,0,0)),Entity(997003,true,new Vector3(101.3f,0,0))};
        var enemy=Entity(997004,false,new Vector3(99,0,2));
        ui.ViewCamera=camera; ui.LocalOwnerId=997001; ui.LocalPlayerId=players[0].EntityId;
        foreach(var player in players)
        {
            var manager=player.GetComponent<PNAbilityManager>();
            // Fixture-only definitions exercise shared Dash and independent states without requiring Eve logic.
            manager.Abilities.Define(new PNAbilitySettings { Id="test:review_state",ShowInUI=true,Kind=PNAbilityKind.State,HasDuration=true,Duration=20,Reapply=PNAbilityReapply.Independent,DisplayOrder=-2 }.Create());
            manager.Abilities.GrantState(EveId.Parse("test:review_state"),20); manager.Abilities.GrantState(EveId.Parse("test:review_state"),10);
            manager.Abilities.Define(PNAbilityDefinition.Parse("[{id:'test:review_dash',kind:'Cooldown',showInUI:true,maximum:3,initialAmount:3,cooldown:5,displayOrder:-1}]")[0]);
            manager.Abilities.GetCooldown(EveId.Parse("test:review_dash")).TryConsume();
        }
        ui.SetTestExpanded(false); ui.RefreshUI(); yield return new WaitForSeconds(.3f);
        yield return new WaitForEndOfFrame();
        Check(ui.HudPlayer==players[0],"HUD selects explicit local owner/player instead of another co-op player");
        Check(World(players[0]).rootVisualElement.Q("ability-grid").childCount==5,"Active or recovering Ability IDs retain one icon; inactive IDs leave no gaps");
        Check(World(enemy).rootVisualElement.Q("enemy-vitals").resolvedStyle.display==DisplayStyle.Flex,"Ordinary enemy exposes HP and buff placeholders");
        var interaction=World(players[0]).GetComponent<BoxCollider>();
        Check(interaction==null || interaction.isTrigger,"Automatic world interaction collider cannot block gameplay motion");
        Check(World(players[0]).GetComponent<UIRenderer>().bounds.size.x>.8,"Native world size converts pixels to visible world units");
        yield return Capture("Coop-spaced");
        players[1].transform.position=players[0].transform.position+new Vector3(.28f,0,.12f);
        players[2].transform.position=players[0].transform.position+new Vector3(.55f,0,.22f);
        yield return new WaitForSeconds(.1f); yield return Capture("Coop-crowded");
        ui.SetTestExpanded(true); yield return new WaitForSeconds(.1f);
        var selector=ui.HudRoot.Q<DropdownField>("test-entity"); selector.value=selector.choices.Single(c=>c.Contains("997004")); ui.RefreshUI();
        Check(ui.HudRoot.Q<Label>("test-stats").text.Contains("health"),"Interactive entity selection updates evaluated/base stats");
        yield return Capture("Test-enemy");
        selector.value=selector.choices.Single(c=>c.Contains("997001")); ui.RefreshUI();
        Check(ui.HudRoot.Q<Label>("test-abilities").text.Contains("review_state"),"Debug details include individual Ability handles");
        yield return Capture("Test-player");
        players[2].gameObject.SetActive(false); ui.RefreshUI();
        Check(World(players[2]).rootVisualElement.style.display.value==DisplayStyle.None,"Disabled entity hides retained panel");
        players[2].gameObject.SetActive(true); players[2].transform.position=new Vector3(1000,0,0); ui.RefreshUI();
        Check(World(players[2]).rootVisualElement.style.display.value==DisplayStyle.None,"Offscreen anchor hides panel");
        players[1].SetCurrent(PNContentIds.Health,0); ui.RefreshUI();
        Check(World(players[1]).rootVisualElement.style.display.value==DisplayStyle.None,"Dead entity hides world panel");
        var retiredName="Combat UI "+players[2].EntityId;
        players[2].GetComponent<EveEntityLoader>().Unload(); ui.RefreshUI(); yield return null;
        Check(!ui.GetComponentsInChildren<UIDocument>().Any(d=>d.name==retiredName),"Unload retires entity panel");
        var stack=camera.GetUniversalAdditionalCameraData().cameraStack;
        ui.enabled=false; yield return null;
        Check(stack.Count==0 && camera.cullingMask==mask,"Disable releases owned camera and restores its layer mask");
        ui.enabled=true; ui.ViewCamera=camera; ui.LocalOwnerId=997001; ui.LocalPlayerId=players[0].EntityId;
        ui.RefreshUI(); yield return null;
        Check(ui.HudPlayer==players[0] && ui.WorldCount>=2,"Reenable rebuilds UI from current entity state");
        Status="PASS "+checks+" native UI checks";
    }
}
#endif
