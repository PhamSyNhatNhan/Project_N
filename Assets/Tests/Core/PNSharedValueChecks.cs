#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Docs/SkillNetworkFrameworkPlan.md 4.7, G5, Edit Mode with real worlds: the Game glue of the shared value store. Two world instances alive at once never share a value, a world's store goes with the world,
// a skill-side write (PNSkillAction.SetShared) reaches the store only in Authority mode, a checkpoint keeps the persistent world keys across unload/restore under a new incarnation, and the run save (schema 3)
// keeps the persistent session keys. The portable part (declarations, JSON, sync block, persistence rules) is PNSharedValueTests in the smoke suite; the wire is the Player scenario.
public sealed class PNSharedChecksModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)
    {
        var values = hub.SharedValues();
        values.Register(new PNSharedValueDefinition(EveId.Parse("fixture:gate"), PNSharedScope.World, PNSharedKind.Flag, new JValue(false)));
        values.Register(new PNSharedValueDefinition(EveId.Parse("fixture:wave"), PNSharedScope.World, PNSharedKind.Number, new JValue(0), persist: true, min: 0, max: 9));
        values.Register(new PNSharedValueDefinition(EveId.Parse("fixture:gold"), PNSharedScope.Session, PNSharedKind.Number, new JValue(0), persist: true));
        values.Register(new PNSharedValueDefinition(EveId.Parse("fixture:phase"), PNSharedScope.Session, PNSharedKind.Text, new JValue("a")));
        hub.GetOrCreate<PNWorldDefinition>().Register(new PNWorldDefinition(EveId.Parse("fixture:shared_reserved_slot"), new string('d', 64), typeof(PNReservedSlotProbeProvider), () => new PNReservedSlotProbeProvider(), new PNWorldPolicy(PNEmptyWorldPolicy.Continue)));
    }
}

// A world whose content tries to take a slot id the engine reserves (a leading dollar sign): the world must fail to load, and an ordinary id must still work.
public sealed class PNReservedSlotProbeProvider : IPNWorldProvider
{
    public static string Outcome = "";
    public System.Collections.IEnumerator Load(PNWorldBuildContext context)
    {
        var root = new GameObject("reserved slot probe"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, context.PhysicsScene);
        context.Own(() => { if (root != null) UnityEngine.Object.DestroyImmediate(root); });
        try { context.AddSlot("$shared", context.PhysicsScene, new[] { root }); Outcome = "accepted"; }
        catch (InvalidOperationException) { Outcome = "refused"; }
        context.AddSlot("ordinary", context.PhysicsScene, new[] { root }); yield return null;
    }
}

