using UnityEngine;

// EN: A soft floor shadow for the ErMode paper-sprite prototype.
// VI: Bóng mềm trên sàn cho bản thử nhân vật giấy của ErMode.
[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class ErModeCharacterShadow : MonoBehaviour, IPNSceneEntityVisual
{
    public void BindGround(Collider value) { ground=value; UpdateShadow(); }
    [SerializeField] private Transform target;
    [SerializeField] private Collider ground;
    [SerializeField, Min(0.01f)] private float width = 0.52f;
    [SerializeField, Min(0.01f)] private float length = 0.28f;
    [SerializeField, Min(0f)] private float footOffset = 0.14f;
    [SerializeField, Min(0f)] private float surfaceOffset = 0.012f;
    [SerializeField] private Color shadowColor = new Color(0.08f, 0.12f, 0.11f, 0.34f);

    private SpriteRenderer shadowRenderer;
    private Texture2D texture;
    private Sprite sprite;

    private void OnEnable()
    {
        shadowRenderer = GetComponent<SpriteRenderer>();
        CreateSprite();
        UpdateShadow();
    }

    private void LateUpdate() => UpdateShadow();

    private void UpdateShadow()
    {
        if (shadowRenderer == null) return;
        if (target == null || ground == null)
        {
            shadowRenderer.enabled = false;
            return;
        }

        Vector3 origin = target.position + Vector3.up;
        if (!ground.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, 10f))
        {
            shadowRenderer.enabled = false;
            return;
        }

        float height = Mathf.Max(0f, target.position.y - footOffset - hit.point.y);
        float size = 1f / (1f + height * 0.4f);
        float opacity = 1f / (1f + height * 0.8f);
        var flat = Quaternion.FromToRotation(Vector3.forward, hit.normal);
        // While a cinematic with AllowYaw is live, turn the ellipse about the ground normal so its long axis stays across the camera view.
        var service = Application.isPlaying ? PNViewCamera.Service : null;
        if (service != null && service.Orientation.YawOverridden) flat = Quaternion.AngleAxis(service.Orientation.Yaw, hit.normal) * flat;
        transform.SetPositionAndRotation(hit.point + hit.normal * surfaceOffset, flat);
        transform.localScale = new Vector3(width * size, length * size, 1f);
        shadowRenderer.color = new Color(shadowColor.r, shadowColor.g, shadowColor.b,
            shadowColor.a * opacity);
        shadowRenderer.sortingOrder = 0;
        shadowRenderer.enabled = true;
    }

    private void CreateSprite()
    {
        ReleaseSprite();
        const int resolution = 64;
        texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                float u = (x + 0.5f) / resolution * 2f - 1f;
                float v = (y + 0.5f) / resolution * 2f - 1f;
                float edge = Mathf.Clamp01((1f - Mathf.Sqrt(u * u + v * v)) * 2.2f);
                float alpha = edge * edge * (3f - 2f * edge);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0, 0, resolution, resolution),
            new Vector2(0.5f, 0.5f), resolution);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        shadowRenderer.sprite = sprite;
    }

    private void OnDisable()
    {
        if (shadowRenderer != null) shadowRenderer.enabled = false;
        ReleaseSprite();
    }

    private void ReleaseSprite()
    {
        if (shadowRenderer != null && shadowRenderer.sprite == sprite) shadowRenderer.sprite = null;
        if (Application.isPlaying)
        {
            if (sprite != null) Destroy(sprite);
            if (texture != null) Destroy(texture);
        }
        else
        {
            if (sprite != null) DestroyImmediate(sprite);
            if (texture != null) DestroyImmediate(texture);
        }
        sprite = null;
        texture = null;
    }
}
