#if UNITY_EDITOR
using System;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public sealed class EveInputPlayProbe : EveCharacterSkill
{
    public string InputTrace;
    protected override void StartedAttack(InputAction.CallbackContext context)
    { InputTrace += " start:" + context.interaction?.GetType().Name; base.StartedAttack(context); }
    protected override void PerformedAttack(InputAction.CallbackContext context)
    { InputTrace += " perform:" + context.interaction?.GetType().Name; base.PerformedAttack(context); }
    protected override void CanceledAttack(InputAction.CallbackContext context)
    { InputTrace += " cancel:" + context.interaction?.GetType().Name; base.CanceledAttack(context); }
    protected override void Update() { }
    public void Tick(float seconds) => AdvanceAttack(seconds);
}

// Input System callbacks are verified in Play Mode; Edit Mode suppresses player actions.
public static class EveMovesetPlayModeChecks
{
    private static Action finish, cleanup;
    private static double started;
    public static string LastResult { get; private set; }
    public static string Run()
    {
        if (!EditorApplication.isPlaying || !EveRegistrySession.IsReady)
            throw new InvalidOperationException("Run in Play Mode after the registry is ready.");
        if (finish != null) throw new InvalidOperationException("Input check already running.");
        var others = UnityEngine.Object.FindObjectsByType<PNPlayerSkill>(FindObjectsSortMode.None);
        var enabled = others.Select(s => s.enabled).ToArray();
        var oldUpdate = InputSystem.settings.updateMode;
        var oldBackground = InputSystem.settings.backgroundBehavior;
        var oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        Keyboard keyboard = null; GameObject root = null; EveEntityLoader loader = null; int checks = 0; bool scheduled = false;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        try
        {
            foreach (var other in others) other.enabled = false;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            root = new GameObject("Isolated Eve input fixture"); root.SetActive(false); root.transform.position = new Vector3(100, 10, 100);
            root.AddComponent<EveEntityIdentity>().Assign(EveEntityId.NewLocal(), EveId.Parse("project_n:eve"), 0);
            root.AddComponent<EveCharacterStat>(); var skill = root.AddComponent<EveInputPlayProbe>();
            loader = root.AddComponent<EveEntityLoader>(); loader.Load(EveRegistrySession.Current); root.SetActive(true);
            root.GetComponent<PNAbilityManager>().enabled = false; // Clock-independent input fixture.
            keyboard = InputSystem.AddDevice<Keyboard>();
            void Tap(Key key)
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); InputSystem.Update();
                System.Threading.Thread.Sleep(25);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
            }
            Check(skill.PC.Controller.Ulti.bindings.Any(b => b.effectivePath == "<Gamepad>/rightShoulder"), "Ulti gamepad binding conflicts with Jump.");
            Tap(Key.J); Check(skill.CurrentCast == EveCast.Attack, "J Tap failed: ready=" + skill.IsReady + "; loader=" + loader.IsReady + "; trace=" + skill.InputTrace + "; enabled=" + skill.PC.Controller.Attack.enabled + "; delay=" + skill.AttackInput.RepeatDelay); skill.Tick(.4f);
            Tap(Key.U); Check(skill.CurrentCast == EveCast.Skill, "U Tap failed.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.K)); InputSystem.Update();
            Check(skill.CurrentCast == EveCast.Dash && skill.DashRecovery.Current == 2, "K key-down failed to interrupt Skill immediately.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
            Check(skill.DashRecovery.Current == 2, "K release consumed another charge."); skill.Tick(.2f);
            skill.BurstGauge.Add(150); Tap(Key.O); Check(skill.IsInBurst, "O failed to activate Burst.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.J)); InputSystem.Update();
            finish = () =>
            {
                InputSystem.Update();
                Check(skill.CurrentCast == EveCast.Repeat && skill.SlashStacks.Current == 5,
                    "Held J failed PNRepeat; cast=" + skill.CurrentCast + "; stacks=" + skill.SlashStacks.Current +
                    "; pressed=" + keyboard.jKey.isPressed + "; trace=" + skill.InputTrace);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); skill.Tick(.5f);
                Check(!skill.IsBusy && skill.SlashStacks.Current == 5, "Release did not stop Repeat chaining.");
                Tap(Key.I); Check(skill.CurrentCast == EveCast.Ultimate, "I Tap failed.");
                Tap(Key.K); Check(skill.CurrentCast == EveCast.Ultimate, "Dash interrupted Ultimate.");
                skill.Tick(3f); Check(!skill.IsBusy && !skill.IsInBurst, "Burst Ultimate did not finish after three slashes.");
                LastResult = "PASS " + checks + " Play Mode Input System checks: J Tap/Repeat/release, U, K interrupts, O, I, gamepad binding.";
            };
            cleanup = () =>
            {
                if (keyboard != null) InputSystem.RemoveDevice(keyboard);
                if (loader != null) loader.Unload();
                if (root != null) UnityEngine.Object.Destroy(root);
                InputSystem.settings.updateMode = oldUpdate;
                InputSystem.settings.backgroundBehavior = oldBackground;
                InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput;
                for (int i = 0; i < others.Length; i++) if (others[i] != null) others[i].enabled = enabled[i];
            };
            started = EditorApplication.timeSinceStartup; LastResult = "Running held-key frame check";
            EditorApplication.update += Pump; scheduled = true;
            return LastResult;
        }
        finally
        {
            if (!scheduled)
            {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (loader != null) loader.Unload();
            if (root != null) UnityEngine.Object.Destroy(root);
            InputSystem.settings.updateMode = oldUpdate;
            InputSystem.settings.backgroundBehavior = oldBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput;
            for (int i = 0; i < others.Length; i++) if (others[i] != null) others[i].enabled = enabled[i];
            }
        }
    }
    private static void Pump()
    {
        InputSystem.Update();
        if (EditorApplication.timeSinceStartup - started < .75) return;
        EditorApplication.update -= Pump;
        try
        {
            if (!EditorApplication.isPlaying) throw new Exception("Play Mode ended before input check completed.");
            finish();
        }
        catch (Exception error) { LastResult = "FAIL: " + error; }
        finally
        {
            try { cleanup?.Invoke(); }
            finally { finish = cleanup = null; System.IO.File.WriteAllText("F:/unity/Project_N/.utmp/EveMovesetInputResult.txt", LastResult); }
        }
    }
}
#endif
