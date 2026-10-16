#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class PNElementalBuffEditorChecks
{
    public static string Run()
        => Run(".utmp/BuffCoopReview/ElementalCaptures");
    public static string Run(string captureFolder)
    {
        if (Application.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Edit Mode required.");
        var original=SceneManager.GetActiveScene();bool dirty=original.isDirty;
        var docs=new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),Path.Combine(Application.persistentDataPath,"Mods"),Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);
        var preview=EditorSceneManager.NewPreviewScene();var evidence=new List<string>();int checks=0;
        RenderTexture texture=null;Texture2D output=null;Material bodyMaterial=null;VolumeProfile profile=null;var previous=RenderTexture.active;
        void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;evidence.Add("PASS "+message);}
        PNStat Entity(long id, Vector3 position)
        {
            var root=new GameObject("Elemental disposable "+id);root.SetActive(false);SceneManager.MoveGameObjectToScene(root,preview);root.transform.position=position;root.layer=31;
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id),EveId.Parse("project_n:dummy"),0);
            var capsule=root.AddComponent<CapsuleCollider>();capsule.radius=.18f;capsule.height=.9f;capsule.center=Vector3.up*.45f;
            root.AddComponent<EveTimeScale>();var stat=root.AddComponent<PNEnemyStat>();var loader=root.AddComponent<EveEntityLoader>();loader.Load(hub);root.SetActive(true);root.GetComponent<PNBuffManager>().Advance(0);return stat;
        }
        try
        {
            EveRegistrySession.Begin();EveRegistrySession.Start(hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,docs,"test:elemental"));
            var source=Entity(989001,new Vector3(100,0,0));var second=Entity(989002,new Vector3(100,0,1));
            source.SetSource(EveId.Parse("test:native_attack"),new[]{new EveStatModifier(PNContentIds.AttackPower,100,0)});
            second.SetSource(EveId.Parse("test:native_attack"),new[]{new EveStatModifier(PNContentIds.AttackPower,200,0)});
            var targets=new[]{Entity(989003,new Vector3(-1.8f,0,0)),Entity(989004,new Vector3(-.6f,0,0)),Entity(989005,new Vector3(.6f,0,0)),Entity(989006,new Vector3(1.8f,0,0))};
            var capture=PNBuffSource.Capture(source);var arrows=targets[0].GetComponent<PNBuffManager>().Buffs;
            foreach(var element in new[]{"fire","ice","lightning"})
            {
                var id=EveId.Parse("base:"+element+"_vulnerability");var tag=EveId.Parse("base:"+element);
                double baseline=targets[0].CalculateDamage(100,PNContentIds.Magic,tag);
                var receipt=arrows.Apply(new PNBuffApplyRequest(id,capture));
                Check(receipt.Status==PNBuffStatus.Applied && Math.Abs(targets[0].CalculateDamage(100,PNContentIds.Magic,tag)-baseline*1.75)<1e-8,"Native "+element+" receiver mitigation");
                Check(arrows.Apply(new PNBuffApplyRequest(id,PNBuffSource.Capture(second))).Handle.Equals(receipt.Handle),"Native target grouping "+element);
            }
            Check(targets[0].GetComponentsInChildren<PNBuffElementVisual>().Length==3,"One distinct arrow per vulnerability");
            var names=new[]{"burn","electrocution","frostbite"};var caps=new[]{35,10,5};var ratios=new[]{1.75,2.62,3.5};
            for(int i=0;i<3;i++)
            {
                var target=targets[i+1];var manager=target.GetComponent<PNBuffManager>();var buffs=manager.Buffs;
                var id=EveId.Parse("base:"+names[i]);var grant=buffs.Apply(new PNBuffApplyRequest(id,capture,JObject.Parse("{'stacks':"+caps[i]+"}")));
                PNDamageResult hit=null;Action<PNDamageResult> watch=r=>hit=r;target.DamageReceived+=watch;
                try{buffs.Trigger(grant.Handle);}finally{target.DamageReceived-=watch;}
                Check(hit!=null && Math.Abs(hit.Damage.Amount-source.Evaluate(PNContentIds.AttackPower)*ratios[i])<1e-8 && !hit.Damage.CanCrit,"Native cap damage "+names[i]);
                var other=buffs.Apply(new PNBuffApplyRequest(id,PNBuffSource.Capture(second)));
                Check(target.GetComponentsInChildren<PNBuffElementVisual>().Length==1 && target.GetComponentInChildren<PNBuffElementVisual>().GrantCount==2,"Multiple sources share DOT visual "+names[i]);
                buffs.Remove(other.Handle);Check(target.GetComponentInChildren<PNBuffElementVisual>().GrantCount==1,"Removing one source keeps DOT visual "+names[i]);
                var clock=target.GetComponent<EveTimeScale>();clock.AddOwnedModifier(EveId.Parse("test:slow"),EveTimeModifierKind.Scale,-.5f,target);
                double remaining=buffs.Get(grant.Handle).Remaining.Value;manager.Advance(.2);
                Check(Math.Abs(buffs.Get(grant.Handle).Remaining.Value-(remaining-.1))<1e-8,"Native Entity lifetime "+names[i]);clock.ClearModifiers();
            }
            // Neutral silhouette reference in an isolated scene; no authored character visuals edited.
            bodyMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit")){hideFlags=HideFlags.HideAndDontSave};bodyMaterial.SetColor("_BaseColor",new Color(.16f,.19f,.24f));
            foreach(var target in targets)
            {
                var body=GameObject.CreatePrimitive(PrimitiveType.Capsule);body.transform.SetParent(target.transform,false);body.transform.localPosition=Vector3.up*.45f;body.transform.localScale=new Vector3(.36f,.45f,.36f);body.layer=31;
                body.GetComponent<MeshRenderer>().sharedMaterial=bodyMaterial;UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            }
            var cameraRoot=new GameObject("Elemental preview camera");SceneManager.MoveGameObjectToScene(cameraRoot,preview);var camera=cameraRoot.AddComponent<Camera>();camera.enabled=false;camera.scene=preview;
            camera.orthographic=true;camera.orthographicSize=1.02f;camera.cullingMask=1<<31;camera.transform.position=new Vector3(0,.55f,-6);camera.transform.rotation=Quaternion.identity;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.028f,.038f,.055f);camera.allowHDR=true;
            var additional=cameraRoot.AddComponent<UniversalAdditionalCameraData>();additional.renderPostProcessing=true;additional.volumeLayerMask=1<<31;
            var volumeRoot=new GameObject("Elemental preview Bloom");SceneManager.MoveGameObjectToScene(volumeRoot,preview);volumeRoot.layer=31;
            var volume=volumeRoot.AddComponent<Volume>();volume.isGlobal=true;volume.priority=10000;profile=ScriptableObject.CreateInstance<VolumeProfile>();volume.sharedProfile=profile;
            var bloom=profile.Add<Bloom>(true);bloom.threshold.Override(1);bloom.intensity.Override(.3f);bloom.scatter.Override(.6f);
            texture=new RenderTexture(1600,650,24,RenderTextureFormat.ARGBHalf);texture.Create();camera.targetTexture=texture;
            output=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
            Directory.CreateDirectory(captureFolder);
            var buffShader=Shader.Find("Project_N/VFX/BuffElemental");Check(buffShader!=null && !ShaderUtil.ShaderHasError(buffShader),"Dedicated elemental shader compiles");
            for(int frame=0;frame<3;frame++)
            {
                foreach(var target in targets)foreach(var visual in target.GetComponentsInChildren<PNBuffElementVisual>())visual.AdvanceVisual(frame==0?.14f:.27f,camera);
                camera.Render();RenderTexture.active=texture;output.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);output.Apply();
                Check(output.GetPixels32().Count(p=>p.r>110 || p.g>110 || p.b>110)>100,"Actual URP Buff pixels frame "+frame);
                File.WriteAllBytes(Path.Combine(captureFolder,"Frame"+frame+".png"),output.EncodeToPNG());
            }
            var scaled=targets[1];var effect=scaled.GetComponentInChildren<PNBuffElementVisual>();
            effect.AdvanceVisual(0,camera);float originalWidth=effect.BodyWidth,originalHeight=effect.BodyHeight;
            float MeshWidth(PNBuffElementVisual v)
            {var positions=v.GetComponent<MeshFilter>().sharedMesh.vertices.Select(p=>Vector3.Dot(v.transform.TransformPoint(p),camera.transform.right)).ToArray();return positions.Max()-positions.Min();}
            float oldMeshWidth=MeshWidth(effect);scaled.transform.localScale=Vector3.one*2;effect.AdvanceVisual(0,camera);
            Check(Math.Abs(effect.BodyWidth-originalWidth*2)<1e-6 && Math.Abs(effect.BodyHeight-originalHeight*2)<1e-6,"VFX dimensions track current uniform collider scale");
            Check(Math.Abs(MeshWidth(effect)-oldMeshWidth*2)<1e-5,"World VFX geometry scales once, without parent double scaling");scaled.transform.localScale=Vector3.one;
            scaled.transform.localScale=new Vector3(-1,1,1);effect.AdvanceVisual(0,camera);
            Check(Math.Abs(MeshWidth(effect)-oldMeshWidth)<1e-5,"Mirrored entity scale preserves world effect size");
            scaled.transform.localScale=new Vector3(0,1,1);effect.AdvanceVisual(0,camera);Check(!effect.GetComponent<MeshRenderer>().enabled,"Singular parent scale hides safely");scaled.transform.localScale=Vector3.one;
            var collider=scaled.GetComponent<CapsuleCollider>();collider.height=1.4f;collider.radius=.24f;collider.center=new Vector3(.13f,.7f,.08f);effect.AdvanceVisual(0,camera);
            Check(Math.Abs(effect.BodyHeight-1.4f)<1e-6 && Math.Abs(effect.BodyWidth-.48f)<1e-6 && Vector3.Distance(effect.BodyCenter,scaled.transform.TransformPoint(collider.center))<1e-6,"VFX follows runtime collider center/height/radius edits");
            scaled.transform.localScale=new Vector3(2,1,.5f);effect.AdvanceVisual(0,camera);
            Check(Math.Abs(effect.BodyWidth-.96f)<1e-6 && Math.Abs(effect.BodyHeight-1.4f)<1e-6,"Capsule dimensions honor nonuniform radial scale");
            collider.enabled=false;effect.AdvanceVisual(0,camera);Check(!effect.GetComponent<MeshRenderer>().enabled,"Missing enabled body collider hides presentation without fake fixed size");
            var trigger=scaled.gameObject.AddComponent<SphereCollider>();trigger.isTrigger=true;trigger.radius=20;effect.AdvanceVisual(0,camera);
            Check(!effect.GetComponent<MeshRenderer>().enabled,"Trigger hitbox cannot become body sizing source");
            var box=scaled.gameObject.AddComponent<BoxCollider>();box.size=new Vector3(.4f,1,.6f);box.center=Vector3.up*.5f;scaled.transform.localScale=Vector3.one;effect.AdvanceVisual(0,camera);
            Check(effect.BodyCollider==box && Math.Abs(effect.BodyWidth-.4f)<1e-6 && Math.Abs(effect.BodyHeight-1)<1e-6,"Collider replacement resolves enabled Box shape");
            scaled.transform.rotation=Quaternion.Euler(0,45,0);effect.AdvanceVisual(0,camera);
            Check(Math.Abs(effect.BodyWidth-(.4f+.6f)*Mathf.Sqrt(.5f))<1e-5,"Rotated box support projects directly from collider axes");
            scaled.transform.rotation=Quaternion.identity;box.enabled=false;trigger.isTrigger=false;trigger.radius=.2f;effect.AdvanceVisual(0,camera);
            Check(effect.BodyCollider==trigger && Math.Abs(effect.BodyWidth-.4f)<1e-6 && Math.Abs(effect.BodyHeight-.4f)<1e-6,"Sphere sizing ignores unrelated visual mesh dimensions");
            trigger.enabled=false;collider.enabled=true;collider.height=.9f;collider.radius=.18f;collider.center=Vector3.up*.45f;
            effect.AdvanceVisual(0,camera);scaled.transform.localScale=Vector3.one*1.6f;
            foreach(var visual in scaled.GetComponentsInChildren<PNBuffElementVisual>())visual.AdvanceVisual(0,camera);
            targets[0].transform.localScale=Vector3.one*1.25f;
            foreach(var visual in targets[0].GetComponentsInChildren<PNBuffElementVisual>())visual.AdvanceVisual(0,camera);
            camera.Render();RenderTexture.active=texture;output.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);output.Apply();File.WriteAllBytes(Path.Combine(captureFolder,"Scaled.png"),output.EncodeToPNG());scaled.transform.localScale=Vector3.one;
            targets[0].transform.localScale=Vector3.one;
            var arrowClock=targets[0].GetComponent<EveTimeScale>();arrowClock.MinScale=.2f;arrowClock.AddOwnedModifier(EveId.Parse("test:freeze"),EveTimeModifierKind.Scale,-1,targets[0]);
            var first=targets[0].GetComponentInChildren<PNBuffElementVisual>();float before=first.VisualTime;targets[0].GetComponent<PNBuffManager>().Advance(1);first.AdvanceVisual(0,camera);
            Check(Math.Abs(first.VisualTime-before-.2)<1e-6,"Visual Entity clock honors minimum");arrowClock.ClearModifiers();
            arrowClock.MinScale=0;arrowClock.AddOwnedModifier(EveId.Parse("test:shader_freeze"),EveTimeModifierKind.Scale,-1,targets[0]);
            first.AdvanceVisual(0,camera);before=first.VisualTime;float shaderTime=first.GetComponent<MeshRenderer>().sharedMaterials[0].GetFloat("_BuffTime");targets[0].GetComponent<PNBuffManager>().Advance(1);first.AdvanceVisual(0,camera);
            Check(first.VisualTime==before && first.GetComponent<MeshRenderer>().sharedMaterials.All(m=>m.GetFloat("_BuffTime")==shaderTime),"Entity freeze holds explicit shader time for body and glow");arrowClock.ClearModifiers();
            arrows.Suspend(true);Check(targets[0].GetComponentsInChildren<PNBuffElementVisual>().Length==0,"Suspend cleans all vulnerability renderers");
            arrows.Suspend(false);Check(targets[0].GetComponentsInChildren<PNBuffElementVisual>().Length==3,"Resume reacquires arrows without reapply");
            foreach(var target in targets)
            {
                var owned=target.GetComponentsInChildren<PNBuffElementVisual>();
                var meshes=owned.Select(v=>v.GetComponent<MeshFilter>().sharedMesh).ToArray();
                var materials=owned.SelectMany(v=>v.GetComponent<MeshRenderer>().sharedMaterials).ToArray();
                target.GetComponent<EveEntityLoader>().Unload();
                Check(target.GetComponentsInChildren<PNBuffElementVisual>().Length==0,"Unload releases renderer "+target.name);
                Check(meshes.All(m=>m==null) && materials.All(m=>m==null),"Unload destroys owned meshes and materials "+target.name);
            }
        }
        finally
        {
            RenderTexture.active=previous;if(output!=null)UnityEngine.Object.DestroyImmediate(output);if(texture!=null){texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
            try{if(EveRegistrySession.State!=EveSessionState.Stopped)EveRegistrySession.End();}finally{EditorSceneManager.ClosePreviewScene(preview);}
            if(bodyMaterial!=null)UnityEngine.Object.DestroyImmediate(bodyMaterial);
            if(profile!=null){foreach(var component in profile.components)UnityEngine.Object.DestroyImmediate(component);UnityEngine.Object.DestroyImmediate(profile);}
        }
        Check(SceneManager.GetActiveScene()==original && original.isDirty==dirty,"Original scene and dirty state preserved");
        string summary="PASS "+checks+" elemental native/render checks";File.WriteAllLines(Path.Combine(captureFolder,"NativeResults.txt"),evidence.Concat(new[]{summary}));return summary;
    }
}
#endif

