#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// A projectile "a mod" could write (IL3, Docs/InstanceLifecyclePlan.md 3.1): a directional projectile that logs its hooks and plays EndingSeconds of Ending after a real finish.
// Definitions: test:ending (lives 5 s, no wall), test:ending_short (lives .2 s). The checks read Log for the order of the hooks.
public sealed class PNProjectileLifecycleProbe : PNDirectionalProjectile
{
    public static readonly List<string> Log = new List<string>();
    public static PNProjectileLifecycleProbe Last;
    public static float EndingSeconds = .3f;
    public int EndingSteps; public float EndingTotal; public Vector3 PositionAtEnd; public int MovesInEnding;
    public static void Reset() { Log.Clear(); Last = null; EndingSeconds = .3f; }
    protected override void OnStart(PNProjectileSpawn spawn) { base.OnStart(spawn); Last = this; Log.Add("start"); EndingSteps = 0; EndingTotal = 0; MovesInEnding = 0; }
    protected override void OnEnd(PNProjectileEndReason reason) { Log.Add("end:" + reason); PositionAtEnd = Position; base.OnEnd(reason); }
    public override float EndingDuration => EndingSeconds;
    protected override void OnEnding(float delta) { EndingSteps++; EndingTotal += delta; if ((Position - PositionAtEnd).sqrMagnitude > 1e-8f) MovesInEnding++; }
    protected override void OnCancel(PNProjectileEndReason reason) { Log.Add("cancel:" + reason); }
    protected override void Cleanup() { Log.Add("cleanup"); base.Cleanup(); }
}

public sealed class PNProjectileLifecycleProbeModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)
    {
        var registry = hub.Instances();
        var data = JObject.Parse("{'speed':10,'acceleration':0,'speedMin':0,'speedMax':200,'life':{'mode':'Time','value':5},'volume':{'size':[0.4,0.4,0.4]},'environmentMask':[],'hitLimit':1}");
        var probe = new PNProjectileDefinition(EveId.Parse("test:ending"), typeof(PNProjectileLifecycleProbe), data, PNDirectionalSettings.ValidateDefinition, PNDirectionalSettings.ValidateInput);
        registry.Register(probe);
        var shortData = (JObject)data.DeepClone(); shortData["life"] = JObject.Parse("{'mode':'Time','value':0.2}");
        registry.Register(probe.Derive(EveId.Parse("test:ending_short"), shortData));
    }
}
#endif
