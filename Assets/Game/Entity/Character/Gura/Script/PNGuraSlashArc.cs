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
    // Attacks 1-2 lie on a plane tilted about the aim: one side of the sector high, the other low. The two strikes turn opposite ways, so their planes cross.
    // The plane is raised so its low side stays above the floor; only the height leaves the hit slab, the ground footprint stays the sector.
    private const float TiltRaise = .28f;
    private static readonly float TiltSlope = Mathf.Tan(18f * Mathf.Deg2Rad);
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
    private Color brightTint = Bright, deepTint = Deep, foamTint = Foam;
    private float opacity = 1f;
    private readonly bool finisher;
    private readonly float clockScale;
    private readonly GameObject foam;
    private readonly Mesh foamMesh;
    private readonly Vector3[] foamPoints = new Vector3[Splashes * 4];
    private readonly Vector2[] foamUv = new Vector2[Splashes * 4];
    private readonly Color[] foamColors = new Color[Splashes * 4];

    private sealed class Layer
    {
        public GameObject Node;
        public Mesh Mesh;
        public MeshRenderer Renderer;
        public MaterialPropertyBlock Properties;
        public ParticleSystem.MinMaxGradient Tint;
        public readonly Vector3[] Points = new Vector3[(Columns + 1) * (Rows + 1)];
        public readonly Vector2[] Uv = new Vector2[(Columns + 1) * (Rows + 1)];
        public readonly Color[] Colors = new Color[(Columns + 1) * (Rows + 1)];
    }

    private PNGuraSlashArc(Transform owner, ParticleSystem deepSource, ParticleSystem brightSource, Material foamMaterial, bool finisher, float clockScale)
    {
        this.finisher = finisher;
        this.clockScale = clockScale;
        deep = Build(owner, "Gura slash deep", deepSource.GetComponent<ParticleSystemRenderer>().sharedMaterial, 0);
        bright = Build(owner, "Gura slash bright", brightSource.GetComponent<ParticleSystemRenderer>().sharedMaterial, 1);
        if (finisher)
        {
            deep.Properties = new MaterialPropertyBlock();
            bright.Properties = new MaterialPropertyBlock();
            deep.Tint = deepSource.colorOverLifetime.color;
            bright.Tint = brightSource.colorOverLifetime.color;
        }
        if (foamMaterial == null) return;
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

    public static PNGuraSlashArc Create(Transform owner, bool finisher, float clockScale)
    {
        var pack = Resources.Load<PNGuraWaterPrefabs>("PNVfx/GuraWaterPrefabs");
        if (pack == null) return null;
        var prefab = finisher ? pack.FinisherSlash : pack.Slash;
        if (prefab == null) return null;
        var deepSource = FindSystem(prefab, "fx/slash_alp");
        var brightSource = FindSystem(prefab, "fx/slash_add");
        if (deepSource == null || brightSource == null) return null;
        return new PNGuraSlashArc(owner, deepSource, brightSource,
            pack.Slash != null ? MaterialOf(pack.Slash, "fx/splash1") : null, finisher, clockScale);
    }

    private static ParticleSystem FindSystem(GameObject prefab, string path)
    {
        var child = prefab.transform.Find(path);
        return child != null ? child.GetComponent<ParticleSystem>() : null;
    }

    private static Material MaterialOf(GameObject prefab, string path)
    {
        var child = prefab.transform.Find(path);
        var renderer = child != null ? child.GetComponent<ParticleSystemRenderer>() : null;
        return renderer != null ? renderer.sharedMaterial : null;
    }

    // Height above the sweep's centre at a part (0..1) of the swing, `reach` metres out. High where the swing starts, low where it ends, whichever way it turns.
    public static float TiltLift(float swing, float reach, float angle) =>
        TiltRaise - TiltSlope * reach * Mathf.Sin(Mathf.Lerp(-angle * .5f, angle * .5f, swing) * Mathf.Deg2Rad);

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
        layer.Renderer = layer.Node.GetComponent<MeshRenderer>();
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
    public void Draw(Vector3 centre, Vector3 forward, float radius, float innerRadius, float angle, float side, float since, Func<float, float, float> lift)
    {
        since *= clockScale;
        if (since < -Lead || since >= Tail || radius <= 0f) { Hide(); return; }
        var pose = new Sweep(centre, forward, radius, innerRadius, angle, side, lift);
        if (finisher)
        {
            // The original waterslash materials use a continuous UV strip and a dissolve mask.
            float swept = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-Lead, 0f, since));
            float wake = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.018f, Tail, since));
            float opacity = Mathf.InverseLerp(-Lead, -.004f, since) * (1f - wake);
            Draw(deep, pose, -1, deep.Tint.Evaluate(.12f + wake * .56f), opacity, -.004f, swept, wake);
            Draw(bright, pose, -1, bright.Tint.Evaluate(.18f + wake * .60f), opacity * .86f, .004f, swept, wake);
            DrawFoam(centre, forward, radius, angle, side, since - .022f, lift);
            return;
        }
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Tail - .085f, Tail, since));
        Draw(deep, pose, Frame(DeepFrames, since), deepTint, fade * opacity, -.004f);
        Draw(bright, pose, Frame(BrightFrames, since), brightTint, fade * opacity, .004f);
        DrawFoam(centre, forward, radius, angle, side, since, lift);
    }

    // A straight cut instead of a crescent: the same two flipbook layers on a line, thick in the middle and pointed at both ends. `since` places the flipbook, `swept` (0..1) how much of the
    // line is drawn, `born` is the real time from the hit for the splashes that break off it.
    public void DrawLine(Vector3 from, Vector3 to, float width, float since, float swept, float alpha, float born, Vector3? side = null)
    {
        if (swept <= .002f || alpha <= .001f || since >= Tail) { Hide(); return; }
        float held = Mathf.Max(0f, since);
        float fade = alpha * opacity * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Tail - .085f, Tail, since)));
        // The way its width spreads: flat on the floor unless a steep line asks to face the camera.
        Vector3 wide = side ?? Vector3.Cross(Vector3.up, to - from).normalized;
        Line(deep, from, to, wide, width, swept, Frame(DeepFrames, held), deepTint, fade, -.004f);
        Line(bright, from, to, wide, width * .82f, swept, Frame(BrightFrames, held), brightTint, fade, .004f);
        LineFoam(from, to, width, swept, born);
    }

    private static void Line(Layer layer, Vector3 from, Vector3 to, Vector3 across, float width, float swept, int frame, Color color, float alpha, float offset)
    {
        if (layer.Node == null) return;
        int tileX = frame % Tiles, tileY = Tiles - 1 - frame / Tiles;
        Color tint = color.linear; tint.a *= alpha;
        var node = layer.Node.transform;
        for (int column = 0; column <= Columns; column++)
        {
            float u = column / (float)Columns;
            float along = u * Mathf.Clamp01(swept);
            Vector3 centre = Vector3.Lerp(from, to, along);
            // Pointed at both ends of what is drawn.
            float taper = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * u)), .75f);
            for (int row = 0; row <= Rows; row++)
            {
                float radial = row / (float)Rows;
                int index = column * (Rows + 1) + row;
                layer.Points[index] = node.InverseTransformPoint(centre + across * (width * taper * (radial - .62f)) + Vector3.up * offset);
                layer.Uv[index] = new Vector2((tileX + Mathf.Lerp(FromU, ToU, along)) / Tiles, (tileY + Mathf.Lerp(FromV, ToV, radial)) / Tiles);
                layer.Colors[index] = tint;
            }
        }
        layer.Mesh.vertices = layer.Points;
        layer.Mesh.uv = layer.Uv;
        layer.Mesh.colors = layer.Colors;
        layer.Mesh.RecalculateBounds();
        layer.Node.SetActive(true);
    }

    private void LineFoam(Vector3 from, Vector3 to, float width, float swept, float since)
    {
        if (foam == null) return;
        var node = foam.transform;
        Color tint = Foam.linear;
        Vector3 along = (to - from).normalized, across = Vector3.Cross(Vector3.up, along);
        float length = Vector3.Distance(from, to);
        bool any = false;
        for (int i = 0; i < Splashes; i++)
        {
            float scatter = Mathf.Repeat(i * .618034f + .13f, 1f), size = Mathf.Repeat(i * .414214f + .57f, 1f), depth = Mathf.Repeat(i * .732051f + .31f, 1f);
            float at01 = Mathf.Lerp(.12f, .92f, i / (Splashes - 1f)) + (depth - .5f) * .07f;
            float phase = (since - (at01 - .75f) * .05f - .025f * scatter) / (.13f + .11f * scatter);
            int a = i * 4;
            if (phase < 0f || phase >= 1f || at01 > swept)
            {
                for (int k = 0; k < 4; k++) { foamPoints[a + k] = node.InverseTransformPoint(from); foamColors[a + k] = Color.clear; }
                continue;
            }
            any = true;
            float side = i % 2 == 0 ? 1f : -1f;
            Vector3 lean = Quaternion.AngleAxis(side * Mathf.Lerp(14f, 62f, scatter), Vector3.up) * along;
            Vector3 tangent = Vector3.Cross(Vector3.up, lean) * side;
            float half = length * Mathf.Lerp(.04f, .095f, size * size);
            float wide = half * (SplashToV - SplashFromV) / (SplashToU - SplashFromU);
            float flown = Mathf.Sin(phase * Mathf.PI * .5f);
            Vector3 at = Vector3.Lerp(from, to, at01) + across * (side * width * (.12f + .55f * depth * flown))
                + Vector3.up * (.008f + (.07f + .1f * size) * Mathf.Sin(phase * Mathf.PI));
            int frame = Mathf.Min(SplashFrames - 1, (int)(phase * SplashFrames));
            int tileX = frame % SplashTiles, tileY = SplashTiles - 1 - frame / SplashTiles;
            for (int k = 0; k < 4; k++)
            {
                bool far = k >= 2, broad = (k & 1) != 0;
                foamPoints[a + k] = node.InverseTransformPoint(at + lean * (far ? half : -half) + tangent * (broad ? wide : -wide));
                foamUv[a + k] = new Vector2((tileX + (far ? SplashToU : SplashFromU)) / SplashTiles, (tileY + (broad ? SplashToV : SplashFromV)) / SplashTiles);
                foamColors[a + k] = tint;
            }
        }
        foamMesh.vertices = foamPoints;
        foamMesh.uv = foamUv;
        foamMesh.colors = foamColors;
        foamMesh.RecalculateBounds();
        foam.SetActive(any);
    }

    private readonly struct Sweep
    {
        public readonly Vector3 Centre, Forward;
        public readonly float Radius, InnerRadius, Angle, Side;
        public readonly Func<float, float, float> Lift;

        public Sweep(Vector3 centre, Vector3 forward, float radius, float innerRadius, float angle, float side, Func<float, float, float> lift)
        {
            Centre = centre;
            Forward = forward;
            Radius = radius;
            InnerRadius = innerRadius;
            Angle = angle;
            Side = side;
            Lift = lift;
        }
    }

    // Splashes break along the whole arc as the water passes. They are decoration and may fly past the hit sector; the water sheet and its edge are what show the hit.
    private void DrawFoam(Vector3 centre, Vector3 forward, float radius, float angle, float side, float since, Func<float, float, float> lift)
    {
        if (foam == null) return;
        var node = foam.transform;
        Color tint = foamTint.linear;
        tint.a *= opacity;
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
            float half = radius * Mathf.Lerp(.08f, finisher ? .15f : .21f, size * size);
            float width = half * (SplashToV - SplashFromV) / (SplashToU - SplashFromU);
            float flown = Mathf.Sin(phase * Mathf.PI * .5f);
            float reach = radius * (finisher ? .86f + .11f * depth + .16f * flown : .68f + .24f * depth + .36f * flown);
            Vector3 at = centre + outward * reach + tangent * (radius * .06f * flown)
                + Vector3.up * (lift(swing, Mathf.Min(reach, radius)) + .008f + (.12f + .14f * size) * Mathf.Sin(phase * Mathf.PI));
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

    private static void Draw(Layer layer, in Sweep pose, int frame, Color color, float alpha, float offset, float swept = 1f, float dissolve = 0f)
    {
        if (layer.Node == null) return;
        bool strip = frame < 0;
        int tileX = frame % Tiles, tileY = Tiles - 1 - frame / Tiles;
        Color tint = color.linear; tint.a *= alpha;
        float outer = InnerShape + (1f - InnerShape) * ToV;
        var node = layer.Node.transform;
        for (int column = 0; column <= Columns; column++)
        {
            float u = column / (float)Columns;
            float swing = u * swept;
            Vector3 outward = Quaternion.AngleAxis(pose.Side * Mathf.Lerp(-pose.Angle * .5f, pose.Angle * .5f, swing), Vector3.up) * pose.Forward;
            Color columnTint = tint;
            if (strip) columnTint.a *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Mathf.Min(u, 1f - u) * 12f));
            for (int row = 0; row <= Rows; row++)
            {
                float radial = row / (float)Rows;
                float v = Mathf.Lerp(FromV, ToV, radial);
                float reach = Mathf.Lerp(pose.InnerRadius, pose.Radius,
                    strip ? radial : (InnerShape + (1f - InnerShape) * v) / outer);
                int index = column * (Rows + 1) + row;
                layer.Points[index] = node.InverseTransformPoint(pose.Centre + outward * reach + Vector3.up * (pose.Lift(swing, reach) + offset));
                layer.Uv[index] = strip ? new Vector2(u, 1f - radial) : new Vector2((tileX + Mathf.Lerp(FromU, ToU, swing)) / Tiles, (tileY + v) / Tiles);
                layer.Colors[index] = columnTint;
            }
        }
        layer.Mesh.vertices = layer.Points;
        layer.Mesh.uv = layer.Uv;
        layer.Mesh.colors = layer.Colors;
        layer.Mesh.RecalculateBounds();
        if (strip)
        {
            layer.Properties.SetFloat(CutProperty, Mathf.Lerp(.19f, .88f, dissolve));
            layer.Renderer.SetPropertyBlock(layer.Properties);
        }
        layer.Node.SetActive(true);
    }

    private static readonly int CutProperty = Shader.PropertyToID("_Cut");

    public void Hide()
    {
        if (deep.Node != null) deep.Node.SetActive(false);
        if (bright.Node != null) bright.Node.SetActive(false);
        if (foam != null) foam.SetActive(false);
    }

    // A water shadow's cut: the same shapes, thinner and paler than her own.
    public PNGuraSlashArc AsShadow(float strength)
    {
        brightTint = new Color(.62f, .83f, .92f);
        deepTint = new Color(.47f, .62f, .76f);
        foamTint = new Color(.84f, .93f, .98f);
        opacity = strength;
        return this;
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
