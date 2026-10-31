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

public static class PNStarkeeperSweepVfxChecks
{
    public static void Batch()
    {
        string output = Environment.GetEnvironmentVariable("PN_STARKKEEPER_VFX_OUTPUT") ?? Path.GetFullPath(".utmp/StarkeeperSlashV2");
        Directory.CreateDirectory(output);
        try { File.WriteAllText(Path.Combine(output, "native.txt"), Run(output)); EditorApplication.Exit(0); }
        catch (Exception e) { File.WriteAllText(Path.Combine(output, "native.txt"), e.ToString()); Debug.LogException(e); EditorApplication.Exit(1); }
    }
    public static string Run(string output)
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stopped disposable Editor required.");
        Directory.CreateDirectory(output); int baseline = Resources.FindObjectsOfTypeAll<Material>().Count(x => x.name.StartsWith("Starkeeper sweep "));
        int meshBaseline = Resources.FindObjectsOfTypeAll<UnityEngine.Mesh>().Count(x => x.name.StartsWith("Starkeeper sweep "));
        var ink = PNVisualPaperSettings.Ink; var mode = PNVisualPaperSettings.Mode; bool bake = PNVisualPaperSettings.BakeInk;
        var scene = EditorSceneManager.NewPreviewScene(); GameObject source = null, cameraRoot = null; RenderTexture target = null;
        PNStarkeeperSweepVisual fx = null; PNVisualBody body = null; PNStarkeeperTelegraph warning = null;
        try
        {
            string root = Path.Combine(Application.streamingAssetsPath, "BaseContent"); PNVisualPacks.Set("project_n", root);
            var docs = new PNGameContentSource(root, ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
            var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
            cameraRoot = new GameObject("Sweep VFX preview camera"); SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            var camera = cameraRoot.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene; camera.orthographic = true;
            camera.aspect = 1.5f; camera.orthographicSize = 1.65f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.30f,.36f,.32f,1);
            camera.transform.position = new Vector3(0, 3, -4); camera.transform.LookAt(new Vector3(0,.18f,.4f));
            var data = cameraRoot.AddComponent<UniversalAdditionalCameraData>(); data.SetRenderer(1);
            target = new RenderTexture(960,640,24); target.Create(); camera.targetTexture = target;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starkeeper/Prefab/Starkeeper.prefab");
            source = UnityEngine.Object.Instantiate(prefab); source.SetActive(false); SceneManager.MoveGameObjectToScene(source, scene);
            foreach (var b in source.GetComponents<MonoBehaviour>()) b.enabled = false;
            source.SetActive(true); body = source.GetComponent<PNVisualBody>(); body.Begin(hub.Characters.Get(EveId.Parse("project_n:starkeeper")), hub);
            var actor = body.Actor; actor.HideStrings(); actor.Play("star_sweep", true); actor.Tick(.01f);
            var facing = actor.GetComponent<PNCameraFacingTarget>(); facing.ViewCamera = camera; facing.Align();
            var weapon = source.GetComponent<PNStarkeeperPresentation>(); weapon.BeginSweep(.4f,.35f,Vector3.forward);
            fx = PNStarkeeperSweepVisual.Create(source.transform,weapon,.4f,.35f,1.4f,100,Vector3.forward,camera);
            var slashRenderers = fx.GetComponentsInChildren<MeshRenderer>().Where(x => x.gameObject.name.StartsWith("Retained slash ")).ToArray();
            if (slashRenderers.Length != 10) throw new Exception("Expected five layers per sweep.");
            foreach (var renderer in slashRenderers)
            {
                var material = renderer.sharedMaterial;
                if (material.GetTexture("_SlashFlowTex") == null || material.GetTexture("_SlashGrainTex") == null) throw new Exception("Missing slash texture layers.");
                float layer = material.GetFloat("_SlashLayer");
                if (material.GetFloat("_DstBlend") != (layer == 2 || layer == 3 ? 1 : 10)) throw new Exception("Wrong alpha/additive layer blend.");
            }
            warning = PNStarkeeperTelegraph.Sector(Vector3.zero,Vector3.forward,1.4f,100,scene,camera);
            var warningRoot=scene.GetRootGameObjects().Single(x=>x.name=="Starkeeper sweep");
            var nodeTransform=warningRoot.transform.Find("Constellation stars");
            var nodeMesh=nodeTransform.GetComponent<MeshFilter>().sharedMesh;
            if(nodeMesh.vertexCount!=20 || nodeTransform.GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_Mode")!=15) throw new Exception("Expected five round light nodes, not flat star stamps.");
            var points=nodeMesh.vertices;
            var normal=nodeTransform.TransformDirection(Vector3.Cross(points[1]-points[0],points[2]-points[0]).normalized);
            if(Mathf.Abs(Vector3.Dot(normal,camera.transform.forward))<.99f) throw new Exception("Warning lights are not facing the camera.");
            var warningScreen=points.Select(v=>camera.WorldToViewportPoint(nodeTransform.TransformPoint(v)).x).ToArray();
            float warningMin=warningScreen.Min(),warningMax=warningScreen.Max();
            int frame = 0; float previousAge = 0; Vector3[] retained = null;
            foreach (float age in new[] { .1f,.30f,.39f,.40f,.42f,.46f,.50f,.53f,.60f,.66f,.70f,.75f,.77f,.81f,.85f,.88f,.95f,1.01f,1.12f })
            {
                actor.Tick(age - previousAge); previousAge = age; facing.Align(); weapon.SetAge(age); weapon.TickWeapon(0);
                warning.SetPhase(age/.4f); if(age>=.4f)warning.Hide();
                fx.SetTime(age);
                if (age == .4f) { fx.Emit(0,Vector3.zero,Vector3.forward); fx.Hit(new Vector3(.15f,0,.8f)); }
                if (age == .75f) { weapon.SetAim(Quaternion.AngleAxis(35,Vector3.up)*Vector3.forward); weapon.TickWeapon(0); fx.Emit(1,Vector3.zero,Quaternion.AngleAxis(35,Vector3.up)*Vector3.forward); fx.Hit(new Vector3(.55f,0,.65f)); }
                fx.Align(); Shot((frame++).ToString("D2"));
                var slash = fx.transform.Find("Retained slash 1").GetComponent<MeshFilter>().sharedMesh;
                if (age == .53f)
                {
                    retained = slash.vertices;
                    var outerScreen=retained.Where((v,index)=>index%2==1).Select(v=>camera.WorldToViewportPoint(v).x).ToArray();
                    if(warningMin<outerScreen.Min()-.015f || warningMax>outerScreen.Max()+.015f) throw new Exception("Warning extends visibly beyond the slash span.");
                    for(int i=1;i<retained.Length;i+=2)
                    {
                        var planar=retained[i]; planar.y=0;
                        if(Mathf.Abs(planar.magnitude-1.4f)>.001f || Vector3.Angle(Vector3.forward,planar)>50.01f) throw new Exception("Slash extends outside registered 1.4m/100deg sector.");
                    }
                    var glowMaterial=fx.transform.Find("Spear purple cast glow").GetComponent<MeshRenderer>().sharedMaterial;
                    if(glowMaterial.GetFloat("_Opacity")<=0 || glowMaterial.GetColor("_Tint").b<=glowMaterial.GetColor("_Tint").g) throw new Exception("No purple weapon glow during sweep.");
                }
                if (age == .60f && (retained == null || !retained.SequenceEqual(slash.vertices))) throw new Exception("Slash mark moved after the spear's swing ended.");
                if(age==.53f || age==.88f)
                {
                    int stroke=age==.53f?0:1;
                    var direction=stroke==0?Vector3.forward:Quaternion.AngleAxis(35,Vector3.up)*Vector3.forward;
                    foreach(var renderer in slashRenderers.Skip(stroke*5).Take(5))
                        for(int vertexIndex=1;vertexIndex<renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount;vertexIndex+=2)
                        {
                            var planar=renderer.GetComponent<MeshFilter>().sharedMesh.vertices[vertexIndex];planar.y=0;
                            if(planar.magnitude>1.401f || Vector3.Angle(direction,planar)>50.01f) throw new Exception("Slash outer edge exceeds its damage sector, including reverse/reaim.");
                        }
                }
                if(age==.46f || age==.81f)
                {
                    int stroke=age==.46f?0:1;
                    var vertices=slashRenderers[stroke*5+1].GetComponent<MeshFilter>().sharedMesh.vertices;
                    var shaft=source.transform.Find("Star spear").position;
                    for(int i=0;i<vertices.Length;i+=2)
                        if(Vector3.Distance(vertices[i],shaft)>.041f) throw new Exception("Slash wake is detached from the spear shaft during swing.");
                }
                var blade = slash.vertices;
                var before = fx.transform.Find("Gold star particles").GetComponent<MeshFilter>().sharedMesh.vertices;
                fx.SetTime(age); fx.SetTime(age);
                if (!before.SequenceEqual(fx.transform.Find("Gold star particles").GetComponent<MeshFilter>().sharedMesh.vertices)) throw new Exception("Particles moved at held Action age.");
                if (!blade.SequenceEqual(slash.vertices)) throw new Exception("Slash changed at held Action age.");
            }
            data.SetRenderer(0); fx.SetTime(.85f); Shot("19_renderer2d");
            warning.Dispose();
            warning=PNStarkeeperTelegraph.Sector(Vector3.zero,Vector3.forward,1.4f,100,scene,camera);
            warning.SetPhase(1); fx.SetTime(1.12f); Shot("20_warning_renderer2d");
            warning.Dispose(); warning = null;
            fx.Dispose(); fx = null;
            fx = PNStarkeeperSweepVisual.Create(source.transform,weapon,.4f,.35f,1.4f,100,Vector3.forward,camera); fx.SetTime(.2f); fx.Dispose(); fx = null;
            body.Clear(); body = null; UnityEngine.Object.DestroyImmediate(source); source = null;
            if (Resources.FindObjectsOfTypeAll<Material>().Count(x => x.name.StartsWith("Starkeeper sweep ")) != baseline) throw new Exception("Sweep VFX materials leaked.");
            if (Resources.FindObjectsOfTypeAll<UnityEngine.Mesh>().Count(x => x.name.StartsWith("Starkeeper sweep ")) != meshBaseline) throw new Exception("Sweep VFX meshes leaked.");
            return "PASS five textured layers per sweep; alpha/additive contracts; mesh cleanup; real Starkeeper puppet/spear; two planar slash marks match registered 1.4m/100deg sector; camera-facing luminous warning core/halo without bloom; screen-span; both-renderer warning renders; shaft attachment; purple weapon glow; 35deg reaim/contact particles; held-age freeze; retained geometry after swing; both renderers; completion/early-dispose cleanup. 21 actual rendered frames.\n" + PNStarkeeperEditorChecks.Run() + "\n" + PNStarkeeperEditorChecks.RunCombat();
            void Shot(string name)
            {
                camera.Render(); var previous = RenderTexture.active; RenderTexture.active = target; var image = new Texture2D(960,640,TextureFormat.RGBA32,false);
                try { image.ReadPixels(new Rect(0,0,960,640),0,0); image.Apply();
                    if(name=="02")
                    {
                        var vertices=nodeMesh.vertices;
                        var centre=nodeTransform.TransformPoint((vertices[8]+vertices[9]+vertices[10]+vertices[11])*.25f);
                        var corePoint=camera.WorldToScreenPoint(centre);
                        var haloPoint=camera.WorldToScreenPoint(centre+camera.transform.right*.045f);
                        var core=image.GetPixel(Mathf.RoundToInt(corePoint.x),Mathf.RoundToInt(corePoint.y));
                        var halo=image.GetPixel(Mathf.RoundToInt(haloPoint.x),Mathf.RoundToInt(haloPoint.y));
                        var background=image.GetPixel(10,10);
                        if(core.r<.85f || core.g<.6f || halo.r-background.r<.03f) throw new Exception("Warning core/halo is not visibly luminous without camera bloom.");
                    }
                    File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG()); }
                finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
            }
        }
        finally
        {
            warning?.Dispose(); fx?.Dispose(); body?.Clear(); if(source!=null)UnityEngine.Object.DestroyImmediate(source);
            if(cameraRoot!=null)UnityEngine.Object.DestroyImmediate(cameraRoot); if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}
            EditorSceneManager.ClosePreviewScene(scene); PNVisualRuntime.Release(); PNVisualPacks.Clear();
            PNVisualPaperSettings.Ink=ink; PNVisualPaperSettings.Mode=mode; PNVisualPaperSettings.BakeInk=bake;
        }
    }
}
#endif
