#if UNITY_5_3_OR_NEWER
using UnityEngine;

// EN: The soft shadow on the ground under a body's feet. It is NOT the paper shadow between rig parts (PNVisualPaperSettings). It replaces ErModeCharacterShadow, which only shadowed what one ground collider
//     (named by the spawn point) was under: a grid world has no such collider (chunk colliders are rebuilt), so the shadow went out as soon as the body left the spawn cell.
//     Ground: a collider the owner binds (BindGround, the Hall's scene floor) is tried first; when it is not hit, or none is bound, a ray goes down through the PHYSICS SCENE of the body, against the ground layer
//     (13: scene floors and grid chunks), triggers ignored. The shadow shrinks and fades with the height of the capsule's bottom over the ground, so a body in the air needs no setting.
//     `PNGroundShadow.Ensure` gives every spawned character one (size from the capsule) unless its prefab has a shadow already or its `body.shadow` is false; `body.shadow` scale/opacity multiply the result.
//     Serialized names match ErModeCharacterShadow, so the prefabs that had it keep their values. The sprite is one shared soft ellipse.
// VI: Bóng mềm dưới chân (không phải bóng giấy giữa các lớp). Tự dò mặt đất bằng tia trong physics scene của thân (layer 13); collider gắn sẵn (Hall) được thử trước. Thu nhỏ và mờ dần theo độ cao.
[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class PNGroundShadow : MonoBehaviour, IPNSceneEntityVisual
{
    public const int GroundMask = 1 << PNGridWorldHost.GroundLayer;
    public const float WidthPerRadius = 4.7f, LengthPerWidth = .54f, RayHeight = 1f, RayLength = 10f;

    [SerializeField] private Transform target;
    [SerializeField] private Collider ground;
    [SerializeField, Min(0.01f)] private float width = 0.52f;
    [SerializeField, Min(0.01f)] private float length = 0.28f;
    [SerializeField, Min(0f)] private float footOffset = 0.14f;
    [SerializeField, Min(0f)] private float surfaceOffset = 0.012f;
    [SerializeField] private Color shadowColor = new Color(0.08f, 0.12f, 0.11f, 0.34f);

    private SpriteRenderer shadowRenderer;
    private CapsuleCollider capsule;
    private bool bodyEnabled = true;
    private float scaleMultiplier = 1f, opacityMultiplier = 1f;

    public void BindGround(Collider value) { ground = value; UpdateShadow(); }
    public bool Visible => shadowRenderer != null && shadowRenderer.enabled;

    // Applies a body's `shadow` setting (on/off, scale, opacity) to this shadow.
    public void Apply(PNBodyShadow setting)
    {
        setting = setting ?? PNBodyShadow.Default;
        bodyEnabled = setting.Enabled; scaleMultiplier = setting.Scale; opacityMultiplier = setting.Opacity;
        UpdateShadow();
    }

    // Finds the shadow of a spawned body or makes one (a child "Ground Shadow", size from the capsule), then applies its setting. A body whose prefab carries another IPNSceneEntityVisual (the old ErMode shadow) keeps that one and gets no setting.
    public static PNGroundShadow Ensure(GameObject root, PNBodyShadow setting)
    {
        var own = root.GetComponentInChildren<PNGroundShadow>(true);
        if (own == null)
        {
            foreach (var visual in root.GetComponentsInChildren<MonoBehaviour>(true)) if (visual is IPNSceneEntityVisual) return null;   // an older shadow the prefab carries (the ErMode one)
            var child = new GameObject("Ground Shadow"); child.transform.SetParent(root.transform, false);
            child.AddComponent<SpriteRenderer>();
            own = child.AddComponent<PNGroundShadow>();
            own.target = root.transform;
            var body = root.GetComponent<CapsuleCollider>();
            if (body != null) { own.width = Mathf.Max(.1f, body.radius * WidthPerRadius); own.length = own.width * LengthPerWidth; }
        }
        own.Apply(setting);
        return own;
    }

    private void OnEnable()
    {
        shadowRenderer = GetComponent<SpriteRenderer>();
        shadowRenderer.sprite = SharedSprite();
        UpdateShadow();
    }

    private void OnDisable() { if (shadowRenderer != null) shadowRenderer.enabled = false; }
    private void LateUpdate() => UpdateShadow();

    private bool FindGround(Vector3 origin, out Vector3 point, out Vector3 normal)
    {
        point = default; normal = Vector3.up;
        if (ground != null && ground.Raycast(new Ray(origin, Vector3.down), out var bound, RayLength)) { point = bound.point; normal = bound.normal; return true; }
        if (!gameObject.scene.GetPhysicsScene().Raycast(origin, Vector3.down, out var hit, RayLength, GroundMask, QueryTriggerInteraction.Ignore)) return false;
        point = hit.point; normal = hit.normal; return true;
    }

    private void UpdateShadow()
    {
        if (shadowRenderer == null) return;
        if (target == null || !bodyEnabled) { shadowRenderer.enabled = false; return; }
        if (capsule == null) capsule = target.GetComponent<CapsuleCollider>();
        Vector3 origin = target.position + Vector3.up * RayHeight;
        if (!FindGround(origin, out var point, out var normal)) { shadowRenderer.enabled = false; return; }

        float bottom = capsule != null ? capsule.bounds.min.y : target.position.y - footOffset;
        float height = Mathf.Max(0f, bottom - point.y);
        float size = 1f / (1f + height * 0.4f) * scaleMultiplier;
        float opacity = 1f / (1f + height * 0.8f) * opacityMultiplier;
        var flat = Quaternion.FromToRotation(Vector3.forward, normal);
        // While a cinematic with AllowYaw is live, turn the ellipse about the ground normal so its long axis stays across the camera view.
        var service = Application.isPlaying ? PNViewCamera.Service : null;
        if (service != null && service.Orientation.YawOverridden) flat = Quaternion.AngleAxis(service.Orientation.Yaw, normal) * flat;
        transform.SetPositionAndRotation(point + normal * surfaceOffset, flat);
        transform.localScale = new Vector3(width * size, length * size, 1f);
        shadowRenderer.color = new Color(shadowColor.r, shadowColor.g, shadowColor.b, shadowColor.a * opacity);
        shadowRenderer.sortingOrder = 0;
        shadowRenderer.enabled = true;
    }

    // One soft ellipse for every shadow (a texture per body would cost a Texture2D each).
    private static Sprite shared;
    private static Sprite SharedSprite()
    {
        if (shared != null) return shared;
        const int resolution = 64;
        var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "PN Ground Shadow" };
        for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                float u = (x + 0.5f) / resolution * 2f - 1f, v = (y + 0.5f) / resolution * 2f - 1f;
                float edge = Mathf.Clamp01((1f - Mathf.Sqrt(u * u + v * v)) * 2.2f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, edge * edge * (3f - 2f * edge)));
            }
        texture.Apply();
        shared = Sprite.Create(texture, new Rect(0, 0, resolution, resolution), new Vector2(0.5f, 0.5f), resolution);
        shared.hideFlags = HideFlags.HideAndDontSave; shared.name = "PN Ground Shadow";
        return shared;
    }
}
#endif
