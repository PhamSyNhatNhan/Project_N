#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Docs/ObservedViewPlan.md PV6, Starkeeper: the two pictures (PNStarkeeperSweepView, PNStarkeeperPointView) on a real prefab, driven by AGE alone (no Action runs here), in a preview scene.
//  A  the views built and driven directly: strokes at their times, the re-aim fact, a late joiner (seek) skipping what already happened, the finish fact, End/cleanup, the body released;
//  B  the engine's observer path (a Client session, the Action never runs): the same pictures from the stream's progress and facts.
// It does NOT show a rendered frame of the final look, real Play Mode timing or a network round trip (the Player scenarios do).
public static class PNStarkeeperViewChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Editor required");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Starkeeper view check " + n + ": " + what); n++; }
        var docs = new PNGameContentSource(Application.streamingAssetsPath + "/BaseContent", "unused", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        PNVisualPacks.Set("project_n", Application.streamingAssetsPath + "/BaseContent");
        GameObject Spawn(Scene scene, long id, long ownerId)
        {
            var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starkeeper/Prefab/Starkeeper.prefab")); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene);
            go.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse("project_n:starkeeper"), ownerId); go.GetComponent<EveEntityLoader>().Load(hub); go.SetActive(true);
            Check(go.GetComponent<PNVisualBody>().Begin(hub.Characters.Get(EveId.Parse("project_n:starkeeper")), hub), "the Starkeeper has its puppet");
            return go;
        }
        void Cleanup(Scene scene, params GameObject[] objects)
        {
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { try { foreach (var o in objects) if (o != null) UnityEngine.Object.DestroyImmediate(o); } finally { EditorSceneManager.ClosePreviewScene(scene); PNVisualRuntime.Release(); } }
        }
        int Markers(Scene scene) => scene.GetRootGameObjects().Count(o => o != null && o.activeInHierarchy && (o.name.StartsWith("Starkeeper sweep") || o.name.StartsWith("Starkeeper star point") || o.name.StartsWith("Starkeeper Quet")));
        // a stroke of the sweep is "on" while its layer mesh has opaque colours; the visual keeps the two strokes' five layers in order
        int ActiveStrokes(Scene scene)
        {
            var root = scene.GetRootGameObjects().FirstOrDefault(o => o.name == "Starkeeper Quet Sao VFX"); if (root == null) return 0;
            var layers = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.name == "Retained slash 0").ToArray(); int on = 0;
            foreach (var f in layers) { var colors = f.sharedMesh.colors; if (colors.Length > 0 && colors[0].a > .5f) on++; }
            return on;
        }
        Vector3 MidRay(Scene scene, int stroke)
        {
            var root = scene.GetRootGameObjects().First(o => o.name == "Starkeeper Quet Sao VFX");
            var f = root.GetComponentsInChildren<MeshFilter>(true).Where(m => m.name == "Retained slash 0").ElementAt(stroke);
            var v = f.sharedMesh.vertices; var mid = v[20 * 2 + 1] - root.transform.position; mid.y = 0; return mid.normalized;      // the outer edge half way along the arc, from the sweep's origin (the entity is at 0)
        }
        PNActionViewBuild Build(PNSkill skill, EveId id, Vector3 direction, Vector3? position, double scale = 1) => new PNActionViewBuild(skill, skill.GetData(id), direction, position, scale);

        // ================= A: the views, driven directly =================
        var sceneA = EditorSceneManager.NewPreviewScene(); GameObject go = null;
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:starkeeper-view"));
            go = Spawn(sceneA, 978401, 0); var skill = go.GetComponent<PNStarkeeperSkill>(); var body = go.GetComponent<PNVisualBody>();
            Check(skill.IsReady, "the Starkeeper skill is ready");
            var sweepDef = hub.Actions().Get(PNStarkeeperCombatContent.SweepAction); var pointDef = hub.Actions().Get(PNStarkeeperCombatContent.StarPointAction);
            Check(sweepDef.ViewDeclared && pointDef.ViewDeclared && sweepDef.PresentationScaleTag == PNContentIds.MeleeScale, "both Actions declare a view; the sweep's is sized by melee_scale");

            // ---- the sweep ----
            var sweep = (PNStarkeeperSweepView)PNStarkeeperSweepView.Create(Build(skill, PNStarkeeperCombatContent.StarSweep, Vector3.right, null));
            sweep.Advance(0, 1, true);
            Check(Markers(sceneA) == 2 && ActiveStrokes(sceneA) == 0, "a warning sector and the effect root exist at age 0, no stroke yet");
            Check(body.Actor.IsDriven && body.Actor.Animator.BaseState == "star_sweep", "the body plays star_sweep driven by the age");
            sweep.Advance(.39, 1, false); Check(ActiveStrokes(sceneA) == 0, "no stroke before the windup (0.4 s)");
            sweep.Advance(.41, 1, false); Check(ActiveStrokes(sceneA) == 1, "the first stroke appears at the windup");
            sweep.Fact(PNStarkeeperSweepView.AimFact, new[] { 0d, 1d }, .6);
            sweep.Advance(.75, 1, false); sweep.Advance(.95, 1, false);
            Check(ActiveStrokes(sceneA) == 1, "the first stroke is over (0.36 s) and the second is on");
            Check(Vector3.Dot(MidRay(sceneA, 1), Vector3.forward) > .9f && Vector3.Dot(MidRay(sceneA, 0), Vector3.right) > .9f, "the first stroke follows the cast direction, the second the re-aim fact");
            sweep.Result(new PNActionViewResult("hit", new Vector3(1, 0, 0), 1, .95));
            sweep.Hide(); Check(Markers(sceneA) == 1, "a hidden view switches its effect root off");
            sweep.Advance(1.0, 1, false); Check(Markers(sceneA) == 2, "and the next frame shows it again");
            sweep.End(PNActionViewEnd.Completed);
            Check(Markers(sceneA) == 0 && !body.Actor.IsDriven && !body.Holding, "End removes every picture and releases the body");
            sweep.End(PNActionViewEnd.Completed);

            // a late joiner placed at 0.8 s: the first stroke (0.4 s) is long gone and is not drawn; the second is
            var late = (PNStarkeeperSweepView)PNStarkeeperSweepView.Create(Build(skill, PNStarkeeperCombatContent.StarSweep, Vector3.right, null));
            late.Advance(.8, 1, true);
            Check(ActiveStrokes(sceneA) == 1 && Vector3.Dot(MidRay(sceneA, 1), Vector3.right) > .9f, "a late joiner sees only the stroke still in its life, and with no aim fact the second follows the first");
            Check(body.Actor.IsDriven && body.Actor.Animator.BaseTime > .5f, "the body is placed at the age at once");
            late.End(PNActionViewEnd.Cancelled);
            Check(Markers(sceneA) == 0, "a cancelled run leaves nothing");
            var scaled = (PNStarkeeperSweepView)PNStarkeeperSweepView.Create(Build(skill, PNStarkeeperCombatContent.StarSweep, Vector3.right, null, 2));
            scaled.Advance(0, 1, true);
            var telegraph = sceneA.GetRootGameObjects().First(o => o.name == "Starkeeper sweep").transform.Find("Constellation boundary");
            Check(telegraph.GetComponent<MeshFilter>().sharedMesh.vertices.Max(v => v.magnitude) > 2.6f, "the committed scale (2) doubles the warning radius");
            scaled.End(PNActionViewEnd.Completed);

            // ---- the star point ----
            var anchor = new Vector3(3, 0, 0);
            var point = (PNStarkeeperPointView)PNStarkeeperPointView.Create(Build(skill, PNStarkeeperCombatContent.StarPoint, Vector3.right, anchor));
            point.Advance(0, 1, true);
            Check(Markers(sceneA) == 1 && body.Actor.IsDriven && body.Actor.Animator.BaseState == "star_point", "the mark exists and the body plays star_point driven");
            point.Advance(1.0, 1, false); Check(!point.Visual.Reached, "not reached during the windup");
            point.Advance(2.1, 1, false);
            point.Fact(PNStarkeeperPointView.FinishFact, new[] { 1d }, 2.2); point.Advance(2.3, 1, false);
            Check(point.Visual.Reached, "a finish fact of 1 shows the star reached");
            point.End(PNActionViewEnd.Completed);
            Check(Markers(sceneA) == 0 && !body.Actor.IsDriven, "a completed run removes its pictures and releases the body");
            var wall = (PNStarkeeperPointView)PNStarkeeperPointView.Create(Build(skill, PNStarkeeperCombatContent.StarPoint, Vector3.right, anchor));
            wall.Advance(2.05, 1, false); wall.Fact(PNStarkeeperPointView.FinishFact, new[] { 0d }, 2.1); wall.Advance(2.5, 1, false);
            Check(!wall.Visual.Reached, "a finish fact of 0 (a wall) shows no arrival");
            wall.Fact(PNStarkeeperPointView.FinishFact, new[] { 1d }, 2.4); Check(!wall.Visual.Reached, "the first finish stands");
            wall.End(PNActionViewEnd.Cancelled); Check(Markers(sceneA) == 0, "a cancelled run (Edit Mode: no fade) leaves nothing");
            var silent = (PNStarkeeperPointView)PNStarkeeperPointView.Create(Build(skill, PNStarkeeperCombatContent.StarPoint, Vector3.right, anchor));
            silent.Advance(2.2, 1, false); Check(!silent.Visual.Reached, "before the grace has passed the lunge is still in flight");
            silent.Advance(2.4, 1, false); Check(silent.Visual.Reached, "with no finish fact the lunge is shown as arriving once the grace has passed");
            silent.End(PNActionViewEnd.Completed);
            var joiner = (PNStarkeeperPointView)PNStarkeeperPointView.Create(Build(skill, PNStarkeeperCombatContent.StarPoint, Vector3.right, anchor));
            joiner.Advance(3.0, 1, true); Check(joiner.Visual.Reached && body.Actor.IsDriven, "a joiner placed in the recovery sees the star reached (no replay of the dropped fact)");
            joiner.End(PNActionViewEnd.Completed);
            Check(go.transform.Find("Star spear").GetComponent<MeshRenderer>().enabled, "the spear renderer is restored");
            Check(skill.ActionViewFailures == 0, "no view code failed");
        }
        finally { Cleanup(sceneA, go); }

        // ================= B: an observer runs only the views =================
        var sceneB = EditorSceneManager.NewPreviewScene(); GameObject observed = null;
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Client, EveContentCompatibility.Create(hub, docs, "test:starkeeper-view-observer"));
            typeof(EveSessionContext).GetMethod("AdmitLocalOwner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(EveRegistrySession.Context, new object[] { 5L });
            var areaRoot = new GameObject("Starkeeper view area manager"); SceneManager.MoveGameObjectToScene(areaRoot, sceneB); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            observed = Spawn(sceneB, 978402, 0); var skill = observed.GetComponent<PNStarkeeperSkill>(); var body = observed.GetComponent<PNVisualBody>();
            skill.SetObservedActionPresentation(true);
            string sweepData = hub.Skills.ResolveIdentity(PNStarkeeperCombatContent.StarSweep).ToString(), pointData = hub.Skills.ResolveIdentity(PNStarkeeperCombatContent.StarPoint).ToString();
            Check(skill.CanPresentObservedAction(sweepData, PNStarkeeperCombatContent.SweepAction.ToString()) && skill.CanPresentObservedAction(pointData, PNStarkeeperCombatContent.StarPointAction.ToString()), "an observer can draw both Actions by their views");
            const double dt = .016; var run = new PNActionRunKey(70, 1);
            skill.PresentObservedAction(run, sweepData, Vector3.right, .02, true, 1, 0);
            Check(skill.ActionViewsRunning == 1 && Markers(sceneB) == 2 && body.Actor.IsDriven, "the observer builds the sweep's picture and drives the body from the stream");
            Check(skill.ViewedBySkill.TryGetValue(sweepData, out int viewedSweeps) && viewedSweeps == 1 && skill.ViewedKeys.Contains("70:1:" + sweepData) && skill.ObservedViewCount == 1, "the run is counted as drawn by its view (the Player fixtures count it)");
            for (double p = .02; p < .9; p += dt) skill.PresentObservedAction(run, sweepData, Vector3.right, p, true, 1, dt);
            skill.PresentObservedAction(run, sweepData, Vector3.right, .9, true, 1, 0);
            Check(ActiveStrokes(sceneB) == 1, "the observer shows the second stroke at 0.9 s from the progress alone");
            skill.ReconcileObservedActionPresentation(true, k => null); skill.ReconcileObservedActionPresentation(true, k => PNActionRunEnd.Completed);
            Check(skill.ActionViewsRunning == 0 && Markers(sceneB) == 0 && !body.Actor.IsDriven, "the run's end removes the picture and releases the body");
            var pointRun = new PNActionRunKey(70, 2); var facts = new List<PNActionFact> { new PNActionFact(2.2, PNStarkeeperPointView.FinishFact, new[] { 1d }) };
            skill.PresentObservedRun(pointRun, pointData, Vector3.right, new Vector3(3, 0, 0), 2.5, true, 1, 0, 1, facts);
            var pv = sceneB.GetRootGameObjects().Single(o => o.name == "Starkeeper star point").GetComponent<PNStarkeeperPointVisual>();
            Check(pv.Reached, "an observer that joins at 2.5 s with the finish fact shows the star reached");
            skill.ReconcileObservedActionPresentation(true, k => null); skill.ReconcileObservedActionPresentation(true, k => PNActionRunEnd.Completed);
            Check(Markers(sceneB) == 0 && skill.ActionViewFailures == 0, "the point's picture ends with the run and no view code failed");
        }
        finally { Cleanup(sceneB, observed); }
        return "PASS " + n + " Starkeeper view checks (sweep and star point views on a real prefab, direct and through the observer path); no rendered frame, no network";
    }
}
#endif
