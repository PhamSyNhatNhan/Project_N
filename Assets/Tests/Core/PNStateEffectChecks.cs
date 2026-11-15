#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// Docs/SkillNetworkFrameworkPlan.md G2c, Edit Mode: the `stateEffects` contract of a skill (state, stat source, Buffs): what parses, what is refused, what content validation catches.
// The engine behavior (authority applies on start, takes back on any end, finisher holds) is covered by the Eve JSON configuration checks, the Burst Buff checks and the golden trace.
public static class PNStateEffectChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("State effect check " + n + ": " + what); n++; }
        bool Refused(string json) { try { PNStateEffectData.ParseAll(JToken.Parse(json)); return false; } catch (FormatException) { return true; } }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        const string ok = "[{\"state\":\"project_n:eve_burst_state\",\"statSource\":{\"id\":\"project_n:t\",\"modifiers\":[{\"statId\":\"project_n:attack_power\",\"value\":5,\"group\":0}]},\"buffs\":[\"base:eve_blazing_assault\"]}]";

        var parsed = PNStateEffectData.ParseAll(JToken.Parse(ok));
        Check(parsed.Count == 1 && parsed[0].State.ToString() == "project_n:eve_burst_state" && parsed[0].Modifiers.Count == 1 && parsed[0].BuffIds.Count == 1, "valid declaration did not parse");
        Check(PNStateEffectData.ParseAll(null).Count == 0, "no declaration is no effect");
        Check(PNStateEffectData.ParseAll(JToken.Parse("[{\"state\":\"project_n:a\",\"buffs\":[\"base:b\"]}]"))[0].SourceId.IsValid == false, "a Buff-only effect has no stat source");
        Check(Refused("{}"), "an object instead of an array was accepted");
        Check(Refused("[{\"buffs\":[\"base:b\"]}]"), "an entry without state was accepted");
        Check(Refused("[{\"state\":\"project_n:a\"}]"), "an entry with neither a stat source nor Buffs was accepted");
        Check(Refused("[{\"state\":\"project_n:a\",\"buffs\":[\"base:b\"],\"extra\":1}]"), "an unknown field was accepted");
        Check(Refused("[{\"state\":\"project_n:a\",\"buffs\":[\"base:b\",\"base:b\"]}]"), "a repeated Buff was accepted");
        Check(Refused("[{\"state\":\"project_n:a\",\"buffs\":[\"base:b\"]},{\"state\":\"project_n:a\",\"buffs\":[\"base:c\"]}]"), "a repeated state was accepted");
        Check(Refused("[{\"state\":\"project_n:a\",\"statSource\":{\"id\":\"project_n:t\",\"modifiers\":[]}}]"), "a stat source without modifiers was accepted");
        Check(Refused("[{\"state\":\"project_n:a\",\"statSource\":{\"modifiers\":[{\"statId\":\"project_n:attack_power\",\"value\":1,\"group\":0}]}}]"), "a stat source without id was accepted");
        Check(Refused("[{\"state\":\"project_n:a\",\"statSource\":{\"id\":\"project_n:t\",\"modifiers\":[{\"statId\":\"project_n:attack_power\",\"group\":0}]}}]"), "a modifier without value was accepted");
        parsed[0].Validate(hub);
        Check(true, "valid declaration validates");
        bool Invalid(string json) { try { PNStateEffectData.ParseAll(JToken.Parse(json))[0].Validate(hub); return false; } catch (Exception) { return true; } }
        Check(Invalid("[{\"state\":\"project_n:a\",\"buffs\":[\"base:no_such_buff\"]}]"), "an unknown Buff passed validation");
        Check(Invalid("[{\"state\":\"project_n:a\",\"statSource\":{\"id\":\"project_n:t\",\"modifiers\":[{\"statId\":\"project_n:no_such_stat\",\"value\":1,\"group\":0}]}}]"), "an unknown stat passed validation");
        return "PASS " + n + " state effect contract checks (parse, refusals, content validation)";
    }
}
#endif
