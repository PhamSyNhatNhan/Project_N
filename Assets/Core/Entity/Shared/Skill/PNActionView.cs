using System;
using UnityEngine;

// EN: Observed View (Docs/ObservedViewPlan.md, PV1). The picture of one Action run. A view is built by the engine from the run's context (direction, position, scale) and driven by the
//     engine on EVERY machine that shows the run: the Authority and the Predicted machine run the Action AND its view (the age comes from the Action's execution frame); an Observed machine
//     runs only the view (the age comes from the run's progress on the Action stream). A view never touches gameplay state (no Ability, stat, Buff, motion, hitbox) and never knows its role.
// VI: Phần trình diễn của một run Action. Engine dựng từ ngữ cảnh và đẩy view ở mọi máy; view không đụng trạng thái gameplay và không biết vai trò của máy.
public enum PNActionViewEnd { Completed, Cancelled }

// A gameplay result of the run, handed to its view (PV4): a hit that landed, a heal or Buff that reached someone. `Kind` is a registered vocabulary word, not a network field.
public readonly struct PNActionViewResult
{
    public readonly string Kind;
    public readonly Vector3 Point;
    public readonly long TargetId;
    public readonly double Age;
    public PNActionViewResult(string kind, Vector3 point, long targetId, double age) { Kind = kind; Point = point; TargetId = targetId; Age = age; }
}

public interface IPNActionView
{
    // True while the view still shows something after `End` (a fading trail); the engine keeps it and ends it again on teardown.
    bool HasTail { get; }
    // Set by the engine on the run that leads (when several runs of one Action overlap on an Observed machine, the newest one leads); a lone run always leads.
    bool Lead { set; }
    // `age` is the run's progress in its own seconds (the Action's clock, attack speed and slow areas included), `rate` the speed it advances at now (0 while held), `seeking` is true
    // when the age was SET (first placement, late join, a big lag) instead of advanced: the view must not replay what lies between the old and the new age.
    void Advance(double age, double rate, bool seeking);
    // A fact the Action decided at `age` (`context.Present(key, values)`): one-shot pictures. Facts older than the one-shot window of a seek are dropped by the engine before they get here.
    void Fact(string key, double[] values, double age);
    void Result(in PNActionViewResult result);
    // The machine cannot read the run now (coverage lost): stop drawing, keep the state.
    void Hide();
    void End(PNActionViewEnd kind);
}

public readonly struct PNActionViewBuild
{
    public readonly PNSkill Skill;
    public readonly PNSkillData Data;
    public readonly Vector3 Direction;
    public readonly Vector3? Position;
    public readonly double Scale;
    public PNActionViewBuild(PNSkill skill, PNSkillData data, Vector3 direction, Vector3? position, double scale)
    { Skill = skill; Data = data; Direction = direction; Position = position; Scale = scale; }
}

// The catch-up policy (decision 2.9) and the view declaration rule (decision 2.7), pure so a check can run them without a scene.
public static class PNActionViewPolicy
{
    public const double GradualWindow = 0.15;   // seconds: a lag up to this is caught up gradually, a bigger one is a seek
    public const double MaxCatchUpRate = 1.5;   // the view runs at most this much faster than the run while it catches up
    public const double OneShotWindow = 0.1;    // seconds: after a seek, a one-shot fact older than this behind the new age is dropped
    // PV8: an Action with neither `WithView` nor `NoView` is a registration error (mods included); the legacy replay it used to fall back to is gone.
    public const bool DeclarationRequired = true;

    // The age a view takes after one frame. `placed` is false for the first placement. `dt` <= 0 means "no clock": the view takes the target (a seek when it is far).
    public static double Follow(double age, double target, double dt, double rate, bool placed, out bool seeking)
    {
        seeking = false;
        if (!placed) { seeking = true; return target; }
        double lag = target - age;
        if (dt <= 0) { seeking = Math.Abs(lag) > GradualWindow; return target; }
        if (lag < -GradualWindow) { seeking = true; return target; }
        if (lag < 0) return age;
        double allowed = dt * Math.Max(rate, 1d) * MaxCatchUpRate;
        if (lag <= allowed) return target;
        if (lag <= GradualWindow) return age + allowed;
        seeking = true; return target;
    }

    public static bool DropsFact(double factAge, double targetAge, bool seeking) => seeking && targetAge - factAge > OneShotWindow;

    public static void ValidateDeclaration(PNSkillActionDefinition definition, bool required)
    {
        if (required && !definition.ViewDeclared)
            throw new FormatException("Action " + definition.Id + " must declare WithView(...) or NoView().");
    }
}

// A presentation fact of a run on the wire (PV2): `[age, key, values...]`. Bounds: at most `MaxPerRun` facts per run, a key of at most `MaxKey` characters from
// `[a-z0-9_.:-]`, at most `MaxValues` finite numbers (|v| <= `MaxMagnitude`). Immutable once made, so the decoder and the view can share it.
public sealed class PNActionFact
{
    public const int MaxPerRun = 8, MaxKey = 32, MaxValues = 4;
    // The fact an Action presents for the one it helped (`context.PresentRecipient`): one number, the entity id.
    public const string RecipientKey = "recipient";
    public const double MaxMagnitude = 1e9;
    public readonly double Age;
    public readonly string Key;
    private readonly double[] values;
    public int Count => values.Length;
    public double this[int index] => values[index];
    public double[] CopyValues() => (double[])values.Clone();
    public PNActionFact(double age, string key, double[] values)
    {
        if (!ValidAge(age)) throw new ArgumentException("A fact age is a finite number >= 0.", nameof(age));
        if (!ValidKey(key)) throw new ArgumentException("A fact key is 1-" + MaxKey + " characters of [a-z0-9_.:-].", nameof(key));
        values = values ?? Array.Empty<double>();
        if (values.Length > MaxValues) throw new ArgumentException("A fact carries at most " + MaxValues + " numbers.", nameof(values));
        foreach (var v in values) if (!ValidValue(v)) throw new ArgumentException("A fact number must be finite and |v| <= " + MaxMagnitude + ".", nameof(values));
        Age = age == 0 ? 0 : age; Key = key; this.values = (double[])values.Clone();
    }
    public static bool ValidAge(double age) => !double.IsNaN(age) && !double.IsInfinity(age) && age >= 0;
    public static bool ValidValue(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && Math.Abs(v) <= MaxMagnitude;
    public static bool ValidKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxKey) return false;
        foreach (char c in key) if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_' || c == '.' || c == ':' || c == '-')) return false;
        return true;
    }
}
