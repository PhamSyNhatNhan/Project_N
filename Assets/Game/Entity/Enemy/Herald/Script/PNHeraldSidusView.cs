#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: The picture of one Herald Sidus cast (Docs/ObservedViewPlan.md PV6, group 2): the cast pose and the gathering light placed by the run's AGE, the pulse ring at the end of the windup (its own clock, started when the age
//     reaches `windup`; a joiner placed long after it does not draw it), and one burst at each ally that got the Buff, from `recipients` facts (up to 4 entity ids each) the Action presents when it decided. The Buff and the guard are the Action's.
// VI: Hình của một lần Herald Sidus: tư thế, ánh sáng gom theo tuổi, vòng xung lúc hết windup, và tia lên từng đồng minh nhận Buff (dữ kiện `recipients`).
public sealed class PNHeraldSidusView : IPNActionView
{
    public const string RecipientsFact = "recipients";
    public const int IdsPerFact = 4;
    private const float PulseSeconds = .45f;                                   // the ring's own life (PNHeraldPresentation); a pulse older than this is not drawn for a joiner
    private readonly PNSkill skill; private readonly float windup, release, recovery, radius;
    private PNHeraldPresentation presentation; private PNVisualBody body;
    private bool lead = true, pulsed, ended;
    public bool HasTail => false;
    public bool Lead { set { lead = value; } }
    public bool Ended => ended;
    public bool Pulsed => pulsed;

    public static IPNActionView Create(PNActionViewBuild build) => new PNHeraldSidusView(build);
    private PNHeraldSidusView(PNActionViewBuild build)
    {
        skill = build.Skill;
        var p = (JObject)build.Data.Action.Perform;
        windup = p["windup"].Value<float>(); release = p["release"].Value<float>(); recovery = p["recovery"].Value<float>(); radius = p["radius"].Value<float>();
        body = skill.GetComponent<PNVisualBody>(); presentation = skill.GetComponent<PNHeraldPresentation>();
    }

    public void Advance(double age, double rate, bool seeking)
    {
        if (ended) return;
        float t = (float)age;
        if (lead) body?.PlayActionAt("cast", age, windup + release + recovery, seeking);
        presentation?.Cast(t, windup, release);
        if (!pulsed && t >= windup) { pulsed = true; if (t - windup <= PulseSeconds) presentation?.Pulse(radius); }
    }
    public void Fact(string key, double[] values, double age)
    {
        if (ended || key != RecipientsFact || values == null) return;
        for (int i = 0; i < values.Length && i < IdsPerFact; i++)
            if (skill.TryFindEntity((long)values[i], out var target) && target != null) presentation?.Recipient(target.transform.position);
    }
    public void Result(in PNActionViewResult result) { }
    public void Hide() { }
    public void End(PNActionViewEnd kind)
    {
        if (ended) return; ended = true;
        presentation?.EndCast(); presentation = null;
        body?.EndAction(); body = null;
    }
}
#endif
