#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Docs/StrikeBlastPlan.md stage 2 (B4): the Blast manager and the shapes. Edit Mode, a preview scene, nothing saved. Covered here: the timeline (nothing before the delay, one detonation, the tail),
// the shapes and `blast_scale` (also the capture tags), the three dependencies (Independent / Owner / Action), a time stop that freezes the blast (the delay does not pass), pool reuse
// with a clean clock, and the end of the session (Shutdown ends every blast).
public static class PNBlastEditorChecks
{
    public static string Run()
    {
        if (Application.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Edit required.");
        var docs = new PNGameContentSource(Application.streamingAssetsPath + "/BaseContent", "unused", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        var scene = EditorSceneManager.NewPreviewScene(); int n = 0; bool started = false; var made = new List<GameObject>();
        void Check(bool ok, string what) { if (!ok) throw new Exception("Blast check " + n + ": " + what); n++; }
        try
        {
            EveRegistrySession.Begin(); started = true; EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:blast"));
            var rootObject = new GameObject("Blast manager fixture"); SceneManager.MoveGameObjectToScene(rootObject, scene); made.Add(rootObject);
            var blasts = rootObject.AddComponent<PNBlastManager>(); blasts.Initialize(EveRegistrySession.Context);
            var areas = rootObject.AddComponent<PNAreaManager>(); areas.Initialize(EveRegistrySession.Context);
            PNStat Owner(long id)
            {
                var go = new GameObject("Blast owner " + id); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene); go.layer = PNLayers.Void;
                go.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse("project_n:dummy"), 0); go.AddComponent<CapsuleCollider>().height = 1; go.AddComponent<EveTimeScale>();
                var stat = go.AddComponent<PNEnemyStat>(); go.AddComponent<EveEntityLoader>().Load(hub); go.SetActive(true); go.GetComponent<PNBuffManager>().Advance(0); made.Add(go); return stat;
            }
            PNStat Dummy(long id, Vector3 where)
            {
                var d = new GameObject("Blast dummy " + id); d.SetActive(false); SceneManager.MoveGameObjectToScene(d, scene); d.transform.position = where;
                d.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse("project_n:eve"), 0);
                var s = d.AddComponent<PNPlayerStat>().AsPlayer(); d.AddComponent<Rigidbody>().isKinematic = true;
                var c = d.AddComponent<CapsuleCollider>(); c.radius = .2f; c.height = 1.4f; c.center = Vector3.up * .7f; d.layer = PNLayers.Player;
                d.AddComponent<EveEntityLoader>().Load(hub); d.SetActive(true); made.Add(d); return s;
            }
            float Health(PNStat s) => (float)s.GetCurrent(PNContentIds.Health);
            JObject Input(double delay, double radius, double tail = 0, double height = 1, string extra = "")
                => JObject.Parse("{'delay':" + delay + ",'tail':" + tail + ",'radius':" + radius + ",'height':" + height + ",'value':{'amount':100},'canCrit':false,'tags':['base:magic']" + extra + "}");
            PNBlastReceipt Make(EveId id, JObject input, Vector3 at, PNStat owner, PNBlastDependency dependency)
                => Create(id, input, at, owner, dependency);
            PNBlastReceipt Create(EveId id, JObject input, Vector3 at, PNStat owner, PNBlastDependency dependency)
            { Physics.SyncTransforms(); return blasts.Create(new PNBlastCreateRequest(id, input, PNCombatSource.Capture(owner), scene, at, owner, dependency)); }
            PNBlastInstance Running() => scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNBlastInstance>(true)).FirstOrDefault(x => x.IsRunning);

            var owner = Owner(994100); var near = Dummy(994101, new Vector3(.5f, 0, 0)); var far = Dummy(994102, new Vector3(1.7f, 0, 0));
            Check(owner.IsReady && near.IsReady && far.IsReady, "the owner and the dummies are ready");
            int baseline = PNTimeTargets.For(null).Count;

