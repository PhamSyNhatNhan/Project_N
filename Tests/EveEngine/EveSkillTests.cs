using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static EveSessionSmokeTests;

// EN: Test-only game parser for flexible payloads and rollback.
// VI: Bộ đọc của game giả lập để kiểm tra dữ liệu linh hoạt và rollback.
public class EveParsingTestSkill : EveSkill
{
    public bool Fail;
    public bool ReturnNull;
    public int Calls;
    public override string CharacterDefinitionId => "test:fallback";
    protected override object LoadSkillData(EveId skillId, object data)
    {
        Calls++;
        if (Fail) throw new FormatException("Game rejected its skill data.");
        if (ReturnNull) return null;
        if (data == null) throw new FormatException("This game skill requires data.");
        return ((JObject)data)["custom"].Value<int>();
    }
    public override void Unload() { Calls = 0; base.Unload(); }
}

// EN: Checks flexible schemas, registration safety, data isolation and entity loading.
// VI: Kiểm tra schema linh hoạt, đăng ký an toàn, cô lập dữ liệu và nạp entity.
public static class EveSkillTests
{
    private const string Json = "{\"schemaVersion\":1,\"characters\":[{\"id\":\"test:hero\",\"skill\":[{\"id\":\"test:attack\",\"data\":{\"custom\":7,\"nested\":{\"arbitrary\":[true,\"test:opaque_ref\"]}}}]}]}";
    private static readonly EveId Hero = EveId.Parse("test:hero");
    private static readonly EveId Attack = EveId.Parse("test:attack");
    private static EveRegistryHub Build(string json = Json) => new EveContentBootstrap().Build(
        Array.Empty<EveIContentModule>(), new[] { new EveContentDocument("skills.json", json, "test") });

