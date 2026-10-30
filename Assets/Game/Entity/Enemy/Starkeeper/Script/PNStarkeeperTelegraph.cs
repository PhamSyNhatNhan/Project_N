#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Cast-owned floor markers of the Starkeeper: a sector for the sweep, a star with a line to it for the star point. Every value is driven by the phase the Action passes in
// (no clock of its own); the meshes, materials and the root are created per cast and destroyed by Dispose. Uses the already warmed PN/StarweaverEffects shader (mode 0:
// vertex colour x tint x opacity, alpha blended), so no new shader or variant.
public sealed class PNStarkeeperTelegraph : IDisposable
{
    private readonly GameObject root;
    private readonly List<Mesh> meshes = new List<Mesh>();
    private readonly List<Material> materials = new List<Material>();
    private readonly Material fill, rim;
    private readonly bool star;
    private float shown = 1f;
    private static readonly Color Gold = new Color(1f, .78f, .25f, 1f);

    private PNStarkeeperTelegraph(string name, Vector3 position, Quaternion rotation, Scene scene, bool star)
    {
        this.star = star;
        root = new GameObject(name); SceneManager.MoveGameObjectToScene(root, scene); root.transform.SetPositionAndRotation(position + Vector3.up * .025f, rotation);
        fill = Material(new Color(1f, .72f, .2f, 1f)); rim = Material(Gold);
    }
    private Material Material(Color tint)
    {
        var shader = Resources.Load<Shader>("PNStarweaverEffects"); if (shader == null) throw new InvalidOperationException("Starweaver effect shader missing");
        var m = new Material(shader) { name = "Starkeeper telegraph", hideFlags = HideFlags.HideAndDontSave }; materials.Add(m);
        m.SetFloat("_Mode", 0); m.SetFloat("_DstBlend", 10); m.SetColor("_Tint", tint); m.SetFloat("_Opacity", 0f); return m;
    }
    private void Draw(string name, Mesh mesh, Material material)
    {
        meshes.Add(mesh); var go = new GameObject(name); go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
    }
    // Triangles are added on both sides so the floor markers show whichever way the shader culls.
    private static Mesh Build(string name, List<Vector3> vertices, List<int> triangles)
    {
        var all = new List<int>(triangles.Count * 2); all.AddRange(triangles);
        for (int i = 0; i < triangles.Count; i += 3) { all.Add(triangles[i]); all.Add(triangles[i + 2]); all.Add(triangles[i + 1]); }
        var colors = new Color[vertices.Count]; for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
        var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave }; mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(0, new Vector2[vertices.Count]); mesh.SetTriangles(all, 0); mesh.RecalculateBounds();
        return mesh;
    }

    // A sector of `angle` degrees and `radius` metres in front of `origin`, centred on `direction`.
    public static PNStarkeeperTelegraph Sector(Vector3 origin, Vector3 direction, float radius, float angle, Scene scene)
    {
        direction.y = 0; if (direction.sqrMagnitude < 1e-6f) direction = Vector3.forward;
        var t = new PNStarkeeperTelegraph("Starkeeper sweep", origin, Quaternion.LookRotation(direction.normalized, Vector3.up), scene, false);
        try
        {
            const int n = 28; var v = new List<Vector3> { Vector3.zero }; var tri = new List<int>(); var rimV = new List<Vector3>(); var rimT = new List<int>();
            for (int i = 0; i <= n; i++) { float a = (-angle * .5f + angle * i / n) * Mathf.Deg2Rad; var p = new Vector3(Mathf.Sin(a) * radius, 0, Mathf.Cos(a) * radius); v.Add(p); if (i > 0) { tri.Add(0); tri.Add(i); tri.Add(i + 1); } }
            for (int i = 0; i <= n; i++) { float a = (-angle * .5f + angle * i / n) * Mathf.Deg2Rad; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)); rimV.Add(d * radius); rimV.Add(d * (radius - .06f)); if (i > 0) { int k = i * 2; rimT.AddRange(new[] { k - 2, k, k + 1, k - 2, k + 1, k - 1 }); } }
            t.Draw("Sector fill", Build("Sector fill", v, tri), t.fill); t.Draw("Sector rim", Build("Sector rim", rimV, rimT), t.rim);
            t.Apply(0f);
        }
        catch { t.Dispose(); throw; }
        return t;
    }

    // A four-pointed star of `markRadius` at `anchor` and a thin line from `origin` to it.
    public static PNStarkeeperTelegraph Point(Vector3 origin, Vector3 anchor, float markRadius, Scene scene)
    {
        var flat = anchor - origin; flat.y = 0; float length = flat.magnitude;
        var t = new PNStarkeeperTelegraph("Starkeeper star point", origin, Quaternion.LookRotation(length > 1e-4f ? flat / length : Vector3.forward, Vector3.up), scene, true);
        try
        {
            var v = new List<Vector3> { new Vector3(0, 0, length) }; var tri = new List<int>();
            for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4, r = i % 2 == 0 ? markRadius * 1.6f : markRadius * .55f; v.Add(new Vector3(Mathf.Sin(a) * r, 0, length + Mathf.Cos(a) * r)); }
            for (int i = 1; i <= 8; i++) { tri.Add(0); tri.Add(i); tri.Add(i % 8 + 1); }
            var ring = new List<Vector3>(); var ringT = new List<int>(); const int n = 24;
            for (int i = 0; i <= n; i++) { float a = i * Mathf.PI * 2 / n; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)); ring.Add(new Vector3(0, 0, length) + d * markRadius); ring.Add(new Vector3(0, 0, length) + d * (markRadius - .05f)); if (i > 0) { int k = i * 2; ringT.AddRange(new[] { k - 2, k, k + 1, k - 2, k + 1, k - 1 }); } }
            float w = .03f; var line = new List<Vector3> { new Vector3(-w, 0, 0), new Vector3(w, 0, 0), new Vector3(w, 0, length), new Vector3(-w, 0, length) };
            t.Draw("Star", Build("Star", v, tri), t.fill); t.Draw("Star ring", Build("Star ring", ring, ringT), t.rim); t.Draw("Line", Build("Line", line, new List<int> { 0, 3, 2, 0, 2, 1 }), t.rim);
            t.Apply(0f);
        }
        catch { t.Dispose(); throw; }
        return t;
    }

    // phase 0..1 over the windup: fades in, then blinks faster toward the end (the star point's warning of the lunge).
    public void SetPhase(float phase)
    {
        phase = Mathf.Clamp01(phase); Apply(phase);
    }
    private void Apply(float phase)
    {
        float baseAlpha = .16f + .26f * phase, blink = star ? Mathf.Max(0f, Mathf.Sin(phase * phase * 70f)) * .45f * phase : 0f;
        fill.SetFloat("_Opacity", (baseAlpha + blink) * shown); rim.SetFloat("_Opacity", Mathf.Clamp01(.5f + .5f * phase + blink) * shown);
    }
    public void Hide() { shown = 0f; Apply(0f); }
    // The sweep's second sector: re-aimed, shown only for the short moment before the second sweep.
    public void Show(Vector3 direction)
    {
        direction.y = 0; if (direction.sqrMagnitude > 1e-6f && root != null) root.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        shown = 1f; Apply(.8f);
    }
    public void Dispose()
    {
        if (root != null) { root.SetActive(false); Destroy(root); }
        foreach (var m in meshes) Destroy(m); foreach (var m in materials) Destroy(m); meshes.Clear(); materials.Clear();
    }
    private static void Destroy(UnityEngine.Object o) { if (o == null) return; if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o); }
}
#endif
