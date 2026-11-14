#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Docs/StrikeBlastPlan.md stage 0: the clocks that are not entities. PNTimeTargets (the world's list), the clock of every area, the time-scale zone
// polling that list by layer (and not slowing itself), and `area_scale`. Edit Mode, a preview scene, nothing saved. Projectile behavior under a zone is
// covered by PNProjectileTimeStopEditorChecks, which now goes through the same list.
public class PNTimeProbeArea : PNAreaInstance
{
    public static double Total; public static int Calls;
    protected override void Initialize(PNAreaCreateRequest request) { Total = 0; Calls = 0; }
    public override void Advance(float delta) { Total += delta; Calls++; }
    protected override void Cleanup() { }
}
public sealed class PNTimeFreeProbeArea : PNTimeProbeArea { public override bool AffectedByTime => false; }
public sealed class PNTimeProbeModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)
    {
        var registry = hub.Instances();
        registry.Register(new PNAreaDefinition(EveId.Parse("time_test:probe"), typeof(PNTimeProbeArea), new JObject(), (data, content) => { }, input => { }));
        registry.Register(new PNAreaDefinition(EveId.Parse("time_test:free"), typeof(PNTimeFreeProbeArea), new JObject(), (data, content) => { }, input => { }));
    }
}
public static class PNTimeTargetsEditorChecks
{
    private sealed class Fake : IPNTimeTarget
    {
        public EveTimeScale Clock; public int Layer = PNLayers.Area; public Vector3 Position; public bool Active = true;
        public EveTimeScale TimeClock => Clock; public int TimeLayer => Layer; public Vector3 TimePosition => Position; public bool TimeActive => Active;
    }
    public static string Run()
    {
        if (Application.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Edit required.");
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNTimeProbeModule() }, docs);
        var scene = EditorSceneManager.NewPreviewScene(); int checks = 0; PNAreaManager areas = null; var made = new List<GameObject>();
        void Check(bool ok, string name) { if (!ok) throw new Exception("Time targets check failed: " + name); checks++; }
        PNStat Entity(long id, Vector3 position)
        {
            var go = new GameObject("Time fixture " + id); go.SetActive(false); SceneManager.MoveGameObjectToScene(go, scene); go.layer = PNLayers.Void; go.transform.position = position;
            go.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse("project_n:dummy"), 0);
            go.AddComponent<CapsuleCollider>().height = 1; go.AddComponent<EveTimeScale>();
            var stat = go.AddComponent<PNEnemyStat>(); go.AddComponent<EveEntityLoader>().Load(hub); go.SetActive(true); go.GetComponent<PNBuffManager>().Advance(0); made.Add(go); return stat;
        }
        try
        {
            // ---- the list itself ----
            var list = PNTimeTargets.For(null);
            Check(PNTimeTargets.For(null) == list, "the list of no world is one shared list");
            var holder = new GameObject("Clock holder"); SceneManager.MoveGameObjectToScene(holder, scene); made.Add(holder);
            var clock = holder.AddComponent<PNAreaClock>();
            int before = list.Count;
            var near = new Fake { Clock = clock, Position = new Vector3(1, 0, 0) }; var far = new Fake { Clock = clock, Position = new Vector3(9, 0, 0) };
            var wrongLayer = new Fake { Clock = clock, Position = new Vector3(1, 0, 0), Layer = PNLayers.Projectile }; var ended = new Fake { Clock = clock, Position = new Vector3(1, 0, 0), Active = false };
            var eNear = list.Register(near); var eFar = list.Register(far); var eLayer = list.Register(wrongLayer); var eEnded = list.Register(ended);
            Check(list.Count == before + 4 && eNear.Id != eFar.Id && eNear.IsRegistered, "targets register with distinct ids");
            var hits = new List<PNTimeTargets.Entry>();
            list.Query(Vector3.zero, 3, PNLayers.Bit(PNLayers.Area), hits);
            Check(hits.Count == 1 && hits[0] == eNear, "a query keeps what is inside the radius, in the layer mask and still active");
            hits.Clear(); list.Query(Vector3.zero, 3, ~0, hits);
            Check(hits.Count == 2 && !hits.Contains(eEnded) && hits.Contains(eLayer), "the mask ~0 takes every layer but never an ended target");
            hits.Clear(); list.Query(Vector3.zero, 20, PNLayers.Bit(PNLayers.Area), hits);
            Check(hits.Count == 2, "a wider radius reaches the far target");
            PNTimeTargets.Unregister(eNear); PNTimeTargets.Unregister(eNear);
            hits.Clear(); list.Query(Vector3.zero, 20, PNLayers.Bit(PNLayers.Area), hits);
            Check(!eNear.IsRegistered && hits.Count == 1 && hits[0] == eFar && list.Count == before + 3, "unregistering is idempotent and keeps the other entries reachable");
            PNTimeTargets.Unregister(eFar); PNTimeTargets.Unregister(eLayer); PNTimeTargets.Unregister(eEnded);
            Check(list.Count == before, "the list is back to its size");

            // ---- areas follow a zone ----
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:time_targets"));
            var root = new GameObject("Area manager fixture"); SceneManager.MoveGameObjectToScene(root, scene); made.Add(root); areas = root.AddComponent<PNAreaManager>(); areas.Initialize(EveRegistrySession.Context);
            var source = Entity(994001, new Vector3(40, 0, 0)); var capture = PNCombatSource.Capture(source);
            PNAreaReceipt Probe(string id, Vector3 position, PNCombatSource using_ = null) => areas.Create(new PNAreaCreateRequest(EveId.Parse(id), new JObject(), using_ ?? capture, scene, position));
            PNAreaReceipt Zone(Vector3 position, string layers, double radius = 3, double reduction = .5, PNCombatSource using_ = null)
                => areas.Create(new PNAreaCreateRequest(EveId.Parse("base:timescale_area"),
                    JObject.Parse("{'radius':" + radius + ",'reduction':" + reduction + ",'minimumReduction':0,'duration':20,'dependent':false,'targetLayers':[" + layers + "],'layerMode':'Include'}"), using_ ?? capture, scene, position));
            int baseline = PNTimeTargets.For(null).Count;
            var probe = Probe("time_test:probe", Vector3.zero);
            Check(probe.Status == PNAreaStatus.Created && PNTimeTargets.For(null).Count == baseline + 1, "an area that follows time registers while it runs");
            var instance = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNTimeProbeArea>(true)).First(x => x.IsRunning && !(x is PNTimeFreeProbeArea));
            Check(instance.Clock != null && instance.Clock.ModifierCount == 0 && instance.Clock.MinScale == 0, "every area has a clock, untouched and with minimum zero");
            PNTimeProbeArea.Total = 0; areas.Advance(.1f);
            Check(Math.Abs(PNTimeProbeArea.Total - .1) < 1e-6, "with no time effect the area advances by the game delta");

            var zone = Zone(Vector3.zero, "'Area'");
            Check(zone.Status == PNAreaStatus.Created && PNTimeTargets.For(null).Count == baseline + 1, "the zone itself is not in the list (it does not follow time)");
            PNTimeProbeArea.Total = 0; areas.Advance(.1f);
            Check(instance.Clock.ModifierCount == 2 && Math.Abs(instance.Clock.Scale - .5f) < 1e-6 && Math.Abs(PNTimeProbeArea.Total - .05) < 1e-6, "a zone selecting the Area layer slows the area to half (" + PNTimeProbeArea.Total + ")");
            var zoneBehavior = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNTimeScaleArea>(true)).First(x => x.IsRunning);
            Check(zoneBehavior.Clock != null && zoneBehavior.Clock.ModifierCount == 0 && !zoneBehavior.AffectedByTime, "the zone has no modifier on its own clock");
            var zone2 = Zone(Vector3.zero, "'Area'"); areas.Advance(.1f);
            Check(zoneBehavior.Clock.ModifierCount == 0 && instance.Clock.ModifierCount == 4, "two overlapping zones never slow each other, they stack on the area");
            zone2.Handle.End(); areas.Advance(.3f);
            Check(instance.Clock.ModifierCount == 2, "ending a zone gives back exactly its modifiers");

            var projectileOnly = Zone(new Vector3(0, 0, 20), "'Projectile'", 30, 1);
            areas.Advance(.1f);
            Check(instance.Clock.ModifierCount == 2, "a zone that selects another layer leaves the area alone");
            projectileOnly.Handle.End(); areas.Advance(.3f);

            var free = Probe("time_test:free", new Vector3(0, 0, 1));
            Check(free.Status == PNAreaStatus.Created && PNTimeTargets.For(null).Count == baseline + 1, "an area that does not follow time stays out of the list");
            var freeInstance = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNTimeFreeProbeArea>(true)).First(x => x.IsRunning);
            PNTimeProbeArea.Total = 0; areas.Advance(.1f);
            Check(freeInstance.Clock.ModifierCount == 0 && PNTimeProbeArea.Total >= .1 - 1e-6, "an area that does not follow time keeps the game delta inside a zone");
            free.Handle.End(); areas.Advance(.1f);

            zone.Handle.End(); areas.Advance(.3f);
            Check(instance.Clock.ModifierCount == 0 && Math.Abs(instance.Clock.Scale - 1f) < 1e-6, "with the zone gone the clock is whole again");

            // a stopped area does not advance but is still advanced (so modifiers whose owner vanished are reclaimed)
            var stop = Zone(Vector3.zero, "'Area'", 3, 1); areas.Advance(.1f); PNTimeProbeArea.Total = 0; PNTimeProbeArea.Calls = 0; areas.Advance(.1f);
            Check(instance.Clock.Scale == 0f && PNTimeProbeArea.Total == 0 && PNTimeProbeArea.Calls == 1, "a full stop freezes the area but still ticks it with zero");
            stop.Handle.End(); areas.Advance(.3f);

            // ---- end and reuse ----
            var pooledCount = areas.PooledObjectCount; var again = Zone(Vector3.zero, "'Area'", 3, .5); areas.Advance(.1f);
            Check(instance.Clock.ModifierCount == 2, "a zone slows the area again");
            probe.Handle.End(); areas.Advance(.1f);
            Check(PNTimeTargets.For(null).Count == baseline && instance.Clock.ModifierCount == 0, "an ended area leaves the list and drops every modifier");
            again.Handle.End(); areas.Advance(.3f);
            var reused = Probe("time_test:probe", Vector3.zero);
            Check(reused.Status == PNAreaStatus.Created && instance.Clock.ModifierCount == 0 && instance.Clock.Scale == 1f && areas.PooledObjectCount >= 1 && pooledCount >= 1, "a pooled area starts again with a clean clock");
            reused.Handle.End(); areas.Advance(.3f);

            // ---- area_scale ----
            var reach = Probe("time_test:probe", new Vector3(5, 0, 0));
            var instance2 = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNTimeProbeArea>(true)).First(x => x.IsRunning && !(x is PNTimeFreeProbeArea));
            var plain = Zone(Vector3.zero, "'Area'", 3); areas.Advance(.1f);
            Check(instance2.Clock.ModifierCount == 0, "a zone of radius 3 does not reach an area at 5");
            plain.Handle.End(); areas.Advance(.3f);
            source.SetSource(EveId.Parse("test:area_scale_source"), new[] { new EveStatModifier(PNContentIds.AreaScale, 1d, 1, PNContentIds.Scale) });
            var doubled = Zone(Vector3.zero, "'Area'", 3, .5, PNCombatSource.Capture(source)); areas.Advance(.1f);
            var doubledBehavior = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNTimeScaleArea>(true)).First(x => x.IsRunning);
            Check(Math.Abs(doubledBehavior.Radius - 6f) < 1e-4 && instance2.Clock.ModifierCount == 2, "the creator's area_scale doubles the radius, so the zone reaches 5 (radius " + doubledBehavior.Radius + ")");
            doubled.Handle.End(); reach.Handle.End(); areas.Advance(.3f);
            Check(PNTimeTargets.For(null).Count == baseline && areas.BorrowedCount == 0, "everything ended: no entry and no borrowed area is left");
            return "PNTimeTargetsEditorChecks: PASS (" + checks + " checks)";
        }
        finally
        {
            areas?.Shutdown(); if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End();
            foreach (var go in made) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
#endif
