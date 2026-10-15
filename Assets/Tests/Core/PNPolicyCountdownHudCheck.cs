using System;
using System.Collections;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Rendering.Universal;

// Opt-in disposable normal Player HUD test when the desktop Unity connector is unavailable.
public sealed class PNPolicyCountdownHudCheck : MonoBehaviour
{
    private PNPolicyRuntime runtime; private PNPlayerStat player; private PNUiRuntime ui;
    private PNBuffManager observedManager; private Action<PNBuffWireMessage> messageObserver;
    private Camera camera; private RenderTexture worldTexture,hudTexture; private string output; private int checks;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var args=Environment.GetCommandLineArgs(); if(!args.Contains("-pn-step3b-hud"))return;
        int index=Array.IndexOf(args,"-pn-output");
        var runner=new GameObject("Owned Step3b HUD check").AddComponent<PNPolicyCountdownHudCheck>();
        runner.output=index>=0?args[index+1]:Path.Combine(Application.persistentDataPath,"Step3bHud");
    }
    private void Check(bool value,string reason) { if(!value)throw new Exception(reason);checks++; }
    private IEnumerator Start()
    {
        Directory.CreateDirectory(output); Application.runInBackground=true; QualitySettings.vSyncCount=0; Application.targetFrameRate=120;
        var sequence=Sequence();Exception failure=null;
        while(true) { bool more=false;object item=null;try {more=sequence.MoveNext();if(more)item=sequence.Current;}catch(Exception e){failure=e;}if(!more||failure!=null)break;yield return item; }
        if(failure!=null)File.WriteAllText(Path.Combine(output,"Failure.txt"),failure.ToString());
        if(observedManager?.Publisher!=null && messageObserver!=null) observedManager.Publisher.Message-=messageObserver;
        if(ui!=null)Destroy(ui.gameObject);if(player!=null)Destroy(player.gameObject);yield return null;
        runtime?.EndSession();for(int i=0;i<10000&&EveRegistrySession.State==EveSessionState.Stopping;i++){EveRegistrySession.PumpCleanup();yield return null;}
        if(camera!=null)Destroy(camera.gameObject);if(worldTexture!=null){worldTexture.Release();Destroy(worldTexture);}if(hudTexture!=null){hudTexture.Release();Destroy(hudTexture);}yield return null;
        File.WriteAllText(Path.Combine(output,"Result.txt"),(failure==null?"PASS ":"FAIL ")+checks+" normal Player HUD checks; session="+EveRegistrySession.State);
        Application.Quit(failure==null?0:1);
    }
    private string Number(long id)
    { var card=ui.HudRoot.Q("player-buffs").Q("buff-"+id); if(card==null)return null;var labels=card.Query<Label>().ToList();return labels.Count==4?labels[3].text:null; }
    private IEnumerator Sequence()
    {
        runtime=new GameObject("Owned HUD content runtime").AddComponent<PNPolicyRuntime>();runtime.SelectedRole=EveSessionRole.Host;runtime.Initialize();
        var root=new GameObject("Owned HUD player");root.SetActive(false);
        root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(987001),EveId.Parse("project_n:eve"),1);
        root.AddComponent<EveTimeScale>();player=root.AddComponent<PNPlayerStat>();root.AddComponent<PNAbilityManager>();root.AddComponent<PNBuffManager>();
        root.AddComponent<EveEntityLoader>().Load(EveRegistrySession.Current);root.SetActive(true);
        camera=new GameObject("Owned HUD camera").AddComponent<Camera>();camera.tag="MainCamera";camera.orthographic=true;camera.transform.position=new Vector3(0,6,-10);camera.transform.LookAt(Vector3.zero);
        camera.GetUniversalAdditionalCameraData().renderType=CameraRenderType.Base;
        worldTexture=new RenderTexture(1280,720,24);worldTexture.Create();camera.targetTexture=worldTexture;
        hudTexture=new RenderTexture(1280,720,0);hudTexture.Create();
        ui=new GameObject("Owned real HUD").AddComponent<PNUiRuntime>();ui.ViewCamera=camera;ui.LocalOwnerId=1;ui.LocalPlayerId=player.EntityId;
        foreach(var doc in ui.GetComponentsInChildren<UIDocument>())if(doc.panelSettings.renderMode!=PanelRenderMode.WorldSpace)doc.panelSettings.targetTexture=hudTexture;
        var buffs=player.GetComponent<PNBuffManager>();var receipt=buffs.Buffs.Apply(new PNBuffApplyRequest(EveId.Parse("project_n:timescale_reduction"),PNBuffSource.Capture(player),new JObject{{"duration",8},{"reduction",0},{"minimumReduction",0}}));Check(receipt.Handle.IsAlive,"Native HUD Buff applied.");
        int messages=0;observedManager=buffs;messageObserver=_=>messages++;buffs.Publisher.Message+=messageObserver;
        yield return null;yield return null;ui.RefreshUI();
        long id=buffs.Publisher.Snapshot().States.Single().InstanceId;
        string start=Number(id);Check(!string.IsNullOrEmpty(start),"HUD countdown text exists.");Capture("Running");
        float deadline=Time.realtimeSinceStartup+2;while(Time.realtimeSinceStartup<deadline)yield return null;
        ui.RefreshUI();Check(Number(id)!=start,"HUD countdown changes without wire publication.");Check(messages==0,"No per-second Buff message.");Capture("Later");
        // Manager lifecycle owns suspension; direct collection suspension is
        // resumed by the next enabled manager Update.
        buffs.enabled=false;yield return null;ui.RefreshUI();string frozen=Number(id);Capture("Suspended");
        deadline=Time.realtimeSinceStartup+1;while(Time.realtimeSinceStartup<deadline)yield return null;
        Check(Number(id)==frozen,"HUD frozen value retained.");
        buffs.enabled=true;deadline=Time.realtimeSinceStartup+1.2f;while(Time.realtimeSinceStartup<deadline)yield return null;
        ui.RefreshUI();Check(Number(id)!=frozen,"HUD countdown resumed.");yield return null;Capture("Resumed");
    }
    private void Capture(string name)
    {
        var prior=RenderTexture.active;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
        try {RenderTexture.active=hudTexture;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();Check(image.GetPixels32().Count(p=>p.r>80||p.g>80||p.b>80)>100,"HUD has visible rendered pixels.");File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());}
        finally {RenderTexture.active=prior;Destroy(image);}
    }
}
