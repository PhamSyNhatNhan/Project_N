using EveEngine;
using UnityEngine;

// Player-test blast (IL2, Docs/InstanceLifecyclePlan.md, `--pn-object-sync-multistrike`): strikes at FirstAt and SecondAt while it runs, finishes at EndAt, strikes once more as its aftershock in OnEnd, then plays EndingSeconds of Ending.
// The strike is a cylinder of Radius at the blast. It is registered only by a CLONE-ONLY build (the build script adds PNStrikesPlayModule to the clone's content modules), never by the game.
// Counters are per machine: every machine that runs the blast counts its own strikes.
public sealed class PNStrikesPlayBlast : PNBlastInstance
{
    public static readonly EveId Id = EveId.Parse("project_n:strikes_play_test");
    public const float FirstAt = .3f, SecondAt = .6f, EndAt = .9f, EndingSeconds = .4f, Radius = 2.5f;
    public static int Starts, Strikes, Ends, EndingSteps;
    public static readonly System.Text.StringBuilder Timing = new System.Text.StringBuilder();
    public static void ResetCounters() { Starts = Strikes = Ends = EndingSteps = 0; Timing.Clear(); }
    private float age; private int made; private PNHitCapture cap; private PNPreparedDamage prepared;
    protected override void OnStart(PNBlastCreateRequest request)
    {
        Starts++; age = 0; made = 0;
        if (request.IsReplica) { cap = null; prepared = null; }
        else { cap = CreateCapture(new[] { EveId.Parse("base:magic") }); prepared = cap.Prepare(5d, false, null); }
    }
    protected override void OnPerform(float gameDelta)
    {
        float before = age; age += gameDelta;
        if (before < FirstAt && age >= FirstAt) Hit();
        if (before < SecondAt && age >= SecondAt) Hit();
        if (age >= EndAt) Finish(PNBlastEndReason.Expired);
    }
    // A copy told "ended" by the record before its own clock reached a strike makes the missing ones now (the host already made them), then the aftershock.
    protected override void OnEnd(PNBlastEndReason reason) { Ends++; if (reason == PNBlastEndReason.Expired) { while (made < 2) Hit(); Hit(); } }
    public override float EndingDuration => EndingSeconds;
    protected override void OnEnding(float delta) { EndingSteps++; }
    protected override void Cleanup() { }
    public override void OnPoolRelease() { cap = null; prepared = null; base.OnPoolRelease(); }
    private void Hit()
    {
        made++; Strikes++; Timing.Append("s").Append(made - 1).Append("@").Append(age.ToString("F2")).Append(IsReplica ? "c " : "r ").Append(Time.realtimeSinceStartup.ToString("F2")).Append(" | ");
        var at = transform.position;
        Strike(new PNBlastStrike(at, Radius, 1.5f, b => new Vector2(b.center.x - at.x, b.center.z - at.z).magnitude <= Radius, cap, prepared));
    }
}

public sealed class PNStrikesPlayModule : EveIContentModule
{
    public void Register(EveRegistryHub hub)
        => hub.Instances().Register(new PNBlastDefinition(PNStrikesPlayBlast.Id, typeof(PNStrikesPlayBlast), new Newtonsoft.Json.Linq.JObject(), (token, registry) => { }, token => { }));
}
