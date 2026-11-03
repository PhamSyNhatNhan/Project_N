#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using EveEngine;

// The Starchanter (visual/starchanter.json, made in the rig editor: a rig that extends the humanoid with a spring hood and cloak, its own mob motion set) played by the real actor in the Editor.
// One check per RunCommand. Frames go to <output>/ for the user to look at.
public static partial class PNVisualEditorChecks
{
    public static string RunStarchanter(string outputDirectory = "G:/ProjectN_Camera/RigRuntime/Starchanter")
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        PNVisualSystem.Prune();
        int checks = 0; Stage stage = null; GameObject root = null;
        string baseDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets", "BaseContent"));
        var hub = LoadWithDocuments(baseDirectory, ("starchanter", File.ReadAllText(Path.Combine(baseDirectory, "visual", "starchanter.json"))));
        try
        {
            var rigDef = hub.Get<PNVisualRigDefinition>().Get(EveId.Parse("project_n:starchanter"));
            Require(rigDef.Extends.HasValue && rigDef.Resolve(id => hub.Get<PNVisualRigDefinition>().Get(id)).Bones.Count == 11, "the Starchanter rig extends the humanoid (8 bones) with a hood, a cloak and a tie"); checks++;
            stage = new Stage(); stage.Camera.orthographicSize = .45f; stage.Camera.transform.position = new Vector3(0f, .3f, -5f);
            var actor = SpawnAppearance(stage, hub, "project_n:starchanter", out root);
            Require(actor.QuadCount == 12, "twelve quads: the six body parts and the six outfit drawings (" + actor.QuadCount + ")"); checks++;
            foreach (var step in new (string name, string state, float time, bool restart)[] { ("idle", "idle", 1.0f, true), ("walk", "walk", .45f, true), ("run", "run", .3f, true), ("hurt", "hurt", .08f, true),
                ("dead_a", "dead", .5f, true), ("dead_b", "dead", 1.6f, false), ("stun", "stun", 1.0f, true), ("recover", "recover", .25f, true), ("heal_channel", "heal_channel", .9f, true), ("heal_out", "heal_teleport_out", .3f, true), ("heal_in", "heal_teleport_in", .6f, true) })
            {
                if (step.state == "hurt") { actor.Play("idle", true); Settle(actor, .3f); }
                Require(actor.Play(step.state, step.restart), "state " + step.state + " exists"); checks++; Settle(actor, step.time);
                stage.Capture(outputDirectory + "/" + step.name + ".png"); int painted = stage.Painted();
                Require(painted > (step.name == "heal_out" ? 3000 : 6000), step.name + ": paints the figure (" + painted + " px)"); checks++;
            }
            var before = actor.Mesh.vertices; for (int i = 0; i < 5; i++) actor.Tick(0f); var after = actor.Mesh.vertices; bool same = before.Length == after.Length; for (int i = 0; same && i < before.Length; i++) same = before[i] == after[i];
            Require(same, "Tick(0) leaves every vertex exactly where it was"); checks++;
            actor.Play("run", true); for (int i = 0; i < 200; i++) actor.Tick(.016f);
            long gc0 = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 600; i++) actor.Tick(.016f);
            Require((GC.GetAllocatedBytesForCurrentThread() - gc0) / 600 <= 200, "the figure allocates almost nothing per frame"); checks++;
        }
        finally { if (root != null) UnityEngine.Object.DestroyImmediate(root); stage?.Dispose(); PNVisualRuntime.Release(); PNVisualPacks.Clear(); }
        return "PNVisualEditorChecks.RunStarchanter: PASS (" + checks + " checks); frames in " + outputDirectory;
    }

    public static string RunStarforger(string outputDirectory = "G:/ProjectN_Camera/RigRuntime/Starforger")
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        PNVisualSystem.Prune();
        int checks = 0; Stage stage = null; GameObject root = null;
        string baseDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets", "BaseContent"));
        var hub = LoadWithDocuments(baseDirectory, ("starforger", File.ReadAllText(Path.Combine(baseDirectory, "visual", "starforger.json"))));
        try
        {
            var rigDef = hub.Get<PNVisualRigDefinition>().Get(EveId.Parse("project_n:starforger"));
            Require(rigDef.Extends.HasValue && rigDef.Resolve(id => hub.Get<PNVisualRigDefinition>().Get(id)).Bones.Count == 8, "the Starforger rig extends the humanoid and adds no bone"); checks++;
            stage = new Stage(); stage.Camera.orthographicSize = .45f; stage.Camera.transform.position = new Vector3(0f, .3f, -5f);
            var actor = SpawnAppearance(stage, hub, "project_n:starforger", out root);
            Require(actor.QuadCount == 12, "twelve quads: the six body parts, four outfit drawings and two shoes (" + actor.QuadCount + ")"); checks++;
            foreach (var step in new (string name, string state, float time, bool restart)[] { ("idle", "idle", 1.0f, true), ("walk", "walk", .45f, true), ("run", "run", .3f, true), ("hurt", "hurt", .08f, true), ("dead_a", "dead", .5f, true), ("dead_b", "dead", 2.6f, false), ("stun", "stun", 1.0f, true), ("recover", "recover", .25f, true) })
            {
                if (step.state == "hurt") { actor.Play("idle", true); Settle(actor, .3f); }
                Require(actor.Play(step.state, step.restart), "state " + step.state + " exists"); checks++; Settle(actor, step.time);
                stage.Capture(outputDirectory + "/" + step.name + ".png"); int painted = stage.Painted();
                Require(painted > 6000, step.name + ": paints the figure (" + painted + " px)"); checks++;
            }
            var before = actor.Mesh.vertices; for (int i = 0; i < 5; i++) actor.Tick(0f); var after = actor.Mesh.vertices; bool same = before.Length == after.Length; for (int i = 0; same && i < before.Length; i++) same = before[i] == after[i];
            Require(same, "Tick(0) leaves every vertex exactly where it was"); checks++;
            actor.Play("run", true); for (int i = 0; i < 200; i++) actor.Tick(.016f);
            long gc0 = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 600; i++) actor.Tick(.016f);
            Require((GC.GetAllocatedBytesForCurrentThread() - gc0) / 600 <= 200, "the figure allocates almost nothing per frame"); checks++;
        }
        finally { if (root != null) UnityEngine.Object.DestroyImmediate(root); stage?.Dispose(); PNVisualRuntime.Release(); PNVisualPacks.Clear(); }
        return "PNVisualEditorChecks.RunStarforger: PASS (" + checks + " checks); frames in " + outputDirectory;
    }

    public static string RunStarreaper(string outputDirectory = "G:/ProjectN_Camera/RigRuntime/Starreaper")
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        PNVisualSystem.Prune();
        int checks = 0; Stage stage = null; GameObject root = null;
        string baseDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets", "BaseContent"));
        var hub = LoadWithDocuments(baseDirectory, ("starreaper", File.ReadAllText(Path.Combine(baseDirectory, "visual", "starreaper.json"))));
        try
        {
            var rigDef = hub.Get<PNVisualRigDefinition>().Get(EveId.Parse("project_n:starreaper"));
            Require(rigDef.Extends.HasValue && rigDef.Resolve(id => hub.Get<PNVisualRigDefinition>().Get(id)).Bones.Count == 10, "the Starreaper rig extends the humanoid and adds a hood and a cloak bone"); checks++;
            stage = new Stage(); stage.Camera.orthographicSize = .45f; stage.Camera.transform.position = new Vector3(0f, .3f, -5f);
            var actor = SpawnAppearance(stage, hub, "project_n:starreaper", out root);
            Require(actor.QuadCount == 10, "ten quads: the six body parts and four outfit drawings (" + actor.QuadCount + ")"); checks++;
            foreach (var step in new (string name, string state, float time, bool restart)[] { ("idle", "idle", 1.0f, true), ("walk", "walk", .45f, true), ("run", "run", .3f, true), ("hurt", "hurt", .08f, true), ("dead_a", "dead", .5f, true), ("dead_b", "dead", 2.6f, false), ("stun", "stun", 1.0f, true), ("recover", "recover", .25f, true) })
            {
                if (step.state == "hurt") { actor.Play("idle", true); Settle(actor, .3f); }
                Require(actor.Play(step.state, step.restart), "state " + step.state + " exists"); checks++; Settle(actor, step.time);
                stage.Capture(outputDirectory + "/" + step.name + ".png"); int painted = stage.Painted();
                Require(painted > 6000, step.name + ": paints the figure (" + painted + " px)"); checks++;
            }
            var before = actor.Mesh.vertices; for (int i = 0; i < 5; i++) actor.Tick(0f); var after = actor.Mesh.vertices; bool same = before.Length == after.Length; for (int i = 0; same && i < before.Length; i++) same = before[i] == after[i];
            Require(same, "Tick(0) leaves every vertex exactly where it was"); checks++;
            actor.Play("run", true); for (int i = 0; i < 200; i++) actor.Tick(.016f);
            long gc0 = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 600; i++) actor.Tick(.016f);
            Require((GC.GetAllocatedBytesForCurrentThread() - gc0) / 600 <= 200, "the figure allocates almost nothing per frame"); checks++;
        }
        finally { if (root != null) UnityEngine.Object.DestroyImmediate(root); stage?.Dispose(); PNVisualRuntime.Release(); PNVisualPacks.Clear(); }
        return "PNVisualEditorChecks.RunStarreaper: PASS (" + checks + " checks); frames in " + outputDirectory;
    }
}
#endif
