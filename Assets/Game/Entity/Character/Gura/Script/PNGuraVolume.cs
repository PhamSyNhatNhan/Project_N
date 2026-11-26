#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class PNGuraVolume : IDisposable
{
    private readonly GameObject node;
    private readonly MeshRenderer renderer;
    private readonly MaterialPropertyBlock properties = new();
    public Mesh Mesh { get; }
    public Transform Transform => node.transform;

    public PNGuraVolume(Transform parent, string name, bool ghost = false)
    {
        node = new GameObject(name) { layer = parent.gameObject.layer };
        node.transform.SetParent(parent, false);
        Mesh = new Mesh { name = name };
        node.AddComponent<MeshFilter>().sharedMesh = Mesh;
        renderer = node.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = Resources.Load<Material>(ghost ? "PNVfx/GuraWater" : "PNVfx/GuraSolid");
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        Hide();
    }

    public void Show(float opacity = 1f, float waterline = -10000f, bool waterSkin = false)
    {
        properties.SetFloat("_Solid", 1f);
        properties.SetFloat("_Opacity", opacity);
        properties.SetFloat("_Waterline", waterline);
        properties.SetFloat("_WaterSkin", waterSkin ? 1f : 0f);
        renderer.SetPropertyBlock(properties);
        node.SetActive(true);
    }

    public void Hide() { if (node != null) node.SetActive(false); }

    public void Dispose()
    {
        Hide();
        if (Application.isPlaying) { UnityEngine.Object.Destroy(Mesh); UnityEngine.Object.Destroy(node); }
        else { UnityEngine.Object.DestroyImmediate(Mesh); UnityEngine.Object.DestroyImmediate(node); }
    }
}

internal sealed class PNGuraFacets
{
    public readonly List<Vector3> Points = new();
    public readonly List<Color> Colors = new();
    private readonly List<int> triangles = new();

    public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        int start = Points.Count;
        Points.Add(a); Points.Add(b); Points.Add(c);
        Colors.Add(color); Colors.Add(color); Colors.Add(color);
        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
    }

    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        Triangle(a, b, c, color);
        Triangle(a, c, d, color);
    }

    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color ca, Color cb, Color cc, Color cd)
    {
        int start = Points.Count;
        Points.Add(a); Points.Add(b); Points.Add(c); Points.Add(d);
        Colors.Add(ca); Colors.Add(cb); Colors.Add(cc); Colors.Add(cd);
        triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
        triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
    }

    // Smooth shared positions only on the hull. Teeth, fins and facial details retain their own edges.
    public void ApplyHull(Mesh mesh, int smoothVertexCount)
    {
        Apply(mesh);
        Vector3[] normals = mesh.normals;
        var shared = new Dictionary<Vector3, Vector3>();
        for (int i = 0; i < smoothVertexCount; i++)
        {
            shared.TryGetValue(Points[i], out Vector3 normal);
            shared[Points[i]] = normal + normals[i];
        }
        for (int i = 0; i < smoothVertexCount; i++) normals[i] = shared[Points[i]].normalized;
        mesh.normals = normals;
    }

    public void Apply(Mesh mesh, Vector3[] points = null)
    {
        mesh.Clear();
        if (points != null) mesh.SetVertices(points);
        else mesh.SetVertices(Points);
        mesh.SetColors(Colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
    }
}
#endif
