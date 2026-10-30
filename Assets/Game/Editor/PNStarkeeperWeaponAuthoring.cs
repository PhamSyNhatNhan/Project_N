#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Builds the Starkeeper's golden spear (Starkeeper/Art/StarSpear.asset + StarSpear.mat) and puts it on the Starkeeper prefab as the child "Star spear".
// Flat-shaded low-poly mesh, +Y toward the blade tip, origin at the centre of the middle diamond, front faces -Z (toward the camera, like the Starweaver's needle).
// One material, one mesh and ONE gold: the faces carry the colour in vertex colours, shaded by a fixed light, so the spear reads as metal without a lit shader.
// Parts are joined, not merely touching: one rod runs through the whole spear, the middle gem is truncated (flat caps wider than the rod, so the rod enters a face,
// not a point), and the blade's lower point and the tail spike's upper point sink into their collars. Running it again rebuilds the same asset (same GUID) and the same prefab child.
public static class PNStarkeeperWeaponAuthoring
{
    const string Folder = "Assets/Game/Entity/Enemy/Starkeeper/Art";
    const string MeshPath = Folder + "/StarSpear.asset", MaterialPath = Folder + "/StarSpear.mat";
    const string PrefabPath = "Assets/Game/Entity/Enemy/Starkeeper/Prefab/Starkeeper.prefab";
    const float Scale = .91f;                 // 0.55 m of drawing -> 0.5 m spear
    static readonly Vector3 Light = new Vector3(-.4f, .7f, -.6f).normalized;
    static readonly Color Gold = new Color(.90f, .66f, .20f);                // the whole spear is ONE gold; the facets differ only by the fixed light, the core star is the same gold unshaded

