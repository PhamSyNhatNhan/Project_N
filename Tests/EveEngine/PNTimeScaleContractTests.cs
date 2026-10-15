using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using static EveSessionSmokeTests;

// Clock/registry/input contracts only. Native Unity tests establish actual modifiers.
public static class PNTimeScaleContractTests
{
    public static void Run()
    {
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, Array.Empty<EveContentDocument>());
        var timed = hub.Buffs().Get(EveId.Parse("base:timescale_reduction"));
        var area = hub.Buffs().Get(EveId.Parse("base:timescale_reduction_area"));
        var areaDefinition = hub.Get<PNAreaDefinition>().GetEffective(EveId.Parse("base:timescale_area"));
        var areaPayload = (JObject)areaDefinition.Payload;
        var visual = new PNTimeScaleAreaVisualSettings(areaPayload["visual"]);
        Check(visual.Maximum == 20 && visual.Rate == 6 && visual.Lifetime == 3 && visual.Size == .025f && visual.Speed == .05f && visual.Aspect == 3, "Registered slow streak tuning is authoritative");
        Check(visual.TintStrength == .58f && visual.Chromatic == .12f && visual.Vignette == .15f && visual.Smoothness == .5f && visual.Saturation == -20, "Registered stronger tint and focused regional edge tuning");
        Check(visual.EdgeDistortion == .009f && visual.RimWidth == .1f && visual.RimIntensity == .12f && visual.RimPulse == .12f, "Registered visible boundary and slow refraction tuning");
        Check(visual.ContactWidth == .015f && visual.ContactIntensity == 0 && visual.BirthDuration == .35f && visual.BirthIntensity == 0 && visual.TwistStrength == .018f && visual.TwistSpeed == .45f, "Registered expanding volume and slow space twist without contact line or creation pulse");
        var changed = (JObject)areaPayload["visual"].DeepClone(); changed["maximumParticles"] = 0;
        bool visualRejected = false; try { new PNTimeScaleAreaVisualSettings(changed); } catch (FormatException) { visualRejected = true; }
        Check(visualRejected, "Invalid particle capacity rejected before activation");
        changed = (JObject)areaPayload["visual"].DeepClone(); changed["unknown"] = true;
        visualRejected = false; try { new PNTimeScaleAreaVisualSettings(changed); } catch (FormatException) { visualRejected = true; }
        Check(visualRejected, "Unknown area visual tuning rejected");
        Check(new PNTimeScaleAreaVisualSettings(null).Maximum == visual.Maximum, "Older derived area payload shares same default visual schema");
        Check(timed.MultipleInstances && area.MultipleInstances && timed.CanDispel && !area.CanDispel, "Timescale independent instance/dispel contracts");
        Check(timed.Tags.Contains(hub.BuffTags().ResolveIdentity(EveId.Parse("base:timescale"))) || timed.Tags.Contains(EveId.Parse("base:timescale")), "Timescale vocabulary registered");
        var input = JObject.Parse("{'reduction':3,'minimumReduction':2,'duration':1}");
        timed.ValidateInput(input); area.ValidateInput(JObject.Parse("{'reduction':3,'minimumReduction':2}"));
        foreach (string json in new[] { "{}", "{'reduction':1,'minimumReduction':0}", "{'reduction':-1,'minimumReduction':0,'duration':1}", "{'reduction':1,'minimumReduction':-1,'duration':1}", "{'reduction':1,'minimumReduction':0,'duration':0}", "{'reduction':1,'minimumReduction':0,'duration':1,'unknown':true}" })
        { bool rejected = false; try { timed.ValidateInput(JObject.Parse(json)); } catch (FormatException) { rejected = true; } catch (ArgumentException) { rejected = true; } Check(rejected, "Timescale invalid input rejected " + json); }
        var nonfinite = (JObject)input.DeepClone(); nonfinite["reduction"] = double.NaN;
        bool nan = false; try { timed.ValidateInput(nonfinite); } catch (FormatException) { nan = true; } Check(nan, "Timescale nonfinite input rejected");
        var session = Guid.NewGuid(); var source = new PNBuffSource(session, new EveEntityId(1));
        using var buffs = new PNBuffCollection(hub, session);
        var request = new PNBuffApplyRequest(EveId.Parse("base:timescale_reduction"), source, input); input["duration"] = 100;
        var first = buffs.Apply(request); var second = buffs.Apply(request);
        Check(first.Status == PNBuffStatus.Applied && second.Status == PNBuffStatus.Applied && !first.Handle.Equals(second.Handle), "Same-source timescale Apply remains independent");
        buffs.Advance(.6, 0, 0); Check(Math.Abs(buffs.Get(first.Handle).Remaining.Value - .4) < 1e-8, "Timescale Game expiry at zero entity scale and cloned request");
        buffs.Remove(first.Handle); Check(!first.Handle.IsAlive && second.Handle.IsAlive, "Removing exact timescale instance preserves peer");
        buffs.Advance(.4, 0, 0); Check(!second.Handle.IsAlive, "Timescale Game exact expiry");
        var permanent = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:timescale_reduction_area"), source, JObject.Parse("{'reduction':1,'minimumReduction':0}")));
        buffs.Advance(100, 0, 0); Check(permanent.Handle.IsAlive && !buffs.Get(permanent.Handle).Remaining.HasValue, "Area Buff has no implicit duration");
        Check(buffs.Remove(permanent.Handle).Status == PNBuffStatus.Rejected, "Area Buff rejects ordinary dispel");
        buffs.Remove(permanent.Handle, PNBuffEndReason.Bypass, true); Check(!permanent.Handle.IsAlive, "Area Buff bypass uses normal ending");
    }
}
