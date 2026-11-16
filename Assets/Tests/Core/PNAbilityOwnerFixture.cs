#if UNITY_EDITOR
using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// Native fixture clock, not a production fallback. Sequential host/client sessions
// deliberately share one fake admitted epoch and capture stamp.
public static class PNAbilityOwnerFixture
{
    public static readonly Guid Epoch = Guid.Parse("8b18bd640ea44170a759a75ec312e3d9");
    public static bool Ready = true;
    public static double Now = 100;
    private sealed class Timeline : IPNNetworkTimeline, IPNTimeRecoveryBudget
    { public PNTimeSample Sample() => new PNTimeSample(Epoch,Ready,Now,Now,Now-.5,0); public double RemainingRecoverySeconds => 15; }
    public static void Attach() { Ready = true; Now = 100; PNTimeService.Attach(EveRegistrySession.Context, new Timeline()); }
    public static JObject Image(PNSkillOwnerView view, JObject old)
    {
        var image = (JObject)view.Capture();
        if (old["a"] is JArray rows && rows.Count > 0) image["a"] = rows;
        image["b"] = old["b"]; image["k"] = 0;
        image["r"] = new JObject { ["ack"] = 0, ["p"] = 0, ["rej"] = new JArray() };
        return image;
    }
}
#endif
