#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class EveVfxEditorChecks
{
    public static string RenderPreview()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var scene = EditorSceneManager.NewPreviewScene(); RenderTexture target = null;
        Texture2D output = null; VolumeProfile profile = null;
        var previous = RenderTexture.active;
        string path = Path.GetFullPath(".utmp/EveSweepSequence.png"); int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        try
        {
            var shader = Shader.Find("Project_N/VFX/EveEnergy");
            Check(shader != null && !ShaderUtil.ShaderHasError(shader), "New Eve energy shader failed compilation.");
            var bladeShader=Shader.Find("Project_N/VFX/EveBlade");
            Check(bladeShader!=null && !ShaderUtil.ShaderHasError(bladeShader),"Faceted blade shader failed compilation.");
            var doc = JObject.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "BaseContent/characters/EveCharacter.json")));
            var entries = (JArray)doc["characters"][0]["skill"];
            var names = new[] { "attack_right", "attack_left", "ultimate" };
            var weaponSettings=EveWeaponSettings.Parse(entries.OfType<JObject>().Single(x=>(string)x["id"]=="project_n:eve_moveset")["data"]["skill"]["tuning"]["weapon"]);
            var times = new[] { .16f, .32f, .48f, .66f };
            for (int row = 0; row < names.Length; row++)
            {
                var data = entries.OfType<JObject>().Single(x => (string)x["id"] == "project_n:eve_" + names[row]);
                var settings = EveSweepSettings.Parse(data["data"]["action"]["perform"]);
                for (int column = 0; column < times.Length; column++)
                {
                    var owner = new GameObject("Sweep sequence " + names[row] + " " + times[column]);
                    SceneManager.MoveGameObjectToScene(owner, scene);
                    owner.transform.position = new Vector3(-10.5f + column * 7, 0, 6 - row * 6);
                    var weapon=EveWeaponRig.Create(owner.transform,weaponSettings);
                    Check(weapon.GetComponentsInChildren<SpriteRenderer>().Length==0 && weapon.BladeMesh.bounds.size.z>0,
                        "Energy blade is still a flat sprite.");
                    var visual = EveSweepVisual.Create(owner.transform, EveHitboxPose.Planar(owner.transform.position, Vector3.forward), settings);
                    float headStart = visual.transform.InverseTransformPoint(visual.HeadPosition).z;
                    for (float t = .02f; t < times[column]; t += .02f) visual.Render(t, settings.Duration * .02f);
                    visual.Render(times[column], settings.Duration * .02f);
                    var filter = visual.GetComponent<MeshFilter>(); var vertices = filter.sharedMesh.vertices;
                    Check(weapon.BladeRenderer.transform.localScale==Vector3.one && weapon.BladeMesh.bounds.min.x<0,
                        "Attack resized the physical sword or removed its light root.");
                    Check(Vector3.Distance(weapon.EnergyBlade.TransformPoint(Vector3.right),weapon.BladeTip)<.001f,
                        "Extended energy edge does not reach the slash tip.");
                    if (row==0 && column==0) Check((vertices[vertices.Length-4]-vertices[vertices.Length-2]).magnitude>settings.Radius*.8f,
                        "Slash still covers only the sword tip.");
                    Check(vertices.Length >= 8 && visual.Progress == times[column], "Moving ribbon timeline failed.");
                    Check(Math.Abs((vertices[vertices.Length-3]-vertices[vertices.Length-1]).magnitude-settings.TrailDepth)<.0001f, "Slash trail has no real cross-section depth.");
                    Check(visual.GetComponentInChildren<PNArcSlashVisual>() == null, "New VFX reused legacy slash.");
                    Check(visual.GetComponentInChildren<TrailRenderer>().positionCount > 0, "Leading edge trail has no swept path.");
                    if (column >= 2) Check(visual.LiveParticles > 0, "Sweep did not emit real ParticleSystem sparks.");
                    // The ribbon spans only recent motion; its oldest vertex is never the starting blade at mid-sweep.
                    if (column == 2 && times[column]>settings.SweepStart+settings.TrailFraction)
                    {
                        float oldest = visual.transform.InverseTransformPoint(visual.transform.TransformPoint(vertices[1])).z;
                        Check(Math.Abs(oldest - headStart) > .05f, "Ribbon still displays full static crescent.");
                    }
                    foreach (var child in owner.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
                }
            }
            var energySettings = EveEnergySettings.Parse(entries.OfType<JObject>().Single(x => (string)x["id"] == "project_n:eve_moveset")["data"]["skill"]["tuning"]["energy"]);
            var effectsRoot = new GameObject("Phase particle QA"); SceneManager.MoveGameObjectToScene(effectsRoot, scene);
            var energy = EveEnergyVisual.Create(effectsRoot.transform, energySettings);
            energy.SetBurst(true); energy.Flash(); energy.Charge(.8f); energy.Advance(.16f);
            Check(energy.GetComponentsInChildren<ParticleSystem>().Count(x => x.particleCount > 0) == 3, "Burst/charge/pulse particles missing: " + string.Join(",", energy.GetComponentsInChildren<ParticleSystem>().Select(x => x.name + "=" + x.particleCount)));
            energy.SetMotion(true); energy.CancelCast();
            Check(!energy.GetComponentInChildren<TrailRenderer>().emitting, "Cancel retained emitting movement trail.");
            energy.SetBurst(false); UnityEngine.Object.DestroyImmediate(effectsRoot);
            var volumeRoot = new GameObject("Sweep preview Bloom"); SceneManager.MoveGameObjectToScene(volumeRoot, scene); volumeRoot.layer = 31;
            var volume = volumeRoot.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 10000;
            profile = ScriptableObject.CreateInstance<VolumeProfile>(); volume.sharedProfile = profile;
            var bloom = profile.Add<Bloom>(true); bloom.threshold.Override(1f); bloom.intensity.Override(.35f); bloom.scatter.Override(.6f);
            var mapping = profile.Add<Tonemapping>(true); mapping.mode.Override(TonemappingMode.ACES);
            var cameraRoot = new GameObject("Sweep preview camera"); SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            var camera = cameraRoot.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene;
            camera.orthographic = true; camera.orthographicSize = 11; camera.cullingMask = 1 << 31;
            camera.transform.position = new Vector3(0, 22, -14); camera.transform.LookAt(Vector3.zero);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.018f, .024f, .04f); camera.allowHDR = true;
            var additional = cameraRoot.AddComponent<UniversalAdditionalCameraData>(); additional.renderPostProcessing = true;
            additional.volumeLayerMask = 1 << 31; additional.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            target = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGBHalf); target.Create(); camera.targetTexture = target;
            camera.Render(); RenderTexture.active = target;
            output = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            output.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); output.Apply();
            int bright = 0; foreach (var pixel in output.GetPixels32()) if (pixel.r > 80 && pixel.r > pixel.b * 1.2f) bright++;
            Check(bright > 300, "New sweep did not render golden energy.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, output.EncodeToPNG());
            return "PASS " + checks + " moving sweep/trail/ParticleSystem/phase checks + actual URP render; golden pixels=" + bright + "; preview=" + path;
        }
        finally
        {
            RenderTexture.active = previous;
            if (output != null) UnityEngine.Object.DestroyImmediate(output);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(scene);
            if (profile != null) { foreach (var component in profile.components) UnityEngine.Object.DestroyImmediate(component); UnityEngine.Object.DestroyImmediate(profile); }
            if (original.isDirty != dirty) throw new Exception("VFX preview changed active scene dirty state.");
        }
    }
}
#endif
