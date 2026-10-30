#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.UIElements;

// Opt-in Player review of the Starkeeper in the real ErMode run (the Flow Player, started by PNMenuPlayReview solo with -pn-menu-play-starkeeper): the Starweaver is retired so it cannot
// interfere, the Starkeeper fights the idle player on its own AI. Records what the AI actually does:
//   - Quet sao: casts seen, damage to the player, frames.
//   - Diem sao: the marker appears; windup stillness for the registered 2 s; the lunge distance toward the star; the player's damage; then a second run with a wall dropped
//     between the Starkeeper and the star (it must stop at the wall and hurt nobody).
// Not production code. Results: <output>/starkeeper.txt and PNG frames.
public sealed class PNStarkeeperTitlePress : MonoBehaviour
{
    // The Flow Player always passes through the Title: press its Start button so the menu review (which waits for the Hall) can begin.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!Environment.GetCommandLineArgs().Any(x => x.StartsWith("-pn-menu-play-starkeeper", StringComparison.Ordinal))) return;
        var go = new GameObject("Starkeeper title press"); DontDestroyOnLoad(go); go.AddComponent<PNStarkeeperTitlePress>();
    }
    private IEnumerator Start()
    {
        Application.runInBackground = true; float end = Time.realtimeSinceStartup + 60f;
        while (Time.realtimeSinceStartup < end)
        {
            var document = FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.rootVisualElement != null && d.rootVisualElement.Q("pn-title-start") != null);
            if (document != null && PNLocalization.IsReady)
            {
                var button = document.rootVisualElement.Q<Button>("pn-title-start");
                using (var e = NavigationSubmitEvent.GetPooled()) { e.target = button; button.SendEvent(e); }
                Destroy(gameObject); yield break;
            }
            yield return null;
        }
    }
}

