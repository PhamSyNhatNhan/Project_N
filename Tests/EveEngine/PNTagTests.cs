using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static EveSessionSmokeTests;

public class PNTagProbeModule : PNProbeModule
{
    public static readonly EveId Star = EveId.Parse("addon:star");
    public override void Register(EveRegistryHub hub)
    {
        base.Register(hub);
        hub.Tags().Register(new PNTagDefinition(Star));
    }
}

// EN: Covers extensible tags, immutable per-hook payload metadata and tag-only action loading.
// VI: Kiểm tra tag mở rộng, metadata bất biến theo hook và nạp action chỉ có tag không cần value.
public static class PNTagTests
{
    public static void Run()
    {
        var hub = new EveRegistryHub();
        new PNTagProbeModule().Register(hub);
        Check(hub.Tags().Count == 13 && ReferenceEquals(hub.Tags(), hub.Get<PNTagDefinition>()),
            "named tag registry contains built-in types/elements and a mod tag");
        Check(hub.Tags().Get(PNTagProbeModule.Star).Id == PNTagProbeModule.Star &&
            !hub.DamageTypes().TryGet(PNTagProbeModule.Star, out _) && !hub.Elements().TryGet(PNTagProbeModule.Star, out _),
            "generic tag needs neither a damage type nor an element");
        Reject(() => new PNTagDefinition(default), "invalid tag ID rejected");
        Reject(() => hub.Tags().Register(new PNTagDefinition(PNContentModule.Fire)), "duplicate tag ID rejected");
        hub.Seal();
        Check(hub.Tags().IsSealed, "tags seal with the content hub");
        Reject(() => hub.Tags().Register(new PNTagDefinition(EveId.Parse("addon:late"))), "late tag registration rejected");

        var set = new PNStatSet(hub);
        set.AddBase(new EveBaseStat(PNContentModule.Defense, 500d));
        var source = EveId.Parse("test:star_bonus");
        set.SetSource(source, new[] {
            new EveStatModifier(PNTagProbeModule.Star, .2d, 1, PNContentModule.Scale),
            new EveStatModifier(PNTagProbeModule.Star, -.2d, 1, PNContentModule.Mitigation)
        });
        Check(Near(set.Calculate(PNContentModule.Scale, 100d, PNTagProbeModule.Star), 120d), "generic tag selects outgoing bonus");
        Check(Near(set.Calculate(PNContentModule.Mitigation, 100d, PNTagProbeModule.Star), 80d), "generic tag selects incoming resistance");
        Check(Near(set.CalculateDamage(100d, PNContentModule.Physical, PNTagProbeModule.Star), 40d),
            "generic tag resistance applies after defense");
        Check(Near(set.CalculateDamage(100d, PNContentModule.True, PNTagProbeModule.Star), 80d), "true still obeys generic tag resistance");
        Check(Near(set.CalculateDamage(100d, PNContentModule.Physical), 50d), "unselected generic tag does not affect damage");
        Reject(() => set.SetSource(source, new[] { new EveStatModifier(PNTagProbeModule.Star, 1d, 0, PNContentModule.Mitigation, EveId.Parse("test:missing_flat")) }), "unknown incoming flat contribution rejected");
        Check(Near(set.Calculate(PNContentModule.Scale, 100d, PNTagProbeModule.Star), 120d), "invalid tag source replacement is atomic");
        set.SetSource(EveId.Parse("test:star_flat"), new[] { new EveStatModifier(PNTagProbeModule.Star, 5d, 0) });
        Check(Near(set.Calculate(PNContentModule.Scale, 100d, PNTagProbeModule.Star), 125d) &&
            set.Evaluate(PNTagProbeModule.Star) == 0d, "tag flat follows scale percentages and cannot create a stat value");
        Reject(() => set.CalculateDamage(100d, PNTagProbeModule.Star), "generic tag alone does not imply damage type");
        Reject(() => set.CalculateDamage(100d, PNContentModule.Physical, PNContentModule.AttackPower), "stat ID is not implicitly a damage tag");

        var original = JObject.Parse("{\"formId\":\"addon:second_form\",\"tags\":[\"addon:star\",\"project_n:fire\",\"addon:star\"]}");
        var action = new PNSkillActionData(EveId.Parse("test:action"), perform: original);
        original["tags"][0] = "addon:changed";
        Check(action.PerformTags.Count == 2 && action.PerformTags.Contains(PNTagProbeModule.Star) &&
            action.Perform["formId"].Value<string>() == "addon:second_form" && action.Perform["value"] == null,
            "form payload carries tags without value or duration and is isolated from source mutation");
        Check(action.StartTags.Count == 0 && action.ExitTags.Count == 0 && action.CancelTags.Count == 0,
            "omitted hook tags are empty");
        action.ValidateTags(hub);
        Check(action.Copy().PerformTags.SequenceEqual(action.PerformTags), "copy preserves normalized tags");
        Reject(() => ((IList<EveId>)action.PerformTags)[0] = PNContentModule.Ice, "effective tags are read-only");
        var returned = action.Perform;
        returned["tags"] = new JArray("addon:changed");
        Check(action.PerformTags.Contains(PNTagProbeModule.Star), "payload getter mutation cannot change effective tags");
        var empty = new PNSkillActionData(EveId.Parse("test:action"), perform: JObject.Parse("{\"tags\":[]}"));
        Check(empty.PerformTags.Count == 0, "explicit empty tag array is allowed");
        var primitive = new PNSkillActionData(EveId.Parse("test:action"), perform: new JValue(12));
        Check(primitive.PerformTags.Count == 0, "non-object action payloads remain supported without tags");
        foreach (var bad in new[] { "null", "\"addon:star\"", "{}", "[1]", "[null]", "[true]", "[\"\"]" })
            Reject(() => new PNSkillActionData(EveId.Parse("test:action"), perform: JObject.Parse("{\"tags\":" + bad + "}")),
                "malformed tag field rejected");
        foreach (var phase in new[] { "start", "perform", "exit", "cancel" })
        {
            var data = PNSkillData.Parse(JObject.Parse("{\"action\":{\"id\":\"test:action\",\"" + phase +
                "\":{\"tags\":[\"addon:unknown\"]}}}"));
            Reject(() => data.Action.ValidateTags(hub), "unknown tag rejected in every hook");
        }
        var multiType = new PNSkillActionData(EveId.Parse("test:action"), perform:
            JObject.Parse("{\"tags\":[\"project_n:physical\",\"project_n:magic\"]}"));
        multiType.ValidateTags(hub);
        Check(multiType.PerformTags.Count == 2, "non-damage action tags do not enforce damage type rules");

        var fingerprint = EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:tags");
        Check(fingerprint.ContentHashes.ContainsKey("registry:" + typeof(PNTagDefinition).FullName), "tags participate in coop content fingerprints");
        var changed = new EveRegistryHub();
        new PNTagProbeModule().Register(changed);
        changed.Tags().Register(new PNTagDefinition(EveId.Parse("addon:extra")));
        changed.Seal();
        Reject(() => fingerprint.Validate(EveContentCompatibility.Create(changed, Array.Empty<EveContentDocument>(), "test:tags")),
            "coop rejects mismatched tag registrations");
        LoadTaggedAction("addon:star", false);
        LoadTaggedAction("addon:unknown", true);
    }

