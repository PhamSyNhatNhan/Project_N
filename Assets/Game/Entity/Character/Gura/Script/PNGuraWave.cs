#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// EN: The breaking wave of Gura's rush: one smooth wave-shaped mesh (built once, 1 wide, 1 high, 1 long, travelling +z, origin under its thickest part) drawn with the PNVfx/GuraWave material. Display only.
// VI: Con song vo: mot luoi hinh song dung mot lan, ve bang material PNVfx/GuraWave. Chi la hinh.
internal sealed class PNGuraWave : IDisposable
{
    private static readonly int Opacity = Shader.PropertyToID("_Opacity"), FxTime = Shader.PropertyToID("_FxTime"), Dissolve = Shader.PropertyToID("_Dissolve"), Flow = Shader.PropertyToID("_Flow"), Curl = Shader.PropertyToID("_Curl"), Calm = Shader.PropertyToID("_Calm");
    private static Mesh shape;
    private readonly GameObject node;
    private readonly MeshRenderer renderer;
    private readonly MaterialPropertyBlock block = new();

    public PNGuraWave(Transform owner, string name = "Gura wave")
    {
        node = new GameObject(name) { layer = owner.gameObject.layer };
        node.transform.SetParent(owner, false);
        node.AddComponent<MeshFilter>().sharedMesh = Shape();
        renderer = node.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = Resources.Load<Material>("PNVfx/GuraWave");
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        node.SetActive(false);
    }

    // `crest`: the ground under the crest. `dissolve`: 0 whole, 1 gone. `curl`: how far the crest leans over the nose. `calm`: 0 breaking, 1 spent water with little foam. `upward`: the side the shell bulges to (up for a wave on the floor).
    public void Draw(Vector3 crest, Vector3 forward, float width, float height, float length, float time, float opacity, float dissolve = 0f, float curl = 0f, Vector3? upward = null, float calm = 0f)
    {
        if (node == null) return;
        if (opacity <= .01f || height <= .01f || dissolve >= 1f) { Hide(); return; }
        node.transform.SetPositionAndRotation(crest, Quaternion.LookRotation(forward, upward ?? Vector3.up));
        Vector3 under = node.transform.parent.lossyScale;
        node.transform.localScale = new Vector3(width / under.x, height / under.y, length / under.z);
        block.SetFloat(Opacity, opacity);
        block.SetFloat(FxTime, time);
        block.SetFloat(Dissolve, dissolve);
        block.SetVector(Flow, node.transform.forward);
        block.SetFloat(Curl, curl);
        block.SetFloat(Calm, calm);
        renderer.SetPropertyBlock(block);
        node.SetActive(true);
    }

    public void Hide() { if (node != null) node.SetActive(false); }

    public void Dispose()
    {
        if (node == null) return;
        Hide();
        if (Application.isPlaying) Object.Destroy(node); else Object.DestroyImmediate(node);
    }

    // One smooth low shell: a narrow flat tail widening into a round nose, thickest just behind the nose, its rim on the ground all round. u runs tail (0) to nose (1), s across (-1..1); the origin
    // is under the thickest part (u = Shoulder). From the rim a flat apron runs out on the floor, widest toward the tail (uv2.x: 0 rim, 1 outer edge): part of the same mesh, so it can never come loose.
    private const float Shoulder = .72f;

    private static Mesh Shape()
    {
        if (shape != null) return shape;
        const int along = 48, across = 32, apron = 0, columns = across + 1 + apron * 2;
        var points = new List<Vector3>((along + 1) * columns);
        var uv = new List<Vector2>((along + 1) * columns);
        var flat = new List<Vector2>((along + 1) * columns);
        // The apron is drawn first: nothing writes depth, so the far side of it must not paint over the shell.
        var triangles = new List<int>(along * (columns - 1) * 6);
        var floor = new List<int>(along * apron * 12);
        for (int i = 0; i <= along; i++)
        {
            // Closer rows toward the nose, where the outline turns fastest.
            float u = 1f - Mathf.Pow(1f - i / (float)along, 1.6f);
            float cap = Mathf.Clamp01((u - Shoulder) / (1f - Shoulder));
            float round = Mathf.Sqrt(Mathf.Max(0f, 1f - cap * cap));
            float rise = Mathf.SmoothStep(0f, 1f, u / Shoulder);
            float wide = .5f * Mathf.Lerp(.52f, 1f, rise) * round;
            float tall = Mathf.Lerp(.12f, 1f, rise) * Mathf.Pow(round, .8f);
            float skirt = Mathf.Lerp(.36f, .28f, Mathf.Clamp01(u / Shoulder)) * Mathf.Clamp01((1f - u) * 3.5f);
            // One row from the left outer edge to the right one.
            for (int j = 0; j < columns; j++)
            {
                int shell = j - apron;
                float s, out_ = 0f;
                if (shell < 0) { s = -1f; out_ = -shell / (float)apron; }
                else if (shell > across) { s = 1f; out_ = (shell - across) / (float)apron; }
                else s = Mathf.Sin((shell / (float)across * 2f - 1f) * Mathf.PI * .5f);          // closer columns at the rim
                float cross = out_ > 0f ? 0f : Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(Mathf.Abs(s), 2.4f)), .7f);
                points.Add(new Vector3(s * (wide + skirt * out_), tall * cross, u - Shoulder - skirt * out_ * .8f));
                uv.Add(new Vector2(u, (s + 1f) * .5f));
                flat.Add(new Vector2(out_, 0f));
                if (i == 0 || j == 0) continue;
                int k = i * columns + j;
                var into = j <= apron || j > apron + across ? floor : triangles;
                into.Add(k - columns - 1); into.Add(k - 1); into.Add(k - columns);
                into.Add(k - columns); into.Add(k - 1); into.Add(k);
            }
        }
        shape = new Mesh { name = "Gura breaking wave", hideFlags = HideFlags.HideAndDontSave };
        shape.SetVertices(points);
        shape.SetUVs(0, uv);
        shape.SetUVs(1, flat);
        floor.AddRange(triangles);
        shape.SetTriangles(floor, 0);
        shape.RecalculateNormals();
        shape.RecalculateBounds();
        return shape;
    }
}

