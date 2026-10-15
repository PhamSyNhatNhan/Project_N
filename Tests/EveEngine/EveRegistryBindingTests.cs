using System;
using System.IO;
using System.Linq;
using EveEngine;
using static EveSessionSmokeTests;

// EN: Verifies stable identities, override contracts, deterministic selection and JSON rollback.
// VI: Kiểm tra danh tính ổn định, hợp đồng override, lựa chọn xác định và rollback JSON.
public static class EveRegistryBindingTests
{
    private static EveId Id(string value) => EveId.Parse(value);
    private static EveRegistryHub Hub()
    {
        var hub = new EveRegistryHub();
        hub.NameRegistry<EveStatDefinition>("stats");
        new EveBuiltInContentModule().Register(hub);
        new PNContentModule().Register(hub);
        new PNGameplayContentModule().Register(hub);
        return hub;
    }
    private static void Request(EveRegistryHub hub, string provider, EveStatKind kind = EveStatKind.Resource, string version = "1.0.0")
    {
        var target = Id(provider + ":health");
        hub.Stats.Register(new EveStatDefinition(target, kind, EveMaxChangePolicy.PreserveCurrent));
        hub.Stats.RequestOverride(Id("base:health"), target, provider, version, new[] { "project_n" });
    }
    private static EveContentCompatibility Fingerprint(EveRegistryHub hub)
        => EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:bindings");

