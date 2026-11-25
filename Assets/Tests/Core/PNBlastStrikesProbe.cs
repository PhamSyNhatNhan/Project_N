#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// A blast "a mod" could write (IL2, Docs/InstanceLifecyclePlan.md 3.5): it strikes twice while it runs (FirstAt, SecondAt), finishes at EndAt, strikes once more as its aftershock in OnEnd, and plays EndingSeconds of Ending.
// It logs its hooks so the checks can read the order. Radius 1.5 m, height 1.5 m, 10 damage per strike.
public sealed class PNBlastStrikesProbe : PNBlastInstance
{
    public static readonly EveId Id = EveId.Parse("blast_test:strikes");
    public static readonly List<string> Log = new List<string>();
    public const float FirstAt = .2f, SecondAt = .5f, EndAt = .8f, EndingSeconds = .3f;
    private float age; private PNHitCapture cap; private PNPreparedDamage prepared;
    public int EndingSteps { get; private set; }
    public float Age => age;
    protected override void OnStart(PNBlastCreateRequest request)
    {
        Log.Add("start"); age = 0; EndingSteps = 0;
        if (request.IsReplica) { cap = null; prepared = null; }
        else { cap = CreateCapture(new[] { EveId.Parse("base:magic") }); prepared = cap.Prepare(10d, false, null); }
    }
    protected override void OnPerform(float gameDelta)
    {
        float before = age; age += gameDelta;
        if (before < FirstAt && age >= FirstAt) Hit();
        if (before < SecondAt && age >= SecondAt) Hit();
        if (age >= EndAt) Finish(PNBlastEndReason.Expired);
    }
    protected override void OnEnd(PNBlastEndReason reason) { Log.Add("end:" + reason); if (reason == PNBlastEndReason.Expired) Hit(); }
    public override float EndingDuration => EndingSeconds;
    protected override void OnEnding(float delta) { EndingSteps++; }
    protected override void OnCancel(PNBlastEndReason reason) { Log.Add("cancel:" + reason); Hit(); }
    protected override void Cleanup() { Log.Add("cleanup"); }
    public override void OnPoolRelease() { cap = null; prepared = null; base.OnPoolRelease(); }
    private void Hit()
    {
        var at = transform.position;
        Strike(new PNBlastStrike(at, 1.5f, 1.5f, b => new Vector2(b.center.x - at.x, b.center.z - at.z).magnitude <= 1.5f, cap, prepared));
    }
}

public sealed class PNBlastStrikesProbeModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)
        => hub.Instances().Register(new PNBlastDefinition(PNBlastStrikesProbe.Id, typeof(PNBlastStrikesProbe), new Newtonsoft.Json.Linq.JObject(), (token, registry) => { }, token => { }));
}
#endif
