#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: The pictures of the two Starforger runs (Docs/ObservedViewPlan.md PV6), placed by the run's AGE the same way on the machine that runs the Action and on one that only watches it.
//     Slam: the warning ring (sized by melee_scale like the hit), the crouch / air / slam body states, the hammer pose and the landing burst. The Action keeps the hop, the invulnerability and the hit and presents the
//     one physical result the picture depends on: `land` (1 = it landed and slammed, 0 = it was stopped before). With no `land` the landing is shown at its registered time.
//     Star: the cast pose and the forge. The star itself is a summon and is not part of the picture.
// VI: Hình của hai run Starforger theo tuổi của run. Slam: vòng cảnh báo, các trạng thái thân, tư thế búa, vụ nổ chạm đất; Action cấp dữ kiện `land` (1 chạm đất và nện, 0 bị chặn).
public sealed class PNStarforgerSlamView : IPNActionView
{
    public const string LandFact = "land";
    private const float ArrivalGrace = .1f, ImpactSeconds = .65f;                // a landing older than the burst's life is not replayed for a late joiner
    private readonly PNSkill skill;
    private readonly float crouch, hop, recovery;
    private PNStarforgerPresentation presentation; private PNVisualBody body; private PNStarforgerTelegraph telegraph;
    private bool lead = true, landed, slammed, ended, hidden; private float landAt;
    public bool HasTail => false;
    public bool Lead { set { lead = value; } }
    public bool Ended => ended;
    public PNStarforgerTelegraph Telegraph => telegraph;
    public bool Landed => landed;

    public static IPNActionView Create(PNActionViewBuild build) => new PNStarforgerSlamView(build);
    private PNStarforgerSlamView(PNActionViewBuild build)
    {
        skill = build.Skill;
        var p = (JObject)build.Data.Action.Perform;
        crouch = p["crouch"].Value<float>(); recovery = p["recovery"].Value<float>(); hop = p["hop"]["duration"].Value<float>();
        var landing = build.Position ?? throw new InvalidOperationException("The slam view needs the landing point.");
        float radius = PNHitCapture.ScaleLength(p["hit"]["radius"].Value<float>(), build.Scale);
        try
        {
            body = skill.GetComponent<PNVisualBody>();
            var d = build.Direction; d.y = 0;
            Face(d);
            presentation = skill.GetComponent<PNStarforgerPresentation>(); presentation?.BeginSlam(crouch, hop, recovery);
            telegraph = PNStarforgerTelegraph.Create(skill.gameObject.scene, landing, radius, crouch + hop);
        }
        catch { Release(); throw; }
    }
    private void Face(Vector3 direction)
    {
        if (!lead || direction.sqrMagnitude < 1e-8f) return;
        var actor = body != null ? body.Actor : null; var camera = PNViewCamera.Current;
        if (actor != null) actor.SetFacingLeft(Vector3.Dot(direction, camera != null ? camera.transform.right : Vector3.right) < 0);
    }

    public void Advance(double age, double rate, bool seeking)
    {
        if (ended) return;
        float t = (float)age;
        if (hidden) { telegraph?.SetVisible(true); hidden = false; }
        if (!landed && t >= crouch + hop + ArrivalGrace) Land(true, crouch + hop, t);
        if (lead)
        {
            if (!landed) { if (t < crouch) body?.PlayActionAt("crouch", age, crouch, seeking); else body?.PlayActionAt("air", t - crouch, hop, seeking); }
            else if (slammed) body?.PlayActionAt("slam", t - landAt, recovery, seeking);
            presentation?.SetAge(t);
        }
        telegraph?.SetTime(t);
    }
    private void Land(bool slam, float at, float now)
    {
        landed = true; slammed = slam; landAt = at;
        if (presentation != null) { presentation.SetAge(at); presentation.Slam(); }
        telegraph?.SetTime(at);
        if (now - at <= ImpactSeconds) telegraph?.Impact();
        else { telegraph?.Dispose(); telegraph = null; }
        telegraph?.SetTime(now);
    }

    public void Fact(string key, double[] values, double age)
    {
        if (ended || landed || key != LandFact || values == null || values.Length < 1) return;
        float at = Mathf.Max((float)age, crouch);
        Land(values[0] > .5, at, Mathf.Max(at, telegraph != null ? telegraph.Age : at));
    }
    public void Result(in PNActionViewResult result) { }
    public void Hide() { if (ended) return; hidden = true; telegraph?.SetVisible(false); }
    public void End(PNActionViewEnd kind) { if (ended) return; ended = true; Release(); }
    private void Release()
    {
        telegraph?.Dispose(); telegraph = null;
        presentation?.EndPose(); presentation = null;
        body?.EndAction(); body = null;
    }
}

public sealed class PNStarforgerStarView : IPNActionView
{
    private readonly PNSkill skill;
    private readonly float cast, recovery;
    private PNStarforgerPresentation presentation; private PNVisualBody body;
    private bool lead = true, ended;
    public bool HasTail => false;
    public bool Lead { set { lead = value; } }
    public bool Ended => ended;

    public static IPNActionView Create(PNActionViewBuild build) => new PNStarforgerStarView(build);
    private PNStarforgerStarView(PNActionViewBuild build)
    {
        skill = build.Skill;
        var p = (JObject)build.Data.Action.Perform;
        cast = p["cast"].Value<float>(); recovery = p["recovery"].Value<float>();
        body = skill.GetComponent<PNVisualBody>();
        var d = build.Direction; d.y = 0;
        var actor = body != null ? body.Actor : null; var camera = PNViewCamera.Current;
        if (lead && actor != null && d.sqrMagnitude > 1e-8f) actor.SetFacingLeft(Vector3.Dot(d, camera != null ? camera.transform.right : Vector3.right) < 0);
        presentation = skill.GetComponent<PNStarforgerPresentation>(); presentation?.BeginStar(cast, recovery);
    }
    public void Advance(double age, double rate, bool seeking)
    {
        if (ended) return;
        body?.PlayActionAt("cast_star", age, cast + recovery, seeking);
        presentation?.SetAge((float)age);
    }
    public void Fact(string key, double[] values, double age) { }
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
