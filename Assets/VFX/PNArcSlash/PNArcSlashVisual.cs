using System;
using EveEngine;
using UnityEngine;

// Golden energy ribbons; geometry follows the authored hit track, presentation owns no damage.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PNArcSlashVisual : MonoBehaviour
{
    private const int Segments = 40;
    private const int Bands = 5;
    private const int Sparks = 12;
    private Mesh mesh;
    private Vector3[] vertices;
    private Color[] colors;
    private Vector2[] uvs;
    public float EnergyIntensity { get; set; } = 1f;
    private float radius = 0.6f;
    private EveEasyPooling pool;
    private GameObject pooledRoot;
    public EveHitboxTrackData[] Tracks { get; set; }
    public float Progress { get; private set; }
    public float HitStart { get; set; }
    public float HitEnd { get; set; } = 1f;
    public Transform AttackRoot => pooledRoot == null ? (transform.parent == null ? transform : transform.parent) : pooledRoot.transform;

    // EN: The skill owns this pool across casts; operations only borrow its visuals.
    // VI: Skill giữ pool qua nhiều lần chém; operation chỉ mượn VFX từ pool.
    public static EveEasyPooling CreatePool(string path = "PNVfx/PNArcSlash")
    {
        var prefab = Resources.Load<GameObject>(path);
        if (prefab == null) throw new InvalidOperationException("Missing Resources/PNVfx/PNArcSlash prefab.");
        return new EveEasyPooling(prefab);
    }

    public static PNArcSlashVisual Spawn(EveEasyPooling pool, PNSkill owner, PNSlashVfxSettings settings, Vector3 castDirection)
    {
        if (pool == null) throw new ArgumentNullException(nameof(pool));
        if (float.IsNaN(castDirection.x) || float.IsInfinity(castDirection.x) ||
            float.IsNaN(castDirection.y) || float.IsInfinity(castDirection.y) ||
            float.IsNaN(castDirection.z) || float.IsInfinity(castDirection.z))
            throw new ArgumentException("Slash direction must be finite.", nameof(castDirection));
        double length = Math.Sqrt((double)castDirection.x * castDirection.x + (double)castDirection.z * castDirection.z);
        if (length == 0d) throw new ArgumentException("Slash needs a nonzero XZ direction.", nameof(castDirection));
        var direction = new Vector3((float)(castDirection.x / length), 0f, (float)(castDirection.z / length));
        var position = owner.transform.position + direction * settings.ForwardOffset + Vector3.up * settings.Height;
        var instance = pool.Get();
        try
        {
            var visual = instance.GetComponentInChildren<PNArcSlashVisual>(true);
            if (visual == null) throw new InvalidOperationException("Slash prefab needs PNArcSlashVisual.");
            instance.transform.position = position;
            // EN: The mesh points along local +X; use only the direction supplied by the skill.
            // VI: Mesh huong local +X; chi dung huong do skill truyen xuong.
            instance.transform.rotation = Quaternion.Euler(0f, -Mathf.Atan2(direction.z, direction.x) * Mathf.Rad2Deg, 0f);
            visual.pool = pool;
            visual.pooledRoot = instance;
            var definition = instance.GetComponent<PNSlashPrefab>();
            visual.Tracks = definition == null ? null : definition.Capture();
            if (definition != null) { definition.RuntimeTracks = visual.Tracks; instance.transform.position = owner.transform.position; }
            visual.Initialize(settings.Radius);
            visual.EnergyIntensity = 1f;
            visual.Render(0f);
            instance.SetActive(true);
            return visual;
        }
        catch { pool.Release(instance); throw; }
    }

    public void Initialize(float value)
    {
        radius = value;
        if (mesh != null) return;
        mesh = new Mesh { name = "PN Arc Slash (runtime)", hideFlags = HideFlags.HideAndDontSave };
        mesh.MarkDynamic();
        vertices = new Vector3[Bands * (Segments + 1) * 2 + Sparks * 4];
        colors = new Color[vertices.Length];
        uvs = new Vector2[vertices.Length];
        var triangles = new int[Bands * Segments * 6 + Sparks * 6];
        int index = 0;
        for (int band = 0; band < Bands; band++)
            for (int segment = 0; segment < Segments; segment++)
            {
                int v = band * (Segments + 1) * 2 + segment * 2;
                uvs[v] = new Vector2(segment / (float)Segments, 0); uvs[v + 1] = new Vector2(segment / (float)Segments, 1);
                triangles[index++] = v; triangles[index++] = v + 2; triangles[index++] = v + 1;
                triangles[index++] = v + 1; triangles[index++] = v + 2; triangles[index++] = v + 3;
            }
        for (int spark = 0; spark < Sparks; spark++)
        {
            int v = Bands * (Segments + 1) * 2 + spark * 4;
            uvs[v] = new Vector2(0, 0); uvs[v + 1] = new Vector2(0, 1);
            uvs[v + 2] = new Vector2(1, 0); uvs[v + 3] = new Vector2(1, 1);
            triangles[index++] = v; triangles[index++] = v + 1; triangles[index++] = v + 2;
            triangles[index++] = v + 2; triangles[index++] = v + 1; triangles[index++] = v + 3;
        }
        mesh.vertices = vertices;
        mesh.colors = colors;
        // Last band vertices also need UVs (the triangle loop ends one segment early).
        for (int band = 0; band < Bands; band++)
        { int v = band * (Segments + 1) * 2 + Segments * 2; uvs[v] = new Vector2(1, 0); uvs[v + 1] = new Vector2(1, 1); }
        mesh.uv = uvs;
        mesh.triangles = triangles;
        GetComponent<MeshFilter>().sharedMesh = mesh;
        var texture = Resources.Load<Texture2D>("PNVfx/EveEnergy/EnergyRibbon");
        if (texture != null)
        {
            var block = new MaterialPropertyBlock(); var renderer = GetComponent<MeshRenderer>();
            renderer.GetPropertyBlock(block); block.SetTexture("_MainTex", texture); block.SetFloat("_UseTexture", 1);
            renderer.SetPropertyBlock(block);
        }
    }

    public void Render(float progress)
    {
        if (mesh == null) Initialize(radius);
        float t = Mathf.Clamp01(progress);
        Progress = t;
        if (pooledRoot != null && Tracks != null)
        {
            var definition = pooledRoot.GetComponent<PNSlashPrefab>();
            if (definition != null)
            {
                definition.RuntimeTracks = Tracks;
                var components = definition.GetComponentsInChildren<EveHitboxTrack>(true);
                for (int i = 0; i < components.Length; i++)
                { components[i].RuntimeData = i < Tracks.Length ? Tracks[i] : null; components[i].RuntimeRoot = definition.transform; components[i].RuntimeTime = t; components[i].RuntimeEnabled = t >= HitStart && t <= HitEnd; }
            }
        }
        EveHitboxFrame? authored = Tracks != null && Tracks.Length != 0
            ? Tracks[0].Sample(t, new EveHitboxPose(Vector3.zero, Quaternion.identity)) : (EveHitboxFrame?)null;
        if (authored.HasValue)
        {
            transform.localPosition = authored.Value.Pose.Position;
            // Rotate vertices below; root/visual transform still describes the captured aim.
        }
        float reveal = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.22f));
        float fade = Mathf.Pow(1f - t, 1.15f);
        float span = authored.HasValue ? Tracks[0].Geometry.Angle * Tracks[0].Angular.Evaluate(t) : 170f * reveal;
        float turn = authored.HasValue ? -span * .5f : -95f + t * 70f;
        float drawRadius = authored.HasValue ? Tracks[0].Geometry.Radius * Tracks[0].Radial.Evaluate(t) : radius * (1f + t * .12f);
        for (int band = 0; band < Bands; band++)
        {
            float r = drawRadius * (band == 2 ? .78f : band == 3 ? 1.035f : band == 4 ? .91f : 1f);
            float width = drawRadius * (band == 0 ? .22f : band == 1 ? .065f : band == 2 ? .035f : band == 3 ? .13f : .024f);
            var color = band == 0 ? new Color(3.2f, 1.9f, .3f) : band == 1 ? new Color(5f, 4.7f, 3.2f) :
                band == 2 ? new Color(2.8f, 1.25f, .12f) : band == 3 ? new Color(1.8f, .75f, .08f) : new Color(4f, 3f, 1.3f);
            color.r *= EnergyIntensity; color.g *= EnergyIntensity; color.b *= EnergyIntensity;
            for (int segment = 0; segment <= Segments; segment++)
            {
                float u = segment / (float)Segments;
                float angle = (turn + u * span) * Mathf.Deg2Rad;
                float taper = Mathf.Pow(Mathf.Sin(u * Mathf.PI), 0.7f);
                float inner = r - width * taper;
                int v = band * (Segments + 1) * 2 + segment * 2;
                vertices[v] = new Vector3(Mathf.Cos(angle) * inner, band * 0.003f, Mathf.Sin(angle) * inner);
                vertices[v + 1] = new Vector3(Mathf.Cos(angle) * r, band * 0.003f, Mathf.Sin(angle) * r);
                color.a = fade * reveal * taper * (band == 3 ? .22f : band == 0 ? .6f : 1f);
                colors[v] = color; colors[v + 1] = color;
            }
        }
        for (int spark = 0; spark < Sparks; spark++)
        {
            float angle = (turn + span * (spark + .5f) / Sparks + t * 12f) * Mathf.Deg2Rad;
            var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            var side = new Vector3(-direction.z, 0f, direction.x) * drawRadius * .008f;
            var tip = direction * drawRadius * (1.03f + t * (.35f + spark * .018f));
            var tail = tip - direction * drawRadius * (.04f + .13f * (1f - t));
            int v = Bands * (Segments + 1) * 2 + spark * 4;
            vertices[v] = tail - side; vertices[v + 1] = tail + side;
            vertices[v + 2] = tip - side * 0.1f; vertices[v + 3] = tip + side * 0.1f;
            var color = new Color(4.2f * EnergyIntensity, 3.1f * EnergyIntensity, 1.2f * EnergyIntensity, fade * reveal);
            colors[v + 2] = colors[v + 3] = color;
            color.a *= .15f; colors[v] = colors[v + 1] = color;
        }
        if (authored.HasValue)
            for (int i = 0; i < vertices.Length; i++) vertices[i] = authored.Value.Pose.Rotation * vertices[i];
        mesh.vertices = vertices;
        mesh.colors = colors;
        mesh.RecalculateBounds();
    }

    public void Release()
    {
        var ownerPool = pool;
        pool = null;
        Tracks = null;
        var definition = pooledRoot == null ? null : pooledRoot.GetComponent<PNSlashPrefab>();
        if (definition != null) definition.RuntimeTracks = null;
        if (definition != null)
            foreach (var track in definition.GetComponentsInChildren<EveHitboxTrack>(true)) { track.RuntimeData = null; track.RuntimeRoot = null; }
        ownerPool?.Release(pooledRoot == null ? gameObject : pooledRoot);
        pooledRoot = null;
    }
    private void OnDestroy()
    {
        if (mesh != null)
        {
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
        }
    }
}
