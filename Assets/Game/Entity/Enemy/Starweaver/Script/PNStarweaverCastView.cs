#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: The picture of one Starweaver cast (Docs/ObservedViewPlan.md PV6, group 2): the cast pose of the puppet (`cast_weave` / `cast_constellation`) and the weaving light that gathers for `windup`,
//     placed by the run's AGE the same on every machine. The Action keeps the projectiles / the triangle blast and reads the weapon muzzle at the release; it presents no fact: nothing physical changes this picture.
// VI: Hình của một lần Starweaver cast: tư thế thân và ánh sáng dệt gom trong `windup`, đặt theo tuổi của run. Action giữ đạn / vùng tam giác; không cần dữ kiện.
public sealed class PNStarweaverCastView : IPNActionView
{
    private readonly PNSkill skill; private readonly float windup, recovery; private readonly bool triangle;
    private PNVisualBody body; private PNStarweaverCastVisual visual; private PNStarweaverPresentation presentation;
    private bool lead = true, released, ended, hidden;
    public bool HasTail => false;
    public bool Lead { set { lead = value; } }
    public bool Ended => ended;
    public PNStarweaverCastVisual Visual => visual;

    public static IPNActionView Create(PNActionViewBuild build) => new PNStarweaverCastView(build);
    private PNStarweaverCastView(PNActionViewBuild build)
    {
        skill = build.Skill;
        var p = (JObject)build.Data.Action.Perform;
        windup = p["windup"].Value<float>(); recovery = p["recovery"].Value<float>();
        triangle = p["operationId"].Value<string>() == PNBlastGameplayContent.CreateBlast.ToString();
        try
        {
            body = skill.GetComponent<PNVisualBody>(); presentation = skill.GetComponent<PNStarweaverPresentation>();
            visual = PNStarweaverCastVisual.Create(skill.transform, presentation, body != null ? body.Actor : null, triangle, windup, build.Direction);
        }
        catch { Release(); throw; }
    }

    public void Advance(double age, double rate, bool seeking)
    {
        if (ended) return;
        float t = (float)age;
        if (hidden) { if (visual != null) visual.gameObject.SetActive(true); hidden = false; }
        if (lead) body?.PlayActionAt(triangle ? "cast_constellation" : "cast_weave", age, windup + recovery, seeking);
        visual?.SetTime(t);
        if (!released && t >= windup) { released = true; presentation?.TickWeapon(0); visual?.Align(); }
    }
    public void Fact(string key, double[] values, double age) { }
    public void Result(in PNActionViewResult result) { }
    public void Hide() { if (ended) return; hidden = true; if (visual != null) visual.gameObject.SetActive(false); }
    public void End(PNActionViewEnd kind) { if (ended) return; ended = true; Release(); }
    private void Release()
    {
        visual?.Dispose(); visual = null;
        body?.EndAction(); body = null; presentation = null;
    }
}
#endif
