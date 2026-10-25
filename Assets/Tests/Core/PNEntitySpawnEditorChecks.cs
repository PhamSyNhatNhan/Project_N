#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// A prop whose character declares no Health (the stat class only says which character it is).
public sealed class PNCrateStat : PNEnemyStat { public override string CharacterDefinitionId => "test:crate"; }
// Content for the factory checks: a mod-registered prefab, a character without Health that names it, and an entity role with a spawn Buff.
public sealed class PNEntitySpawnFixtureModule : EveIContentModule
{
    public static GameObject Crate, Dummy;
    public void Register(EveRegistryHub hub)
    {
        if (Crate != null) UnityEngine.Object.DestroyImmediate(Crate);
        Crate = new GameObject("crate prefab"); Crate.SetActive(false); Crate.hideFlags = HideFlags.HideAndDontSave;
        Crate.AddComponent<EveEntityIdentity>(); Crate.AddComponent<PNCrateStat>(); Crate.AddComponent<EveEntityLoader>(); Crate.AddComponent<BoxCollider>();
        PNEntityPrefabs.Register("mymod:crate", Crate, "mymod");
        hub.GetOrCreate<EveCharacterDefinition>().Register(new EveCharacterDefinition(EveId.Parse("test:crate"), new[] { new EveInitialStat(PNContentIds.AttackPower, 1d) }, null, "", JObject.Parse("{'body':{'prefab':'mymod:crate','loot':'mymod:crate_loot'}}")));
        // An enemy body without skill or movement (those need prepared world navigation): the stat, the loader and a collider, for the pool reuse checks.
        if (Dummy != null) UnityEngine.Object.DestroyImmediate(Dummy);
        Dummy = new GameObject("dummy enemy prefab"); Dummy.SetActive(false); Dummy.hideFlags = HideFlags.HideAndDontSave;
        Dummy.AddComponent<EveEntityIdentity>(); Dummy.AddComponent<PNEnemyStat>(); Dummy.AddComponent<EveEntityLoader>(); Dummy.AddComponent<BoxCollider>();
        PNEntityPrefabs.Register("mymod:dummy_body", Dummy, "mymod");
        var enemyRole = hub.Instances().GetKind<PNEntityDefinition>(PNEntityGameplayContent.Enemy); var enemyData = (JObject)enemyRole.Payload; enemyData["prefab"] = "mymod:dummy_body";
        hub.Instances().Register(enemyRole.Derive(EveId.Parse("test:dummy_enemy"), enemyData, null));
        var prop = hub.Instances().GetKind<PNEntityDefinition>(PNEntityGameplayContent.Prop);
        var data = (JObject)prop.Payload; data["spawnBuffs"] = new JArray("base:buff_stat_template");
        hub.Instances().Register(prop.Derive(EveId.Parse("test:guard"), data, null));
    }
}

