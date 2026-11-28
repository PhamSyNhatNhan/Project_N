#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;

// EN: The picture of Gura's Burst slam (Docs/GuraCharacter.md 4.4), after Water_Impact: water climbs over her whole body (it melts off after the landing), water kicks up where she takes off, thin strands wind after her through the leap and
//     straighten on the way down, and where she lands the water crown breaks out to the hit radius, then falls and is gone before the Action ends. View only: the leap and
//     the hit belong to the Action. Everything is drawn from the Action's age, so a late or replayed view shows the same frame.
// VI: Hinh cua cu nhay Burst: nuoc ban luc bat chan, dai nuoc xoan theo nguoi roi keo thang khi bo xuong, dap dat thi vuong mien nuoc no ra dung ban kinh danh roi tan het.
internal sealed class PNGuraBurstLeap : IDisposable
{
    // Takeoff water, the trail she drags, and how the pack's impact (which strikes at 1.18 s) is read from her landing.
    private const float CoatRise = .09f, TakeoffSeconds = .36f, TrailSeconds = .2f, ImpactFrom = 1.18f, ImpactRate = 1.25f, BodyHeight = .55f;
    private const int Strands = 3, Points = 36;

    private readonly Transform owner;
    private readonly PNGuraWaterFx water;
    private readonly PNGuraWaterParticles crown;
    private readonly float landAt, height, radius, duration;
    private readonly List<Vector3> samples = new(64), strand = new(Points);
    private readonly List<float> ages = new(64);
    private Vector3 takeoff, landing;
    private bool placed, landed;
    // The water over her whole body (the sheet of attack 3 and the Skill's rush) and its ring of light.
    private Material coatMaterial, ringMaterial;
    private PNGuraEchoRush.Silhouette coat, ring;
    private PNVisualActor coated;

    // `landAt`: when her step ends on the floor; `height`: the registered apex of that step; `radius`: the scaled hit radius.
    internal PNGuraBurstLeap(Transform owner, float landAt, float height, float radius, float duration)
    {
        this.owner = owner;
        this.landAt = Mathf.Max(.05f, landAt);
        this.height = height;
        this.radius = radius;
        this.duration = duration;
        water = new PNGuraWaterFx(owner, "Gura burst leap water");
        crown = PNGuraWaterParticles.Create(owner, "burst_land");
    }

