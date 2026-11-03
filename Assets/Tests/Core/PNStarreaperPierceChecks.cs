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
    public static string RunStarreaperPierce(string output,int frames=120,bool drawHitbox=false)
    {
        if(Application.isPlaying||EveRegistrySession.State!=EveSessionState.Stopped)throw new InvalidOperationException("Stopped Editor required");
        Directory.CreateDirectory(output);int checks=0;int baseline=Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.StartsWith("Starreaper pierce "));
        var oldStringCamera=PNVisualStringSystem.CameraOverride;float oldStringTime=PNVisualStringSystem.FixedTime;
        Material wireMaterial=null;LineRenderer[] wire=null;var corners=new Vector3[8];
        Stage stage=null;PNProjectileManager manager=null;GameObject source=null;PNSkillAction action=null;PNSkillActionContext context=null;RenderTexture rt=null;Texture2D tex=null;bool session=false;
        var docs=new PNGameContentSource(Application.streamingAssetsPath+"/BaseContent","unused",Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);PNVisualPacks.Set("project_n",Application.streamingAssetsPath+"/BaseContent");
        void Check(bool value,string label){Require(value,label);checks++;}
        try
        {
            stage=new Stage();stage.Camera.backgroundColor=new Color(.20f,.26f,.23f);stage.Camera.orthographicSize=1.0f;stage.Camera.aspect=4f/3;
            stage.Camera.transform.position=new Vector3(0,2,-4);stage.Camera.transform.LookAt(new Vector3(0,.25f,0));
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
            if(drawHitbox)
            {
                wireMaterial=new Material(Resources.Load<Shader>("PNStarweaverEffects")){name="Starreaper preview hitbox",hideFlags=HideFlags.HideAndDontSave,renderQueue=3050};
                wireMaterial.SetFloat("_Mode",17);wireMaterial.SetFloat("_DstBlend",10);wireMaterial.SetFloat("_Opacity",1);wireMaterial.SetColor("_Tint",new Color(.30f,1,.70f,1));
                wire=new LineRenderer[12];
                for(int i=0;i<12;i++)
                {
                    var go=new GameObject("Actual OBB edge "+i);SceneManager.MoveGameObjectToScene(go,stage.Scene);var l=go.AddComponent<LineRenderer>();wire[i]=l;l.sharedMaterial=wireMaterial;l.useWorldSpace=true;l.positionCount=2;l.widthMultiplier=.0025f;l.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;l.receiveShadows=false;l.enabled=false;
                }
            }
            void Wire(PNStarreaperProjectile pShot)
            {
                if(wire==null)return;foreach(var line in wire)line.enabled=pShot!=null&&pShot.IsRunning;if(pShot==null||!pShot.IsRunning)return;
                var h=pShot.HalfExtents;var rot=Quaternion.LookRotation(pShot.Forward,Vector3.up);
                for(int i=0;i<8;i++)corners[i]=pShot.Position+rot*new Vector3((i&1)==0?-h.x:h.x,(i&2)==0?-h.y:h.y,(i&4)==0?-h.z:h.z);
                int edge=0;for(int i=0;i<8;i++)for(int axis=0;axis<3;axis++){int j=i^(1<<axis);if(j<i)continue;wire[edge].SetPosition(0,corners[i]);wire[edge++].SetPosition(1,corners[j]);}
            }
            PNStat Dummy(float x,int id)
            {
                var go=new GameObject("Pierce target "+id);go.SetActive(false);SceneManager.MoveGameObjectToScene(go,stage.Scene);go.layer=PNLayers.Player;go.transform.position=new Vector3(x,0,0);
                go.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id),EveId.Parse("project_n:dummy"),0);var stat=go.AddComponent<PNEnemyStat>();var collider=go.AddComponent<BoxCollider>();collider.center=new Vector3(0,.2f,0);collider.size=new Vector3(.12f,.5f,.22f);go.AddComponent<EveEntityLoader>().Load(hub);go.SetActive(true);go.GetComponent<PNBuffManager>().Advance(0);
                var art=new GameObject("Target puppet");art.transform.SetParent(go.transform,false);var puppet=art.AddComponent<PNVisualActor>();puppet.Bind(hub.Get<PNVisualAppearanceDefinition>().Get(EveId.Parse("project_n:visual_test")),hub,Vector3.zero);puppet.Play("idle",true);puppet.Tick(.1f);var face=art.GetComponent<PNCameraFacingTarget>();face.ViewCamera=stage.Camera;face.Align();return stat;
            }
            var a=Dummy(.42f,987602);var bTarget=Dummy(.95f,987603);double hpA=a.GetCurrent(PNContentIds.Health),hpB=bTarget.GetCurrent(PNContentIds.Health);Physics.SyncTransforms();
            var shader=Resources.Load<Shader>("PNStarreaperPierce");Check(shader!=null&&shader.passCount==2&&!ShaderUtil.ShaderHasError(shader),"pierce shader compiles");
            Check(hub.Get<PNShaderWarmupDefinition>().Get(EveId.Parse("base:shader_starreaperpierce")).ShaderName==shader.name,"pierce shader warm-up alias");
            var skill=source.GetComponent<PNStarreaperSkill>();action=skill.CreateAction(PNStarreaperCombatContent.Pierce,out context,new[]{a},Vector3.right,new Vector3(99,99,99));action.Start(context);
            float firstShot=-1;bool exit=false;PNStarreaperProjectile arrow=null;Vector3[] earlyBeam=null;float lateTailReach=-1;
            for(int frame=0;frame<frames;frame++)
            {
                float t=frame/30f;
                if(!exit&&action.AdvanceAuxiliary(1f/30)){action.Exit(context);exit=true;}
                var launchMuzzle=fx.ProjectilePosition;
                actor.Tick(1f/30);facing.Align();fx.Tick(1f/30);
                var shots=manager.Snapshot();
                if(arrow==null&&shots.Count>0)
                {
                    arrow=Resources.FindObjectsOfTypeAll<PNStarreaperProjectile>().Single(x=>x.IsRunning&&x.gameObject.scene==stage.Scene);firstShot=t+1f/30;
                    Check(firstShot>=1.5f-.001f&&firstShot<1.55f,"fires only after the registered 1.5s windup");
                    Check(Vector3.Distance(arrow.Position,launchMuzzle)<.01f,"launch starts at the live nocked light centre without a visual jump");
                    Check(arrow.HalfExtents==new Vector3(.25f,.15f,.25f)&&arrow.GetComponentInChildren<PNStarreaperArrowVisual>().VisualHalfExtents==arrow.HalfExtents,"0.5 x 0.3 x 0.5 hitbox is passed directly to the visual");
                    Check(actor.Animator.BaseState=="pierce_release","release switches to the recoil and settle pose");
                }
                manager.Advance(1f/30);
                foreach(var pShot in Resources.FindObjectsOfTypeAll<PNStarreaperProjectile>().Where(x=>x.IsRunning&&x.gameObject.scene==stage.Scene))pShot.GetComponentInChildren<PNStarreaperArrowVisual>().Render(pShot.Position,pShot.Forward,pShot.Age,stage.Camera);
                Wire(arrow);
                PNVisualStringSystem.FixedTime=t;PNVisualStringSystem.Draw();
                if(frame==8)earlyBeam=source.transform.Find("Starreaper violet bow/Xuyen Tinh charge light").GetComponent<MeshFilter>().sharedMesh.vertices.Take(4).ToArray();
                Shot("frame_"+frame.ToString("000"));
                if(frame==37||frame==39||frame==42)
                {
                    var inkMesh=source.transform.Find("Starreaper violet bow/Xuyen Tinh charge ink").GetComponent<MeshFilter>().sharedMesh;
                    var inkVertices=inkMesh.vertices;var inkModes=inkMesh.uv2;var localTip=source.transform.Find("Starreaper violet bow").InverseTransformPoint(fx.MuzzlePosition);
                    float reach=0;int plumeVertices=0;
                    for(int k=0;k<inkVertices.Length;k++)if(inkModes[k].x==12){reach=Mathf.Max(reach,Vector3.Distance(inkVertices[k],localTip));plumeVertices++;}
                    if(frame==37)lateTailReach=reach;
                    if(frame==39)Check(plumeVertices>0&&reach<lateTailReach*.65f,"last stream tail physically converges into the front tip before release");
                    if(frame==42)Check(plumeVertices==0&&actor.Animator.BaseState=="pierce_charge","all intake tails finish while still charging, before the shot");
                }
                if(frame==54){stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(1);Shot("flight_renderer3D");stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(0);}
                if(frame==33)
                {
                    var batch=source.transform.Find("Starreaper violet bow/Xuyen Tinh charge light").GetComponent<MeshFilter>().sharedMesh;var vertices=batch.vertices;
                    fx.Tick(0);Check(vertices.SequenceEqual(batch.vertices),"charge particles freeze on zero entity delta");
                    Check(Mathf.Abs(Vector3.Distance(earlyBeam[0],earlyBeam[1])-Vector3.Distance(vertices[0],vertices[1]))<.00001f&&Mathf.Abs(Vector3.Distance(earlyBeam[1],earlyBeam[2])-Vector3.Distance(vertices[1],vertices[2]))<.00001f,"stored light keeps fixed length and width while moving with the string");
                    Check((vertices[0].x+vertices[1].x)*.5f<(earlyBeam[0].x+earlyBeam[1].x)*.5f-.02f,"stored projectile actually moves backward during the draw");
                    var bow=source.transform.Find("Starreaper violet bow");var cord=bow.Find("Starreaper bow string").GetComponent<LineRenderer>();var nock=bow.InverseTransformPoint(cord.GetPosition(1));var beamBack=(vertices[0]+vertices[3])*.5f;
                    Check(Mathf.Abs(beamBack.x-nock.x)<.00001f&&Mathf.Abs(beamBack.y-nock.y)<.00001f,"rear tip stays at the pulled string centre");
                    Check(source.transform.Find("Starreaper violet bow").GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_ChargeGlow")>.6f,"whole bow glows as charge builds");
                    Check(actor.Animator.BaseState=="pierce_charge"&&PNVisualStringSystem.LastStrings==2,"charge pose and both puppet strings render together");
                    Check(bow.Find("Xuyen Tinh charge ink").GetComponent<MeshFilter>().sharedMesh.uv2.Any(v=>v.x==12)&&batch.uv2.Any(v=>v.x==13)&&batch.uv2.Any(v=>v.x==1),"charge combines black intake bodies, violet rims and gold threads");
                    var ink=bow.Find("Xuyen Tinh charge ink").GetComponent<MeshFilter>().sharedMesh;var iv=ink.vertices;var im=ink.uv2;var iu=ink.uv;var front=bow.InverseTransformPoint(fx.MuzzlePosition);int heads=0;
                    for(int k=0;k<iv.Length;k+=4)if(im[k].x==12&&iu[k+1].x>.9999f){heads++;Require(Vector3.Distance((iv[k+1]+iv[k+2])*.5f,front)<.00001f,"incoming head joins the moving FRONT tip");}
                    Check(heads>0,"intake endpoints reach the moving front tip, not the middle of the core");
                    stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(1);Shot("charge_renderer3D");stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(0);
                    var oldCameraPosition=stage.Camera.transform.position;float oldSize=stage.Camera.orthographicSize;
                    var coreCentre=fx.MuzzlePosition-source.transform.Find("Starreaper violet bow").right*.17f;
                    stage.Camera.transform.position=coreCentre-stage.Camera.transform.forward*3;stage.Camera.orthographicSize=.26f;Shot("charge_core_detail");
                    stage.Camera.transform.position=oldCameraPosition;stage.Camera.orthographicSize=oldSize;
                }
            }
            Check(arrow!=null&&arrow.HitCount==2&&arrow.IsRunning,"arrow pierces two targets and remains alive");
            double afterA=a.GetCurrent(PNContentIds.Health),afterB=bTarget.GetCurrent(PNContentIds.Health);Check(afterA<hpA&&afterB<hpB,"both targets receive actual projectile damage");
            manager.Advance(.1f);Check(a.GetCurrent(PNContentIds.Health)==afterA&&bTarget.GetCurrent(PNContentIds.Health)==afterB,"pierced targets are not hit twice");
            var visual=arrow.GetComponentInChildren<PNStarreaperArrowVisual>();visual.Render(arrow.Position,arrow.Forward,arrow.Age,stage.Camera);var trail=visual.transform.Find("Piercing starlight light").GetComponent<MeshFilter>().sharedMesh;var held=trail.vertices;
            manager.Advance(0);visual.Render(arrow.Position,arrow.Forward,arrow.Age,stage.Camera);Check(held.SequenceEqual(trail.vertices),"flight and contact particles freeze on projectile age");
            manager.End(arrow.Handle,PNProjectileEndReason.Requested);Check(visual.GetComponentsInChildren<MeshFilter>().All(m=>m.sharedMesh.vertexCount==0),"pooled end clears both visual batches");
            if(!exit)action.Cancel(context);action.Dispose();action=null;
            var cancelled=skill.CreateAction(PNStarreaperCombatContent.Pierce,out var cc,new[]{a},Vector3.right,Vector3.zero);
            try{cancelled.Start(cc);cancelled.AdvanceAuxiliary(.8f);fx.Tick(.1f);cancelled.Cancel(cc);fx.Tick(0);Check(manager.ActiveCount==0&&source.transform.Find("Starreaper violet bow/Xuyen Tinh charge light").GetComponent<MeshFilter>().sharedMesh.vertexCount==0,"cancel removes charge without release or projectile");}finally{cancelled.Dispose();}
            // A high target must now be missed; lowering it into the short volume
            // still proves the retained sideways width on this same shot.
            double edgeHp=bTarget.GetCurrent(PNContentIds.Health);
            var reuse=skill.CreateAction(PNStarreaperCombatContent.Pierce,out var reuseContext,new[]{a},Vector3.right,Vector3.zero);
            try
            {
                reuse.Start(reuseContext);reuse.AdvanceAuxiliary(1.5f);fx.Tick(0);
                var reused=Resources.FindObjectsOfTypeAll<PNStarreaperProjectile>().Single(x=>x.IsRunning&&x.gameObject.scene==stage.Scene);
                Check(ReferenceEquals(reused,arrow)&&reused.HitCount==0&&reused.Age==0,"pool reuse starts with a fresh clock and hit state");
                Check(reused.transform.Find("Starreaper arrow visual/Faceted starlance head")==null,"pooled shot is a light stream without a solid arrow head");
                bTarget.transform.position=reused.Position+reused.Forward*1.4f+Vector3.Cross(Vector3.up,reused.Forward)*.31f+Vector3.up*.48f;Physics.SyncTransforms();
                var stream=reused.GetComponentInChildren<PNStarreaperArrowVisual>();stream.Render(reused.Position,reused.Forward,reused.Age,stage.Camera);
                var flightMesh=stream.transform.Find("Piercing starlight light").GetComponent<MeshFilter>().sharedMesh;
                Check(!flightMesh.uv2.Any(v=>v.x>3.5f&&v.x<4.5f),"oval energy envelope is removed from the live projectile");
                Check(new[]{0,5,6,7,9}.All(mode=>flightMesh.uv2.Any(v=>v.x==mode))&&!flightMesh.uv2.Any(v=>v.x==2||v.x==8),"flight joins old soft wake, sheath and side energy with no detached leading effect");
                var flightMaterial=stream.transform.Find("Piercing starlight light").GetComponent<MeshRenderer>().sharedMaterial;
                Check(new[]{"_BeamTex","_FlowTex","_CoverTex"}.All(property=>flightMaterial.GetTexture(property)!=null),"all three project-owned pack masks are bound");
                for(int i=0;i<36;i++)manager.Advance(1f/30);
                Check(reused.HitCount==1&&bTarget.GetCurrent(PNContentIds.Health)==edgeHp,"0.3m height misses the target formerly caught by the 1m volume");
                bTarget.transform.position=reused.Position+reused.Forward*.6f+Vector3.Cross(Vector3.up,reused.Forward)*.31f+Vector3.up*.12f;Physics.SyncTransforms();
                for(int i=0;i<18;i++)manager.Advance(1f/30);
                Check(reused.HitCount==2&&bTarget.GetCurrent(PNContentIds.Health)<edgeHp,"shorter hitbox retains its 0.5m sideways width");
                manager.End(reused.Handle,PNProjectileEndReason.Requested);reuse.Exit(reuseContext);
            }
            finally{reuse.Dispose();}
        }
        finally
        {
            action?.Dispose();manager?.Shutdown();if(wireMaterial!=null)UnityEngine.Object.DestroyImmediate(wireMaterial);if(session)EveRegistrySession.End();
            if(source!=null){source.GetComponent<PNStarreaperPresentation>()?.Dispose();source.GetComponent<PNVisualBody>()?.Clear();UnityEngine.Object.DestroyImmediate(source);}
            if(stage!=null)stage.Camera.targetTexture=stage.Target;if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}if(tex!=null)UnityEngine.Object.DestroyImmediate(tex);PNVisualStringSystem.Release();stage?.Dispose();PNVisualRuntime.Release();PNVisualPacks.Clear();PNVisualStringSystem.CameraOverride=oldStringCamera;PNVisualStringSystem.FixedTime=oldStringTime;
        }
        Check(Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.StartsWith("Starreaper pierce "))==baseline,"owned energy materials are released");
        string report="Starreaper Pierce PASS "+checks+"; actual Action/projectile manager/contacts and rendered native preview, not gameplay Play Mode";File.WriteAllText(output+"/native.txt",report);return report;
    }
}
#endif
