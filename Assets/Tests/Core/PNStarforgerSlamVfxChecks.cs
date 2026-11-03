#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

public static partial class PNVisualEditorChecks
{
    public static string RunStarforgerSlamVfx(string output,int frames=150,bool showStrings=false)
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stopped Editor required");
        Directory.CreateDirectory(output);int checks=0;Stage stage=null;GameObject source=null;RenderTexture rt=null;Texture2D image=null;PNStarforgerTelegraph warning=null;
        var oldStringCamera=PNVisualStringSystem.CameraOverride;float oldStringTime=PNVisualStringSystem.FixedTime;
        int baseline=Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name=="Starforger golden hammer"||m.name=="Starforger jump coat"||m.name.StartsWith("Starreaper pierce Starforger"));
        var docs=new PNGameContentSource(Application.streamingAssetsPath+"/BaseContent","unused",Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);PNVisualPacks.Set("project_n",Application.streamingAssetsPath+"/BaseContent");
        var slam=(JObject)hub.Skills.Get(PNStarforgerCombatContent.Slam).Data;
        var hop=PNAgentTrajectorySettings.Parse(slam["action"]["perform"]["hop"],hub,false);
        void Check(bool ok,string label){Require(ok,label);checks++;}
        try
        {
            Check(Mathf.Abs(hop.Height-2f)<.0001f&&Mathf.Abs(hop.Duration-.8f)<.0001f,"registered Hop is 2m high with unchanged 0.8s duration");
            var shake=PNStarforgerTelegraph.LandingShake;
            Check(shake.Duration<=.2f&&shake.Attenuation(6)==0&&shake.Clock==PNCameraClock.Camera,"short landing recoil uses camera clock and local falloff");
            stage=new Stage();stage.Camera.backgroundColor=new Color(.20f,.25f,.24f);stage.Camera.orthographicSize=1.8f;stage.Camera.aspect=4f/3;
            stage.Camera.transform.position=new Vector3(0,3.15f,-4.5f);stage.Camera.transform.LookAt(new Vector3(0,1.15f,0));
            rt=new RenderTexture(1280,960,24);rt.Create();stage.Camera.targetTexture=rt;image=new Texture2D(1280,960,TextureFormat.RGB24,false);
            void Shot(string name){if(showStrings){PNVisualStringSystem.CameraOverride=stage.Camera;PNVisualStringSystem.Draw();}var saved=RenderTexture.active;try{stage.Camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1280,960),0,0);image.Apply();File.WriteAllBytes(output+"/"+name+".png",image.EncodeToPNG());}finally{RenderTexture.active=saved;}}
            source=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starforger/Prefab/Starforger.prefab"));source.SetActive(false);SceneManager.MoveGameObjectToScene(source,stage.Scene);
            foreach(var c in source.GetComponents<MonoBehaviour>())c.enabled=false;source.SetActive(true);source.transform.position=Vector3.left*.7f;
            var body=source.GetComponent<PNVisualBody>();body.Begin(hub.Characters.Get(EveId.Parse("project_n:starforger")),hub);var actor=body.Actor;if(showStrings)actor.ShowStrings();else actor.HideStrings();actor.Play("idle",true);actor.Tick(.2f);
            if(showStrings)Check(actor.Strings.Count==2&&actor.TryGetSocketLocal("hat_crown",out _)&&actor.TryGetSocketLocal("hat_rear_tip",out _),"two authored strings resolve to crown and rear hat sockets");
            var facing=actor.GetComponent<PNCameraFacingTarget>();facing.ViewCamera=stage.Camera;facing.Align();var fx=source.GetComponent<PNStarforgerPresentation>();fx.Tick(0);
            var hammer=source.transform.Find("Starforger golden hammer");var geometry=hammer.GetComponent<MeshFilter>().sharedMesh;var material=hammer.GetComponent<MeshRenderer>().sharedMaterial;
            Check(geometry.bounds.size.x>=.298f&&geometry.bounds.size.z>=.14f,"hammer head is wider and thicker than the former block head");
            Check(geometry.bounds.min.y>-.145f&&geometry.bounds.max.y<.22f,"short 0.25m handle forms a compact heavy hammer");
            Check(geometry.normals.Length==geometry.vertexCount&&geometry.normals.Distinct().Count()>8,"hammer has closed chamfer faces with real surface normals");
            Check(material.shader.name=="PN/StarreaperWeapon"&&!ShaderUtil.ShaderHasError(material.shader),"registered normal-lit metal shader renders the solid gold volume");
            var stable=geometry.vertices;fx.Tick(.25f);Check(stable.SequenceEqual(geometry.vertices),"idle never bends the heavy hammer mesh");
            var heldPosition=hammer.position;fx.Tick(0);Check(heldPosition==hammer.position,"zero entity delta freezes hammer hover");
            var cameraPosition=stage.Camera.transform.position;var cameraRotation=stage.Camera.transform.rotation;float cameraSize=stage.Camera.orthographicSize;
            stage.Camera.transform.position=fx.HeadPosition+new Vector3(.3f,.35f,-1);stage.Camera.transform.LookAt(fx.HeadPosition);stage.Camera.orthographicSize=.25f;Shot("hammer_detail");
            stage.Camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);stage.Camera.orthographicSize=cameraSize;
            warning=PNStarforgerTelegraph.Create(stage.Scene,new Vector3(.4f,0,0),.7f,1.3f);
            bool started=false,airborne=false,landed=false,done=false;Vector3 beforeDrop=Vector3.zero;
            for(int frame=0;frame<frames;frame++)
            {
                float t=frame/30f,cast=t-1;
                if(t>=1&&!started){started=true;body.PlayAction("crouch",.5f);actor.SetPhase(0);fx.BeginSlam(.5f,.8f,.8f);}
                if(started&&!done)
                {
                    fx.SetAge(cast);warning.SetTime(Mathf.Max(0,cast));
                    if(cast<.5f){actor.SetPhase(Mathf.Clamp01(cast/.5f));}
                    else if(cast<1.3f)
                    {
                        if(!airborne){airborne=true;body.PlayAction("air",.8f);actor.SetPhase(0);}
                        float u=Mathf.Clamp01((cast-.5f)/.8f);actor.SetPhase(u);
                        // Preview movement only: not a simulation of the registered
                        // Hop/nav/iframes or gameplay damage.
                        source.transform.position=new Vector3(Mathf.Lerp(-.7f,.4f,u),hop.Height*4*u*(1-u),0);
                    }
                    else
                    {
                        if(!landed){landed=true;source.transform.position=new Vector3(.4f,0,0);body.PlayAction("slam",.8f);actor.SetPhase(0);fx.Slam();warning.Impact();}
                        actor.SetPhase(Mathf.Clamp01((cast-1.3f)/.8f));
                        if(cast>=2.1f){done=true;fx.EndPose();body.EndAction();actor.Play("idle",true);}
                    }
                }
                actor.Tick(1f/30);facing.Align();fx.Tick(1f/30);fx.RenderFlight(stage.Camera);warning.Render(stage.Camera);Shot("frame_"+frame.ToString("000"));
                if(frame==40)Check(actor.Animator.BaseState=="crouch","crouch phase now resolves to an authored puppet pose");
                if(frame==56){Check(actor.Animator.BaseState=="air","airborne state now resolves to tucked-leg pose");beforeDrop=fx.HeadPosition;}
                if(frame==60)
                {
                    var coat=source.GetComponentsInChildren<MeshFilter>().Single(x=>x.name=="Starforger jump coat");
                    Check(coat.sharedMesh==actor.Mesh&&coat.gameObject.activeInHierarchy,"airborne coat follows borrowed live puppet mesh");
                    var batch=source.GetComponentsInChildren<MeshFilter>().Single(x=>x.name=="Starforger flight light").sharedMesh;
                    Check(batch.uv2.Any(v=>v.x==6),"actual flight position history draws a textured energy wake");
                    var ink=source.GetComponentsInChildren<MeshFilter>().Single(x=>x.name=="Starforger flight ink").sharedMesh;
                    var v=ink.vertices;float broad=0;for(int i=0;i<v.Length;i+=4)broad=Mathf.Max(broad,Mathf.Max(Vector3.Distance(v[i],v[i+3]),Vector3.Distance(v[i+1],v[i+2])));
                    Check(broad>=actor.Mesh.bounds.size.y*.85f,"flight wake spans nearly full puppet height");
                    var held=batch.vertices;fx.Tick(0);fx.RenderFlight(stage.Camera);
                    Check(held.SequenceEqual(batch.vertices),"zero clock delta preserves trail history and geometry");
                    stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(1);Shot("flight_renderer3D");stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(0);
                }
                if(frame==70)
                {
                    Check(actor.Animator.BaseState=="slam"&&fx.HeadPosition.y<beforeDrop.y-.3f,"hammer and puppet have dropped into the landing pose");
                    stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(1);Shot("impact_renderer3D");stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(0);
                    var batch=Resources.FindObjectsOfTypeAll<MeshFilter>().Single(x=>x.name=="Starforger slam light"&&x.gameObject.scene==stage.Scene).sharedMesh;
                    Check(batch.uv2.Any(v=>v.x==21)&&batch.uv2.Any(v=>v.x==1)&&batch.uv2.Any(v=>v.x==15),"landing uses torn expanding shell, gold streaks and gold star shards");
                    var held=batch.vertices;warning.SetTime(cast);warning.Render(stage.Camera);Check(held.SequenceEqual(batch.vertices),"same Action age freezes impact animation without Time.deltaTime");
                }
                if(frame==92)Check(Resources.FindObjectsOfTypeAll<MeshFilter>().Single(x=>x.name=="Starforger slam light"&&x.gameObject.scene==stage.Scene).sharedMesh.vertexCount==0,"landing effect clears before recovery ends");
            }
            Check(done&&actor.Animator.BaseState=="idle","recovery returns puppet to idle");
            Check(stable.SequenceEqual(geometry.vertices),"hammer geometry stays rigid through every slam phase");
            Check(!source.GetComponentsInChildren<MeshFilter>(true).Single(x=>x.name=="Starforger jump coat").gameObject.activeInHierarchy,"jump coat retires after landing");
            fx.BeginSlam(.5f,.8f,.8f);fx.SetAge(.7f);fx.Tick(0);fx.RenderFlight(stage.Camera);fx.EndPose();
            Check(!source.GetComponentsInChildren<MeshFilter>(true).Single(x=>x.name=="Starforger jump coat").gameObject.activeInHierarchy&&source.GetComponentsInChildren<MeshFilter>(true).Single(x=>x.name=="Starforger flight light").sharedMesh.vertexCount==0,"cancelling a jump clears coat, wake and particles immediately");
            fx.BeginSlam(.5f,.8f,.8f);fx.SetAge(.7f);fx.Tick(0);fx.RenderFlight(stage.Camera);
            Check(!source.GetComponentsInChildren<MeshFilter>(true).Single(x=>x.name=="Starforger flight ink").sharedMesh.uv2.Any(v=>v.x==9),"a reused cast starts without old trail history");fx.EndPose();
        }
        finally
        {
            PNVisualStringSystem.CameraOverride=oldStringCamera;PNVisualStringSystem.FixedTime=oldStringTime;
            warning?.Dispose();if(source!=null){source.GetComponent<PNStarforgerPresentation>()?.Dispose();source.GetComponent<PNVisualBody>()?.Clear();UnityEngine.Object.DestroyImmediate(source);}
            if(stage!=null)stage.Camera.targetTexture=stage.Target;if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}if(image!=null)UnityEngine.Object.DestroyImmediate(image);stage?.Dispose();PNVisualRuntime.Release();PNVisualPacks.Clear();
        }
        Check(Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name=="Starforger golden hammer"||m.name=="Starforger jump coat"||m.name.StartsWith("Starreaper pierce Starforger"))==baseline,"owned hammer and impact materials are disposed");
        string report="Starforger Slam VFX PASS "+checks+"; native rendered puppet/hammer/impact, explicit preview jump; not gameplay AI/nav/damage/iframe proof";File.WriteAllText(output+"/native.txt",report);return report;
    }
}
#endif
