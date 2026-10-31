#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class PNStarkeeperPointVfxChecks
{
    public static string Run(string output)
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stopped preview required.");
        Directory.CreateDirectory(output);
        int materialBaseline=Resources.FindObjectsOfTypeAll<Material>().Count(x=>x.name.StartsWith("Starkeeper point "));
        int meshBaseline=Resources.FindObjectsOfTypeAll<UnityEngine.Mesh>().Count(x=>x.name.StartsWith("Starkeeper point "));
        var ink=PNVisualPaperSettings.Ink;var mode=PNVisualPaperSettings.Mode;bool bake=PNVisualPaperSettings.BakeInk;
        var scene=EditorSceneManager.NewPreviewScene();GameObject source=null,cameraRoot=null;RenderTexture target=null;PNVisualBody body=null;PNStarkeeperPointVisual fx=null;
        try
        {
            string root=Path.Combine(Application.streamingAssetsPath,"BaseContent");PNVisualPacks.Set("project_n",root);
            var docs=new PNGameContentSource(root,".utmp/no-mods",Array.Empty<string>()).ReadDocuments().ToArray();
            var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);
            cameraRoot=new GameObject("Point VFX preview camera");SceneManager.MoveGameObjectToScene(cameraRoot,scene);
            var camera=cameraRoot.AddComponent<Camera>();camera.enabled=false;camera.scene=scene;camera.orthographic=true;camera.aspect=1.5f;camera.orthographicSize=1.35f;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.30f,.36f,.32f,1);camera.transform.position=new Vector3(.2f,3,-4);camera.transform.LookAt(new Vector3(.2f,.2f,.2f));
            var data=cameraRoot.AddComponent<UniversalAdditionalCameraData>();data.SetRenderer(1);target=new RenderTexture(960,640,24);target.Create();camera.targetTexture=target;
            source=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starkeeper/Prefab/Starkeeper.prefab"));source.SetActive(false);SceneManager.MoveGameObjectToScene(source,scene);
            foreach(var b in source.GetComponents<MonoBehaviour>())b.enabled=false;source.SetActive(true);
            var origin=new Vector3(-.8f,0,0);var anchor=new Vector3(1,0,.4f);source.transform.position=origin;
            body=source.GetComponent<PNVisualBody>();body.Begin(hub.Characters.Get(EveId.Parse("project_n:starkeeper")),hub);var actor=body.Actor;actor.HideStrings();actor.Play("star_point",true);
            var facing=actor.GetComponent<PNCameraFacingTarget>();facing.ViewCamera=camera;facing.Align();
            var weapon=source.GetComponent<PNStarkeeperPresentation>();var spear=source.transform.Find("Star spear");var sharedMesh=spear.GetComponent<MeshFilter>().sharedMesh;
            var aim=(anchor-origin).normalized;weapon.BeginPoint(2,.25f,aim);weapon.TickWeapon(0);
            fx=PNStarkeeperPointVisual.Create(source.transform,weapon,anchor,.35f,2,camera);
            foreach(var name in new[]{"Starkeeper body trail","Starkeeper body energy trail"})
            {
                var material=fx.transform.Find(name).GetComponent<MeshRenderer>().sharedMaterial;
                if(material.GetTexture("_MainTex")!=Resources.Load<Texture2D>("StarkeeperVfx/DashTrailMask") || material.GetTexture("_SlashFlowTex")==null)
                    throw new Exception("Body trail missing authored mask or flow texture.");
            }
            if(fx.GetComponentsInChildren<Collider>().Length!=0)throw new Exception("Presentation must not create damage colliders.");
            float previous=0;int frame=0;var timeline=new System.Collections.Generic.List<string>();
            var samples=Enumerable.Range(0,41).Select(i=>(float)Math.Round(i*.05,3))
                .Concat(Enumerable.Range(1,12).Select(i=>(float)Math.Round(2+i*.02,3)))
                .Concat(Enumerable.Range(1,19).Select(i=>(float)Math.Round(2.25+i*.05,3)))
                .Concat(new[]{.35f,.9f,1.55f,1.9f,2.05f,2.10f,2.16f,2.25f,2.28f,2.33f,2.43f,2.65f,2.9f,3.2f}).Distinct().OrderBy(x=>x);
            foreach(float age in samples)
            {
                source.transform.position=Vector3.Lerp(origin,anchor,Mathf.Clamp01((age-2)/.25f));actor.Tick(age-previous);previous=age;actor.transform.position=source.transform.position;facing.Align();weapon.SetAge(age);weapon.TickWeapon(0);
                fx.SetTime(age,age>=2 && age<2.25f);if(age==2.25f)fx.Finish(true);fx.Align();var file=(frame++).ToString("D2");Shot(file);timeline.Add(file+"\t"+age.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var particles=fx.transform.Find("Gold energy particles").GetComponent<MeshFilter>().sharedMesh;var before=particles.vertices;fx.SetTime(age,age>=2 && age<2.25f);fx.SetTime(age,age>=2 && age<2.25f);
                if(!before.SequenceEqual(particles.vertices))throw new Exception("Point particles moved at held age.");
                var bodyMesh=fx.transform.Find("Starkeeper body trail").GetComponent<MeshFilter>().sharedMesh;var bodyBefore=bodyMesh.vertices;
                fx.SetTime(age,age>=2 && age<2.25f);fx.Align();
                if(!bodyBefore.SequenceEqual(bodyMesh.vertices))throw new Exception("Body trail advances at held age.");
                if(age==2.10f)
                {
                    if(bodyMesh.colors.All(x=>x.a<=0) || fx.transform.Find("Starkeeper body energy trail").GetComponent<MeshFilter>().sharedMesh.colors.All(x=>x.a<=0))throw new Exception("Missing layered body trail during lunge.");
                    var bodyHead=(bodyMesh.vertices[0]+bodyMesh.vertices[1])*.5f;
                    if(Vector3.Distance(bodyHead,actor.transform.TransformPoint(new Vector3(0,actor.CapsuleHeight*.5f,0)))>.005f)throw new Exception("Body trail is not attached to torso/cloak.");
                    if(Vector3.Distance(bodyMesh.vertices[2],bodyMesh.vertices[3])<actor.CapsuleHeight*1.1f)throw new Exception("Body trail is thinner than the character height.");
                    var sheath=fx.transform.Find("Tip pressure sheath");var core=fx.transform.Find("Tip white violet core");
                    if(sheath.GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_Opacity")<=0 || core.GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_Opacity")<=0)throw new Exception("Missing spear-tip force layer.");
                    var expectedTip=fx.transform.Find("Spectral spear").TransformPoint(new Vector3(0,.28f,0));
                    if(Vector3.Distance(sheath.position,expectedTip)>.001f)throw new Exception("Force layer detached from spear tip.");
                }
                if(age==2.9f)
                {
                    if(bodyMesh.colors.Any(x=>x.a>0))throw new Exception("Body trail did not fade after stop.");
                    if(fx.transform.Find("Tip pressure sheath").GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_Opacity")>0)throw new Exception("Tip force lingers after stop.");
                }
                if(age<2 && bodyMesh.colors.Any(x=>x.a>0))throw new Exception("Body trail appears before dash.");
                if(fx.GetComponentsInChildren<MeshRenderer>().Any(x=>x.name.StartsWith("Starkeeper afterimage")))throw new Exception("Removed body afterimages still exist.");
                if(fx.transform.Find("Upright target star")!=null || fx.transform.Find("Forward violet energy")!=null)throw new Exception("Old star/cone remains.");
                if(age>=.35f && age<2)
                {
                    var planted=fx.transform.Find("Thrown gold spear");
                    if(Vector3.Distance(planted.TransformPoint(new Vector3(0,.28f,0)),anchor+Vector3.up*.015f)>.005f)throw new Exception("Gold spear is not planted at fixed destination.");
                    if(spear.GetComponent<MeshRenderer>().enabled)throw new Exception("Original spear duplicates planted gold proxy.");
                }
                if(age==1.9f)
                {
                    var spectral=fx.transform.Find("Spectral spear");var material=spectral.GetComponent<MeshRenderer>().sharedMaterial;
                    if(material.GetFloat("_Mode")!=20 || material.GetFloat("_Reveal")<.9f || material.GetFloat("_Opacity")<.5f)throw new Exception("Spectral spear did not form.");
                    if(particles.colors.Count(x=>x.a>.05f)<25)throw new Exception("Missing energy gathering.");
                }
                if(age==2.10f && fx.TrailSampleCount<3)throw new Exception("Missing recorded motion trail.");
                if(age==2.65f && (!spear.GetComponent<MeshRenderer>().enabled || fx.transform.Find("Thrown gold spear").gameObject.activeSelf))throw new Exception("Original spear not restored after recall.");
            }
            File.WriteAllLines(Path.Combine(output,"timeline.tsv"),timeline);
            fx.Dispose();fx=null;
            data.SetRenderer(0);source.transform.position=origin;actor.transform.position=origin;weapon.BeginPoint(2,.25f,aim);weapon.SetAge(1.9f);weapon.TickWeapon(0);
            fx=PNStarkeeperPointVisual.Create(source.transform,weapon,anchor,.35f,2,camera);fx.SetTime(1.9f,false);Shot("charge_renderer2d");
            source.transform.position=Vector3.Lerp(origin,anchor,.4f);actor.transform.position=source.transform.position;weapon.SetAge(2.1f);weapon.TickWeapon(0);fx.SetTime(2.1f,true);Shot("thrust_renderer2d");
            source.transform.position=Vector3.Lerp(origin,anchor,.65f);actor.transform.position=source.transform.position;weapon.SetAge(2.14f);weapon.TickWeapon(0);fx.SetTime(2.14f,true);Shot("body_trail_renderer2d");
            if(fx.transform.Find("Starkeeper body energy trail").GetComponent<MeshFilter>().sharedMesh.colors.All(x=>x.a<=0))throw new Exception("No body trail on renderer2D.");
            fx.Finish(false);fx.SetTime(2.2f,false);Shot("blocked_fade");
            if(fx.Reached || fx.transform.Find("Target star fragments")!=null)throw new Exception("Blocked motion must recall without burst.");
            fx.SetTime(2.45f,false);if(!spear.GetComponent<MeshRenderer>().enabled || fx.transform.Find("Thrown gold spear").gameObject.activeSelf)throw new Exception("Blocked spear did not return.");fx.Dispose();fx=null;
            source.transform.position=origin;actor.transform.position=origin;weapon.BeginPoint(2,.25f,aim);weapon.SetAge(1.9f);weapon.TickWeapon(0);
            fx=PNStarkeeperPointVisual.Create(source.transform,weapon,anchor,.35f,2,camera);fx.SetTime(1.9f,false);
            var bends=new[]{new Vector3(.20f,0,.10f),new Vector3(.42f,0,.32f),new Vector3(.75f,0,.13f)};
            for(int i=0;i<bends.Length;i++)
            {
                float t=2+i*.04f;source.transform.position=origin+bends[i];actor.transform.position=source.transform.position;weapon.SetAge(t);weapon.TickWeapon(0);fx.SetTime(t,true);
            }
            Shot("curved_trail");
            var ribbon=fx.transform.Find("Spectral motion trail").GetComponent<MeshFilter>().sharedMesh.vertices;
            var head=(ribbon[0]+ribbon[1])*.5f;var tail=(ribbon[ribbon.Length-2]+ribbon[ribbon.Length-1])*.5f;float bend=0;
            for(int i=0;i<ribbon.Length;i+=2){var point=(ribbon[i]+ribbon[i+1])*.5f;bend=Mathf.Max(bend,Vector3.Cross(point-head,(tail-head).normalized).magnitude);}
            if(bend<.008f)throw new Exception("Motion trail ignores sampled curved path.");fx.Dispose();fx=null;
            fx=PNStarkeeperPointVisual.Create(source.transform,weapon,anchor,.35f,2,camera);fx.SetTime(.4f,false);fx.CancelFade();
            var held=fx.transform.Find("Gold energy particles").GetComponent<MeshFilter>().sharedMesh.vertices;
            fx.AdvanceCancelFade(0);
            if(!held.SequenceEqual(fx.transform.Find("Gold energy particles").GetComponent<MeshFilter>().sharedMesh.vertices))throw new Exception("Cancel fade moved at held owner time.");
            fx.AdvanceCancelFade(.1f);Shot("cancel_fade");
            if(fx.Reached)throw new Exception("Cancel fade must not burst.");
            fx.AdvanceCancelFade(.23f);if(fx!=null)throw new Exception("Cancel fade did not release its resources.");fx=null;
            spear.GetComponent<MeshRenderer>().enabled=false;
            fx=PNStarkeeperPointVisual.Create(source.transform,weapon,anchor,.35f,2,camera);fx.Dispose();fx=null;
            if(spear.GetComponent<MeshRenderer>().enabled)throw new Exception("Original hidden renderer state was not preserved.");
            spear.GetComponent<MeshRenderer>().enabled=true;
            if(!spear.GetComponent<MeshRenderer>().enabled)throw new Exception("Original spear renderer not restored.");
            if(actor.Mesh==null || actor.Mesh.vertexCount==0 || actor.Renderer.sharedMaterial.GetTexture("_MainTex")==null)throw new Exception("Borrowed actor mesh/atlas was destroyed by VFX.");
            body.Clear();body=null;UnityEngine.Object.DestroyImmediate(source);source=null;
            if(!AssetDatabase.Contains(sharedMesh) || sharedMesh.vertexCount==0)throw new Exception("Borrowed spear mesh destroyed.");
            if(Resources.FindObjectsOfTypeAll<Material>().Count(x=>x.name.StartsWith("Starkeeper point "))!=materialBaseline || Resources.FindObjectsOfTypeAll<UnityEngine.Mesh>().Count(x=>x.name.StartsWith("Starkeeper point "))!=meshBaseline)throw new Exception("Point VFX resources leaked.");
            return "PASS "+(frame+6)+" rendered frames: gold spear throw/fixed planted tip/recall; spectral spear formation; gold gathering; actual curved history trail; layered body trail/no character afterimages/fade; tip pressure sheath/core; renderer state restoration; completed/blocked/cancel recall; held-age freeze; no VFX colliders; both renderers; complete/cancel/owner-clock fade resource cleanup; borrowed spear mesh preserved. Scripted presentation path only, not AI/navigation/wall physics.";
            void Shot(string name)
            {
                camera.Render();var old=RenderTexture.active;RenderTexture.active=target;var image=new Texture2D(960,640,TextureFormat.RGBA32,false);
                try{image.ReadPixels(new Rect(0,0,960,640),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());}
                finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
            }
        }
        finally
        {
            fx?.Dispose();body?.Clear();if(source!=null)UnityEngine.Object.DestroyImmediate(source);if(cameraRoot!=null)UnityEngine.Object.DestroyImmediate(cameraRoot);if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
            EditorSceneManager.ClosePreviewScene(scene);PNVisualRuntime.Release();PNVisualPacks.Clear();PNVisualPaperSettings.Ink=ink;PNVisualPaperSettings.Mode=mode;PNVisualPaperSettings.BakeInk=bake;
        }
    }
}
#endif
