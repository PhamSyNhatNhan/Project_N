#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;

// Opt-in Player review of the Starreaper in the real ErMode run (the Flow Player, started by PNMenuPlayReview solo with -pn-menu-play-starreaper). The player stands 1.2 m from it and the
// review records what the AI really does for 45 s: which skill starts when (from the cooldown Abilities), the retreat, the shadow and its arrows, the pierce arrow's flight and hits,
// the rain's warnings and landings, and the damage the player takes; then it puts the player under one landing of a rain to prove the explosion damages. Not production code.
// Results: <output>/starreaper.txt and PNG frames.
public static class PNStarreaperPlayReview
{
    public static IEnumerator Case(string output, Action<Exception> fail, Func<bool> hasFailed)
    {
        var lines = new List<string>(); int shots = 0;
        void Log(string s) { lines.Add(Time.realtimeSinceStartup.ToString("0.00") + "s " + s); }
        void Shot(string label) { ScreenCapture.CaptureScreenshot(Path.Combine(output, "sc_" + (shots++).ToString("00") + "_" + label + ".png")); }
        string path = Path.Combine(output, "starreaper.txt");
        PNStarreaperSkill skill = null; PNPlayerStat player = null; float start = Time.realtimeSinceStartup;
        while ((skill == null || player == null || !skill.IsReady || !player.IsReady) && Time.realtimeSinceStartup - start < 40f)
        {
            if (skill == null) skill = UnityEngine.Object.FindFirstObjectByType<PNStarreaperSkill>();
            if (player == null) player = UnityEngine.Object.FindFirstObjectByType<PNPlayerStat>();
            yield return null;
        }
        if (skill == null || player == null || !skill.IsReady) { File.WriteAllLines(path, lines); fail(new Exception("Starreaper / player never became ready (reaper=" + (skill != null) + " player=" + (player != null) + ")")); yield break; }
        // the other mobs would also attack the player: retire them so the log is about the Starreaper only
        foreach (var other in UnityEngine.Object.FindObjectsByType<PNEnemySkill>(FindObjectsSortMode.None).Where(x => !ReferenceEquals(x, skill)).ToList())
        { PNEntityHosts.Find(EveRegistrySession.Context)?.Retire(other.GetComponent<EveEntityLoader>()); Log("retired " + other.name); }
        var reaper = skill.GetComponent<PNStat>(); var health = PNContentIds.Health;
        var abilities = skill.GetComponent<PNAbilityManager>();
        var ids = new[] { PNStarreaperCombatContent.Pierce, PNStarreaperCombatContent.Afterimage, PNStarreaperCombatContent.Rain };
        PNAbilityCooldown Cd(EveId id) => abilities.Abilities.GetCooldown(EveId.Parse(id + "_ready"));
        float Gap() { var d = reaper.transform.position - player.transform.position; d.y = 0; return d.magnitude; }
        void KeepPlayer() { if (player.IsReady && !player.IsDead && player.GetCurrent(health) < player.GetMax(health) * .5) player.SetCurrent(health, player.GetMax(health)); }
        yield return new WaitForSeconds(.5f);
        var pp = reaper.transform.position + new Vector3(1.2f, 0f, 0f); player.transform.position = new Vector3(pp.x, player.transform.position.y, pp.z); player.SetCurrent(health, player.GetMax(health));
        Log("ready: reaper " + reaper.transform.position.ToString("F2") + " player " + player.transform.position.ToString("F2") + " gap " + Gap().ToString("F2") + " visual " + skill.GetComponent<PNVisualBody>()?.Actor?.Animator.BaseState
            + " hp " + reaper.GetCurrent(health).ToString("F0") + " atk " + reaper.Evaluate(EveId.Parse("base:attack_power")).ToString("F0") + " speed " + reaper.Evaluate(EveId.Parse("base:move_speed")).ToString("F2"));
        Shot("start");

        var ready = ids.ToDictionary(i => i, i => true); var castAt = new Dictionary<EveId, List<float>>(); foreach (var i in ids) castAt[i] = new List<float>();
        float t0 = Time.realtimeSinceStartup, lastHp = (float)player.GetCurrent(health), lastLog = 0f; var hits = new List<string>();
        var seenArrows = new HashSet<int>(); bool moved = false, shotShadow = false, shotRain = false, shotArrow = false, probed = false; float rainSeenAt = -1f; Vector3 retreatFrom = Vector3.zero; bool retreatNoted = false;
        int maxArrows = 0, maxShadows = 0, maxRains = 0; var arrowStart = new Dictionary<int, Vector3>(); var arrowLife = new Dictionary<int, float>(); var arrowSeen = new Dictionary<int, float>();
        var shadowAt = -1f; var arrowSpawns = new List<string>();
        while (Time.realtimeSinceStartup - t0 < 45f)
        {
            float now = Time.realtimeSinceStartup - t0;
            foreach (var id in ids) { bool r = Cd(id).CanConsume(); if (ready[id] && !r) { castAt[id].Add(now); Log("CAST " + id.ToString().Replace("project_n:starreaper_", "") + " starts; gap " + Gap().ToString("F2") + " reaper " + reaper.transform.position.ToString("F2") + " player " + player.transform.position.ToString("F2")); if (id == PNStarreaperCombatContent.Afterimage) { retreatFrom = reaper.transform.position; retreatNoted = true; } } ready[id] = r; }
            if (!moved && now > 20f) { moved = true; var q = reaper.transform.position + new Vector3(-2.2f, 0f, 0f); player.transform.position = new Vector3(q.x, player.transform.position.y, q.z); lastHp = (float)player.GetCurrent(health); Log("player moved to 2.2 m (inside the rain range) at " + now.ToString("F1")); }
            float hp = (float)player.GetCurrent(health); if (hp < lastHp - .5f) { hits.Add("-" + (lastHp - hp).ToString("F0") + "@" + now.ToString("F2")); Log("player hit -" + (lastHp - hp).ToString("F0") + " (hp " + hp.ToString("F0") + ") gap " + Gap().ToString("F2")); } lastHp = hp;
            if (hp < player.GetMax(health) * .4) { player.SetCurrent(health, player.GetMax(health)); lastHp = (float)player.GetMax(health); }
            var arrows = Resources.FindObjectsOfTypeAll<PNStarreaperProjectile>().Where(x => x.IsRunning).ToArray(); maxArrows = Mathf.Max(maxArrows, arrows.Length);
            foreach (var a in arrows) { int key = a.GetHashCode(); if (seenArrows.Add(key)) { arrowStart[key] = a.transform.position; arrowSeen[key] = now; var h = a.transform.forward; Log("ARROW appears at " + a.transform.position.ToString("F2") + " heading " + h.ToString("F2") + " (reaper " + reaper.transform.position.ToString("F2") + ")"); if (!shotArrow) { shotArrow = true; Shot("arrow"); } } arrowLife[key] = now - arrowSeen[key]; }
            var shadows = Resources.FindObjectsOfTypeAll<PNStarreaperShadowSummon>().Where(x => x.IsRunning).ToArray(); maxShadows = Mathf.Max(maxShadows, shadows.Length);
            if (shadows.Length > 0 && shadowAt < 0) { shadowAt = now; Log("SHADOW appears at " + shadows[0].transform.position.ToString("F2") + " (the reaper has moved " + (retreatNoted ? Vector3.Distance(retreatFrom, reaper.transform.position).ToString("F2") : "?") + " m)"); Shot("shadow"); }
            if (shadows.Length > 0 && !shotShadow && now - shadowAt > 1.9f) { shotShadow = true; Shot("shadow_fire"); }
            if (shadows.Length == 0 && shadowAt >= 0 && shadowAt > -1) { Log("SHADOW gone after " + (now - shadowAt).ToString("F2") + " s"); shadowAt = -2; }
            var rains = Resources.FindObjectsOfTypeAll<PNStarreaperRainBlast>().Where(x => x.IsRunning).OrderBy(x => x.Delay).ToArray(); maxRains = Mathf.Max(maxRains, rains.Length);
            if (rains.Length > 0)
            {
                var rain = rains[0]; var rainPoints = rains.Select(x => x.Center).ToList();
                if (rainSeenAt < 0) { rainSeenAt = now; Log("RAIN blasts appear with " + rainPoints.Count + " landing points (centre-ish " + rainPoints[0].ToString("F2") + ")"); Shot("rain_warning"); }
                if (!probed && rain.Age > .55f && rainPoints.Count > 0)
                {
                    probed = true; var under = rainPoints[rainPoints.Count - 1]; player.transform.position = new Vector3(under.x, player.transform.position.y, under.z); lastHp = (float)player.GetCurrent(health);
                    Log("PROBE: player placed under the LAST landing " + under.ToString("F2") + " at rain age " + rain.Age.ToString("F2") + "; hp " + lastHp.ToString("F0"));
                }
                if (!shotRain && rain.Age > 1.05f) { shotRain = true; Shot("rain_landing"); }
            }
            else if (rainSeenAt >= 0) { Log("RAIN blasts gone after " + (now - rainSeenAt).ToString("F2") + " s"); rainSeenAt = -1f; }
            if (now - lastLog > 3f) { lastLog = now; Log("diag: gap " + Gap().ToString("F2") + " state " + skill.GetComponent<PNVisualBody>()?.Actor?.Animator.BaseState + " casting " + skill.IsExecutingEnemyAction + " casts " + skill.CastCount + " arrows " + arrows.Length); }
            yield return null;
        }
        foreach (var id in ids) Log("casts " + id.ToString().Replace("project_n:starreaper_", "") + ": " + castAt[id].Count + " at [" + string.Join(", ", castAt[id].Select(x => x.ToString("F1"))) + "]");
        Log("arrows seen " + seenArrows.Count + " (max alive " + maxArrows + "), max shadows " + maxShadows + ", max rains " + maxRains + ", player hits " + hits.Count + ": " + string.Join(" ", hits));
        Log("arrow lifetimes: " + string.Join(" ", arrowLife.Values.Select(x => x.ToString("F1"))));
        bool allCast = ids.All(i => castAt[i].Count >= 1), oneShadow = maxShadows <= 1;
        Log("summary: allSkillsCast=" + allCast + " atMostOneShadow=" + oneShadow + " rainProbeHit=" + (hits.Any()));
        File.WriteAllLines(path, lines);
        if (!allCast || !oneShadow) fail(new Exception("Starreaper review incomplete: allSkillsCast=" + allCast + " atMostOneShadow=" + oneShadow));
    }
}
#endif
