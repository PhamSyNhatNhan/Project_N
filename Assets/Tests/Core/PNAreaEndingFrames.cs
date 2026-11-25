#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// IL1a rendered evidence (Docs/InstanceLifecyclePlan.md section 6): a time-scale area while it runs and while it shrinks in its Ending, drawn by a camera in a preview scene to .utmp/AreaEndingIL/.
// It needs an idle Edit Mode and no authored scene content; it returns the visual radius at each frame so the shrink is a number as well as a picture.
public static class PNAreaEndingFrames
{
    public static string Run()
    {
        if (Application.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Edit required.");
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        var scene = EditorSceneManager.NewPreviewScene(); PNAreaManager manager = null; RenderTexture rt = null; Texture2D image = null; var previous = RenderTexture.active;
        Directory.CreateDirectory(".utmp/AreaEndingIL");
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:il_frames"));
            var mr = new GameObject("mgr"); SceneManager.MoveGameObjectToScene(mr, scene); manager = mr.AddComponent<PNAreaManager>(); manager.Initialize(EveRegistrySession.Context);
            var sr = new GameObject("src"); sr.SetActive(false); SceneManager.MoveGameObjectToScene(sr, scene); sr.transform.position = Vector3.one * 30;
            sr.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(992009), EveId.Parse("project_n:dummy"), 0); var source = sr.AddComponent<PNEnemyStat>(); sr.AddComponent<EveEntityLoader>().Load(hub); sr.SetActive(true);
            var cr = new GameObject("cam"); SceneManager.MoveGameObjectToScene(cr, scene); var camera = cr.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene; camera.cullingMask = -1; camera.fieldOfView = 40;
            camera.transform.SetPositionAndRotation(new Vector3(0, 7, -6), Quaternion.Euler(50, 0, 0)); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.1f, .12f, .14f);
            cr.AddComponent<UniversalAdditionalCameraData>(); rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGBHalf); rt.Create(); camera.targetTexture = rt; image = new Texture2D(960, 540, TextureFormat.RGB24, false);
            void Capture(string name) { camera.Render(); RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply(); File.WriteAllBytes(".utmp/AreaEndingIL/" + name + ".png", image.EncodeToPNG()); }
            var input = new JObject { ["radius"] = 2.5, ["reduction"] = .9, ["minimumReduction"] = .9, ["duration"] = 15, ["dependent"] = false, ["targetLayers"] = new JArray(), ["layerMode"] = "Include" };
            var receipt = manager.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"), input, PNCombatSource.Capture(source), scene, Vector3.zero));
            var area = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNTimeScaleArea>(true)).First(x => x.Handle.InstanceId == receipt.Handle.InstanceId && x.IsRunning);
            manager.Advance(1.2f); Capture("1_running"); float full = area.VisualRadius;
            area.Handle.End(); bool alive = area.Handle.IsAlive; manager.Advance(.08f); Capture("2_ending_early"); float early = area.VisualRadius; bool ending = area.IsEnding;
            manager.Advance(.1f); Capture("3_ending_late"); float late = area.VisualRadius;
            int borrowedDuring = manager.BorrowedCount; manager.Advance(.6f);
            return "radius full/early/late=" + full.ToString("F2") + "/" + early.ToString("F2") + "/" + late.ToString("F2") + " aliveAfterEnd=" + alive + " endingEarly=" + ending + " borrowedDuring=" + borrowedDuring + " borrowedAfter=" + manager.BorrowedCount + " registeredAfter=" + PNTimeScaleAreaRendering.RegisteredCount;
        }
        finally { manager?.Shutdown(); EveRegistrySession.End(); RenderTexture.active = previous; if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); } if (image != null) UnityEngine.Object.DestroyImmediate(image); EditorSceneManager.ClosePreviewScene(scene); }
    }
}
#endif
