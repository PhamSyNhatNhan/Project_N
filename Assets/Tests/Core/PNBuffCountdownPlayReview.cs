#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

// Disposable Play fixture for the replica-derived Buff countdown (RulesVersion70): the real HUD shows a number that
// counts down while the host publishes no per-second message. No authored scene/configuration modification.
public sealed class PNBuffCountdownPlayReview : MonoBehaviour
{
    public static string Status = "Idle";
    private PNUiRuntime ui;
    private Camera oldCamera, camera;
    private long oldOwner;
    private EveEntityId oldPlayer;
    private bool oldMobile, oldExpanded;
    private Scene scene;
    private readonly List<GameObject> roots = new List<GameObject>();
    private readonly List<string> evidence = new List<string>();
    private readonly string folder = "F:/unity/Project_N/.utmp/Countdown/PlayReview";
    private int checks;
    private string mode;
    public static string Run()
    {
        if (!Application.isPlaying || !EveRegistrySession.IsReady || EditorApplication.isPaused) throw new InvalidOperationException("Ready unpaused Play required");
        var host = new GameObject("Buff countdown review host"); var review = host.AddComponent<PNBuffCountdownPlayReview>();
        review.ui = FindObjectsByType<PNUiRuntime>(FindObjectsSortMode.None).First(); review.oldCamera = review.ui.ViewCamera;
        review.oldOwner = review.ui.LocalOwnerId; review.oldPlayer = review.ui.LocalPlayerId; review.oldMobile = review.ui.ShowMobileControls; review.oldExpanded = review.ui.TestExpanded;
        review.scene = SceneManager.CreateScene("Disposable Buff countdown review"); Directory.CreateDirectory(review.folder);
        review.mode = PNMobilePlatform.UsesSimulatedScreen ? "Simulator" : "Game";
        Status = "Running"; EditorApplication.update += review.Pump; review.StartCoroutine(review.Guarded(review.Sequence())); return Status;
    }
    private void Pump() { if (Application.isPlaying) { EditorApplication.QueuePlayerLoopUpdate(); UnityEditorInternal.InternalEditorUtility.RepaintAllViews(); } }
    private void OnDestroy() => EditorApplication.update -= Pump;
    private GameObject Root(string name) { var root = new GameObject(name); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, scene); roots.Add(root); return root; }
    private PNStat Entity(long id, bool player, Vector3 position)
    {
        var root = Root("Buff countdown fixture " + id); root.transform.position = position;
        root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse(player ? "project_n:eve" : "project_n:dummy"), id);
        root.AddComponent<EveTimeScale>(); PNStat stat = player ? (PNStat)root.AddComponent<PNPlayerStat>() : root.AddComponent<PNEnemyStat>();
        if (player) root.AddComponent<PNAbilityManager>(); root.AddComponent<EveEntityLoader>().Load(EveRegistrySession.Current); root.SetActive(true);
        return stat;
    }
    private void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; evidence.Add("PASS " + message); }
    private VisualElement Enemy(PNStat stat) => ui.GetComponentsInChildren<UIDocument>().Single(d => d.name == "Combat UI " + stat.EntityId).rootVisualElement.Q("enemy-buffs");
    private IEnumerator Capture(string name)
    {
        yield return null; var texture = PNPauseVisualReview.ReadSelectedView();
        File.WriteAllBytes(Path.Combine(folder, mode + "-" + name + ".png"), texture.EncodeToPNG()); Destroy(texture);
    }
    // Bottom-right badge text of the single card in a HUD container ("" when the badge is hidden).
    private static string Countdown(VisualElement container, long instanceId)
    {
        var card = container.Q("buff-" + instanceId); if (card == null) return null;
        var label = card.Query<Label>().ToList()[3];
        return label.parent.resolvedStyle.display == DisplayStyle.None ? "" : label.text;
    }
    private static int Value(string text) => int.Parse(text);
    private IEnumerator Sequence()
    {
        var cameraRoot = Root("Buff countdown review camera"); camera = cameraRoot.AddComponent<Camera>(); camera.CopyFrom(Camera.main);
        camera.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Base; camera.depth = 100; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f, .17f, .19f);
        camera.transform.position = new Vector3(100, 6, -6); camera.transform.LookAt(new Vector3(100, 0, 0)); camera.orthographic = true; camera.orthographicSize = 4; cameraRoot.SetActive(true);
        var player = Entity(998201, true, new Vector3(100, 0, 0)); var enemy = Entity(998202, false, new Vector3(99, 0, 2));
        ui.ViewCamera = camera; ui.LocalOwnerId = 998201; ui.LocalPlayerId = player.EntityId; ui.SetTestExpanded(false);
        var source = PNBuffSource.Capture(player);
        var playerManager = player.GetComponent<PNBuffManager>(); var p = playerManager.Buffs; var e = enemy.GetComponent<PNBuffManager>().Buffs;
        var profile = EveRegistrySession.Current.Buffs().Get(EveId.Parse("project_n:timescale_reduction"));
        Check(profile.Countdown != null && profile.Countdown.Slot == PNBuffCountdownSlot.BottomRight, "Stock timescale reduction opts in to the BottomRight countdown");
        long messages = 0; Action<byte[]> count = _ => messages++; playerManager.ReplicationDelta += count;
        PNBuffHandle Slow(PNBuffCollection buffs, double seconds) => buffs.Apply(new PNBuffApplyRequest(EveId.Parse("project_n:timescale_reduction"), source,
            new JObject { { "reduction", 0 }, { "minimumReduction", 0 }, { "duration", seconds } })).Handle;
        var longPlayer = Slow(p, 8); var longEnemy = Slow(e, 8); var shortEnemy = Slow(e, 2);
        ui.RefreshUI(); yield return new WaitForSeconds(.3f); ui.RefreshUI(); yield return null;
        var hud = ui.HudRoot.Q("player-buffs"); var world = Enemy(enemy);
        Check(hud.childCount == 1 && world.childCount == 2, "Player and enemy cards display");
        long playerId = playerManager.Presentation.PresentationSnapshot()[0].InstanceId;
        var enemyCards = enemy.GetComponent<PNBuffManager>().Presentation.PresentationSnapshot();
        long longEnemyId = enemyCards[0].InstanceId, shortEnemyId = enemyCards[1].InstanceId;
        string first = Countdown(hud, playerId);
        Check(first != "" && Value(first) >= 7 && Value(first) <= 8, "HUD shows the derived countdown at start (" + first + ")");
        Check(playerManager.Buffs.PresentationSnapshot()[0].Numbers.BottomRight == null, "Raw collection slot stays null for the opted-in Buff");
        yield return Capture("Running-8");
        long baseline = messages; int firstValue = Value(first);
        yield return new WaitForSeconds(1.6f); ui.RefreshUI(); yield return null;
        string later = Countdown(hud, playerId);
        Check(later != "" && Value(later) < firstValue, "HUD countdown decreased from " + first + " to " + later);
        Check(messages == baseline, "Host emitted no Buff message while the countdown changed (" + (messages - baseline) + " new)");
        string enemyLater = Countdown(world, longEnemyId);
        Check(enemyLater != "" && Value(enemyLater) <= 7, "Enemy world card counts down too (" + enemyLater + ")");
        yield return Capture("Running-later");
        // The short Buff must expire without ever displaying zero.
        bool sawZero = false;
        for (int i = 0; i < 90 && world.Q("buff-" + shortEnemyId) != null; i++)
        {
            string text = Countdown(world, shortEnemyId); if (text == "0") sawZero = true;
            ui.RefreshUI(); yield return null;
        }
        yield return new WaitForSeconds(.4f); ui.RefreshUI(); yield return null;
        Check(!sawZero && world.childCount == 1 && !shortEnemy.IsAlive, "Short Buff expires without a zero and its card retires");
        // Suspend: the number holds, then continues after resume.
        string beforeSuspend = Countdown(hud, playerId);
        playerManager.enabled = false; ui.RefreshUI(); yield return null; yield return new WaitForSeconds(.1f);
        string frozen = Countdown(hud, playerId);
        yield return new WaitForSeconds(1.2f); ui.RefreshUI(); yield return null;
        Check(frozen != "" && Countdown(hud, playerId) == frozen, "Countdown holds while suspended (" + frozen + ")");
        yield return Capture("Suspended");
        playerManager.enabled = true; yield return new WaitForSeconds(1.3f); ui.RefreshUI(); yield return null;
        string resumed = Countdown(hud, playerId);
        Check(resumed != "" && Value(resumed) < Value(frozen) + 1, "Countdown resumes from the held value (" + frozen + " -> " + resumed + ")");
        p.Remove(longPlayer, PNBuffEndReason.Bypass, true); e.Remove(longEnemy, PNBuffEndReason.Bypass, true);
        yield return new WaitForSeconds(.35f); ui.RefreshUI(); yield return null;
        Check(hud.childCount == 0 && world.childCount == 0, "Ended Buffs leave no placeholder slots");
        playerManager.ReplicationDelta -= count;
        Status = "PASS " + checks;
    }
    private IEnumerator Guarded(IEnumerator sequence)
    {
        while (true)
        {
            object next;
            try { if (!sequence.MoveNext()) break; next = sequence.Current; }
            catch (Exception error) { Status = "FAIL " + error; Debug.LogException(error); break; }
            yield return next;
        }
        (sequence as IDisposable)?.Dispose(); EditorApplication.update -= Pump;
        ui.ViewCamera = oldCamera; ui.LocalOwnerId = oldOwner; ui.LocalPlayerId = oldPlayer; ui.ShowMobileControls = oldMobile; ui.SetTestExpanded(oldExpanded); ui.RefreshUI();
        foreach (var root in roots) if (root != null) { root.GetComponent<EveEntityLoader>()?.Unload(); Destroy(root); }
        if (scene.IsValid()) SceneManager.UnloadSceneAsync(scene);
        File.WriteAllLines(Path.Combine(folder, mode + "-CountdownResults.txt"), evidence.Concat(new[] { Status })); Destroy(gameObject);
    }
}
#endif
