#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Golden of the real mesh for the Visual editor tool (Docs/VisualEditorGuide.md 3.2): plays the scenarios of ArtSource/VisualDemo/editor/tests/golden/scenarios.json on the real PNVisualActor
//     (every visual document of BaseContent, a disposable preview scene) and writes Mesh.vertices at the frames a scenario lists in `meshFrames`. The browser tool's core/mesh.js must give the same
//     positions. Run through Unity_RunCommand, ONE check per command; never inside a batch with the Preload / ShadePreload / Bake checks.
// VI: Golden cua mesh that cho tool soan Visual: chay kich ban tren PNVisualActor that va ghi dinh cua mesh o cac khung can so.
public static partial class PNVisualEditorChecks
{
    public static string ToolGolden(string scenariosPath, string outputPath)
    {
        if (UnityEditor.EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        PNVisualSystem.Prune();
        var input = JObject.Parse(File.ReadAllText(scenariosPath)); var baseDirectory = Path.GetFullPath((string)input["base"]);
        var documents = Directory.GetFiles(Path.Combine(baseDirectory, "visual"), "*.json").OrderBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.Ordinal)
            .Select(f => new EveContentDocument(Path.GetFileNameWithoutExtension(f), File.ReadAllText(f), "project_n")).ToList();
        PNVisualPacks.Clear(); PNVisualPacks.Set("project_n", baseDirectory);
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, documents);
        Stage stage = null; var output = new JObject(); int frames = 0, scenarios = 0;
        try
        {
            stage = new Stage(); stage.Camera.orthographicSize = 1.5f; stage.Camera.transform.position = new Vector3(0f, .6f, -5f);
            foreach (JObject sc in (JArray)input["scenarios"])
            {
                var meshFrames = new HashSet<int>(((JArray)sc["meshFrames"] ?? new JArray()).Select(t => (int)t)); if (meshFrames.Count == 0) continue;
                var root = new GameObject("Tool golden root"); SceneManager.MoveGameObjectToScene(root, stage.Scene);
                var visual = new GameObject("Visual"); visual.transform.SetParent(root.transform, false);
                var actor = visual.AddComponent<PNVisualActor>();
                actor.Bind(hub.Get<PNVisualAppearanceDefinition>().Get(EveId.Parse((string)sc["appearance"])), hub, Vector3.zero, sc["build"] != null ? EveId.Parse((string)sc["build"]) : (EveId?)null);
                var facing = visual.GetComponent<PNCameraFacingTarget>(); facing.ViewCamera = stage.Camera; facing.Align();
                actor.SetFacingLeft(sc["facingLeft"] != null && (bool)sc["facingLeft"]);
                var record = new JObject(); int index = 0;
                foreach (JObject step in (JArray)sc["steps"])
                {
                    if (step["facingLeft"] != null) actor.SetFacingLeft((bool)step["facingLeft"]);
                    if (step["play"] != null) actor.Play((string)step["play"], step["restart"] != null && (bool)step["restart"]);
                    if (step["stopOverlay"] != null && (bool)step["stopOverlay"]) actor.Animator.StopOverlay();
                    if (step["phase"] != null) actor.SetPhase((float)step["phase"]);
                    if (step["reset"] != null && (bool)step["reset"]) actor.Animator.ResetDynamics();
                    if (step["feed"] != null) actor.Animator.FeedBodyMotion((float)step["feed"][0], (float)step["feed"][1]);
                    actor.Tick(step["dt"] == null ? 0f : (float)step["dt"]);
                    if (meshFrames.Contains(index))
                    {
                        var vertices = actor.Mesh.vertices; var a = new JArray(); foreach (var v in vertices) { a.Add((double)v.x); a.Add((double)v.y); }
                        record[index.ToString()] = a; frames++;
                    }
                    index++;
                }
                output[(string)sc["id"]] = record; scenarios++;
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
        finally { stage?.Dispose(); PNVisualRuntime.Release(); PNVisualPacks.Clear(); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
        File.WriteAllText(outputPath, output.ToString(Newtonsoft.Json.Formatting.None));
        return "wrote " + frames + " mesh frames of " + scenarios + " scenarios to " + outputPath;
    }
}
#endif
