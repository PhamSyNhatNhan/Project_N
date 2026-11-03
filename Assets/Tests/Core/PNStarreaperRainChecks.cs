#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

public static partial class PNVisualEditorChecks
{
    // Native presentation fixture: real Action charge/cancel, then the same
    // area-owned visual driven explicitly. Does not claim area damage/AI evidence.
    public static string RunStarreaperRain(string output, int frames=150)
    {
        if(Application.isPlaying||EveRegistrySession.State!=EveSessionState.Stopped)throw new InvalidOperationException("Stopped Editor required");
        Directory.CreateDirectory(output);int checks=0;
        int baseline=Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.StartsWith("Starreaper pierce "));
        var oldCamera=PNVisualStringSystem.CameraOverride;float oldTime=PNVisualStringSystem.FixedTime;
        Stage stage=null;GameObject source=null;RenderTexture rt=null;Texture2D tex=null;PNSkillAction action=null;
        PNStarreaperRainVisual rain=null;bool session=false;
        var docs=new PNGameContentSource(Application.streamingAssetsPath+"/BaseContent","unused",Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);
        PNVisualPacks.Set("project_n",Application.streamingAssetsPath+"/BaseContent");
        void Check(bool value,string label){Require(value,label);checks++;}
        try
        {
            stage=new Stage();stage.Camera.backgroundColor=new Color(.17f,.22f,.23f);stage.Camera.orthographicSize=2.05f;stage.Camera.aspect=4f/3;
            stage.Camera.transform.position=new Vector3(0,2.6f,-6);stage.Camera.transform.LookAt(new Vector3(0,1.55f,0));
            rt=new RenderTexture(1280,960,24);rt.Create();stage.Camera.targetTexture=rt;tex=new Texture2D(1280,960,TextureFormat.RGB24,false);
            void Shot(string name){var saved=RenderTexture.active;try{stage.Camera.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,960),0,0);tex.Apply();File.WriteAllBytes(output+"/"+name+".png",tex.EncodeToPNG());}finally{RenderTexture.active=saved;}}
            EveRegistrySession.Begin();session=true;EveRegistrySession.Start(hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,docs,"test:starreaper-rain"));
            source=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starreaper/Prefab/Starreaper.prefab"));source.SetActive(false);SceneManager.MoveGameObjectToScene(source,stage.Scene);
            source.transform.position=new Vector3(-.6f,0,0);source.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(987611),EveId.Parse("project_n:starreaper"),0);
            source.GetComponent<EveEntityLoader>().Load(hub);source.SetActive(true);foreach(var c in source.GetComponents<MonoBehaviour>())c.enabled=false;
            var body=source.GetComponent<PNVisualBody>();body.Begin(hub.Characters.Get(EveId.Parse("project_n:starreaper")),hub);
            var actor=body.Actor;actor.ShowStrings();actor.Play("idle",true);actor.Tick(.5f);
            var facing=actor.GetComponent<PNCameraFacingTarget>();facing.ViewCamera=stage.Camera;facing.Align();
            var fx=source.GetComponent<PNStarreaperPresentation>();fx.Tick(0);PNVisualStringSystem.CameraOverride=stage.Camera;
            var skill=source.GetComponent<PNStarreaperSkill>();action=skill.CreateAction(PNStarreaperCombatContent.Rain,out var context,null,Vector3.right,Vector3.zero);action.Start(context);
            var bow=source.transform.Find("Starreaper violet bow");var batch=bow.Find("Xuyen Tinh charge light").GetComponent<MeshFilter>().sharedMesh;
            var ink=bow.Find("Xuyen Tinh charge ink").GetComponent<MeshFilter>().sharedMesh;
            Check(actor.Animator.BaseState=="rain_charge","real rain Action selects its own phase charge pose");
            rain=new PNStarreaperRainVisual(source.transform,source.layer);
            var landing=new List<Vector3>{new Vector3(.7f,0,0),new Vector3(1.6f,0,.5f),new Vector3(.5f,0,1.3f),new Vector3(-.8f,0,1.4f),new Vector3(-1.4f,0,.5f)};
            bool released=false;
            for(int frame=0;frame<frames;frame++)
            {
                float t=frame/30f;
                if(frame<60)action.AdvanceAuxiliary(1f/30*.9999f);
                if(frame==60)
                {
                    Check(fx.Drawing&&actor.Animator.BaseState=="rain_charge","charge still held immediately before registered 2 seconds");
                    Check(!ink.uv2.Any(v=>v.x==12),"all intake tails finish before release");
                    var origin=fx.ProjectilePosition;var direction=fx.SkyDirection;
                    Check(Vector3.Dot(direction,actor.transform.up)>.98f&&Vector3.Dot(direction,Vector3.up)>.90f,"bow and nocked light point almost vertically upward");
                    action.Cancel(context);action.Dispose();action=null;fx.Tick(0);
                    Check(batch.vertexCount==0&&ink.vertexCount==0,"real pre-shot cancellation clears charge without a sky shot");
                    // Explicit presentation release, not a simulated damage-area launch.
                    fx.Release();body.PlayAction("rain_release",.5f);actor.SetPhase(0);
                    rain.Begin(origin,landing,.5f,1,.15f,direction);released=true;
                    Check(!source.GetComponentsInChildren<LineRenderer>().Any(l=>l.name=="Rain arrow into the sky"),"old untextured sky line is removed");
                }
                if(released){actor.SetPhase(Mathf.Clamp01((t-2)/.5f));rain.Advance(t-2);}
                actor.Tick(1f/30);facing.Align();fx.Tick(1f/30);
                // A native preview has no gameplay camera director. Pass its camera
                // explicitly without changing PNViewCamera's global output.
                if(released)rain.RenderSky(stage.Camera);
                PNVisualStringSystem.FixedTime=t;PNVisualStringSystem.Draw();
                Shot("frame_"+frame.ToString("000"));
                if(frame==30)
                {
                    Check(batch.uv2.Any(v=>v.x==13)&&batch.uv2.Any(v=>v.x==1)&&ink.uv2.Any(v=>v.x==14),"charge renders violet rims, gold threads and black stars");
                    var localBow=actor.transform.InverseTransformPoint(bow.position);Check(Mathf.Abs(localBow.x-Mathf.Max(.58f,actor.Mesh.bounds.extents.x+.30f)*.62f*.77f*.88f)<.01f&&localBow.y>=.42f,"rain grip is close to mask and lifted for a nearly vertical sky shot");
                    var before=batch.vertices;fx.Tick(0);Check(before.SequenceEqual(batch.vertices),"zero actor delta freezes charge flow");
                    var pos=stage.Camera.transform.position;float size=stage.Camera.orthographicSize;
                    stage.Camera.transform.position=new Vector3(-.25f,1.05f,-3);stage.Camera.orthographicSize=.80f;Shot("charge_detail");stage.Camera.transform.position=pos;stage.Camera.orthographicSize=size;
                    stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(1);Shot("charge_renderer3D");stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(0);
                }
                if(frame==67)
                {
                    var sky=source.transform.Find("Starreaper rain visual/Rain sky energy light").GetComponent<MeshFilter>().sharedMesh;
                    Check(sky.uv2.Any(v=>v.x==5)&&sky.uv2.Any(v=>v.x==6),"sky shot uses textured core and flowing wake");
                    var before=sky.vertices;rain.Advance(t-2);rain.RenderSky(stage.Camera);Check(before.SequenceEqual(sky.vertices),"same area age freezes rising shot");
                    stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(1);Shot("sky_renderer3D");stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(0);
                }
            }
            var root=source.transform.Find("Starreaper rain visual");
            var first=root.Find("Rain landing 0 light").GetComponent<MeshFilter>().sharedMesh;
            var firstInk=root.Find("Rain landing 0 ink").GetComponent<MeshFilter>().sharedMesh;
            rain.Advance(.5f);rain.RenderSky(stage.Camera);
            Check(first.uv2.Any(v=>v.x==16)&&firstInk.uv2.Any(v=>v.x==17),"warning combines grounded violet boundary and dark centre");
            var warning=first.vertices;float extent=warning.Take(4).Max(v=>v.x)-warning.Take(4).Min(v=>v.x);
            Check(Mathf.Abs(extent*.82f-1f)<.0001f,"warning mask rim uses the actual 0.5m radius diameter");
            rain.Advance(.9f);rain.RenderSky(stage.Camera);
            Check(first.uv2.Any(v=>v.x==5)&&first.uv2.Any(v=>v.x==6)&&firstInk.uv2.Any(v=>v.x==14),"fall contains textured spindle, violet wake and black stars");
            var held=first.vertices;rain.Advance(.9f);rain.RenderSky(stage.Camera);Check(held.SequenceEqual(first.vertices),"same area age freezes falling rain");
            Shot("fall_detail");
            rain.Advance(1.033f);rain.RenderSky(stage.Camera);
            Check(!first.uv2.Any(v=>v.x==16)&&first.uv2.Any(v=>v.x==1)&&firstInk.uv2.Any(v=>v.x==17),"contact replaces warning with strike fragments and ink deposit");
            Check(first.uv2.Any(v=>v.x==6)&&!first.uv2.Any(v=>v.x==13),"impact consumes incoming wake with no upright energy ribbons");
            Check(firstInk.uv2.Any(v=>v.x==23)&&first.uv2.Any(v=>v.x==20)&&!firstInk.uv2.Any(v=>v.x==18),"explosion uses animated pack impact and smoke sheets instead of procedural lobes");
            var sheetFrames=first.uv2;rain.Advance(1.08f);rain.RenderSky(stage.Camera);
            Check(first.uv2.Where(v=>v.x==20).Any(v=>v.y>sheetFrames.Where(k=>k.x==20).First().y),"impact frame actually advances within its four-frame sequence");
            rain.Advance(1.033f);rain.RenderSky(stage.Camera);
            var impactHeld=first.vertices;rain.Advance(1.033f);rain.RenderSky(stage.Camera);Check(impactHeld.SequenceEqual(first.vertices),"same age freezes explosion and fragments");
            Shot("impact_renderer2D");stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(1);Shot("impact_renderer3D");stage.Camera.GetUniversalAdditionalCameraData().SetRenderer(0);
            rain.Advance(1.421f);rain.RenderSky(stage.Camera);Check(first.vertexCount==0&&firstInk.vertexCount==0,"landing tail completely vanishes after 0.42s");
            rain.Stop();Check(root.GetComponentsInChildren<MeshFilter>().All(m=>m.sharedMesh.vertexCount==0),"area stop clears sky, warnings, falls and impact batches");
            Check(!root.GetComponentsInChildren<LineRenderer>().Any(),"rain has no old untextured line rings or falling lines");
            int retained=root.GetComponentsInChildren<MeshFilter>().Length;
            rain.Begin(fx.ProjectilePosition,landing,.5f,1,.15f,Vector3.up);rain.Advance(.2f);rain.RenderSky(stage.Camera);
            Check(root.GetComponentsInChildren<MeshFilter>().Length==retained&&first.vertexCount>0,"pooled visual restarts and reuses all landing batches");
            rain.Stop();rain.RenderSky(stage.Camera);Check(first.vertexCount==0,"camera redraw cannot resurrect cancelled landing VFX");

        }
        finally
        {
            action?.Dispose();rain?.Dispose();
            if(source!=null){source.GetComponent<EveEntityLoader>()?.Unload();source.GetComponent<EveEntityIdentity>()?.ResetIdentity();source.GetComponent<PNStarreaperPresentation>()?.Dispose();source.GetComponent<PNVisualBody>()?.Clear();UnityEngine.Object.DestroyImmediate(source);}
            if(session)EveRegistrySession.End();
            if(stage!=null)stage.Camera.targetTexture=stage.Target;if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}if(tex!=null)UnityEngine.Object.DestroyImmediate(tex);
            PNVisualStringSystem.Release();stage?.Dispose();PNVisualRuntime.Release();PNVisualPacks.Clear();PNVisualStringSystem.CameraOverride=oldCamera;PNVisualStringSystem.FixedTime=oldTime;
        }
        Check(Resources.FindObjectsOfTypeAll<Material>().Count(m=>m.name.StartsWith("Starreaper pierce "))==baseline,"owned energy materials are disposed");
        string report="Starreaper Rain PASS "+checks+"; real Action charge/cancel and native rendered presentation; release visuals explicitly driven, no area damage/AI Play Mode evidence";
        File.WriteAllText(output+"/native.txt",report);return report;
    }
}
#endif
