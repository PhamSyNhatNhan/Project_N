#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Native checks of the Visual rig runtime features (Docs/VisualRigRuntimeImplementationPlan.md): R0 a free rig (the test creature does not extend the humanoid), the drawing box, Tick(0) and body motion;
//     later phases add spring chains, IK, lattice deformation and skin weights here. Same pattern as RunBend: a disposable preview scene, real content, real frames in G:/ProjectN_Camera/RigRuntime.
// VI: Kiem tra native cac tinh nang rig moi cua Visual: R0 rig tu do (sinh vat thu khong ke thua humanoid), khung ve, Tick(0), chuyen dong cua than.
public static partial class PNVisualEditorChecks
{
    private static EveRegistryHub LoadWithDocuments(string baseDirectory, params (string name, string json)[] documents)
    {
        PNVisualPacks.Clear(); PNVisualPacks.Set("project_n", baseDirectory);
        var list = new List<EveContentDocument> { new EveContentDocument("visual", File.ReadAllText(Path.Combine(baseDirectory, "visual", "humanoid.json")), "project_n") };
        foreach (var d in documents) list.Add(new EveContentDocument(d.name, d.json, "project_n"));
        return new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, list);
    }

    private static PNVisualActor SpawnAppearance(Stage stage, EveRegistryHub hub, string appearance, out GameObject root, float x = 0f)
    {
        root = new GameObject("Rig feature root"); SceneManager.MoveGameObjectToScene(root, stage.Scene); root.transform.position = new Vector3(x, 0f, 0f);
        var visual = new GameObject("Visual"); visual.transform.SetParent(root.transform, false);
        var actor = visual.AddComponent<PNVisualActor>();
        actor.Bind(hub.Get<PNVisualAppearanceDefinition>().Get(EveId.Parse(appearance)), hub, Vector3.zero);
        var facing = visual.GetComponent<PNCameraFacingTarget>(); facing.ViewCamera = stage.Camera; facing.Align();
        return actor;
    }

    private static void Settle(PNVisualActor actor, float seconds) { float t = 0f; while (t < seconds - 1e-4f) { float dt = Mathf.Min(.02f, seconds - t); actor.Tick(dt); t += dt; } }

    // R0: a rig that does not extend the humanoid, played by the real actor, body and mesh code.
    public static string RunCreature(string outputDirectory = "G:/ProjectN_Camera/RigRuntime/Creature")
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        PNVisualSystem.Prune();
        int checks = 0; Stage stage = null; GameObject root = null, instance = null, root2 = null, root3 = null; Scene bodyScene = default;
        string baseDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets", "BaseContent"));
        string creatureJson = File.ReadAllText(Path.Combine(baseDirectory, "visual", "creature_test.json"));
        var hub = LoadWithDocuments(baseDirectory, ("creature", creatureJson));
        try
        {
            var atlas = PNVisualRuntime.AtlasFor(hub);
            Require(atlas.ImageCount == 16 && atlas.Texture != null, "the atlas packs the 10 humanoid and the 6 creature PNGs (" + atlas.ImageCount + ")"); checks++;
            var rigDef = hub.Get<PNVisualRigDefinition>().Get(EveId.Parse("project_n:creature_test"));
            Require(!rigDef.Extends.HasValue && rigDef.Resolve(id => hub.Get<PNVisualRigDefinition>().Get(id)).Bones.Count == 13, "the creature rig has its own 13 bones and extends nothing"); checks++;
            stage = new Stage(); stage.Camera.orthographicSize = .45f; stage.Camera.transform.position = new Vector3(0f, .3f, -5f);
            var actor = SpawnAppearance(stage, hub, "project_n:creature_test", out root);
            Require(actor.QuadCount == 8 && actor.Mesh.vertexCount == 4 + 2 * 13 + 4 + 4 + 63 + 44 + 4 + 4 && actor.Mesh.GetIndexCount(0) == 5 * 6 + 12 * 6 + 8 * 6 * 6 + 3 * 10 * 6, "one mesh holds the body, the 12 strips of the tail, the stars, the 8 x 6 flag, the 3 x 10 ribbon and the two arm pieces (" + actor.Mesh.vertexCount + " vertices, " + actor.Mesh.GetIndexCount(0) + " indices)"); checks++;
            Require(Mathf.Abs(actor.CapsuleHeight - .35f) < 1e-4f && Mathf.Abs(actor.CapsuleRadius - .12f) < 1e-4f, "the capsule comes from the rig and the normal build"); checks++;
            // every state paints the creature
            foreach (var step in new (string name, string state, float time, bool restart)[] { ("idle_0", "idle", 0f, false), ("idle_1", "idle", .9f, false), ("walk", "walk", .4f, false), ("run", "run", .3f, false),
                ("hurt", "hurt", .1f, true), ("dead_fall", "dead", .4f, true), ("dead_floor", "dead", 1.5f, false), ("stun", "stun", .8f, true), ("recover_mid", "recover", .25f, true), ("recover_end", "recover", .6f, false) })
            {
                if (step.state == "hurt") { actor.Play("idle", true); Settle(actor, .3f); }
                actor.Play(step.state, step.restart); Settle(actor, step.time);
                stage.Capture(outputDirectory + "/" + step.name + ".png"); int painted = stage.Painted();
                Require(painted > 6000, step.name + ": paints the creature (" + painted + " px)"); checks++;
            }
            // Tick(0) is a snapshot of the current mesh: it changes nothing (the mirror capture of the Lorica break relies on it)
            actor.Play("idle", true); Settle(actor, 1.1f);
            var before = actor.Mesh.vertices; for (int i = 0; i < 5; i++) actor.Tick(0f); var after = actor.Mesh.vertices;
            bool same = before.Length == after.Length; for (int i = 0; same && i < before.Length; i++) same = before[i] == after[i];
            Require(same, "Tick(0) leaves every vertex exactly where it was"); checks++;
            // facing left mirrors the creature (the tail goes from the left of the body to its right)
            actor.Play("idle", true); Settle(actor, .01f); stage.Capture(null); var right = PaintedCentroid(stage);
            actor.SetFacingLeft(true); actor.Tick(.01f); stage.Capture(outputDirectory + "/left.png"); var left = PaintedCentroid(stage);
            Require(Mathf.Abs((right.x - Stage.Size * .5f) + (left.x - Stage.Size * .5f)) < 14f, "facing left mirrors the picture about the centre (" + right.x + " vs " + left.x + ")"); checks++;
            actor.SetFacingLeft(false);
            // the same atlas flavour flip as the settings do: the mesh and the vertex count stay
            int vertices = actor.Mesh.vertexCount; actor.KeyActor = !actor.KeyActor; actor.KeyActor = !actor.KeyActor;
            Require(actor.Mesh.vertexCount == vertices && actor.QuadCount == 8, "a Rebind keeps the mesh"); checks++;
            // no allocation per frame
            actor.Play("run", true); for (int i = 0; i < 200; i++) actor.Tick(.016f);
            long gc0 = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 600; i++) actor.Tick(.016f);
            Require((GC.GetAllocatedBytesForCurrentThread() - gc0) / 600 <= 200, "the creature allocates almost nothing per frame"); checks++;
            // a teleport (a jump of more than 3 m between two ticks) is survived
            root.transform.position += new Vector3(40f, 0f, 0f); actor.Tick(.016f); root.transform.position = Vector3.zero; actor.Tick(.016f);
            Require(actor.Mesh.vertexCount == vertices, "a teleport between two ticks changes nothing structural"); checks++;
            UnityEngine.Object.DestroyImmediate(root); root = null;

            // the drawing box: a rig with a tiny capsule whose stars swing far outside it gets a box that holds them; a rig that says drawBounds gets at least that box
            var tiny = JObject.Parse(creatureJson); var tinyRig = (JObject)tiny["extensions"]["project_n"]["visualRigs"][0]; tinyRig["capsule"]["height"] = .08; tinyRig["capsule"]["radius"] = .04;
            var hubTiny = LoadWithDocuments(baseDirectory, ("creature", tiny.ToString()));
            var small = SpawnAppearance(stage, hubTiny, "project_n:creature_test", out root2);
            var capsuleBox = new Bounds(new Vector3(0f, small.CapsuleHeight * .5f, 0f), new Vector3(small.CapsuleHeight * 2f, small.CapsuleHeight * 2.5f, .1f));
            Require(small.Mesh.bounds.extents.x >= .19f && small.Mesh.bounds.extents.x > capsuleBox.extents.x && small.Mesh.bounds.size.y > capsuleBox.size.y, "the drawing box grows to hold parts outside a small capsule (extents " + small.Mesh.bounds.extents.x.ToString("0.000") + " x vs " + capsuleBox.extents.x.ToString("0.000") + " from the capsule)"); checks++;
            UnityEngine.Object.DestroyImmediate(root2); root2 = null; PNVisualRuntime.Release();
            tinyRig["drawBounds"] = JObject.Parse("{'width':1.6,'height':1.2,'bottom':0.15}");
            var hubBox = LoadWithDocuments(baseDirectory, ("creature", tiny.ToString()));
            var boxed = SpawnAppearance(stage, hubBox, "project_n:creature_test", out root3);
            Require(boxed.Mesh.bounds.size.x >= 1.6f - 1e-3f && boxed.Mesh.bounds.size.y >= 1.2f - 1e-3f && boxed.Mesh.bounds.min.y <= -.15f + 1e-3f, "drawBounds gives the box asked for (" + boxed.Mesh.bounds.size.ToString("0.00") + ", from y " + boxed.Mesh.bounds.min.y.ToString("0.00") + ")"); checks++;
            UnityEngine.Object.DestroyImmediate(root3); root3 = null; PNVisualRuntime.Release();
            foreach (var bad in new[] { "{'width':-1,'height':1}", "{'width':1,'height':200}", "{'width':1,'height':1,'bottom':1,'x':2}", "{'width':1}" })
            {
                tinyRig["drawBounds"] = JObject.Parse(bad);
                bool refused = false; try { LoadWithDocuments(baseDirectory, ("creature", tiny.ToString())); } catch (Exception) { refused = true; }
                Require(refused, "drawBounds " + bad + " is refused"); checks++;
            }
            // the entity side: the Visual test prefab with the creature as its body
            hub = LoadWithDocuments(baseDirectory, ("creature", creatureJson)); PNVisualRuntime.Release();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Character/VisualTest/Prefab/VisualTest.prefab");
            bodyScene = EditorSceneManager.NewPreviewScene(); instance = UnityEngine.Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(instance, bodyScene);
            var body = instance.GetComponent<PNVisualBody>(); var capsule = instance.GetComponent<CapsuleCollider>(); float feet0 = capsule.center.y - capsule.height * .5f;
            var character = new EveCharacterDefinition(EveId.Parse("project_n:visual_creature_test"), new List<EveInitialStat>(), null, "", JObject.Parse("{'body':{'visual':'project_n:creature_test'}}"));
            Require(body.Begin(character, hub) && body.Actor.QuadCount == 8, "the character's body block gives the creature"); checks++;
            Require(Mathf.Abs(capsule.height - .35f) < 1e-4f && Mathf.Abs(capsule.radius - .12f) < 1e-4f && Mathf.Abs(capsule.center.y - capsule.height * .5f - feet0) < 1e-4f, "the capsule takes the creature's size, feet fixed (" + capsule.height + " x " + capsule.radius + ")"); checks++;
            body.Feed(.02f); instance.transform.position += new Vector3(.04f, 0f, 0f); body.Feed(.02f);
            Require(body.Actor.Animator.BaseState == "run" && body.Actor.Animator.HasState("stun") && body.Actor.Animator.HasState("recover"), "moving fast plays the creature's run; the stun and recover states exist"); checks++;
            body.Clear();
        }
        finally
        {
            foreach (var go in new[] { root, root2, root3 }) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance); if (bodyScene.IsValid()) EditorSceneManager.ClosePreviewScene(bodyScene);
            stage?.Dispose(); PNVisualRuntime.Release(); PNVisualPacks.Clear();
        }
        Require(PNVisualSystem.Count == 0, "no puppet left registered"); checks++;
        return "PASS " + checks + " Visual rig creature checks (frames in " + outputDirectory + ")";
    }

    // R1-R4 inside one real rig: spring chains (tail, ribbon bones), IK (the feeler), lattice (the flag) and skin (the ribbon), each compared with the same creature built without that feature.
    public static string RunFeatures(string outputDirectory = "G:/ProjectN_Camera/RigRuntime/Features")
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        PNVisualSystem.Prune();
        int checks = 0; Stage stage = null; GameObject rootA = null, rootB = null, rootC = null, rootD = null;
        string baseDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets", "BaseContent"));
        string json = File.ReadAllText(Path.Combine(baseDirectory, "visual", "creature_test.json"));
        JObject WithoutDynamics() { var j = JObject.Parse(json); ((JObject)j["extensions"]["project_n"]["visualRigs"][0]).Remove("dynamics"); return j; }
        JObject WithoutSkin() { var j = JObject.Parse(json); foreach (JObject part in (JArray)j["extensions"]["project_n"]["visualParts"]) if (part["skin"] != null) part.Remove("skin"); return j; }
        JObject WithoutLattice() { var j = JObject.Parse(json); foreach (JObject motion in (JArray)j["extensions"]["project_n"]["visualMotions"]) motion.Remove("deform"); return j; }
        try
        {
            stage = new Stage(); stage.Camera.orthographicSize = .5f; stage.Camera.transform.position = new Vector3(0f, .3f, -5f);
            var hubB = LoadWithDocuments(baseDirectory, ("creature", WithoutDynamics().ToString())); var actorB = SpawnAppearance(stage, hubB, "project_n:creature_test", out rootB, 20f);
            var hubC = LoadWithDocuments(baseDirectory, ("creature", WithoutSkin().ToString())); var actorC = SpawnAppearance(stage, hubC, "project_n:creature_test", out rootC, 40f);
            var hubD = LoadWithDocuments(baseDirectory, ("creature", WithoutLattice().ToString())); var actorD = SpawnAppearance(stage, hubD, "project_n:creature_test", out rootD, 60f);
            // the full creature is made LAST: the atlas of the visual runtime belongs to the most recent content, so a frame can only show the actor made with it
            var hubA = LoadWithDocuments(baseDirectory, ("creature", json)); var actorA = SpawnAppearance(stage, hubA, "project_n:creature_test", out rootA);
            var all4 = new[] { actorA, actorB, actorC, actorD };
            Require(actorA.Mesh.vertexCount == actorB.Mesh.vertexCount && actorA.Mesh.vertexCount == actorC.Mesh.vertexCount && actorA.Mesh.vertexCount == actorD.Mesh.vertexCount, "the four variants have the same mesh size"); checks++;
            // R4 at rest: the skinned ribbon is exactly the grid it was made from
            float restDiff = MaxDiff(actorA.Mesh.vertices, actorC.Mesh.vertices, out _, 1e-6f);
            Require(restDiff < 2e-5f, "at rest a skinned ribbon sits exactly where the unskinned grid does (largest difference " + restDiff.ToString("0.0000000") + " m)"); checks++;
            foreach (var actor in all4) actor.Play("idle", true);
            for (int i = 0; i < 70; i++) foreach (var actor in all4) actor.Tick(.016f);
            // R2: the feeler reaches the goal while the goal moves
            var rigResolved = hubA.Get<PNVisualRigDefinition>().Get(EveId.Parse("project_n:creature_test")).Resolve(id => hubA.Get<PNVisualRigDefinition>().Get(id));
            int lower = rigResolved.BoneIndex("armL"), reach = rigResolved.BoneIndex("reach"); float worst = 0f;
            for (int i = 0; i < 100; i++)
            {
                foreach (var each in all4) each.Tick(.016f); var an = actorA.Animator; /* every variant ticks the same, so their phases stay equal for the comparisons below */ int o = lower * 6; float sx = an.BoneScale[lower * 2];
                float tx = an.BoneAffine[o] * 110f * sx + an.BoneAffine[o + 4], ty = an.BoneAffine[o + 1] * 110f * sx + an.BoneAffine[o + 5], gx = an.BoneAffine[reach * 6 + 4], gy = an.BoneAffine[reach * 6 + 5];
                worst = Mathf.Max(worst, Mathf.Sqrt((tx - gx) * (tx - gx) + (ty - gy) * (ty - gy)) * 1630f);
            }
            Require(worst < 1f, "the IK feeler's tip stays on the moving goal (the worst miss in 1.6 s is " + worst.ToString("0.00") + " px)"); checks++;
            stage.Capture(outputDirectory + "/idle_all_features.png"); Require(stage.Painted() > 9000, "the creature with all the features paints a body"); checks++;
            // R3: the lattice moves the flag; the variant without deform tracks keeps it flat
            float flat = MaxDiff(actorA.Mesh.vertices, actorD.Mesh.vertices, out int moved, 2e-4f);
            Require(moved >= 6 && flat > .004f, "the lattice moves flag vertices (" + moved + " vertices moved by up to " + (flat * 1000f).ToString("0.0") + " mm)"); checks++;
            // R1: the body moves, the springs trail; a still body lets them settle
            rootA.transform.position = Vector3.zero; rootB.transform.position = new Vector3(20f, 0f, 0f); rootC.transform.position = new Vector3(40f, 0f, 0f);       // the variants stand off screen so a frame shows only the full creature
            for (int i = 0; i < 40; i++) { foreach (var go in new[] { rootA, rootB, rootC }) go.transform.position += new Vector3(.05f, 0f, 0f); actorA.Tick(.016f); actorB.Tick(.016f); actorC.Tick(.016f); }
            float trail = MaxDiff(actorA.Mesh.vertices, actorB.Mesh.vertices, out int trailing, 1e-4f);
            Require(trailing > 20 && trail > .004f, "a moving creature drags its tail and ribbon behind (" + trailing + " vertices differ from the creature without springs, by up to " + (trail * 1000f).ToString("0.0") + " mm)"); checks++;
            float skinned = MaxDiff(actorA.Mesh.vertices, actorC.Mesh.vertices, out int skinCount, 1e-4f);
            Require(skinCount > 10 && skinned > .003f, "while the ribbon's bones swing the skinned ribbon bends where the rigid grid does not (" + skinCount + " vertices, up to " + (skinned * 1000f).ToString("0.0") + " mm)"); checks++;
            Follow(stage, rootA); stage.Capture(outputDirectory + "/moving.png"); Require(stage.Painted() > 9000, "the creature paints while it is moving"); checks++;
            for (int i = 0; i < 400; i++) { actorA.Tick(.016f); actorB.Tick(.016f); }
            float settle = MaxDiff(actorA.Mesh.vertices, actorB.Mesh.vertices, out _, 1e-3f);
            int worstVertex = 0; float worstSeen = 0f; var va = actorA.Mesh.vertices; var vb = actorB.Mesh.vertices; for (int i = 0; i < va.Length; i++) { float d = (va[i] - vb[i]).magnitude; if (d > worstSeen) { worstSeen = d; worstVertex = i; } }
            Require(settle < .012f, "a creature that stands still lets the springs settle (they follow the animation within " + (settle * 1000f).ToString("0.0") + " mm of the one without springs; vertex " + worstVertex + " of " + va.Length + ": " + va[worstVertex].ToString("0.000") + " vs " + vb[worstVertex].ToString("0.000") + "; animator times " + actorA.Animator.BaseTime.ToString("0.000") + " / " + actorB.Animator.BaseTime.ToString("0.000") + ")"); checks++;
            // Tick(0) and facing left with every feature on
            var before = actorA.Mesh.vertices; for (int i = 0; i < 4; i++) actorA.Tick(0f); var after = actorA.Mesh.vertices; bool same = true; for (int i = 0; i < before.Length; i++) same &= before[i] == after[i];
            Require(same, "Tick(0) changes nothing, springs and IK included"); checks++;
            actorA.SetFacingLeft(true); actorA.Tick(.016f); Follow(stage, rootA); stage.Capture(outputDirectory + "/left.png"); Require(stage.Painted() > 9000, "facing left paints too"); checks++;
            actorA.SetFacingLeft(false);
            // a long gap (out of view) and a teleport leave no NaN in the mesh
            actorA.Tick(5f); rootA.transform.position += new Vector3(30f, 0f, 0f); actorA.Tick(.016f); var verts = actorA.Mesh.vertices; bool finite = true; for (int i = 0; i < verts.Length; i++) finite &= !float.IsNaN(verts[i].x + verts[i].y) && !float.IsInfinity(verts[i].x + verts[i].y);
            Require(finite, "a long gap and a teleport leave every vertex finite"); checks++;
            // no allocation per frame with everything on
            rootA.transform.position = Vector3.zero; actorA.Play("run", true); for (int i = 0; i < 200; i++) actorA.Tick(.016f);
            long gc0 = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 600; i++) { rootA.transform.position += new Vector3(.001f, 0f, 0f); actorA.Tick(.016f); }
            Require((GC.GetAllocatedBytesForCurrentThread() - gc0) / 600 <= 200, "the creature with springs, IK, lattice and skin allocates almost nothing per frame"); checks++;
        }
        finally
        {
            foreach (var go in new[] { rootA, rootB, rootC, rootD }) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            stage?.Dispose(); PNVisualRuntime.Release(); PNVisualPacks.Clear();
        }
        Require(PNVisualSystem.Count == 0, "no puppet left registered"); checks++;
        return "PASS " + checks + " Visual rig feature checks (frames in " + outputDirectory + ")";
    }

    // The capture camera looks at the object (only sideways): a creature that walked away is still in the frame.
    private static void Follow(Stage stage, GameObject target) { var p = stage.Camera.transform.position; stage.Camera.transform.position = new Vector3(target.transform.position.x, p.y, p.z); }

    // The largest distance between two vertex arrays of the same length, and how many vertices are farther apart than `threshold`.
    private static float MaxDiff(Vector3[] a, Vector3[] b, out int count, float threshold)
    {
        float max = 0f; count = 0; int n = Mathf.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) { float d = (a[i] - b[i]).magnitude; if (d > max) max = d; if (d > threshold) count++; }
        return max;
    }

    // The centroid of the pixels that differ from the background, in image pixels.
    private static Vector2 PaintedCentroid(Stage stage)
    {
        var bg = stage.Camera.backgroundColor; var pixels = stage.Image.GetPixels32(); double sx = 0, sy = 0; int n = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i]; if (Mathf.Abs(p.r / 255f - bg.r) > .04f || Mathf.Abs(p.g / 255f - bg.g) > .04f || Mathf.Abs(p.b / 255f - bg.b) > .04f) { sx += i % Stage.Size; sy += i / Stage.Size; n++; }
        }
        return n == 0 ? Vector2.zero : new Vector2((float)(sx / n), (float)(sy / n));
    }
}
#endif
