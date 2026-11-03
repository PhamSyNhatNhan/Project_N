#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;

// Opt-in Player review of the Starchanter in the real ErMode run (the Flow Player, started by PNMenuPlayReview solo with -pn-menu-play-starchanter): the Starkeeper is its ally (same layer).
// Records what the AI really does: the boost (walks to the ally, speed_boost lands on it), the heal (teleport next to the hurt ally, guard on itself, ticks of 5% every 1.5 s, 10% at the end)
// and the guard break (two hits on the Starchanter end the cast: no more ticks, no final heal). Not production code. Results: <output>/starchanter.txt and PNG frames.
public static class PNStarchanterPlayReview
{
    public static IEnumerator Case(string output, Action<Exception> fail, Func<bool> hasFailed)
    {
        var lines = new List<string>(); int shots = 0;
        void Log(string s) { lines.Add(Time.realtimeSinceStartup.ToString("0.0") + "s " + s); }
        void Shot(string label) { ScreenCapture.CaptureScreenshot(Path.Combine(output, "sc_" + (shots++).ToString("00") + "_" + label + ".png")); }
        string path = Path.Combine(output, "starchanter.txt");
        PNStarchanterSkill chanterSkill = null; PNStarkeeperSkill keeperSkill = null; PNPlayerStat player = null; float start = Time.realtimeSinceStartup;
        while ((chanterSkill == null || keeperSkill == null || player == null || !chanterSkill.IsReady || !keeperSkill.IsReady || !player.IsReady) && Time.realtimeSinceStartup - start < 40f)
        {
            if (chanterSkill == null) chanterSkill = UnityEngine.Object.FindFirstObjectByType<PNStarchanterSkill>();
            if (keeperSkill == null) keeperSkill = UnityEngine.Object.FindFirstObjectByType<PNStarkeeperSkill>();
            if (player == null) player = UnityEngine.Object.FindFirstObjectByType<PNPlayerStat>();
            yield return null;
        }
        if (chanterSkill == null || keeperSkill == null || player == null || !chanterSkill.IsReady) { File.WriteAllLines(path, lines); fail(new Exception("Starchanter / Starkeeper / player never became ready (chanter=" + (chanterSkill != null) + " keeper=" + (keeperSkill != null) + " player=" + (player != null) + ")")); yield break; }
        var weaver = UnityEngine.Object.FindFirstObjectByType<PNStarweaverSkill>();
        if (weaver != null) { PNEntityHosts.Find(EveRegistrySession.Context)?.Retire(weaver.GetComponent<EveEntityLoader>()); Log("Starweaver retired"); }
        var chanter = chanterSkill.GetComponent<PNStat>(); var keeper = keeperSkill.GetComponent<PNStat>(); var chanterBuffs = chanterSkill.GetComponent<PNBuffManager>(); var keeperBuffs = keeperSkill.GetComponent<PNBuffManager>();
        var health = PNContentIds.Health; var speedId = PNSupportBuffContent.SpeedBoost; var guardId = PNSupportBuffContent.BlockHits;
        void KeepPlayer() { if (player.IsReady && !player.IsDead) player.SetCurrent(health, player.GetMax(health)); }
        bool Has(PNBuffManager m, EveId id) => m != null && m.IsReady && m.Buffs.Snapshot().Any(x => x.Id == id);
        double GuardHits() { var b = chanterBuffs.Buffs.Snapshot().FirstOrDefault(x => x.Id == guardId); return b == null ? -1 : b.Stacks ?? -1; }
        float Gap() { var a = chanter.transform.position - keeper.transform.position; a.y = 0; return a.magnitude; }
        yield return new WaitForSeconds(.5f);
        Log("ready: chanter " + chanter.transform.position.ToString("F2") + " keeper " + keeper.transform.position.ToString("F2") + " gap " + Gap().ToString("F2") + " keeper hp " + keeper.GetCurrent(health).ToString("F0") + "/" + keeper.GetMax(health).ToString("F0")
            + " chanter visual " + chanterSkill.GetComponent<PNVisualBody>()?.Actor?.Animator.BaseState + " layers " + chanter.Layer + "/" + keeper.Layer);
        Shot("start");

        // ---- 1. the AI has probably cast its first boost already (it is ready at spawn): just record the state ----
        float t0 = 0f;
        int Boosts(PNBuffManager m) => m == null || !m.IsReady ? 0 : m.Buffs.Snapshot().Count(x => x.Id == speedId);
        Log("start state: speed_boost instances on the keeper " + Boosts(keeperBuffs) + ", on the Starchanter " + Boosts(chanterBuffs) + ", chanter casts " + chanterSkill.CastCount);
        Shot("boost_start");

        // ---- 2. heal: the ally is hurt (40%): teleport, guard, ticks, final ----
        keeper.SetCurrent(health, keeper.GetMax(health) * .4); float baseline = (float)keeper.GetCurrent(health), max = (float)keeper.GetMax(health);
        Log("keeper hurt to " + baseline.ToString("F0") + "/" + max.ToString("F0")); yield return new WaitForSeconds(.2f);
        t0 = Time.realtimeSinceStartup; bool casting = false, tele = false; float lastHp = baseline, castStart = 0f; var ticks = new List<string>(); float gapAtChannel = -1f; double guardAtChannel = -1; bool shotTele = false, shotChannel = false;
        while (Time.realtimeSinceStartup - t0 < 16f)
        {
            KeepPlayer(); float hp = (float)keeper.GetCurrent(health);
            if (!casting && chanterSkill.IsExecutingEnemyAction) { casting = true; castStart = Time.realtimeSinceStartup; Log("heal cast begins, gap " + Gap().ToString("F2") + ", chanter visual " + chanterSkill.GetComponent<PNVisualBody>()?.Actor?.Animator.BaseState); Shot("heal_begin"); }
            if (casting && !tele && Gap() < 1.2f && Time.realtimeSinceStartup - castStart > .3f) { tele = true; Log("teleported next to the ally after " + (Time.realtimeSinceStartup - castStart).ToString("F2") + " s, gap " + Gap().ToString("F2")); }
            if (tele && gapAtChannel < 0 && Has(chanterBuffs, guardId)) { gapAtChannel = Gap(); guardAtChannel = GuardHits(); Log("guard on the Starchanter: hits " + guardAtChannel + " at " + (Time.realtimeSinceStartup - castStart).ToString("F2") + " s"); }
            if (tele && !shotChannel && Time.realtimeSinceStartup - castStart > 2.2f) { shotChannel = true; Shot("heal_channel"); }
            if (hp > lastHp + .5f) { ticks.Add("+" + (hp - lastHp).ToString("F0") + "@" + (Time.realtimeSinceStartup - castStart).ToString("F2")); }
            lastHp = hp;
            if (casting && !chanterSkill.IsExecutingEnemyAction) break;
            yield return null;
        }
        float gained = lastHp - baseline;
        Log("heal: ticks " + string.Join(" ", ticks) + "; total +" + gained.ToString("F0") + " of " + (max * .25f).ToString("F0") + " expected (3 x 5% + 10%); guard hits " + guardAtChannel + "; cast took " + (Time.realtimeSinceStartup - castStart).ToString("F2") + " s");
        bool healOk = casting && tele && ticks.Count >= 4 && Mathf.Abs(gained - max * .25f) < max * .02f && guardAtChannel == 2;

        // ---- 3. guard break: a second heal, two hits on the Starchanter end it early ----
        keeper.SetCurrent(health, keeper.GetMax(health) * .4); baseline = (float)keeper.GetCurrent(health); lastHp = baseline; t0 = Time.realtimeSinceStartup; casting = false; float breakAt = 0f; var after = new List<string>();
        Log("second heal (guard break): waiting for the cooldown");
        bool hitDone = false, castEnded = false; float endAt = 0f, hpAtBreak = 0f;
        while (Time.realtimeSinceStartup - t0 < 30f)
        {
            KeepPlayer(); float hp = (float)keeper.GetCurrent(health);
            if (!casting && chanterSkill.IsExecutingEnemyAction) { casting = true; castStart = Time.realtimeSinceStartup; Log("second heal begins"); }
            if (casting && !hitDone && Has(chanterBuffs, guardId) && Time.realtimeSinceStartup - castStart > 2.2f)
            {
                hitDone = true; breakAt = Time.realtimeSinceStartup; hpAtBreak = hp;
                for (int i = 0; i < 3; i++)
                {
                    var r = chanter.ReceiveDamage(new PNDamage(chanter.SessionId, keeper.EntityId, 30, new[] { PNContentIds.Physical }).WithIFrameDuration(0d));
                    Log("hit " + (i + 1) + " on the Starchanter: ignored=" + r.Ignored + " guard hits left " + GuardHits());
                }
                Shot("guard_break");
            }
            if (hitDone && !castEnded && !chanterSkill.IsExecutingEnemyAction) { castEnded = true; endAt = Time.realtimeSinceStartup; Log("cast ended " + (endAt - breakAt).ToString("F2") + " s after the hits; keeper hp " + hp.ToString("F0") + " (at the break " + hpAtBreak.ToString("F0") + ")"); }
            if (hitDone && Time.realtimeSinceStartup - breakAt > 6f) break;
            lastHp = hp; yield return null;
        }
        float finalHp = (float)keeper.GetCurrent(health);
        bool breakOk = hitDone && castEnded && endAt - breakAt < 1.5f && finalHp - hpAtBreak < max * .06f;
        Log("guard break: ended early=" + castEnded + ", hp after the break +" + (finalHp - hpAtBreak).ToString("F0") + " (a tick is " + (max * .05f).ToString("F0") + ", the final heal " + (max * .1f).ToString("F0") + ")");
        // ---- 4. a boost on purpose: nobody hurt, wait for the cooldown and watch it walk to the ally and boost it ----
        keeper.SetCurrent(health, keeper.GetMax(health)); yield return new WaitForSeconds(.3f);
        int before = Boosts(keeperBuffs); float minGap = Gap(), gapWhenBoosted = -1f; t0 = Time.realtimeSinceStartup; bool walkedFar = false, selfBoostSeen = false; float startGap = Gap();
        Log("boost: keeper instances " + before + ", gap " + startGap.ToString("F2"));
        // the ally moves on its own (it chases the idle player); the player is parked far away so the keeper stands still near the Starchanter's reach
        while (Time.realtimeSinceStartup - t0 < 30f && Boosts(keeperBuffs) <= before)
        {
            KeepPlayer(); minGap = Mathf.Min(minGap, Gap()); selfBoostSeen |= Boosts(chanterBuffs) > 0; yield return null;
        }
        bool boosted = Boosts(keeperBuffs) > before; if (boosted) gapWhenBoosted = Gap();
        Log("boost: new instance on the keeper=" + boosted + " after " + (Time.realtimeSinceStartup - t0).ToString("F1") + " s; gap at the start " + startGap.ToString("F2") + ", closest " + minGap.ToString("F2") + ", when boosted " + gapWhenBoosted.ToString("F2")
            + "; Starchanter instances " + Boosts(chanterBuffs) + "; keeper move speed " + keeper.Evaluate(EveId.Parse("base:move_speed")).ToString("F2"));
        Shot("boost");
        bool boostOk = boosted && gapWhenBoosted <= 2.2f && Boosts(chanterBuffs) > 0;
        // ---- 5. alone: the Starkeeper is gone, a hostile (the player) stands 3 m away: it walks up and strikes every 5 s ----
        PNEntityHosts.Find(EveRegistrySession.Context)?.Retire(keeperSkill.GetComponent<EveEntityLoader>()); Log("Starkeeper retired: the Starchanter has no ally left"); yield return new WaitForSeconds(.5f);
        var pp = chanter.transform.position + new Vector3(3f, 0f, 0f); player.transform.position = new Vector3(pp.x, player.transform.position.y, pp.z); player.SetCurrent(health, player.GetMax(health));
        var strikes = new List<float>(); float lastPlayerHp = (float)player.GetCurrent(health); t0 = Time.realtimeSinceStartup; float minReach = 99f; bool shotStrike = false; float diagAt = 0f; bool diag2 = false;
        while (Time.realtimeSinceStartup - t0 < 16f)
        {
            float hp = (float)player.GetCurrent(health); var d = chanter.transform.position - player.transform.position; d.y = 0; minReach = Mathf.Min(minReach, d.magnitude);
            if (hp < lastPlayerHp - .5f) { strikes.Add(Time.realtimeSinceStartup - t0); Log("strike on the player: -" + (lastPlayerHp - hp).ToString("F0") + " at distance " + d.magnitude.ToString("F2") + " m"); if (!shotStrike) { shotStrike = true; Shot("strike"); } }
            if (Time.realtimeSinceStartup - t0 > diagAt) { diagAt += 1.5f; var sc = chanterSkill.GetComponent<PNAbilityManager>(); Log("diag: distance " + d.magnitude.ToString("F2") + " chanter " + chanter.transform.position.ToString("F2") + " player " + player.transform.position.ToString("F2") + " executing=" + chanterSkill.IsExecutingEnemyAction + " casts=" + chanterSkill.CastCount + " visual=" + chanterSkill.GetComponent<PNVisualBody>()?.Actor?.Animator.BaseState + " playerLayer=" + player.Layer + " targets=" + chanter.Targets(player)); }
            if (Time.realtimeSinceStartup - t0 > diagAt - 1.4f && !diag2) { diag2 = true; var everyone = UnityEngine.Object.FindObjectsByType<PNStat>(FindObjectsSortMode.None).ToList();
                var ally2 = PNStarchanterSupport.NearestAlly(chanter, everyone, 8f); var host1 = PNStarchanterSupport.NearestHostile(chanter, everyone, 8f); var host2 = PNStarchanterSupport.NearestHostile(chanter, everyone, 8f, s2 => PNWorldGameplay.CanTarget(chanterSkill, s2));
                var from = chanter.transform.position + Vector3.up * .2f; var dd = player.transform.position + Vector3.up * .2f - from; var buf = new RaycastHit[8]; int rays = chanter.gameObject.scene.GetPhysicsScene().Raycast(from, dd.normalized, buf, dd.magnitude, LayerMask.GetMask("Wall"), QueryTriggerInteraction.Ignore);
                Log("diag2: stats " + everyone.Count + " ally=" + (ally2 != null ? ally2.name + " ready=" + ally2.IsReady + " dead=" + ally2.IsDead : "none") + " hostile(no filter)=" + (host1 != null ? host1.name : "none") + " hostile(CanTarget)=" + (host2 != null ? host2.name : "none") + " wallRayHits=" + rays
                    + " strikeReady=" + chanterSkill.GetComponent<PNAbilityManager>()?.Abilities.GetCooldown(EveId.Parse(PNStarchanterCombatContent.Strike + "_ready")).CanConsume()); }
            lastPlayerHp = hp; yield return null;
        }
        float minGapBetween = 99f; for (int i = 1; i < strikes.Count; i++) minGapBetween = Mathf.Min(minGapBetween, strikes[i] - strikes[i - 1]);
        bool strikeOk = strikes.Count >= 2 && minGapBetween >= 4.5f && minReach <= 1.4f;
        Log("alone: " + strikes.Count + " strikes, closest " + minReach.ToString("F2") + " m, shortest gap between strikes " + (strikes.Count > 1 ? minGapBetween.ToString("F1") : "-") + " s (expected >= 4.5 and a close reach)");
        Log("summary: boost=" + boostOk + " heal=" + healOk + " guardBreak=" + breakOk + " strike=" + strikeOk);
        File.WriteAllLines(path, lines);
        if (!boostOk || !healOk || !breakOk || !strikeOk) fail(new Exception("Starchanter review incomplete: boost=" + boostOk + " heal=" + healOk + " guardBreak=" + breakOk + " strike=" + strikeOk));
    }
}
#endif