    public static void Run()
    {
        var hub = Hub();
        Check(!hub.Stats.TryGet(Id("base:attack"), out _) && !hub.Stats.TryGet(Id("project_n:attack"), out _), "Old attack stat identities are removed, without compatibility aliases");
        Check(hub.Tags().ResolveIdentity(Id("base:attack")) == PNContentIds.AttackTag && !hub.Tags().TryGet(Id("base:attack_damage"), out _), "Attack tag uses the freed identity without a legacy damage alias");
        var classifiedHub = Hub(); classifiedHub.Seal();
        var classified = new PNStatSet(classifiedHub);
        classified.AddBase(new EveBaseStat(Id("base:attack_power"), 100d));
        classified.SetSource(Id("test:power_and_tag"), new[] {
            new EveStatModifier(Id("base:attack_power"), .2d, 1),
            new EveStatModifier(Id("base:attack"), .2d, 1, PNContentIds.Scale)
        });
        Check(classified.Evaluate(PNContentIds.AttackPower) == 120d && classified.Calculate(PNContentIds.Scale, classified.Evaluate(PNContentIds.AttackPower), Id("base:attack")) == 144d,
            "Attack power and attack tag bonuses each apply once on separate selectors");
        Reject(() => classified.AddBase(new EveBaseStat(Id("base:attack"), 10d)), "Old stat ID cannot become an entity base stat through its new tag meaning");
        Check(hub.Stats.Get(Id("base:attack_power")).Id == PNContentModule.AttackPower && hub.Stats.Get(Id("base:move_speed")).Id == EveBuiltInContentModule.MoveSpeed, "base resolves engine and game definitions");
        Check(hub.Actions().Get(Id("base:slash_action")).Create() is PNSlashAction, "base resolves action factory");
        Check(!hub.Stats.TryGet(Id("base:slash_action"), out _) && !hub.Actions().TryGet(Id("base:attack_power"), out _), "base lookup is type scoped");
        Reject(() => hub.Stats.Register(new EveStatDefinition(Id("base:custom"), EveStatKind.Number)), "base cannot own direct definitions");
        Reject(() => hub.Stats.RegisterBase(Id("base:attack_power"), PNContentModule.Defense, "project_n"), "duplicate concept rejected");
        Reject(() => hub.Stats.RegisterBase(Id("base:atk"), PNContentModule.AttackPower, "project_n"), "duplicate identity rejected");
        Reject(() => hub.Stats.RegisterBase(Id("base:chain"), Id("base:attack_power"), "project_n"), "binding chains rejected");
        Reject(() => hub.Stats.RequestOverride(Id("base:missing"), Id("addon:health"), "addon", "1", new[] { "project_n" }), "unknown concept rejected");
        Reject(() => hub.Stats.RequestOverride(Id("base:health"), Id("other:health"), "addon", "1", new[] { "project_n" }), "foreign override implementation rejected");
        Reject(() => hub.Stats.RequestOverride(Id("base:health"), Id("addon:health"), "addon", "1", Array.Empty<string>()), "override requires owner dependency");
        Reject(() => hub.Tags().RequestOverride(Id("base:physical"), Id("addon:physical"), "addon", "1", new[] { "project_n" }), "locked concept rejects override");
        Request(hub, "addon");
        Reject(() => hub.Stats.RequestOverride(Id("base:health"), Id("addon:health"), "addon", "1", new[] { "project_n" }), "duplicate provider rejected");
        hub.Seal();
        Check(hub.Stats.Get(Id("base:health")).Id == Id("addon:health") && hub.Stats.Get(PNContentModule.Health).Id == PNContentModule.Health, "public override and explicit original coexist");
        Check(hub.Stats.ResolveIdentity(Id("base:health")) == PNContentModule.Health && hub.Stats.GetEffective(PNContentModule.Health).Id == Id("addon:health"), "identity is stable while implementation changes");
        var binding = hub.Stats.BindingSnapshot().Single(value => value.Id == Id("base:health"));
        Check(binding.Provider == "addon" && binding.Version == "1.0.0" && binding.Owner == "project_n" && binding.AllowsOverride, "diagnostics expose selected provider and version");
        Reject(() => hub.Stats.SelectOverride(Id("base:health"), "project_n"), "sealed selection immutable");
        Reject(() => hub.Stats.RequestOverride(Id("base:attack_power"), Id("addon:attack"), "addon", "1", new[] { "project_n" }), "sealed override immutable");
        Reject(() => hub.Stats.RegisterBase(Id("base:new"), Id("addon:new"), "project_n"), "sealed vocabulary immutable");
        var set = new PNStatSet(hub);
        set.LoadCharacter(new EveCharacterDefinition(Id("test:hero"), new[] {
            new EveInitialStat(Id("base:health"), 100, 40), new EveInitialStat(PNContentModule.AttackPower, 100, null), new EveInitialStat(PNContentModule.Defense, -100, null) }));
        Check(set.TryGetBase(PNContentModule.Health, out var health) && health is EveResourceStat resource && resource.MaxChangePolicy == EveMaxChangePolicy.PreserveCurrent, "runtime uses selected stat metadata");
        Check(set.BaseSnapshot().Count == 7 && set.GetCurrent(Id("base:health")) == set.GetCurrent(PNContentModule.Health), "old and public IDs share one resource alongside combat defaults");
        set.SetSource(Id("test:buff"), new[] {
            new EveStatModifier(Id("base:attack_power"), .2d, 1, Id("base:scale")), new EveStatModifier(PNContentModule.AttackPower, .3d, 1),
            new EveStatModifier(Id("base:physical"), .2d, 1), new EveStatModifier(Id("base:physical"), -.2d, 1, Id("base:mitigation")), new EveStatModifier(Id("base:health"), 100, 0) });
        Check(set.Evaluate(Id("base:attack_power"), PNContentModule.AttackPower) == 150, "mixed IDs do not double count stats or buffs");
        Check(set.GetMax(Id("base:health")) == 200 && set.GetCurrent(PNContentModule.Health) == 40, "override resource policy applies");
        Check(set.Evaluate(Id("base:defense")) == -100, "public defense retains signed rule");
        Check(set.Calculate(Id("base:scale"), 100, Id("base:physical"), PNContentModule.Physical) == 120, "public tag and calculation normalize together");
        Check(set.ResolveDamageType(new[] { Id("base:physical"), PNContentModule.Physical }).Id == PNContentModule.Physical, "mixed damage tags select one type");
        var duplicate = new PNStatSet(hub); duplicate.AddBase(new EveBaseStat(Id("base:attack_power"), 1));
        Reject(() => duplicate.AddBase(new EveBaseStat(PNContentModule.AttackPower, 2)), "duplicate runtime base via public name rejected");
        var conflict = Hub(); Request(conflict, "addon"); Request(conflict, "other");
        Reject(conflict.Seal, "conflict cannot select by load order");
        Check(!conflict.IsSealed && !conflict.Stats.IsSealed && !conflict.Actions().IsSealed, "failed validation cannot partly seal hub");
        conflict.Stats.SelectOverride(Id("base:health"), "other"); conflict.Seal();
        Check(conflict.Stats.Get(Id("base:health")).Id == Id("other:health"), "explicit provider resolves conflict");
        var wrongKind = Hub(); Request(wrongKind, "addon", EveStatKind.Number); Reject(wrongKind.Seal, "resource-number override rejected");
        var missing = Hub(); missing.Stats.RegisterBase(Id("base:missing"), Id("project_n:missing"), "project_n"); Reject(missing.Seal, "missing original blocks seal");
        var absent = Hub(); absent.Stats.SelectOverride(Id("base:health"), "absent"); Reject(absent.Seal, "absent selected provider rejected");
        var original = Hub(); Request(original, "addon"); original.Stats.SelectOverride(Id("base:health"), "project_n"); original.Seal();
        Check(original.Stats.Get(Id("base:health")).Id == PNContentModule.Health, "user can choose original implementation");
        var calculation = Hub();
        calculation.Calculations().Register(new PNStatCalculationDefinition(Id("addon:scale"), typeof(PNTestCustomCalculation)));
        calculation.Calculations().RequestOverride(Id("base:scale"), Id("addon:scale"), "addon", "1", new[] { "project_n" }); calculation.Seal();
        var computed = new PNStatSet(calculation); computed.AddBase(new EveBaseStat(PNContentModule.AttackPower, 100));
        computed.SetSource(Id("test:buff"), new[] { new EveStatModifier(Id("base:attack_power"), 2, 1) });
        Check(computed.Evaluate(PNContentModule.AttackPower) == 120, "existing runtime uses overridden calculation");
        computed.SetSource(Id("test:flat"), new[] { new EveStatModifier(Id("base:fire"), 5d, 0, Id("base:scale")) });
        Check(computed.Evaluate(PNContentModule.AttackPower, PNContentModule.Fire) == 120 &&
            computed.Calculate(Id("base:scale"), 100d, PNContentModule.Fire) == 150,
            "selected custom Scale owns flat formula while Evaluate filters damage flats");
        var action = Hub(); action.Actions().Register(new PNSkillActionDefinition(Id("addon:slash"), typeof(PNBindingSlash)));
        action.Actions().RequestOverride(Id("base:slash_action"), Id("addon:slash"), "addon", "1", new[] { "project_n" }); action.Seal();
        Check(action.Actions().Get(Id("base:slash_action")).Create() is PNBindingSlash, "compatible slash override accepted");
        var invalidAction = Hub(); invalidAction.Actions().Register(new PNSkillActionDefinition(Id("addon:slash"), typeof(PNProbeAction)));
        invalidAction.Actions().RequestOverride(Id("base:slash_action"), Id("addon:slash"), "addon", "1", new[] { "project_n" }); Reject(invalidAction.Seal, "incompatible action rejected before cast");
        var secondVersion = Hub(); Request(secondVersion, "addon", version: "2.0.0"); secondVersion.Seal();
        Reject(() => Fingerprint(hub).Validate(Fingerprint(secondVersion)), "coop detects provider version mismatch");
        var same = Hub(); Request(same, "addon"); same.Seal(); Fingerprint(hub).Validate(Fingerprint(same));
        Check(Fingerprint(hub).ContentHashes.Keys.Any(key => key.StartsWith("bindings:")), "coop hashes binding table");
        var reversed = Hub(); Request(reversed, "other"); Request(reversed, "addon"); reversed.Stats.SelectOverride(Id("base:health"), "other"); reversed.Seal();
        Fingerprint(conflict).Validate(Fingerprint(reversed)); Check(true, "provider order leaves fingerprint unchanged");
        Json();
    }

