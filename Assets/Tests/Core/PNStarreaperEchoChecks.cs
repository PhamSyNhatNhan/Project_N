#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

public static partial class PNVisualEditorChecks
{
    public static string RunStarreaperEcho(string output,int frames=120)
    {
        if(Application.isPlaying||EveRegistrySession.State!=EveSessionState.Stopped)throw new InvalidOperationException("Stopped Editor required");
        Directory.CreateDirectory(output);int checks=0;int echoBaseline=Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.StartsWith("Starreaper echo "));int baseline=Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.StartsWith("Starreaper pierce "));
        var oldStringCamera=PNVisualStringSystem.CameraOverride;float oldStringTime=PNVisualStringSystem.FixedTime;
        Material wireMaterial=null;
        Stage stage=null;PNProjectileManager manager=null;GameObject source=null;var madeShadows=new System.Collections.Generic.List<GameObject>();PNSkillAction action=null;PNSkillActionContext context=null;RenderTexture rt=null;Texture2D tex=null;bool session=false;
        var docs=new PNGameContentSource(Application.streamingAssetsPath+"/BaseContent","unused",Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);PNVisualPacks.Set("project_n",Application.streamingAssetsPath+"/BaseContent");
        void Check(bool value,string label){Require(value,label);checks++;}
        try
        {
            stage=new Stage();stage.Camera.backgroundColor=new Color(.20f,.26f,.23f);stage.Camera.orthographicSize=1.55f;stage.Camera.aspect=4f/3;
            stage.Camera.transform.position=new Vector3(-.65f,2,-4);stage.Camera.transform.LookAt(new Vector3(-.65f,.25f,0));
            rt=new RenderTexture(1280,960,24);rt.Create();stage.Camera.targetTexture=rt;tex=new Texture2D(1280,960,TextureFormat.RGB24,false);
            void Shot(string name){var saved=RenderTexture.active;try{stage.Camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,960),0,0);tex.Apply();File.WriteAllBytes(output+"/"+name+".png",tex.EncodeToPNG());}finally{RenderTexture.active=saved;}}
            EveRegistrySession.Begin();session=true;EveRegistrySession.Start(hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,docs,"test:starreaper-pierce"));
            var mg=new GameObject("Pierce manager");SceneManager.MoveGameObjectToScene(mg,stage.Scene);manager=mg.AddComponent<PNProjectileManager>();manager.Initialize(EveRegistrySession.Context);
            source=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starreaper/Prefab/Starreaper.prefab"));source.SetActive(false);SceneManager.MoveGameObjectToScene(source,stage.Scene);source.transform.position=new Vector3(-.7f,0,0);
            source.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(987601),EveId.Parse("project_n:starreaper"),0);source.GetComponent<EveEntityLoader>().Load(hub);source.SetActive(true);
            foreach(var b in source.GetComponents<MonoBehaviour>())b.enabled=false;
            var body=source.GetComponent<PNVisualBody>();body.Begin(hub.Characters.Get(EveId.Parse("project_n:starreaper")),hub);var actor=body.Actor;actor.ShowStrings();actor.Play("idle",true);actor.Tick(.5f);
            var facing=actor.GetComponent<PNCameraFacingTarget>();facing.ViewCamera=stage.Camera;facing.Align();var fx=source.GetComponent<PNStarreaperPresentation>();fx.Tick(0);PNVisualStringSystem.CameraOverride=stage.Camera;
            Check(actor.Strings.Count==2&&actor.TryGetSocketLocal("hat_crown",out var crownSocket)&&actor.TryGetSocketLocal("hat_tail_tip",out var tailSocket),"two puppet strings bind to crown and dynamic hat-tail sockets");
            PNStat Dummy(float x,int id)
            {
                var go=new GameObject("Pierce target "+id);go.SetActive(false);SceneManager.MoveGameObjectToScene(go,stage.Scene);go.layer=PNLayers.Player;go.transform.position=new Vector3(x,0,0);
                go.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id),EveId.Parse("project_n:dummy"),0);var stat=go.AddComponent<PNEnemyStat>();var collider=go.AddComponent<BoxCollider>();collider.center=new Vector3(0,.2f,0);collider.size=new Vector3(.12f,.5f,.22f);go.AddComponent<EveEntityLoader>().Load(hub);go.SetActive(true);go.GetComponent<PNBuffManager>().Advance(0);
                var art=new GameObject("Target puppet");art.transform.SetParent(go.transform,false);var puppet=art.AddComponent<PNVisualActor>();puppet.Bind(hub.Get<PNVisualAppearanceDefinition>().Get(EveId.Parse("project_n:visual_test")),hub,Vector3.zero);puppet.Play("idle",true);puppet.Tick(.1f);var face=art.GetComponent<PNCameraFacingTarget>();face.ViewCamera=stage.Camera;face.Align();return stat;
            }
            var a=Dummy(.42f,987602);var bTarget=Dummy(.95f,987603);double hpA=a.GetCurrent(PNContentIds.Health),hpB=bTarget.GetCurrent(PNContentIds.Health);Physics.SyncTransforms();
            var shader=Resources.Load<Shader>("PNStarreaperPierce");Check(shader!=null&&shader.passCount==2&&!ShaderUtil.ShaderHasError(shader),"pierce shader compiles");
            Check(hub.Get<PNShaderWarmupDefinition>().Get(EveId.Parse("base:shader_starreaperpierce")).ShaderName==shader.name,"pierce shader warm-up alias");
                        var skill=source.GetComponent<PNStarreaperSkill>();var unused=skill.CreateAction(PNStarreaperCombatContent.Afterimage,out context,new[]{a},Vector3.right,source.transform.position);unused.Dispose();
            int shadowCount=0;
            PNStarreaperShadowSummon MakeShadow()
            {
                var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starreaper/Prefab/StarreaperShadow.prefab"));go.SetActive(false);SceneManager.MoveGameObjectToScene(go,stage.Scene);go.transform.position=source.transform.position;
                go.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(987650+shadowCount++),EveId.Parse("project_n:starreaper_shadow"),0);go.GetComponent<EveEntityLoader>().Load(hub);
                go.GetComponent<PNSummon>().Begin(PNCombatSource.Capture(source.GetComponent<PNStat>()),source.GetComponent<PNStat>().EntityId,"starreaper_shadow",shadowCount,1d);go.SetActive(true);madeShadows.Add(go);return go.GetComponent<PNStarreaperShadowSummon>();
            }
            var shadow=MakeShadow();
            Check(shadow.GetComponent<PNSummon>().IsActive&&shadow.GetComponent<PNSummon>().OwnerSource.LoadedSource==source.GetComponent<PNStat>(),"real registered shadow summon is created with the owner's combat source");
            fx.BeginRetreat(.16f);int peak=0;
            for(int frame=0;frame<frames;frame++)
            {
                float t=(frame+1)/30f;source.transform.position=new Vector3(-.7f-1.5f*Mathf.Clamp01(t/.16f),0,0);
                actor.Tick(1f/30);facing.Align();fx.Tick(1f/30);shadow.Tick(1f/30);manager.Advance(1f/30);
                shadow.RenderPreview(stage.Camera);
                foreach(var arrow in Resources.FindObjectsOfTypeAll<PNStarreaperProjectile>().Where(x=>x.IsRunning&&x.gameObject.scene==stage.Scene))arrow.GetComponentInChildren<PNStarreaperArrowVisual>().Render(arrow.Position,arrow.Forward,arrow.Age,stage.Camera);
                if(frame==2)
                {
                    var coat=actor.transform.Find("Starreaper retreat coat");Check(coat!=null&&coat.gameObject.activeSelf&&coat.GetComponent<MeshFilter>().sharedMesh==actor.Mesh,"retreat coat covers the actual live puppet mesh");
                    var trail=source.transform.Find("Starreaper violet bow/Xuyen Tinh charge ink").GetComponent<MeshFilter>().sharedMesh;var vertices=trail.vertices.Take(4).Select(v=>source.transform.Find("Starreaper violet bow").TransformPoint(v)).ToArray();
                    Check(vertices.Max(v=>v.y)-vertices.Min(v=>v.y)>=actor.Mesh.bounds.size.y,"retreat trail geometry covers full puppet height including hat");
                    var held=trail.vertices;fx.Tick(0);Check(held.SequenceEqual(trail.vertices),"zero delta freezes the retreat trail and coat timing");
                    stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(1);Shot("retreat_renderer3D");stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(0);
                }
                if(frame==15)Check(actor.transform.Find("Starreaper retreat coat").gameObject.activeSelf,"coat persists beyond its old 0.22s fade");
                if(frame==20)Check(!actor.transform.Find("Starreaper retreat coat").gameObject.activeSelf,"coat finishes its new 0.45s fade while real puppet stays visible");
                if(frame==62)
                {
                    var live=Resources.FindObjectsOfTypeAll<PNStarreaperProjectile>().Single(x=>x.IsRunning&&x.gameObject.scene==stage.Scene);
                    var v=live.GetComponentInChildren<PNStarreaperArrowVisual>();var m=v.transform.Find("Piercing starlight light").GetComponent<MeshFilter>().sharedMesh;
                    Check(v.Simplified&&m.uv2.Any(k=>k.x==5)&&!m.uv2.Any(k=>k.x==6||k.x==7||k.x==11||k.x==15),"shadow arrow has simple core and short wake without ornate ribbons or stars");
                    Check(live.HalfExtents==new Vector3(.25f,.15f,.25f),"simple arrow retains the original physical volume");
                }
                peak=Math.Max(peak,manager.ActiveCount);PNVisualStringSystem.FixedTime=t;PNVisualStringSystem.Draw();Shot("frame_"+frame.ToString("000"));
                if(frame==30)
                {
                    Check(manager.ActiveCount==0&&shadow.Started&&!shadow.Locked,"shadow holds for its registered 2 seconds before shooting");
                    Check(shadow.GetComponentsInChildren<Collider>().Length==0,"visual shadow has no collider or health entity");
                    var echo=shadow.transform.Find("Starreaper echo");var bodyMaterial=echo.Find("Echo atlas silhouette").GetComponent<MeshRenderer>().sharedMaterial;
                    Check(bodyMaterial.GetFloat("_EchoMode")==1&&bodyMaterial.GetTexture("_MainTex")!=null,"shadow borrows live puppet atlas for dark silhouette");
                    Check(echo.Find("Starreaper violet bow")!=null,"shadow has the rigid bow and a drawn energy string");
                    Check(echo.Find("Starreaper violet bow").GetComponent<MeshRenderer>().sharedMaterial.HasProperty("_EchoMode")&&echo.Find("Starreaper violet bow").GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_EchoMode")==2,"echo bow uses its own fading dark material");
                    var position=stage.Camera.transform.position;var rotation=stage.Camera.transform.rotation;float size=stage.Camera.orthographicSize;stage.Camera.transform.position=new Vector3(-.7f,.65f,-3);stage.Camera.transform.LookAt(new Vector3(-.7f,.25f,0));stage.Camera.orthographicSize=.65f;Shot("echo_detail");stage.Camera.transform.position=position;stage.Camera.transform.rotation=rotation;stage.Camera.orthographicSize=size;
                    stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(1);Shot("echo_renderer3D");stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(0);
                }
                if(frame==61)Check(manager.ActiveCount==1,"first actual projectile launches after the delay");
                if(frame==66)Check(shadow.Shots==2&&shadow.IsDissolving,"second shot ends gameplay while the dissolve continues");
                if(frame==75){Check(shadow.IsDissolving,"clone is still dissolving after old 0.35s point");Shot("dissolve_detail");}
            }
            Check(peak==2,"shadow produces exactly two actual pooled projectiles");
            Check(shadow.Shots==2,"the shadow fired exactly its two arrows");
            var again=MakeShadow();again.Tick(.1f);var againSummon=again.GetComponent<PNSummon>();
            source.SetActive(false);again.Tick(.1f);againSummon.Tick();Check(againSummon.IsActive&&!againSummon.IsRetiring&&again.Started,"independent shadow survives owner disable");
            source.SetActive(true);int arrowsBefore=manager.ActiveCount;int againShots=again.Shots;again.Release();UnityEngine.Object.DestroyImmediate(again.gameObject);Check(again==null&&againSummon==null&&againShots==0&&manager.ActiveCount==arrowsBefore&&shadow.Shots==2,"destroying a shadow before its shot clears it without firing");
        }
        finally
        {
            action?.Dispose();foreach(var made in madeShadows)if(made!=null){made.GetComponent<PNStarreaperShadowSummon>()?.Release();UnityEngine.Object.DestroyImmediate(made);}manager?.Shutdown();if(wireMaterial!=null)UnityEngine.Object.DestroyImmediate(wireMaterial);if(session)EveRegistrySession.End();
            if(source!=null){source.GetComponent<PNStarreaperPresentation>()?.Dispose();source.GetComponent<PNVisualBody>()?.Clear();UnityEngine.Object.DestroyImmediate(source);}
            if(stage!=null)stage.Camera.targetTexture=stage.Target;if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}if(tex!=null)UnityEngine.Object.DestroyImmediate(tex);PNVisualStringSystem.Release();stage?.Dispose();PNVisualRuntime.Release();PNVisualPacks.Clear();PNVisualStringSystem.CameraOverride=oldStringCamera;PNVisualStringSystem.FixedTime=oldStringTime;
        }
        Check(Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.StartsWith("Starreaper pierce "))==baseline,"owned energy materials are released");
        Check(Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.StartsWith("Starreaper echo "))==echoBaseline,"echo atlas and bow materials are released");
        string report="Starreaper Echo PASS "+checks+"; real registered shadow summon/two projectiles and native render; retreat is explicitly animated fixture, not real AI motion";File.WriteAllText(output+"/native.txt",report);return report;
    }
}
#endif
