#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// All layers are placed from the supplied age. The shapes can be rebuilt at any point in a run.
internal sealed class PNGuraWaterFx : IDisposable
{
    private static readonly Color Aqua = new Color(.055f, .65f, .94f);
    private static readonly Color Foam = new Color(.54f, .94f, 1f);
    private static readonly Color DeepWater = new Color(.025f, .30f, .68f);
    private static readonly Color FlatCrest = new Color(.5f, 1f, 1f);
    private readonly GameObject node;
    private readonly Mesh mesh;
    private bool disposed;
    private readonly List<Vector3> points = new List<Vector3>(256);
    private readonly List<Color> colors = new List<Color>(256);
    private readonly List<Vector2> uv = new List<Vector2>(256);
    private readonly List<int> triangles = new List<int>(768);

    public PNGuraWaterFx(Transform owner, string name = "Gura Water")
    {
        node = new GameObject(name) { layer = owner.gameObject.layer };
        node.transform.SetParent(owner, false);
        node.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        mesh = new Mesh { name = "Gura water geometry" };
        mesh.MarkDynamic();
        node.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = node.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = Resources.Load<Material>("PNVfx/GuraWater");
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        node.SetActive(false);
    }

    public void Begin()
    {
        points.Clear();
        colors.Clear();
        uv.Clear();
        triangles.Clear();
    }

    public void Draw()
    {
        if (mesh == null) return;
        mesh.Clear();
        if (points.Count > 0)
        {
            mesh.SetVertices(points);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
        }
        node.SetActive(points.Count > 0);
    }

