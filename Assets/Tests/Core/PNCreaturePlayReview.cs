#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;

// Opt-in Player review of a rig puppet that does NOT extend the humanoid (Docs/VisualRigRuntimeImplementationPlan.md R0): the Flow Player is started by PNMenuPlayReview solo with
// -pn-menu-play-creature and --pn-allow-forbidden and -pn-menu-play-character=project_n:visual_creature_test, so the player's own body is the test creature (prefab visual_test, body.visual
// project_n:creature_test). The probe watches what the real run does with it: it is drawn (visible, quad count, drawing box), it idles, moves (the walk and run states), is hurt and dies.
// Not production code. Results: <output>/creature.txt and PNG frames.
public static class PNCreaturePlayReview
{
    public static IEnumerator Case(string output, Action<Exception> fail, Func<bool> hasFailed)
    {
        var lines = new List<string>(); int shots = 0;
        void Log(string s) { lines.Add(Time.realtimeSinceStartup.ToString("0.0") + "s " + s); }
        void Shot(string label) { ScreenCapture.CaptureScreenshot(Path.Combine(output, "cr_" + (shots++).ToString("00") + "_" + label + ".png")); }
        string path = Path.Combine(output, "creature.txt");
        PNVisualBody body = null; float start = Time.realtimeSinceStartup;
        while (body == null && Time.realtimeSinceStartup - start < 40f)
        {
            body = UnityEngine.Object.FindObjectsByType<PNVisualBody>(FindObjectsSortMode.None).FirstOrDefault(b => b.Actor != null && b.Actor.Appearance != null && b.Actor.Appearance.Id.ToString() == "project_n:creature_test");
            yield return null;
        }
        if (body == null) { File.WriteAllLines(path, lines); fail(new Exception("The creature body never appeared (is --pn-allow-forbidden passed?)")); yield break; }
        var actor = body.Actor; var stat = body.GetComponent<PNStat>(); var playerStat = UnityEngine.Object.FindFirstObjectByType<PNPlayerStat>();
        Log("stats: body stat " + (stat != null ? stat.GetType().Name + " hp=" + stat.GetCurrent(PNContentIds.Health) : "none") + "; PNPlayerStat " + (playerStat != null ? "hp=" + playerStat.GetCurrent(PNContentIds.Health) + " sameObject=" + (playerStat.gameObject == body.gameObject) : "none"));
        if (playerStat != null) stat = playerStat;
        Log("health max=" + stat.GetMax(PNContentIds.Health) + " attack=" + stat.Evaluate(PNContentIds.AttackPower) + " initialized=" + stat.IsInitialized); var capsule = body.GetComponent<CapsuleCollider>();
        yield return new WaitForSeconds(1.0f);
        Log("stat on arrival: health=" + stat.GetCurrent(PNContentIds.Health) + " IsDead=" + stat.IsDead);
        // the run spawns this fixture with 0 health (cause not found, not the rig); revive it so the states below are the creature's own
        if (stat.IsDead) { stat.SetCurrent(PNContentIds.Health, 100000); yield return new WaitForSeconds(1.0f); }
        Log("stat: health=" + stat.GetCurrent(PNContentIds.Health) + " IsDead=" + stat.IsDead + " HasHealth=" + stat.HasHealth);
        Log("creature ready: appearance=" + actor.Appearance.Id + " quads=" + actor.QuadCount + " vertices=" + actor.Mesh.vertexCount + " capsule=" + capsule.height.ToString("0.00") + "x" + capsule.radius.ToString("0.00")
            + " drawBox=" + actor.Mesh.bounds.size.ToString("0.00") + " state=" + actor.Animator.BaseState + " visible=" + actor.Visible + " inView=" + actor.InView);
        Shot("idle");
        bool idleOk = actor.QuadCount == 8 && actor.Visible && actor.Animator.BaseState == "idle";
        // the motion with bezier handles (Docs/VisualCurveHandlesPlan.md): it plays in the real run, and the body keeps it until the movement state changes
        bool bounceOk = actor.Play("bounce", true); yield return new WaitForSeconds(.4f); Shot("bounce");
        Log("bounce: played=" + bounceOk + " state=" + actor.Animator.BaseState + " time=" + actor.Animator.BaseTime.ToString("0.00")); bounceOk &= actor.Animator.BaseState == "bounce";
        yield return new WaitForSeconds(.8f); actor.Play("idle", true);
        // walk and run: move the body along the floor at the speeds the feed reads (the state follows the speed)
        string seen = "";
        foreach (var speed in new[] { 0.6f, 4.5f })
        {
            float t = 0f; var from = body.transform.position;
            while (t < 1.2f) { t += Time.deltaTime; body.transform.position += new Vector3(speed * Time.deltaTime, 0f, 0f); if (t > .5f && t < .56f) Shot(speed < 2f ? "walk" : "run"); yield return null; }
            Log("moved at " + speed.ToString("0.0") + " m/s over " + Vector3.Distance(from, body.transform.position).ToString("0.00") + " m: state=" + actor.Animator.BaseState + " visible=" + actor.Visible);
            seen += actor.Animator.BaseState + ",";
        }
        yield return new WaitForSeconds(.6f);
        // hurt: the replicated health drops, the feed plays the overlay
        double hp = stat.GetCurrent(PNContentIds.Health); stat.SetCurrent(PNContentIds.Health, hp - 10); yield return null; yield return null; Shot("hurt");
        Log("after losing 10 health: overlay=" + actor.Animator.OverlayState + " base=" + actor.Animator.BaseState);
        bool hurtOk = actor.Animator.OverlayState == "hurt";
        yield return new WaitForSeconds(1.0f);
        stat.SetCurrent(PNContentIds.Health, 0); yield return new WaitForSeconds(.35f); Shot("dead_fall");
        yield return new WaitForSeconds(1.4f); Shot("dead_floor");
        Log("after death: base=" + actor.Animator.BaseState + " visible=" + actor.Visible);
        bool deadOk = actor.Animator.BaseState == "dead";
        Log("summary: idle=" + idleOk + " states seen " + seen + " hurt=" + hurtOk + " dead=" + deadOk);
        File.WriteAllLines(path, lines);
        if (!bounceOk || !idleOk || !hurtOk || !deadOk || !seen.Contains("walk") || !seen.Contains("run")) fail(new Exception("Creature review incomplete: idle=" + idleOk + " states=" + seen + " hurt=" + hurtOk + " dead=" + deadOk));
    }
}
#endif
