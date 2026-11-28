using System.Collections.Generic;
using EveEngine;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
using Object = UnityEngine.Object;
#endif

// EN: Gura's eyes while her Burst is on: the eyes become lamps with a flare, gone with her puppet. A registered look
//     (`project_n:gura_burst_eyes`, named by her `stateEffects`); the blue streaks of her hair are a content glow (`project_n:gura_burst_hair`) beside it. Display only.
// VI: Mat Gura trong Burst: phat sang co quang. La mot look; vet xanh tren toc la glow khai bang JSON.
public static class PNGuraBurstEyeLooks
{
    public static readonly EveId Id = EveId.Parse("project_n:gura_burst_eyes");

    public static PNLook Create(PNLookBuild build)
    {
#if UNITY_5_3_OR_NEWER
        return build.Skill != null ? new PNGuraBurstEyeLook(build.Skill) : null;
#else
        return null;
#endif
    }
}

#if UNITY_5_3_OR_NEWER
internal sealed class PNGuraBurstEyeLook : PNLook
{
    private PNGuraBurstEyesVisual visual;

    public PNGuraBurstEyeLook(PNSkill skill)
    {
        var node = new GameObject("Gura Burst Eyes") { layer = skill.gameObject.layer };
        node.transform.SetParent(skill.transform, false);
        visual = node.AddComponent<PNGuraBurstEyesVisual>();
        visual.Bind(skill);
    }

    public override bool HasTail => true;

    public override void End(PNActionViewEnd kind)
    {
        if (visual == null) return;
        if (kind == PNActionViewEnd.Cancelled) { visual.Remove(); visual = null; }
        else visual.Release();
    }
}

public sealed class PNGuraBurstEyesVisual : MonoBehaviour
{
    private const float RiseSeconds = .35f, ReleaseSeconds = .5f;
    private static readonly string[] EyeSlots = { "eyeN", "eyeF" };

    private Transform owner;
    private EveTimeScale clock;
    private PNVisualBody body;
    private PNGuraBurstGlow glow;
    private float age, releasedFor = -1f;

    public bool Releasing => releasedFor >= 0f;

    public void Bind(PNSkill target)
    {
        owner = target.transform;
        clock = owner.GetComponent<EveTimeScale>();
        body = owner.GetComponentInChildren<PNVisualBody>();
        glow = new PNGuraBurstGlow(transform);
        Tick(0f);
    }

    public void Release()
    {
        if (releasedFor < 0f) releasedFor = 0f;
    }

    public void Remove()
    {
        Clear();
        if (this == null) return;
        if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
    }

    private void LateUpdate() => Tick(clock != null && clock.isActiveAndEnabled ? clock.DeltaTime : Time.deltaTime);

