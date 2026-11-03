#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

public static partial class PNVisualEditorChecks
{
    public static string RunStarreaperBow(string output,int movieFrames=0,bool clothReview=false,bool volumeReview=false)
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stopped Editor required");
        Directory.CreateDirectory(output);Stage stage=null;GameObject source=null;RenderTexture target=null;Texture2D image=null;int checks=0;
        int baseline=Resources.FindObjectsOfTypeAll<Material>().Count(x=>x.name=="Starreaper violet bow");
        string contentRoot=Application.streamingAssetsPath+"/BaseContent";PNVisualPacks.Set("project_n",contentRoot);
        var docs=new PNGameContentSource(contentRoot,"unused",Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveEngine.EveContentBootstrap().Build(new EveEngine.EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);
        try
        {
            stage=new Stage();stage.Camera.backgroundColor=new Color(.17f,.21f,.22f);stage.Camera.orthographicSize=clothReview?.5f:.40f;stage.Camera.aspect=4f/3;
            stage.Camera.transform.position=new Vector3(.07f,1.6f,-3);stage.Camera.transform.LookAt(new Vector3(.07f,.23f,0));
            target=new RenderTexture(1024,768,24);target.Create();stage.Camera.targetTexture=target;image=new Texture2D(1024,768,TextureFormat.RGB24,false);
            source=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starreaper/Prefab/Starreaper.prefab"));source.SetActive(false);SceneManager.MoveGameObjectToScene(source,stage.Scene);
            foreach(var c in source.GetComponents<MonoBehaviour>())c.enabled=false;source.SetActive(true);
            var body=source.GetComponent<PNVisualBody>();body.Begin(hub.Characters.Get(EveEngine.EveId.Parse("project_n:starreaper")),hub);
            var actor=body.Actor;actor.HideStrings();actor.Play("idle",true);actor.Tick(.5f);var facing=actor.GetComponent<PNCameraFacingTarget>();facing.ViewCamera=stage.Camera;facing.Align();
            var fx=source.GetComponent<PNStarreaperPresentation>();fx.Tick(0);
            var bow=source.transform.Find("Starreaper violet bow");var mesh=bow.GetComponent<MeshFilter>().sharedMesh;var material=bow.GetComponent<MeshRenderer>().sharedMaterial;
            Require(mesh.bounds.size.z>.02f,"bow has solid bevel depth");checks++;
            Require(material.shader.name=="PN/StarreaperWeapon"&&material.passCount==2&&!ShaderUtil.ShaderHasError(material.shader),"opaque normal-lit weapon shader supports both renderers");checks++;
            Require(hub.Get<PNShaderWarmupDefinition>().Get(EveEngine.EveId.Parse("base:shader_starreaperweapon")).ShaderName==material.shader.name,"solid weapon shader is registered for warm-up");checks++;
            Require(mesh.normals.Length==mesh.vertexCount&&mesh.normals.Any(n=>n.z<-.9f)&&mesh.normals.Any(n=>n.z>.9f),"solid frame has real front and back surface normals");checks++;
            Require(bow.Find("Round grip")==null&&mesh.name.Contains("continuous angular frame"),"grip belongs to the angular frame without a separate round tube");checks++;
            Require(bow.Find("Outer shoulder armour").localPosition.z<bow.Find("Inner shoulder armour").localPosition.z&&bow.Find("Outer shoulder armour").GetComponent<MeshFilter>().sharedMesh.bounds.size.z>.02f,"upper armour shells are separately raised and bevelled");checks++;
            Require(!source.GetComponentsInChildren<LineRenderer>().Any(x=>x.name.Contains("ink contour")),"painted black contour renderers are removed");checks++;
            Require(mesh.bounds.max.y>Mathf.Abs(mesh.bounds.min.y)*1.8f&&mesh.bounds.min.x<-.20f,"concept D upper crescent is larger and extends behind the lower limb");checks++;
            var line=source.GetComponentsInChildren<LineRenderer>().Single(x=>x.name=="Starreaper bow string");
            var top=bow.InverseTransformPoint(line.GetPosition(0));var bottom=bow.InverseTransformPoint(line.GetPosition(2));
            Require(Mathf.Abs(top.x-bottom.x)<.0001f&&top.y<mesh.bounds.max.y-.1f,"straight energy string anchors at the inner shoulder instead of the ornamental tip");checks++;
            var halo=source.GetComponentsInChildren<LineRenderer>().Single(x=>x.name=="Starreaper string energy halo");
            Require(halo.enabled&&halo.widthMultiplier>line.widthMultiplier*2&&line.sharedMaterial.GetColor("_Tint").r>1,"energy string has a bright core and a separate soft halo");checks++;
            var idleStart=bow.position;var idleMesh=mesh.vertices;fx.Tick(.6f);
            Require(Vector3.Distance(idleStart,bow.position)>.005f&&idleMesh.SequenceEqual(mesh.vertices),"idle floats the entire rigid bow without bending its geometry");checks++;
            var idleHeld=bow.position;fx.Tick(0);Require(idleHeld==bow.position,"zero delta freezes bow idle");checks++;
            var atRest=line.GetPosition(1);
            fx.BeginDraw(1,Vector3.right);fx.SetAge(.95f);fx.Tick(0);
            Require(Vector3.Distance(atRest,line.GetPosition(1))>.04f,"string is pulled visibly back");checks++;
            Require(idleMesh.SequenceEqual(mesh.vertices),"rigid bow mesh remains identical while the string draws");checks++;
            var fixedPosition=bow.position;var drawn=mesh.vertices;var stringDraw=line.GetPosition(1);fx.Tick(0);
            Require(drawn.SequenceEqual(mesh.vertices)&&stringDraw==line.GetPosition(1),"zero delta freezes the bow");checks++;
            Require(source.GetComponentsInChildren<LineRenderer>().Single(x=>x.name=="Starreaper nocked arrow").enabled,"drawn bow retains a nocked arrow");checks++;
            fx.Release();fx.EndPose();fx.Tick(.25f);
            Require(Vector3.Distance(stringDraw,line.GetPosition(1))>.04f,"string snaps forward after release");checks++;
            Require(idleMesh.SequenceEqual(mesh.vertices),"release and idle blending preserve rigid bow geometry");checks++;
            actor.SetFacingLeft(true);actor.Tick(0);fx.Tick(0);Require(bow.position.x<0&&bow.localScale.x<0,"mirroring preserves the upper limb at the top");checks++;actor.SetFacingLeft(false);actor.Tick(0);
            fx.Dispose();Require(source.transform.Find("Starreaper violet bow")==null,"explicit teardown removes bow resources");checks++;
            fx.Tick(0);Require(source.transform.Find("Starreaper violet bow")!=null,"pool reuse reconstructs bow");checks++;
            var rig=hub.Get<PNVisualRigDefinition>().Get(EveEngine.EveId.Parse("project_n:starreaper")).Resolve(id=>hub.Get<PNVisualRigDefinition>().Get(id));
            if(clothReview)
            {
                Require(rig.Dynamics.Count==9,"three independent three-link cloth chains");checks++;
                Require(actor.Mesh.vertexCount>570,"hat and cloak render as dense skinned grids");checks++;
                var frozen=(float[])actor.Animator.BoneAffine.Clone();actor.Tick(0);
                Require(frozen.SequenceEqual(actor.Animator.BoneAffine),"zero delta freezes cloth springs");checks++;
                source.transform.position=Vector3.right*.10f;actor.Tick(1f/30);var moved=(float[])actor.Animator.BoneAffine.Clone();
                Require(new[]{"hat_tail_c","coat_left_c","coat_right_c"}.All(n=>{int o=rig.BoneIndex(n)*6;return Math.Abs(moved[o]-frozen[o])>.00001f||Math.Abs(moved[o+1]-frozen[o+1])>.00001f;}),"all three cloth tips respond to motion");checks++;
                Require(actor.Mesh.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)),"skinned cloth stays finite after motion");checks++;
                source.transform.position=Vector3.zero;actor.Animator.ResetDynamics();actor.Tick(0);
            }
            if(volumeReview)
            {
                var current=source.transform.Find("Starreaper violet bow");var cameraData=stage.Camera.GetUniversalAdditionalCameraData();var oldPosition=stage.Camera.transform.position;var oldRotation=stage.Camera.transform.rotation;float oldSize=stage.Camera.orthographicSize;
                var oldBowRotation=current.rotation;var lines=current.GetComponentsInChildren<LineRenderer>();var enabledLines=lines.Select(x=>x.enabled).ToArray();foreach(var l in lines)l.enabled=false;actor.Renderer.enabled=false;
                stage.Camera.orthographicSize=.31f;stage.Camera.transform.position=current.position+new Vector3(0,.9f,-2);stage.Camera.transform.LookAt(current.position+Vector3.up*.08f);
                void Photo(string filename){var saved=RenderTexture.active;try{stage.Camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1024,768),0,0);image.Apply();File.WriteAllBytes(output+"/"+filename,image.EncodeToPNG());}finally{RenderTexture.active=saved;}}
                cameraData.SetRenderer(0);Photo("volume_renderer2D.png");current.rotation=oldBowRotation*Quaternion.Euler(0,48,0);cameraData.SetRenderer(1);Photo("volume_oblique3D.png");
                current.rotation=oldBowRotation;cameraData.SetRenderer(0);stage.Camera.transform.SetPositionAndRotation(oldPosition,oldRotation);stage.Camera.orthographicSize=oldSize;actor.Renderer.enabled=true;for(int i=0;i<lines.Length;i++)lines[i].enabled=enabledLines[i];
            }
            bool shot=false,ended=false;string lastState=null;
            for(int frame=0;frame<Math.Max(movieFrames,1);frame++)
            {
                float t=frame/30f;
                if(clothReview){string state=t<2?"idle":t<4?"run":t<4.16f?"run":"idle";if(state!=lastState){actor.Play(state,true);lastState=state;}
                    float x=t<2?0:t<4?Mathf.Sin((t-2)*Mathf.PI)*.16f:t<4.16f?-1.5f*Mathf.SmoothStep(0,1,(t-4)/.16f):-1.5f;
                    source.transform.position=Vector3.right*x;
                    stage.Camera.transform.position=new Vector3(x+.07f,1.6f,-3);stage.Camera.transform.LookAt(new Vector3(x+.07f,.23f,0));facing.Align();
                }
                actor.Tick(1f/30);
                if(!clothReview&&t>=2.2f&&t<3.2f){if(!fx.Drawing)fx.BeginDraw(1,Vector3.right);fx.SetAge(t-2.2f);}
                if(!clothReview&&t>=3.2f&&!shot){fx.Release();shot=true;}if(!clothReview&&t>=3.25f&&!ended){fx.EndPose();ended=true;}
                fx.Tick(1f/30);var saved=RenderTexture.active;
                try{stage.Camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1024,768),0,0);image.Apply();File.WriteAllBytes(output+"/frame_"+frame.ToString("000")+".png",image.EncodeToPNG());}
                finally{RenderTexture.active=saved;}
            }
        }
        finally
        {
            if(source!=null){source.GetComponent<PNStarreaperPresentation>()?.Dispose();source.GetComponent<PNVisualBody>()?.Clear();UnityEngine.Object.DestroyImmediate(source);}
            if(stage!=null)stage.Camera.targetTexture=stage.Target;if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(image!=null)UnityEngine.Object.DestroyImmediate(image);
            stage?.Dispose();PNVisualRuntime.Release();PNVisualPacks.Clear();
        }
        Require(Resources.FindObjectsOfTypeAll<Material>().Count(x=>x.name=="Starreaper violet bow")==baseline,"bow materials released");checks++;
        var report=(clothReview?"Starreaper cloth: PASS ":"Starreaper bow: PASS ")+checks+"; native preview, not gameplay Play Mode.";File.WriteAllText(output+"/native.txt",report);return report;
    }
}
#endif
