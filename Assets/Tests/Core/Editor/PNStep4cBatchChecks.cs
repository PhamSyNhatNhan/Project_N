#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Step 4.2 lot B (B2): popup transform write parity against an independent copy of the pre-B2 Render, the native popup suite,
// the render capture and the Player build. Writes only under .utmp/Step4c.
public static class PNStep4cBatchChecks
{
    public static void Run()
    {
        bool skipChecks = Environment.GetCommandLineArgs().Contains("-pn-step4c-skip-checks");
        string folder = "F:/unity/Project_N/.utmp/Step4c/" + (skipChecks ? "Native-Baseline" : "Native");
        Directory.CreateDirectory(folder);
        string host = null;
        try
        {
            var scene = SceneManager.GetActiveScene(); bool dirty = scene.isDirty;
            if (Application.isBatchMode && string.IsNullOrEmpty(scene.path) && !dirty)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Tests/Generated")) AssetDatabase.CreateFolder("Assets/Tests", "Generated");
                host = "Assets/Tests/Generated/Step4cBatch_" + Guid.NewGuid().ToString("N") + ".unity";
                if (!EditorSceneManager.SaveScene(scene, host)) throw new Exception("Cannot save owned empty batch scene.");
            }
            EveHotPathMetrics.Enabled = EveHotPathMetrics.CountOnly = false;
            if (!skipChecks)
            {
                File.WriteAllText(folder + "/Parity.txt", Parity());
                File.WriteAllText(folder + "/Native.txt", PNDamagePopupEditorChecks.Run());
                File.WriteAllText(folder + "/RenderAfter.txt", PNDamagePopupRenderChecks.Run());
                File.Copy("F:/unity/Project_N/.utmp/PNDamagePopupGlow.png", folder + "/RenderAfter.png", true);
                if (scene != SceneManager.GetActiveScene() || scene.isDirty != dirty) throw new Exception("Checks changed authored scene state.");
            }
            if (Environment.GetCommandLineArgs().Contains("-pn-step4c-build"))
            File.WriteAllText(folder + "/BatchResult.txt", "PASS requested checks and builds");
            if (host != null) AssetDatabase.DeleteAsset(host);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            File.WriteAllText(folder + "/BatchFailure.txt", e.ToString());
            if (host != null) AssetDatabase.DeleteAsset(host);
            if (Application.isBatchMode) EditorApplication.Exit(1); else throw;
        }
    }

    // Independent copy of the pre-B2 Render transform part (position, scale, then rotation).
    private static void OldRender(Transform target, Vector3 origin, Vector3 baseScale, PNDamagePopup p, bool critical, float progress, Camera camera)
    {
        target.position = origin + Vector3.up * (p.Rise * progress);
        target.localScale = baseScale * (critical ? p.CriticalScaleMultiplier : 1f) * Mathf.Max(0f, p.Scale.Evaluate(progress));
        if (camera != null) target.rotation = camera.transform.rotation;
    }

    private static string Parity()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-8f * (1f + a.sqrMagnitude);
        bool NearQ(Quaternion a, Quaternion b) => Mathf.Abs(Quaternion.Dot(a, b)) > 1f - 1e-6f;
        var prefab = Resources.Load<GameObject>("PNUI/PNDamagePopup");
        if (prefab == null) throw new Exception("Resources prefab PNUI/PNDamagePopup not found.");
        var rng = new System.Random(20261014);
        var owned = new System.Collections.Generic.List<GameObject>();
        try
        {
            GameObject Make(string name, Vector3 position, Quaternion rotation, Vector3 scale)
            {
                var go = new GameObject(name); go.transform.SetPositionAndRotation(position, rotation); go.transform.localScale = scale; owned.Add(go); return go;
            }
            Quaternion RandomRotation() => Quaternion.Euler(rng.Next(360), rng.Next(360), rng.Next(360));
            Camera MakeCamera(Quaternion rotation)
            {
                var go = new GameObject("step4c camera"); owned.Add(go); go.transform.rotation = rotation; return go.AddComponent<Camera>();
            }
            var parents = new[]
            {
                (position: Vector3.zero, rotation: Quaternion.identity, scale: Vector3.one),
                (position: new Vector3(3, -2, 5), rotation: Quaternion.Euler(20, 70, 140), scale: new Vector3(1.5f, .7f, 2f)),
                (position: new Vector3(-8, 4, 1), rotation: Quaternion.Euler(90, 10, 30), scale: new Vector3(.4f, 3f, 1f))
            };
            int cases = 0;
            foreach (var parentSpec in parents)
            foreach (bool critical in new[] { false, true })
            foreach (string cameraMode in new[] { "static", "rotating", "replaced", "destroyed", "null", "main" })
            {
                var parent = Make("parent", parentSpec.position, parentSpec.rotation, parentSpec.scale);
                var shadowParent = Make("shadow parent", parentSpec.position, parentSpec.rotation, parentSpec.scale);
                var root = UnityEngine.Object.Instantiate(prefab, parent.transform); owned.Add(root);
                var popup = root.GetComponent<PNDamagePopup>();
                var shadow = Make("shadow", Vector3.zero, Quaternion.identity, Vector3.one).transform;
                shadow.SetParent(shadowParent.transform, false);
                shadow.localPosition = root.transform.localPosition; shadow.localRotation = root.transform.localRotation; shadow.localScale = root.transform.localScale;
                Vector3 baseScale = root.transform.localScale;
                Camera current = cameraMode == "null" || cameraMode == "main" ? null : MakeCamera(RandomRotation());
                GameObject mainCamera = null;
                if (cameraMode == "main") { mainCamera = new GameObject("step4c main camera"); owned.Add(mainCamera); mainCamera.tag = "MainCamera"; mainCamera.AddComponent<Camera>(); mainCamera.transform.rotation = RandomRotation(); }
                for (int life = 0; life < 2; life++) // second pass reuses the popup
                {
                    var origin = new Vector3(rng.Next(-50, 50) / 7f, rng.Next(-50, 50) / 7f, rng.Next(-50, 50) / 7f);
                    popup.Begin(5d + life, origin, Color.white, null, critical, Array.Empty<EveId>());
                    for (int step = 0; step < 9; step++)
                    {
                        // external changes before Advance: both popup and shadow receive the same ones
                        if (step % 3 == 1)
                        {
                            var p = new Vector3(rng.Next(-9, 9), rng.Next(-9, 9), rng.Next(-9, 9)); var r = RandomRotation(); var s = new Vector3(.5f + rng.Next(5), .5f + rng.Next(5), .5f + rng.Next(5));
                            root.transform.position = p; root.transform.rotation = r; root.transform.localScale = s;
                            shadow.position = p; shadow.rotation = r; shadow.localScale = s;
                        }
                        if (cameraMode == "rotating") current.transform.rotation = RandomRotation();
                        if (cameraMode == "replaced" && step % 2 == 0) current = MakeCamera(RandomRotation());
                        if (cameraMode == "destroyed" && step == 4 && current != null) { UnityEngine.Object.DestroyImmediate(current.gameObject); }
                        if (cameraMode == "main") mainCamera.transform.rotation = RandomRotation();
                        float dt = .02f + rng.Next(4) * .01f;
                        Camera passed = cameraMode == "main" || cameraMode == "null" ? null : current;
                        popup.Advance(dt, passed);
                        Camera effective = passed != null ? passed : Camera.main;
                        OldRender(shadow, origin, baseScale, popup, critical, popup.Progress, effective);
                        Check(Near(root.transform.position, shadow.position), "Popup world position differs from the pre-B2 render: " + cameraMode);
                        Check(Near(root.transform.localScale, shadow.localScale), "Popup local scale differs from the pre-B2 render: " + cameraMode);
                        Check(NearQ(root.transform.rotation, shadow.rotation), "Popup world rotation differs from the pre-B2 render: " + cameraMode);
                        var block = new MaterialPropertyBlock(); root.GetComponent<MeshRenderer>().GetPropertyBlock(block);
                        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(popup.FadeStart, 1f, popup.Progress));
                        Check(Mathf.Abs(block.GetFloat("_PopupFade") - fade) < 1e-6f && Mathf.Abs(block.GetFloat("_EmissionIntensity") - (critical ? popup.CriticalGlowIntensity : popup.GlowIntensity)) < 1e-6f,
                            "Popup property block differs from the pre-B2 render: " + cameraMode);
                    }
                }
                if (mainCamera != null) { UnityEngine.Object.DestroyImmediate(mainCamera); mainCamera = null; }
                cases++;
            }
            return "PASS " + checks + " step4c transform/property-block parity checks over " + cases + " seeded cases (3 parents incl. rotated + non-uniform scale, crit on/off, camera static/rotating/replaced/destroyed/null/Camera.main, external transform edits, reuse).";
        }
        finally { foreach (var go in owned) if (go != null) UnityEngine.Object.DestroyImmediate(go); }
    }
}
#endif
