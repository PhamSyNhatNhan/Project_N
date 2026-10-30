#if UNITY_5_3_OR_NEWER
using UnityEngine;
using UnityEngine.Rendering;

// EN: Minimal visual for a homing projectile: a shaped head (a quad turned to the camera and rolled along the projectile's heading) and a
//     TrailRenderer tail, both from registered pack textures (see PNProjectileTextureReferences). Movement and hits stay in the framework;
//     the visual follows the pooled object and the projectile clock, and has no hit or launch effects yet. Each implementation owns its
//     visual (no shared service).
// VI: Hiển thị tối thiểu cho đạn homing: đầu đạn có hình (quad hướng về camera, xoay theo hướng bay) và đuôi TrailRenderer, dùng texture
//     đã đăng ký. Chuyển động và va chạm do framework lo; hiển thị bám theo đối tượng pool và đồng hồ đạn. Chưa có hiệu ứng nổ/bắn.
public class PNTrailHomingProjectile : PNHomingProjectile
{
    private PNProjectileTrail trail;
    public override void OnPoolCreated() { base.OnPoolCreated(); trail = PNProjectileTrail.Create(this); }
    protected override void Initialize(PNProjectileSpawn spawn) { base.Initialize(spawn); trail?.Begin(Clock); }
    protected override void Cleanup() { try { trail?.End(); } finally { base.Cleanup(); } }
    public override void DisposePooledResources() { trail?.Dispose(); trail = null; base.DisposePooledResources(); }
}

// Head quad + trail. The trail is a TrailRenderer (world space); it stands still when the projectile clock stops (its point lifetime is
// extended while stopped, so a frozen projectile keeps its tail).
public sealed class PNProjectileTrail : MonoBehaviour
{
    private const float TrailTime = .32f, HeadLength = .4f, HeadWidth = .4f;
    private static Material headMaterial, trailMaterial; private static Mesh quad;
    private PNProjectileInstance owner; private PNProjectileClock clock; private Transform head; private TrailRenderer tail;
    private bool started, stopped;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] private static void ResetStatics() { headMaterial = trailMaterial = null; quad = null; }
    public static PNProjectileTrail Create(PNProjectileInstance owner)
    {
        var child = new GameObject("Projectile visual"); child.transform.SetParent(owner.transform, false); child.layer = owner.gameObject.layer;
        var result = child.AddComponent<PNProjectileTrail>(); result.owner = owner; result.Build(); return result;
    }
    private static void LoadMaterials()
    {
        if (headMaterial != null && trailMaterial != null && quad != null) return;
        var references = Resources.Load<PNProjectileTextureReferences>("PNVfx/ProjectileTextures");
        if (references == null || references.Head == null || references.Trail == null) throw new System.InvalidOperationException("Registered projectile texture references are missing.");
        var shader = Shader.Find("Project_N/VFX/ProjectileTrail");
        if (shader == null) throw new System.InvalidOperationException("Missing projectile trail shader.");
        Material Make(string name, Texture texture) { var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave }; material.SetTexture("_MainTex", texture); return material; }
        headMaterial = Make("Projectile head (shared)", references.Head); headMaterial.SetColor("_Tint", new Color(1.7f, 1.2f, .45f, 1f)); trailMaterial = Make("Projectile trail (shared)", references.Trail);
        quad = new Mesh { name = "Projectile head quad", hideFlags = HideFlags.HideAndDontSave };
        quad.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) };
        quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        quad.triangles = new[] { 0, 2, 1, 2, 3, 1 }; quad.RecalculateBounds();
    }
    private void Build()
    {
        LoadMaterials();
        var node = new GameObject("Head"); node.transform.SetParent(transform, false); node.layer = gameObject.layer; head = node.transform;
        node.AddComponent<MeshFilter>().sharedMesh = quad;
        var renderer = node.AddComponent<MeshRenderer>(); renderer.sharedMaterial = headMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        head.localScale = new Vector3(HeadLength, HeadWidth, 1f);
        tail = gameObject.AddComponent<TrailRenderer>(); tail.sharedMaterial = trailMaterial; tail.shadowCastingMode = ShadowCastingMode.Off; tail.receiveShadows = false;
        tail.time = TrailTime; tail.minVertexDistance = .05f; tail.alignment = LineAlignment.View; tail.textureMode = LineTextureMode.Stretch; tail.numCapVertices = 3; tail.emitting = false;
        tail.widthCurve = new AnimationCurve(new Keyframe(0, .17f), new Keyframe(1, 0f));
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(new Color(1.7f, 1.1f, .35f), 0), new GradientColorKey(new Color(1.2f, .45f, .1f), 1) }, new[] { new GradientAlphaKey(.95f, 0), new GradientAlphaKey(0f, 1) });
        tail.colorGradient = gradient;
        head.gameObject.SetActive(false);
    }
    public void SetPalette(Color core,Color end)
    {
        var block=new MaterialPropertyBlock();block.SetColor("_Tint",core);head.GetComponent<MeshRenderer>().SetPropertyBlock(block);
        var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(core,0),new GradientColorKey(end,1)},new[]{new GradientAlphaKey(.9f,0),new GradientAlphaKey(0,1)});tail.colorGradient=gradient;
    }
    // The pooled object may still be inactive when it is initialized, so the visual starts on the first LateUpdate after activation.
    public void Begin(PNProjectileClock projectileClock)
    {
        clock = projectileClock; started = false; stopped = false;
        if (tail != null) { tail.emitting = false; tail.Clear(); tail.time = TrailTime; }
        if (head != null) head.gameObject.SetActive(false);
    }
    public void End()
    {
        started = true; if (tail != null) { tail.emitting = false; tail.Clear(); }
        if (head != null) head.gameObject.SetActive(false);
    }
    private void LateUpdate()
    {
        if (clock == null || owner == null) return;
        if (!started) { started = true; tail.Clear(); tail.emitting = true; head.gameObject.SetActive(true); }
        bool frozen = clock.Scale <= 0f;
        if (frozen != stopped) { stopped = frozen; tail.time = frozen ? 3600f : TrailTime; }
        // Face the camera and roll so that the head's +x axis follows the heading as seen on screen.
        var camera = PNViewCamera.Current; if (camera == null) return;
        var forward = camera.transform.forward; var heading = Vector3.ProjectOnPlane(owner.Forward, forward);
        if (heading.sqrMagnitude < 1e-6f) return;
        var right = heading.normalized; head.rotation = Quaternion.LookRotation(forward, Vector3.Cross(forward, right));
    }
    public void Dispose() { if (this != null) Destroy(gameObject); }
}
#else
// Placeholder so the standalone (non-Unity) build can register the implementation, like PNHomingProjectile.
public class PNTrailHomingProjectile { }
#endif
