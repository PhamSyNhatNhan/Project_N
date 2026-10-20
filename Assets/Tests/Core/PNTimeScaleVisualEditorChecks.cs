#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;

public static class PNTimeScaleVisualEditorChecks
{
    public static string Run()
    {
        if (Application.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Edit required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var originalFloor = original.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<ErModeGroundVisual>()).First();
        var originalMaterial = originalFloor.GetComponent<SpriteRenderer>().sharedMaterial;
        var mainCamera = original.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>()).First(x => x.name == "Main Camera");
        var docs = new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        int baselineDustMaterials = Resources.FindObjectsOfTypeAll<Material>().Count(x=>x.name=="Owned timescale dust material");
        int baselineFloorMeshes = Resources.FindObjectsOfTypeAll<UnityEngine.Mesh>().Count(x=>x.name=="Owned floor sprite depth mesh");
        var scene = EditorSceneManager.NewPreviewScene(); var evidence = new List<string>();
        PNAreaManager manager = null; RenderTexture rt = null; Texture2D image = null; var previous = RenderTexture.active;
        Directory.CreateDirectory(".utmp/TimeScaleTwistReview");
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); evidence.Add("PASS " + label); }
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:time_vfx"));
            var managerRoot = new GameObject("Time visual fixture manager"); SceneManager.MoveGameObjectToScene(managerRoot, scene); manager = managerRoot.AddComponent<PNAreaManager>(); manager.Initialize(EveRegistrySession.Context);
            var floor = new GameObject("Owned floor sprite preview"); SceneManager.MoveGameObjectToScene(floor, scene); floor.layer = 31; floor.transform.SetPositionAndRotation(originalFloor.transform.position, originalFloor.transform.rotation); floor.transform.localScale = originalFloor.transform.lossyScale; floor.AddComponent<SpriteRenderer>().sharedMaterial = originalMaterial; floor.AddComponent<ErModeGroundVisual>();
            var renderer = floor.GetComponent<SpriteRenderer>();
            var proxy = floor.GetComponentsInChildren<MeshRenderer>().First(x => x.name == "Owned floor depth geometry");
            proxy.gameObject.layer = 31; var depthMaterial = proxy.sharedMaterial; var depthMesh = proxy.GetComponent<MeshFilter>().sharedMesh;
            Check(renderer.sharedMaterial == originalMaterial && depthMaterial.renderQueue == 2000 && depthMaterial.FindPass("VFXFloorDepth") >= 0 && depthMaterial.FindPass("DepthOnly") < 0, "Floor has VFX-only pass and cannot write main scene depth");
            var sourceRoot = new GameObject("Visual fixture source"); sourceRoot.SetActive(false); SceneManager.MoveGameObjectToScene(sourceRoot, scene); sourceRoot.transform.position = Vector3.one * 30;
            sourceRoot.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(992001), EveId.Parse("project_n:dummy"), 0); var source = sourceRoot.AddComponent<PNEnemyStat>(); sourceRoot.AddComponent<EveEntityLoader>().Load(hub); sourceRoot.SetActive(true);
            var cameraRoot = new GameObject("Actual camera pose visual preview"); SceneManager.MoveGameObjectToScene(cameraRoot, scene); var camera = cameraRoot.AddComponent<Camera>(); camera.fieldOfView = mainCamera.fieldOfView; camera.nearClipPlane = mainCamera.nearClipPlane; camera.farClipPlane = mainCamera.farClipPlane; camera.enabled = false; camera.scene = scene; camera.cullingMask = 1 << 31;
            camera.transform.SetPositionAndRotation(mainCamera.transform.position, mainCamera.transform.rotation); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.04f,.05f,.06f);
            cameraRoot.AddComponent<UniversalAdditionalCameraData>(); rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGBHalf); rt.Create(); camera.targetTexture = rt; image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            Color32[] Capture(string name)
            { camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply(); File.WriteAllBytes(".utmp/TimeScaleTwistReview/" + name + ".png", image.EncodeToPNG()); return image.GetPixels32(); }
            proxy.enabled = false; var reference = Capture("TransparentFloorReference"); proxy.enabled = true;
            var floorOnly = Capture("FloorOnly");
            var dummy = original.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<DummyStat>(true)).First();
            var dummyVisual = dummy.transform.Find("Visual").GetComponent<SpriteRenderer>(); var capsule=dummy.GetComponent<CapsuleCollider>();
            var portraitRoot=new GameObject("Dummy physical-rest portrait fixture"); SceneManager.MoveGameObjectToScene(portraitRoot,scene);
            portraitRoot.transform.position=new Vector3(dummy.transform.position.x, floor.transform.position.y+capsule.height*.5f-capsule.center.y, dummy.transform.position.z);
            var portrait=new GameObject("Visual at unchanged billboard offset"); portrait.transform.SetParent(portraitRoot.transform,false); portrait.layer=31;
            portrait.transform.localPosition=dummyVisual.transform.localPosition; portrait.transform.localRotation=dummyVisual.transform.localRotation; portrait.transform.localScale=dummyVisual.transform.localScale;
            var portraitRenderer=portrait.AddComponent<SpriteRenderer>(); portraitRenderer.sprite=dummyVisual.sprite; portraitRenderer.sharedMaterial=dummyVisual.sharedMaterial; portraitRenderer.color=dummyVisual.color;
            var bottom=dummyVisual.sprite.vertices.Select(v=>portrait.transform.TransformPoint(new Vector3(v.x,v.y,0))).Min(v=>v.y);
            Check(bottom<floor.transform.position.y,"Fixture reproduces Dummy billboard extending below floor at physical rest");
            var baseline = Capture("FloorBaseline"); Check(baseline.Count(x => x.g > 15) > 100000, "Actual sprite grid renders with original camera pose");
            Check(baseline.Zip(floorOnly,(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)).Count(x=>x>5)>50,"Full resting portrait renders with original material despite below-floor portion");
            portraitRoot.SetActive(false); var floorComparison=Capture("FloorDepthDoesNotChangeColor"); portraitRoot.SetActive(true);
            Check(floorComparison.Zip(reference, (a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)).Max() <= 3, "Adding floor depth preserves original sprite colors and grid");
            // Eve is spawned from its prefab at run time and the scene Dummy (with its shadow) is inactive, so no shadow sprite exists in the authored
            // scene. Enable a copy of the scene Dummy's ErModeCharacterShadow ([ExecuteAlways]: it builds its sprite on enable) in the preview scene to get
            // the same transparent shadow sprite, shared material, colour, rotation and scale.
            var shadowCopy = UnityEngine.Object.Instantiate(original.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<SpriteRenderer>(true)).First(x=>x.name=="Dummy Ground Shadow").gameObject);
            shadowCopy.transform.SetParent(null); SceneManager.MoveGameObjectToScene(shadowCopy, scene); shadowCopy.SetActive(true);
            var originalShadow = shadowCopy.GetComponent<SpriteRenderer>(); Check(originalShadow.sprite != null, "Shadow fixture builds its own sprite when enabled");
            var shadowRoot = new GameObject("Owned shadow above depth floor"); SceneManager.MoveGameObjectToScene(shadowRoot,scene); shadowRoot.layer=31;
            shadowRoot.transform.SetPositionAndRotation(floor.transform.position+Vector3.up*.02f, originalShadow.transform.rotation); shadowRoot.transform.localScale=originalShadow.transform.lossyScale;
            var shadow=shadowRoot.AddComponent<SpriteRenderer>(); shadow.sprite=originalShadow.sprite; shadow.sharedMaterial=originalShadow.sharedMaterial; shadow.color=originalShadow.color; shadow.sortingOrder=originalShadow.sortingOrder;
            var shadowFrame=Capture("ShadowAboveFloor");
            Check(shadowFrame.Zip(baseline,(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)).Count(x=>x>0)>20, "Existing transparent ground shadow remains visible above floor depth");
            Check(originalShadow.sharedMaterial.shader.name=="Universal Render Pipeline/2D/Sprite-Unlit-Default" && originalShadow.sharedMaterial.GetFloat("_ZWrite")==0, "Ground shadow keeps shared transparent non-depth material");
            UnityEngine.Object.DestroyImmediate(shadowRoot);
            PNTimeScaleArea Spawn(Vector3 point, float radius = 2.5f)
            {
                var input = new JObject { ["radius"] = radius, ["reduction"] = .9, ["minimumReduction"] = .9, ["duration"] = 15, ["dependent"] = false, ["layerMask"] = 0, ["layerMode"] = "Include" };
                var receipt = manager.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"), input, PNBuffSource.Capture(source), scene, point));
                Check(receipt.Status == PNAreaStatus.Created, "Pooled visual region created");
                var region = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNTimeScaleArea>(true)).First(x => x.Handle.InstanceId == receipt.Handle.InstanceId && x.IsRunning);
                region.gameObject.layer = 31; return region;
            }
            var below = Spawn(new Vector3(0, -5, 0)); manager.Advance(1); var hidden = Capture("BelowFloor");
            Check(hidden.Zip(baseline, (a,b) => Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)).Max() <= 3, "Fully buried sphere and dust are occluded by actual floor depth");
            proxy.enabled=false; var noVfxDepth=Capture("BelowFloorWithoutPrivateDepth"); proxy.enabled=true;
            Check(noVfxDepth.Zip(hidden,(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)).Count(x=>x>0)>100,"Private depth actively removes buried VFX while main portrait stays intact");
            below.Handle.End(); manager.Advance(.31f);
            var area = Spawn(new Vector3(0,.14f,0));
            var birthRenderer=area.GetComponentInChildren<ParticleSystemRenderer>(); birthRenderer.enabled=false;
            manager.Advance(.04f); Capture("BirthEarly"); manager.Advance(.06f); var birthMid=Capture("BirthMid");
            Check(area.VisualRadius>0 && area.VisualRadius<area.Radius && area.Radius==2.5f,"Opening changes visual radius without changing gameplay radius");
            Check(Mathf.Abs(birthRenderer.transform.localScale.x-area.VisualRadius/area.Radius)<.0001f,"Particle volume follows opening radius");
            manager.Advance(0); var birthPaused=Capture("BirthPaused");
            Check(birthMid.Zip(birthPaused,(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)).Max()<=3,"Opening volume holds when Game delta is zero");
            manager.Advance(.08f); var birthLate=Capture("BirthLate");
            Check(birthMid.Zip(birthLate,(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)).Count(x=>x>8)>100,"Opening volume expands while opacity fade is already complete");
            manager.Advance(.2f); Capture("BirthFinished"); birthRenderer.enabled=true;
            manager.Advance(.62f); var tinted = Capture("RegionAndDust");
            Check(area.DustCount > 0 && area.DustCount <= 20, "Real ParticleSystem emits bounded purple dust on Game delta");
            Check(tinted.Zip(baseline, (a,b) => a.g+1 < b.g).Count(x=>x) > 10000, "Light regional tint is visible while original floor colors remain readable");
            Check(tinted.Zip(baseline,(a,b)=>b.g>20 && a.g>=b.g*.5f).Count(x=>x)>100000,"Moderate tint preserves green-channel detail instead of wiping it out");
            var systemCheck=area.GetComponentInChildren<ParticleSystem>();
            Check(systemCheck.main.startSize3D && systemCheck.main.startSizeY.constant>systemCheck.main.startSizeX.constantMax && systemCheck.main.startSpeed.constant<=.08f && systemCheck.colorOverLifetime.enabled,"Glow1 particles are narrow slow streaks with lifetime fade");
            var dustRenderer = area.GetComponentInChildren<ParticleSystemRenderer>(); dustRenderer.enabled=false;
            camera.transform.position=mainCamera.transform.position*1.7f;
            var boundaryFirst=Capture("BoundaryWideA");
            Check(Mathf.Abs(area.VisualRadius-area.Radius)<.0001f,"Full visual radius matches gameplay radius after opening");
            manager.Advance(2.5f); var boundarySecond=Capture("BoundaryWideB");
            Check(boundaryFirst.Zip(boundarySecond,(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)).Count(x=>x>1)>30,"Boundary slowly refracts and pulses on Game time with particles hidden");
            camera.transform.SetPositionAndRotation(mainCamera.transform.position,mainCamera.transform.rotation); dustRenderer.enabled=true;
            var dustObject = area.GetComponentInChildren<ParticleSystem>().gameObject; float particleTime = area.DustTime; manager.Advance(0);
            Check(area.DustTime == particleTime, "Zero Game delta preserves particle time");
            var second = Spawn(new Vector3(0,.14f,0)); manager.Advance(2); Capture("Overlap");
            var systems = new[]{area.GetComponentInChildren<ParticleSystem>(), second.GetComponentInChildren<ParticleSystem>()};
            int alive = 0; foreach(var system in systems) { var particles = new ParticleSystem.Particle[20]; int count=system.GetParticles(particles); alive += particles.Take(count).Count(p=>p.remainingLifetime>0); }
            Check(alive <= 20, "Coincident regions suppress duplicate particle occupancy");
            second.transform.position = Vector3.right * 2; manager.Advance(.2f); Capture("Intersecting");
            camera.transform.position = new Vector3(0,.7f,-.4f); camera.transform.rotation = Quaternion.Euler(20,0,0); Capture("CameraInside");
            camera.transform.SetPositionAndRotation(mainCamera.transform.position, mainCamera.transform.rotation);
            float fullRadius=area.VisualRadius; area.Handle.End(); second.Handle.End(); manager.Advance(.15f); Capture("FadeOut"); Check(area.VisualRadius>0 && area.VisualRadius<fullRadius,"Retiring volume contracts toward center"); manager.Advance(0); float pausedRadius=area.VisualRadius; manager.Advance(0); Check(area.VisualRadius==pausedRadius,"Zero Game delta freezes contraction"); manager.Advance(.2f);
            Check(PNTimeScaleAreaRendering.RegisteredCount == 0 && manager.BorrowedCount == 0, "Visual retirement unsubscribes and returns pooled particles");
            var reused = Spawn(Vector3.zero); Check(reused.GetComponentInChildren<ParticleSystem>().gameObject == dustObject && reused.DustCount == 0 && reused.DustTime == 0, "Particle object reused and reset rather than recreated");
            reused.Handle.End(); manager.Advance(.31f);
            foreach(string path in new[]{"PNVfx/GroundDepth","PNVfx/TimeScaleArea","PNVfx/TimeScaleDust"}) Check(!ShaderUtil.ShaderHasError(Resources.Load<Shader>(path)), "Native shader compiled " + path);
            var visual = floor.GetComponent<ErModeGroundVisual>(); visual.enabled = false;
            Check(depthMaterial == null && depthMesh == null && renderer.sharedMaterial == originalMaterial, "Floor disable releases owned depth mesh/material and preserves sprite material");
            Check(originalFloor.GetComponent<SpriteRenderer>().sharedMaterial == originalMaterial && original.isDirty == dirty, "Authored floor/shared material and scene state preserved");

        }
        finally
        {
            manager?.Shutdown(); EveRegistrySession.End(); RenderTexture.active = previous;
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); } if (image != null) UnityEngine.Object.DestroyImmediate(image);
            EditorSceneManager.ClosePreviewScene(scene); File.WriteAllLines(".utmp/TimeScaleTwistReview/NativeResults.txt", evidence);
        }
        Check(Resources.FindObjectsOfTypeAll<Material>().Count(x=>x.name=="Owned timescale dust material")==baselineDustMaterials, "Explicit pool destruction releases all retained dust materials in Edit Mode");
        Check(Resources.FindObjectsOfTypeAll<UnityEngine.Mesh>().Count(x=>x.name=="Owned floor sprite depth mesh")==baselineFloorMeshes, "Floor fixture depth mesh count returns to baseline");
        File.WriteAllLines(".utmp/TimeScaleTwistReview/NativeResults.txt", evidence);
        return "PASS " + evidence.Count + " native floor depth and timescale VFX checks";
    }
}
#endif
