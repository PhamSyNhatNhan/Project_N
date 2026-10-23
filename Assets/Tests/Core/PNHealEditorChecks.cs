#if UNITY_EDITOR
using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Native API/registry checks in a disposable scene. No authored scenes or visuals are edited.
public static class PNHealEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped)
            throw new InvalidOperationException("Heal checks require idle Edit Mode and a stopped session.");
        var active = SceneManager.GetActiveScene(); bool dirty = active.isDirty;
        var preview = EditorSceneManager.NewPreviewScene(); bool started = false; int checks = 0;
        Action<bool, string> check = (valid, message) => { if (!valid) throw new Exception(message); checks++; };
        Func<double, double, bool> near = (a, b) => Math.Abs(a - b) < .00001d;
        try
        {
            var hub = new EveRegistryHub(); new EveBuiltInContentModule().Register(hub); new PNContentModule().Register(hub);
            var gameplay = new PNGameplayContentModule(); gameplay.Register(hub);
            var hero = EveId.Parse("test:native_heal_hero"); var skillId = EveId.Parse("test:native_multi_value");
            var data = JObject.Parse("{\"action\":{\"id\":\"base:damage_action\",\"perform\":{\"value\":[{\"amount\":500,\"statId\":\"base:attack_power\"},{\"amount\":50,\"statId\":\"base:defense\"},{\"amount\":100}],\"tags\":[\"base:true\"]}}}");
            hub.Skills.Register(new EveSkillDefinition(skillId, data));
            hub.GetOrCreate<EveCharacterDefinition>().Register(new EveCharacterDefinition(hero, new[] {
                new EveInitialStat(PNContentIds.Health, 2000d, 2000d), new EveInitialStat(PNContentIds.AttackPower, 100d),
                new EveInitialStat(PNContentIds.Defense, 500d)
            }, new[] { skillId }));
            var docs = new PNGameContentSource(System.IO.Path.Combine(Application.streamingAssetsPath,"BaseContent"),System.IO.Path.Combine(Application.persistentDataPath,"Mods"),Array.Empty<string>()).ReadDocuments().ToArray();
            new EveJsonContentLoader().Register(docs,hub); gameplay.FinalizeContent(hub);
            hub.Seal(); gameplay.ValidateContent(hub); check(true, "Composite registered content validation with real built-in Skill references");
            EveRegistrySession.Begin(); started = true;
            EveRegistrySession.Start(hub, EveSessionRole.Offline,
                EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:native_heal"));
            var source = Make(preview, hub, hero, 914001); var first = Make(preview, hub, hero, 914002); var second = Make(preview, hub, hero, 914003);
            var skill = source.GetComponent<PNSkill>();
            first.SetCurrent(PNContentIds.Health, 400d); second.SetCurrent(PNContentIds.Health, 400d);
            var values = PNSkillValues.Parse(data["action"]["perform"]["value"]);
            check(values.EvaluateEach(source).SequenceEqual(new[] { 500d, 250d, 100d }), "Separate scaling component results");
            source.SetSource(EveId.Parse("test:native_heal_bonus"), new[] {
                new EveStatModifier(PNContentIds.Heal, .2d, 1), new EveStatModifier(PNContentIds.Heal, 100d, 0),
                new EveStatModifier(PNContentIds.CritRate, 100d, 0), new EveStatModifier(PNContentIds.CritDamage, 350d, 0)
            });
            first.SetSource(EveId.Parse("test:native_heal_received"), new[] {
                new EveStatModifier(PNContentIds.Heal, -.5d, 1, PNContentIds.Mitigation)
            });
            int events = 0;
            first.HealReceived += result => { if (first.GetCurrent(PNContentIds.Health) != result.HealthAfter) throw new Exception("Event before HP commit"); events++; };
            skill.CreateAction(skillId, out var context, new[] { first, first, second }).Dispose();
            using (var operation = (PNHealOperation)hub.Operations().Get(EveId.Parse("base:heal")).Create())
            {
                operation.Execute(context, new JObject { ["value"] = data["action"]["perform"]["value"].DeepClone() });
                check(near(operation.Heal.BaseAmount, 850d) && near(operation.Heal.Amount, 1120d), "Heal operation explicit sum and Scale flat");
                check(operation.Results.Count == 2 && ReferenceEquals(operation.Results[0].Heal, operation.Results[1].Heal), "Target dedupe and immutable shared packet");
                check(near(first.GetCurrent(PNContentIds.Health), 960d) && near(second.GetCurrent(PNContentIds.Health), 1520d), "Per-target Mitigation without DEF or crit");
                check(first.IFrameRemaining == 0d && second.IFrameRemaining == 0d && events == 1, "Heal event distinct from damage/iframe");
                first.SetCurrent(PNContentIds.Health, 1950d);
                var capped = first.ReceiveHeal(operation.Heal);
                check(capped.AppliedHeal == 50d && capped.Overheal == 510d && capped.HealthAfter == 2000d, "HP cap and overheal");
                check(first.ReceiveHeal(operation.Heal).Status == PNHealStatus.NoMissingHealth, "Full HP heal result");
                first.SetCurrent(PNContentIds.Health, 0d);
                check(first.ReceiveHeal(operation.Heal).Status == PNHealStatus.DeadTarget && first.IsDead, "No resurrection");
                first.SetCurrent(PNContentIds.Health, 400d);
                first.RemoveSource(EveId.Parse("test:native_heal_received"));
            }
            source.RemoveSource(EveId.Parse("test:native_heal_bonus"));
            second.SetCurrent(PNContentIds.Health, 2000d);
            using (var damage = (PNDamageAction)skill.CreateAction(skillId, out var damageContext, new[] { second }))
            {
                damage.Start(damageContext); damage.Perform(damageContext);
                check(damage.Damage.Amount == 850d && second.GetCurrent(PNContentIds.Health) == 1150d, "Composite damage sums once and old pipeline remains");
                damage.Exit(damageContext);
            }
            double iframe = second.IFrameRemaining;
            check(source.SendHeal(second, 100d).AppliedHeal == 100d && second.IFrameRemaining == iframe, "Heal unaffected by active iframe");
            source.SetSource(EveId.Parse("test:native_heal_bonus"), new[] { new EveStatModifier(PNContentIds.Heal, 100d, 0) });
            var packet = source.CreateHeal(100d);
            source.GetComponent<EveEntityLoader>().Unload();
            check(first.ReceiveHeal(packet).AppliedHeal == 200d, "Delayed packet survives source unload");
            first.GetComponent<EveEntityLoader>().Unload(); first.GetComponent<EveEntityLoader>().Load(hub);
            first.SetCurrent(PNContentIds.Health, 100d); int priorEvents = events;
            first.ReceiveHeal(packet);
            check(events == priorEvents, "Heal subscribers removed on unload");
        }
        finally
        {
            try { if (started) EveRegistrySession.End(); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        check(active.isDirty == dirty, "Authored scene state changed");
        return "PASS native value/heal " + checks + ": components500/250/100 -> heal850 -> Scale1120 -> received560/1120; HP cap, dead target, iframe, unload, registered damage850; dirty=" + active.isDirty;
    }

    private static PNStat Make(Scene scene, EveRegistryHub hub, EveId hero, long id)
    {
        var obj = new GameObject("Native heal probe " + id) { hideFlags = HideFlags.HideAndDontSave }; obj.SetActive(false);
        SceneManager.MoveGameObjectToScene(obj, scene);
        obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), hero, 0);
        var stat = obj.AddComponent<PNStat>(); obj.AddComponent<PNSkill>(); obj.AddComponent<EveEntityLoader>().Load(hub);
        return stat;
    }
}
#endif
