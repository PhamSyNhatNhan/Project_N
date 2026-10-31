#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Cast-owned floor markers: constellation boundary for sweep one only; star/line for Star Point.
// Phase and position come from the Action. Both use the already warmed project shader.
public sealed class PNStarkeeperTelegraph : IDisposable, IPNCameraFacing
{
    private readonly GameObject root;
    private readonly List<Mesh> meshes = new List<Mesh>();
    private readonly List<Material> materials = new List<Material>();
    private readonly Material fill, rim;
    private readonly bool star;
    private float shown = 1f;
    private Camera preview; private Mesh nodeMesh; private Vector3[] nodeCenters, nodeVertices; private float[] nodeSizes;
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
    public static PNStarkeeperTelegraph Sector(Vector3 origin, Vector3 direction, float radius, float angle, Scene scene, Camera preview = null)
    {
        direction.y = 0; if (direction.sqrMagnitude < 1e-6f) direction = Vector3.forward;
        var t = new PNStarkeeperTelegraph("Starkeeper sweep", origin, Quaternion.LookRotation(direction.normalized, Vector3.up), scene, false);
        try
        {
            t.preview = preview;
            // Compact luminous round nodes at the slash height; no floor-star stamps.
            t.fill.SetColor("_Tint", new Color(1f,.70f,.22f,1)); t.fill.SetFloat("_Mode",15); t.fill.SetFloat("_DstBlend",1);
            t.rim.SetColor("_Tint",new Color(.86f,.53f,.16f,1)); t.rim.SetFloat("_Mode",16); t.rim.SetFloat("_DstBlend",1);
            var nodes = new List<Vector3>();
            var angles = new[] { -.42f,-.24f,-.05f,.23f,.42f };
            var distances = new[] { .95f,.91f,.97f,.9f,.95f };
            var sizes = new[] { .15f,.104f,.164f,.092f,.132f };
            for(int i=0;i<angles.Length;i++)
            {
                float a=angle*angles[i]*Mathf.Deg2Rad;
                nodes.Add(new Vector3(Mathf.Sin(a),0,Mathf.Cos(a))*radius*distances[i] + Vector3.up*.275f);
            }
            var starV = new List<Vector3>(); var starT = new List<int>(); var uv = new List<Vector2>();
            for(int nodeIndex=0;nodeIndex<nodes.Count;nodeIndex++)
            {
                var node=nodes[nodeIndex]; int k = starV.Count; float size = sizes[nodeIndex];
                starV.Add(node + new Vector3(-size,0,-size)); starV.Add(node + new Vector3(size,0,-size));
                starV.Add(node + new Vector3(size,0,size)); starV.Add(node + new Vector3(-size,0,size));
                uv.AddRange(new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
                starT.AddRange(new[] { k,k+1,k+2,k,k+2,k+3 });
            }
            var stars = Build("Constellation stars", starV, starT); stars.SetUVs(0,uv);
            t.nodeMesh=stars; t.nodeCenters=nodes.ToArray(); t.nodeSizes=sizes; t.nodeVertices=new Vector3[starV.Count];
            t.Draw("Constellation stars", stars, t.fill);
            var lineV = new List<Vector3>(); var lineT = new List<int>();
            void Link(Vector3 from, Vector3 to)
            {
                var side = Vector3.Cross(Vector3.up, (to-from).normalized) * .012f; int k = lineV.Count;
                lineV.Add(from-side); lineV.Add(from+side); lineV.Add(to+side); lineV.Add(to-side);
                lineT.AddRange(new[] { k,k+1,k+2,k,k+2,k+3 });
            }
            for(int i=0;i<nodes.Count-1;i++) Link(nodes[i],nodes[i+1]);
            var links=Build("Constellation boundary",lineV,lineT);
            var linkUv=new List<Vector2>();
            for(int i=0;i<nodes.Count-1;i++) linkUv.AddRange(new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right});
            links.SetUVs(0,linkUv); t.Draw("Constellation boundary",links,t.rim);
            t.Apply(0f); t.Align();
            if(Application.isPlaying) PNCameraFacingSystem.Register(t);
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
        phase = Mathf.Clamp01(phase); Apply(phase); if(!star) Align();
    }
    private void Apply(float phase)
    {
        if(!star) { fill.SetFloat("_Reveal",phase); rim.SetFloat("_Reveal",phase); }
        float baseAlpha = .16f + .26f * phase, blink = star ? Mathf.Max(0f, Mathf.Sin(phase * phase * 70f)) * .45f * phase : 0f;
        fill.SetFloat("_Opacity", (star ? baseAlpha + blink : .6f + .4f * phase) * shown); rim.SetFloat("_Opacity", Mathf.Clamp01(.5f + .5f * phase + blink) * shown);
    }
    public void SetOrigin(Vector3 origin) { if (root != null) root.transform.position = origin + Vector3.up * .025f; }
    public bool Align()
    {
        if(root==null || nodeMesh==null) return false;
        var camera=PNCameraFacing.ResolveCamera(preview,Application.isPlaying); if(camera==null)return false;
        var right=root.transform.InverseTransformVector(camera.transform.right);
        var up=root.transform.InverseTransformVector(camera.transform.up);
        for(int i=0;i<nodeCenters.Length;i++) for(int j=0;j<4;j++)
        {
            float x=j==0||j==3?-1:1,y=j<2?-1:1;
            nodeVertices[i*4+j]=nodeCenters[i]+(right*x+up*y)*nodeSizes[i];
        }
        nodeMesh.vertices=nodeVertices;nodeMesh.RecalculateBounds();return true;
    }
    public void Hide() { shown = 0f; Apply(0f); }
    public void Dispose()
    {
        PNCameraFacingSystem.Unregister(this);
        if (root != null) { root.SetActive(false); Destroy(root); }
        foreach (var m in meshes) Destroy(m); foreach (var m in materials) Destroy(m); meshes.Clear(); materials.Clear();
    }
    private static void Destroy(UnityEngine.Object o) { if (o == null) return; if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o); }
}
#endif