    private static void Json()
    {
        string json = "{\"schemaVersion\":1,\"statDefinitions\":[{\"id\":\"addon:health\",\"kind\":\"resource\"}],\"characters\":[{\"id\":\"addon:hero\",\"stats\":[{\"statId\":\"base:health\",\"base\":100,\"initialCurrent\":20}]}],\"overrides\":[{\"registry\":\"stats\",\"id\":\"base:health\",\"implementationId\":\"addon:health\",\"version\":\"1.0.0\"}]}";
        EveContentDocument Doc(string content, string[] deps = null) => new EveContentDocument("addon.json", content, "addon", dependencies: deps ?? new[] { "project_n" });
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, new[] { Doc(json) });
        Check(hub.Stats.Get(Id("base:health")).Id == Id("addon:health") && hub.Characters.Get(Id("addon:hero")).Stats[0].Id == PNContentModule.Health, "JSON reference and override preserve identity");
        Reject(() => new EveContentBootstrap().Build(new[] { new PNContentModule() }, new[] { Doc(json, Array.Empty<string>()) }), "JSON base reference requires dependency");
        var original = Hub(); var loader = new EveJsonContentLoader();
        Reject(() => loader.Register(new[] { Doc(json.Replace("addon:health\",\"version", "addon:missing\",\"version")) }, original), "invalid override target rejected");
        Check(!original.Stats.TryGet(Id("addon:health"), out _) && !original.Characters.TryGet(Id("addon:hero"), out _), "invalid override rolls back JSON batch");
        string duplicate = json.Replace("\"statId\":\"base:health\",\"base\":100,\"initialCurrent\":20}", "\"statId\":\"base:health\",\"base\":100,\"initialCurrent\":20},{\"statId\":\"project_n:health\",\"base\":100}");
        Reject(() => loader.Register(new[] { Doc(duplicate) }, Hub()), "JSON duplicate public and original stat rejected");
        Reject(() => loader.Register(new[] { Doc(json.Replace("addon:health\",\"kind", "base:health\",\"kind")) }, Hub()), "mod cannot define base namespace");
        Reject(() => loader.Register(new[] { Doc(json.Replace("\"kind\":\"resource\"", "\"kind\":\"number\"")) }, Hub()), "JSON incompatible override rejected");
        var baseDocuments = new EveDirectoryContentSource(Path.Combine(UnityEngine.Application.streamingAssetsPath, "BaseContent"), "unused", Array.Empty<string>()).ReadDocuments();
        var game = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, baseDocuments);
        Check(game.Skills.Get(Id("base:eve_slash")).Id == PNGameplayContentModule.EveSlash && game.Characters.Get(Id("base:eve")).Id == Id("project_n:eve"), "game JSON concepts have public names");
        string invalidPayload = "{\"schemaVersion\":1,\"characters\":[{\"id\":\"addon:hero\",\"skill\":[{\"id\":\"addon:slash\",\"data\":{\"action\":{\"id\":\"base:slash_action\",\"perform\":{\"duration\":0}}}}]}]}";
        Reject(() => new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, new[] { Doc(invalidPayload) }), "action payload validated at bootstrap");
        var validPayload = invalidPayload.Replace("\"duration\":0", "\"duration\":0.32");
        Reject(() => new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, new[] { Doc(validPayload, Array.Empty<string>()) }), "public action reference requires game dependency");
        var payloadHub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, new[] { Doc(validPayload) });
        Check(payloadHub.Skills.Get(Id("addon:slash")) != null, "public action reference accepts declared game dependency");
        var taggedPayload = validPayload.Replace("\"duration\":0.32", "\"duration\":0.32,\"tags\":[\"base:missing\"]");
        Reject(() => new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, new[] { Doc(taggedPayload) }), "unknown public payload tag rejected before session");
        var duplicateRequest = json.Replace("\"version\":\"1.0.0\"}]", "\"version\":\"1.0.0\"},{\"registry\":\"stats\",\"id\":\"base:health\",\"implementationId\":\"addon:health\",\"version\":\"1.0.0\"}]");
        var duplicateHub = Hub();
        Reject(() => loader.Register(new[] { Doc(duplicateRequest) }, duplicateHub), "duplicate JSON override rejected atomically");
        Check(!duplicateHub.Stats.TryGet(Id("addon:health"), out _), "duplicate override leaves no pending definition committed");
        string path = Path.Combine(Path.GetTempPath(), "EveBindingSelections-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Check(EveOverrideSelection.Read(path).Count == 0, "missing selection file uses defaults");
            File.WriteAllText(path, "{\"schemaVersion\":1,\"selections\":[{\"registry\":\"stats\",\"id\":\"base:health\",\"provider\":\"project_n\"}]}");
            var selected = new EveContentBootstrap().Build(new[] { new PNContentModule() }, new[] { Doc(json) }, EveOverrideSelection.Read(path));
            Check(selected.Stats.Get(Id("base:health")).Id == PNContentModule.Health, "external selection chooses original over mod");
            File.WriteAllText(path, "{\"schemaVersion\":1,\"selections\":[],\"unknown\":1}");
            Reject(() => EveOverrideSelection.Read(path), "unknown selection field rejected");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

public class PNBindingSlash : PNSlashAction { }
