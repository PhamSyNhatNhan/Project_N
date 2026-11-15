#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

// Docs/SkillNetworkFrameworkPlan.md G8, Edit Mode: the presentation channel (the look of an entity that is not an Action): the authority writes, a client only applies the picture, limits hold.
public static class PNPresentationChecks
{
    public static string Run()
    {
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Presentation check " + n + ": " + what); n++; }
        bool Throws(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } catch (InvalidOperationException) { return true; } }
        var log = new List<string>(); int dirty = 0;
        var authority = new PNPresentation(() => true); authority.Changed += (k, v) => log.Add(k + "=" + v); authority.Dirty += () => dirty++;
        Check(authority.Get("burst") == "" && authority.Count == 0, "nothing is set at first");
        Check(authority.Set("burst", "1") && authority.Get("burst") == "1" && log.Count == 1 && log[0] == "burst=1" && dirty == 1, "the authority sets a value (Changed and Dirty raised)");
        Check(!authority.Set("burst", "1") && log.Count == 1 && dirty == 1, "the same value changes nothing");
        Check(authority.Set("burst", "") && authority.Get("burst") == "" && log[1] == "burst=" && authority.Count == 0 && dirty == 2, "an empty value clears the key");
        Check(!authority.Set("burst", "") && dirty == 2, "clearing a missing key changes nothing");
        Check(Throws(() => authority.Set("Burst", "1")) && Throws(() => authority.Set("", "1")) && Throws(() => authority.Set("a b", "1")) && Throws(() => authority.Set(new string('a', 25), "1")), "a key is lowercase letters, digits and underscores, 1..24");
        Check(Throws(() => authority.Set("burst", new string('x', 25))) && Throws(() => authority.Set("burst", null)), "a value is text of at most 24 characters");
        for (int i = 0; i < PNPresentation.MaxEntries; i++) authority.Set("k" + i, "1");
        Check(Throws(() => authority.Set("extra", "1")) && authority.Count == PNPresentation.MaxEntries, "an entity holds at most " + PNPresentation.MaxEntries + " values");
        Check(authority.Set("k0", "2"), "an existing key can still change when the entity is full");

        var client = new PNPresentation(() => false); var seen = new List<string>(); client.Changed += (k, v) => seen.Add(k + "=" + v);
        Check(!client.Set("burst", "1") && client.Count == 0 && seen.Count == 0, "a client's Set is ignored");
        Check(client.Apply(new Dictionary<string, string> { ["burst"] = "1", ["charge"] = "3" }) && client.Get("burst") == "1" && client.Get("charge") == "3" && seen.Count == 2, "a client applies the authority's picture (Changed per key)");
        seen.Clear(); Check(client.Apply(new Dictionary<string, string> { ["burst"] = "1", ["charge"] = "3" }) && seen.Count == 0, "the same picture changes nothing");
        Check(client.Apply(new Dictionary<string, string> { ["burst"] = "1" }) && client.Get("charge") == "" && seen.Count == 1 && seen[0] == "charge=", "a key that left the picture is cleared");
        seen.Clear();
        Check(!client.Apply(new Dictionary<string, string> { ["burst"] = "" }) && !client.Apply(new Dictionary<string, string> { ["Bad"] = "1" }) && !client.Apply(null) && client.Get("burst") == "1" && seen.Count == 0, "a malformed picture applies nothing");
        var tooMany = new Dictionary<string, string>(); for (int i = 0; i <= PNPresentation.MaxEntries; i++) tooMany["k" + i] = "1";
        Check(!client.Apply(tooMany) && client.Count == 1, "a picture with too many values applies nothing");
        client.Reset(); Check(client.Count == 0 && seen.Count == 0, "Reset forgets silently");

#if UNITY_EDITOR
        var parsed = PNPresentationSync.Parse(new JObject { ["v"] = new JObject { ["burst"] = "1" } });
        Check(parsed.Count == 1 && parsed["burst"] == "1", "the block is read");
        bool Bad(JToken token) { try { PNPresentationSync.Parse(token); return false; } catch (FormatException) { return true; } }
        Check(Bad(new JObject()) && Bad(new JObject { ["v"] = new JObject { ["burst"] = 1 } }) && Bad(new JObject { ["v"] = new JObject { ["burst"] = "" } }) && Bad(new JObject { ["v"] = new JObject { ["Bad"] = "1" } }), "a malformed block is refused");
#endif
        return "PASS " + n + " presentation checks (authority writes, client applies the picture, late picture, limits, block parse)";
    }
}
#endif
