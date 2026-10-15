using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

public static class PNBuffElementPresenterContent
{
    public static readonly EveId Id = EveId.Parse("project_n:buff_element_presenter");
    public static void Register(EveRegistryHub hub)
    {
        hub.BuffPresenters().Register(new PNBuffPresenterDefinition(Id, Id,
            context => new PNBuffElementPresenter(context), data =>
            {
                if (data == null) return;
                var obj = PNBuffContentBatch.Object(data); PNBuffContentBatch.Fields(obj, "stacks", "maximum");
                if (PNBuffContentBatch.Integer(obj, "stacks") < 0 || PNBuffContentBatch.Integer(obj, "maximum") < 1) throw new FormatException("Invalid visual density.");
            }, groupByProfile: true));
        hub.BuffPresenters().RegisterBase(EveId.Parse("base:buff_element_presenter"), Id, "project_n",
            compatible: (original, replacement) => original.PayloadVersion == replacement.PayloadVersion && original.GroupByProfile == replacement.GroupByProfile);
    }
}

public sealed class PNBuffElementPresenter : PNBuffPresenter
{
    private readonly PNBuffPresenterContext context;
#if UNITY_5_3_OR_NEWER
    private readonly Dictionary<long, PNBuffElementVisualLease> leases = new Dictionary<long, PNBuffElementVisualLease>();
#endif
    private IReadOnlyList<PNBuffWireState> states = Array.Empty<PNBuffWireState>();
    public PNBuffElementPresenter(PNBuffPresenterContext context) { this.context = context; }
    public override void Update(IReadOnlyList<PNBuffWireState> next, double now)
    {
        states = next;
#if UNITY_5_3_OR_NEWER
        var data = context.Profile.Payload as JObject;
        if (data?["visual"] == null || context.Target == null) return;
        foreach (var id in leases.Keys.Where(x => !next.Any(y => y.InstanceId == x)).ToArray()) { var lease = leases[id]; lease.Dispose(); context.Resources.Forget(lease); leases.Remove(id); }
        foreach (var state in next)
        {
            if (!leases.TryGetValue(state.InstanceId, out var lease))
            { lease = context.Resources.Own(PNBuffElementVisualLease.Create(context.Target, context.Profile.Id, (JObject)data["visual"], state.Clock)); leases.Add(state.InstanceId, lease); }
            if (state.DynamicData is JObject values) lease.SetStacks(PNBuffContentBatch.Integer(values, "stacks"), PNBuffContentBatch.Integer(values, "maximum"));
        }
        Advance(now);
#endif
    }
    public override void Advance(double now)
    {
#if UNITY_5_3_OR_NEWER
        double time = states.Count == 0 ? 0 : states.Max(x => x.VisualTime(now));
        foreach (var lease in leases.Values) lease.SetVisualTime(time);
#endif
    }
    public override void Cue(PNBuffCue cue)
    {
#if UNITY_5_3_OR_NEWER
        if (cue.Name == "tick" && leases.TryGetValue(cue.InstanceId, out var lease)) lease.Pulse();
#endif
    }
    public override void Dispose()
    {
#if UNITY_5_3_OR_NEWER
        foreach (var lease in leases.Values) lease.Dispose(); leases.Clear();
#endif
    }
}
