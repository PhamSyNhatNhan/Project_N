#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// One cast owns every drawable. Action age drives both shader flow and star particles.
[DefaultExecutionOrder(250)]
public sealed class PNStarkeeperSweepVisual : MonoBehaviour, IPNCameraFacing, IDisposable
{
    private const int Segments = 40, ParticleCount = 64, LayerCount = 5;
    private sealed class Stroke
    {
        public float Birth = -1; public Vector3 Origin, Aim, StartDirection, LastDirection, Normal; public int Index;
        public Vector3 InnerAnchor; public float SweepAngle; public bool Tracking; public int ShedCount;
        public readonly Mesh[] Meshes = new Mesh[LayerCount];
        public readonly Material[] Materials = new Material[LayerCount];
        public readonly Vector3[][] Vertices = { new Vector3[(Segments + 1) * 2], new Vector3[(Segments + 1) * 2], new Vector3[(Segments + 1) * 2], new Vector3[(Segments + 1) * 2], new Vector3[(Segments + 1) * 2] };
        public readonly Color[][] Colors = { new Color[(Segments + 1) * 2], new Color[(Segments + 1) * 2], new Color[(Segments + 1) * 2], new Color[(Segments + 1) * 2], new Color[(Segments + 1) * 2] };
    }
    private struct Spark { public float Birth, Life, Size; public Vector3 Origin, Velocity; }
    private readonly List<Material> materials = new List<Material>();
    private readonly List<Mesh> meshes = new List<Mesh>();
    private readonly Stroke[] strokes = { new Stroke(), new Stroke() };
    private readonly Spark[] sparks = new Spark[ParticleCount];
    private readonly Vector3[] particleVertices = new Vector3[ParticleCount * 4];
    private readonly Color[] particleColors = new Color[ParticleCount * 4];
    private Transform source, spear, glow; private PNStarkeeperPresentation presentation;
    private LineRenderer edge; private Mesh particleMesh; private Material glowMaterial, edgeMaterial;
    private PNStarkeeperWeaponGlow fullWeaponGlow;
    private Camera preview; private float age, windup, gap, radius, angle; private Vector3 aim;
    private int nextSpark; private bool disposed;
    public float VisualAge => age;
    public static PNStarkeeperSweepVisual Create(Transform source, PNStarkeeperPresentation presentation,
        float windup, float gap, float radius, float angle, Vector3 aim, Camera preview = null)
    {
        var root = new GameObject("Starkeeper Quet Sao VFX"); SceneManager.MoveGameObjectToScene(root, source.gameObject.scene);
        var v = root.AddComponent<PNStarkeeperSweepVisual>();
        v.source = source; v.presentation = presentation; v.spear = source.Find("Star spear");
        v.windup = windup; v.gap = gap; v.radius = radius; v.angle = angle; v.aim = aim; v.preview = preview;
        try
        {
            v.Build(); v.SetTime(0);
            var identity = source.GetComponent<EveEngine.EveEntityIdentity>();
            if (identity != null) PNWorldPresentationRegistration.Register(identity.World, root);
            return v;
        }
        catch { v.Dispose(); throw; }
    }
    private Material Make(int mode, string texture, Color tint, bool additive = false)
    {
        var shader = Resources.Load<Shader>("PNStarweaverEffects");
        if (shader == null) throw new InvalidOperationException("Star effect shader is missing.");
        var m = new Material(shader) { name = "Starkeeper sweep " + mode, hideFlags = HideFlags.HideAndDontSave };
        materials.Add(m); m.SetFloat("_Mode", mode); m.SetFloat("_DstBlend", additive ? 1 : 10);
        m.SetColor("_Tint", tint); m.SetTexture("_NoiseTex", Resources.Load<Texture2D>("StarweaverVfx/Noise"));
        if (texture != null) m.SetTexture("_MainTex", Resources.Load<Texture2D>("StarweaverVfx/" + texture));
        return m;
    }
    private Mesh Mesh(string name, int vertices, int[] triangles, Vector2[] uv)
    {
        var mesh = new Mesh { name = "Starkeeper sweep " + name, hideFlags = HideFlags.HideAndDontSave };
        mesh.MarkDynamic(); mesh.vertices = new Vector3[vertices]; mesh.uv = uv; mesh.triangles = triangles; meshes.Add(mesh); return mesh;
    }
    private Transform Draw(string name, Mesh mesh, Material material)
    {
        var go = new GameObject(name); go.layer = source.gameObject.layer; go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false; return go.transform;
    }
    private void Build()
    {
        var slashMask = Resources.Load<Texture2D>("StarkeeperVfx/SlashMask");
        var slashDissolve = Resources.Load<Texture2D>("StarkeeperVfx/SlashDissolve");
        var flow = Resources.Load<Texture2D>("StarkeeperVfx/SlashFlow");
        var grain = Resources.Load<Texture2D>("StarkeeperVfx/SlashGrain");
        if (slashMask == null || slashDissolve == null || flow == null || grain == null) throw new InvalidOperationException("Starkeeper slash masks are missing.");
        glowMaterial = Make(5, "Glow", new Color(.65f, .2f, 1.2f, 1), true);
        edgeMaterial = Make(5, "Ribbon", new Color(.95f, .2f, 1.5f, 1), true);
        if(spear!=null) fullWeaponGlow=new PNStarkeeperWeaponGlow(transform,spear,"Starkeeper sweep ");
        var stars = Make(1, null, new Color(1.35f, .88f, .25f, 1), true);
        var uv = new Vector2[(Segments + 1) * 2]; var triangles = new int[Segments * 6];
        for (int i = 0; i <= Segments; i++)
        {
            uv[i * 2] = new Vector2(i / (float)Segments, 0); uv[i * 2 + 1] = new Vector2(i / (float)Segments, 1);
            if (i == Segments) continue; int k = i * 2, t = i * 6;
            triangles[t] = k; triangles[t + 1] = k + 1; triangles[t + 2] = k + 2;
            triangles[t + 3] = k + 1; triangles[t + 4] = k + 3; triangles[t + 5] = k + 2;
        }
        foreach (var stroke in strokes) for (int i = 0; i < LayerCount; i++)
        {
            var material = Make(14, null, Color.white, i == 2 || i == 3);
            material.renderQueue = 3000 + (i == 4 ? 0 : i + 1);
            material.SetTexture("_SlashFlowTex", flow); material.SetTexture("_SlashGrainTex", grain);
            material.SetTexture("_MainTex", slashMask); material.SetTexture("_SlashDissolveTex", slashDissolve); material.SetFloat("_SlashLayer", i);
            stroke.Materials[i] = material;
            stroke.Meshes[i] = Mesh("ribbon", uv.Length, triangles, uv);
            Draw("Retained slash " + i, stroke.Meshes[i], material);
        }
        var quadUV = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }; var quadTriangles = new[] { 0, 1, 2, 0, 2, 3 };
        var quad = Mesh("glow quad", 4, quadTriangles, quadUV);
        quad.vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0), new Vector3(.5f,.5f,0), new Vector3(-.5f,.5f,0) };
        quad.colors = new[] { Color.white, Color.white, Color.white, Color.white }; quad.RecalculateBounds();
        glow = Draw("Spear charge", quad, glowMaterial);
        var line = new GameObject("Spear gold edge"); line.layer = source.gameObject.layer; line.transform.SetParent(transform, false);
        edge = line.AddComponent<LineRenderer>(); edge.sharedMaterial = edgeMaterial; edge.useWorldSpace = true; edge.positionCount = 2; edge.widthMultiplier = .009f;
        var puv = new Vector2[ParticleCount * 4]; var pt = new int[ParticleCount * 6];
        for (int i = 0; i < ParticleCount; i++)
        {
            sparks[i].Birth = -10;
            for (int j = 0; j < 4; j++) puv[i * 4 + j] = quadUV[j];
            for (int j = 0; j < 6; j++) pt[i * 6 + j] = i * 4 + quadTriangles[j];
        }
        particleMesh = Mesh("star particles", puv.Length, pt, puv); Draw("Gold star particles", particleMesh, stars);
        if (Application.isPlaying) PNCameraFacingSystem.Register(this);
    }
    public void SetTime(float seconds)
    {
        if (disposed) return; age = seconds;
        foreach (var m in materials) m.SetFloat("_FxTime", age);
        Align();
    }
    public void Emit(int index, Vector3 position, Vector3 direction)
    {
        if (disposed || index < 0 || index > 1) return;
        var s = strokes[index]; if (s.Birth >= 0) return;
        presentation?.TickWeapon(0);
        s.Birth = age; s.Origin = position + Vector3.up * .3f; s.InnerAnchor = spear != null ? spear.position : s.Origin;
        s.Aim = direction.normalized; s.Index = index; s.Tracking = true;
        s.Normal = Vector3.up;
        float sign = index == 0 ? 1 : -1;
        s.StartDirection = s.LastDirection = Quaternion.AngleAxis(-sign * angle * .5f, s.Normal) * s.Aim;
        aim = direction;
        for (int i = 0; i < 4; i++)
        {
            var ray = Quaternion.AngleAxis(i * 30 - 45, s.Normal) * s.StartDirection;
            AddSpark(presentation != null ? presentation.TipPosition : s.Origin + s.StartDirection * .28f, ray * (.35f + (i % 3) * .1f), .2f, .02f + (i % 3) * .008f);
        }
        Align();
    }
    public void Hit(Vector3 point)
    {
        if (disposed) return;
        for (int i = 0; i < 6; i++)
        {
            float a = i * 2.399963f;
            AddSpark(point + Vector3.up * .24f, new Vector3(Mathf.Cos(a), .4f, Mathf.Sin(a)) * .7f, .2f, i == 0 ? .13f : .035f);
        }
    }
    private void AddSpark(Vector3 origin, Vector3 velocity, float life, float size)
    { sparks[nextSpark] = new Spark { Birth = age, Life = life, Size = size, Origin = origin, Velocity = velocity }; nextSpark = (nextSpark + 1) % ParticleCount; }
    private void RenderStroke(Stroke s)
    {
        float elapsed = age - s.Birth; bool active = s.Birth >= 0 && elapsed >= 0 && elapsed < .36f;
        if (s.Tracking && elapsed >= 0)
        {
            // Same planar sector, origin, radius and direction as the registered damage cast.
            if(spear != null) s.InnerAnchor = spear.position;
            float progress = Mathf.Clamp01(elapsed / .12f);
            s.SweepAngle = (s.Index == 0 ? 1 : -1) * angle * (1-Mathf.Pow(1-progress,3));
            if (progress >= 1) s.Tracking = false;
        }
        // Emit only once per Action-age threshold; held time cannot shed additional particles.
        while (active && s.ShedCount < 4 && elapsed >= .025f + s.ShedCount * .025f)
        {
            int k = s.ShedCount++;
            var ray = Quaternion.AngleAxis(s.SweepAngle * (.68f + k * .07f), s.Normal) * s.StartDirection;
            float outer = radius;
            var tangent = Vector3.Cross(s.Normal, ray) * (s.SweepAngle < 0 ? -1 : 1);
            AddSpark(s.Origin + ray * outer, ray * (.18f + k * .06f) + tangent * .34f,
                .18f + k * .025f, k == 1 ? .032f : .019f);
        }
        for (int layer = 0; layer < LayerCount; layer++)
        {
            s.Materials[layer].SetFloat("_SlashPhase", Mathf.Clamp01(elapsed / .36f));
            float outer = radius;
            // All layers keep the registered outer radius; cosmetic echo is inset.
            if (layer == 4) outer *= .94f;
            float inner = .035f; // Tight attachment around the animated shaft; outer arc still matches damage.
            for (int i = 0; i <= Segments; i++)
            {
                float u = i / (float)Segments;
                var ray = Quaternion.AngleAxis((layer == 4 ? u * .94f + .03f : u) * s.SweepAngle, s.Normal) * s.StartDirection;
                var centre = s.Origin - s.Normal * (layer * .0015f);
                s.Vertices[layer][i * 2] = s.InnerAnchor - s.Normal * (layer * .0015f) + ray * inner;
                s.Vertices[layer][i * 2 + 1] = centre + ray * outer;
                s.Colors[layer][i * 2] = s.Colors[layer][i * 2 + 1] = new Color(1, 1, 1, active ? 1 : 0);
            }
            s.Meshes[layer].vertices = s.Vertices[layer]; s.Meshes[layer].colors = s.Colors[layer]; s.Meshes[layer].RecalculateBounds();
        }
    }
    public bool Align()
    {
        if (disposed || source == null) return false;
        var camera = PNCameraFacing.ResolveCamera(preview, Application.isPlaying); if (camera == null) return false;
        foreach (var stroke in strokes) RenderStroke(stroke);
        var right = camera.transform.right; var up = camera.transform.up;
        var tip = presentation != null ? presentation.TipPosition : source.position + Vector3.up * .3f;
        float charge = age < windup ? Mathf.Clamp01(age / windup) : 0;
        float flash = 0; foreach (var s in strokes) if (s.Birth >= 0 && age >= s.Birth) flash = Mathf.Max(flash, Mathf.Clamp01(1 - (age - s.Birth) / .1f));
        float slashGlow=0;
        foreach(var s in strokes) if(s.Birth>=0 && age>=s.Birth) slashGlow=Mathf.Max(slashGlow,Mathf.Clamp01(1-(age-s.Birth)/.18f));
        fullWeaponGlow?.Set(slashGlow,age,camera);
        glow.SetPositionAndRotation(tip, Quaternion.LookRotation(camera.transform.forward, up)); glow.localScale = Vector3.one * (.04f + charge * .07f + flash * .08f);
        glowMaterial.SetFloat("_Opacity", charge * .5f + flash * .8f);
        edge.SetPosition(0, spear != null ? spear.TransformPoint(new Vector3(0, .06f, 0)) : tip - aim * .16f); edge.SetPosition(1, tip);
        edgeMaterial.SetFloat("_Opacity", charge * .7f + flash); edge.enabled = charge > 0 || flash > 0;
        for (int i = 0; i < ParticleCount; i++)
        {
            var spark = sparks[i]; float t = age - spark.Birth; float alpha = t >= 0 && t < spark.Life ? 1 - t / spark.Life : 0;
            Vector3 point = spark.Origin + spark.Velocity * t; float size = spark.Size * Mathf.Sqrt(alpha);
            if (i < 8 && age < windup)
            {
                float p = Mathf.Repeat(age * 2.2f + i / 8f, 1), a = i * 2.399963f;
                point = tip + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * ((1 - p) * .12f);
                alpha = Mathf.Sin(p * Mathf.PI) * charge; size = .018f * alpha;
            }
            for (int j = 0; j < 4; j++)
            {
                float x = j == 0 || j == 3 ? -.5f : .5f, y = j < 2 ? -.5f : .5f;
                particleVertices[i * 4 + j] = point + (right * x + up * y) * size; particleColors[i * 4 + j] = new Color(1, 1, 1, alpha);
            }
        }
        particleMesh.vertices = particleVertices; particleMesh.colors = particleColors; particleMesh.RecalculateBounds(); return true;
    }
    private void LateUpdate() { if (source == null || !source.gameObject.activeInHierarchy) Dispose(); else Align(); }
    private void Release()
    {
        if (disposed) return; disposed = true; PNCameraFacingSystem.Unregister(this);
        fullWeaponGlow?.Dispose();fullWeaponGlow=null;
        foreach (var m in materials) DestroyResource(m); foreach (var m in meshes) DestroyResource(m); materials.Clear(); meshes.Clear();
    }
    public void Dispose() { if (disposed) return; Release(); gameObject.SetActive(false); DestroyResource(gameObject); }
    private void OnDestroy() { Release(); }
    private static void DestroyResource(UnityEngine.Object value) { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
}
#endif
