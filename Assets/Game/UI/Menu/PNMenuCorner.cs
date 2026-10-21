#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.UIElements;

// EN: The two corner buttons of every menu screen except pause: a rounded hollow triangle (back, left) and a rounded hollow square
//     (close, right). Thin 1.2 px line with soft layered glow (no second crisp outline), as in Docs/Mockups/ModeMenuDemo.html.
// VI: Hai nút góc: tam giác bo góc (quay lại) và hình vuông bo góc (đóng), nét mảnh và glow mềm nhiều lớp.
internal sealed class PNMenuCorner : VisualElement
{
    internal enum Kind { Back, Close }
    private sealed class Shape : VisualElement
    {
        private readonly Kind kind;
        public Shape(Kind kind)
        {
            this.kind = kind; pickingMode = PickingMode.Ignore; generateVisualContent += Draw;
            style.position = Position.Absolute; style.left = 0; style.right = 0; style.top = 0; style.bottom = 0;
            style.backgroundImage = Glow(kind);
        }
        private void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            p.lineJoin = LineJoin.Round; p.lineCap = LineCap.Round;
            p.strokeColor = new Color(1, 1, 1, .95f); p.lineWidth = 1.2f; RoundedPath(p, Points(kind, contentRect.center), Radius(kind)); p.Stroke();
        }
    }
    private static Vector2[] Points(Kind kind, Vector2 c)
    {
        if (kind == Kind.Close) { float h = 10f; return new[] { c + new Vector2(-h, -h), c + new Vector2(h, -h), c + new Vector2(h, h), c + new Vector2(-h, h) }; }
        return new[] { c + new Vector2(-9f, 0), c + new Vector2(7f, -12f), c + new Vector2(7f, 12f) };
    }
    private static float Radius(Kind kind) => kind == Kind.Close ? 4f : 3f;

    // The soft glow under the line is a baked texture (one per kind, built on first use): the distance of each pixel to the rounded outline
    // through a gaussian. Wide painter strokes around such tight corners left small spikes, a texture cannot.
    private static readonly Texture2D[] glows = new Texture2D[2];
    private static Texture2D Glow(Kind kind)
    {
        int index = (int)kind;
        if (glows[index] != null) return glows[index];
        const int size = 96, scale = 2; // the button is 48 units, the texture two pixels per unit
        var outline = Sample(Points(kind, new Vector2(24, 24)), Radius(kind));
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Texture rows run bottom to top, the element's top to bottom.
                var q = new Vector2((x + .5f) / scale, (size - 1 - y + .5f) / scale);
                float d = float.MaxValue;
                for (int i = 0; i < outline.Length; i++) d = Mathf.Min(d, DistanceToSegment(q, outline[i], outline[(i + 1) % outline.Length]));
                float a = .30f * Mathf.Exp(-(d * d) / (2f * 2.4f * 2.4f)) + .10f * Mathf.Exp(-(d * d) / (2f * 5f * 5f));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Menu corner glow " + kind, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        texture.SetPixels32(pixels); texture.Apply(false, true);
        return glows[index] = texture;
    }
    // The closed outline as a polyline: straight edges joined by rounded corners (quadratic curves with the tangent length of an arc of `radius`).
    private static Vector2[] Sample(Vector2[] v, float radius)
    {
        var result = new System.Collections.Generic.List<Vector2>(); int n = v.Length;
        for (int i = 0; i < n; i++)
        {
            Vector2 prev = v[(i + n - 1) % n], cur = v[i], next = v[(i + 1) % n];
            Vector2 a = (prev - cur), b = (next - cur); float la = a.magnitude, lb = b.magnitude; a /= la; b /= lb;
            float angle = Mathf.Acos(Mathf.Clamp(Vector2.Dot(a, b), -1f, 1f));
            float t = Mathf.Min(radius / Mathf.Tan(angle * .5f), Mathf.Min(la, lb) * .5f);
            Vector2 p0 = cur + a * t, p2 = cur + b * t;
            for (int k = 0; k <= 6; k++) { float s = k / 6f; result.Add((1 - s) * (1 - s) * p0 + 2 * (1 - s) * s * cur + s * s * p2); }
        }
        return result.ToArray();
    }
    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
        return (p - (a + ab * t)).magnitude;
    }
    internal static void RoundedPath(Painter2D p, Vector2[] v, float radius)
    {
        int n = v.Length;
        p.BeginPath();
        p.MoveTo((v[n - 1] + v[0]) * .5f);
        for (int i = 0; i < n; i++) p.ArcTo(v[i], (v[i] + v[(i + 1) % n]) * .5f, radius);
        p.ClosePath();
    }

    public const float ButtonSize = 48f, MobileButtonSize = 68f;
    private bool mobile;
    // Touch screens get larger buttons (the shape scales with the button), like the pause toggle (64 against 56).
    public bool Mobile
    {
        get => mobile;
        set
        {
            if (mobile == value) return;
            mobile = value; float size = value ? MobileButtonSize : ButtonSize, scale = size / ButtonSize;
            foreach (var b in new[] { Back, Close })
            { b.style.width = size; b.style.height = size; b[0].style.scale = new Scale(new Vector2(scale, scale)); }
        }
    }
    public Button Back { get; }
    public Button Close { get; }
    private bool backDimmed;
    private float alpha = 1f;

    public PNMenuCorner(Action back, Action close)
    {
        name = "pn-menu-corner"; pickingMode = PickingMode.Ignore;
        style.position = Position.Absolute; style.left = 0; style.right = 0; style.top = 0; style.bottom = 0;
        Back = Make("pn-menu-back", Kind.Back, back); Back.style.left = 16;
        Close = Make("pn-menu-close", Kind.Close, close); Close.style.right = 16;
        Add(Back); Add(Close);
    }

    private static Button Make(string name, Kind kind, Action click)
    {
        var b = new Button(click) { name = name, focusable = false };
        b.style.position = Position.Absolute; b.style.top = 14; b.style.width = ButtonSize; b.style.height = ButtonSize;
        b.style.backgroundColor = Color.clear; b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 0;
        b.style.paddingLeft = b.style.paddingRight = b.style.paddingTop = b.style.paddingBottom = 0;
        b.style.marginLeft = b.style.marginRight = b.style.marginTop = b.style.marginBottom = 0;
        b.Add(new Shape(kind));
        b.RegisterCallback<PointerEnterEvent>(_ => b.style.scale = new Scale(new Vector2(1.08f, 1.08f)));
        b.RegisterCallback<PointerLeaveEvent>(_ => b.style.scale = new Scale(Vector2.one));
        b.RegisterCallback<PointerDownEvent>(_ => b.style.scale = new Scale(new Vector2(.94f, .94f)));
        b.RegisterCallback<PointerUpEvent>(_ => b.style.scale = new Scale(Vector2.one));
        return b;
    }

    // First screen: back is dimmed (it still closes the menu with Esc, the button itself only shows that nothing is behind).
    public bool BackDimmed { get => backDimmed; set { backDimmed = value; Apply(); } }
    public float Alpha { get => alpha; set { alpha = Mathf.Clamp01(value); Apply(); } }
    private void Apply() { Back.style.opacity = alpha * (backDimmed ? .3f : 1f); Close.style.opacity = alpha; Back.SetEnabled(!backDimmed); }

    // Keeps the buttons inside the device safe area.
    public void Inset(float left, float right, float top)
    { Back.style.left = 16 + left; Close.style.right = 16 + right; Back.style.top = 14 + top; Close.style.top = 14 + top; }
}
#endif