    // The water climbs from her feet as she takes off, covers her through the leap and melts from the head down once she is down.
    private void Coat(float age, PNVisualActor actor)
    {
        if (actor == null || actor.Renderer == null) { HideCoat(); return; }
        if (coatMaterial == null)
        {
            coatMaterial = PNGuraEchoRush.CreateMaterial("Gura burst coat");
            ringMaterial = PNGuraEchoRush.CreateMaterial("Gura burst coat ring", true);
        }
        if (coatMaterial == null || ringMaterial == null) return;
        if (coated != actor)
        {
            coat?.Dispose();
            ring?.Dispose();
            coat = new PNGuraEchoRush.Silhouette(actor, coatMaterial, "Gura burst coat sheet");
            ring = new PNGuraEchoRush.Silhouette(actor, ringMaterial, "Gura burst coat ring", true);
            coated = actor;
        }
        float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / CoatRise));
        float melt = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(landAt + .06f, landAt + .3f, age));
        bool rising = rise < 1f && melt <= 0f;
        float dissolve = rising ? Mathf.Lerp(.995f, 0f, rise) : melt;
        coat.Draw(actor, age, dissolve, true, .96f, rising);
        ring.Draw(actor, age, dissolve, true, .9f * (1f - melt), rising, 1f, .03f);
    }

    private void HideCoat()
    {
        coat?.Hide();
        ring?.Hide();
    }

    internal void Draw(float age, bool seeking, PNVisualActor actor)
    {
        Vector3 origin = owner.position;
        float f = Mathf.Clamp01(age / landAt);
        float lift = height * 4f * f * (1f - f);
        if (!placed || seeking)
        {
            takeoff = origin - Vector3.up * lift;
            samples.Clear();
            ages.Clear();
            landed = false;
            placed = true;
        }
        if (!landed && age >= landAt)
        {
            landing = new Vector3(origin.x, takeoff.y, origin.z);
            landed = true;
        }
        if (age <= landAt && (ages.Count == 0 || age > ages[ages.Count - 1]))
        {
            samples.Add(origin + Vector3.up * BodyHeight);
            ages.Add(age);
        }
        while (ages.Count > 1 && ages[1] < age - TrailSeconds) { ages.RemoveAt(0); samples.RemoveAt(0); }

        Coat(age, actor);

        var camera = PNViewCamera.Current;
        Vector3 view = camera != null ? camera.transform.forward : Vector3.forward;
        Vector3 right = camera != null ? camera.transform.right : Vector3.right;
        Vector3 up = camera != null ? camera.transform.up : Vector3.up;
        Vector3 viewer = new Vector3(-view.x, 0f, -view.z);
        viewer = viewer.sqrMagnitude < 1e-6f ? Vector3.back : viewer.normalized;

        water.Begin();
        if (age < TakeoffSeconds) Takeoff(age, view, right, up);
        Trail(age, view);
        if (age < landAt) Leap(origin, age, view, viewer);
        if (landed) Landing(age - landAt, view, viewer, right, up);
        water.Draw();

        float since = age - landAt;
        if (landed && since >= 0f && age < duration)
            crown?.Sample(landing + Vector3.up * .03f, Vector3.zero, new Vector3(radius * .34f, radius * .27f, radius * .34f), ImpactFrom + since * ImpactRate, seeking);
        else crown?.Hide();
    }

    private void Takeoff(float age, Vector3 view, Vector3 right, Vector3 up)
    {
        float p = age / TakeoffSeconds, fade = 1f - p * p;
        Vector3 floor = takeoff + Vector3.up * .03f;
        float open = 1f - (1f - p) * (1f - p);
        water.Pool(floor, Mathf.Lerp(.3f, .75f, open), age, .7f * fade);
        water.Ripple(floor + Vector3.up * .01f, Mathf.Lerp(.25f, 1.5f, open), .09f, fade);
        water.Crown(floor, Mathf.Lerp(.32f, .7f, open), 1.05f * Mathf.Sin(Mathf.PI * Mathf.Lerp(.1f, 1f, p)), age, fade);
        water.Mist(floor, .75f, age, right, up);
        Jets(floor, .18f, .6f, p, 9, 1.4f, .055f, fade, view);
    }

    // The strands she drags: wide and winding on the way up, drawn in and straight on the way down.
    private void Trail(float age, Vector3 view)
    {
        if (samples.Count < 2) return;
        float from = Mathf.Max(ages[0], age - TrailSeconds), to = ages[ages.Count - 1];
        if (to - from < .01f) return;
        float fade = 1f - Mathf.Clamp01((age - landAt) / .1f);
        for (int k = 0; k < Strands; k++)
        {
            strand.Clear();
            for (int i = 0; i < Points; i++)
            {
                float u = i / (float)(Points - 1), at = Mathf.Lerp(from, to, u);
                Vector3 point = Along(at, out Vector3 tangent);
                Vector3 side = Vector3.Cross(view, tangent);
                side = side.sqrMagnitude < 1e-6f ? Vector3.right : side.normalized;
                Vector3 other = Vector3.Cross(tangent, side).normalized;
                float falling = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(landAt * .4f, landAt * .8f, at));
                float turn = k * Mathf.PI * 2f / Strands + at * Mathf.Lerp(30f, 9f, falling);
                float reach = Mathf.Lerp(.46f, .17f, falling) * (.4f + .6f * Mathf.Sin(Mathf.PI * Mathf.Lerp(.15f, .9f, u)));
                strand.Add(point + (side * Mathf.Cos(turn) + other * Mathf.Sin(turn)) * reach);
            }
            water.Strand(strand, k == 0 ? .11f : .07f, (k == 0 ? .95f : .8f) * fade, view);
        }
    }

    private Vector3 Along(float at, out Vector3 tangent)
    {
        int i = 1;
        while (i < ages.Count - 1 && ages[i] < at) i++;
        Vector3 a = samples[i - 1], b = samples[i];
        tangent = (b - a).sqrMagnitude < 1e-8f ? Vector3.up : (b - a).normalized;
        return Vector3.Lerp(a, b, Mathf.InverseLerp(ages[i - 1], ages[i], at));
    }

    private void Leap(Vector3 origin, float age, Vector3 view, Vector3 viewer)
    {
        float apex = landAt * .5f;
        // Going up: two ribbons turning about her.
        float rising = Mathf.Clamp01(age / .05f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(apex * .8f, apex * 1.4f, age)));
        for (int i = 0; i < 2; i++)
            water.Swirl(origin + Vector3.up * (.08f + .22f * i), .56f - .08f * i, .34f, .5f, age * 1250f + i * 180f, 230f, .11f, rising * .9f, viewer);
        // Coming down: the water drawn out above her into one blade.
        float falling = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(apex, apex * 1.45f, age));
        if (falling <= 0f || samples.Count < 2) return;
        Along(ages[ages.Count - 1], out Vector3 way);
        Vector3 head = origin + Vector3.up * BodyHeight + way * .35f;
        water.Streak(head - way * Mathf.Lerp(.6f, 3.2f, falling), head, .42f, .5f * falling, view);
        water.Streak(head - way * Mathf.Lerp(.4f, 2f, falling), head + way * .15f, .15f, .9f * falling, view);
    }

    private void Landing(float since, Vector3 view, Vector3 viewer, Vector3 right, Vector3 up)
    {
        float length = Mathf.Max(.2f, duration - landAt);
        float p = Mathf.Clamp01(since / length);
        float gone = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.62f, .96f, p));
        if (gone <= 0f) return;
        float open = 1f - Mathf.Pow(1f - Mathf.Clamp01(since / .16f), 3f);
        Vector3 floor = landing + Vector3.up * .03f;
        water.WetShade(floor - Vector3.up * .01f, Vector3.forward, radius * open, radius * open, .4f * gone);
        // The edge of the hit: a bright ring that reaches the radius and stays there while it fades.
        water.Ripple(floor + Vector3.up * .02f, radius * open, .16f, gone);
        water.BlastEdge(floor + Vector3.up * .015f, radius * open, gone);
        water.Ripple(floor + Vector3.up * .025f, radius * Mathf.Clamp01(since / .34f) * .7f, .09f, (1f - Mathf.Clamp01(since / .34f)) * gone);
        float wall = 1f - Mathf.Clamp01(since / .3f);
        water.Surge(floor, Mathf.Lerp(.5f, radius * .94f, open), .85f * Mathf.Sin(Mathf.PI * Mathf.Lerp(.15f, 1f, Mathf.Clamp01(since / .3f))), since, .85f * wall, viewer, 0f);
        water.Crown(floor, Mathf.Lerp(.4f, radius * .45f, open), 1.7f * Mathf.Sin(Mathf.PI * Mathf.Lerp(.12f, 1f, Mathf.Clamp01(since / .36f))), since, wall);
        water.Mist(floor, radius * .8f, since, right, up);
        Jets(floor, radius * .3f, radius * .92f, Mathf.Clamp01(since / .4f), 18, 2.1f, .075f, gone, view);
    }

    // Thin pointed jets thrown up and out of a ring; they thin out as they fall (no beads).
    private void Jets(Vector3 floor, float from, float to, float p, int count, float high, float width, float alpha, Vector3 view)
    {
        if (p <= 0f || p >= 1f) return;
        for (int i = 0; i < count; i++)
        {
            float seed = Mathf.Repeat(i * .618034f + .15f, 1f), other = Mathf.Repeat(i * .414214f + .28f, 1f);
            float turn = i * 2.39996f;
            Vector3 outward = new(Mathf.Sin(turn), 0f, Mathf.Cos(turn));
            Vector3 root = floor + outward * Mathf.Lerp(from, to, seed);
            float top = high * Mathf.Lerp(.45f, 1f, other);
            float rise = Mathf.Sin(Mathf.PI * Mathf.Lerp(.12f, 1f, p));
            Vector3 head = root + Vector3.up * (top * rise) + outward * (.35f * p);
            Vector3 tail = root + Vector3.up * (top * rise * Mathf.Lerp(.05f, .7f, p)) + outward * (.12f * p);
            water.Streak(tail, head, width * (1f - .5f * p), alpha * (1f - p * p), view);
        }
    }

    internal void Hide()
    {
        placed = false;
        HideCoat();
        water.Hide();
        crown?.Hide();
    }

    public void Dispose()
    {
        coat?.Dispose();
        ring?.Dispose();
        coat = ring = null;
        coated = null;
        foreach (var material in new[] { coatMaterial, ringMaterial })
            if (material != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(material); else UnityEngine.Object.DestroyImmediate(material); }
        coatMaterial = ringMaterial = null;
        water.Dispose();
        crown?.Dispose();
    }
}
#endif
