#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: The pictures of the three Starreaper runs (Docs/ObservedViewPlan.md PV6), placed by the run's AGE the same on the machine that runs the Action and on one that only watches it.
//     Pierce and Rain draw the bow back for `windup`, release it, and rest; the Action keeps the timeline and the shot and presents the one decision that changes the picture while the bow is drawn:
//     the `aim` fact (planar x, z; sent only when the aim has turned noticeably, a few times per run). Afterimage retreats, then shoots: the Action presents `finish` (1 = the shot was made, 0 = the cast ended
//     without it) with the planar aim at that moment; with no `finish` the shot is shown when the registered retreat is over.
// VI: Hình của ba run Starreaper theo tuổi của run. Pierce/Rain: kéo cung `windup` rồi thả; Action cấp dữ kiện `aim` (x, z) khi hướng ngắm đổi đáng kể. Afterimage: lùi rồi bắn; Action cấp `finish`.
public static class PNStarreaperViewShared
{
    public const string AimFact = "aim";
    public static Vector3 Flat(Vector3 v, Vector3 fallback) { v.y = 0; return v.sqrMagnitude < 1e-6f ? fallback : v.normalized; }
    public static void Face(PNVisualBody body, bool lead, Vector3 direction)
    {
        if (!lead || direction.sqrMagnitude < 1e-8f) return;
        var actor = body != null ? body.Actor : null; var camera = PNViewCamera.Current;
        if (actor != null) actor.SetFacingLeft(Vector3.Dot(direction, camera != null ? camera.transform.right : Vector3.right) < 0);
    }
}

// Xuyen Tinh and Mua Tinh share one picture: the bow drawn for `windup` (rain: raised to the sky), then released.
public sealed class PNStarreaperDrawView : IPNActionView
{
    private readonly bool rain; private readonly float windup, recovery;
    private readonly string charge, release;
    private PNStarreaperPresentation presentation; private PNVisualBody body;
    private bool lead = true, fired, ended; private Vector3 aim;
    public bool HasTail => false;
    public bool Lead { set { lead = value; } }
    public bool Ended => ended;
    public bool Fired => fired;

    public static IPNActionView CreatePierce(PNActionViewBuild build) => new PNStarreaperDrawView(build, false);
    public static IPNActionView CreateRain(PNActionViewBuild build) => new PNStarreaperDrawView(build, true);
    private PNStarreaperDrawView(PNActionViewBuild build, bool rain)
    {
        this.rain = rain;
        var p = (JObject)build.Data.Action.Perform;
        windup = p["windup"].Value<float>(); recovery = p["recovery"].Value<float>();
        charge = rain ? "rain_charge" : "pierce_charge"; release = rain ? "rain_release" : "pierce_release";
        var skill = build.Skill; aim = PNStarreaperViewShared.Flat(build.Direction, new Vector3(0f, 0f, 1f));
        body = skill.GetComponent<PNVisualBody>();
        PNStarreaperViewShared.Face(body, lead, aim);
        presentation = skill.GetComponent<PNStarreaperPresentation>();
        presentation?.BeginDraw(windup, Pose(), true, rain);
    }
    private Vector3 Pose() => rain ? Vector3.up * .99f + aim * .14f : aim;

    public void Advance(double age, double rate, bool seeking)
    {
        if (ended) return;
        float t = (float)age;
        if (t < windup)
        {
            if (lead) body?.PlayActionAt(charge, age, windup, seeking);
            presentation?.SetAge(t);
            return;
        }
        if (!fired)
        {
            fired = true;
            if (presentation != null) { presentation.SetAge(windup); presentation.Tick(0); presentation.Release(); presentation.EndPose(); }
        }
        if (lead) body?.PlayActionAt(release, t - windup, recovery, seeking);
    }
    public void Fact(string key, double[] values, double age)
    {
        if (ended || key != PNStarreaperViewShared.AimFact || values == null || values.Length < 2) return;
        aim = PNStarreaperViewShared.Flat(new Vector3((float)values[0], 0f, (float)values[1]), aim);
        if (!fired) presentation?.SetAim(Pose());
        PNStarreaperViewShared.Face(body, lead, aim);
    }
    public void Result(in PNActionViewResult result) { }
    public void Hide() { }
    public void End(PNActionViewEnd kind)
    {
        if (ended) return; ended = true;
        presentation?.EndPose(); presentation = null;
        body?.EndAction(); body = null;
    }
}

public sealed class PNStarreaperAfterimageView : IPNActionView
{
    public const string FinishFact = "finish";
    private const float ArrivalGrace = .1f;                                     // a retreat that sent no `finish` this long after its registered time is shown as over, the shot made
    private readonly float retreat, recovery;
    private PNStarreaperPresentation presentation; private PNVisualBody body;
    private bool lead = true, finished, shot, ended, overlayStarted; private float finishAt; private Vector3 aim;
    public bool HasTail => false;
    public bool Lead { set { lead = value; } }
    public bool Ended => ended;
    public bool Shot => shot;

    public static IPNActionView Create(PNActionViewBuild build) => new PNStarreaperAfterimageView(build);
    private PNStarreaperAfterimageView(PNActionViewBuild build)
    {
        var p = (JObject)build.Data.Action.Perform;
        recovery = p["recovery"].Value<float>(); retreat = p["motion"]["duration"].Value<float>();
        aim = PNStarreaperViewShared.Flat(build.Direction, new Vector3(0f, 0f, 1f));
        var skill = build.Skill;
        body = skill.GetComponent<PNVisualBody>();
        PNStarreaperViewShared.Face(body, lead, aim);
        presentation = skill.GetComponent<PNStarreaperPresentation>(); presentation?.BeginRetreat(retreat);
    }

    public void Advance(double age, double rate, bool seeking)
    {
        if (ended) return;
        float t = (float)age;
        if (!finished && t >= retreat + ArrivalGrace) Finish(true, retreat, aim);
        if (!finished) { if (lead) body?.PlayActionAt("dash", age, retreat + .1f, seeking); return; }
        if (!shot) return;
        if (lead)
        {
            // `shoot` is an Overlay motion on the rig: it is started once and plays on the actor's clock over the base. The base stays `dash`, held at the end of the retreat
            // (driving the base with the shot's age would pull it back to the start of the dash).
            body?.PlayActionAt("dash", Mathf.Min(t, retreat), retreat + .1f, seeking);
            if (!overlayStarted) { overlayStarted = true; body?.Actor?.Play("shoot", true); }
        }
        presentation?.SetAge(t - finishAt);
    }
    private void Finish(bool made, float at, Vector3 direction)
    {
        finished = true; finishAt = at; shot = made; aim = direction;
        if (!made) return;
        PNStarreaperViewShared.Face(body, lead, aim);
        if (presentation != null) { presentation.BeginDraw(recovery, aim); presentation.Release(); }
    }
    public void Fact(string key, double[] values, double age)
    {
        if (ended || finished || key != FinishFact || values == null || values.Length < 1) return;
        var direction = values.Length >= 3 ? PNStarreaperViewShared.Flat(new Vector3((float)values[1], 0f, (float)values[2]), aim) : aim;
        Finish(values[0] > .5, Mathf.Max((float)age, 0f), direction);
    }
    public void Result(in PNActionViewResult result) { }
    public void Hide() { }
    public void End(PNActionViewEnd kind)
    {
        if (ended) return; ended = true;
        presentation?.EndPose(); presentation = null;
        body?.EndAction(); body = null;
    }
}
#endif
