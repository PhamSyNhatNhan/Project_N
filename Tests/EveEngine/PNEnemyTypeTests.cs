using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static EveSessionSmokeTests;

public class PNEnemyTypeProbeModule : PNContentModule
{
    public override void Register(EveRegistryHub hub)
    {
        base.Register(hub);
        hub.EnemyTypes().Register(new PNEnemyTypeDefinition(EveId.Parse("addon:champion"), "Champion"));
    }
}

public static class PNEnemyTypeTests
{
    private static EveId Id(string value) => EveId.Parse(value);
    private static EveContentDocument Document(string field, string[] dependencies = null) => new EveContentDocument(
        "enemy.json", "{schemaVersion:1,characters:[{id:'test:enemy',stats:[{statId:'base:health',base:100,initialCurrent:100}]" + field + "}]}",
        "test", dependencies: dependencies ?? new[] { "project_n", "addon" });
    private static EveRegistryHub Build(string field = "", string[] dependencies = null) => new EveContentBootstrap().Build(
        new EveIContentModule[] { new PNEnemyTypeProbeModule() }, new[] { Document(field, dependencies) });
    private static EveRegistryHub BuildReference(string field, string[] dependencies)
    {
        // No stat references: failures here must originate from enemy classification.
        var json = JObject.Parse(Document(field).Json);
        json["characters"][0]["stats"] = new JArray();
        return new EveContentBootstrap().Build(new EveIContentModule[] { new PNEnemyTypeProbeModule() },
            new[] { new EveContentDocument("classification_reference.json", json.ToString(), "test", dependencies: dependencies) });
    }