    public void Tick(float delta)
    {
        if (glow == null || owner == null) return;
        age += delta;
        if (releasedFor >= 0f) releasedFor += delta;
        if (releasedFor >= ReleaseSeconds) { Remove(); return; }

        float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / RiseSeconds));
        float sink = releasedFor < 0f ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(releasedFor / ReleaseSeconds));

        if (body == null) body = owner.GetComponentInChildren<PNVisualBody>();
        var actor = body != null ? body.Actor : null;

        // The light goes with her puppet; away with it too.
        if (actor == null || actor.Renderer == null || !actor.Renderer.enabled || actor.Mesh == null) { glow.Hide(); return; }
        glow.Draw(actor, age, rise * (1f - sink));
    }

    private void OnDestroy() => Clear();

    private void Clear()
    {
        glow?.Dispose(); glow = null;
    }

    // What shines on the eyes: the lit eye itself and its flare.
    private sealed class PNGuraBurstGlow
    {
        private static readonly int MainTex = Shader.PropertyToID("_MainTex"), Opacity = Shader.PropertyToID("_Opacity"), Mode = Shader.PropertyToID("_Mode");

        private sealed class Sheet
        {
            public GameObject Node;
            public MeshRenderer Renderer;
            public MeshFilter Filter;
            public Mesh Mesh;
            public readonly MaterialPropertyBlock Block = new();
            public readonly List<Vector3> Vertices = new();
            public readonly List<Vector2> Uv = new();
            public readonly List<Vector4> Bounds = new();
            public readonly List<Color> Colors = new();
            public readonly List<int> Triangles = new();

            public void Clear() { Vertices.Clear(); Uv.Clear(); Bounds.Clear(); Colors.Clear(); Triangles.Clear(); }

            // `first` is the strength of corners a and b, `last` of c and d.
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector4 bounds, float first, float last)
            {
                int n = Vertices.Count;
                Vertices.Add(a); Vertices.Add(b); Vertices.Add(c); Vertices.Add(d);
                Uv.Add(ua); Uv.Add(ub); Uv.Add(uc); Uv.Add(ud);
                for (int i = 0; i < 4; i++) Bounds.Add(bounds);
                Colors.Add(new Color(1f, 1f, 1f, first)); Colors.Add(new Color(1f, 1f, 1f, first)); Colors.Add(new Color(1f, 1f, 1f, last)); Colors.Add(new Color(1f, 1f, 1f, last));
                Triangles.Add(n); Triangles.Add(n + 1); Triangles.Add(n + 2); Triangles.Add(n); Triangles.Add(n + 2); Triangles.Add(n + 3);
            }

            public void Write()
            {
                Mesh.Clear();
                Mesh.SetVertices(Vertices); Mesh.SetUVs(0, Uv); Mesh.SetUVs(2, Bounds); Mesh.SetColors(Colors); Mesh.SetTriangles(Triangles, 0);
            }
        }

        private readonly Material material;
        private readonly Sheet lamps, flare;
        private readonly Sheet[] sheets;
        private readonly Vector3[] corners = new Vector3[4];
        private readonly Vector2[] uvs = new Vector2[4];

        public PNGuraBurstGlow(Transform host)
        {
            var shader = Resources.Load<Shader>("PNVfx/GuraGlow");
            if (shader == null) return;
            material = new Material(shader) { name = "Gura Burst glow", hideFlags = HideFlags.HideAndDontSave };
            lamps = Make(host, "Gura Burst eyes"); flare = Make(host, "Gura Burst eye flare");
            sheets = new[] { lamps, flare };
        }

        private Sheet Make(Transform host, string name)
        {
            var sheet = new Sheet { Node = new GameObject(name) { layer = host.gameObject.layer } };
            sheet.Node.transform.SetParent(host, false);
            sheet.Filter = sheet.Node.AddComponent<MeshFilter>();
            sheet.Renderer = sheet.Node.AddComponent<MeshRenderer>();
            sheet.Renderer.sharedMaterial = material;
            sheet.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sheet.Renderer.receiveShadows = false;
            sheet.Mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            sheet.Mesh.MarkDynamic();
            sheet.Filter.sharedMesh = sheet.Mesh;
            sheet.Node.SetActive(false);
            return sheet;
        }

        // Lay a sheet on the puppet's own plane, `depth` behind it (negative: in front).
        private static void Lay(Sheet sheet, PNVisualActor actor, float depth)
        {
            var at = actor.transform;
            sheet.Node.transform.SetPositionAndRotation(at.TransformPoint(new Vector3(0f, 0f, depth)), at.rotation);
            Vector3 world = at.lossyScale, under = sheet.Node.transform.parent.lossyScale;
            sheet.Node.transform.localScale = new Vector3(world.x / under.x, world.y / under.y, world.z / under.z);
        }

        private static void Show(Sheet sheet, Texture texture, float mode, float opacity, int order)
        {
            sheet.Block.Clear();
            if (texture != null) sheet.Block.SetTexture(MainTex, texture);
            sheet.Block.SetFloat(Mode, mode); sheet.Block.SetFloat(Opacity, opacity);
            sheet.Renderer.SetPropertyBlock(sheet.Block);
            sheet.Renderer.sortingOrder = order;
            sheet.Node.SetActive(true);
        }

        private static void Disc(Sheet sheet, Vector3 middle, Vector3 right, Vector3 up, float strength)
        {
            sheet.Quad(middle - right + up, middle + right + up, middle + right - up, middle - right - up,
                new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, -1f), Vector4.zero, strength, strength);
        }

        public void Draw(PNVisualActor actor, float age, float strength)
        {
            if (material == null || strength <= .001f) { Hide(); return; }
            var texture = actor.Renderer.sharedMaterial.GetTexture(MainTex);
            float pulse = .5f + .5f * Mathf.Sin(age * 5.2f);
            int order = actor.Renderer.sortingOrder;

            lamps.Clear(); flare.Clear();
            for (int eye = 0; eye < EyeSlots.Length; eye++)
            {
                if (!actor.TryCopySlotQuad(EyeSlots[eye], corners, uvs, out var bounds, out float alpha) || alpha <= .01f) continue;
                lamps.Quad(corners[0], corners[1], corners[2], corners[3], uvs[0], uvs[1], uvs[2], uvs[3], bounds, alpha, alpha);

                Vector3 middle = (corners[0] + corners[1] + corners[2] + corners[3]) * .25f;
                Vector3 right = (corners[1] - corners[0]) * .5f, up = (corners[0] - corners[3]) * .5f;
                Disc(flare, middle, right * 2.2f, up * 1.7f, .6f * alpha);
            }

            lamps.Write(); Lay(lamps, actor, -.012f); Show(lamps, texture, 0f, strength, order + 2);
            flare.Write(); Lay(flare, actor, -.013f); Show(flare, null, 1f, strength * Mathf.Lerp(.75f, 1f, pulse), order + 3);
        }

        public void Hide()
        {
            if (material == null) return;
            foreach (var sheet in sheets) sheet.Node.SetActive(false);
        }

        public void Dispose()
        {
            if (material == null) return;
            foreach (var sheet in sheets) { Drop(sheet.Mesh); Drop(sheet.Node); }
            Drop(material);
        }

        private static void Drop(Object item)
        {
            if (item == null) return;
            if (Application.isPlaying) Object.Destroy(item); else Object.DestroyImmediate(item);
        }
    }
}
#endif
