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
    private static readonly Color FlatCrest = new Color(.5f, 1f, 1f);
    private readonly GameObject node;
    private readonly Mesh mesh;
    private bool disposed;
    private readonly List<Vector3> points = new List<Vector3>(256);
    private readonly List<Color> colors = new List<Color>(256);
    private readonly List<Vector2> uv = new List<Vector2>(256);
    private readonly List<int> triangles = new List<int>(768);

    public PNGuraWaterFx(Transform owner)
    {
        node = new GameObject("Gura Water") { layer = owner.gameObject.layer };
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

    public void Trail(float startAge, float sweepEnd, float now, float lifetime, Func<float, PNGuraAttackWeapon.Pose> sample, bool sharp)
    {
        // Same recent-sample ribbon as the Eve sweep. Rebuild its bounded history at the selected Action age.
        float from = Mathf.Max(startAge, now - lifetime);
        float to = Mathf.Min(now, sweepEnd);
        if (to - from < .001f) return;
        int samples = Mathf.Clamp(Mathf.CeilToInt((to - from) / .006f), 2, 24);
        int start = points.Count;
        for (int i = 0; i <= samples; i++)
        {
            float u = i / (float)samples;
            float at = Mathf.Lerp(from, to, u);
            var blade = sample(at);
            float fresh = Mathf.Clamp01(1f - (now - at) / lifetime);
            float taper = Mathf.Sqrt(fresh);
            Vector3 tip = blade.At(.99f);
            Vector3 shaft = Vector3.Lerp(tip, blade.At(sharp ? .72f : .82f), taper);
            Vector3 ridge = Vector3.Lerp(shaft, tip, .83f);
            Color inner = WithAlpha(Aqua, .05f * fresh);
            Color water = WithAlpha(Aqua, (sharp ? .85f : .62f) * fresh * fresh);
            Color foam = WithAlpha(Foam, (sharp ? 1f : .91f) * fresh);
            Vertex(shaft, new Vector2(u, 0f), inner);
            Vertex(ridge, new Vector2(u, .64f), water);
            Vertex(tip, new Vector2(u, .96f), foam);

            if (i != 0)
            {
                int row = start + i * 3;
                Join(row - 3, row - 2, row, row + 1);
                Join(row - 2, row - 1, row + 1, row + 2);
            }
        }
    }

    // The edge of a hit sector: a thin flat-colour crest exactly at the hit radius, from the start of the swing to `swept` (0..1) of the hit angle.
    public void SectorEdge(Vector3 centre, Vector3 forward, float radius, float angle, float side, float swept, Func<float, float> lift, float fade)
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
            Vector3 at = centre + Vector3.up * lift(swing);
            // v = 2 asks the shader for the flat vertex colour, as the flipbook water beside it is drawn.
            Vertex(at + outward * (radius - width), new Vector2(u, 2f), WithAlpha(crest, alpha));
            Vertex(at + outward * radius, new Vector2(u, 2f), WithAlpha(crest, alpha));
            if (i != 0) Strip(start + i * 2);
        }
    }

    private void Join(int a, int b, int c, int d)
    {
        triangles.Add(a); triangles.Add(b); triangles.Add(c);
        triangles.Add(c); triangles.Add(b); triangles.Add(d);
    }

    public void Flight(in PNGuraAttackWeapon.Pose blade, float age)
    {
        Vector3 tip = blade.At(1f);
        Vector3 normal = Vector3.Cross(blade.Axis, blade.Across);
        for (int layer = 0; layer < 3; layer++)
        {
            const int segments = 25;
            int start = points.Count;
            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments;
                float angle = layer * 2.0944f + u * 4.7f - age * 12f;
                Vector3 radial = blade.Across * Mathf.Cos(angle) + normal * Mathf.Sin(angle);
                float opening = Mathf.Sin(u * Mathf.PI * .88f);
                Vector3 center = tip - blade.Axis * (u * (1.12f + .28f * Mathf.Clamp01(age / .14f)));
                center += radial * ((.108f + .075f * u) * opening);
                float width = .034f * (.18f + opening) * (1f - .5f * u);
                float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u * 17f)) * (1f - u);
                Vertex(center - radial * width, new Vector2(u, 0f), WithAlpha(Aqua, alpha * .78f));
                Vertex(center + radial * width, new Vector2(u, 1f), WithAlpha(Foam, alpha));
                if (i != 0) Strip(start + i * 2);
            }
        }
    }

    public void GroundWake(Vector3 origin, Vector3 forward, float length, float width, float age, float alpha)
    {
        Vector3 lateral = Vector3.Cross(Vector3.up, forward);
        for (int side = -1; side <= 1; side += 2)
        {
            const int rows = 18;
            int start = points.Count;
            for (int i = 0; i <= rows; i++)
            {
                float u = i / (float)rows;
                float spread = width * (.31f + .68f * u);
                float thickness = width * (.07f + .20f * Mathf.Sin(Mathf.PI * u));
                Vector3 center = origin - forward * (u * length) + lateral * (side * spread);
                float fade = alpha * (1f - u) * (.78f + .22f * Mathf.Sin(u * 15f - age * 22f));
                Vertex(center - lateral * thickness, new Vector2(u, 0f), WithAlpha(Aqua, fade * .35f));
                Vertex(center + lateral * thickness, new Vector2(u, 1f), WithAlpha(Foam, fade));
                if (i != 0) Strip(start + i * 2);
            }
        }
    }

    public void BowSplash(Vector3 origin, Vector3 direction, float width, float height, float age, float alpha)
    {
        Vector3 lateral = Vector3.Cross(Vector3.up, direction);
        for (int side = -1; side <= 1; side += 2)
        {
            const int segments = 18;
            int start = points.Count;
            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments;
                float arc = u * 2.05f;
                float envelope = Mathf.Sin(u * Mathf.PI);
                Vector3 basePoint = origin + direction * (Mathf.Cos(arc)*width)
                    + lateral * (side * Mathf.Sin(arc)*width*.85f);
                float crest = height * envelope * (.86f + .14f * Mathf.Sin(age*30f + u*17f));
                float fade = alpha * envelope;
                Vertex(basePoint, new Vector2(u,.07f), WithAlpha(Aqua, fade*.11f));
                Vertex(basePoint + Vector3.up*crest + lateral*(side*.065f*envelope),
                    new Vector2(u,.97f), WithAlpha(Foam, fade*.93f));
                if (i != 0) Strip(start + i * 2);
            }
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
