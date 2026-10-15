using UnityEngine;

// EN: A simple editable floor marker for the ErMode 2.5D prototype.
// VI: Mặt sàn thử nghiệm hiển thị ngay trong Scene View của ErMode.
[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class ErModeGroundVisual : MonoBehaviour
{
    [SerializeField] private Color floorColor = new Color(0.34f, 0.43f, 0.4f, 1f);
    private Texture2D texture;
    private Sprite sprite;
    private Material depthMaterial;
    private GameObject depthRoot;
    private Mesh depthMesh;
#if UNITY_EDITOR
    private bool refreshQueued;
#endif

    private void OnEnable() => Refresh();
    private void OnValidate()
    {
#if UNITY_EDITOR
        if (isActiveAndEnabled && !refreshQueued)
        { refreshQueued = true; UnityEditor.EditorApplication.delayCall += RefreshDeferred; }
#endif
    }
#if UNITY_EDITOR
    private void RefreshDeferred()
    { refreshQueued = false; if (this != null && isActiveAndEnabled) Refresh(); }
#endif

    private void Refresh()
    {
        Release();
        const int size = 128;
        texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Point
        };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool line = x % 8 == 0 || y % 8 == 0;
                bool alternate = ((x / 8) + (y / 8)) % 2 == 0;
                texture.SetPixel(x, y, line ? new Color(0.72f, 0.78f, 0.75f, 1f) :
                    alternate ? Color.white : new Color(0.92f, 0.94f, 0.93f, 1f));
            }
        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        SpriteRenderer renderer = GetComponent<SpriteRenderer>();
        var shader = Resources.Load<Shader>("PNVfx/GroundDepth");
        if (shader != null)
        {
            depthMaterial = new Material(shader) { name = "Owned ErMode floor depth", hideFlags = HideFlags.HideAndDontSave };
            depthRoot = new GameObject("Owned floor depth geometry") { hideFlags = HideFlags.HideAndDontSave };
            depthRoot.transform.SetParent(transform, false); depthRoot.transform.localPosition = new Vector3(0,0,.001f); depthRoot.layer = gameObject.layer;
            depthMesh = new Mesh { name = "Owned floor sprite depth mesh", hideFlags = HideFlags.HideAndDontSave };
            var vertices = sprite.vertices; var points = new Vector3[vertices.Length];
            for (int i = 0; i < points.Length; i++) points[i] = new Vector3(vertices[i].x, vertices[i].y, 0);
            var spriteTriangles = sprite.triangles; var triangles = new int[spriteTriangles.Length];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = spriteTriangles[i];
            depthMesh.vertices = points; depthMesh.uv = sprite.uv; depthMesh.triangles = triangles; depthMesh.RecalculateBounds();
            depthRoot.AddComponent<MeshFilter>().sharedMesh = depthMesh;
            var depthRenderer = depthRoot.AddComponent<MeshRenderer>(); depthRenderer.sharedMaterial = depthMaterial;
            depthRoot.AddComponent<PNVfxFloorDepth>();
            depthRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; depthRenderer.receiveShadows = false;
        }
        renderer.sprite = sprite;
        renderer.color = floorColor;
        renderer.sortingOrder = -100;
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall -= RefreshDeferred; refreshQueued = false;
#endif
        Release();
    }
    private void Release()
    {
        if (depthRoot != null) depthRoot.SetActive(false);
        if (GetComponent<SpriteRenderer>() is { } renderer && renderer.sprite == sprite)
            renderer.sprite = null;
        if (Application.isPlaying)
        {
            if (sprite != null) Destroy(sprite);
            if (texture != null) Destroy(texture);
            if (depthMaterial != null) Destroy(depthMaterial);
            if (depthRoot != null) Destroy(depthRoot);
            if (depthMesh != null) Destroy(depthMesh);
        }
        else
        {
            if (sprite != null) DestroyImmediate(sprite);
            if (texture != null) DestroyImmediate(texture);
            if (depthMaterial != null) DestroyImmediate(depthMaterial);
            if (depthRoot != null) DestroyImmediate(depthRoot);
            if (depthMesh != null) DestroyImmediate(depthMesh);
        }
        sprite = null;
        texture = null;
        depthMaterial = null;
        depthRoot = null; depthMesh = null;
    }
}