// EN: The water the rush leaves lying on the floor: one flat strip along the way it went, drawn by the same material in its floor mode (the pattern stays where it is; old parts break up).
// VI: Dai nuoc nam tren san doc duong da luot.
internal sealed class PNGuraWakeStrip : IDisposable
{
    private static readonly int Opacity = Shader.PropertyToID("_Opacity"), FxTime = Shader.PropertyToID("_FxTime"), Floor = Shader.PropertyToID("_Floor"), Flow = Shader.PropertyToID("_Flow");
    private readonly GameObject node;
    private readonly Mesh mesh;
    private readonly Material material;
    private readonly MeshRenderer renderer;
    private readonly MaterialPropertyBlock block = new();
    private readonly List<Vector3> points = new();
    private readonly List<Vector2> uv = new(), flat = new();
    private readonly List<int> triangles = new();

    public PNGuraWakeStrip(Transform owner, int queue = 3010)
    {
        node = new GameObject("Gura rush floor water") { layer = owner.gameObject.layer };
        node.transform.SetParent(owner, false);
        mesh = new Mesh { name = "Gura rush floor water", hideFlags = HideFlags.HideAndDontSave };
        mesh.MarkDynamic();
        node.AddComponent<MeshFilter>().sharedMesh = mesh;
        renderer = node.AddComponent<MeshRenderer>();
        var shared = Resources.Load<Material>("PNVfx/GuraWave");
        // Under the coat and the wave.
        if (shared != null) material = new Material(shared) { hideFlags = HideFlags.HideAndDontSave, renderQueue = queue };
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        node.SetActive(false);
    }

    public void Begin() { points.Clear(); uv.Clear(); flat.Clear(); triangles.Clear(); }

    // One cross-section, from the oldest end forward. `along`: metres from the start; `old`: 0 fresh, 1 gone.
    public void Add(Vector3 centre, Vector3 side, float half, float along, float old, float alpha)
    {
        int k = points.Count;
        points.Add(node.transform.InverseTransformPoint(centre - side * half));
        points.Add(node.transform.InverseTransformPoint(centre + side * half));
        uv.Add(new Vector2(along, 0f)); uv.Add(new Vector2(along, 1f));
        flat.Add(new Vector2(old, alpha)); flat.Add(new Vector2(old, alpha));
        if (k == 0) return;
        triangles.Add(k - 2); triangles.Add(k); triangles.Add(k - 1);
        triangles.Add(k - 1); triangles.Add(k); triangles.Add(k + 1);
    }

    public void Draw(float time, Vector3 forward)
    {
        if (node == null) return;
        if (points.Count < 4) { Hide(); return; }
        mesh.Clear();
        mesh.SetVertices(points);
        mesh.SetUVs(0, uv);
        mesh.SetUVs(1, flat);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        block.SetFloat(Opacity, 1f);
        block.SetFloat(FxTime, time);
        block.SetFloat(Floor, 1f);
        block.SetVector(Flow, forward);
        renderer.SetPropertyBlock(block);
        node.SetActive(true);
    }

    public void Hide() { if (node != null) node.SetActive(false); }

    public void Dispose()
    {
        if (node == null) return;
        Hide();
        foreach (var owned in new Object[] { material, mesh, node })
            if (owned != null) { if (Application.isPlaying) Object.Destroy(owned); else Object.DestroyImmediate(owned); }
    }
}
#endif
