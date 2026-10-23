#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class EveConfigurationEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Requires idle Edit Mode.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty; int checks = 0;
        var scene = EditorSceneManager.NewPreviewScene(); EveEntityLoader loader = null;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"),
            Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().Select(document =>
        {
            var obj = JObject.Parse(document.Json);
            var characters = obj["characters"] as JArray;
            var eve = characters?.OfType<JObject>().FirstOrDefault(x => (string)x["id"] == "project_n:eve");
            if (eve == null) return document;
            var entries = (JArray)eve["skill"];
            var main = entries.OfType<JObject>().Single(x => (string)x["id"] == "project_n:eve_moveset")["data"]["skill"];
            var abilities = (JArray)main["abilities"];
            JObject Ability(string suffix) => abilities.OfType<JObject>().Single(x => (string)x["id"] == "project_n:eve_" + suffix);
            Ability("skill_ready")["cooldown"] = 7;
            Ability("dash_ready")["maximum"] = 4; Ability("dash_ready")["initialAmount"] = 4; Ability("dash_ready")["cooldown"] = 3;
            Ability("burst_gauge")["maximum"] = 200; Ability("slash_stacks")["maximum"] = 16;
            Ability("burst_state")["duration"] = 9; Ability("next_attack")["duration"] = 6;
            var tuning = main["tuning"]; tuning["burstCost"] = 80; tuning["burstInitialStacks"] = 4;
            tuning["burstAttackBonus"] = 75; tuning["burstAttackSpeedBonus"] = 25; tuning["burstEndRefund"] = 20; tuning["skillGauge"] = 7;
            tuning["repeatDelay"] = .4;
            var attack = entries.OfType<JObject>().Single(x => (string)x["id"] == "project_n:eve_attack_right")["data"]["action"]["perform"];
            attack["duration"] = .5; attack["radius"] = 2.4; attack["value"]["amount"] = 240;
            return new EveContentDocument(document.Name, obj.ToString(), document.ContentNamespace, document.IsMetadata, document.Dependencies);
        }).ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:eve_json"));
        try
        {
            var fallbackRoot = new GameObject("Legacy character ID fallback probe"); fallbackRoot.SetActive(false); SceneManager.MoveGameObjectToScene(fallbackRoot, scene);
            fallbackRoot.AddComponent<EveEntityIdentity>(); var fallbackStat = fallbackRoot.AddComponent<EveCharacterStat>();
            var serialized = new SerializedObject(fallbackStat); serialized.FindProperty("characterId").stringValue = "project_n:eve"; serialized.ApplyModifiedPropertiesWithoutUndo();
            var fallbackManager = fallbackRoot.AddComponent<PNAbilityManager>(); var fallbackLoader = fallbackRoot.AddComponent<EveEntityLoader>();
            try { fallbackLoader.Load(hub); Check(fallbackManager.Abilities.Definitions.Count == 7, "Legacy stat character ID fallback did not load JSON abilities."); }
            finally { fallbackLoader.Unload(); }
            var root = new GameObject("Eve JSON-only configuration probe"); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(987200), EveId.Parse("project_n:eve"), 0);
            var manager = root.AddComponent<PNAbilityManager>(); loader = root.AddComponent<EveEntityLoader>(); loader.Load(hub);
            Check(manager.Abilities.Definitions.Count == 7 && root.GetComponent<EveCharacterSkill>() == null, "Abilities depend on skill runtime creation.");
            Check(((PNAbilityCooldown)manager.Abilities.Find(EveCharacterSkill.DashCooldownId)[0]).Current == 4, "Manager ignored JSON charge count.");
            loader.Unload(); var stats = root.AddComponent<EveCharacterStat>(); var skill = root.AddComponent<PNEveAttackSpeedProbe>();
            loader.Load(hub); root.SetActive(true);
            Check(skill.SkillRecovery.Definition.Cooldown == 7 && skill.DashRecovery.Definition.Cooldown == 3, "Cooldowns were overridden by code.");
            Check(skill.BurstGauge.Capacity == 200 && skill.SlashStacks.Capacity == 16, "Capacity was hardcoded.");
            Check(Math.Abs(skill.AttackInput.RepeatDelay - .4f) < .0001f, "Repeat timing did not use JSON.");
            Check(skill.TrySkill() && skill.BurstGauge.Current == 7 && skill.Empowerment.DurationRemaining == 6, "Skill reward/duration did not use JSON.");
            skill.TickAttack(.2f);
            skill.BurstGauge.Add(200); Check(skill.TryBurst() && skill.BurstGauge.Current == 120 && skill.SlashStacks.Current == 4 && skill.BurstState.DurationRemaining == 9,
                "Burst cost/stacks/duration did not use JSON.");
            Check(stats.Evaluate(PNContentIds.AttackPower) == 85 && stats.Evaluate(PNContentIds.AttackSpeed) == 125, "Burst bonuses were hardcoded.");
            manager.Advance(9); Check(!skill.IsInBurst && skill.BurstGauge.Current == 140, "Burst refund did not use JSON.");
            Check(skill.TryAttack(Vector3.right) && skill.AttackVisual.Radius == 2.4f, "Attack geometry did not use its own JSON profile.");
            skill.TickAttack(.34f); Check(skill.IsAttacking, "Attack duration was overridden by Inspector/default.");
            skill.TickAttack(.17f); Check(!skill.IsAttacking, "Custom duration did not complete.");
            var data = skill.GetData(EveId.Parse("base:eve_attack_right"));
            Check(EveSweepSettings.Parse(data.Action.Perform).Value.Amount == 240, "Attack damage did not use JSON.");
            var invalid = (JObject)data.Action.Perform; invalid["sweepEnd"] = .01;
            bool rejected = false; try { EveSweepSettings.Parse(invalid); } catch (FormatException) { rejected = true; }
            Check(rejected, "Invalid sweep window accepted.");
        }
        finally { loader?.Unload(); EveRegistrySession.End(); EditorSceneManager.ClosePreviewScene(scene); }
        Check(original.isDirty == dirty, "JSON checks changed gameplay scene.");
        return "PASS " + checks + " JSON configuration checks: manager before skill, custom charges/cooldowns/durations/cost/rewards/bonuses/geometry/damage, validation.";
    }
}
#endif
