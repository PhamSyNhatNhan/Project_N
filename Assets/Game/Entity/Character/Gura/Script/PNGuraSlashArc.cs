#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Rendering;

// EN: The two-layer water crescent of NamuFX `Slash_Water` (its own materials and 4x4 slash flipbooks, referenced and unchanged), laid on the exact hit sector of a strike: the outer edge is the scaled
//     hit radius and the two ends are the hit angle. The flipbooks already hold the motion (the water runs along u, then breaks up from the tail), so one frame number placed by the Action age is the
//     whole animation. The sheet is a little higher where the swing starts than where it ends, inside the hitbox's thickness; the radius on the ground does not change with that.
// VI: Luoi liem nuoc hai lop cua Slash_Water dat dung vao quat hitbox: mep ngoai = ban kinh trung, hai dau = goc trung. Frame flipbook dat theo tuoi Action.
internal sealed class PNGuraSlashArc : IDisposable
{
    // The part of a flipbook cell that holds water: u runs along the swing, v from the inner side to the outer edge (the sheets' outer edge reaches about .95).
    private const float FromU = .04f, ToU = .97f, FromV = .12f, ToV = .95f;
    private const float InnerShape = .22f;   // how far toward the centre v = 0 would reach, as a part of the radius
    private const float StartLift = .17f, EndLift = -.11f;
    private const int Columns = 56, Rows = 5, Tiles = 4;
    // Seconds from the hit -> flipbook frame. Frame 4/5 (the full crescent) falls on the hit.
    private static readonly Vector2[] BrightFrames =
        { new(-.070f, 0f), new(-.045f, 2f), new(-.020f, 3f), new(0f, 4f), new(.030f, 5f), new(.070f, 6f), new(.120f, 7f), new(.170f, 8f), new(.230f, 11f) };
    private static readonly Vector2[] DeepFrames =
        { new(-.070f, 0f), new(-.045f, 2f), new(-.020f, 4f), new(0f, 5f), new(.045f, 6f), new(.095f, 7f), new(.150f, 9f), new(.205f, 11f), new(.250f, 13f) };
    public const float Lead = .070f, Tail = .250f;
    // The start colours of the layers of the source prefab.
    private static readonly Color Bright = new(.193f, .768f, 1f);
    private static readonly Color Deep = new(.179f, .429f, .774f);
    private static readonly Color Foam = new(.599f, .898f, 1f);
    // The foam breaks of `fx/splash1`: a 3x3 flipbook whose first six cells hold a small splash inside this part of the cell.
    private const int Splashes = 11, SplashTiles = 3, SplashFrames = 6;
    private const float SplashFromU = .46f, SplashToU = .70f, SplashFromV = .40f, SplashToV = .60f;

    private readonly Layer deep, bright;
    private readonly GameObject foam;
    private readonly Mesh foamMesh;
    private readonly Vector3[] foamPoints = new Vector3[Splashes * 4];
    private readonly Vector2[] foamUv = new Vector2[Splashes * 4];
    private readonly Color[] foamColors = new Color[Splashes * 4];

    private sealed class Layer
    {
        public GameObject Node;
        public Mesh Mesh;
        public readonly Vector3[] Points = new Vector3[(Columns + 1) * (Rows + 1)];
        public readonly Vector2[] Uv = new Vector2[(Columns + 1) * (Rows + 1)];
        public readonly Color[] Colors = new Color[(Columns + 1) * (Rows + 1)];
    }

    private PNGuraSlashArc(Transform owner, Material deepMaterial, Material brightMaterial, Material foamMaterial)
    {
        deep = Build(owner, "Gura slash deep", deepMaterial, 0);
        bright = Build(owner, "Gura slash bright", brightMaterial, 1);
        foamMesh = new Mesh { name = "Gura slash foam" };
        foamMesh.MarkDynamic();
        var triangles = new int[Splashes * 12];
        for (int i = 0; i < Splashes; i++)
        {
            int a = i * 4;
            int[] quad = { a, a + 1, a + 2, a + 2, a + 1, a + 3, a, a + 2, a + 1, a + 2, a + 3, a + 1 };
            quad.CopyTo(triangles, i * 12);
        }
        foamMesh.vertices = foamPoints;
        foamMesh.triangles = triangles;
        foam = Node(owner, "Gura slash foam", foamMesh, foamMaterial, 2);
    }

