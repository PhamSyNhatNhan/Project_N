using System;
using System.Globalization;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Interactions;
using static EveSessionSmokeTests;

public class PNInputProbeSkill : PNPlayerSkill
{
    public string Trace = "";
    public void InitializeInput() { Awake(); OnEnable(); }
    public void EnableInput() { enabled = true; OnEnable(); }
    public void DisableInput() { enabled = false; OnDisable(); }
    public void DestroyInput() => OnDestroy();
    protected override void StartTapAttack() => Trace += "start;";
    protected override void EnterTapAttack() => Trace += "enter;";
    protected override void TapAttack() => Trace += "tap;";
    protected override void ExitTapAttack() => Trace += "exit;";
    protected override void CanceledTapAttack() => Trace += "cancel;";
    protected override void RepeatAttack() => Trace += "repeat;";
}

public static class PNPlayerSkillInputTests
{
    public static void Run()
    {
        var config = new PlayerSkillInputConfig { HoldMode = PlayerSkillHoldMode.SlowTap, TapDuration = 0.2f, SlowTapDuration = 0.8f };
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Check(config.BuildInteractions() == "tap(duration=0.2);slowTap(duration=0.8)", "input timing uses invariant culture");
        }
        finally { CultureInfo.CurrentCulture = culture; }
        config.HoldMode = PlayerSkillHoldMode.Hold; config.HoldDuration = 0.6f;
        Check(config.BuildInteractions().Contains("hold(duration=0.6)"), "hold duration is configurable");
        config.CanTap = false; config.HoldMode = PlayerSkillHoldMode.None;
        Check(!config.HasInteraction && config.BuildInteractions() == "", "no input modes produces empty configuration");
        config.RepeatInterval = float.NaN;
        Reject(() => config.Validate(), "invalid input timing rejected");

        var first = new PNInputProbeSkill();
        var second = new PNInputProbeSkill();
        first.InitializeInput(); second.InitializeInput();
        Check(first.PC != second.PC && first.PC.Controller.Attack != second.PC.Controller.Attack,
            "skill input is owned per entity without a Move component");
        var tap = new TapInteraction();
        first.PC.Controller.Attack.EmitStarted(tap);
        first.PC.Controller.Attack.EmitPerformed(tap);
        Check(first.Trace == "start;enter;tap;exit;" && second.Trace == "", "input hooks retain original dispatch order and isolated callbacks");
        first.CanInput = false;
        Check(first.Trace.EndsWith("cancel;") && !first.PC.Controller.Attack.enabled, "blocking input cancels held interaction");
        first.CanInput = true;
        first.MenuInputBlocked = true;
        Check(!first.CanInput && !first.PC.Controller.Dash.enabled && second.CanInput,
            "menu gate blocks local skill actions without blocking another entity");
        first.CanInput = false;
        first.MenuInputBlocked = false;
        Check(!first.CanInput && !first.PC.Controller.Attack.enabled,
            "closing menu preserves a gameplay input lock acquired while open");
        first.MenuInputBlocked = true;
        first.CanInput = true;
        Check(!first.CanInput && !first.PC.Controller.Skill.enabled,
            "gameplay unlock cannot bypass an open menu");
        first.MenuInputBlocked = false;
        Check(first.CanInput && first.PC.Controller.Attack.enabled,
            "removing the last menu gate rearms configured input");
        first.Trace = "";
        first.DisableInput(); first.EnableInput(); first.DisableInput(); first.EnableInput();
        first.PC.Controller.Attack.EmitPerformed(tap);
        Check(first.Trace == "enter;tap;exit;", "re-enable does not duplicate callbacks");
        first.AttackInput.CanTap = false;
        first.AttackInput.HoldMode = PlayerSkillHoldMode.Repeat;
        first.AttackInput.RepeatDelay = 0.5f; first.AttackInput.RepeatInterval = 0.15f;
        first.RefreshInput();
        Check(first.PC.Controller.Attack.bindings[0].overrideInteractions == "PNRepeat(delay=0.5,interval=0.15)", "runtime reconfiguration replaces interactions");
        first.Trace = "";
        first.PC.Controller.Attack.EmitPerformed(new PNRepeatInteraction());
        Check(first.Trace == "repeat;", "repeat retains direct repeat hook");
        first.AttackInput.HoldMode = PlayerSkillHoldMode.None;
        first.RefreshInput();
        Check(!first.PC.Controller.Attack.enabled && first.PC.Controller.Attack.bindings[0].overrideInteractions == "",
            "removing all modes clears old override and disables button");
        first.Unload();
        Check(!first.PC.Controller.Skill.enabled, "unload disables skill input");
        var oldAction = second.PC.Controller.Attack;
        second.DestroyInput();
        oldAction.Enable(); oldAction.EmitPerformed(tap);
        Check(second.Trace == "" && oldAction.disposed && second.PC == null, "destroy detaches callbacks and disposes controls");
        first.DestroyInput();

        var repeat = new PNRepeatInteraction { delay = 0.7f, interval = 0.2f };
        var context = new InputInteractionContext { Actuated = true };
        repeat.Process(ref context);
        Check(context.Trace == "start;" && context.Timeout == 0.7f, "repeat starts with configured delay");
        context.timerHasExpired = true;
        repeat.Process(ref context);
        Check(context.Trace == "start;perform;" && context.Timeout == 0.2f, "repeat timeout performs and schedules next interval");
        context.Actuated = false;
        repeat.Process(ref context);
        Check(context.Trace == "start;perform;cancel;", "release wins over expired repeat timer");
        repeat.Reset();
        repeat.delay = 0f; repeat.interval = 0f;
        context = new InputInteractionContext { Actuated = true };
        repeat.Process(ref context);
        Check(context.Timeout == InputSystem.settings.defaultHoldTime, "zero repeat delay selects project default");
        context.timerHasExpired = true;
        repeat.Process(ref context);
        Check(context.Timeout == 0.1f, "zero repeat interval selects positive fallback");
    }
}
