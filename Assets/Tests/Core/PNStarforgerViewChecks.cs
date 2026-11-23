#if UNITY_EDITOR
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Docs/ObservedViewPlan.md PV6, group 1: the Starforger pictures (PNStarforgerSlamView, PNStarforgerStarView) and the Starreaper pictures (PNStarreaperDrawView for Pierce and Rain, PNStarreaperAfterimageView) on real
// prefabs, driven by AGE alone (no Action runs in A), then through the engine's observer path (B, a Client session where the Action never runs). Edit Mode, preview scenes.
// It does NOT show a rendered frame of the final look, real Play Mode timing or a network round trip (the Player scenarios do).
public static class PNStarforgerViewChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Editor required");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Group 1 view check " + n + ": " + what); n++; }
        var docs = new PNGameContentSource(Application.streamingAssetsPath + "/BaseContent", "unused", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        PNVisualPacks.Set("project_n", Application.streamingAssetsPath + "/BaseContent");
        GameObject Spawn(Scene scene, string prefab, string character, long id)
        {
            var go = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefab)); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene);
            go.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse(character), 0); go.GetComponent<EveEntityLoader>().Load(hub); go.SetActive(true);
            Check(go.GetComponent<PNVisualBody>().Begin(hub.Characters.Get(EveId.Parse(character)), hub), character + " has its puppet");
            return go;
        }
        void Cleanup(Scene scene, params GameObject[] objects)
        {
            try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); }
            finally { try { foreach (var o in objects) if (o != null) UnityEngine.Object.DestroyImmediate(o); } finally { EditorSceneManager.ClosePreviewScene(scene); PNVisualRuntime.Release(); } }
        }
        int Rings(Scene scene) => scene.GetRootGameObjects().Count(o => o != null && o.activeInHierarchy && o.name == "Starforger slam warning");
        bool Driven(PNVisualBody b, string state) => b.Actor.IsDriven && b.Actor.Animator.BaseState == state;
        PNActionViewBuild Build(PNSkill skill, EveId id, Vector3 direction, Vector3? position, double scale = 1) => new PNActionViewBuild(skill, skill.GetData(id), direction, position, scale);
        const string ForgerPrefab = "Assets/Game/Entity/Enemy/Starforger/Prefab/Starforger.prefab", ReaperPrefab = "Assets/Game/Entity/Enemy/Starreaper/Prefab/Starreaper.prefab";
        var landing = new Vector3(2.5f, 0, 0);

        // ================= A: the views, driven directly =================
        var sceneA = EditorSceneManager.NewPreviewScene(); GameObject forger = null, reaper = null;
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:group1-view"));
            forger = Spawn(sceneA, ForgerPrefab, "project_n:starforger", 978501); var fs = forger.GetComponent<PNStarforgerSkill>(); var fb = forger.GetComponent<PNVisualBody>();
            reaper = Spawn(sceneA, ReaperPrefab, "project_n:starreaper", 978502); var rs = reaper.GetComponent<PNStarreaperSkill>(); var rb = reaper.GetComponent<PNVisualBody>();
            Check(fs.IsReady && rs.IsReady, "both skills are ready");
            var slamDef = hub.Actions().Get(PNStarforgerCombatContent.SlamAction); var starDef = hub.Actions().Get(PNStarforgerCombatContent.StarAction);
            Check(slamDef.ViewDeclared && starDef.ViewDeclared && slamDef.PresentationScaleTag == PNContentIds.MeleeScale, "both Starforger Actions declare a view; the slam's is sized by melee_scale");
            Check(hub.Actions().Get(PNStarreaperCombatContent.PierceAction).ViewDeclared && hub.Actions().Get(PNStarreaperCombatContent.RainAction).ViewDeclared && hub.Actions().Get(PNStarreaperCombatContent.AfterimageAction).ViewDeclared, "all three Starreaper Actions declare a view");

            // ---- Slam ----
            var slam = (PNStarforgerSlamView)PNStarforgerSlamView.Create(Build(fs, PNStarforgerCombatContent.Slam, Vector3.right, landing));
            slam.Advance(0, 1, true);
            Check(Rings(sceneA) == 1 && Driven(fb, "crouch") && !slam.Landed, "the landing ring exists and the body crouches, driven by the age");
            slam.Advance(.6, 1, false); Check(Driven(fb, "air"), "after the crouch (0.5 s) the body is airborne");
            slam.Fact(PNStarforgerSlamView.LandFact, new[] { 1d }, 1.3); slam.Advance(1.35, 1, false);
            Check(slam.Landed && Driven(fb, "slam"), "a land fact of 1 shows the landing and the slam pose");
            slam.Hide(); Check(Rings(sceneA) == 0, "a hidden view switches its ring off");
            slam.Advance(1.4, 1, false); Check(Rings(sceneA) == 1, "and the next frame shows it again");
            slam.Fact(PNStarforgerSlamView.LandFact, new[] { 0d }, 1.5); Check(Driven(fb, "slam"), "the first land stands");
            slam.End(PNActionViewEnd.Completed);
            Check(Rings(sceneA) == 0 && !fb.Actor.IsDriven && !fb.Holding, "End removes the ring and releases the body");
            slam.End(PNActionViewEnd.Completed);

            float hitRadius = fs.GetData(PNStarforgerCombatContent.Slam).Action.Perform["hit"]["radius"].Value<float>();
            foreach (double sc in new[] { 1d, 2d, .5d })
            {
                var sized = (PNStarforgerSlamView)PNStarforgerSlamView.Create(Build(fs, PNStarforgerCombatContent.Slam, Vector3.right, landing, sc));
                float want = PNHitCapture.ScaleLength(hitRadius, sc);
                Check(Mathf.Abs(sized.Telegraph.Radius - want) < 1e-5f && (sc != 2d || Mathf.Abs(sized.Telegraph.Radius - 2 * hitRadius) < 1e-4f), "the warning ring radius follows melee_scale " + sc + " (" + sized.Telegraph.Radius + " vs hit " + want + ")");
                sized.End(PNActionViewEnd.Cancelled);
            }
            var blocked = (PNStarforgerSlamView)PNStarforgerSlamView.Create(Build(fs, PNStarforgerCombatContent.Slam, Vector3.right, landing));
            blocked.Advance(.7, 1, false); blocked.Fact(PNStarforgerSlamView.LandFact, new[] { 0d }, .9); blocked.Advance(1.4, 1, false);
            Check(blocked.Landed && Driven(fb, "air"), "a land fact of 0 shows the landing without the slam pose");
            blocked.End(PNActionViewEnd.Cancelled); Check(Rings(sceneA) == 0, "a cancelled run leaves nothing");
            var silent = (PNStarforgerSlamView)PNStarforgerSlamView.Create(Build(fs, PNStarforgerCombatContent.Slam, Vector3.right, landing));
            silent.Advance(1.35, 1, false); Check(!silent.Landed, "before the grace has passed the jump is still in the air");
            silent.Advance(1.45, 1, false); Check(silent.Landed && Driven(fb, "slam"), "with no land fact the slam is shown at its registered time once the grace has passed");
            silent.End(PNActionViewEnd.Completed);
            var joiner = (PNStarforgerSlamView)PNStarforgerSlamView.Create(Build(fs, PNStarforgerCombatContent.Slam, Vector3.right, landing));
            joiner.Advance(2.4, 1, true);
            Check(joiner.Landed && Rings(sceneA) == 0 && Driven(fb, "slam"), "a joiner placed in the recovery sees the slam pose without the old burst");
            joiner.End(PNActionViewEnd.Completed);

            // ---- Star ----
            var star = (PNStarforgerStarView)PNStarforgerStarView.Create(Build(fs, PNStarforgerCombatContent.Star, Vector3.right, null));
            star.Advance(0, 1, true); Check(Driven(fb, "cast_star") && forger.transform.Find("Starforger golden hammer") != null, "the cast pose and the hammer are drawn by the age");
            star.Advance(.6, 1, false); Check(Driven(fb, "cast_star"), "the pose goes on through the cast");
            star.End(PNActionViewEnd.Completed); Check(!fb.Actor.IsDriven && !fb.Holding, "End releases the body");

            // ---- Pierce / Rain ----
            foreach (var rain in new[] { false, true })
            {
                string charge = rain ? "rain_charge" : "pierce_charge", release = rain ? "rain_release" : "pierce_release";
                var fx = reaper.GetComponent<PNStarreaperPresentation>();
                var view = (PNStarreaperDrawView)(rain ? PNStarreaperDrawView.CreateRain(Build(rs, PNStarreaperCombatContent.Rain, Vector3.right, null)) : PNStarreaperDrawView.CreatePierce(Build(rs, PNStarreaperCombatContent.Pierce, Vector3.right, null)));
                view.Advance(0, 1, true);
                Check(fx.Drawing && Driven(rb, charge), (rain ? "rain" : "pierce") + ": the bow is drawn and the body charges");
                view.Fact(PNStarreaperViewShared.AimFact, new[] { 0d, 1d }, .5); view.Advance(.9, 1, false);
                Check(fx.Drawing && !view.Fired, "the draw goes on through the windup");
                view.Advance((rain ? 2.0 : 1.5) + .05, 1, false);
                Check(view.Fired && !fx.Drawing && Driven(rb, release), "at the windup the bow is released and the body recoils");
                view.End(PNActionViewEnd.Completed); Check(!rb.Actor.IsDriven && !rb.Holding, "End releases the body");
                var joinerDraw = (PNStarreaperDrawView)(rain ? PNStarreaperDrawView.CreateRain(Build(rs, PNStarreaperCombatContent.Rain, Vector3.right, null)) : PNStarreaperDrawView.CreatePierce(Build(rs, PNStarreaperCombatContent.Pierce, Vector3.right, null)));
                joinerDraw.Advance(rain ? 2.2 : 1.7, 1, true);
                Check(joinerDraw.Fired && !fx.Drawing && Driven(rb, release), "a joiner placed after the release sees the recoil and no drawn bow");
                joinerDraw.End(PNActionViewEnd.Cancelled);
            }

            // ---- Afterimage ----
            var after = (PNStarreaperAfterimageView)PNStarreaperAfterimageView.Create(Build(rs, PNStarreaperCombatContent.Afterimage, Vector3.right, null));
            after.Advance(0, 1, true); Check(Driven(rb, "dash") && !after.Shot, "the retreat plays the dash and no shot yet");
            after.Advance(.15, 1, false); float dashBefore = rb.Actor.Animator.BaseTime;
            after.Fact(PNStarreaperAfterimageView.FinishFact, new[] { 1d, 0d, 1d }, .16); after.Advance(.3, 1, true);;
            Check(Driven(rb, "dash") && rb.Actor.Animator.BaseTime >= dashBefore - .001f, "the base stays at the end of the dash when the shot starts (not pulled back by the shot's age)");
            Check(after.Shot && rb.Actor.Animator.OverlayState == "shoot" && reaper.GetComponent<PNStarreaperPresentation>().Drawing, "a finish fact of 1 shows the shot: shoot pose (an overlay motion: the base stays dash) and a released bow (shot " + after.Shot + ", overlay " + rb.Actor.Animator.OverlayState + ", driven " + rb.Actor.IsDriven + ", drawing " + reaper.GetComponent<PNStarreaperPresentation>().Drawing + ")");
            after.End(PNActionViewEnd.Completed); Check(!rb.Actor.IsDriven, "End releases the body");
            var none = (PNStarreaperAfterimageView)PNStarreaperAfterimageView.Create(Build(rs, PNStarreaperCombatContent.Afterimage, Vector3.right, null));
            none.Advance(.1, 1, true); none.Fact(PNStarreaperAfterimageView.FinishFact, new[] { 0d }, .12); none.Advance(.6, 1, false);
            Check(!none.Shot && Driven(rb, "dash"), "a finish fact of 0 shows no shot");
            none.End(PNActionViewEnd.Cancelled);
            var quiet = (PNStarreaperAfterimageView)PNStarreaperAfterimageView.Create(Build(rs, PNStarreaperCombatContent.Afterimage, Vector3.right, null));
            quiet.Advance(.2, 1, false); Check(!quiet.Shot, "before the grace has passed the retreat is still going");
            quiet.Advance(.3, 1, true); Check(quiet.Shot && rb.Actor.Animator.OverlayState == "shoot", "with no finish fact the shot is shown once the grace has passed");
            quiet.End(PNActionViewEnd.Completed);
            Check(fs.ActionViewFailures == 0 && rs.ActionViewFailures == 0, "no view code failed");
        }
        finally { Cleanup(sceneA, forger, reaper); }

        // ================= B: an observer runs only the views =================
        var sceneB = EditorSceneManager.NewPreviewScene(); GameObject observed = null, observedReaper = null;
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Client, EveContentCompatibility.Create(hub, docs, "test:group1-view-observer"));
            typeof(EveSessionContext).GetMethod("AdmitLocalOwner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(EveRegistrySession.Context, new object[] { 5L });
            var areaRoot = new GameObject("Group 1 view area manager"); SceneManager.MoveGameObjectToScene(areaRoot, sceneB); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            observed = Spawn(sceneB, ForgerPrefab, "project_n:starforger", 978503); var fs = observed.GetComponent<PNStarforgerSkill>(); var fb = observed.GetComponent<PNVisualBody>();
            observedReaper = Spawn(sceneB, ReaperPrefab, "project_n:starreaper", 978504); var rs = observedReaper.GetComponent<PNStarreaperSkill>(); var rb = observedReaper.GetComponent<PNVisualBody>();
            fs.SetObservedActionPresentation(true); rs.SetObservedActionPresentation(true);
            string slamData = hub.Skills.ResolveIdentity(PNStarforgerCombatContent.Slam).ToString(), pierceData = hub.Skills.ResolveIdentity(PNStarreaperCombatContent.Pierce).ToString(), afterData = hub.Skills.ResolveIdentity(PNStarreaperCombatContent.Afterimage).ToString();
            Check(fs.CanPresentObservedAction(slamData, PNStarforgerCombatContent.SlamAction.ToString()) && rs.CanPresentObservedAction(pierceData, PNStarreaperCombatContent.PierceAction.ToString()) && rs.CanPresentObservedAction(afterData, PNStarreaperCombatContent.AfterimageAction.ToString()), "an observer can draw the Actions by their views");
            const double dt = .016; var run = new PNActionRunKey(71, 1);
            fs.PresentObservedRun(run, slamData, Vector3.right, landing, .02, true, 1, 0, 1, new List<PNActionFact>());
            Check(fs.ActionViewsRunning == 1 && Rings(sceneB) == 1 && Driven(fb, "crouch"), "the observer builds the slam's picture and drives the body from the stream");
            Check(fs.ViewedBySkill.TryGetValue(slamData, out int viewed) && viewed == 1 && fs.ObservedViewCount == 1, "the run is counted as drawn by its view");
            var facts = new List<PNActionFact> { new PNActionFact(1.3, PNStarforgerSlamView.LandFact, new[] { 1d }) };
            for (double p = .02; p < 1.4; p += dt) fs.PresentObservedRun(run, slamData, Vector3.right, landing, p, true, 1, dt, 1, p >= 1.3 ? facts : new List<PNActionFact>());
            Check(Driven(fb, "slam"), "the observer shows the slam pose from the progress and the land fact alone");
            fs.ReconcileObservedActionPresentation(true, k => null); fs.ReconcileObservedActionPresentation(true, k => PNActionRunEnd.Completed);
            Check(fs.ActionViewsRunning == 0 && Rings(sceneB) == 0 && !fb.Actor.IsDriven, "the run's end removes the picture and releases the body");
            var pierceRun = new PNActionRunKey(71, 2);
            rs.PresentObservedRun(pierceRun, pierceData, Vector3.right, null, 1.2, true, 1, 0, 1, new List<PNActionFact>());
            Check(observedReaper.GetComponent<PNStarreaperPresentation>().Drawing && Driven(rb, "pierce_charge"), "an observer that joins at 1.2 s sees the bow still drawn");
            rs.ReconcileObservedActionPresentation(true, k => null); rs.ReconcileObservedActionPresentation(true, k => PNActionRunEnd.Completed);
            var afterRun = new PNActionRunKey(71, 3); var afterFacts = new List<PNActionFact> { new PNActionFact(.16, PNStarreaperAfterimageView.FinishFact, new[] { 1d, 0d, 1d }) };
            rs.PresentObservedRun(afterRun, afterData, Vector3.right, null, .3, true, 1, 0, 1, afterFacts);
            Check(rb.Actor.Animator.OverlayState == "shoot", "an observer that joins after the retreat with the finish fact sees the shot");
            rs.ReconcileObservedActionPresentation(true, k => null); rs.ReconcileObservedActionPresentation(true, k => PNActionRunEnd.Completed);
            Check(rs.ActionViewsRunning == 0 && !rb.Actor.IsDriven && fs.ActionViewFailures == 0 && rs.ActionViewFailures == 0, "the pictures end with their runs and no view code failed");
        }
        finally { Cleanup(sceneB, observed, observedReaper); }
        return "PASS " + n + " group 1 view checks (Starforger slam and star, Starreaper pierce, rain and afterimage views on real prefabs, direct and through the observer path); no rendered frame, no network";
    }
}
#endif
