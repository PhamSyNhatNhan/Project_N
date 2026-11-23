#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: The pictures of the two Starchanter support runs (Docs/ObservedViewPlan.md PV6, group 3), placed by the run's AGE the same on every machine.
//     Heal: the vanish / arrive / channel body states, the teleport light, the healing zone and its pulses. The cast point is the context position (the landing). The Action keeps the teleport, the guard and the
//     healing, and presents one fact per heal pulse: `heal` [final(1/0), recipient ids (up to 3)] at the age it healed (also when nobody was healed, so the zone still pulses). A run that never presents a final
//     pulse (the guard broke) ends as an interrupted cast.
//     Boost: the gathering light and the pulse at the end of the windup by age; the `boost` fact [recipient ids (up to 4)] draws a spark on each ally that got the Buff.
// VI: Hình của hai skill hỗ trợ Starchanter theo tuổi của run. Heal: thân biến mất/xuất hiện/niệm, ánh dịch chuyển, vùng hồi và nhịp; dữ kiện `heal` [cuối, id người nhận]. Boost: ánh gom và xung theo tuổi; dữ kiện `boost` [id].
public sealed class PNStarchanterHealView : IPNActionView
{
    public const string HealFact = "heal";
    public const int MaxRecipients = 3;
    private readonly PNSkill skill; private readonly float outSeconds, inSeconds, duration, interval, radius; private readonly Vector3 landing;
    private PNStarchanterPresentation presentation; private PNVisualBody body;
    private bool lead = true, arrived, channelDone, finalSeen, ended;
    public bool HasTail => false;
    public bool Lead { set { lead = value; } }
    public bool Ended => ended;
    public bool Arrived => arrived;
    public bool FinalSeen => finalSeen;

    public static IPNActionView Create(PNActionViewBuild build) => new PNStarchanterHealView(build);
    private PNStarchanterHealView(PNActionViewBuild build)
    {
        skill = build.Skill;
        var p = (JObject)build.Data.Action.Perform;
        outSeconds = p["teleportOut"].Value<float>(); inSeconds = p["teleportIn"].Value<float>(); duration = p["duration"].Value<float>(); interval = p["interval"].Value<float>(); radius = p["radius"].Value<float>();
        landing = build.Position ?? skill.transform.position;
        body = skill.GetComponent<PNVisualBody>(); presentation = skill.GetComponent<PNStarchanterPresentation>();
    }

    public void Advance(double age, double rate, bool seeking)
    {
        if (ended) return;
        float t = (float)age;
        presentation?.Heal(t, outSeconds, inSeconds, duration, interval, radius);
        if (!arrived && t >= outSeconds) { arrived = true; presentation?.TeleportArrived(landing); }
        if (!lead || channelDone) return;
        float channelStart = outSeconds + inSeconds;
        if (t < outSeconds) body?.PlayActionAt("heal_teleport_out", age, outSeconds, seeking);
        else if (t < channelStart) body?.PlayActionAt("heal_teleport_in", t - outSeconds, inSeconds, seeking);
        else if (t < channelStart + duration) body?.PlayActionAt("heal_channel", t - channelStart, duration + .05f, seeking);
        else { channelDone = true; body?.EndAction(); }                              // the channel is over: the body goes back to its own clock for the recovery
    }
    public void Fact(string key, double[] values, double age)
    {
        if (ended || key != HealFact || values == null || values.Length < 1) return;
        bool final = values[0] > .5; if (final) finalSeen = true;
        presentation?.PulseReady();
        for (int i = 1; i < values.Length && i <= MaxRecipients; i++)
            if (skill.TryFindEntity((long)values[i], out var target) && target != null) presentation?.HealRecipient(target.transform.position, final);
    }
    public void Result(in PNActionViewResult result) { }
    public void Hide() { }
    public void End(PNActionViewEnd kind)
    {
        if (ended) return; ended = true;
        presentation?.EndCast(finalSeen); presentation = null;
        body?.EndAction(); body = null;
    }
}

public sealed class PNStarchanterBoostView : IPNActionView
{
    public const string BoostFact = "boost";
    public const int MaxRecipients = 4;
    private readonly PNSkill skill; private readonly float windup, recovery, radius;
    private PNStarchanterPresentation presentation; private PNVisualBody body;
    private bool lead = true, pulsed, ended;
    public bool HasTail => false;
    public bool Lead { set { lead = value; } }
    public bool Ended => ended;
    public bool Pulsed => pulsed;

    public static IPNActionView Create(PNActionViewBuild build) => new PNStarchanterBoostView(build);
    private PNStarchanterBoostView(PNActionViewBuild build)
    {
        skill = build.Skill;
        var p = (JObject)build.Data.Action.Perform;
        windup = p["windup"].Value<float>(); recovery = p["recovery"].Value<float>(); radius = p["radius"].Value<float>();
        body = skill.GetComponent<PNVisualBody>(); presentation = skill.GetComponent<PNStarchanterPresentation>();
    }
    public void Advance(double age, double rate, bool seeking)
    {
        if (ended) return;
        float t = (float)age;
        if (lead) body?.PlayActionAt("heal_channel", age, windup + recovery, seeking);
        presentation?.Boost(t, windup, radius);
        if (!pulsed && t >= windup) { pulsed = true; presentation?.BoostPulse(); }
    }
    public void Fact(string key, double[] values, double age)
    {
        if (ended || key != BoostFact || values == null) return;
        for (int i = 0; i < values.Length && i < MaxRecipients; i++)
            if (skill.TryFindEntity((long)values[i], out var target) && target != null) presentation?.BoostRecipient(target.transform.position);
    }
    public void Result(in PNActionViewResult result) { }
    public void Hide() { }
    public void End(PNActionViewEnd kind)
    {
        if (ended) return; ended = true;
        presentation?.EndCast(pulsed); presentation = null;
        body?.EndAction(); body = null;
    }
}
#endif
