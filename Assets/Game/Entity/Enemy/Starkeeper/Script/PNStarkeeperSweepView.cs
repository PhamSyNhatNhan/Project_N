#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: The picture of one Quet sao run (Docs/ObservedViewPlan.md PV6): the warning sector, the two strokes and their sparks, the spear pose and the body's motion, all placed by the run's AGE, the same on
//     the machine that runs the Action and on one that only watches it. The Action keeps the timeline, the step, the hitboxes and the one decision that changes the picture (the re-aim, presented as the `aim` fact).
//     The view never touches gameplay and never asks which machine it is on.
// VI: Hình của một lần Quet sao theo tuổi của run: cảnh báo, hai nhát, tia lửa, tư thế giáo, thân. Action chỉ còn logic và dữ kiện `aim` (hướng nhát hai).
public sealed class PNStarkeeperSweepView : IPNActionView
{
    public const string AimFact = "aim";
    private const float StrokeSeconds = .36f;                                  // a stroke older than this is gone: a late joiner does not draw it
    private readonly PNSkill skill;
    private readonly float windup, gap, reaimAt, recovery;
    private readonly Vector3 first;
    private Vector3 second;
    private PNStarkeeperPresentation presentation; private PNVisualBody body; private PNStarkeeperTelegraph telegraph; private PNStarkeeperSweepVisual visual;
    private bool lead = true, emitted0, emitted1, ended, hidden, placed;
    public bool HasTail => false;
    public bool Lead { set { lead = value; } }
    public bool Ended => ended;
    public PNStarkeeperSweepVisual Visual => visual;
    public PNStarkeeperTelegraph Telegraph => telegraph;

    public static IPNActionView Create(PNActionViewBuild build) => new PNStarkeeperSweepView(build);
    private PNStarkeeperSweepView(PNActionViewBuild build)
    {
        skill = build.Skill;
        var p = (JObject)build.Data.Action.Perform;
        windup = p["windup"].Value<float>(); gap = p["gap"].Value<float>(); reaimAt = p["reaimAt"].Value<float>(); recovery = p["recovery"].Value<float>();
        float angle = p["angle"].Value<float>(), radius = PNHitCapture.ScaleLength(p["radius"].Value<float>(), build.Scale);
        first = Flat(build.Direction, new Vector3(0f, 0f, 1f)); second = first;
        try
        {
            body = skill.GetComponent<PNVisualBody>();
            presentation = skill.GetComponent<PNStarkeeperPresentation>(); presentation?.BeginSweep(windup, gap, first);
            telegraph = PNStarkeeperTelegraph.Sector(skill.transform.position, first, radius, angle, skill.gameObject.scene);
            visual = PNStarkeeperSweepVisual.Create(skill.transform, presentation, windup, gap, radius, angle, first);
        }
        catch { Release(); throw; }
    }
    private static Vector3 Flat(Vector3 v, Vector3 fallback) { v.y = 0; return v.sqrMagnitude < 1e-6f ? fallback : v.normalized; }

    public void Advance(double age, double rate, bool seeking)
    {
        if (ended) return;
        float t = (float)age;
        if (hidden || !placed) { if (visual != null) visual.gameObject.SetActive(true); hidden = false; }
        placed = true;
        if (lead)
        {
            body?.PlayActionAt("star_sweep", age, windup + gap + recovery, seeking);
            presentation?.SetAge(t);
        }
        if (t < windup) telegraph?.SetOrigin(skill.transform.position);
        telegraph?.SetPhase(Mathf.Clamp01(t / windup));
        // a second stroke the run has already passed by more than its life is not drawn for a late joiner; a first one likewise
        if (!emitted0 && t >= windup) { emitted0 = true; Stroke(0, windup, first, t); telegraph?.Hide(); }
        if (!emitted1 && t >= windup + gap)
        {
            emitted1 = true; if (t < windup + reaimAt) second = first;
            Stroke(1, windup + gap, second, t); telegraph?.Hide();
        }
        visual?.SetTime(t);
    }
    private void Stroke(int index, float hitAt, Vector3 direction, float now)
    {
        if (visual == null || now - hitAt > StrokeSeconds) return;
        visual.SetTime(hitAt); visual.Emit(index, skill.transform.position, direction); visual.SetTime(now);
    }

    public void Fact(string key, double[] values, double age)
    {
        if (ended || key != AimFact || values == null || values.Length < 2) return;
        second = Flat(new Vector3((float)values[0], 0f, (float)values[1]), first);
        presentation?.SetAim(second);
        var actor = body != null ? body.Actor : null; var camera = PNViewCamera.Current;
        if (lead && actor != null) actor.SetFacingLeft(Vector3.Dot(second, camera != null ? camera.transform.right : Vector3.right) < 0);
    }
    public void Result(in PNActionViewResult result) { if (!ended && result.Kind == "hit") visual?.Hit(result.Point); }
    public void Hide() { if (ended) return; hidden = true; telegraph?.Hide(); if (visual != null) visual.gameObject.SetActive(false); }
    public void End(PNActionViewEnd kind) { if (ended) return; ended = true; Release(); }
    private void Release()
    {
        visual?.Dispose(); visual = null;
        telegraph?.Dispose(); telegraph = null;
        presentation?.EndPose(); presentation = null;
        body?.EndAction(); body = null;
    }
}
#endif
