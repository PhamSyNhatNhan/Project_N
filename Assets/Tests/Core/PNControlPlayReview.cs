#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Temporary fixture in Ready Play Mode; tests use the normal player loop.
public sealed class PNControlPlayReview : MonoBehaviour
{
    public static string Status = "Idle";
    private readonly List<string> evidence = new List<string>();
    private readonly List<Action> restores = new List<Action>();
    private Keyboard keyboard;
    private GameObject fixture;
    private InputSettings previousSettings, settings;
    private HideFlags previousFlags;
    private bool previousBackground;
    private int checks;
    private bool cleaned;
    public static string Run()
    {
        if (!Application.isPlaying || !EveRegistrySession.IsReady || EditorApplication.isPaused) throw new InvalidOperationException("Ready unpaused Play Mode required.");
        Status = "Running";
        var review = new GameObject("Disposable control Play review").AddComponent<PNControlPlayReview>();
        EditorApplication.update += review.Pump; return Status;
    }
    private void Pump() { EditorApplication.QueuePlayerLoopUpdate(); }
    private void Start() => StartCoroutine(Guarded());
    private void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; evidence.Add("PASS " + message); }
    private IEnumerator Guarded()
    {
        previousBackground = Application.runInBackground; Application.runInBackground = true;
        previousSettings = InputSystem.settings; previousFlags = previousSettings.hideFlags;
        if (previousFlags == HideFlags.HideAndDontSave) previousSettings.hideFlags = HideFlags.DontSave;
        settings = Instantiate(previousSettings); settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = settings;
        var sequence = Sequence();
        while (true)
        {
            object next;
            try { if (!sequence.MoveNext()) { Status = "PASS " + checks + " control Play checks"; break; } next = sequence.Current; }
            catch (Exception error) { Status = "FAIL " + error; break; }
            yield return next;
        }
        (sequence as IDisposable)?.Dispose(); Cleanup();
        Directory.CreateDirectory(".utmp/ControlReview"); File.WriteAllLines(".utmp/ControlReview/PlayResults.txt", evidence.Concat(new[] { Status })); Destroy(gameObject);
    }
    private IEnumerator Sequence()
    {
        var existingDevices = InputSystem.devices.ToArray();
        foreach (var other in FindObjectsByType<PNPlayerMove>(FindObjectsSortMode.None))
        {
            var controls = other.PC; if (controls == null) continue; var old = controls.devices;
            controls.devices = existingDevices; restores.Add(() => { if (other != null) controls.devices = old; });
        }
        keyboard = InputSystem.AddDevice<Keyboard>("PNControlReviewKeyboard");
        fixture = new GameObject("Control Play fixture"); fixture.SetActive(false); fixture.transform.position = new Vector3(500, 5, 500);
        fixture.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(989950), EveId.Parse("project_n:eve"), 0);
        fixture.AddComponent<EveCharacterStat>(); fixture.AddComponent<PNAbilityManager>();
        var body = fixture.AddComponent<Rigidbody>(); body.useGravity = false;
        var capsule = fixture.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f;
        var move = fixture.AddComponent<PNPlayerMove>(); var skill = fixture.AddComponent<EveCharacterSkill>();
        var loader = fixture.AddComponent<EveEntityLoader>(); loader.Load(EveRegistrySession.Current); fixture.SetActive(true);
        yield return null;
        move.PC.devices = new InputDevice[] { keyboard };
        var state = skill.Controls; var start = body.position;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D)); yield return new WaitForSecondsRealtime(.15f);
        Check(body.position.x > start.x + .1f && move.MoveDirection.x > .9f, "Real held keyboard input moves fixture in Play Mode.");
        var outer = state.Acquire("play outer", new[] { PNControlIds.Move, PNControlIds.FaceControl, PNControlIds.Jump });
        yield return new WaitForSecondsRealtime(.05f); start = body.position; yield return new WaitForSecondsRealtime(.15f);
        Check(Vector3.Distance(body.position, start) < .01f && move.MoveDirection.sqrMagnitude == 0, "Lease stops held movement without disabling keyboard.");
        Check(!move.TryJump(), "Direct jump respects effective lock.");
        Check(skill.TryDash(), "Walking lock permits Dash."); yield return new WaitForSecondsRealtime(.3f);
        Check(Vector3.Distance(body.position, start) > 2f && !move.CanMove && state.Snapshot().Count == 1, "Dash finishes and releases only its own nested leases.");
        outer.Dispose(); start = body.position; yield return new WaitForSecondsRealtime(.15f);
        Check(body.position.x > start.x + .1f, "Held movement resumes after last lease ends.");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return new WaitForSecondsRealtime(.1f);
        skill.BurstGauge.Add(150);
        using (state.Acquire("play all", new[] { PNControlIds.SkillAll }))
        {
            var charges = skill.DashRecovery.Current; var gauge = skill.BurstGauge.Current;
            Check(!skill.TryAttack() && !skill.TrySkill() && !skill.TryDash() && !skill.TryUltimate() && !skill.TryBurst(), "All five direct Try methods respect global gate in Play Mode.");
            Check(skill.DashRecovery.Current == charges && skill.BurstGauge.Current == gauge, "Rejected requests preserve resources.");
        }
        Check(skill.TryDash(), "Dash available after global lease release.");
        var retained = state.Acquire("play retained", new[] { PNControlIds.Move }); skill.enabled = false; yield return null;
        Check(!skill.IsDashing && state.Snapshot().Count == 1 && !move.CanMove, "Disabling skill cleans Action leases but retains external owner.");
        retained.Dispose(); loader.Unload();
        Check(state.IsDisposed, "Unload invalidates collection.");
    }
    private void Cleanup()
    {
        if (cleaned) return; cleaned = true; EditorApplication.update -= Pump;
        try { if (fixture != null) { fixture.GetComponent<EveEntityLoader>()?.Unload(); Destroy(fixture); fixture = null; } }
        finally
        {
            foreach (var restore in restores) restore(); restores.Clear();
            if (keyboard != null && InputSystem.devices.Contains(keyboard)) InputSystem.RemoveDevice(keyboard);
            if (previousSettings != null) { InputSystem.settings = previousSettings; previousSettings.hideFlags = previousFlags; }
            if (settings != null) Destroy(settings);
            Application.runInBackground = previousBackground;
        }
    }
    private void OnDestroy() => Cleanup();
}
#endif
