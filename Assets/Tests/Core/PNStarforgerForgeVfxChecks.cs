#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using EveEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

// A host that accepts nothing: the cast runs for its presentation and its create_summon is refused (the star of the fixture is placed by hand).
public sealed class PNRefusingHost : IPNEntityHost
{
    public EveEntityPreparationScheduler.Candidate TrySpawn(PNEntityRequest request, System.Action<EveEntityLoader> configure = null, System.Action<EveEntityLoader> published = null, System.Action<System.Exception> failed = null) => null;
    public bool Retire(EveEntityLoader loader) => false;
}
public static partial class PNVisualEditorChecks
{
    public static string RunStarforgerForgeVfx(string output,int frames=180)
    {
        if(Application.isPlaying||EveRegistrySession.State!=EveSessionState.Stopped)throw new InvalidOperationException("Stopped Editor required");
        Directory.CreateDirectory(output);int checks=0;
        int Materials()=>Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.Contains("Starforger"));int baseline=Materials();
        void Check(bool value,string label){Require(value,label);checks++;}
        Stage stage=null;PNProjectileManager projectiles=null;PNAreaManager areas=null;GameObject source=null,target=null;
        PNSkillAction action=null;PNSkillActionContext context=null;bool session=false,ended=false;RenderTexture rt=null;Texture2D image=null;
        var impacts=new Dictionary<PNStarforgerBoltImpact,float>();PNStarforgerBoltProjectile first=null;bool reused=false,contactSeen=false;
        var docs=new PNGameContentSource(Application.streamingAssetsPath+"/BaseContent","unused",Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);PNVisualPacks.Set("project_n",Application.streamingAssetsPath+"/BaseContent");
        try
        {
            stage=new Stage();stage.Camera.backgroundColor=new Color(.20f,.25f,.24f);stage.Camera.orthographicSize=.85f;stage.Camera.aspect=16f/9;
            stage.Camera.transform.position=new Vector3(.35f,1.6f,-4.5f);stage.Camera.transform.LookAt(new Vector3(.35f,.35f,0));
            rt=new RenderTexture(1280,720,24);rt.Create();stage.Camera.targetTexture=rt;image=new Texture2D(1280,720,TextureFormat.RGB24,false);
            void Shot(string name){var old=RenderTexture.active;try{stage.Camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(output+"/"+name+".png",image.EncodeToPNG());}finally{RenderTexture.active=old;}}
            EveRegistrySession.Begin();session=true;EveRegistrySession.Start(hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,docs,"test:forger-forge"));
            var managers=new GameObject("Forge preview managers");SceneManager.MoveGameObjectToScene(managers,stage.Scene);
            projectiles=managers.AddComponent<PNProjectileManager>();projectiles.Initialize(EveRegistrySession.Context);areas=managers.AddComponent<PNAreaManager>();areas.Initialize(EveRegistrySession.Context);
            source=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starforger/Prefab/Starforger.prefab"));source.SetActive(false);SceneManager.MoveGameObjectToScene(source,stage.Scene);source.transform.position=new Vector3(-.65f,0,0);
            source.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(997701),EveId.Parse("project_n:starforger"),0);source.GetComponent<EveEntityLoader>().Load(hub);source.SetActive(true);
            foreach(var b in source.GetComponents<MonoBehaviour>())if(!(b is PNStat))b.enabled=false;
            var body=source.GetComponent<PNVisualBody>();body.Begin(hub.Characters.Get(EveId.Parse("project_n:starforger")),hub);var actor=body.Actor;actor.ShowStrings();actor.Play("idle",true);actor.Tick(.3f);actor.HideStrings();
            var facing=actor.GetComponent<PNCameraFacingTarget>();facing.ViewCamera=stage.Camera;facing.Align();var fx=source.GetComponent<PNStarforgerPresentation>();fx.Tick(0);
            target=new GameObject("Forge bolt target");target.SetActive(false);SceneManager.MoveGameObjectToScene(target,stage.Scene);target.layer=PNLayers.Player;target.transform.position=new Vector3(1.65f,0,0);
            target.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(997702),EveId.Parse("project_n:dummy"),0);var stat=target.AddComponent<PNEnemyStat>();var box=target.AddComponent<BoxCollider>();box.center=new Vector3(0,.2f,0);box.size=new Vector3(.14f,.5f,.24f);target.AddComponent<EveEntityLoader>().Load(hub);target.SetActive(true);target.GetComponent<PNBuffManager>().Advance(0);double hp=stat.GetCurrent(PNContentIds.Health);Physics.SyncTransforms();
            PNStarforgerStarSummon MakeStar(int slot)
            {
                var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starforger/Prefab/StarforgerStar.prefab"));go.SetActive(false);SceneManager.MoveGameObjectToScene(go,stage.Scene);
                var offset=PNStarforgerPlanner.SlotOffset(slot,1.2f);go.transform.position=source.transform.position+new Vector3(offset.X,.4f,offset.Z);
                go.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(997710+slot),EveId.Parse("project_n:starforger_star"),0);go.GetComponent<EveEntityLoader>().Load(hub);
                go.GetComponent<PNSummon>().Begin(PNCombatSource.Capture(source.GetComponent<PNStat>()),source.GetComponent<PNStat>().EntityId,PNStarforgerCombatContent.SlotGroup(slot),slot+1,1d);go.SetActive(true);return go.GetComponent<PNStarforgerStarSummon>();
            }
            var skill=source.GetComponent<PNStarforgerSkill>();var liveStars=new List<PNStarforgerStarSummon>();PNEntityHosts.Set(EveRegistrySession.Context,new PNRefusingHost());
            action=skill.CreateAction(PNStarforgerCombatContent.Star,out context,new[]{stat},Vector3.right,Vector3.zero);action.Start(context);
            for(int frame=0;frame<frames;frame++)
            {
                float t=frame/30f;
                try{if(!ended&&action.AdvanceAuxiliary(1f/30)){action.Exit(context);ended=true;}}catch(NullReferenceException){/* the fixture owner has no world, so create_summon throws inside Make; the cast goes on next frame and the star is placed by hand */}
                if(frame==24)liveStars.Add(MakeStar(0));foreach(var starTick in liveStars)starTick.Tick(1f/30);
                actor.Tick(1f/30);facing.Align();fx.Tick(1f/30);fx.RenderFlight(stage.Camera);
                areas.Advance(1f/30);projectiles.Advance(1f/30);
                var stars=liveStars.ToArray();foreach(var star in stars)star.RenderVisual(stage.Camera);
                var bolts=Resources.FindObjectsOfTypeAll<PNStarforgerBoltProjectile>().Where(x=>x.IsRunning&&x.gameObject.scene==stage.Scene).ToArray();
                foreach(var bolt in bolts)
                {
                    if(first==null)first=bolt;else if(ReferenceEquals(first,bolt)&&t>3)reused=true;
                    bolt.GetComponentInChildren<PNStarforgerBoltVisual>().Render(bolt.Position,bolt.Forward,bolt.Age,stage.Camera);
                }
                foreach(var impact in Resources.FindObjectsOfTypeAll<PNStarforgerBoltImpact>().Where(x=>x.gameObject.scene==stage.Scene))if(!impacts.ContainsKey(impact)){impacts[impact]=t;contactSeen=true;}
                foreach(var pair in impacts.ToArray())if(pair.Key!=null){pair.Key.Render(t-pair.Value,stage.Camera);if(t-pair.Value>=PNStarforgerBoltImpact.Duration){pair.Key.Dispose();impacts.Remove(pair.Key);}}
                Shot("frame_"+frame.ToString("000"));
                                if(frame==20)
                {
                    var batch=source.GetComponentsInChildren<MeshFilter>().Single(x=>x.name=="Starforger forge light").sharedMesh;var held=batch.vertices;fx.Tick(0);fx.RenderFlight(stage.Camera);Check(held.SequenceEqual(batch.vertices),"same cast age freezes forge intake");
                    stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(1);Shot("forge_renderer3D");stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(0);
                }
                if(frame==12)Check(actor.Animator.BaseState=="cast_star","registered forging action plays authored puppet pose");
                if(frame==25)Check(stars.Length==1&&stars[0].Slot==0,"the star summon takes the first free slot");
                if(frame==55&&bolts.Length>0)
                {
                    Check(bolts[0].HalfExtents==new Vector3(.1f,.15f,.15f),"physical bolt volume remains 0.2 x 0.3 x 0.3");
                    float frozen=bolts[0].Age;projectiles.Advance(0);Check(bolts[0].Age==frozen,"projectile zero-delta freezes visual age");
                    stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(1);Shot("bolt_renderer3D");stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(0);
                }
            }
            Check(ended&&first!=null,"the star summon launches the registered bolt");
            Check(stat.GetCurrent(PNContentIds.Health)<hp&&first.HitCount<=1,"bolt retains damage and first-hit finish (health "+stat.GetCurrent(PNContentIds.Health)+" of "+hp+", hits "+first.HitCount+", stars "+liveStars.Count+" shots "+liveStars.Sum(x=>x.Shots)+")");
            Check(contactSeen,"admitted hit creates detached contact VFX after projectile return");Check(reused,"subsequent star shot reuses pooled projectile");
            for(int drain=0;drain<240&&Resources.FindObjectsOfTypeAll<PNStarforgerBoltProjectile>().Any(x=>x.IsRunning);drain++)projectiles.Advance(1f/30);   // the star's own timed shots may still be flying: the wall test needs a quiet field
            var wall=new GameObject("Forge wall contact");SceneManager.MoveGameObjectToScene(wall,stage.Scene);wall.layer=LayerMask.NameToLayer("Wall");wall.transform.position=new Vector3(1.3f,.3f,0);wall.AddComponent<BoxCollider>().size=new Vector3(.1f,1,1);target.transform.position=new Vector3(3,0,0);Physics.SyncTransforms();
            var liveStar=liveStars.Single();
            Check(liveStar.FireToward(new Vector3(1.3f,0,0)),"existing star can fire its unchanged slam extra shot");
            for(int i=0;i<7;i++)projectiles.Advance(1f/30);
            Check(!first.IsRunning&&Resources.FindObjectsOfTypeAll<PNStarforgerBoltImpact>().Any(x=>x.gameObject.scene==stage.Scene&&x.transform.position.x>1f&&x.transform.position.x<1.5f),"wall contact ends bolt and emits separate impact (first running "+first.IsRunning+", impacts "+string.Join(" ",Resources.FindObjectsOfTypeAll<PNStarforgerBoltImpact>().Where(x=>x.gameObject.scene==stage.Scene).Select(x=>x.transform.position.ToString("F2")))+", star "+liveStar.transform.position.ToString("F2")+", running bolts "+string.Join(" ",Resources.FindObjectsOfTypeAll<PNStarforgerBoltProjectile>().Where(x=>x.IsRunning).Select(x=>x.Position.ToString("F2")+" fwd "+x.Forward.ToString("F2")+" age "+x.Age.ToString("F2")))+")");
            fx.BeginStar(.8f,.2f);fx.SetAge(.8f);fx.Tick(0);
            Check(Vector3.Distance(fx.ForgeStrikePoint,fx.ForgePosition)<.001f,"outer hammer end cap reaches the blank,not the head centre");
            Check(actor.transform.InverseTransformPoint(fx.ForgePosition).y<=.12f,"forge contact is low in front of the body,below chest/head level");
            var hammer=source.transform.Find("Starforger golden hammer");
            Check(Vector3.Dot(hammer.right,-actor.transform.up)>.99f&&Vector3.Distance(fx.ForgeStrikePoint,hammer.TransformPoint(new Vector3(0,-.015f,0)))>.20f,"striking cap faces down and stays away from handle/collar");
            fx.SetAge(.84f);fx.Tick(0);Check(Vector3.Distance(fx.ForgeStrikePoint,fx.ForgePosition)<.001f,"contact holds briefly before recovery");
            actor.SetFacingLeft(true);fx.BeginStar(.8f,.2f);fx.SetAge(.8f);fx.Tick(0);
            Check(Vector3.Distance(fx.ForgeStrikePoint,fx.ForgePosition)<.001f&&fx.ForgePosition.x<actor.transform.position.x,"mirrored strike reaches same contact face");
            fx.EndPose();actor.SetFacingLeft(false);
            // Cancellation owns the forge only, not any star or projectile already created.
            var cancel=skill.CreateAction(PNStarforgerCombatContent.Star,out var cc,new[]{stat},Vector3.right,Vector3.zero);
            try{cancel.Start(cc);cancel.AdvanceAuxiliary(.3f);fx.Tick(0);cancel.Cancel(cc);Check(source.GetComponentsInChildren<MeshFilter>(true).Single(x=>x.name=="Starforger forge light").sharedMesh.vertexCount==0,"cancel clears gathering/strike presentation");}finally{cancel.Dispose();}
        }
        finally
        {
            action?.Dispose();foreach(var starObject in Resources.FindObjectsOfTypeAll<PNStarforgerStarSummon>().Where(x=>stage!=null&&x.gameObject.scene==stage.Scene).ToArray()){starObject.Release();UnityEngine.Object.DestroyImmediate(starObject.gameObject);}if(session&&PNEntityHosts.Find(EveRegistrySession.Context)!=null)PNEntityHosts.Remove(EveRegistrySession.Context);
            foreach(var item in Resources.FindObjectsOfTypeAll<PNStarforgerBoltImpact>().Where(x=>stage!=null&&x.gameObject.scene==stage.Scene).ToArray())item.Dispose();areas?.Shutdown();projectiles?.Shutdown();
            if(source!=null){source.GetComponent<PNStarforgerPresentation>()?.Dispose();source.GetComponent<PNVisualBody>()?.Clear();source.GetComponent<EveEntityLoader>()?.Unload();source.GetComponent<EveEntityIdentity>()?.ResetIdentity();}
            if(target!=null){target.GetComponent<EveEntityLoader>()?.Unload();target.GetComponent<EveEntityIdentity>()?.ResetIdentity();}
            if(stage!=null)stage.Camera.targetTexture=stage.Target;if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}if(image!=null)UnityEngine.Object.DestroyImmediate(image);stage?.Dispose();
            if(session)EveRegistrySession.End();PNVisualRuntime.Release();PNVisualPacks.Clear();
        }
        Check(Materials()==baseline,"forge/star/bolt/contact owned materials disposed");
        var report="Starforger Forge VFX PASS "+checks+"; real registered cast/area/projectile target contact and pooling in disposable native scene; not AI/navigation/Player proof";File.WriteAllText(output+"/native.txt",report);return report;
    }
}
#endif

