#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using EveEngine;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

public static class PNLoricaRigPresentationChecks
{
    public static string Run(string output)
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stopped Editor required");
        Directory.CreateDirectory(output);int checks=0,frames=0;
        void Require(bool value,string message){if(!value)throw new Exception(message);checks++;}
        var original=SceneManager.GetActiveScene();bool dirty=original.isDirty;
        var oldMode=PNVisualPaperSettings.Mode;var oldInk=PNVisualPaperSettings.Ink;var oldLook=PNLoricaShatter.Look;
        var oldCamera=PNVisualStringSystem.CameraOverride;float oldTime=PNVisualStringSystem.FixedTime;
        int captures=PNVisualReflectionCapture.ActiveCount,effects=PNLoricaShatter.ActiveCount;
        var scene=EditorSceneManager.NewPreviewScene();GameObject cameraRoot=null,source=null;PNVisualBody body=null;RenderTexture rt=null;var trace=new StringBuilder();
        try
        {
            PNVisualSystem.Prune();PNVisualPaperSettings.Mode=PNPaperShadowMode.Off;PNLoricaShatter.Look=PNLoricaShatter.LookKind.Mirror;
            string root=Path.Combine(Application.streamingAssetsPath,"BaseContent");PNVisualPacks.Set("project_n",root);
            var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},new PNGameContentSource(root,".utmp/no-mods",Array.Empty<string>()).ReadDocuments().ToArray());
            cameraRoot=new GameObject("Lorica rig preview");SceneManager.MoveGameObjectToScene(cameraRoot,scene);var camera=cameraRoot.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.orthographic=true;camera.orthographicSize=.55f;camera.aspect=1.3f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.24f,.28f,.30f,1);camera.transform.position=new Vector3(0,.35f,-4);cameraRoot.AddComponent<UniversalAdditionalCameraData>().SetRenderer(1);
            rt=new RenderTexture(832,640,24);rt.Create();camera.targetTexture=rt;
            PNVisualStringSystem.CameraOverride=camera;PNVisualStringSystem.FixedTime=1;
            void Export(RenderTexture target,string file)
            {
                var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false,true);
                try{image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,file+".png"),image.EncodeToPNG());frames++;}
                finally{UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=previous;}
            }
            void Shot(string name){PNVisualStringSystem.Draw();camera.Render();Export(rt,name);}
            foreach(string name in new[]{"Starweaver","Starkeeper"})
            {
                source=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/"+name+"/Prefab/"+name+".prefab"));source.SetActive(false);SceneManager.MoveGameObjectToScene(source,scene);foreach(var script in source.GetComponents<MonoBehaviour>())script.enabled=false;source.SetActive(true);
                body=source.GetComponent<PNVisualBody>();body.Begin(hub.Characters.Get(EveId.Parse("project_n:"+name.ToLowerInvariant())),hub);var actor=body.Actor;
                var facing=actor.GetComponent<PNCameraFacingTarget>();facing.ViewCamera=camera;facing.Align();actor.ShowStrings();actor.Play("idle",true);actor.Tick(1.2f);
                // End the authored entrance before injecting a break; no real gameplay is claimed by this timing fixture.
                body.Feed(0);body.Feed(1);actor.Tick(1);body.Feed(1);
                foreach(bool left in new[]{false,true})
                {
                    actor.SetFacingLeft(left);actor.Play("idle",true);actor.Tick(1);facing.Align();string prefix=name+(left?"_left":"_right");
                    using(var capture=PNVisualReflectionCapture.Capture(actor,camera))
                    {
                        Require(capture!=null,"capture missing");Require(capture.Size.y<1 && capture.Size.y>.15f,"snapshot included strings/culling margin");Export(capture.Texture,prefix+"_source");
                        var previous=RenderTexture.active;RenderTexture.active=capture.Texture;var pixels=new Texture2D(capture.Texture.width,capture.Texture.height,TextureFormat.RGBA32,false,true);pixels.ReadPixels(new Rect(0,0,pixels.width,pixels.height),0,0);pixels.Apply();var data=pixels.GetPixels32();RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);
                        Require(data.Count(x=>x.a>240)>data.Length*.1,"reflection empty or wrong alpha");Require(data.Count(x=>x.a==0)>data.Length*.05,"snapshot has no transparency");
                    }
                    Require(PNVisualReflectionCapture.ActiveCount==captures,"capture leak");
                    PNLoricaShatter.PlayRig(scene,actor,null,camera);Require(PNLoricaShatter.ActiveCount==effects+1 && PNVisualReflectionCapture.ActiveCount==captures+1,"mirror snapshot ownership missing");
                    var effect=scene.GetRootGameObjects().Single(x=>x.name=="Lorica shatter");
                    foreach(float age in new[]{.04f,.13f,.32f})
                    {foreach(var particle in effect.GetComponentsInChildren<ParticleSystem>())particle.Simulate(age,true,true,false);Shot(prefix+"_shards_"+Mathf.RoundToInt(age*100));}
                    cameraRoot.GetComponent<UniversalAdditionalCameraData>().SetRenderer(0);Shot(prefix+"_renderer2d");cameraRoot.GetComponent<UniversalAdditionalCameraData>().SetRenderer(1);
                    UnityEngine.Object.DestroyImmediate(effect);Require(PNLoricaShatter.ActiveCount==effects && PNVisualReflectionCapture.ActiveCount==captures,"effect snapshot leaked");
                }
                bool present=false;double remaining=7;
                body.BreakTimingSource=()=>new PNBuffTimingView(present,1,remaining,false,1,PNBuffClock.Entity);
                body.Feed(0);present=true;body.Feed(0);actor.Tick(.7f);
                Require(body.LoricaBroken && actor.Animator.BaseState=="stun","break stun missing");
                foreach(double value in new[]{1.0,.8,.6,.4,.1,0.0})
                {
                    remaining=value;body.Feed(0);var state=actor.Strings.State(0);float phase=state.PhaseTime;actor.Tick(.05f);body.Feed(0);
                    if(value<=.8){Require(state.Phase==PNVisualStringPhase.Lowering,"strings did not lower before break end");Require(Math.Abs(state.PhaseTime-phase)<.0001,"driven lowering advanced twice");}
                    trace.AppendLine(name+" remaining="+value+" phase="+state.Phase+" progress="+state.PhaseTime);Shot(name+"_lower_"+Mathf.RoundToInt((float)value*100));
                }
                Require(body.LoricaBroken,"projected zero prematurely ended break");present=false;body.Feed(0);
                Require(!body.LoricaBroken && !body.PullingUp && actor.Strings.State(0).Phase==PNVisualStringPhase.Attached,"post-break extra lowering delay");Shot(name+"_end");
                // Short/early-end/reapply and frame-zero freeze.
                present=true;remaining=.2;body.Feed(0);remaining=.1;body.Feed(0);Require(actor.Strings.State(0).Phase==PNVisualStringPhase.Lowering,"short break lowering");remaining=3;body.Feed(0);Require(actor.Strings.State(0).Phase==PNVisualStringPhase.Hidden,"reapply did not retract lowering");present=false;body.Feed(0);Require(actor.Strings.State(0).Phase==PNVisualStringPhase.Attached,"early removal did not attach");
                body.BreakTimingSource=null;body.Clear();UnityEngine.Object.DestroyImmediate(source);body=null;source=null;
            }
            Require(!ShaderUtil.ShaderHasError(Resources.Load<Shader>("PNVisualActor")),"actor shader error");
            File.WriteAllText(Path.Combine(output,"timeline.txt"),trace.ToString());
        }
        finally
        {
            if(body!=null)body.Clear();if(source!=null)UnityEngine.Object.DestroyImmediate(source);
            foreach(var go in scene.GetRootGameObjects())if(go!=cameraRoot)UnityEngine.Object.DestroyImmediate(go);
            PNVisualStringSystem.Release();PNVisualStringSystem.CameraOverride=oldCamera;PNVisualStringSystem.FixedTime=oldTime;
            PNVisualPaperSettings.Mode=oldMode;PNVisualPaperSettings.Ink=oldInk;PNLoricaShatter.Look=oldLook;PNVisualRuntime.Release();PNVisualPacks.Clear();
            if(cameraRoot!=null)UnityEngine.Object.DestroyImmediate(cameraRoot);if(rt!=null){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}EditorSceneManager.ClosePreviewScene(scene);
        }
        Require(PNVisualReflectionCapture.ActiveCount==captures && PNLoricaShatter.ActiveCount==effects,"final resource counts differ");Require(original.isDirty==dirty,"original scene changed");
        return "PASS "+checks+" Lorica rig presentation assertions, "+frames+" renders; source RGBA, mirror ownership, both facings/renderers, lowering deadline/held time/early end/reapply. Scripted timing only; gameplay Lorica checked separately.";
    }
    public static string RunIntegration(string output)
    {
        if(Application.isPlaying || EveRegistrySession.State!=EveSessionState.Stopped)throw new InvalidOperationException("Stopped, closed session required");
        Directory.CreateDirectory(output);int checks=0;void Require(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
        var scene=EditorSceneManager.NewPreviewScene();GameObject source=null,cg=null;PNVisualBody body=null;RenderTexture target=null;bool started=false;
        var oldCamera=PNVisualStringSystem.CameraOverride;var oldMode=PNVisualPaperSettings.Mode;var oldInk=PNVisualPaperSettings.Ink;
        int baseline=PNLoricaShatter.ActiveCount,captures=PNVisualReflectionCapture.ActiveCount;
        try
        {
            string root=Path.Combine(Application.streamingAssetsPath,"BaseContent");PNVisualPacks.Set("project_n",root);var docs=new PNGameContentSource(root,".utmp/no-mods",Array.Empty<string>()).ReadDocuments().ToArray();
            var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);
            EveRegistrySession.Begin();EveRegistrySession.Start(hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,docs,"test:lorica_rig"));started=true;
            cg=new GameObject("Lorica integration camera");SceneManager.MoveGameObjectToScene(cg,scene);var camera=cg.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.orthographic=true;camera.orthographicSize=.55f;camera.aspect=1.3f;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.24f,.28f,.30f);camera.transform.position=new Vector3(0,.35f,-4);cg.AddComponent<UniversalAdditionalCameraData>().SetRenderer(1);
            target=new RenderTexture(832,640,24);target.Create();camera.targetTexture=target;PNVisualStringSystem.CameraOverride=camera;
            void Shot(string name)
            {PNVisualStringSystem.Draw();camera.Render();var previous=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(832,640,TextureFormat.RGBA32,false);try{image.ReadPixels(new Rect(0,0,832,640),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());}finally{UnityEngine.Object.DestroyImmediate(image);RenderTexture.active=previous;}}
            long id=999120;
            foreach(string name in new[]{"starweaver","starkeeper"})
            {
                source=new GameObject("Lorica integration "+name);source.SetActive(false);SceneManager.MoveGameObjectToScene(source,scene);
                source.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id++),EveId.Parse("project_n:"+name),0);var stat=source.AddComponent<PNEnemyStat>();var clock=source.AddComponent<EveTimeScale>();var capsule=source.AddComponent<CapsuleCollider>();capsule.height=.4f;capsule.radius=.11f;capsule.center=Vector3.up*.2f;body=source.AddComponent<PNVisualBody>();var loader=source.AddComponent<EveEntityLoader>();loader.Load(hub);source.SetActive(true);
                var manager=source.GetComponent<PNBuffManager>();manager.Advance(0);body.Begin(hub.Characters.Get(EveId.Parse("project_n:"+name)),hub);var actor=body.Actor;var facing=actor.GetComponent<PNCameraFacingTarget>();facing.ViewCamera=camera;facing.Align();actor.ShowStrings();actor.Tick(1);body.Feed(0);body.Feed(1);body.Feed(1);
                var presentation=source.GetComponent<PNLoricaPresentation>();presentation.Present(camera);Shot(name+"_before");
                for(int n=0;n<30 && stat.LoricaCurrent>0 && !stat.IsDead;n++)
                {
                    stat.ReceiveDamage(new PNDamage(stat.SessionId,new EveEntityId(1),100,new[]{PNContentIds.Physical}).WithIFrameDuration(0));presentation.Present(camera);
                }
                Require(!stat.IsDead && stat.LoricaCurrent==0,"real hits did not break "+name);
                Require(PNLoricaShatter.ActiveCount==baseline+1 && PNVisualReflectionCapture.ActiveCount==captures+1,"real break did not create one rig mirror");presentation.Present(camera);Require(PNLoricaShatter.ActiveCount==baseline+1,"duplicate mirror emission");
                body.Feed(0);actor.Tick(.7f);Require(body.LoricaBroken && actor.Animator.BaseState=="stun","real Buff not connected to rig");
                var controls=source.GetComponent<PNControlManager>().State;Require(!controls.IsAllowed(PNControlIds.FaceControl)&&!controls.IsAllowed(PNControlIds.FaceSkill),"face permission not denied");
                var effect=scene.GetRootGameObjects().Single(x=>x.name=="Lorica shatter");foreach(var ps in effect.GetComponentsInChildren<ParticleSystem>())ps.Simulate(.08f,true,true,false);Shot(name+"_broken");
                clock.AddTimedModifier(EveId.Parse("test:lorica_freeze"),EveTimeModifierKind.Scale,-1f,60);manager.TryReadPresentationTiming(PNContentIds.LoricaBreak,out var timing);double before=timing.Remaining.Value;manager.Advance(2);body.Feed(0);manager.TryReadPresentationTiming(PNContentIds.LoricaBreak,out timing);Require(Math.Abs(before-timing.Remaining.Value)<.0001,"timing progressed in time stop");clock.ClearModifiers();
                foreach(var ps in effect.GetComponentsInChildren<ParticleSystem>())ps.Simulate(4,true,true,false);
                manager.Advance(before-.4);body.Feed(0);Require(actor.Strings.State(0).Phase==PNVisualStringPhase.Lowering && Math.Abs(actor.Strings.State(0).PhaseTime-.5f)<.002,"real remaining did not drive half-lowered strings");Shot(name+"_lowering");
                manager.Advance(.401);body.Feed(0);Require(!body.LoricaBroken && !body.PullingUp && actor.Strings.State(0).Phase==PNVisualStringPhase.Attached,"real break end did not attach immediately");Require(controls.IsAllowed(PNControlIds.FaceControl)&&controls.IsAllowed(PNControlIds.FaceSkill),"face locks retained after break");Shot(name+"_end");
                UnityEngine.Object.DestroyImmediate(effect);Require(PNLoricaShatter.ActiveCount==baseline && PNVisualReflectionCapture.ActiveCount==captures,"mirror destroy resource leak");body.Clear();loader.Unload();UnityEngine.Object.DestroyImmediate(source);source=null;body=null;
            }
        }
        finally
        {
            if(body!=null)body.Clear();if(source!=null){source.GetComponent<EveEntityLoader>()?.Unload();UnityEngine.Object.DestroyImmediate(source);}foreach(var go in scene.GetRootGameObjects())if(go!=cg)UnityEngine.Object.DestroyImmediate(go);
            if(started)EveRegistrySession.End();PNVisualStringSystem.Release();PNVisualStringSystem.CameraOverride=oldCamera;PNVisualPaperSettings.Mode=oldMode;PNVisualPaperSettings.Ink=oldInk;PNVisualRuntime.Release();PNVisualPacks.Clear();if(cg!=null)UnityEngine.Object.DestroyImmediate(cg);if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}EditorSceneManager.ClosePreviewScene(scene);
        }
        Require(PNLoricaShatter.ActiveCount==baseline && PNVisualReflectionCapture.ActiveCount==captures,"integration cleanup leak");return "PASS "+checks+" actual hit -> Buff -> common presenter -> rig mirror/string checks, both shipped characters; time stop, face permissions, end, cleanup. Native Editor components, no AI/Player run.";
    }

}
#endif
