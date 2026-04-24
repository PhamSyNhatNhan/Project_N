using UnityEngine;

/// <summary>
/// Quản lý 4 bức tường đấu trường
/// </summary>
public class ArenaWallManager : MonoBehaviour
{
    // ── Appearance ────────────────────────────────────────────────
    [Header("Wall Appearance")]
    [SerializeField] private Color  wallColor     = new Color(0.65f, 0.12f, 0.05f, 1f);
    [SerializeField] private float  wallThickness = 1.5f;
    [SerializeField] private string wallLayerName = "ArenaWall";
    [SerializeField] private int    sortingOrder  = 5;

    // ── Lerp Speeds ───────────────────────────────────────────────
    [Header("Lerp Speeds")]
    [SerializeField] private float setupLerpSpeed   = 20f;
    [SerializeField] private float defaultLerpSpeed = 3f;

    // ── Internal ──────────────────────────────────────────────────
    private Transform tTop, tBottom, tLeft, tRight;
    private Vector2   currentSize;
    private Vector2   targetSize;
    private float     lerpSpeed;
    private bool      isActive;

    public bool IsAtTargetSize =>
        isActive && Vector2.Distance(currentSize, targetSize) < 0.02f;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake() => EnsureWallsCreated();

    private void Update()
    {
        if (!isActive || IsAtTargetSize) return;
        currentSize = Vector2.MoveTowards(currentSize, targetSize, lerpSpeed * Time.deltaTime);
        ApplySize(currentSize);
    }

    // ── Public API ────────────────────────────────────────────────
    public void SetupArena(Vector2 size)
    {
        EnsureWallsCreated(); // guard phòng execution-order issue
        currentSize = Vector2.zero;
        targetSize  = size;
        lerpSpeed   = setupLerpSpeed;
        ApplySize(Vector2.zero);
        SetAllActive(true);
        isActive = true;
    }

    public void SetTargetSize(Vector2 newSize, float speed)
    {
        targetSize = newSize;
        lerpSpeed  = speed > 0f ? speed : defaultLerpSpeed;
    }

    public void HideArena()
    {
        SetAllActive(false);
        isActive = false;
    }

    // ── Internal ──────────────────────────────────────────────────
    private void EnsureWallsCreated()
    {
        if (tTop != null) return;
        int layer = LayerMask.NameToLayer(wallLayerName);
        tTop    = MakeWall("Wall_Top",    layer);
        tBottom = MakeWall("Wall_Bottom", layer);
        tLeft   = MakeWall("Wall_Left",   layer);
        tRight  = MakeWall("Wall_Right",  layer);
        SetAllActive(false);
    }

    private void ApplySize(Vector2 s)
    {
        float hw = s.x * 0.5f, hh = s.y * 0.5f;
        float t  = wallThickness, ht = t * 0.5f;

        tTop.position    = new Vector3(0f,  hh + ht, 0f);
        tTop.localScale  = new Vector3(s.x + t * 2f, t, 1f);

        tBottom.position   = new Vector3(0f, -hh - ht, 0f);
        tBottom.localScale = new Vector3(s.x + t * 2f, t, 1f);

        tLeft.position   = new Vector3(-hw - ht, 0f, 0f);
        tLeft.localScale = new Vector3(t, s.y, 1f);

        tRight.position   = new Vector3(hw + ht, 0f, 0f);
        tRight.localScale = new Vector3(t, s.y, 1f);
    }

    private Transform MakeWall(string wallName, int layer)
    {
        var go = new GameObject(wallName);
        go.transform.SetParent(transform);
        if (layer >= 0) go.layer = layer;

        var col  = go.AddComponent<BoxCollider2D>();
        col.size = Vector2.one;

        var sr    = go.AddComponent<SpriteRenderer>();
        sr.sprite = MakeWhiteSprite();
        sr.color  = wallColor;
        sr.sortingOrder = sortingOrder;

        return go.transform;
    }

    private void SetAllActive(bool v)
    {
        if (tTop)    tTop.gameObject.SetActive(v);
        if (tBottom) tBottom.gameObject.SetActive(v);
        if (tLeft)   tLeft.gameObject.SetActive(v);
        if (tRight)  tRight.gameObject.SetActive(v);
    }

    private static Sprite MakeWhiteSprite()
    {
        var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.8f, 0.2f, 0.1f, 0.25f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(targetSize.x, targetSize.y, 0f));
    }
}