    private static void LoadTaggedAction(string tag, bool reject)
    {
        string json = "{\"schemaVersion\":1,\"characters\":[{\"id\":\"test:tag_hero\",\"skill\":[{\"id\":\"test:tag_skill\",\"data\":{\"action\":{\"id\":\"test:action\",\"perform\":{\"formId\":\"addon:second_form\",\"tags\":[\"" + tag + "\"]}}}}]}]}";
        var hub = new EveContentBootstrap().Build(new[] { new PNTagProbeModule() },
            new[] { new EveContentDocument("tags.json", json, "test") });
        EveRegistrySession.Begin();
        EveRegistrySession.Start(hub, EveSessionRole.Offline,
            EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:tags"));
        var identity = new EveEntityIdentity();
        identity.Assign(new EveEntityId(899), EveId.Parse("test:tag_hero"), 0);
        var loader = new EveTestLoader();
        var skill = new PNSkill();
        MonoBehaviour[] components = { identity, loader, skill };
        foreach (var component in components) component.Components = components;
        try
        {
            if (reject)
            {
                Reject(() => loader.Load(hub), "unknown skill tag prevents Ready");
                Check(!skill.IsReady && loader.State == EveEntityLoadState.Failed, "tag validation failure rolls back skill loading");
            }
            else
            {
                loader.Load(hub);
                using (var action = (PNProbeAction)skill.CreateAction(EveId.Parse("test:tag_skill"), out var context))
                {
                    action.Perform(context);
                    Check(action.Calls == 1 && context.Data.Action.PerformTags.Single() == PNTagProbeModule.Star &&
                        context.Data.Action.Perform["value"] == null, "loaded tag-only action executes without value");
                }
            }
        }
        finally { loader.Unload(); EveRegistrySession.End(); }
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < .001d;
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (ArgumentException) { Check(true, name); return; }
        catch (FormatException) { Check(true, name); return; }
        catch (InvalidOperationException) { Check(true, name); return; }
        catch (KeyNotFoundException) { Check(true, name); return; }
        catch (NotSupportedException) { Check(true, name); return; }
        throw new Exception("Expected rejection: " + name);
    }
}
