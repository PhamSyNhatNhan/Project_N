#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class PNVisualEditorChecks
{
    // Private mob-art review: actual prefab/rig/materials in a disposable preview scene.
    public static string RunStarchanterVfx(string output)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before the preview.");
        int baseline = Resources.FindObjectsOfTypeAll<Material>().Count(x => x.name.StartsWith("Starchanter resonance "));
        var oldStringCamera=PNVisualStringSystem.CameraOverride;float oldStringTime=PNVisualStringSystem.FixedTime;
        int checks = 0; Stage stage = null; GameObject source = null;
        string contentRoot = Application.streamingAssetsPath + "/BaseContent";
        PNVisualPacks.Set("project_n", contentRoot);
        var documents = new PNGameContentSource(contentRoot,"unused",Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveEngine.EveContentBootstrap().Build(new EveEngine.EveIContentModule[] {new PNContentModule(),new PNGameplayContentModule()},documents);
        try
        {
            stage = new Stage(); stage.Camera.backgroundColor = new Color(.24f,.31f,.28f,1);
            stage.Camera.orthographicSize = .48f;
            stage.Camera.transform.position = new Vector3(.05f,1.65f,-3f); stage.Camera.transform.LookAt(new Vector3(.05f,.20f,0));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starchanter/Prefab/Starchanter.prefab");
            Require(prefab.GetComponent<PNStarchanterPresentation>() != null,"prefab owns persistent fork presentation"); checks++;
            source = UnityEngine.Object.Instantiate(prefab); source.SetActive(false); SceneManager.MoveGameObjectToScene(source,stage.Scene);
            foreach(var b in source.GetComponents<MonoBehaviour>())b.enabled=false;
            source.SetActive(true);
            var body = source.GetComponent<PNVisualBody>(); body.Begin(hub.Characters.Get(EveEngine.EveId.Parse("project_n:starchanter")),hub);
            var actor = body.Actor; actor.HideStrings();actor.Play("idle",true);actor.Tick(.6f);
            var facing = actor.GetComponent<PNCameraFacingTarget>();facing.ViewCamera=stage.Camera;facing.Align();
            var fx = source.GetComponent<PNStarchanterPresentation>(); fx.Preview(actor,stage.Camera);fx.Advance(.3f);
            Require(fx.HasWeapon,"idle weapon exists");checks++;stage.Capture(output+"/01_idle.png");
            var focus=source.transform.Find("Starchanter tuning fork and resonance/Open gold tuning fork");
            Require(Mathf.Abs(focus.localScale.x-.6f)<.0001f,"focus is 60 percent of previous size");checks++;
            Require(actor.HasStrings&&actor.Strings.Count==2&&actor.TryGetSocketWorld("chant_crown",out _)&&actor.TryGetSocketWorld("chant_hood_tip",out _),"two puppet strings with resolved hood anchors");checks++;
            actor.ShowStrings();PNVisualStringSystem.CameraOverride=stage.Camera;PNVisualStringSystem.FixedTime=1;
            PNVisualStringSystem.Draw();SceneManager.MoveGameObjectToScene(PNVisualStringSystem.Renderer.gameObject,stage.Scene);
            stage.Capture(output+"/08_two_strings.png");Require(PNVisualStringSystem.LastStrings==2,"both strings enter the shared draw batch");checks++;
            actor.HideStrings();PNVisualStringSystem.Draw();
            fx.Heal(0,.6f,.7f,5,1.5f,2);source.transform.position=Vector3.right*.5f;fx.TeleportArrived();fx.Heal(.75f,.6f,.7f,5,1.5f,2);fx.Advance(0);
            var teleportMesh=source.GetComponentsInChildren<MeshFilter>().Single(x=>x.name=="Starchanter teleport stardust").sharedMesh.vertices;
            Require(Mathf.Abs(teleportMesh.Take(192).Average(x=>x.x))<.12f,"departure dust stays at old world position after teleport");checks++;
            Require(Mathf.Abs(teleportMesh.Skip(192).Average(x=>x.x)-.5f)<.12f,"arrival dust forms at new world position");checks++;
            Require(actor.Mesh.uv2.All(x=>x.y<.4f),"whole puppet fades during teleport appearance");checks++;
            source.transform.position=Vector3.zero;fx.EndCast(false);
            Require(actor.Mesh.uv2.All(x=>Mathf.Abs(x.y-1)<.0001f),"cancel restores whole-puppet opacity");checks++;
            actor.Play("heal_channel",true);actor.Tick(.4f);
            fx.Heal(2.4f,.6f,.7f,5,1.5f,2);
            fx.PulseReady();fx.HealRecipient(source.transform.position,false);
            fx.Advance(.2f);stage.Capture(output+"/02_heal_close.png");
            var cloudSystem=source.GetComponentsInChildren<ParticleSystem>().Single(x=>x.name=="Soft cloud particles");
            var cloudSample=new ParticleSystem.Particle[128];int cloudCount=cloudSystem.GetParticles(cloudSample);
            Require(cloudCount>0&&cloudSample.Take(cloudCount).Any(x=>x.position.y>.10f),"prewarmed smoke particles occupy a layer above ground");checks++;
            var boundary=source.GetComponentsInChildren<LineRenderer>().Single(x=>x.name=="Exact healing radius");
            var boundaryPoints=new Vector3[boundary.positionCount];boundary.GetPositions(boundaryPoints);
            Require(boundaryPoints.All(x=>Mathf.Abs(new Vector2(x.x,x.z).magnitude-2)<.0001f),"boundary matches the full two-metre healing radius");checks++;
            var front=source.GetComponentsInChildren<MeshFilter>().First(x=>x.name=="Outward healing energy front").sharedMesh;
            float firstFront=front.bounds.extents.x;var pose=cloudSample.Take(cloudCount).Select(x=>x.position).ToArray();
            Require(fx.LivingDust>0 && PNStarchanterPresentation.HealingDust.g>PNStarchanterPresentation.HealingDust.r*2,"healing emits green particles");checks++;
            var particles = source.GetComponentsInChildren<ParticleSystem>();
            var before = particles.Select(x=>x.particleCount).ToArray();fx.Advance(0);
            Require(before.SequenceEqual(particles.Select(x=>x.particleCount)),"zero entity delta does not simulate particles");checks++;
            cloudSystem.GetParticles(cloudSample);
            Require(pose.SequenceEqual(cloudSample.Take(cloudCount).Select(x=>x.position)),"zero entity delta leaves smoke positions unchanged");checks++;
            var p0=fx.ForkPosition;fx.Advance(.2f);Require(Vector3.Distance(p0,fx.ForkPosition)>.0001f,"weapon moves while hovering");checks++;
            Require(front.bounds.extents.x>firstFront+.15f,"healing front travels outward from the center");checks++;
            actor.SetFacingLeft(true);fx.Align();Require(fx.ForkPosition.x<0,"weapon follows mirrored rig side");checks++;stage.Capture(output+"/03_mirrored.png");actor.SetFacingLeft(false);
            stage.Camera.orthographicSize=2.4f;stage.Camera.transform.position=new Vector3(0,3,-4);stage.Camera.transform.LookAt(new Vector3(0,.05f,0));facing.Align();
            fx.PulseReady();fx.HealRecipient(Vector3.right*.65f,false);fx.Advance(.6f);stage.Capture(output+"/04_heal_zone.png");
            stage.Camera.orthographicSize=.48f;stage.Camera.transform.position=new Vector3(.05f,1.65f,-3);stage.Camera.transform.LookAt(new Vector3(.05f,.2f,0));facing.Align();
            fx.PulseReady();fx.HealRecipient(Vector3.zero,true);fx.EndCast(true);fx.Advance(.18f);stage.Capture(output+"/05_final_heal.png");
            fx.Heal(2,.6f,.7f,5,1.5f,2);fx.EndCast(false);fx.Advance(.12f);stage.Capture(output+"/06_interrupted.png");
            Require(!fx.Casting,"interrupt leaves no active channel");checks++;
            fx.Advance(.10f);
            Require(boundary.startColor.a<.0001f,"interrupted radius marker gone within 0.2 seconds");checks++;
            var smokeMaterial=cloudSystem.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            Require(smokeMaterial.GetFloat("_Opacity")>0,"interrupted smoke keeps a soft short tail");checks++;
            fx.Advance(.25f);
            Require(smokeMaterial.GetFloat("_Opacity")<.0001f&&!source.GetComponentsInChildren<MeshRenderer>().Any(x=>x.name=="Outward healing energy front"&&x.enabled),"interrupted smoke and wave gone within 0.45 seconds");checks++;
            fx.Heal(2,.6f,.7f,5,1.5f,2);fx.Advance(.5f);fx.EndCast(true);fx.Advance(.5f);
            Require(smokeMaterial.GetFloat("_Opacity")>.5f,"normal completion keeps the longer healing tail");checks++;
            fx.BeginStrike(.25f,Vector3.right*.45f);fx.Advance(.20f);var pulled=fx.ForkPosition;
            fx.ReleaseStrike(Vector3.right*.45f);fx.Advance(.08f);
            Require(fx.ForkPosition.x>pulled.x+.04f,"whole fork reaches forward from its backswing toward contact");checks++;
            var slash=source.GetComponentsInChildren<MeshRenderer>().Where(x=>x.name.StartsWith("Textured violet slash ")).ToArray();
            Require(slash.Length==3&&slash.All(x=>x.enabled&&x.sharedMaterial.GetTexture("_SlashFlowTex")!=null),"slash has three textured flow/dissolve layers");checks++;
            var forkWake=slash[0].GetComponent<MeshFilter>().sharedMesh.vertices;
            Require(Vector3.Distance(forkWake[forkWake.Length-1],focus.TransformPoint(new Vector3(0,.175f,0)))<.001f&&Vector3.Distance(forkWake[forkWake.Length-2],focus.TransformPoint(new Vector3(0,-.205f,0)))<.001f,"latest wake spans the actual whole fork from shaft bottom to prong tip");checks++;
            stage.Capture(output+"/09_slash.png");
            fx.Advance(.4f);Require(slash.All(x=>!x.enabled),"slash expires without a persistent wave");checks++;
            fx.Advance(.4f);var idleFocus=fx.ForkPosition;
            fx.Boost(0,.3f,2);fx.Advance(.01f);
            fx.Boost(.3f,.3f,2);fx.Advance(0);
            Require(fx.ForkPosition.y>idleFocus.y+.18f,"caster raises the focus for blessing");checks++;
            var volume=source.transform.Find("Starchanter tuning fork and resonance/Starchanter one-shot golden downlight");
            var starRenderer=volume.Find("Lingering gold blessing stars").GetComponent<MeshRenderer>();
            Require(starRenderer.sharedMaterial.GetFloat("_Opacity")==0,"stars do not release during preparation");checks++;
            Require(starRenderer.sharedMaterial.GetFloat("_Mode")==15,"buff uses round luminous star cores instead of four-point icons");checks++;
            Require(!source.GetComponentsInChildren<LineRenderer>().Any(x=>x.name=="Descending gold ray"),"old vertical stripes are removed");checks++;
            var registeredRadius=(float)((Newtonsoft.Json.Linq.JObject)hub.Skills.Get(PNStarchanterCombatContent.Boost).Data)["action"]["perform"]["radius"];
            Require(Mathf.Abs(registeredRadius-2)<.0001f&&Mathf.Abs(volume.localScale.x-registeredRadius)<.0001f,"actual registered buff and halo share the new 2 metre radius");checks++;
            var cone=volume.Find("Soft gold downlight volume").GetComponent<MeshFilter>().sharedMesh.vertices;
            Require(cone.Where(x=>x.y>5.99f).All(x=>new Vector2(x.x,x.z).magnitude<.001f),"halo originates at a six-metre apex above the close camera");checks++;
            Require(volume.Find("Brief buff footprint").GetComponent<MeshFilter>().sharedMesh.vertices.All(x=>new Vector2(x.x,x.z).magnitude<=1.00001f),"downlight footprint is clipped to the circular buff radius");checks++;
            fx.BoostPulse();fx.Advance(.04f);
            Require(starRenderer.sharedMaterial.GetFloat("_Opacity")>0,"one-shot gold stars release on the buff callback");checks++;
            var starMesh=starRenderer.GetComponent<MeshFilter>().sharedMesh;var starPose=starMesh.vertices;fx.Advance(0);
            Require(starPose.SequenceEqual(starMesh.vertices),"zero entity delta freezes the stars");checks++;
            stage.Capture(output+"/10_downlight.png");fx.EndCast(true);fx.Advance(.4f);
            Require(volume.Find("Soft gold downlight volume").GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_Opacity")>0,"halo is still visible beyond the previous 0.38s cutoff");checks++;
            stage.Capture(output+"/10b_downlight_linger.png");fx.Advance(.30f);
            Require(volume.Find("Soft gold downlight volume").GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_Opacity")==0&&starRenderer.sharedMaterial.GetFloat("_Opacity")>0,"stars remain briefly after the 0.70 second halo fades");checks++;
            fx.Advance(.3f);Require(starRenderer.sharedMaterial.GetFloat("_Opacity")==0,"stars expire by 0.98 seconds without a sustained buff effect");checks++;
            source.transform.position=Vector3.right*.2f;fx.Advance(1);
            Require(!source.GetComponentsInChildren<LineRenderer>().Any(x=>x.name=="Broken gold foot wake"),"moving after blessing creates no ongoing foot effect");checks++;
            fx.Boost(0,.3f,2);fx.EndCast(false);fx.Advance(.1f);
            Require(starRenderer.sharedMaterial.GetFloat("_Opacity")==0,"cancelled preparation produces no stars");checks++;
            source.transform.position=Vector3.zero;
            fx.Advance(2);Require(fx.LivingDust==0,"healing dust expires after interruption");checks++;
            fx.Boost(.3f,.3f,2);fx.BoostPulse();fx.BoostRecipient(Vector3.zero);fx.EndCast(true);fx.Advance(.15f);stage.Capture(output+"/07_boost.png");
            // Wide native movie includes the whole six-metre downlight and radius.
            fx.Advance(2);fx.Boost(0,.3f,2);fx.Boost(.3f,.3f,2);fx.BoostPulse();fx.EndCast(true);
            var previousTarget=stage.Camera.targetTexture;var previousPosition=stage.Camera.transform.position;var previousRotation=stage.Camera.transform.rotation;float previousSize=stage.Camera.orthographicSize;float previousAspect=stage.Camera.aspect;
            var movieTarget=new RenderTexture(1280,960,24);movieTarget.Create();var movieImage=new Texture2D(1280,960,TextureFormat.RGB24,false);
            try
            {
                stage.Camera.targetTexture=movieTarget;stage.Camera.aspect=4f/3;stage.Camera.orthographicSize=3.35f;stage.Camera.transform.position=new Vector3(0,3.1f,-8);stage.Camera.transform.LookAt(new Vector3(0,2.9f,0));
                for(int frame=0;frame<42;frame++)
                {
                    fx.Advance(1f/30);var saved=RenderTexture.active;
                    try{stage.Camera.Render();RenderTexture.active=movieTarget;movieImage.ReadPixels(new Rect(0,0,1280,960),0,0);movieImage.Apply();File.WriteAllBytes(output+"/halo_"+frame.ToString("000")+".png",movieImage.EncodeToPNG());}
                    finally{RenderTexture.active=saved;}
                }
            }
            finally{stage.Camera.targetTexture=previousTarget;stage.Camera.transform.SetPositionAndRotation(previousPosition,previousRotation);stage.Camera.orthographicSize=previousSize;stage.Camera.aspect=previousAspect;movieTarget.Release();UnityEngine.Object.DestroyImmediate(movieTarget);UnityEngine.Object.DestroyImmediate(movieImage);}
            // Edit Mode does not dispatch this non-ExecuteAlways component's OnDisable; call its same teardown explicitly.
            fx.Dispose();Require(!fx.HasWeapon,"teardown releases fork");checks++;
            fx.Preview(actor,stage.Camera);fx.Advance(.2f);Require(fx.HasWeapon&&!fx.Casting,"pool reuse reconstructs idle presentation");checks++;
        }
        finally
        {
            if(source!=null) { source.GetComponent<PNStarchanterPresentation>()?.Dispose(); source.GetComponent<PNVisualBody>()?.Clear(); UnityEngine.Object.DestroyImmediate(source); }
            stage?.Dispose();PNVisualRuntime.Release();PNVisualPacks.Clear();
            PNVisualStringSystem.Release();PNVisualStringSystem.CameraOverride=oldStringCamera;PNVisualStringSystem.FixedTime=oldStringTime;
        }
        Require(Resources.FindObjectsOfTypeAll<Material>().Count(x=>x.name.StartsWith("Starchanter resonance "))==baseline,"all owned materials released");checks++;
        string report="Starchanter VFX: PASS "+checks+"; native rendered preview, not a Player run.";
        Directory.CreateDirectory(output);File.WriteAllText(output+"/native.txt",report);return report;
    }
}
#endif
