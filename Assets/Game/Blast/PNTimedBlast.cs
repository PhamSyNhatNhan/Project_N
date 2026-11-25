#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: The common behaviour of a blast of this game (Docs/StrikeBlastPlan.md section 5): it appears at a point, waits `delay` seconds of ITS OWN clock (a time effect that slows
//     or stops the blast moves the telegraph and the detonation together), detonates exactly once, holds `tail` seconds for its visual, and ends.
//     At creation it reads the creator's combat snapshot once: `blast_scale` scales `radius` and `height`, `blast_damage` is added to the damage tags, the damage
//     is prepared once (a hit rolls its own critical). Hits: every living target of the creator's TargetLayers, in the blast's world, whose footprint meets the shape.
//     Subclasses choose the shape (`Hits`) and draw (`OnBegin` / `OnPhase` / `OnDetonated` / `OnEnded`).
// VI: Hanh vi chung cua blast: xuat hien tai mot diem, cho `delay` giay theo dong ho RIENG (lam cham/dung blast thi telegraph va no di cung nhau), no dung mot lan,
//     giu `tail` giay cho hinh anh roi ket thuc. `blast_scale` nhan radius/height, `blast_damage` them vao tag sat thuong; sat thuong chuan bi mot lan.
public abstract class PNTimedBlast : PNBlastInstance
{
    public static readonly string[] CommonFields = PNBlastSchema.Common;
    public static void ValidateCommon(JObject o, EveRegistryHub hub = null) => PNBlastSchema.ValidateCommon(o, hub);

    private PNHitCapture capture; private PNPreparedDamage prepared;
    private float age, delay, tail, tailAge; private bool detonated;
    public Vector3 Center { get; private set; }
    public float Radius { get; private set; }          // after `blast_scale`
    public float Height { get; private set; }          // half extent above and below the centre, after `blast_scale`
    public float Delay => delay;
    public float Age => age;
    public bool Detonated => detonated;
    public PNHitCapture Capture => capture;

    protected abstract bool Hits(Bounds footprint);
    protected virtual void ReadShape(JObject input) { }
    protected virtual void OnBegin(JObject input) { }
    protected virtual void OnPhase(float time, float delay) { }
    protected virtual void OnDetonated() { }
    protected virtual void OnEnded() { }

    protected sealed override void OnStart(PNBlastCreateRequest context)
    {
        var input = (JObject)context.Input;
        ValidateCommon(input); ReadShape(input);
        if (context.IsReplica)
        {
            // A copy on a machine without authority: the same shape and clock, no combat snapshot, nothing to hit (plan O5).
            capture = null; prepared = null;
            Center = context.Position; Radius = PNHitCapture.ScaleLength(input["radius"].Value<float>(), ScaleFactor); Height = PNHitCapture.ScaleLength(input["height"].Value<float>(), ScaleFactor);
        }
        else
        {
            var owner = context.Owner ?? throw new ArgumentException("A blast needs its owner's stats.");
            capture = CreateCapture(PNSkillEffect.Ids(input["tags"]));
            double amount = 0d; foreach (var component in PNSkillValues.Parse(input["value"]).Components) amount += component.Evaluate(owner);
            prepared = capture.Prepare(amount, input["canCrit"].Value<bool>(), null);
            Center = context.Position; Radius = capture.Scale(input["radius"].Value<float>()); Height = capture.Scale(input["height"].Value<float>());
        }
        baseRadius = input["radius"].Value<float>(); baseHeight = input["height"].Value<float>();
        delay = input["delay"].Value<float>(); tail = input["tail"].Value<float>(); age = 0; tailAge = 0; detonated = false;
        OnBegin(input);
    }
    // Waits `delay` seconds of its own clock, strikes once, then ends: the `tail` is its Ending (the visual holds, nothing hurts).
    protected sealed override void OnPerform(float gameDelta)
    {
        if (detonated) return;
        age += gameDelta; OnPhase(Mathf.Min(age, delay), delay);
        if (age < delay) return;
        Detonate(); Finish(PNBlastEndReason.Expired);
    }
    // A copy that is told the blast ended before its own clock reached the strike (the trip made it late) strikes now, so its player is not spared and it draws the detonation.
    // A blast ended before it struck (its owner lost, a guess nobody took over) never strikes and has no tail.
    protected sealed override void OnEnd(PNBlastEndReason reason)
    {
        if (!detonated && IsReplica && reason == PNBlastEndReason.Expired) Detonate();
        tailAge = 0;
    }
    public sealed override float EndingDuration => detonated ? tail : 0f;
    protected sealed override void OnEnding(float delta) { tailAge += delta; OnPhase(delay + tailAge, delay); }
    public override float ProgressSeconds => detonated ? delay + tailAge : age;
    private float baseRadius, baseHeight;
    protected virtual void OnShapeChanged() { }
    protected override void OnScaleCorrected(double scale)
    {
        if (!IsReplica || baseRadius <= 0f) return;
        Radius = PNHitCapture.ScaleLength(baseRadius, scale); Height = PNHitCapture.ScaleLength(baseHeight, scale); OnShapeChanged();
    }
    // The one strike of this blast: the engine's `Strike` door does the rest by the role of the machine (authority hurts and opens claims, a copy claims for its own player, a guess only draws).
    private void Detonate()
    {
        detonated = true;
        Strike(new PNBlastStrike(Center, Radius, Height, Hits, capture, prepared));
        OnDetonated();
    }
    protected override void Cleanup() { OnEnded(); }
    public override void OnPoolRelease() { capture = null; prepared = null; base.OnPoolRelease(); }
}

// A vertical cylinder: planar disc of `radius`, half height `height` (both after `blast_scale`).
public class PNCylinderBlast : PNTimedBlast
{
    public static void Validate(JToken token, EveRegistryHub hub = null)
    {
        var o = PNBuffContentBatch.Object(token); PNBuffContentBatch.Fields(o, CommonFields); ValidateCommon(o, hub);
    }
    protected override bool Hits(Bounds b) => PNBlastGeometry.CircleIntersectsBox(Center.x, Center.z, Radius, b.min.x, b.min.z, b.max.x, b.max.z);
}

// A regular polygon prism: `sides` (3 = the Starweaver triangle) of circumradius `radius`, half height `height`, turned by `rotation` degrees.
public class PNPolygonBlast : PNTimedBlast
{
    protected int Sides { get; private set; }
    protected float Rotation { get; private set; }
    public static void Validate(JToken token, EveRegistryHub hub = null) => PNBlastSchema.ValidatePolygon(token, hub);
    protected override void ReadShape(JObject input) { Sides = input["sides"].Value<int>(); Rotation = input["rotation"].Value<float>(); }
    protected override bool Hits(Bounds b) => PNBlastGeometry.PolygonIntersectsBox(Center.x, Center.z, Radius, Sides, Rotation, b.min.x, b.min.z, b.max.x, b.max.z);
}
#else
using EveEngine;
using Newtonsoft.Json.Linq;
// Standalone validates registered schemas; the Unity-only blast runtime executes in native checks.
public class PNCylinderBlast
{
    public static void Validate(JToken token, EveRegistryHub hub = null) { var o = PNBuffContentBatch.Object(token); PNBuffContentBatch.Fields(o, PNBlastSchema.Common); PNBlastSchema.ValidateCommon(o, hub); }
}
public class PNPolygonBlast
{
    public static void Validate(JToken token, EveRegistryHub hub = null) { PNBlastSchema.ValidatePolygon(token, hub); }
}
#endif
