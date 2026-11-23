#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: The picture of one Diem sao run (Docs/ObservedViewPlan.md PV6): the thrown gold spear and its mark, the spectral spear, the trails, the spear pose and the body's motion, placed by the run's AGE.
//     The marked point is the run's context position (the behavior already clamps it to the registered range), so a machine that joins late needs nothing else. The Action keeps the motion and presents
//     the one physical result the picture depends on: `finish` (1 = the Starkeeper reached the star, 0 = a wall stopped it). With no `finish` the lunge is shown as arriving.
// VI: Hình của một lần Diem sao theo tuổi của run. Điểm đánh dấu là vị trí trong ngữ cảnh run; Action chỉ cấp dữ kiện `finish` (1 tới sao, 0 bị tường chặn).
public sealed class PNStarkeeperPointView : IPNActionView
{
    public const string FinishFact = "finish";
    private const float ArrivalGrace = .1f;                                    // a lunge that sent no `finish` this long after its registered time is shown as arrived
    private readonly PNSkill skill;
    private readonly float windup, lunge, recovery;
    private PNStarkeeperPresentation presentation; private PNVisualBody body; private PNStarkeeperPointVisual visual;
    private bool lead = true, finished, ended, hidden;
    public bool HasTail => visual != null && visual.IsFading;
    public bool Lead { set { lead = value; } }
    public bool Ended => ended;
    public PNStarkeeperPointVisual Visual => visual;

    public static IPNActionView Create(PNActionViewBuild build) => new PNStarkeeperPointView(build);
    private PNStarkeeperPointView(PNActionViewBuild build)
    {
        skill = build.Skill;
        var p = (JObject)build.Data.Action.Perform;
        windup = p["windup"].Value<float>(); recovery = p["recovery"].Value<float>(); lunge = p["motion"]["duration"].Value<float>();
        var anchor = build.Position ?? throw new InvalidOperationException("The star point view needs the marked point.");
        try
        {
            body = skill.GetComponent<PNVisualBody>();
            var facing = build.Direction; facing.y = 0;
            presentation = skill.GetComponent<PNStarkeeperPresentation>(); presentation?.BeginPoint(windup, lunge, facing.sqrMagnitude > 1e-6f ? facing.normalized : new Vector3(0f, 0f, 1f));
            visual = PNStarkeeperPointVisual.Create(skill.transform, presentation, anchor, p["markRadius"].Value<float>(), windup, null, facing);
        }
        catch { Release(false); throw; }
    }

    public void Advance(double age, double rate, bool seeking)
    {
        if (ended) return;
        float t = (float)age;
        if (hidden) { if (visual != null) visual.gameObject.SetActive(true); hidden = false; }
        if (lead)
        {
            body?.PlayActionAt("star_point", age, windup + lunge + recovery, seeking);
            presentation?.SetAge(t);
        }
        if (visual == null) return;
        if (!finished && t >= windup + lunge + ArrivalGrace) Finish(true, windup + lunge, t);
        visual.SetTime(t, t >= windup && !finished);
    }
    private void Finish(bool arrived, float at, float now)
    {
        finished = true;
        visual.SetTime(at, false); visual.Finish(arrived); visual.SetTime(now, false);
    }

    public void Fact(string key, double[] values, double age)
    {
        if (ended || finished || visual == null || key != FinishFact || values == null || values.Length < 1) return;
        float at = Mathf.Max((float)age, windup); Finish(values[0] > .5, at, Mathf.Max(at, visual.VisualAge));
    }
    public void Result(in PNActionViewResult result) { }
    public void Hide() { if (ended) return; hidden = true; if (visual != null) visual.gameObject.SetActive(false); }
    public void End(PNActionViewEnd kind)
    {
        if (ended) { if (visual != null) visual.Dispose(); visual = null; return; }               // the second end is the teardown: the recall fade is cut
        ended = true; Release(kind == PNActionViewEnd.Cancelled);
    }
    private void Release(bool fade)
    {
        if (visual != null) { if (fade) visual.CancelFade(); else visual.Dispose(); if (!fade) visual = null; }
        presentation?.EndPose(); presentation = null;
        body?.EndAction(); body = null;
    }
}
#endif