// EN: Native checks of the entity spawn plan (Docs/EntitySpawnPlan.md). Catalog part (E3): the moved asset keeps its GUID and entries, old and
//     namespaced keys resolve to the same prefab, content load rejects a missing prefab, mod registration rules, the prefab structure fingerprint
//     (changes with gameplay structure, ignores looks) and the coop hash entry. Spawning parts are added by later steps.
// VI: Kiểm native của plan sinh entity. Phần catalog: asset đã dời giữ GUID, khoá cũ và khoá có namespace ra cùng prefab, nạp nội dung từ chối prefab
//     thiếu, luật đăng ký của mod, vân tay cấu trúc prefab và mục băm coop.
public static class PNEntitySpawnEditorChecks
{
    private const string CatalogGuid = "f422052d04e9b3e499e326ac5007f3fa";

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        int count = 0; var evidence = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); evidence.Add("PASS " + message); count++; }
        void Reject(Action action, string label) { try { action(); } catch (Exception) { Check(true, label); return; } throw new Exception("Expected rejection: " + label); }
        var preview = EditorSceneManager.NewPreviewScene(); var fixtures = new List<GameObject>();
        try
        {
            Catalog(Check, Reject, preview, fixtures);
            Factory(Check, Reject);
        }
        finally
        {
            PNEntityPrefabs.CatalogOverride = null;
            foreach (var fixture in fixtures) if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
            EditorSceneManager.ClosePreviewScene(preview);
            if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End();
        }
        Check(SceneManager.GetActiveScene() == original && original.isDirty == dirty, "Authored scene and dirty state preserved.");
        return "PASS " + count + " native entity spawn checks";
    }

    private static void Catalog(Action<bool, string> Check, Action<Action, string> Reject, Scene preview, List<GameObject> fixtures)
    {
        // The asset moved without losing its identity.
        Check(AssetDatabase.GUIDToAssetPath(CatalogGuid) == "Assets/Resources/PNEntities/PNEntityPrefabs.asset" && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/Resources/PNCharacters/PNPlayerPrefabs.asset") == null,
            "the catalog asset moved to Resources/PNEntities/PNEntityPrefabs keeping its GUID, and the old path is gone");
        var catalog = Resources.Load<PNEntityPrefabCatalog>(PNEntityPrefabCatalog.ResourcePath);
        Check(catalog != null && catalog.Entries.Length >= 1, "the catalog loads from Resources by its resource path");
        var eve2 = catalog.Find("project_n:eve_2");
        Check(eve2 != null && ReferenceEquals(catalog.Find("eve_2"), eve2), "the namespaced key and the old key without a namespace resolve to the same prefab");
        Check(PNEntityPrefabs.TryFind("eve_2", out var viaStatic) && ReferenceEquals(viaStatic, eve2) && !PNEntityPrefabs.TryFind("project_n:no_such", out _) && !PNEntityPrefabs.TryFind("mymod:eve_2", out _), "TryFind finds the catalog prefab and nothing else");
        Check(eve2.GetComponent<EveEntityLoader>() != null && eve2.GetComponent<EveEntityIdentity>() != null, "the Eve 2 prefab is a loadable entity (loader and identity)");

        // Content load validates every key content refers to.
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        EveIContentModule[] Modules() => new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() };
        var hub = new EveContentBootstrap().Build(Modules(), docs);
        Check(PNEntityPrefabReferences.Collect(hub).ContainsKey("project_n:eve_2"), "the shipped content refers to project_n:eve_2 and the real content loads with it in the catalog");
        PNEntityPrefabs.CatalogOverride = ScriptableObject.CreateInstance<PNEntityPrefabCatalog>();
        try
        {
            string message = null;
            try { new EveContentBootstrap().Build(Modules(), docs); } catch (Exception error) { message = error.Message; }
            Check(message != null && message.Contains("project_n:eve_2"), "content load rejects a character whose prefab the catalog does not hold (" + message + ")");
        }
        finally { UnityEngine.Object.DestroyImmediate(PNEntityPrefabs.CatalogOverride); PNEntityPrefabs.CatalogOverride = null; }
        hub = new EveContentBootstrap().Build(Modules(), docs);

        // Mod registration rules.
        PNEntityPrefabs.Register("mymod:golem", eve2, "mymod");
        Check(PNEntityPrefabs.TryFind("mymod:golem", out var golem) && ReferenceEquals(golem, eve2), "a mod registers a prefab inside its own namespace");
        Reject(() => PNEntityPrefabs.Register("mymod:golem", eve2, "mymod"), "a duplicate key is rejected");
        Reject(() => PNEntityPrefabs.Register("other:golem", eve2, "mymod"), "a mod cannot register a key of another namespace");
        Reject(() => PNEntityPrefabs.Register("project_n:golem", eve2, "project_n"), "the base namespace cannot be registered by a mod");
        Reject(() => PNEntityPrefabs.Register("mymod", eve2, "mymod"), "a key needs a namespace");
        Reject(() => PNEntityPrefabs.Register("mymod:Bad Key", eve2, "mymod"), "a malformed key is rejected");
        var plain = new GameObject("plain prefab without entity components"); fixtures.Add(plain);
        Reject(() => PNEntityPrefabs.Register("mymod:plain", plain, "mymod"), "a prefab without entity loader and identity is rejected");
        Reject(() => PNEntityPrefabs.Register("mymod:null", null, "mymod"), "a null prefab is rejected");
        new EveContentBootstrap().Build(Modules(), docs);
        Check(!PNEntityPrefabs.TryFind("mymod:golem", out _), "starting a new content build clears the previous build's mod prefabs");
        PNEntityPrefabs.Register("mymod:golem", eve2, "mymod");

        // Which prefab a spawn uses (needs a session for the character registry).
        var compatibility = EveContentCompatibility.Create(hub, docs, "test:entity_prefabs");
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, compatibility);
        try
        {
            var session = EveRegistrySession.Context;
            var fallback = new GameObject("spawn point prefab"); fixtures.Add(fallback);
            Check(ReferenceEquals(PNEntityPrefabs.For(session, "project_n:eve_2", fallback), eve2), "a character that names a prefab spawns from the catalog");
            Check(ReferenceEquals(PNEntityPrefabs.For(session, "project_n:eve", fallback), fallback) && ReferenceEquals(PNEntityPrefabs.For(session, "", fallback), fallback), "a character that names none spawns from the spawn point's prefab");
            var data = new JObject { ["owner"] = "host", ["persist"] = true, ["followsOwner"] = false, ["lifetime"] = JValue.CreateNull(), ["countsForClear"] = false, ["dropsLoot"] = false, ["prefab"] = "mymod:golem" };
            var entity = new PNEntityDefinition(EveId.Parse("test:golem_entity"), typeof(PNHostEntityRecipe), () => new PNHostEntityRecipe(), data);
            Check(ReferenceEquals(PNEntityPrefabs.For(session, "project_n:eve", fallback, entity), eve2), "an entity that names a prefab wins over the character's");
            data["prefab"] = "mymod:missing";
            Reject(() => PNEntityPrefabs.For(session, "project_n:eve", fallback, new PNEntityDefinition(EveId.Parse("test:missing_entity"), typeof(PNHostEntityRecipe), () => new PNHostEntityRecipe(), data)), "an entity naming a prefab that is not registered is rejected at spawn");
        }
        finally { EveRegistrySession.End(); }

        // The fingerprint follows gameplay structure and ignores looks.
        GameObject Copy() { var obj = UnityEngine.Object.Instantiate(eve2); obj.name = eve2.name; SceneManager.MoveGameObjectToScene(obj, preview); fixtures.Add(obj); return obj; }
        string baseline = PNEntityPrefabs.Fingerprint(eve2);
        var copy = Copy();
        Check(PNEntityPrefabs.Fingerprint(copy) == baseline && PNEntityPrefabs.Fingerprint(eve2) == baseline && baseline.Length == 64, "the fingerprint is stable and the same for a clone of the prefab");
        copy.transform.position += new Vector3(4, 1, 2); foreach (var renderer in copy.GetComponentsInChildren<Renderer>(true)) renderer.enabled = !renderer.enabled;
        Check(PNEntityPrefabs.Fingerprint(copy) == baseline, "moving the object and toggling renderers (looks) does not change it");
        var collider = copy.GetComponentsInChildren<Collider>(true).FirstOrDefault();
        Check(collider != null, "the Eve 2 prefab has a collider to edit");
        if (collider is BoxCollider box) box.size *= 1.5f; else if (collider is CapsuleCollider capsule) capsule.radius *= 1.5f; else if (collider is SphereCollider sphere) sphere.radius *= 1.5f;
        string resized = PNEntityPrefabs.Fingerprint(copy);
        Check(resized != baseline, "changing a collider size changes it");
        var layered = Copy(); layered.layer = layered.layer == 0 ? 9 : 0;
        Check(PNEntityPrefabs.Fingerprint(layered) != baseline, "changing the layer changes it");
        var extra = Copy(); extra.AddComponent<AudioSource>();
        Check(PNEntityPrefabs.Fingerprint(extra) != baseline, "adding a component changes it");
        var child = Copy(); new GameObject("extra child").transform.SetParent(child.transform, false);
        Check(PNEntityPrefabs.Fingerprint(child) != baseline, "adding a child object changes it");
        var body = Copy(); var rigid = body.GetComponent<Rigidbody>() ?? body.AddComponent<Rigidbody>(); string withBody = PNEntityPrefabs.Fingerprint(body); rigid.isKinematic = !rigid.isKinematic;
        Check(PNEntityPrefabs.Fingerprint(body) != withBody, "a Rigidbody's kinematic setting changes it");

        // The coop hash carries one entry per referenced prefab.
        var hashes = PNEntityPrefabReferences.Hashes(hub, PNEntityPrefabs.Source);
        Check(hashes.Count == 1 && hashes["entity_prefab:project_n:eve_2"] == baseline, "the coop hash entry for project_n:eve_2 is the prefab fingerprint");
    }

    // E4: the factory builds entities from the instances registry, the catalog and the spawn points, and keeps the old behavior for players.
    private static void Factory(Action<bool, string> Check, Action<Action, string> Reject)
    {
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNWorldFixtureModule(), new PNLifetimeFixtureModule(), new PNEntitySpawnFixtureModule() }, docs);
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:entity_factory"));
        var session = EveRegistrySession.Context; var scheduler = new EveEntityPreparationScheduler(session, new EvePreparationProfile());
        var worlds = new PNWorldRuntime(session, scheduler); var factory = new PNEntityFactory(session, worlds) { Scheduler = scheduler };
        void Pump(Func<bool> ready) { for (int i = 0; i < 2000 && !ready(); i++) scheduler.AdvanceFrame(); if (!ready()) throw new Exception("Spawn work did not drain."); }
        EveEntityPreparationScheduler.Candidate Spawn(PNEntityRequest request)
        {
            var candidate = PNEntitySpawner.Enqueue(scheduler, factory, request, loader => PNEntitySpawner.AssignNew(session, loader, request), failed: error => throw error);
            Pump(() => candidate.Loader?.IsReady == true); return candidate;
        }
        try
        {
            var world = worlds.Create(EveId.Parse("fixture:world")); Pump(() => world.CanExecute);
            var slot0 = worlds.Get(world).Slots.Single(x => x.Id == "chunk-0"); var slot1 = worlds.Get(world).Slots.Single(x => x.Id == "chunk-1");
            var eveSpawn = slot0.Roots.SelectMany(x => x.GetComponentsInChildren<PNPlayerSpawnPoint>(true)).Single();
            var eve2Spawn = slot1.Roots.SelectMany(x => x.GetComponentsInChildren<PNPlayerSpawnPoint>(true)).Single(); eve2Spawn.Character = "project_n:eve_2";

            // A player spawns as before: the spawn point's prefab for Eve, the catalog prefab for Eve 2, the old template name still carried.
            var request = factory.Player(0, world, "chunk-0", null);
            Check(PNEntityRoles.IsPlayer(EveRegistrySession.Context.Registry, request.Entity) && request.Template == "base:scene_spawn" && request.Entity == "base:scene_spawn" && request.Character == "project_n:eve", "Player() builds a request for the old player template, also named as the entity role");
            var eve = Spawn(request); var marker = eve.Loader.GetComponent<PNFactoryEntity>();
            Check(eve.Loader.GetComponent<PNPlayerStat>() != null && marker.Entity == "base:scene_spawn" && marker.Template == "base:scene_spawn" && ReferenceEquals(marker.Prototype, eveSpawn.Prefab),
                "Eve spawns from the spawn point's own prefab and the marker records entity, template and prototype");
            Check(Vector3.Distance(eve.Loader.transform.position, eveSpawn.transform.position) < .01f && eve.Loader.GetComponent<EveEntityIdentity>().World == world, "the player is placed at its spawn point inside its world");
            var eve2Request = factory.Player(1, world, "chunk-1", null); var eve2 = Spawn(eve2Request); var catalogEve2 = Resources.Load<PNEntityPrefabCatalog>(PNEntityPrefabCatalog.ResourcePath).Find("eve_2");
            Check(eve2Request.Character == "project_n:eve_2" && ReferenceEquals(eve2.Loader.GetComponent<PNFactoryEntity>().Prototype, catalogEve2) && !ReferenceEquals(catalogEve2, eve2Spawn.Prefab), "Eve 2 spawns from the catalog prefab, not the spawn point's");

            // Roles without a spawn point: a prop whose character has no Health and names a mod prefab.
            PNEntityRequest PropIn(EveWorldContext into, string entity, string character = "test:crate", Vector3? position = null) => new PNEntityRequest { Entity = entity, Character = character, World = into, WorldIncarnation = into.Incarnation, ContentSlot = "chunk-0", Position = position ?? new Vector3(3, 1, 3), SceneKey = "fixture:scene", EntityKey = "" };
            PNEntityRequest Prop(string entity, string character = "test:crate", Vector3? position = null) => PropIn(world, entity, character, position);
            var crate = Spawn(Prop("project_n:prop")); var crateStat = crate.Loader.GetComponent<PNStat>();
            Check(!crateStat.HasHealth && !crateStat.IsDead && crate.Loader.GetComponent<PNFactoryEntity>().Entity == "project_n:prop" && ReferenceEquals(crate.Loader.GetComponent<PNFactoryEntity>().Prototype, PNEntitySpawnFixtureModule.Crate),
                "a prop spawns from a mod-registered prefab with no spawn point and no Health");
            Check(Vector3.Distance(crate.Loader.transform.position, new Vector3(3, 1, 3)) < .01f && crate.Loader.GetComponent<EveEntityIdentity>().World == world, "an entity without a spawn point is placed at the request position");
            var hit = crateStat.ReceiveDamage(eve.Loader.GetComponent<PNStat>().CreateDamage(50, false, PNContentIds.True));
            Check(hit.Ignored && !crateStat.IsDead, "damage to the spawned prop is ignored (not damageable)");
            var guard = Spawn(Prop("test:guard", position: new Vector3(5, 1, 5)));
            // Edit Mode does not call OnEnable, so the Buff manager of an entity published here stays suspended and the queued Apply resolves Rejected;
            // the receipt shows the role did try. The Buff really landing is checked in the Player run (PNEnemyFollowupPlayReview).
            guard.Loader.GetComponent<PNBuffManager>().Advance(0);
            var guardReceipts = guard.Loader.GetComponent<PNFactoryEntity>().SpawnBuffReceipts;
            Check(guardReceipts.Count == 1 && guardReceipts[0].Status != PNBuffStatus.Failed, "an entity role with spawnBuffs asks the Buff manager to apply them once it is published (receipts " + guardReceipts.Count + ")");
            Check(crate.Loader.GetComponent<PNFactoryEntity>().SpawnBuffReceipts.Count == 0, "a plain prop role applies no spawn Buffs");


            // Role flags kept on the object (E6): the role component carries every flag, the unused ones included.
            var propRole = crate.Loader.GetComponent<PNEntityRole>();
            Check(propRole != null && propRole.Entity == "project_n:prop" && propRole.Owner == PNEntityOwner.Host && propRole.Persist && !propRole.FollowsOwner && !propRole.CountsForClear && !propRole.DropsLoot
                && propRole.Loot == EveId.Parse("mymod:crate_loot") && !propRole.Lifetime.HasValue && crate.Loader.GetComponent<PNEntityLifetime>() == null, "a prop carries its role flags, the character's loot id and no lifetime");
            var enemyRole = Spawn(Prop("project_n:enemy", position: new Vector3(7, 1, 7))).Loader.GetComponent<PNEntityRole>();
            Check(enemyRole.CountsForClear && enemyRole.DropsLoot && enemyRole.Owner == PNEntityOwner.Host, "the enemy role keeps counts-for-clear and drops-loot (read by nothing yet)");
            var eveRole = eve.Loader.GetComponent<PNEntityRole>();
            Check(eveRole != null && eveRole.Owner == PNEntityOwner.Peer && eveRole.Persist && eveRole.FollowsOwner && eveRole.Entity == "base:scene_spawn", "the player carries the peer-owned role under its old template name");

            // Lifetime: removed through the scheduler's retire path after its own seconds.
            var shortLived = Prop("project_n:prop", position: new Vector3(9, 1, 9)); shortLived.Lifetime = 1.5;
            var timed = Spawn(shortLived); var timedLoader = timed.Loader; var life = timedLoader.GetComponent<PNEntityLifetime>();
            Check(life != null && life.Armed && Math.Abs(life.Remaining - 1.5) < 1e-9 && timedLoader.GetComponent<PNEntityRole>().Lifetime == 1.5, "a request lifetime arms the lifetime component");
            life.Advance(1.0);
            Check(!life.Expired && timedLoader.IsReady && !timed.IsReturned && Math.Abs(life.Remaining - .5) < 1e-9, "before it runs out the entity stays");
            life.Advance(.6);
            Check(life.Expired && !life.Armed, "when it runs out the lifetime expires once");
            Pump(() => timed.IsReturned);
            Check(timed.IsReturned && !session.Entities.Snapshot(world).Contains(timedLoader), "the expired entity is retired and returned through the scheduler, not destroyed");
            var reused = Spawn(Prop("project_n:prop", position: new Vector3(9, 1, 9))); var reusedLife = reused.Loader.GetComponent<PNEntityLifetime>();
            Check((reusedLife == null || !reusedLife.Armed) && !reused.Loader.GetComponent<PNEntityRole>().Lifetime.HasValue, "a pooled instance reused for a role without a lifetime keeps no lifetime (same object reused: " + ReferenceEquals(reused.Loader, timedLoader) + ")");

            // Pool reuse (E7): an enemy and a player that were hurt, marked and retired come back as new when their pooled object is reused.
                        var first = Spawn(PropIn(world, "test:dummy_enemy", "project_n:dummy_lorica", new Vector3(11, 1, 11)));
            var sentinel = first.Loader.GetComponent<PNEnemyStat>(); var sentinelLoader = first.Loader;
            double fullHealth = sentinel.GetMax(PNContentIds.Health), fullLorica = sentinel.GetMax(PNContentIds.Lorica);
            string firstBehavior = first.Loader.GetComponent<PNEnemySkill>()?.ActiveBehaviorId; int firstSources = sentinel.Set.SourceSnapshot().Count;
            var attacker = eve.Loader.GetComponent<PNStat>();
            sentinel.SetSource(EveId.Parse("test:dirty"), new[] { new EveStatModifier(PNContentIds.AttackPower, 50d, 0) });
            sentinel.ReceiveDamage(attacker.CreateDamage(fullHealth * .4, false, PNContentIds.True));
            var body = first.Loader.GetComponent<Rigidbody>(); if (body != null && !body.isKinematic) body.linearVelocity = new Vector3(5, 0, 5);
            sentinel.ReceiveDamage(attacker.CreateDamage(fullHealth * 10, false, 0d, PNContentIds.True));
            Check(sentinel.IsDead, "the enemy is hurt, marked with a stat source and killed before it is retired");
            scheduler.RetirePublished(sentinelLoader); Pump(() => first.IsReturned);
            var second = Spawn(PropIn(world, "test:dummy_enemy", "project_n:dummy_lorica", new Vector3(13, 1, 13))); var again = second.Loader.GetComponent<PNEnemyStat>();
            Check(ReferenceEquals(second.Loader, sentinelLoader), "the second enemy is the first one's pooled object (the reuse path is exercised)");
            Check(!again.IsDead && again.GetCurrent(PNContentIds.Health) == fullHealth && again.IFrameRemaining == 0d && again.GetCurrent(PNContentIds.Lorica) == fullLorica && !again.Set.SourceSnapshot().ContainsKey(EveId.Parse("test:dirty")) && again.Set.SourceSnapshot().Count == firstSources,
                "the reused enemy has full health and lorica, no iframe and none of the previous stat sources");
            var reusedBody = second.Loader.GetComponent<Rigidbody>();
            Check(Vector3.Distance(second.Loader.transform.position, new Vector3(13, 1, 13)) < .01f && (reusedBody == null || reusedBody.isKinematic || reusedBody.linearVelocity.sqrMagnitude < 1e-6f), "the reused enemy is at its new position and keeps no velocity");
            Check(second.Loader.GetComponent<PNEnemySkill>()?.ActiveBehaviorId == firstBehavior && second.Loader.GetComponent<PNBuffManager>().Buffs.Snapshot().Count == 0, "the reused enemy starts with the default behavior and no Buff");
            var playerSpawn = Spawn(factory.Player(3, world, "chunk-0", null)); var playerLoader = playerSpawn.Loader; var playerStat = playerLoader.GetComponent<PNStat>();
            double playerHealth = playerStat.GetMax(PNContentIds.Health), playerStart = playerStat.GetCurrent(PNContentIds.Health);
            playerStat.ReceiveDamage(again.CreateDamage(playerHealth * .5, false, PNContentIds.True));
            playerStat.SetSource(EveId.Parse("test:dirty"), new[] { new EveStatModifier(PNContentIds.AttackPower, 50d, 0) });
            double playerHurt = playerStat.GetCurrent(PNContentIds.Health);
            scheduler.RetirePublished(playerLoader); Pump(() => playerSpawn.IsReturned);
            var playerAgain = Spawn(factory.Player(3, world, "chunk-0", null)); var freshStat = playerAgain.Loader.GetComponent<PNStat>();
            Check(ReferenceEquals(playerAgain.Loader, playerLoader) && freshStat.GetCurrent(PNContentIds.Health) == playerStart && playerHurt == 0d && freshStat.IFrameRemaining == 0d && !freshStat.Set.SourceSnapshot().ContainsKey(EveId.Parse("test:dirty")),
                "a player who died and whose pooled object is reused starts again with the character's initial health, no iframe and none of the previous stat sources (same object " + ReferenceEquals(playerAgain.Loader, playerLoader) + ", start " + playerStart + ", hurt " + playerHurt + ", health " + freshStat.GetCurrent(PNContentIds.Health) + "/" + playerHealth + ", iframe " + freshStat.IFrameRemaining + ", dirty source " + freshStat.Set.SourceSnapshot().ContainsKey(EveId.Parse("test:dirty")) + ")");

            // Rejections.
            Reject(() => factory.Get(Prop("project_n:prop", "project_n:dummy")), "an entity whose character names no prefab and has no spawn point is rejected");
            Reject(() => factory.Get(Prop("mymod:nothing")), "an unknown entity role is rejected");
            var mismatch = Prop("project_n:prop"); mismatch.Template = "project_n:enemy";
            Reject(() => factory.Get(mismatch), "Entity and Template naming different roles are rejected");
            Reject(() => factory.Get(Prop("project_n:prop", "test:no_such_character")), "an unknown character is rejected");


            // Checkpoint: an entity of a role that does not persist (summon) is not written; a persistent one (prop) is written and comes back.
            var checkpointController = new PNWorldLifetimeController(session, worlds, factory, scheduler);
            try
            {
                var cpWorld = worlds.Create(EveId.Parse("fixture:lifetime_checkpointunload")); Pump(() => cpWorld.CanExecute);
                Spawn(PropIn(cpWorld, "project_n:prop", position: new Vector3(2, 1, 2))); var summoned = Spawn(PropIn(cpWorld, "project_n:summon", position: new Vector3(4, 1, 2)));
                Check(summoned.Loader.GetComponent<PNEntityRole>().Persist == false && session.Entities.Snapshot(cpWorld).Count == 2, "a summon and a prop are resident in the checkpoint world");
                var capture = checkpointController.CaptureAndUnload(cpWorld); Pump(() => capture.IsTerminal);
                if (capture.Error != null) throw new Exception(capture.Error);
                var saved = (JObject)JArray.Parse(checkpointController.GetCheckpoint(cpWorld.InstanceId).Data.Json)[0]; var actors = (JArray)saved["actors"];
                Check(actors.Count == 1 && (string)actors[0]["template"] == "project_n:prop" && (string)actors[0]["character"] == "test:crate", "the checkpoint holds the prop and leaves the summon out (persist false)");
                var restored = checkpointController.Restore(cpWorld.InstanceId); Pump(() => restored.IsTerminal);
                if (restored.Error != null) throw new Exception(restored.Error);
                var back = session.Entities.Snapshot(restored.World);
                Check(back.Count == 1 && back[0].GetComponent<PNEntityRole>().Entity == "project_n:prop" && back[0].GetComponent<PNStat>().CharacterId == EveId.Parse("test:crate") && ReferenceEquals(back[0].GetComponent<PNFactoryEntity>().Prototype, PNEntitySpawnFixtureModule.Crate),
                    "restoring the checkpoint brings the prop back from its role, character and the mod prefab");
                // Saves: the old role name still imports in the same rules version; a save from another rules version or with an unknown role is refused.
                var snapshot = checkpointController.CaptureSnapshot(restored.World); Pump(() => snapshot.IsTerminal);
                if (snapshot.Error != null) throw new Exception(snapshot.Error);
                var envelope = checkpointController.Export(snapshot.Snapshot);
                JObject Variant(Action<JObject> change) { var document = envelope.Document; change(document); return document; }
                PNWorldSaveEnvelope Wrap(JObject document) => new PNWorldSaveEnvelope(document.ToString(Newtonsoft.Json.Formatting.None));
                var oldName = Variant(d => ((JArray)d["state"][0]["actors"])[0]["template"] = "base:scene_spawn");
                checkpointController.Import(Wrap(oldName));
                Check(true, "a save that names its entity role with the old template name (base:scene_spawn) still imports in the same rules version");
                Reject(() => checkpointController.Import(Wrap(Variant(d => d["rules"] = "project_n:99"))), "a save written under another rules version (such as 99, before the entity change) is refused whole");
                Reject(() => checkpointController.Import(Wrap(Variant(d => ((JArray)d["state"][0]["actors"])[0]["template"] = "mymod:no_such_role"))), "a save naming an unknown entity role is refused");
            }
            finally { checkpointController.Dispose(); }

            foreach (var live in session.Worlds.Snapshot().ToArray()) Pump(() => worlds.Unload(live));
            Check(session.Entities.Count == 0, "world unload returns every spawned entity (no leaked entity)");
        }
        catch (Exception error) { Debug.LogError("Entity factory checks original failure: " + error); throw; }
        finally
        {
            // A failed check must not leave a session or entities behind for the next step.
            try { foreach (var live in session.Worlds.Snapshot().ToArray()) Pump(() => worlds.Unload(live)); } catch (Exception) { }
            EveRegistrySession.End();
            try { factory.Dispose(); } catch (Exception) { }
        }
    }
}

// Batch entry for the clone project: the entity spawn checks plus the world checks that use the entity factory.
public static class PNEntitySpawnBatch
{
    public static void Run()
    {
        var lines = new List<string>(); bool failed = false;
        void Step(string name, Func<string> run)
        {
            try { lines.Add(name + ": " + run()); }
            catch (Exception error) { failed = true; lines.Add(name + ": FAIL " + error.Message); Debug.LogException(error); }
        }
        Step("EntitySpawn", PNEntitySpawnEditorChecks.Run);
        Step("WorldLifecycle", PNWorldLifecycleChecks.Run);
        Step("WorldLifetime", PNWorldLifetimeChecks.Run);
        Step("WorldTransfer", PNWorldTransferChecks.Run);
        Step("WorldExtension", PNWorldExtensionChecks.Run);
        Directory.CreateDirectory(".utmp/EntitySpawnReview");
        File.WriteAllLines(".utmp/EntitySpawnReview/Batch.txt", lines);
        EditorApplication.Exit(failed ? 1 : 0);
    }
}
#endif