public static class PNSharedValueChecks
{
    public static string Run()
    {
        var rows = new List<string>(); void Check(bool value, string label) { if (!value) throw new Exception(label); rows.Add("PASS " + label); Debug.Log("Shared PASS " + label); }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNLifetimeFixtureModule(), new PNSharedChecksModule() }, docs);
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "fixture:shared"));
        var session = EveRegistrySession.Context; var scheduler = new EveEntityPreparationScheduler(session, new EvePreparationProfile()); var worlds = new PNWorldRuntime(session, scheduler);
        var factory = new PNEntityFactory(session, worlds); var life = new PNWorldLifetimeController(session, worlds, factory, scheduler);
        var sync = new PNSyncService(hub, true, 0, session); var service = new PNSharedValueService(session, worlds, sync);
        void Pump(Func<bool> ready) { for (int i = 0; i < 2000 && !ready(); i++) scheduler.AdvanceFrame(); if (!ready()) throw new Exception("Shared value work did not drain."); }
        EveWorldContext Create(PNEmptyWorldPolicy policy) { var world = worlds.Create(EveId.Parse("fixture:lifetime_" + policy.ToString().ToLowerInvariant())); Pump(() => world.CanExecute); return world; }
        EveId gate = EveId.Parse("fixture:gate"), wave = EveId.Parse("fixture:wave"), gold = EveId.Parse("fixture:gold");
        try
        {
            var found = PNSharedValues.Find(session);
            Check(found == service.Values && service.Session.Number(gold) == 0, "the session finds the store the service attached");
            var one = Create(PNEmptyWorldPolicy.Continue); var two = Create(PNEmptyWorldPolicy.Pause);
            Check(service.WorldStores == 2 && found.WorldStore(one) != found.WorldStore(two), "each world instance gets its own store");
            Check(found.Write(gate, new JValue(true), one) == PNSharedWrite.Applied && found.Read(gate, one).Value<bool>() && !found.Read(gate, two).Value<bool>(), "a value written in one world is not seen in the other");
            Check(found.Write(wave, new JValue(5), two) == PNSharedWrite.Applied && found.Read(wave, one).Value<double>() == 0, "two worlds alive at once keep separate numbers");
            Check(found.Write(gold, new JValue(40), null) == PNSharedWrite.Applied && found.Read(gold, one).Value<double>() == 40 && found.Read(gold, two).Value<double>() == 40, "a session value is the same from every world");
            Check(found.Write(gate, new JValue(true), null) == PNSharedWrite.Ignored, "a world key with no world is refused, not written to the session");
            Check(found.Write(gold, new JValue(1), null, PNSkillMode.Predicted) == PNSharedWrite.Ignored && found.Write(gold, new JValue(1), one, PNSkillMode.Observed) == PNSharedWrite.Ignored && found.Read(gold, null).Value<double>() == 40, "predicted and observed casts cannot write");
            Check(found.Write(wave, new JValue(50), one) == PNSharedWrite.Invalid && found.Write(EveId.Parse("fixture:nothing"), new JValue(1), one) == PNSharedWrite.Unknown && found.Write(gold, new JValue("x"), one) == PNSharedWrite.Invalid, "outside the declaration (range, undeclared, wrong kind) nothing is written");
            Check(sync.Flush() >= 3, "the writes of a frame are marked and flushed as blocks");
            var storeOne = found.WorldStore(one);
            Pump(() => worlds.Unload(one));
            Check(service.WorldStores == 1 && found.WorldStore(two) != null && found.Read(wave, two).Value<double>() == 5, "unloading a world removes only its store");
            Check(found.WorldStore(one) == null, "an unloaded world has no store (no store is made for a world that is going away)");
            Check(found.Write(wave, new JValue(1), one) == PNSharedWrite.Ignored, "writing to an unloaded world is ignored");
            Check(storeOne.Set(wave, new JValue(1)) == PNSharedWrite.Ignored, "the disposed store of that world ignores writes");

            // a checkpoint keeps the persistent world key; the key that does not persist starts from its initial value
            var keep = Create(PNEmptyWorldPolicy.CheckpointUnload); var instance = keep.InstanceId;
            found.Write(wave, new JValue(7), keep); found.Write(gate, new JValue(true), keep);
            var captured = life.CaptureAndUnload(keep); Pump(() => captured.IsTerminal); if (captured.Error != null) throw new Exception(captured.Error);
            Check(keep.State == EveWorldState.Disposed && found.WorldStore(keep) == null && service.WorldStores == 1, "a checkpoint-unloaded world drops its store");
            var restored = life.Restore(instance); Pump(() => restored.IsTerminal); if (restored.Error != null) throw new Exception(restored.Error);
            Check(restored.World.Incarnation > keep.Incarnation && found.Read(wave, restored.World).Value<double>() == 7, "the persistent world key survives unload and restore under a new incarnation");
            Check(!found.Read(gate, restored.World).Value<bool>() && found.Read(wave, two).Value<double>() == 5, "a key that does not persist starts again, and the other world is untouched");
            var provided = PNSharedWorldStateProvider.WorldProviders(hub, new PNWorldPolicy());
            Check(provided.Contains(PNSharedWorldStateProvider.Id), "a world policy that names no provider still gets the shared one when a persistent world key exists");

            var probe = worlds.Create(EveId.Parse("fixture:shared_reserved_slot")); Pump(() => probe.CanExecute);
            Check(PNReservedSlotProbeProvider.Outcome == "refused" && probe.CanExecute, "content cannot take a slot id that starts with $ (reserved by the engine), ordinary ids still load");

            // the run save keeps the persistent session keys (schema 3)
            var saved = service.Session.CapturePersisted();
            Check(saved.Count == 1 && saved[gold.ToString()].Value<double>() == 40 && PNRunSave.SchemaVersion == 3, "the session part of a run save holds the persistent session keys only");
            service.Session.Set(gold, new JValue(1)); service.Session.RestorePersisted(saved);
            Check(service.Session.Number(gold) == 40, "a continued run puts the persistent session keys back");

            foreach (var world in session.Worlds.Snapshot().ToArray()) Pump(() => worlds.Unload(world));
            Check(service.WorldStores == 0 && session.Entities.Count == 0 && life.PendingCount == 0, "every store and resource drains with the worlds");
        }
        catch (Exception error) { Debug.LogError("Shared value original failure: " + error); throw; }
        finally { service.Dispose(); Check(PNSharedValues.Find(session) == null, "the service detaches itself from the session"); life.Dispose(); EveRegistrySession.End(); factory.Dispose(); }
        return "PASS " + rows.Count + " native shared value cases\n" + string.Join("\n", rows);
    }
}
#endif