    public void Hide() { if (node != null) node.SetActive(false); }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Hide();
        if (Application.isPlaying) { UnityEngine.Object.Destroy(mesh); UnityEngine.Object.Destroy(node); }
        else { UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(node); }
    }

    // The edge of a hit sector: a thin flat-colour crest exactly at the hit radius, from the start of the swing to `swept` (0..1) of the hit angle.
    public void SectorEdge(Vector3 centre, Vector3 forward, float radius, float angle, float side, float swept, Func<float, float, float> lift, float fade)
    {
        Color crest = FlatCrest.linear;
        if (swept <= 0f || fade <= 0f || radius <= 0f) return;
        const int sections = 48;
        int start = points.Count;
        for (int i = 0; i <= sections; i++)
        {
            float u = i / (float)sections;
            float swing = u * Mathf.Clamp01(swept);
            Vector3 outward = Quaternion.AngleAxis(side * Mathf.Lerp(-angle * .5f, angle * .5f, swing), Vector3.up) * forward;
            // Thin at both ends of the sector, so the ends read as the tips of one stroke.
            float body = Mathf.Clamp01(Mathf.Min(swing, 1f - swing) * 9f);
            float width = Mathf.Lerp(.006f, .026f, body) * radius;
            float alpha = fade * Mathf.Lerp(.5f, 1f, body);
            Vector3 at = centre;
            // v = 2 asks the shader for the flat vertex colour, as the flipbook water beside it is drawn.
            Vertex(at + outward * (radius - width) + Vector3.up * lift(swing, radius - width), new Vector2(u, 2f), WithAlpha(crest, alpha));
            Vertex(at + outward * radius + Vector3.up * lift(swing, radius), new Vector2(u, 2f), WithAlpha(crest, alpha));
            if (i != 0) Strip(start + i * 2);
        }
    }

    private void Join(int a, int b, int c, int d)
    {
        triangles.Add(a); triangles.Add(b); triangles.Add(c);
        triangles.Add(c); triangles.Add(b); triangles.Add(d);
    }


    public void Flight(in PNGuraAttackWeapon.Pose blade, float age) => TridentStream(blade, age, 1.20f, 1f);

    // One directional wake. The shoulders of the stream follow the prongs, and the back ends in a narrow wake.
    public void TridentStream(in PNGuraAttackWeapon.Pose blade, float age, float length, float opacity)
    {
        if (opacity <= .001f) return;
        const int segments = 28;
        const int sides = 8;
        Vector3 tip = blade.At(1f);
        Vector3 normal = Vector3.Cross(blade.Axis, blade.Across).normalized;
        int start = points.Count;
        for (int i = 0; i <= segments; i++)
        {
            float u = i / (float)segments;
            float envelope = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .14f, u)) * Mathf.Pow(1f - u, 1.1f);
            Vector3 core = tip - blade.Axis * (u * length);
            core += blade.Across * (.027f * Mathf.Sin(u * 7f - age * 26f) * u * envelope);
            for (int j = 0; j <= sides; j++)
            {
                float cross = j / (float)sides;
                float turn = cross * Mathf.PI * 2f;
                Vector3 radial = blade.Across * Mathf.Cos(turn) + normal * Mathf.Sin(turn) * .48f;
                float flow = .85f + .15f * Mathf.Sin(u * 36f - age * 25f + turn);
                Vector3 at = core + radial * (.50f * blade.Width * envelope * flow);
                Color color = Color.Lerp(Aqua, Foam, Mathf.Pow(Mathf.Abs(Mathf.Cos(turn)), 3f));
                Vertex(at, new Vector2(u, cross), WithAlpha(color, opacity * envelope * flow * .52f));
                if (i > 0 && j > 0)
                {
                    int k = start + i * (sides + 1) + j;
                    Join(k - sides - 2, k - sides - 1, k - 1, k);
                }
            }
        }
    }

    // The dark underside and the thin surface highlight stay inside the annulus.
    public void FinisherCrest(Vector3 centre, Vector3 aim, float inner, float radius, float angle, float swept, float fade,
        float halfThickness, Func<float, float, float> lift)
    {
        if (swept <= .001f || fade <= .001f || radius <= inner) return;
        const int slices = 56;
        int start = points.Count;
        float band = radius - inner;
        for (int i = 0; i <= slices; i++)
        {
            float u = i / (float)slices;
            float arc = u * swept;
            float nose = Mathf.SmoothStep(0f, 1f, u);
            float breaks = .81f + .19f * Mathf.Sin(arc * 51f);
            Vector3 outward = Quaternion.AngleAxis(Mathf.Lerp(-angle * .5f, angle * .5f, arc), Vector3.up) * aim;
            float height = lift(arc, radius);
            Vector3 at = centre + Vector3.up * height;
            float depth = halfThickness * Mathf.Lerp(.32f, .93f, nose) * breaks;
            float width = band * Mathf.Lerp(.16f, .49f, nose) * breaks;
            float reach = radius - width;
            float end = Mathf.SmoothStep(0f, 1f, (1f - u) * 17f);
            float alpha = fade * Mathf.SmoothStep(0f, 1f, u * 4f) * breaks * end;
            Vector3 high = Vector3.up * Mathf.Min(depth * .39f, Mathf.Max(0f, halfThickness - height));
            Vector3 low = Vector3.down * Mathf.Min(depth, Mathf.Max(0f, halfThickness + height));
            Vertex(at + outward * inner + low * .55f, new Vector2(u, 0f), WithAlpha(DeepWater.linear, alpha * .17f));
            Vertex(at + outward * reach + low, new Vector2(u, .15f), WithAlpha(DeepWater.linear, alpha * .56f));
            Vertex(at + outward * (radius - width * .56f) + low, new Vector2(u, .28f), WithAlpha(DeepWater.linear, alpha * .84f));
            Vertex(at + outward * (radius - width * .15f) + high, new Vector2(u, .75f), WithAlpha(Aqua.linear, alpha * .95f));
            Vertex(at + outward * radius, new Vector2(u, 2f), WithAlpha(Foam.linear, alpha * .84f));
            if (i == 0) continue;
            int k = start + i * 5;
            for (int j = 0; j < 4; j++) Join(k - 5 + j, k - 4 + j, k + j, k + j + 1);
        }
    }

    // The finisher's wall of water along the outer arc: it rises behind the swing, leans toward the hit radius as it breaks and drops back. `since` counts from the hit.
    public void BreakingWave(Vector3 ground, Vector3 aim, float radius, float angle, float side, float since, float height)
    {
        if (since < -PNGuraSlashArc.Lead || since > .36f || radius <= 0f) return;
        const int slices = 72;
        int start = points.Count;
        for (int i = 0; i <= slices; i++)
        {
            float u = i / (float)slices;
            // The swing passes `u` of the arc at this moment; the water there starts to rise then.
            float local = since - Mathf.Lerp(-PNGuraSlashArc.Lead, 0f, u);
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .10f, local));
            float fall = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.11f, .30f, local));
            float ends = Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) * 6f);
            float tongue = .52f + .34f * Mathf.Pow(Mathf.Abs(Mathf.Sin(u * 23f - local * 9f)), 2.4f) + .14f * Mathf.Sin(u * 57f + local * 17f);
            float top = height * rise * (1f - fall) * ends * tongue;
            float alpha = rise * (1f - fall * fall) * ends;
            Vector3 outward = Quaternion.AngleAxis(side * Mathf.Lerp(-angle * .5f, angle * .5f, u), Vector3.up) * aim;
            Vertex(ground + outward * (radius * .76f), new Vector2(u * 3f, .05f), WithAlpha(DeepWater.linear, alpha * .20f));
            Vertex(ground + outward * (radius * .85f) + Vector3.up * (top * .45f), new Vector2(u * 3f, .45f), WithAlpha(DeepWater.linear, alpha * .78f));
            Vertex(ground + outward * (radius * Mathf.Lerp(.88f, .94f, fall)) + Vector3.up * (top * .84f), new Vector2(u * 3f, .90f), WithAlpha(Aqua.linear, alpha * .90f));
            Vertex(ground + outward * (radius * Mathf.Lerp(.91f, .98f, fall)) + Vector3.up * top, new Vector2(u, 2f), WithAlpha(Foam.linear, alpha * .95f));
            Vertex(ground + outward * (radius * Mathf.Lerp(.93f, 1f, fall)) + Vector3.up * (top * 1.10f), new Vector2(u, 2f), WithAlpha(Foam.linear, 0f));
            if (i == 0) continue;
            int k = start + i * 5;
            for (int j = 0; j < 4; j++) Join(k - 5 + j, k - 4 + j, k + j, k + j + 1);
        }
    }

    // The foam the finisher leaves along its arc: a ragged band that drifts outward, breaks into clumps and fades.
    public void ArcFoam(Vector3 ground, Vector3 aim, float radius, float angle, float thickness, float fade, float age)
    {
        if (fade <= .001f) return;
        const int sections = 72;
        int start = points.Count;
        for (int i = 0; i <= sections; i++)
        {
            float u = i / (float)sections;
            float a = u * Mathf.PI * 2f;
            float ends = Mathf.SmoothStep(0f, 1f, Mathf.Min(u, 1f - u) * 7f);
            Vector3 outward = Quaternion.AngleAxis(Mathf.Lerp(-angle * .5f, angle * .5f, u), Vector3.up) * aim;
            float outer = radius * (1f + .022f * Mathf.Sin(a * 6.5f + 1.7f) + .014f * Mathf.Sin(a * 13f - age * 6f));
            float clump = Mathf.Clamp01(.55f + .45f * Mathf.Sin(a * 4f + age * 5f) + .30f * Mathf.Sin(a * 9.5f + 2.1f));
            float wide = thickness * Mathf.Lerp(.18f, 1f, clump) * ends;
            float shown = fade * Mathf.Lerp(.35f, 1f, clump) * ends;
            Vertex(ground + outward * (outer - wide), new Vector2(u, 2f), WithAlpha(Aqua.linear, 0f));
            Vertex(ground + outward * (outer - wide * .3f), new Vector2(u, 2f), WithAlpha(Aqua.linear, shown * .6f));
            Vertex(ground + outward * outer, new Vector2(u, 2f), WithAlpha(Foam.linear, shown));
            if (i == 0) continue;
            int k = start + i * 3;
            Join(k - 3, k - 2, k, k + 1);
            Join(k - 2, k - 1, k + 1, k + 2);
        }
    }

    public void BlastEdge(Vector3 centre, float radius, float alpha)
    {
        const int sections = 64;
        int start = points.Count;
        for (int i = 0; i <= sections; i++)
        {
            float u = i / (float)sections;
            float a = u * 2f * Mathf.PI;
            float breaks = .45f + .55f * Mathf.Pow(.5f + .5f * Mathf.Sin(a * 9f + .6f), 2f);
            Vector3 outward = new(Mathf.Sin(a), 0f, Mathf.Cos(a));
            Vertex(centre + outward * (radius * (.955f - .022f * breaks)), new Vector2(u, 2f), WithAlpha(Aqua.linear, 0f));
            Vertex(centre + outward * (radius * .983f), new Vector2(u, 2f), WithAlpha(Foam.linear, alpha * breaks * .45f));
            Vertex(centre + outward * radius, new Vector2(u, 2f), WithAlpha(Aqua.linear, alpha * breaks * .22f));
            if (i == 0) continue;
            int k = start + i * 3;
            Join(k - 3, k - 2, k, k + 1);
            Join(k - 2, k - 1, k + 1, k + 2);
        }
    }

    public void EdgeSpray(Vector3 centre, Vector3 aim, float radius, float angle, float since, int droplets, float height)
    {
        if (since < 0f || since > .35f || radius <= 0f) return;
        for (int i = 0; i < droplets; i++)
        {
            float seed = Mathf.Repeat(i * .618034f + .15f, 1f);
            float variation = Mathf.Repeat(i * .414214f + .28f, 1f);
            float phase = (since - .065f * seed) / Mathf.Lerp(.16f, .32f, variation);
            if (phase < 0f || phase >= 1f) continue;
            float turn = Mathf.Lerp(-angle * .49f, angle * .49f, (i + .75f * seed) / droplets);
            Vector3 outward = Quaternion.AngleAxis(turn, Vector3.up) * aim;
            Vector3 tangent = Vector3.Cross(Vector3.up, outward);
            Vector3 drift = (outward + tangent * Mathf.Lerp(-.36f, .36f, seed)).normalized;
            float flight = Mathf.Sin(phase * Mathf.PI);
            Vector3 at = centre + outward * radius * Mathf.Lerp(.74f, .98f, variation)
                + drift * (radius * Mathf.Lerp(.09f, .31f, seed) * phase)
                + Vector3.up * (height * Mathf.Lerp(.28f, 1f, variation) * flight);
            Vector3 spine = drift * .70f + Vector3.up * Mathf.Lerp(.9f, -.5f, phase);
            float size = radius * Mathf.Lerp(.009f, .032f, seed * seed) * (1f - phase * .72f);
            int start = points.Count;
            Color tint = WithAlpha(Color.Lerp(Aqua, Foam, seed), Mathf.Sin(Mathf.PI * Mathf.Pow(phase, .45f)));
            Vertex(at - spine * size * 1.9f, new Vector2(0f, 2f), WithAlpha(tint, 0f));
            Vertex(at - tangent * size * .51f, new Vector2(.5f, 2f), tint);
            Vertex(at + tangent * size * .51f, new Vector2(.5f, 2f), tint);
            Vertex(at + spine * size, new Vector2(1f, 2f), tint);
            Join(start, start + 1, start + 2, start + 3);
        }
    }

    // A thin speed line: pointed at both ends, fullest near its head, turned toward `view` so it keeps its width from a tilted camera.
    public void Streak(Vector3 tail, Vector3 head, float width, float alpha, Vector3 view)
    {
        Vector3 along = head - tail;
        if (alpha <= .001f || along.sqrMagnitude < .0025f) return;
        Vector3 across = Vector3.Cross(view, along).normalized;
        const int sections = 20;
        int start = points.Count;
        for (int i = 0; i <= sections; i++)
        {
            float u = i / (float)sections; // 0 = tail
            float body = Mathf.Pow(u, .8f) * Mathf.Clamp01((1f - u) * 5f);
            float shown = alpha * Mathf.SmoothStep(0f, 1f, u * 1.6f);
            Vector3 at = Vector3.Lerp(tail, head, u);
            Vertex(at - across * (width * body), new Vector2(u, 2f), WithAlpha(Aqua.linear, 0f));
            Vertex(at, new Vector2(u, 2f), WithAlpha(Color.Lerp(Aqua, Foam, u).linear, shown));
            Vertex(at + across * (width * body), new Vector2(u, 2f), WithAlpha(Aqua.linear, shown * .3f));
            if (i == 0) continue;
            int k = start + i * 3;
            Join(k - 3, k - 2, k, k + 1);
            Join(k - 2, k - 1, k + 1, k + 2);
        }
    }

    public void Ripple(Vector3 center, float radius, float thickness, float fade)
    {
        const int sections = 44;
        int start = points.Count;
        for (int i = 0; i <= sections; i++)
        {
            float u = i / (float)sections;
            float angle = u * Mathf.PI * 2f;
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vertex(center + outward * (radius - thickness), new Vector2(u, 0f), WithAlpha(Aqua, fade * .1f));
            Vertex(center + outward * radius, new Vector2(u, 1f), WithAlpha(Foam, fade));
            if (i != 0) Strip(start + i * 2);
        }
    }

    // The bright core of a straight cut: thick in the middle, pointed at both ends.
    public void Cut(Vector3 from, Vector3 to, float width, float alpha)
    {
        if (alpha <= .001f || (to - from).sqrMagnitude < .0025f) return;
        Vector3 across = Vector3.Cross(Vector3.up, to - from).normalized;
        const int sections = 24;
        int start = points.Count;
        for (int i = 0; i <= sections; i++)
        {
            float u = i / (float)sections;
            float body = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * u)), .8f);
            Vector3 at = Vector3.Lerp(from, to, u) + Vector3.up * .006f;
            Vertex(at - across * (width * body), new Vector2(u, 2f), WithAlpha(Aqua.linear, 0f));
            Vertex(at, new Vector2(u, 2f), WithAlpha(Color.Lerp(FlatCrest, Color.white, .45f).linear, alpha * Mathf.Clamp01(body * 2f)));
            Vertex(at + across * (width * body), new Vector2(u, 2f), WithAlpha(Aqua.linear, alpha * body * .35f));
            if (i == 0) continue;
            int k = start + i * 3;
            Join(k - 3, k - 2, k, k + 1);
            Join(k - 2, k - 1, k + 1, k + 2);
        }
    }

    // A cut bursting: drops leave both sides of the line, rise and fall.
    public void LineSpray(Vector3 from, Vector3 to, float since, int droplets, float height)
    {
        if (since < 0f || since > .27f) return;
        Vector3 across = Vector3.Cross(Vector3.up, to - from).normalized;
        for (int i = 0; i < droplets; i++)
        {
            float seed = Mathf.Repeat(i * .618034f + .15f, 1f);
            float variation = Mathf.Repeat(i * .414214f + .28f, 1f);
            float phase = (since - .035f * seed) / Mathf.Lerp(.13f, .22f, variation);
            if (phase < 0f || phase >= 1f) continue;
            float side = i % 2 == 0 ? 1f : -1f;
            Vector3 drift = across * side + (to - from).normalized * Mathf.Lerp(-.5f, .5f, variation);
            Vector3 at = Vector3.Lerp(from, to, Mathf.Lerp(.06f, .94f, (i + seed) / droplets))
                + drift * (Mathf.Lerp(.05f, .34f, seed) * phase)
                + Vector3.up * (height * Mathf.Lerp(.2f, 1f, variation) * Mathf.Sin(phase * Mathf.PI * .8f));
            Vector3 spine = drift.normalized * .6f + Vector3.up * Mathf.Lerp(.9f, -.6f, phase);
            Vector3 tangent = Vector3.Cross(Vector3.up, drift).normalized;
            float size = Mathf.Lerp(.014f, .045f, seed * seed) * (1f - phase * .7f);
            int start = points.Count;
            Color tint = WithAlpha(Color.Lerp(Aqua, Foam, seed), Mathf.Sin(Mathf.PI * Mathf.Pow(phase, .45f)));
            Vertex(at - spine * size * 1.9f, new Vector2(0f, 2f), WithAlpha(tint, 0f));
            Vertex(at - tangent * size * .51f, new Vector2(.5f, 2f), tint);
            Vertex(at + tangent * size * .51f, new Vector2(.5f, 2f), tint);
            Vertex(at + spine * size, new Vector2(1f, 2f), tint);
            Join(start, start + 1, start + 2, start + 3);
        }
    }

    // A ring of rising tongues of water, thrown outward at the top.
    public void Crown(Vector3 centre, float radius, float height, float age, float alpha)
    {
        if (alpha <= .001f || height <= .005f) return;
        const int sections = 72;
        int start = points.Count;
        for (int i = 0; i <= sections; i++)
        {
            float u = i / (float)sections;
            float a = u * 2f * Mathf.PI;
            Vector3 outward = new(Mathf.Sin(a), 0f, Mathf.Cos(a));
            float tongue = .22f + .58f * Mathf.Pow(Mathf.Abs(Mathf.Sin(a * 4.5f + 1.3f)), 3f) + .20f * (.5f + .5f * Mathf.Sin(a * 17f + age * 9f));
            float top = height * tongue;
            Vertex(centre + outward * (radius * .86f), new Vector2(u, 2f), WithAlpha(DeepWater.linear, alpha * .50f));
            Vertex(centre + outward * radius + Vector3.up * (top * .48f), new Vector2(u, 2f), WithAlpha(Aqua.linear, alpha * .70f));
            Vertex(centre + outward * (radius * 1.13f) + Vector3.up * (top * .84f), new Vector2(u, 2f), WithAlpha(Foam.linear, alpha * .62f));
            Vertex(centre + outward * (radius * 1.27f) + Vector3.up * top, new Vector2(u, 2f), WithAlpha(Foam.linear, 0f));
            if (i == 0) continue;
            int k = start + i * 4;
            for (int j = 0; j < 3; j++) Join(k - 4 + j, k - 3 + j, k + j, k + j + 1);
        }
    }

    // Drops thrown out of a burst in arcs: they leave the middle, fall past the rim and leave a small ring where they land. Decoration, free to pass the hit radius.
    public void Fountain(Vector3 centre, float radius, float age, int droplets)
    {
        for (int i = 0; i < droplets; i++)
        {
            float seed = Mathf.Repeat(i * .618034f + .15f, 1f);
            float variation = Mathf.Repeat(i * .414214f + .28f, 1f);
            float lift = Mathf.Repeat(i * .732051f + .61f, 1f);
            float phase = (age - .05f - .09f * seed) / Mathf.Lerp(.30f, .54f, variation);
            if (phase < 0f || phase >= 1.18f) continue;
            float turn = i * 2.39996f;
            Vector3 outward = new(Mathf.Sin(turn), 0f, Mathf.Cos(turn));
            float reach = radius * Mathf.Lerp(.62f, 1.28f, seed);
            float flown = 1f - Mathf.Pow(1f - Mathf.Min(phase, 1f), 1.5f);
            Vector3 ground = centre + outward * Mathf.Lerp(radius * .10f, reach, flown);
            if (phase >= .9f)
            {
                if (i % 3 == 0) Ripple(centre + outward * reach, Mathf.Lerp(.025f, .13f, (phase - .9f) / .28f), .014f, (1.18f - phase) * 2.2f);
                if (phase >= 1f) continue;
            }
            float peak = radius * Mathf.Lerp(.20f, .62f, lift);
            Vector3 at = ground + Vector3.up * (peak * 4f * phase * (1f - phase));
            Vector3 spine = (outward * (reach * .8f * (1f - phase * .7f)) + Vector3.up * (peak * 4f * (1f - 2f * phase))).normalized;
            Vector3 tangent = Vector3.Cross(Vector3.up, outward);
            // A big burst throws bigger drops.
            float size = Mathf.Lerp(.022f, .062f, seed * seed) * (1f - phase * .45f) * Mathf.Max(1f, radius * .95f);
            int start = points.Count;
            float alpha = Mathf.Min(1f, phase * 7f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.78f, 1f, phase)));
            Color tint = WithAlpha(Color.Lerp(Aqua, Foam, variation).linear, alpha);
            Vertex(at - spine * size * 2.3f, new Vector2(0f, 2f), WithAlpha(tint, 0f));
            Vertex(at - tangent * size * .55f, new Vector2(.5f, 2f), tint);
            Vertex(at + tangent * size * .55f, new Vector2(.5f, 2f), tint);
            Vertex(at + spine * size, new Vector2(1f, 2f), tint);
            Join(start, start + 1, start + 2, start + 3);
        }
    }

    // A ribbon of water wound around an upright axis, standing on edge so it reads from a tilted camera. The head is bright foam, the tail thins out. The half away from `viewer` is faded: the ribbon
    // reads as wrapping the body whatever the draw order is.
    public void Swirl(Vector3 foot, float radius, float topRadius, float rise, float head, float span, float thickness, float alpha, Vector3 viewer)
    {
        if (alpha <= .001f || radius <= 0f) return;
        const int sections = 40;
        int start = points.Count;
        for (int i = 0; i <= sections; i++)
        {
            float u = i / (float)sections; // 0 = tail, 1 = head
            float a = (head - span * (1f - u)) * Mathf.Deg2Rad;
            Vector3 outward = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float body = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(u, 1.5f))), .75f);
            float front = Mathf.Lerp(.28f, 1f, Mathf.SmoothStep(0f, 1f, .5f + .8f * Vector3.Dot(outward, viewer)));
            float shown = alpha * front * Mathf.SmoothStep(0f, 1f, u * 2.4f);
            Vector3 at = foot + outward * Mathf.Lerp(radius, topRadius, u) + Vector3.up * (rise * u + .012f * Mathf.Sin(a * 3f));
            float half = thickness * body;
            Vertex(at - Vector3.up * half, new Vector2(u, 2f), WithAlpha(DeepWater.linear, shown * .35f));
            Vertex(at, new Vector2(u, 2f), WithAlpha(Aqua.linear, shown * .85f));
            Vertex(at + Vector3.up * (half * .75f), new Vector2(u, 2f), WithAlpha(Color.Lerp(Aqua, Foam, u).linear, shown));
            if (i == 0) continue;
            int k = start + i * 3;
            Join(k - 3, k - 2, k, k + 1);
            Join(k - 2, k - 1, k + 1, k + 2);
        }
    }

    // One drop stretched along `spine`.
    public void Drop(Vector3 at, Vector3 spine, float size, float foam, float alpha)
    {
        if (alpha <= .001f) return;
        spine.Normalize();
        Vector3 tangent = Vector3.Cross(Vector3.forward, spine);
        if (tangent.sqrMagnitude < .01f) tangent = Vector3.right;
        tangent.Normalize();
        int start = points.Count;
        Color tint = WithAlpha(Color.Lerp(Aqua, Foam, foam).linear, alpha);
        Vertex(at - spine * size * 2.2f, new Vector2(0f, 2f), WithAlpha(tint, 0f));
        Vertex(at - tangent * size * .6f, new Vector2(.5f, 2f), tint);
        Vertex(at + tangent * size * .6f, new Vector2(.5f, 2f), tint);
        Vertex(at + spine * size, new Vector2(1f, 2f), tint);
        Join(start, start + 1, start + 2, start + 3);
    }

    private void Vertex(Vector3 point, Vector2 tex, Color color)
    {
        points.Add(node.transform.InverseTransformPoint(point));
        uv.Add(tex);
        colors.Add(color);
    }

    private void Strip(int next)
    {
        Join(next - 2, next - 1, next, next + 1);
    }

    private static Color WithAlpha(Color color, float alpha) { color.a = Mathf.Clamp01(alpha); return color; }
}
#endif