    public static PNGuraSlashArc Create(Transform owner)
    {
        var pack = Resources.Load<PNGuraWaterPrefabs>("PNVfx/GuraWaterPrefabs");
        if (pack == null || pack.Slash == null) return null;
        var deepMaterial = MaterialOf(pack.Slash, "fx/slash_alp");
        var brightMaterial = MaterialOf(pack.Slash, "fx/slash_add");
        var foamMaterial = MaterialOf(pack.Slash, "fx/splash1");
        return deepMaterial == null || brightMaterial == null || foamMaterial == null ? null : new PNGuraSlashArc(owner, deepMaterial, brightMaterial, foamMaterial);
    }

    private static Material MaterialOf(GameObject prefab, string path)
    {
        var child = prefab.transform.Find(path);
        var renderer = child != null ? child.GetComponent<ParticleSystemRenderer>() : null;
        return renderer != null ? renderer.sharedMaterial : null;
    }

    // Height above the sweep's centre at a part (0..1) of the swing.
    public static float Lift(float swing) => Mathf.Lerp(StartLift, EndLift, swing);

    private static Layer Build(Transform owner, string name, Material material, int order)
    {
        var layer = new Layer { Mesh = new Mesh { name = name } };
        layer.Mesh.MarkDynamic();
        var triangles = new int[Columns * Rows * 12];
        int next = 0;
        for (int column = 0; column < Columns; column++)
            for (int row = 0; row < Rows; row++)
            {
                int a = column * (Rows + 1) + row, b = a + 1, c = a + Rows + 1, d = c + 1;
                // Both windings: the source materials cull back faces and a swing may run either way.
                foreach (int index in new[] { a, b, c, c, b, d, a, c, b, c, d, b }) triangles[next++] = index;
            }
        layer.Mesh.vertices = layer.Points;
        layer.Mesh.triangles = triangles;
        layer.Node = Node(owner, name, layer.Mesh, material, order);
        return layer;
    }

    private static GameObject Node(Transform owner, string name, Mesh mesh, Material material, int order)
    {
        var node = new GameObject(name) { layer = owner.gameObject.layer };
        node.transform.SetParent(owner, false);
        node.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = node.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = order;
        node.SetActive(false);
        return node;
    }

