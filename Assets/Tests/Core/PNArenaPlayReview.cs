#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;

// Opt-in Play review of the test arena (Docs/SandboxPlan.md, step G14): with the scene ErMode (now a shell) open in the Editor, a trigger file `.utmp/ArenaTrigger.txt` (line 1 = output folder) makes the next Play Mode
// start check that the default world is `project_n:test_arena`, the player stands at the entry, the dummy and the four star creatures are made, the camera shows the shell view, then it writes a frame and the
// result to <output>/arena.txt. Not production code.
public sealed class PNArenaPlayReview : MonoBehaviour
{
#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var trigger = Path.Combine(Path.GetDirectoryName(Application.dataPath), ".utmp", "ArenaTrigger.txt");
        if (!File.Exists(trigger)) return;
        var rows = File.ReadAllLines(trigger); File.Delete(trigger);
        var host = new GameObject("PNArenaPlayReview"); DontDestroyOnLoad(host); var review = host.AddComponent<PNArenaPlayReview>(); review.output = rows.Length > 0 ? rows[0].Trim() : null; review.hold = rows.Length > 1 && rows[1].Trim() == "hold";
    }
#endif
    public string output; public bool hold;   // line 2 of the trigger "hold": leave Play Mode running after the review (for measuring by hand)
    private readonly List<string> lines = new List<string>(); private bool failed;
    private void Check(bool value, string what) { if (!value) failed = true; lines.Add((value ? "PASS " : "FAIL ") + what); Debug.Log("Arena " + (value ? "PASS " : "FAIL ") + what); }

    private IEnumerator Start()
    {
        output = string.IsNullOrEmpty(output) ? Path.Combine(Application.temporaryCachePath, "Arena") : output; Directory.CreateDirectory(output);
        float end = Time.realtimeSinceStartup + 60f;
        while (!(EveRegistrySession.IsReady && PNLocalWorldContext.Current?.World != null && PNLocalWorldContext.Current.Actor != null) && Time.realtimeSinceStartup < end) yield return null;
        Check(EveRegistrySession.IsReady && PNLocalWorldContext.Current?.World != null, "the session is ready and a world is viewed");
        if (failed) { Finish(); yield break; }
        var world = PNLocalWorldContext.Current.World;
        Check(world.DefinitionId.ToString() == "project_n:test_arena", "the viewed world is the test arena (" + world.DefinitionId + ")");
        Check(PNGameFlow.Phase == PNFlowPhase.Run, "the flow phase is Run");
        string[] wanted = { "project_n:dummy", "project_n:starkeeper", "project_n:starchanter", "project_n:starreaper", "project_n:starforger", "project_n:starherald" };
        end = Time.realtimeSinceStartup + 30f;
        while (wanted.Any(id => UnityEngine.Object.FindObjectsByType<PNStat>().Count(x => x.CharacterDefinitionId == id) != 1) && Time.realtimeSinceStartup < end) yield return null;
        foreach (var id in wanted) Check(UnityEngine.Object.FindObjectsByType<PNStat>().Count(x => x.CharacterDefinitionId == id) == 1, "exactly one " + id + " is made by the map's fixed groups");
        var player = PNLocalWorldContext.Current.Actor;
        Check(player != null && PNEntityRoles.IsPlayer(player), "the local actor is a player");
        if (player != null)
        {
            var p = player.transform.position; lines.Add("NOTE player at " + p);
            Check(Mathf.Abs(p.x - 12f) < 1.5f && Mathf.Abs(p.z - 12f) < 1.5f, "the player stands at the entry (cell 24,24 = 12 m,12 m)");
            foreach (var stat in UnityEngine.Object.FindObjectsByType<PNStat>().Where(x => wanted.Contains(x.CharacterDefinitionId))) lines.Add("NOTE " + stat.CharacterDefinitionId + " at " + stat.transform.position);
        }
        // The Starherald stands 2 m from the Starchanter and 3 m from the Starkeeper: within a few seconds it must have given Sidus (the Star package) to two of its neighbours and never to itself.
        end = Time.realtimeSinceStartup + 20f; int sidus = 0;
        while (Time.realtimeSinceStartup < end && (sidus = UnityEngine.Object.FindObjectsByType<PNEnemyStat>().Count(x => x.IsSidus)) < 2) yield return null;
        var herald = UnityEngine.Object.FindObjectsByType<PNEnemyStat>().FirstOrDefault(x => x.CharacterDefinitionId == "project_n:starherald");
        Check(sidus == 2, "the Starherald cast once: exactly two monsters are Sidus (" + sidus + ")");
        Check(herald != null && !herald.IsSidus, "the Starherald did not give itself Sidus");
        foreach (var stat in UnityEngine.Object.FindObjectsByType<PNEnemyStat>().Where(x => x.IsSidus)) lines.Add("NOTE Sidus on " + stat.CharacterDefinitionId);
        var presentation = herald != null ? herald.GetComponent<PNHeraldPresentation>() : null; lines.Add("NOTE herald effect objects: " + (presentation != null && presentation.HasEffects));
        yield return new WaitForSeconds(3f);
        var view = PNCameraDirector.Current?.ViewCamera;
        Check(view != null && Mathf.Abs(view.transform.rotation.eulerAngles.x - 45f) < 1f && view.clearFlags == CameraClearFlags.SolidColor, "the camera shows the shell view (pitch 45, solid colour background)");
        if (view != null) lines.Add("NOTE camera at " + view.transform.position + " rot " + view.transform.rotation.eulerAngles + " bg " + view.backgroundColor);
        yield return new WaitForEndOfFrame();
        var texture = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(output, "arena.png"), texture.EncodeToPNG()); Destroy(texture); lines.Add("frame " + Path.Combine(output, "arena.png"));
        Finish();
    }
    private void Finish()
    {
        File.WriteAllText(Path.Combine(output, "arena.txt"), (failed ? "FAIL\n" : "PASS\n") + string.Join("\n", lines));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath), ".utmp", "ArenaDone.txt"), failed ? "FAIL" : "PASS");
#if UNITY_EDITOR
        if (!hold) UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}
#endif