    public static void Run()
    {
        var hub = Build();
        var character = hub.Get<EveCharacterDefinition>().Get(Hero);
        var definition = hub.Get<EveSkillDefinition>().Get(Attack);
        Check(character.SkillIds.Count == 1 && character.SkillIds[0] == Attack, "character links registered skill IDs");
        Check(((JObject)definition.Data)["nested"]["arbitrary"][1].Value<string>() == "test:opaque_ref",
            "engine preserves arbitrary nested data without interpreting game references");
        Check(Build("{\"schemaVersion\":1,\"characters\":[{\"id\":\"test:empty\"}]}")
            .Get<EveCharacterDefinition>().Get(EveId.Parse("test:empty")).SkillIds.Count == 0,
            "old characters without skill remain loadable");
        Check(Build(Json.Replace("\"data\":{\"custom\":7,\"nested\":{\"arbitrary\":[true,\"test:opaque_ref\"]}}", "\"data\":[1,2,3]"))
            .Get<EveSkillDefinition>().Get(Attack).Data is JArray, "payload schema can be an array");
        Check(((JObject)Build(Json.Replace("\"custom\":7", "\"custom\":\"2026-09-30T00:00:00Z\""))
            .Get<EveSkillDefinition>().Get(Attack).Data)["custom"].Type == JTokenType.String,
            "date-like custom strings stay strings");

        var input = JObject.Parse("{\"value\":1}");
        var stored = new EveSkillDefinition(Attack, input);
        input["value"] = 2;
        var read = (JObject)stored.Data;
        read["value"] = 3;
        Check(((JObject)stored.Data)["value"].Value<int>() == 1, "definition isolates input and returned JSON aliases");
        var ids = new List<EveId> { Attack };
        var copied = new EveCharacterDefinition(Hero, Array.Empty<EveInitialStat>(), ids);
        ids.Clear();
        Check(copied.SkillIds.Count == 1, "character copies skill ID input");
        Reject(() => ((IList<EveId>)copied.SkillIds).Clear(), "published character skill IDs are immutable");
        Reject(() => new EveCharacterDefinition(Hero, Array.Empty<EveInitialStat>(), new[] { Attack, Attack }), "duplicate character skill IDs");
        Reject(() => new EveSkillDefinition(default, input), "invalid skill definition ID");
        Check(new EveSkillDefinition(Attack).Data == null, "code skill can omit data");
        Check(new EveSkillDefinition(Attack, JValue.CreateNull()).Data == null, "JSON null token normalizes to C# null");
        foreach (string entry in new[] { "{\"id\":\"test:attack\"}", "{\"id\":\"test:attack\",\"data\":null}" })
        {
            var nullableHub = Build("{\"schemaVersion\":1,\"characters\":[{\"id\":\"test:hero\",\"skill\":[" + entry + "]}]}");
            Check(nullableHub.Skills.Get(Attack).Data == null, "missing/explicit null payload registers as C# null");
            var nullableSkill = new EveSkill();
            nullableSkill.Initialize(nullableHub.Characters.Get(Hero), nullableHub.Skills);
            Check(nullableSkill.IsReady && nullableSkill.GetSkillData(Attack) == null &&
                nullableSkill.TryGetSkillData(Attack, out object nullableData) && nullableData == null &&
                !nullableSkill.TryGetSkillData(EveId.Parse("test:absent"), out _),
                "loaded null data remains distinguishable from an absent skill ID");
            Reject(() => new EveParsingTestSkill().Initialize(nullableHub.Characters.Get(Hero), nullableHub.Skills),
                "game override may still require its own data");
            nullableSkill.Unload();
            Check(!nullableSkill.IsReady, "unload clears readiness after a null payload load");
        }

        var first = new EveSkill();
        var second = new EveSkill();
        first.Initialize(character, hub.Get<EveSkillDefinition>());
        second.Initialize(character, hub.Get<EveSkillDefinition>());
        ((JObject)first.GetSkillData(Attack))["custom"] = 99;
        Check(((JObject)second.GetSkillData(Attack))["custom"].Value<int>() == 7 &&
            ((JObject)definition.Data)["custom"].Value<int>() == 7, "each entity has independent JSON data");
        Reject(() => ((IDictionary<EveId, object>)first.Skills).Clear(), "loaded skill ID map cannot be changed by casting");
        var parser = new EveParsingTestSkill();
        parser.Initialize(character, hub.Get<EveSkillDefinition>());
        Check((int)parser.GetSkillData(Attack) == 7 && parser.Calls == 1, "game override converts object into typed data");
        var nullParser = new EveParsingTestSkill { ReturnNull = true };
        nullParser.Initialize(character, hub.Skills);
        Check(nullParser.IsReady && nullParser.GetSkillData(Attack) == null && nullParser.Calls == 1,
            "game hook returning null still completes loading");
        parser.Unload();
        Check(!parser.IsReady && parser.Calls == 0, "unload clears base and derived state");
        Reject(() => parser.Load(new EveRegistryHub()), "unsealed registry rejected");

        foreach (string entry in new[] {
            "null", "{}",
            "{\"id\":\"bad\",\"data\":{}}", "{\"id\":\"other:x\",\"data\":{}}",
            "{\"id\":\"test:x\",\"data\":{},\"kind\":\"unknown\"}",
            "{\"id\":\"test:x\",\"data\":{\"nested\":1,\"nested\":2}}",
            "{\"id\":\"test:x\",\"data\":{}},{\"id\":\"test:x\",\"data\":{}}" })
            Reject(() => Build("{\"schemaVersion\":1,\"characters\":[{\"id\":\"test:hero\",\"skill\":[" + entry + "]}]}"), "invalid skill envelope: " + entry);
        Reject(() => Build("{\"schemaVersion\":1,\"characters\":[{\"id\":\"test:hero\",\"skill\":null}]}"), "null skill list");

        var transaction = new EveRegistryHub();
        Reject(() => new EveJsonContentLoader().Register(new[] {
            new EveContentDocument("good.json", Json, "test"),
            new EveContentDocument("bad.json", Json.Replace("test:hero", "test:other"), "test")
        }, transaction), "same skill definition ID cannot be declared by two characters");
        Check(transaction.Get<EveSkillDefinition>().Count == 0 && transaction.Get<EveCharacterDefinition>().Count == 0,
            "failed JSON batch registers no partial skills or characters");
        var missing = new EveRegistryHub();
        missing.GetOrCreate<EveCharacterDefinition>().Register(copied);
        Reject(() => new EveJsonContentLoader().Register(Array.Empty<EveContentDocument>(), missing), "missing code-defined skill reference");
        var changed = Build(Json.Replace("\"custom\":7", "\"custom\":8"));
        Reject(() => EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:1")
            .Validate(EveContentCompatibility.Create(changed, Array.Empty<EveContentDocument>(), "test:1")),
            "registry fingerprint includes flexible skill data");

        EveRegistrySession.Begin();
        EveRegistrySession.Start(hub, EveSessionRole.Offline,
            EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:1"));
        try
        {
            var identity = new EveEntityIdentity();
            identity.Assign(new EveEntityId(707), Hero, 0);
            var loader = new EveTestLoader();
            var skill = new EveParsingTestSkill { Fail = true };
            MonoBehaviour[] components = { identity, loader, skill };
            foreach (MonoBehaviour component in components) component.Components = components;
            Reject(() => loader.Load(hub), "game parsing failure blocks entity Ready");
            Check(!skill.IsReady && skill.Calls == 0 && loader.State == EveEntityLoadState.Failed,
                "failed load rolls back skill state");
            skill.Fail = false;
            loader.Load(hub);
            Check(loader.IsReady && skill.IsReady && (int)skill.GetSkillData(Attack) == 7,
                "spawn CharacterId overrides Inspector fallback without Stat or Action");
            loader.Load(hub);
            Check(skill.Calls == 1, "entity load remains idempotent");
            loader.Unload();
            skill.ReturnNull = true;
            loader.Load(hub);
            Check(loader.IsReady && skill.IsReady && skill.GetSkillData(Attack) == null,
                "null hook result does not block entity Ready");
            EveRegistrySession.End();
            Check(!skill.IsReady, "session end unloads skill data");
        }
        finally { if (EveRegistrySession.IsReady) EveRegistrySession.End(); }
    }
}