    // centre = the hitbox pose; forward = the aim; side = +1 / -1, the way the swing turns; since = Action age minus the hit age.
    public void Draw(Vector3 centre, Vector3 forward, float radius, float angle, float side, float since)
    {
        if (since < -Lead || since >= Tail || radius <= 0f) { Hide(); return; }
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Tail - .085f, Tail, since));
        Draw(deep, centre, forward, radius, angle, side, Frame(DeepFrames, since), Deep, fade, -.004f);
        Draw(bright, centre, forward, radius, angle, side, Frame(BrightFrames, since), Bright, fade, .004f);
        DrawFoam(centre, forward, radius, angle, side, since);
    }

    // Splashes break along the whole arc as the water passes. They are decoration and may fly past the hit sector; the water sheet and its edge are what show the hit.
    private void DrawFoam(Vector3 centre, Vector3 forward, float radius, float angle, float side, float since)
    {
        if (foam == null) return;
        var node = foam.transform;
        Color tint = Foam.linear;
        bool any = false;
        for (int i = 0; i < Splashes; i++)
        {
            // Three unrelated scatters keep timing, size and place from reading as a row of equal marks.
            float scatter = Mathf.Repeat(i * .618034f + .13f, 1f), size = Mathf.Repeat(i * .414214f + .57f, 1f), depth = Mathf.Repeat(i * .732051f + .31f, 1f);
            float swing = Mathf.Lerp(.05f, .95f, i / (Splashes - 1f)) + (depth - .5f) * .07f;
            float born = (swing - 1f) * .04f + .03f * scatter;
            float life = .12f + .1f * scatter;
            float phase = (since - born) / life;
            int a = i * 4;
            if (phase < 0f || phase >= 1f)
            {
                for (int k = 0; k < 4; k++) { foamPoints[a + k] = node.InverseTransformPoint(centre + forward * (radius * .5f)); foamColors[a + k] = Color.clear; }
                continue;
            }
            any = true;
            Vector3 outward = Quaternion.AngleAxis(side * Mathf.Lerp(-angle * .5f, angle * .5f, swing), Vector3.up) * forward;
            // Each break leans along the swing by its own amount, and flies on past the edge of the water.
            Vector3 along = Quaternion.AngleAxis(side * Mathf.Lerp(5f, 55f, scatter), Vector3.up) * outward;
            Vector3 tangent = Vector3.Cross(Vector3.up, along) * (i % 2 == 0 ? side : -side);
            float half = radius * Mathf.Lerp(.08f, .21f, size * size);
            float width = half * (SplashToV - SplashFromV) / (SplashToU - SplashFromU);
            float flown = Mathf.Sin(phase * Mathf.PI * .5f);
            float reach = radius * (.68f + .24f * depth + .36f * flown);
            Vector3 at = centre + outward * reach + tangent * (radius * .06f * flown)
                + Vector3.up * (Lift(swing) + .008f + (.12f + .14f * size) * Mathf.Sin(phase * Mathf.PI));
            int frame = Mathf.Min(SplashFrames - 1, (int)(phase * SplashFrames));
            int tileX = frame % SplashTiles, tileY = SplashTiles - 1 - frame / SplashTiles;
            for (int k = 0; k < 4; k++)
            {
                bool far = k >= 2, wide = (k & 1) != 0;
                foamPoints[a + k] = node.InverseTransformPoint(at + along * (far ? half : -half) + tangent * (wide ? width : -width));
                foamUv[a + k] = new Vector2((tileX + (far ? SplashToU : SplashFromU)) / SplashTiles, (tileY + (wide ? SplashToV : SplashFromV)) / SplashTiles);
                foamColors[a + k] = tint;
            }
        }
        foamMesh.vertices = foamPoints;
        foamMesh.uv = foamUv;
        foamMesh.colors = foamColors;
        foamMesh.RecalculateBounds();
        foam.SetActive(any);
    }

    private static int Frame(Vector2[] keys, float since)
    {
        for (int i = 1; i < keys.Length; i++)
            if (since < keys[i].x)
                return Mathf.FloorToInt(Mathf.Lerp(keys[i - 1].y, keys[i].y, Mathf.InverseLerp(keys[i - 1].x, keys[i].x, since)) + .001f);
        return Mathf.FloorToInt(keys[keys.Length - 1].y);
    }

    private static void Draw(Layer layer, Vector3 centre, Vector3 forward, float radius, float angle, float side, int frame, Color color, float alpha, float offset)
    {
        if (layer.Node == null) return;
        int tileX = frame % Tiles, tileY = Tiles - 1 - frame / Tiles;
        Color tint = color.linear; tint.a = alpha;
        float outer = InnerShape + (1f - InnerShape) * ToV;
        var node = layer.Node.transform;
        for (int column = 0; column <= Columns; column++)
        {
            float swing = column / (float)Columns;
            Vector3 outward = Quaternion.AngleAxis(side * Mathf.Lerp(-angle * .5f, angle * .5f, swing), Vector3.up) * forward;
            float height = Lift(swing) + offset;
            for (int row = 0; row <= Rows; row++)
            {
                float v = Mathf.Lerp(FromV, ToV, row / (float)Rows);
                float reach = radius * (InnerShape + (1f - InnerShape) * v) / outer;
                int index = column * (Rows + 1) + row;
                layer.Points[index] = node.InverseTransformPoint(centre + outward * reach + Vector3.up * height);
                layer.Uv[index] = new Vector2((tileX + Mathf.Lerp(FromU, ToU, swing)) / Tiles, (tileY + v) / Tiles);
                layer.Colors[index] = tint;
            }
        }
        layer.Mesh.vertices = layer.Points;
        layer.Mesh.uv = layer.Uv;
        layer.Mesh.colors = layer.Colors;
        layer.Mesh.RecalculateBounds();
        layer.Node.SetActive(true);
    }

    public void Hide()
    {
        if (deep.Node != null) deep.Node.SetActive(false);
        if (bright.Node != null) bright.Node.SetActive(false);
        if (foam != null) foam.SetActive(false);
    }

    private bool disposed;
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Release(deep.Node, deep.Mesh);
        Release(bright.Node, bright.Mesh);
        Release(foam, foamMesh);
    }

    private static void Release(GameObject node, Mesh mesh)
    {
        if (node != null) node.SetActive(false);
        if (Application.isPlaying) { UnityEngine.Object.Destroy(mesh); UnityEngine.Object.Destroy(node); }
        else { UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(node); }
    }
}
#endif