            // ---- timeline: nothing before the delay, one detonation, then it ends ----
            float n0 = Health(near), f0 = Health(far);
            var first = Make(PNBlastGameplayContent.Cylinder, Input(1, 1), Vector3.zero, owner, PNBlastDependency.Independent);
            Check(first.Status == PNBlastStatus.Created && blasts.ActiveCount == 1 && PNTimeTargets.For(null).Count == baseline + 1, "a blast is created, runs and follows time (it is in the world's list)");
            var instance = (PNTimedBlast)Running();
            Check(instance.Clock != null && instance.Clock.MinScale == 0 && ((IPNTimeTarget)instance).TimeLayer == PNLayers.Blast, "a blast has its own clock and the Blast layer");
            blasts.Advance(.5f);
            Check(Health(near) == n0 && !instance.Detonated && Math.Abs(instance.Age - .5f) < 1e-5, "nothing happens before the delay");
            blasts.Advance(.6f);
            Check(instance.Detonated || blasts.ActiveCount == 0, "the blast detonates when its own clock reaches the delay");
            Check(Health(near) < n0 && Health(far) == f0, "the body 0.3 m inside the 1 m radius is hit, the body 1.5 m away is not (near " + n0 + "->" + Health(near) + ", far " + f0 + "->" + Health(far) + ", hits " + instance.HitCount + ", bounds " + near.GetComponent<Collider>().bounds + ")");
            float afterOne = Health(near); blasts.Advance(1f);
            Check(Health(near) == afterOne && blasts.ActiveCount == 0 && PNTimeTargets.For(null).Count == baseline, "it detonates once and ends with no tail; it leaves the list");

            // ---- blast_scale: radius, capture tags ----
            owner.SetSource(EveId.Parse("test:blast_scale"), new[] { new EveStatModifier(PNContentIds.BlastScale, 1d, 1) });
            float n1 = Health(near), f1 = Health(far);
            var scaled = Make(PNBlastGameplayContent.Cylinder, Input(.1, 1), Vector3.zero, owner, PNBlastDependency.Independent);
            var scaledInstance = (PNTimedBlast)Running();
            Check(Math.Abs(scaledInstance.Radius - 2f) < 1e-5 && Math.Abs(scaledInstance.Height - 2f) < 1e-5 && scaledInstance.Capture.Tags.Contains(PNContentIds.BlastDamage) && scaledInstance.Capture.Tags.Contains(EveId.Parse("base:magic")),
                "blast_scale +100% doubles radius and height; blast_damage joins the tags");
            blasts.Advance(.2f);
            Check(Health(far) < f1 && Health(near) < n1, "the doubled blast now reaches the 1.5 m body");
            owner.SetSource(EveId.Parse("test:blast_scale"), Array.Empty<EveStatModifier>());

            // ---- polygon: a triangle keeps its tips ----
            var tipDummy = Dummy(994103, new Vector3(0, 0, 1.0f)); var cornerDummy = Dummy(994104, new Vector3(1.0f, 0, 1.0f));
            float t0 = Health(tipDummy), c0 = Health(cornerDummy);
            Make(PNBlastGameplayContent.Polygon, Input(.1, 1.2, 0, 1, ",'sides':3,'rotation':0"), Vector3.zero, owner, PNBlastDependency.Independent); blasts.Advance(.2f);
            Check(Health(tipDummy) < t0 && Health(cornerDummy) == c0, "a triangle of radius 1.2 reaches its tip at +z and not the square corner beside it");

            // ---- the tail holds the blast after the detonation ----
            float n2 = Health(near);
            Make(PNBlastGameplayContent.Cylinder, Input(.1, 1, .5), Vector3.zero, owner, PNBlastDependency.Independent); blasts.Advance(.15f);
            Check(blasts.ActiveCount == 1 && Health(near) < n2, "a blast with a tail detonates and stays alive");
            blasts.Advance(.3f); Check(blasts.ActiveCount == 1, "it stays through the tail");
            blasts.Advance(.3f); Check(blasts.ActiveCount == 0, "and ends when the tail is over");

            // ---- Action dependency: the creator ends it before it detonates ----
            float n3 = Health(near);
            var action = Make(PNBlastGameplayContent.Cylinder, Input(1, 1), Vector3.zero, owner, PNBlastDependency.Action);
            blasts.Advance(.5f); Check(action.Handle.IsAlive, "an Action-dependent blast lives while nobody ends it");
            Check(action.Handle.End() && !action.Handle.IsAlive && blasts.ActiveCount == 0, "the creating Action ends it through the handle");
            blasts.Advance(2f); Check(Health(near) == n3, "an ended blast never detonates");