    public static void Run()
    {
        var hub = Build();
        Check(hub.EnemyTypes().Count == 4 && hub.EnemyTypes().IsSealed, "Enemy types include three originals and extensible code-mod content");
        Check(ReferenceEquals(hub.Bindings("enemy_types"), hub.EnemyTypes()), "Enemy types have their own named registry");
        foreach (var name in new[] { "normal", "elite", "boss" })
            Check(hub.EnemyTypes().Get(Id("base:" + name)).Id == Id("project_n:" + name), "Enemy type aliases resolve to stable original identities");
        var open = new EveRegistryHub(); new PNEnemyTypeProbeModule().Register(open);
        open.GetOrCreate<EveCharacterDefinition>();
        foreach (var name in new[] { "normal", "elite", "boss" })
            Reject(() => open.EnemyTypes().RequestOverride(Id("base:" + name), Id("addon:champion"), "addon", "1", new[] { "project_n" }), "Original enemy type cannot be overridden");
        Reject(() => open.EnemyTypes().Register(new PNEnemyTypeDefinition(PNContentIds.BossEnemy, "Other")), "Duplicate enemy type rejected");
        Reject(() => new PNEnemyTypeDefinition(default, "Invalid"), "Missing type ID rejected");
        Reject(() => new PNEnemyTypeDefinition(Id("addon:empty"), " "), "Empty display name rejected");
        Reject(() => hub.EnemyTypes().Register(new PNEnemyTypeDefinition(Id("addon:late"), "Late")), "No registration after seal");
        Check(PNEnemyContent.ResolveType(hub.Characters.Get(Id("test:enemy")).Data, hub).Id == PNContentIds.NormalEnemy, "Old character content defaults to registered Normal");
        foreach (var value in new[] { "null", "2", "true", "{}", "[]", "''", "'Boss'", "'addon:missing'", "'base:missing'" })
            Reject(() => Build(",data:{enemyTypeId:" + value + "}"), "Malformed or unregistered authored type cannot publish a session");
        Reject(() => Build(",data:[]"), "Character metadata container must be an object");
        Reject(() => Build(",data:{enemyTypeId:'base:boss',enemyTypeId:'base:elite'}"), "Duplicate classification fields rejected");
        foreach (var value in new[] { "base:boss", "project_n:elite" })
            Reject(() => BuildReference(",data:{enemyTypeId:'" + value + "'}", new[] { "addon" }), "Original and public type references require owner dependency");
        Reject(() => BuildReference(",data:{enemyTypeId:'addon:champion'}", new[] { "project_n" }), "Custom type requires dependency");
        Check(BuildReference("", Array.Empty<string>()).Characters.Count == 1, "Legacy fallback does not introduce an authored external dependency");
        Check(BuildReference(",data:{enemyTypeId:'base:boss'}", new[] { "project_n" }).Characters.Count == 1, "Owner dependency permits public type reference");
        var custom = Build(",data:{enemyTypeId:'addon:champion'}");
        Check(PNEnemyContent.ResolveType(custom.Characters.Get(Id("test:enemy")).Data, custom).Id == Id("addon:champion"), "Custom registered type resolves");
        var before = open.Characters.Count;
        Reject(() => new EveJsonContentLoader().Register(new[] { Document(",data:{enemyTypeId:'addon:missing'}") }, open,
            null, new EveICharacterContentReferenceValidator[] { new PNContentModule() }), "Invalid character reference rejects JSON batch");
        Check(open.Characters.Count == before, "Failed batch does not commit characters");
        var payload = JObject.Parse("{enemyTypeId:'base:elite'}");
        var definition = new EveCharacterDefinition(Id("test:immutable"), Array.Empty<EveInitialStat>(), null, "", payload);
        payload["enemyTypeId"] = "base:boss";
        ((JObject)definition.Data)["enemyTypeId"] = "base:normal";
        Check(PNEnemyContent.ResolveType(definition.Data, hub).Id == PNContentIds.EliteEnemy, "Character metadata isolated from input and getter mutation");
        var compatibility = EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:enemy_types");
        Check(compatibility.ContentHashes.ContainsKey("registry:" + typeof(PNEnemyTypeDefinition).FullName), "Enemy vocabulary enters coop hashes");
        Reject(() => compatibility.Validate(EveContentCompatibility.Create(Build(",data:{enemyTypeId:'base:boss'}"),
            Array.Empty<EveContentDocument>(), "test:enemy_types")), "Classification enters character fingerprints without document hashes");
        var different = new EveRegistryHub(); new PNContentModule().Register(different); different.Seal();
        var originalOnly = EveContentCompatibility.Create(different, Array.Empty<EveContentDocument>(), "test:enemy_types");
        Check(originalOnly.ContentHashes["registry:" + typeof(PNEnemyTypeDefinition).FullName] !=
            compatibility.ContentHashes["registry:" + typeof(PNEnemyTypeDefinition).FullName], "Additional custom types change registry fingerprint");
        foreach (var value in new[] { "base:normal", "base:elite", "base:boss", "addon:champion" })
            Load(Build(",data:{enemyTypeId:'" + value + "'}"), Id(value));
        Load(hub, PNContentIds.NormalEnemy);
        var invalid = new EveRegistryHub(); var module = new PNContentModule(); module.Register(invalid);
        invalid.GetOrCreate<EveCharacterDefinition>().Register(new EveCharacterDefinition(Id("test:enemy"), Array.Empty<EveInitialStat>(),
            null, "", JObject.Parse("{enemyTypeId:'addon:missing'}")));
        invalid.Seal();
        Reject(() => module.ValidateContent(invalid), "Code-registered character types validated at startup");
        var unready = new PNEnemyStat();
        Reject(() => unready.Initialize(invalid.Characters.Get(Id("test:enemy")), invalid), "Runtime rejects invalid type before initializing stats");
        Check(!unready.IsReady, "Invalid runtime classification leaves stats uninitialized");
        unready.Initialize(new PNStatSet(hub));
        Check(unready.EnemyTypeId == PNContentIds.NormalEnemy, "Direct stat-set initialization uses registered Normal");
        Reject(() => unready.Initialize(new PNStatSet(hub)), "Direct initialized stats cannot change classification");
        unready.Unload();
        EveRegistrySession.Begin();
        EveRegistrySession.Start(invalid, EveSessionRole.Offline,
            EveContentCompatibility.Create(invalid, Array.Empty<EveContentDocument>(), "test:invalid_enemy"));
        var failedIdentity = new EveEntityIdentity(); failedIdentity.Assign(new EveEntityId(97102), Id("test:enemy"), 0);
        var failedLoader = new EveTestLoader(); var failedStat = new PNEnemyStat();
        MonoBehaviour[] failedComponents = { failedIdentity, failedLoader, failedStat };
        foreach (var component in failedComponents) component.Components = failedComponents;
        try
        {
            Reject(() => failedLoader.Load(invalid), "Invalid code-authored type cannot reach Loader Ready");
            Check(failedLoader.State == EveEntityLoadState.Failed && !failedStat.IsReady, "Failed classification rolls back entity loading");
            Reject(() => { var unused = failedStat.EnemyTypeId; }, "Failed Load leaves no classification handle");
        }
        finally { failedLoader.Unload(); EveRegistrySession.End(); }
    }

    private static void Load(EveRegistryHub hub, EveId expected)
    {
        EveRegistrySession.Begin();
        EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:enemy_types"));
        var identity = new EveEntityIdentity(); identity.Assign(new EveEntityId(97101), Id("test:enemy"), 0);
        var loader = new EveTestLoader(); var stat = new PNEnemyStat();
        MonoBehaviour[] components = { identity, loader, stat };
        foreach (var component in components) component.Components = components;
        try
        {
            Reject(() => { var unused = stat.EnemyTypeId; }, "Classification unavailable before Load");
            loader.Load(hub);
            Check(loader.IsReady && stat.EnemyTypeId == hub.EnemyTypes().ResolveIdentity(expected), "Loader resolves classification before Ready");
            Check(stat.GetCurrent(PNContentIds.Health) == 100d && stat.CombatTeam == PNCombatTeam.Enemy, "Classification does not change stats or combat team");
            loader.Load(hub);
            Check(stat.EnemyTypeId == hub.EnemyTypes().ResolveIdentity(expected), "Repeated Ready Load keeps classification");
            Reject(() => stat.Initialize(hub.Characters.Get(Id("test:enemy")), hub), "Cannot reclassify an initialized enemy");
            loader.Unload();
            Reject(() => { var unused = stat.EnemyType; }, "Unload releases classification");
            loader.Load(hub);
            Check(stat.EnemyTypeId == hub.EnemyTypes().ResolveIdentity(expected), "Reload restores content classification");
        }
        finally { loader.Unload(); EveRegistrySession.End(); }
    }
}
