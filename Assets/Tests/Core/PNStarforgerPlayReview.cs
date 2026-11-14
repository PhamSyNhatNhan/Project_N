#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;

// Opt-in Player review of the Starforger in the real ErMode run (the Flow Player, started by PNMenuPlayReview solo with -pn-menu-play-starforger). The player stands 3 m from it and the review
// records what the AI really does for 50 s: when the slam and the star casts start (from the cooldown Abilities), the jump (height, landing against the player's spot at take-off), the damage the
// player takes (the slam's 300%, the star bolts' 50%), the stars (count, slots, bolts), the extra bolts at take-off, and the invulnerability and the control immunity while airborne (a damage and
// a control Buff are thrown at it in flight and, for comparison, a damage during the crouch). Not production code. Results: <output>/starforger.txt and PNG frames.
public static class PNStarforgerPlayReview
{
    public static IEnumerator Case(string output, Action<Exception> fail, Func<bool> hasFailed)
    {
        var lines = new List<string>(); int shots = 0;
        void Log(string s) { lines.Add(Time.realtimeSinceStartup.ToString("0.00") + "s " + s); }
        void Shot(string label) { ScreenCapture.CaptureScreenshot(Path.Combine(output, "sc_" + (shots++).ToString("00") + "_" + label + ".png")); }
        string path = Path.Combine(output, "starforger.txt");
        PNStarforgerSkill skill = null; PNPlayerStat player = null; float start = Time.realtimeSinceStartup;
        while ((skill == null || player == null || !skill.IsReady || !player.IsReady) && Time.realtimeSinceStartup - start < 40f)
        {
            if (skill == null) skill = UnityEngine.Object.FindFirstObjectByType<PNStarforgerSkill>();
            if (player == null) player = UnityEngine.Object.FindFirstObjectByType<PNPlayerStat>();
            yield return null;
        }
        if (skill == null || player == null || !skill.IsReady) { File.WriteAllLines(path, lines); fail(new Exception("Starforger / player never became ready (forger=" + (skill != null) + " player=" + (player != null) + ")")); yield break; }
        foreach (var other in UnityEngine.Object.FindObjectsByType<PNEnemySkill>(FindObjectsSortMode.None).Where(x => !ReferenceEquals(x, skill)).ToList())
        { PNEntityHosts.Find(EveRegistrySession.Context)?.Retire(other.GetComponent<EveEntityLoader>()); Log("retired " + other.name); }
        var forger = skill.GetComponent<PNStat>(); var health = PNContentIds.Health; var abilities = skill.GetComponent<PNAbilityManager>(); var buffs = skill.GetComponent<PNBuffManager>();
        var ids = new[] { PNStarforgerCombatContent.Slam, PNStarforgerCombatContent.Star };
        PNAbilityCooldown Cd(EveId id) => abilities.Abilities.GetCooldown(EveId.Parse(id + "_ready"));
        float Gap() { var d = forger.transform.position - player.transform.position; d.y = 0; return d.magnitude; }
        yield return new WaitForSeconds(.5f);
        var pp = forger.transform.position + new Vector3(1.2f, 0f, 0f); player.transform.position = new Vector3(pp.x, player.transform.position.y, pp.z); player.SetCurrent(health, player.GetMax(health));
        Log("ready: forger " + forger.transform.position.ToString("F2") + " player " + player.transform.position.ToString("F2") + " gap " + Gap().ToString("F2") + " visual " + skill.GetComponent<PNVisualBody>()?.Actor?.Animator.BaseState
            + " hp " + forger.GetCurrent(health).ToString("F0") + " atk " + forger.Evaluate(EveId.Parse("base:attack_power")).ToString("F0") + " speed " + forger.Evaluate(EveId.Parse("base:move_speed")).ToString("F2"));
        Shot("start");

        var ready = ids.ToDictionary(i => i, i => true); var castAt = new Dictionary<EveId, List<float>>(); foreach (var i in ids) castAt[i] = new List<float>();
        float lastNudge = -9f; float t0 = Time.realtimeSinceStartup, lastHp = (float)player.GetCurrent(health), lastLog = 0f, maxHeight = 0f; var hits = new List<string>();
        int crouchShots = 0; var boltTimes = new List<float>(); var liftoffs = new List<float>(); bool lifted = false; bool airborne = false, probedAir = false, probedCrouch = false, shotAir = false, shotLand = false, shotStars = false; float takeoff = -1f, landed = -1f, castSlam = -1f; Vector3 takeoffFrom = Vector3.zero, playerAtTakeoff = Vector3.zero, landPos = Vector3.zero;
        int maxStars = 0; var starSlots = new HashSet<int>(); var seenBolts = new HashSet<int>(); int boltTotal = 0; int boltsAtTakeoff = 0; float boltWindow = -1f; int starsAtTakeoff = 0;
        string airProbe = "-", airControl = "-", crouchProbe = "-";
        while (Time.realtimeSinceStartup - t0 < 50f)
        {
            float now = Time.realtimeSinceStartup - t0;
            foreach (var id in ids) { bool r = Cd(id).CanConsume(); if (ready[id] && !r) { castAt[id].Add(now); if (id == PNStarforgerCombatContent.Slam) { castSlam = now; probedCrouch = false; } Log("CAST " + id.ToString().Replace("project_n:starforger_", "") + " starts; gap " + Gap().ToString("F2") + " forger " + forger.transform.position.ToString("F2") + " player " + player.transform.position.ToString("F2") + " stars " + skill.AliveCount); } ready[id] = r; }
            float hp = (float)player.GetCurrent(health); if (hp < lastHp - .5f) { hits.Add("-" + (lastHp - hp).ToString("F0") + "@" + now.ToString("F2")); Log("player hit -" + (lastHp - hp).ToString("F0") + " (hp " + hp.ToString("F0") + ") gap " + Gap().ToString("F2") + " forger y " + forger.transform.position.y.ToString("F2")); } lastHp = hp;
            if (hp < player.GetMax(health) * .4) { player.SetCurrent(health, player.GetMax(health)); lastHp = (float)player.GetMax(health); }
            if (!airborne && Gap() < .5f && now - lastNudge > 1f && forger.transform.position.y < .05f) { lastNudge = now; var q = forger.transform.position + new Vector3(1.2f, 0f, 0f); player.transform.position = new Vector3(q.x, player.transform.position.y, q.z); Log("player stepped 1.2 m away from the Starforger (it had landed on top)"); }
            float y = forger.transform.position.y; maxHeight = Mathf.Max(maxHeight, y);
            // the crouch: a damage thrown at it must land (not invulnerable yet)
            if (castSlam >= 0 && !probedCrouch && now - castSlam > .25f && now - castSlam < .45f && y < .05f)
            {
                probedCrouch = true; var r = forger.ReceiveDamage(new PNDamage(forger.SessionId, player.EntityId, 30, new[] { PNContentIds.Physical }).WithIFrameDuration(0d)); crouchProbe = "ignored=" + r.Ignored; Log("PROBE crouch: damage thrown at the Starforger " + crouchProbe);
                if (crouchShots++ < 3) Shot("slam_crouch");
            }
            if (y > .02f && !lifted) { lifted = true; liftoffs.Add(now); Log("LIFT-OFF (y>0.02), stars alive " + skill.AliveCount); } if (y <= .02f) lifted = false;
            bool up = y > .3f;
            if (up && !airborne)
            {
                airborne = true; takeoff = now; takeoffFrom = forger.transform.position; playerAtTakeoff = player.transform.position; probedAir = false; starsAtTakeoff = skill.AliveCount; boltWindow = now;
                Log("TAKE-OFF at " + takeoffFrom.ToString("F2") + " " + (castSlam >= 0 ? (now - castSlam).ToString("F2") : "?") + " s after the cast began; player at " + playerAtTakeoff.ToString("F2") + " stars " + starsAtTakeoff);
            }
            if (airborne && !probedAir && y > .5f)
            {
                probedAir = true; var r = forger.ReceiveDamage(new PNDamage(forger.SessionId, player.EntityId, 30, new[] { PNContentIds.Physical }).WithIFrameDuration(0d)); airProbe = "ignored=" + r.Ignored; Log("PROBE air: damage thrown at the Starforger " + airProbe + " (hp " + forger.GetCurrent(health).ToString("F0") + ")");
                if (buffs != null && buffs.IsReady) { var rc = buffs.Buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_control_template"), PNCombatSource.Capture(player))); airControl = rc.Status.ToString() + (string.IsNullOrEmpty(rc.Message) ? "" : "/" + rc.Message); Log("PROBE air: control Buff thrown at the Starforger -> " + airControl); }
                if (!shotAir) { shotAir = true; Shot("slam_air"); }
            }
            if (airborne && y <= .08f)
            {
                airborne = false; landed = now; landPos = forger.transform.position;
                Log("LANDED after " + (landed - takeoff).ToString("F2") + " s in the air (max height " + maxHeight.ToString("F2") + "); at " + landPos.ToString("F2") + ", the player was at " + playerAtTakeoff.ToString("F2") + " at take-off, flat error " + new Vector2(landPos.x - playerAtTakeoff.x, landPos.z - playerAtTakeoff.z).magnitude.ToString("F2") + " m; the player is now " + Gap().ToString("F2") + " m away");
                if (!shotLand) { shotLand = true; Shot("slam_land"); }
            }
            var stars = Resources.FindObjectsOfTypeAll<PNStarforgerStarSummon>().Where(x => x.IsRunning).ToArray(); maxStars = Mathf.Max(maxStars, stars.Length); foreach (var st in stars) starSlots.Add(st.Slot);
            var bolts = Resources.FindObjectsOfTypeAll<PNStarforgerBoltProjectile>().Where(x => x.IsRunning).ToArray();
            var nowBolts = new HashSet<int>(); foreach (var b in bolts) { int key = b.GetHashCode(); nowBolts.Add(key); if (!seenBolts.Contains(key)) { seenBolts.Add(key); boltTimes.Add(now); boltTotal++; if (boltWindow >= 0 && now - boltWindow < .15f) boltsAtTakeoff++; Log("BOLT appears at " + b.transform.position.ToString("F2") + " heading " + b.transform.forward.ToString("F2")); } }
            seenBolts = nowBolts;
            if (boltWindow >= 0 && now - boltWindow > .5f) { Log("bolts within 0.15 s of take-off: " + boltsAtTakeoff + " (stars alive at take-off " + starsAtTakeoff + ")"); boltWindow = -1f; }
            if (stars.Length >= 2 && !shotStars) { shotStars = true; Shot("stars"); }
            if (now - lastLog > 4f) { lastLog = now; Log("diag: gap " + Gap().ToString("F2") + " casting " + skill.IsExecutingEnemyAction + " casts " + skill.CastCount + " stars " + stars.Length + " slots [" + string.Join(",", stars.Select(x => x.Slot)) + "] bolts " + bolts.Length + " forger hp " + forger.GetCurrent(health).ToString("F0")); }
            yield return null;
        }
        foreach (var lo in liftoffs) Log("bolts appearing within 0.25 s of the lift-off at " + lo.ToString("F2") + ": " + boltTimes.Count(b => Mathf.Abs(b - lo) <= .25f));
        foreach (var id in ids) Log("casts " + id.ToString().Replace("project_n:starforger_", "") + ": " + castAt[id].Count + " at [" + string.Join(", ", castAt[id].Select(x => x.ToString("F1"))) + "]");
        Log("max stars alive " + maxStars + " (slots seen [" + string.Join(",", starSlots.OrderBy(x => x)) + "]), bolts seen " + boltTotal + ", player hits " + hits.Count + ": " + string.Join(" ", hits));
        Log("probes: crouch " + crouchProbe + "; air damage " + airProbe + "; air control Buff " + airControl);
        bool slamCast = castAt[PNStarforgerCombatContent.Slam].Count >= 1, starCast = castAt[PNStarforgerCombatContent.Star].Count >= 1, capOk = maxStars <= 3;
        bool invulnerable = airProbe == "ignored=True", immune = airControl.StartsWith("Rejected");
        Log("summary: slamCast=" + slamCast + " starCast=" + starCast + " atMostThreeStars=" + capOk + " invulnerableInFlight=" + invulnerable + " controlImmuneInFlight=" + immune + " crouchHurts=" + (crouchProbe == "ignored=False"));
        File.WriteAllLines(path, lines);
        if (!slamCast || !starCast || !capOk || !invulnerable || !immune) fail(new Exception("Starforger review incomplete: slam=" + slamCast + " star=" + starCast + " cap=" + capOk + " invulnerable=" + invulnerable + " controlImmune=" + immune));
    }
}
#endif