            // ---- Owner dependency: the owner's loss ends it ----
            var ownerBlast = Make(PNBlastGameplayContent.Cylinder, Input(1, 1), Vector3.zero, owner, PNBlastDependency.Owner);
            blasts.Advance(.5f); Check(ownerBlast.Handle.IsAlive, "an Owner-dependent blast lives while its owner does");
            var snapshotBefore = blasts.Get(ownerBlast.Handle);
            owner.gameObject.SetActive(false); blasts.Advance(.1f);
            Check(!ownerBlast.Handle.IsAlive && Health(near) == n3, "disabling the owner ends its Owner-dependent blast without a detonation");
            owner.gameObject.SetActive(true); owner.GetComponent<PNBuffManager>().Advance(0);
            var survivor = Make(PNBlastGameplayContent.Cylinder, Input(.3, 1), Vector3.zero, owner, PNBlastDependency.Independent);
            owner.gameObject.SetActive(false); blasts.Advance(.4f);
            Check(Health(near) < n3, "an Independent blast outlives its owner and still detonates");
            owner.gameObject.SetActive(true); owner.GetComponent<PNBuffManager>().Advance(0);
            bool refused = false; try { new PNBlastCreateRequest(PNBlastGameplayContent.Cylinder, Input(1, 1), PNCombatSource.Capture(owner), scene, Vector3.zero, null, PNBlastDependency.Owner); } catch (ArgumentException) { refused = true; }
            Check(refused, "an Owner-dependent blast needs its owner");

            // ---- time stop: the telegraph clock and the detonation stop together ----
            float n4 = Health(near);
            var frozen = Make(PNBlastGameplayContent.Cylinder, Input(1, 1), Vector3.zero, owner, PNBlastDependency.Independent); var frozenInstance = (PNTimedBlast)Running();
            var zone = areas.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"),
                JObject.Parse("{'radius':3,'reduction':1,'minimumReduction':0,'duration':20,'dependent':false,'targetLayers':['Blast'],'layerMode':'Include'}"), PNCombatSource.Capture(owner), scene, Vector3.zero));
            Check(zone.Status == PNAreaStatus.Created, "the time zone is created");
            areas.Advance(.1f); blasts.Advance(.5f);
            for (int i = 0; i < 10; i++) { areas.Advance(.1f); blasts.Advance(.5f); }
            Check(frozenInstance.Clock.Scale == 0f && frozenInstance.Age < .05f && !frozenInstance.Detonated && Health(near) == n4, "a full time stop freezes the blast: its age does not advance and it does not detonate (age " + frozenInstance.Age + ")");
            zone.Handle.End(); areas.Advance(.3f); blasts.Advance(.1f);
            Check(frozenInstance.Clock.ModifierCount == 0, "the zone gives back its modifiers");
            blasts.Advance(1.2f);
            Check(Health(near) < n4 && blasts.ActiveCount == 0, "after the stop the blast resumes and detonates");

            // ---- pool reuse starts with a clean clock; session end ends every blast ----
            var reused = Make(PNBlastGameplayContent.Cylinder, Input(5, 1), Vector3.zero, owner, PNBlastDependency.Independent); var reusedInstance = (PNTimedBlast)Running();
            Check(reused.Status == PNBlastStatus.Created && reusedInstance.Clock.ModifierCount == 0 && reusedInstance.Clock.Scale == 1f && reusedInstance.Age == 0f && !reusedInstance.Detonated, "a pooled blast starts again clean");
            float n5 = Health(near);
            blasts.Shutdown();
            Check(blasts.ActiveCount == 0 && !reused.Handle.IsAlive && PNTimeTargets.For(null).Count == baseline && Health(near) == n5, "ending the session ends every running blast without a detonation");
            return "PASS " + n + " blast checks (timeline, shapes, blast_scale, Independent/Owner/Action, time stop, pool reuse, shutdown; a preview scene)";
        }
        finally
        {
            try { if (started) EveRegistrySession.End(); }
            finally { foreach (var o in made) if (o != null) UnityEngine.Object.DestroyImmediate(o); EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
#endif