    sealed class Builder
    {
        public readonly List<Vector3> Vertices = new List<Vector3>(); public readonly List<Color> Colors = new List<Color>(); public readonly List<Vector2> Uvs = new List<Vector2>(); public readonly List<int> Triangles = new List<int>();
        // A flat triangle whose normal points away from `inside`; shade = ambient + diffuse from the fixed light (a glowing face skips the shading).
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 inside, Color color, bool glow = false)
        {
            var normal = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(normal, (a + b + c) / 3f - inside) < 0) { var t = b; b = c; c = t; normal = -normal; }
            normal.Normalize();
            float shade = glow ? 1f : .55f + .45f * Mathf.Clamp01(Vector3.Dot(normal, Light));
            var shaded = new Color(color.r * shade, color.g * shade, color.b * shade, 1f);
            int start = Vertices.Count;
            foreach (var p in new[] { a, b, c }) { Vertices.Add(p * Scale); Colors.Add(shaded); Uvs.Add(new Vector2(.5f, .5f)); }
            Triangles.Add(start); Triangles.Add(start + 1); Triangles.Add(start + 2);
        }
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside, Color color) { Tri(a, b, c, inside, color); Tri(a, c, d, inside, color); }
        // Elongated octahedron: tip, tail, and four equator points (±width on X, ±depth on Z) at height `y`.
        public void Diamond(Vector3 centre, float top, float bottom, float y, float width, float depth, Color color)
        {
            var t = centre + Vector3.up * top; var bt = centre + Vector3.up * bottom;
            var px = centre + new Vector3(width, y, 0); var nx = centre + new Vector3(-width, y, 0); var pz = centre + new Vector3(0, y, depth); var nz = centre + new Vector3(0, y, -depth);
            var inside = centre + Vector3.up * y;
            Tri(t, px, pz, inside, color); Tri(t, pz, nx, inside, color); Tri(t, nx, nz, inside, color); Tri(t, nz, px, inside, color);
            Tri(bt, pz, px, inside, color); Tri(bt, nx, pz, inside, color); Tri(bt, nz, nx, inside, color); Tri(bt, px, nz, inside, color);
        }
        public void Prism(float y0, float y1, float radius0, float radius1, int sides, Color color)
        {
            var inside = new Vector3(0, (y0 + y1) * .5f, 0);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides;
                var b0 = new Vector3(Mathf.Sin(a0) * radius0, y0, Mathf.Cos(a0) * radius0); var b1 = new Vector3(Mathf.Sin(a1) * radius0, y0, Mathf.Cos(a1) * radius0);
                var t0 = new Vector3(Mathf.Sin(a0) * radius1, y1, Mathf.Cos(a0) * radius1); var t1 = new Vector3(Mathf.Sin(a1) * radius1, y1, Mathf.Cos(a1) * radius1);
                Quad(b0, b1, t1, t0, inside, color);
                Tri(new Vector3(0, y1, 0), t0, t1, inside, color); Tri(new Vector3(0, y0, 0), b1, b0, inside, color);
            }
        }
        // A flat four-pointed star facing the camera (-Z) at depth z.
        public void Star(Vector3 centre, float outer, float inner, Color color)
        {
            var inside = centre + Vector3.forward * .05f;
            for (int i = 0; i < 8; i++)
            {
                float a0 = i * Mathf.PI / 4, a1 = (i + 1) * Mathf.PI / 4; float r0 = i % 2 == 0 ? outer : inner, r1 = (i + 1) % 2 == 0 ? outer : inner;
                Tri(centre, centre + new Vector3(Mathf.Sin(a0) * r0, Mathf.Cos(a0) * r0, 0), centre + new Vector3(Mathf.Sin(a1) * r1, Mathf.Cos(a1) * r1, 0), inside, color, true);
            }
        }
    }

    // A bipyramid cut flat at both tips: an equator rectangle (half sizes `width` x `depth` at y) and two flat caps (half sizes capWidth x capDepth at +-capHeight).
    static void Gem(Builder b, float width, float depth, float capHeight, float capWidth, float capDepth, Color color)
    {
        var inside = Vector3.zero;
        Vector3 E(float sx, float sz) => new Vector3(sx * width, 0, sz * depth);
        Vector3 T(float sx, float sz, float s) => new Vector3(sx * capWidth, s * capHeight, sz * capDepth);
        var corners = new[] { (1f, 1f), (-1f, 1f), (-1f, -1f), (1f, -1f) };
        for (int s = -1; s <= 1; s += 2)
        {
            for (int i = 0; i < 4; i++)
            {
                var (ax, az) = corners[i]; var (bx, bz) = corners[(i + 1) % 4];
                b.Quad(E(ax, az), E(bx, bz), T(bx, bz, s), T(ax, az, s), inside, color);
            }
            b.Quad(T(1, 1, s), T(-1, 1, s), T(-1, -1, s), T(1, -1, s), inside, color);
        }
    }

    public static Mesh BuildMesh()
    {
        var b = new Builder(); Vector3 o = Vector3.zero;
        // blade: long diamond, its lower point sunk into the collar below it
        b.Diamond(new Vector3(0, .13f, 0), .17f, -.02f, .045f, .042f, .020f, Gold);
        b.Prism(.093f, .138f, .021f, .025f, 8, Gold);                            // collar (covers the blade's lower point)
        b.Prism(-.17f, .095f, .012f, .012f, 6, Gold);                            // ONE rod through the whole spear
        Gem(b, .052f, .026f, .034f, .024f, .018f, Gold);                         // the middle gem: flat caps, so the rod enters faces
        b.Star(new Vector3(0, 0, -.0285f), .024f, .008f, Gold);                  // core star on the gem's camera side (same gold, unshaded)
        b.Prism(-.19f, -.172f, .017f, .017f, 8, Gold);                           // lower collar
        b.Diamond(new Vector3(0, -.213f, 0), .035f, -.035f, 0f, .020f, .020f, Gold);   // tail spike, its upper point sunk into the lower collar
        var mesh = new Mesh { name = "StarSpear" };
        mesh.SetVertices(b.Vertices); mesh.SetColors(b.Colors); mesh.SetUVs(0, b.Uvs); mesh.SetTriangles(b.Triangles, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    [MenuItem("Project N/Starkeeper/Build spear (mesh, material, prefab child)")]
    public static string Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Game/Entity/Enemy/Starkeeper", "Art");
        var fresh = BuildMesh();
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (mesh == null) AssetDatabase.CreateAsset(fresh, MeshPath);
        else
        {
            // Update in place with the Mesh API (Clear + Set*) so the renderer's copy is re-uploaded; CopySerialized left the preview renderer on the old data.
            mesh.Clear(); mesh.name = "StarSpear"; mesh.SetVertices(fresh.vertices); mesh.SetColors(fresh.colors); mesh.SetUVs(0, fresh.uv); mesh.SetTriangles(fresh.triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.UploadMeshData(false); Object.DestroyImmediate(fresh); EditorUtility.SetDirty(mesh);
        }
        mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            var gold = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Entity/Enemy/Starweaver/Art/Gold.mat");   // same shader and settings as the needle's material
            material = new Material(gold) { name = "StarSpear" }; AssetDatabase.CreateAsset(material, MaterialPath);
        }
        AssetDatabase.SaveAssets();
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var spear = root.transform.Find("Star spear");
            if (spear == null) { var go = new GameObject("Star spear"); go.transform.SetParent(root.transform, false); spear = go.transform; go.AddComponent<MeshFilter>(); go.AddComponent<MeshRenderer>(); }
            spear.gameObject.layer = root.layer;
            spear.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = spear.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            var presentation = root.GetComponent<PNStarkeeperPresentation>(); if (presentation == null) presentation = root.AddComponent<PNStarkeeperPresentation>();
            var so = new SerializedObject(presentation); so.FindProperty("weapon").objectReferenceValue = spear; so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return "Star spear built: " + mesh.vertexCount + " vertices, " + mesh.triangles.Length / 3 + " triangles, bounds " + mesh.bounds.size.ToString("F3");
    }
}
#endif
