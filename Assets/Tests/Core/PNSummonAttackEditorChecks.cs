#if UNITY_EDITOR
using System;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Docs/StrikeBlastPlan.md U0-U3 on the real prefabs and the real spawn pipeline (scheduler, world, factory, local host): the Starreaper shadow is an independent summon
// (it outlives its owner and shoots two arrows), the Starforger star is a dependent turret summon (one per slot, shoots on its rhythm, ends with its owner or its lifetime).
// Edit Mode, a fixture world, nothing saved. The two summons are spawned through the host the way PNCreateSummonOperation does (its own payload checks run in the smoke suite).
public static class PNSummonAttackEditorChecks
{
    public static string Run()
    {
        if (Application.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Stopped Editor required");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Summon attack check " + n + ": " + what); n++; }
        var docs = new PNGameContentSource(Application.streamingAssetsPath + "/BaseContent", "unused", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNWorldFixtureModule() }, docs);
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:summon-attacks"));
        PNVisualPacks.Set("project_n", Application.streamingAssetsPath + "/BaseContent");
        var session = EveRegistrySession.Context; var scheduler = new EveEntityPreparationScheduler(session, new EvePreparationProfile());
        var worlds = new PNWorldRuntime(session, scheduler); var factory = new PNEntityFactory(session, worlds) { Scheduler = scheduler };
        GameObject managerObject = null;
        void Pump(Func<bool> ready, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0) { for (int i = 0; i < 3000 && !ready(); i++) scheduler.AdvanceFrame(); if (!ready()) throw new Exception("Spawn work did not drain (line " + line + ")."); }
        try
        {
            var world = worlds.Create(EveId.Parse("fixture:world")); Pump(() => world.CanExecute);
            var localHost = new PNLocalEntityHost(session, factory, scheduler); PNEntityHosts.Set(session, localHost);
            PNEntityRequest Request(string entity, string character, Vector3 at) => new PNEntityRequest { Entity = entity, Character = character, World = world, WorldIncarnation = world.Incarnation, ContentSlot = "chunk-0", Position = at, SceneKey = "fixture:scene", EntityKey = "" };
            EveEntityPreparationScheduler.Candidate Spawn(PNEntityRequest request)
            {
                var candidate = PNEntitySpawner.Enqueue(scheduler, factory, request, loader => PNEntitySpawner.AssignNew(session, loader, request), failed: error => throw error);
                Pump(() => candidate.Loader?.IsReady == true); return candidate;
            }
            (T Behavior, EveEntityPreparationScheduler.Candidate Candidate) Summon<T>(PNStat owner, string character, string group, Vector3 at, double? lifetime = null) where T : Component
            {
                PNSummon made = null; bool done = false;
                var request = Request("project_n:summon", character, at); request.Summoner = owner.EntityId; request.Lifetime = lifetime;
                var candidate = localHost.TrySpawn(request, loader =>
                {
                    var summon = loader.GetComponent<PNSummon>() ?? loader.gameObject.AddComponent<PNSummon>();
                    summon.Begin(PNCombatSource.Capture(owner), owner.EntityId, group, PNSummons.NextSequence(), 1d); made = summon;
                }, loader => done = true, error => throw error);
                if (candidate == null) throw new Exception("The host refused the summon spawn.");
                PNSummons.Reserve(owner.EntityId, group, candidate); Pump(() => done); PNSummons.Fulfil(candidate);
                return (made.GetComponent<T>(), candidate);
            }
            var playerSpawn = Spawn(factory.Player(3, world, "chunk-0", null)); var player = playerSpawn.Loader.GetComponent<PNStat>();
            player.transform.position = new Vector3(4f, 0f, 0f); Physics.SyncTransforms();
            managerObject = new GameObject("Summon attack projectiles"); SceneManager.MoveGameObjectToScene(managerObject, player.gameObject.scene);
            var projectiles = managerObject.AddComponent<PNProjectileManager>(); projectiles.Initialize(session);
            var catalog = Resources.Load<PNEntityPrefabCatalog>(PNEntityPrefabCatalog.ResourcePath);
            Check(catalog.Find("project_n:starreaper_shadow") != null && catalog.Find("project_n:starforger_star") != null, "both summon prefabs are in the entity prefab catalog");

            // ---- Starreaper shadow: independent ----
            var reaper = Spawn(Request("project_n:prop", "project_n:dummy", new Vector3(0, 0, 0))); var reaperStat = reaper.Loader.GetComponent<PNStat>();
            Check(reaperStat.IsReady && reaperStat.HasHealth, "the owner (a Health dummy standing in for the Starreaper: the real enemy needs world navigation) is ready");
            var shadowMade = Summon<PNStarreaperShadowSummon>(reaperStat, "project_n:starreaper_shadow", "starreaper_shadow", new Vector3(0, 0, 0));
            var shadow = shadowMade.Behavior; var shadowSummon = shadow.GetComponent<PNSummon>(); var shadowStat = shadow.GetComponent<PNStat>();
            var role = shadow.GetComponent<PNEntityRole>();
            Check(role != null && role.Entity == "project_n:summon" && !role.Persist && !shadowStat.HasHealth && shadow.GetComponentsInChildren<Collider>().Length == 0, "the shadow is a summon entity with no Health and no collider");
            Check(PNSummons.Count(reaperStat.EntityId, "starreaper_shadow") == 1 && ReferenceEquals(shadowSummon.OwnerSource.LoadedSource, reaperStat), "it counts in its group and keeps the owner's combat source");
            for (int i = 0; i < 19; i++) shadow.Tick(.1f);
            Check(shadow.Started && !shadow.Locked && shadow.Shots == 0 && projectiles.ActiveCount == 0, "before its 2 s delay the shadow only waits");
            shadow.Tick(.15f);
            Check(shadow.Locked && shadow.Shots == 1 && projectiles.ActiveCount == 1, "after the delay it locks the direction and shoots the first arrow");
            shadow.Tick(.05f);
            Check(shadow.Shots == 1, "the second arrow waits for its 0.15 s gap");
            shadow.Tick(.1f);
            Check(shadow.Shots == 2 && shadow.IsDissolving && projectiles.ActiveCount == 2, "the second arrow follows and the shadow dissolves");
            projectiles.Advance(10f);

            // its owner dies: an independent summon goes on
            var second = Summon<PNStarreaperShadowSummon>(reaperStat, "project_n:starreaper_shadow", "starreaper_shadow", new Vector3(0, 0, 0));
            Check(PNSummons.Count(reaperStat.EntityId, "starreaper_shadow") <= 2, "a new shadow can be summoned in the same group");
            var wasFirst = shadow; for (int i = 0; i < 6; i++) wasFirst.Tick(.1f);
            reaperStat.ReceiveDamage(player.CreateDamage(reaperStat.GetMax(PNContentIds.Health) * 10, false, 0d, PNContentIds.True));
            Check(reaperStat.IsDead, "the owner is dead");
            second.Behavior.Tick(.1f); var secondSummon = second.Behavior.GetComponent<PNSummon>(); secondSummon.Tick();
            Check(secondSummon.IsActive && !secondSummon.IsRetiring, "a dead owner does not retire an independent shadow");
            for (int i = 0; i < 21; i++) second.Behavior.Tick(.1f);
            Check(second.Behavior.Shots == 2, "the shadow of a dead owner still shoots both arrows (" + second.Behavior.Shots + ")");

            // ---- Starforger star: dependent ----
            var forger = Spawn(Request("project_n:prop", "project_n:dummy", new Vector3(0, 0, 0))); var forgerStat = forger.Loader.GetComponent<PNStat>();
            Check(forgerStat.IsReady && forgerStat.HasHealth, "the second owner is ready");
            player.transform.position = new Vector3(2f, 0f, 0f); Physics.SyncTransforms();
            var star0 = Summon<PNStarforgerStarSummon>(forgerStat, "project_n:starforger_star", PNStarforgerCombatContent.SlotGroup(0), new Vector3(1.2f, 0.4f, 0), 16d);
            var star1 = Summon<PNStarforgerStarSummon>(forgerStat, "project_n:starforger_star", PNStarforgerCombatContent.SlotGroup(1), new Vector3(-.6f, 0.4f, 1.04f), 16d);
            star0.Behavior.Tick(.1f); star1.Behavior.Tick(.1f);
            Check(star0.Behavior.Slot == 0 && star1.Behavior.Slot == 1 && PNSummons.Of(forgerStat.EntityId).Count(x => PNStarforgerCombatContent.TrySlot(x.Group, out _)) == 2, "each star takes the slot of its group and the index counts both");
            Check(star0.Behavior.GetComponentsInChildren<Collider>().Length == 0 && !star0.Behavior.GetComponent<PNStat>().HasHealth, "a star has no Health and no collider");
            for (int i = 0; i < 9; i++) star0.Behavior.Tick(.1f);
            Check(star0.Behavior.Shots == 1 && projectiles.ActiveCount >= 1, "the first star shoots after its 1 s first delay (" + star0.Behavior.Shots + ")");
            for (int i = 0; i < 9; i++) star1.Behavior.Tick(.1f);
            Check(star1.Behavior.Shots == 0, "the second star waits its 0.667 s stagger longer");
            for (int i = 0; i < 7; i++) star1.Behavior.Tick(.1f);
            Check(star1.Behavior.Shots == 1, "and then shoots (" + star1.Behavior.Shots + ")");
            Check(star0.Behavior.FireToward(new Vector3(3f, 0f, 0f)) && star0.Behavior.Shots == 2, "an extra shot toward the slam's landing point is allowed");

            // lifetime ends a star; the owner's death ends the other
            star0.Behavior.GetComponent<PNEntityLifetime>().Advance(16.5); Pump(() => star0.Candidate.IsReturned);
            Check(PNSummons.Count(forgerStat.EntityId, PNStarforgerCombatContent.SlotGroup(0)) == 0 && PNSummons.Of(forgerStat.EntityId).Count() == 1, "a star's lifetime retires it through the host and frees its slot");
            forgerStat.ReceiveDamage(player.CreateDamage(forgerStat.GetMax(PNContentIds.Health) * 10, false, 0d, PNContentIds.True));
            Check(forgerStat.IsDead, "the Starforger is dead");
            star1.Behavior.GetComponent<PNSummon>().Tick(); Pump(() => star1.Candidate.IsReturned);
            Check(PNSummons.LiveCount == 0 || PNSummons.Count(forgerStat.EntityId, PNStarforgerCombatContent.SlotGroup(1)) == 0, "the owner's death retires the remaining star (a dependent summon)");
            return "PASS " + n + " summon attack checks (Starreaper shadow independent of its owner; Starforger stars dependent: slots, rhythm, lifetime, owner death; real prefabs, catalog and host spawn)";
        }
        finally
        {
            try { if (managerObject != null) { managerObject.GetComponent<PNProjectileManager>()?.Shutdown(); UnityEngine.Object.DestroyImmediate(managerObject); } } catch (Exception) { }
            try { foreach (var live in session.Worlds.Snapshot().ToArray()) Pump(() => worlds.Unload(live)); } catch (Exception) { }
            PNEntityHosts.Remove(session); EveRegistrySession.End();
            try { factory.Dispose(); } catch (Exception) { }
            PNVisualRuntime.Release(); PNVisualPacks.Clear();
        }
    }
}
#endif