public static class PNStarkeeperPlayReview
{
    public static IEnumerator Case(string output, Action<Exception> fail, Func<bool> hasFailed)
    {
        var lines = new List<string>(); int shots = 0;
        void Log(string s) { lines.Add(Time.realtimeSinceStartup.ToString("0.0") + "s " + s); }
        void Shot(string label) { ScreenCapture.CaptureScreenshot(Path.Combine(output, "sk_" + (shots++).ToString("00") + "_" + label + ".png")); }
        string path = Path.Combine(output, "starkeeper.txt");
        PNStarkeeperSkill skill = null; PNPlayerStat player = null;
        float start = Time.realtimeSinceStartup;
        while ((skill == null || player == null || !skill.IsReady || !player.IsReady) && Time.realtimeSinceStartup - start < 40f)
        {
            if (skill == null) skill = UnityEngine.Object.FindFirstObjectByType<PNStarkeeperSkill>();
            if (player == null) player = UnityEngine.Object.FindFirstObjectByType<PNPlayerStat>();
            yield return null;
        }
        if (skill == null || player == null || !skill.IsReady) { File.WriteAllLines(path, lines); fail(new Exception("Starkeeper or player never became ready (skill=" + (skill != null) + " player=" + (player != null) + ")")); yield break; }
        var weaver = UnityEngine.Object.FindFirstObjectByType<PNStarweaverSkill>();
        if (weaver != null) { var host = PNEntityHosts.Find(EveRegistrySession.Context); host?.Retire(weaver.GetComponent<EveEntityLoader>()); Log("Starweaver retired"); }
        yield return new WaitForSeconds(.5f);
        { var s = skill.GetComponent<PNVisualBody>(); Log("strings: has=" + s.Actor.HasStrings + " pullingUp=" + s.PullingUp); Shot("idle_strings"); }
        var body = skill.GetComponent<PNVisualBody>(); var spear = skill.transform.Find("Star spear");
        Log("ready: Starkeeper " + skill.transform.position.ToString("F2") + " player " + player.transform.position.ToString("F2") + " spear=" + (spear != null) + " visualState=" + body.Actor.Animator.BaseState);
        float Health() => (float)player.GetCurrent(PNContentIds.Health);
        GameObject Marker(string prefix) { foreach (var go in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (go.parent == null && go.name.StartsWith(prefix)) return go.gameObject; return null; }
        int sweeps = 0, points = 0; float t0 = Time.realtimeSinceStartup; bool wallRun = false; var log = new List<string>();
        GameObject wall = null;
        while (Time.realtimeSinceStartup - t0 < 110f && (sweeps < 2 || points < 2))
        {
            // after a lunge the Starkeeper stands on the player: put the player 1.15 m away so a sweep has room to step forward (the step is the point of this case)
            if (!skill.IsExecutingEnemyAction && Vector3.Distance(skill.transform.position, player.transform.position) < .6f && sweeps < 2)
            { var sk0 = skill.transform.position; player.transform.position = new Vector3(sk0.x - 1.15f, player.transform.position.y, sk0.z); yield return new WaitForSeconds(.1f); }
            // keep the player at a point-able distance once a sweep has been seen: 2.8 m away from the Starkeeper, on the side of the arena's centre
            if (sweeps >= 1 && !skill.IsExecutingEnemyAction)
            {
                var sk = skill.transform.position; var to = new Vector3(-sk.x, 0, -sk.z); to = to.sqrMagnitude < .01f ? Vector3.left : to.normalized;
                var spot = sk + to * 2.8f; player.transform.position = new Vector3(spot.x, player.transform.position.y, spot.z);
            }
            var sweepMark = Marker("Starkeeper sweep"); var pointMark = Marker("Starkeeper star point");
            if (sweepMark != null)
            {
                sweeps++; float h0 = Health(), s0 = Time.realtimeSinceStartup; Shot("sweep_windup" + sweeps); bool shotHit = false;
                var sweepStart = skill.transform.position; float gap0 = Vector3.Distance(skill.transform.position, player.transform.position);
                Log("sweep #" + sweeps + " begins, player distance " + gap0.ToString("F2"));
                while (Marker("Starkeeper sweep") != null && Time.realtimeSinceStartup - s0 < 4f) { if (!shotHit && Time.realtimeSinceStartup - s0 > .45f) { shotHit = true; Shot("sweep_hit" + sweeps); } yield return null; }
                Log("sweep #" + sweeps + " advanced " + Vector3.Distance(sweepStart, skill.transform.position).ToString("F2") + " m toward the player (distance " + gap0.ToString("F2") + " -> " + Vector3.Distance(skill.transform.position, player.transform.position).ToString("F2") + ")");
                Log("sweep #" + sweeps + " took " + (Time.realtimeSinceStartup - s0).ToString("F2") + " s, player damage " + (h0 - Health()).ToString("F0") + ", skill casts " + skill.CastCount);
                yield return new WaitForSeconds(.3f); continue;
            }
            if (pointMark != null)
            {
                points++; float h0 = Health(), s0 = Time.realtimeSinceStartup; var origin = skill.transform.position;
                var starRenderer = pointMark.transform.Find("Star")?.GetComponent<MeshRenderer>(); var anchor = starRenderer != null ? starRenderer.bounds.center : pointMark.transform.position;
                float toStar = Vector2.Distance(new Vector2(origin.x, origin.z), new Vector2(anchor.x, anchor.z));
                bool thisWall = points == 2; if (thisWall) wallRun = true;
                Log("star point #" + points + " begins: origin " + origin.ToString("F2") + " star " + anchor.ToString("F2") + " (" + toStar.ToString("F2") + " m), player at " + player.transform.position.ToString("F2") + (thisWall ? ", WALL dropped at 40%" : ""));
                if (thisWall)
                {
                    var dir = (anchor - origin); dir.y = 0; dir.Normalize(); var centre = origin + dir * toStar * .4f + Vector3.up * 1f;
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Review wall"; wall.transform.position = centre; wall.transform.rotation = Quaternion.LookRotation(dir); wall.transform.localScale = new Vector3(3f, 2f, .3f);
                    int layer = LayerMask.NameToLayer("Wall"); if (layer >= 0) wall.layer = layer; UnityEngine.Object.Destroy(wall.GetComponent<MeshRenderer>());
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wall, skill.gameObject.scene);   // the world has its own physics scene: the wall must live in the enemy's scene
                }
                float maxDrift = 0f; bool shotWind = false, shotLunge = false, moved = false; Vector3 end = origin;
                while (Marker("Starkeeper star point") != null && Time.realtimeSinceStartup - s0 < 8f)
                {
                    float age = Time.realtimeSinceStartup - s0; var here = skill.transform.position;
                    if (age < 1.9f) maxDrift = Mathf.Max(maxDrift, Vector2.Distance(new Vector2(here.x, here.z), new Vector2(origin.x, origin.z)));
                    if (!shotWind && age > 1.2f) { shotWind = true; Shot("point_wind" + points); }
                    if (!shotLunge && age > 2.12f) { shotLunge = true; Shot("point_lunge" + points); }
                    if (age > 2.9f && end == origin) end = here;
                    // the lunge is over (0.25 s after the 2 s windup) and the damage counted: move the player 1.15 m off so the sweep that follows has room to step toward it
                    if (!thisWall && !moved && age > 2.6f) { moved = true; player.transform.position = new Vector3(here.x - 1.15f, player.transform.position.y, here.z); }
                    yield return null;
                }
                if (end == origin) end = skill.transform.position;
                float travelled = Vector2.Distance(new Vector2(origin.x, origin.z), new Vector2(end.x, end.z)), toEnd = Vector2.Distance(new Vector2(end.x, end.z), new Vector2(anchor.x, anchor.z));
                Log("star point #" + points + " ended after " + (Time.realtimeSinceStartup - s0).ToString("F2") + " s: windup drift " + maxDrift.ToString("F3") + " m, lunged " + travelled.ToString("F2") + " m of " + toStar.ToString("F2") + ", ends " + toEnd.ToString("F2") + " m from the star, player damage " + (h0 - Health()).ToString("F0"));
                if (wall != null) { UnityEngine.Object.Destroy(wall); wall = null; }
                yield return new WaitForSeconds(.3f); continue;
            }
            yield return null;
        }
        Log("done: sweeps=" + sweeps + " star points=" + points + " wallRun=" + wallRun + " casts=" + skill.CastCount + " failure=" + skill.Failure + " Starkeeper=" + skill.transform.position.ToString("F2") + " state=" + body.Actor.Animator.BaseState);
        Shot("end");
        yield return new WaitForSeconds(.5f);
        File.WriteAllLines(path, lines);
        if (sweeps < 1 || points < 2) fail(new Exception("Starkeeper review incomplete: sweeps=" + sweeps + " points=" + points));
    }
}
#endif
