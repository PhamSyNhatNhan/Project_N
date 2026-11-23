#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Docs/ObservedViewPlan.md PV6, groups 2 and 3: the Starweaver cast view, the Herald Sidus view and the two Starchanter views (Heal, Boost) on real prefabs, driven by AGE alone (A), then through the
// engine's observer path, a Client session where no Action runs (B). Edit Mode, preview scenes. It does NOT show a rendered frame of the final look or a network round trip (the Player scenarios do).
public static class PNStarSupportViewChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Editor required");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Support view check " + n + ": " + what); n++; }
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
        bool Driven(PNVisualBody b, string state) => b.Actor.IsDriven && b.Actor.Animator.BaseState == state;
        PNActionViewBuild Build(PNSkill skill, EveId id, Vector3 direction, Vector3? position, double scale = 1) => new PNActionViewBuild(skill, skill.GetData(id), direction, position, scale);
        const string Weaver = "Assets/Game/Entity/Enemy/Starweaver/Prefab/Starweaver.prefab", Herald = "Assets/Game/Entity/Enemy/Starherald/Prefab/Starherald.prefab", Chanter = "Assets/Game/Entity/Enemy/Starchanter/Prefab/Starchanter.prefab";
        var sidus = EveId.Parse("project_n:starherald_sidus");
        var landing = new Vector3(2f, 0, 0);

        // ================= A: the views, driven directly =================
        var sceneA = EditorSceneManager.NewPreviewScene(); GameObject weaver = null, herald = null, chanter = null;
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:support-view"));
            weaver = Spawn(sceneA, Weaver, "project_n:starweaver", 978601); var ws = weaver.GetComponent<PNStarweaverSkill>(); var wb = weaver.GetComponent<PNVisualBody>();
            herald = Spawn(sceneA, Herald, "project_n:starherald", 978602); var hs = herald.GetComponent<PNHeraldSkill>(); var hb = herald.GetComponent<PNVisualBody>(); var hp = herald.GetComponent<PNHeraldPresentation>();
            chanter = Spawn(sceneA, Chanter, "project_n:starchanter", 978603); var cs = chanter.GetComponent<PNStarchanterSkill>(); var cb = chanter.GetComponent<PNVisualBody>(); var cp = chanter.GetComponent<PNStarchanterPresentation>();
            Check(ws.IsReady && hs.IsReady && cs.IsReady, "the three skills are ready");
            Check(hub.Actions().Get(PNStarweaverCombatContent.Action).ViewDeclared && hub.Actions().Get(PNHeraldCombatContent.SidusAction).ViewDeclared
                && hub.Actions().Get(PNStarchanterCombatContent.HealAction).ViewDeclared && hub.Actions().Get(PNStarchanterCombatContent.BoostAction).ViewDeclared, "all four Actions declare a view");

            // ---- Starweaver: weave and constellation ----
            foreach (var triangle in new[] { false, true })
            {
                var id = triangle ? PNStarweaverCombatContent.Triangle : PNStarweaverCombatContent.Weave; string state = triangle ? "cast_constellation" : "cast_weave"; float windup = triangle ? 2f : .5f;
                var view = (PNStarweaverCastView)PNStarweaverCastView.Create(Build(ws, id, Vector3.right, triangle ? landing : (Vector3?)null));
                view.Advance(0, 1, true);
                Check(view.Visual != null && Driven(wb, state), (triangle ? "constellation" : "weave") + ": the weaving light exists and the body plays " + state + " driven by the age");
                float early = view.Visual.VisualAge; view.Advance(windup * .6, 1, false);
                Check(view.Visual.VisualAge > early, "the light follows the run's age");
                view.Advance(windup + .05, 1, false); view.Hide(); Check(!view.Visual.gameObject.activeSelf, "a hidden view switches its light off");
                view.Advance(windup + .1, 1, false); Check(view.Visual.gameObject.activeSelf, "and the next frame shows it again");
                view.End(PNActionViewEnd.Completed); Check(!wb.Actor.IsDriven && !wb.Holding, "End removes the light and releases the body");
                view.End(PNActionViewEnd.Completed);
                var joiner = (PNStarweaverCastView)PNStarweaverCastView.Create(Build(ws, id, Vector3.right, triangle ? landing : (Vector3?)null));
                joiner.Advance(windup + .3, 1, true); Check(Driven(wb, state) && joiner.Visual != null, "a joiner placed after the release is placed at the age at once");
                joiner.End(PNActionViewEnd.Cancelled);
            }

            // ---- Herald ----
            var h = (PNHeraldSidusView)PNHeraldSidusView.Create(Build(hs, sidus, Vector3.right, null));
            h.Advance(0, 1, true); Check(Driven(hb, "cast") && hp.HasEffects, "the cast pose is placed by the age and the effect exists");
            var pAction = hs.GetData(sidus).Action.Perform; float hw = pAction["windup"].Value<float>();
            h.Advance(hw - .05, 1, false); Check(!h.Pulsed && !hp.RingVisible, "no pulse before the windup ends");
            h.Advance(hw + .02, 1, false); Check(h.Pulsed && hp.RingVisible, "the pulse ring appears when the windup ends");
            long hid = herald.GetComponent<PNStat>().EntityId.Value;
            hp.Advance(.001f); int sparks0 = hp.LiveSparks; h.Fact(PNHeraldSidusView.RecipientsFact, new[] { (double)hid }, hw); hp.Advance(.001f);
            Check(hp.LiveSparks > sparks0, "a recipients fact for a known entity adds particles (" + sparks0 + " -> " + hp.LiveSparks + ")");
            int sparks1 = hp.LiveSparks; h.Fact(PNHeraldSidusView.RecipientsFact, new[] { (double)hid, 9999999d, (double)hid }, hw); hp.Advance(.001f);
            Check(hp.LiveSparks - sparks1 == 2 * (sparks1 - sparks0), "two known ids in one fact give two bursts, the unknown id none");
            int sparks2 = hp.LiveSparks; h.Fact(PNHeraldSidusView.RecipientsFact, new[] { 9999999d }, hw); h.Fact("other", new[] { 1d }, hw); h.Fact(PNHeraldSidusView.RecipientsFact, null, hw); hp.Advance(.001f);
            Check(hp.LiveSparks == sparks2, "an unknown entity, an unknown key and an empty fact add no particle");
            Check(hs.ActionViewFailures == 0, "an unknown entity, an unknown key and an empty fact are ignored");
            h.End(PNActionViewEnd.Completed); Check(!hb.Actor.IsDriven && !hp.RingVisible, "End releases the body and the ring");
            var hl = (PNHeraldSidusView)PNHeraldSidusView.Create(Build(hs, sidus, Vector3.right, null));
            hl.Advance(hw + 1f, 1, true); Check(hl.Pulsed && !hp.RingVisible, "a joiner placed long after the pulse does not draw its ring");
            hl.End(PNActionViewEnd.Cancelled);

            // ---- Starchanter: heal ----
            var heal = (PNStarchanterHealView)PNStarchanterHealView.Create(Build(cs, PNStarchanterCombatContent.Heal, Vector3.right, landing));
            var hp2 = cs.GetData(PNStarchanterCombatContent.Heal).Action.Perform; float outS = hp2["teleportOut"].Value<float>(), inS = hp2["teleportIn"].Value<float>(), dur = hp2["duration"].Value<float>();
            heal.Advance(0, 1, true); Check(Driven(cb, "heal_teleport_out") && !heal.Arrived, "the heal starts vanishing, driven by the age");
            heal.Advance(outS + .05, 1, false); Check(heal.Arrived && Driven(cb, "heal_teleport_in"), "after the vanish the teleport arrives at the landing and the body appears");
            heal.Advance(outS + inS + .1, 1, false); Check(Driven(cb, "heal_channel"), "then the body channels");
            cp.Advance(.001f); int dust0 = cp.LivingDust; heal.Fact(PNStarchanterHealView.HealFact, new[] { 0d, (double)chanter.GetComponent<PNStat>().EntityId.Value }, outS + inS + 1.5); cp.Advance(.001f);
            Check(cp.LivingDust > dust0, "a heal pulse with a known recipient adds dust (" + dust0 + " -> " + cp.LivingDust + ")");
            heal.Advance(outS + inS + dur + .1, 1, false); Check(!cb.Actor.IsDriven, "the channel's end hands the body back to its own clock");
            heal.Fact(PNStarchanterHealView.HealFact, new[] { 1d }, outS + inS + dur);
            Check(heal.FinalSeen && cs.ActionViewFailures == 0, "the final pulse is remembered and no view code failed");
            heal.End(PNActionViewEnd.Completed); heal.End(PNActionViewEnd.Completed);
            var cut = (PNStarchanterHealView)PNStarchanterHealView.Create(Build(cs, PNStarchanterCombatContent.Heal, Vector3.right, landing));
            cut.Advance(outS + inS + 1, 1, true); cut.End(PNActionViewEnd.Cancelled); Check(!cut.FinalSeen && !cb.Actor.IsDriven, "a run cut before its final pulse ends as an interrupted cast");
            var hj = (PNStarchanterHealView)PNStarchanterHealView.Create(Build(cs, PNStarchanterCombatContent.Heal, Vector3.right, landing));
            hj.Advance(outS + inS + 2, 1, true); Check(hj.Arrived && Driven(cb, "heal_channel"), "a joiner placed mid-channel sees the arrived body channelling");
            hj.End(PNActionViewEnd.Completed);

            // ---- Starchanter: boost ----
            var boost = (PNStarchanterBoostView)PNStarchanterBoostView.Create(Build(cs, PNStarchanterCombatContent.Boost, Vector3.right, null));
            float bw = cs.GetData(PNStarchanterCombatContent.Boost).Action.Perform["windup"].Value<float>();
            boost.Advance(0, 1, true); Check(Driven(cb, "heal_channel") && !boost.Pulsed, "the boost gathers, driven by the age");
            boost.Advance(bw + .02, 1, false); Check(boost.Pulsed, "the pulse comes when the windup ends");
            cp.Advance(.001f); int dust1 = cp.LivingDust; boost.Fact(PNStarchanterBoostView.BoostFact, new[] { (double)chanter.GetComponent<PNStat>().EntityId.Value, 9999999d }, bw); boost.Fact("x", null, bw); cp.Advance(.001f);
            Check(cp.LivingDust > dust1, "a boost fact with a known recipient adds dust (" + dust1 + " -> " + cp.LivingDust + ")");
            Check(cs.ActionViewFailures == 0, "recipient facts for a known and an unknown entity do not fail");
            boost.End(PNActionViewEnd.Completed); Check(!cb.Actor.IsDriven, "End releases the body");
            Check(ws.ActionViewFailures == 0 && hs.ActionViewFailures == 0 && cs.ActionViewFailures == 0, "no view code failed");
        }
        finally { Cleanup(sceneA, weaver, herald, chanter); }

        // ================= B: an observer runs only the views =================
        var sceneB = EditorSceneManager.NewPreviewScene(); GameObject ow = null, oh = null, oc = null;
        try
        {
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Client, EveContentCompatibility.Create(hub, docs, "test:support-view-observer"));
            typeof(EveSessionContext).GetMethod("AdmitLocalOwner", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(EveRegistrySession.Context, new object[] { 5L });
            var areaRoot = new GameObject("Support view area manager"); SceneManager.MoveGameObjectToScene(areaRoot, sceneB); areaRoot.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
            ow = Spawn(sceneB, Weaver, "project_n:starweaver", 978604); var ws = ow.GetComponent<PNStarweaverSkill>(); var wb = ow.GetComponent<PNVisualBody>();
            oh = Spawn(sceneB, Herald, "project_n:starherald", 978605); var hs = oh.GetComponent<PNHeraldSkill>(); var hb = oh.GetComponent<PNVisualBody>();
            oc = Spawn(sceneB, Chanter, "project_n:starchanter", 978606); var cs = oc.GetComponent<PNStarchanterSkill>(); var cb = oc.GetComponent<PNVisualBody>();
            ws.SetObservedActionPresentation(true); hs.SetObservedActionPresentation(true); cs.SetObservedActionPresentation(true);
            string weaveData = hub.Skills.ResolveIdentity(PNStarweaverCombatContent.Weave).ToString(), sidusData = hub.Skills.ResolveIdentity(sidus).ToString(), healData = hub.Skills.ResolveIdentity(PNStarchanterCombatContent.Heal).ToString(), boostData = hub.Skills.ResolveIdentity(PNStarchanterCombatContent.Boost).ToString();
            Check(ws.CanPresentObservedAction(weaveData, PNStarweaverCombatContent.Action.ToString()) && hs.CanPresentObservedAction(sidusData, PNHeraldCombatContent.SidusAction.ToString())
                && cs.CanPresentObservedAction(healData, PNStarchanterCombatContent.HealAction.ToString()) && cs.CanPresentObservedAction(boostData, PNStarchanterCombatContent.BoostAction.ToString()), "an observer can draw all four Actions by their views");
            var none = new List<PNActionFact>();
            ws.PresentObservedRun(new PNActionRunKey(72, 1), weaveData, Vector3.right, null, .3, true, 1, 0, 1, none);
            Check(ws.ActionViewsRunning == 1 && Driven(wb, "cast_weave"), "the observer builds the weaving picture and drives the body from the stream");
            ws.ReconcileObservedActionPresentation(true, k => null); ws.ReconcileObservedActionPresentation(true, k => PNActionRunEnd.Completed);
            Check(ws.ActionViewsRunning == 0 && !wb.Actor.IsDriven, "the run's end removes the picture and releases the body");
            hs.PresentObservedRun(new PNActionRunKey(72, 2), sidusData, Vector3.right, null, .2, true, 1, 0, 1, none);
            Check(hs.ActionViewsRunning == 1 && Driven(hb, "cast"), "the observer builds the Herald's cast");
            hs.ReconcileObservedActionPresentation(true, k => null); hs.ReconcileObservedActionPresentation(true, k => PNActionRunEnd.Completed);
            var healRun = new PNActionRunKey(72, 3); var facts = new List<PNActionFact> { new PNActionFact(1.5, PNStarchanterHealView.HealFact, new[] { 0d, (double)oc.GetComponent<PNStat>().EntityId.Value }) };
            cs.PresentObservedRun(healRun, healData, Vector3.right, landing, 1.6, true, 1, 0, 1, facts);
            Check(cs.ActionViewsRunning == 1 && Driven(cb, "heal_channel"), "an observer that joins at 1.6 s sees the arrived body channelling");
            cs.ReconcileObservedActionPresentation(true, k => null); cs.ReconcileObservedActionPresentation(true, k => PNActionRunEnd.Completed);
            cs.PresentObservedRun(new PNActionRunKey(72, 4), boostData, Vector3.right, null, .1, true, 1, 0, 1, none);
            Check(cs.ActionViewsRunning == 1 && Driven(cb, "heal_channel"), "the observer builds the boost's picture");
            cs.ReconcileObservedActionPresentation(true, k => null); cs.ReconcileObservedActionPresentation(true, k => PNActionRunEnd.Completed);
            Check(cs.ActionViewsRunning == 0 && ws.ActionViewFailures == 0 && hs.ActionViewFailures == 0 && cs.ActionViewFailures == 0, "the pictures end with their runs and no view code failed");
        }
        finally { Cleanup(sceneB, ow, oh, oc); }
        return "PASS " + n + " support view checks (Starweaver cast, Herald Sidus, Starchanter heal and boost views on real prefabs, direct and through the observer path); no rendered frame, no network";
    }
}
#endif
