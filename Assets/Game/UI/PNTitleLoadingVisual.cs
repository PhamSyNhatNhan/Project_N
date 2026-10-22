#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.UIElements;

// Presentation only. The owner advances this on its existing real-time tick;
// it has no knowledge of boot, session state, scene changes or loading progress.
internal sealed class PNTitleLoadingVisual : VisualElement, IDisposable
{
    private readonly bool title;
    private readonly Texture2D light;
    private readonly VisualElement centreGlow, sandGlow;
    private float clock;

    internal PNTitleLoadingVisual(bool title)
    {
        this.title = title; pickingMode = PickingMode.Ignore;
        style.position = Position.Absolute; style.left = 0; style.right = 0; style.top = 0; style.bottom = 0;
        light = PNPauseArtwork.Gradient(false);
        centreGlow = Glow(); sandGlow = Glow(); Add(centreGlow); Add(sandGlow);
        generateVisualContent += Draw;
        RegisterCallback<GeometryChangedEvent>(_ => LayoutGlows());
    }

    private VisualElement Glow()
    {
        var e = new VisualElement { pickingMode = PickingMode.Ignore };
        e.style.position = Position.Absolute; e.style.backgroundImage = light;
        return e;
    }
    internal void Tick(float dt) { clock += Mathf.Max(0, dt); MarkDirtyRepaint(); }
    private void LayoutGlows()
    {
        float w = contentRect.width, h = contentRect.height;
        Place(centreGlow, w * (title ? .5f : .48f), h * (title ? .31f : .46f), title ? 210 : 84, title ? 210 : 84);
        Place(sandGlow, w * .93f - 58, h * .9f - 49, 144, 52);
        sandGlow.style.display = title ? DisplayStyle.None : DisplayStyle.Flex;
        centreGlow.style.opacity = title ? .28f : .4f;
    }
    private static void Place(VisualElement e, float x, float y, float w, float h)
    { e.style.left = x - w / 2; e.style.top = y - h / 2; e.style.width = w; e.style.height = h; }

    private static Vector2 Rotate(Vector2 v, float tilt) => new Vector2(v.x * Mathf.Cos(tilt) - v.y * Mathf.Sin(tilt), v.x * Mathf.Sin(tilt) + v.y * Mathf.Cos(tilt));
    private static void Line(Painter2D p, Vector2 a, Vector2 b, float alpha, float width = 1)
    { p.strokeColor = new Color(1, 1, 1, alpha); p.lineWidth = width; p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke(); }
    private static void Ring(Painter2D p, Vector2 c, float rx, float ry, float tilt, float start, float end, float alpha, bool feather = false, bool depth = false)
    {
        int n = Mathf.CeilToInt((end - start) / .025f);
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.Lerp(start, end, (float)i / n), b = Mathf.Lerp(start, end, (float)(i + 1) / n), mid = (a + b) / 2;
            float fade = feather ? Mathf.SmoothStep(0, 1, Mathf.Min(mid - start, end - mid) / .28f) : 1;
            float near = .5f + .5f * Mathf.Sin(mid);
            float opacity = alpha * fade * (depth ? .34f + near * 1.05f : 1);
            Line(p, c + Rotate(new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry), tilt), c + Rotate(new Vector2(Mathf.Cos(b) * rx, Mathf.Sin(b) * ry), tilt), opacity, depth ? .8f + near * .55f : 1);
        }
    }
    private static readonly Vector2[] Corners = { new Vector2(0,-.92f), new Vector2(.3f,-.3f), new Vector2(.92f,0), new Vector2(.3f,.3f), new Vector2(0,.92f), new Vector2(-.3f,.3f), new Vector2(-.92f,0), new Vector2(-.3f,-.3f) };
    private static void Star(Painter2D p, Vector2 c, float size, float alpha, bool fill = false, bool warm = false, float tilt = 0, float flatten = 1)
    {
        Vector2 Pt(int i) { var v = Corners[(i + 8) % 8]; return c + Rotate(new Vector2(v.x * size / 2, v.y * size * .565f * flatten), tilt); }
        p.BeginPath();
        for (int i = 0; i < 8; i++)
        {
            var v = Pt(i); float r = i % 2 == 0 ? .36f : .3f;
            var a = Vector2.Lerp(v, Pt(i - 1), r); var b = Vector2.Lerp(v, Pt(i + 1), r);
            if (i == 0) p.MoveTo(a); else p.LineTo(a);
            p.BezierCurveTo(Vector2.Lerp(a, v, 2f / 3), Vector2.Lerp(b, v, 2f / 3), b);
        }
        p.ClosePath(); var colour = warm ? new Color(1, .94f, .84f, alpha) : new Color(1, 1, 1, alpha);
        if (fill) { p.fillColor = colour; p.Fill(); } else { p.strokeColor = colour; p.lineWidth = size > 50 ? 1.7f : 1.2f; p.Stroke(); }
    }
    private static void Dot(Painter2D p, Vector2 at, float r, float alpha, bool warm = false)
    { p.fillColor = warm ? new Color(1, .96f, .87f, alpha) : new Color(1, 1, 1, alpha); p.BeginPath(); p.Arc(at, r, 0, 360); p.ClosePath(); p.Fill(); }

    private void Draw(MeshGenerationContext context)
    {
        float w = contentRect.width, h = contentRect.height; if (w < 2 || h < 2) return;
        var p = context.painter2D;
        if (title) DrawTitle(p, w, h); else DrawLoading(p, w, h);
    }
    private void DrawTitle(Painter2D p, float w, float h)
    {
        var c = new Vector2(w * .5f, h * .31f);
        float r = w < 600 ? Mathf.Min(w * .34f, 145) : Mathf.Min(w * .25f, 185);
        Ring(p, c, r, r, 0, -2.9f, 1.1f, .43f, true);
        Ring(p, c, r + 12, r + 12, 0, -1.7f, .82f, .22f, true);
        Ring(p, c, r * .73f, r * .73f, 0, 1.25f, 5.2f, .27f, true);
        float tilt = -.45f + Mathf.Sin(clock * .008f) * .12f;
        Ring(p, c, r * 1.32f, r * .5f, tilt, 0, Mathf.PI * 2, .4f);
        float a = clock * .04f + .8f;
        Star(p, c + Rotate(new Vector2(Mathf.Cos(a) * r * 1.32f, Mathf.Sin(a) * r * .5f), tilt), 17, .85f, true, true);
        for (int i = 0; i < 36; i++)
        {
            if (i > 12 && i < 20) continue;
            float angle = -2.8f + i * .047f + clock * .006f;
            var v = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Line(p, c + v * (r - 4), c + v * (r - (i % 6 == 0 ? 12 : 8)), i % 6 == 0 ? .28f : .18f);
        }
        var points = new[] { new Vector2(-.17f,-.16f), new Vector2(-.08f,-.08f), Vector2.zero, new Vector2(.11f,.12f), new Vector2(.22f,.07f) };
        Vector2 At(Vector2 v) => c + new Vector2(v.x * w * .77f, v.y * h * .88f);
        for (int i = 1; i < points.Length; i++) Line(p, At(points[i - 1]), At(points[i]), .26f);
        for (int i = 0; i < points.Length; i++) if (i != 2) Star(p, At(points[i]), i == 0 ? 14 : 10, .7f, true, i == 4);
        float size = 116 * (w < 600 ? .85f : Mathf.Max(.8f, Mathf.Min(w / 1000, h / 590)));
        Star(p, c, size, .97f); Star(p, c + new Vector2(-size * .58f, size * .43f), size * .27f, .8f);
        Star(p, c + new Vector2(size * .53f, -size * .48f), size * .19f, .8f, false, true);
    }
    private void DrawLoading(Painter2D p, float w, float h)
    {
        float r = Mathf.Min(h * .58f, w * .39f); var c = new Vector2(w * .48f, h * .46f);
        Ring(p, c, r, r * .55f, -.43f, 0, Mathf.PI * 2, .4f, false, true);
        Ring(p, c, r - 10, (r - 10) * .55f, -.43f, 0, Mathf.PI * 2, .13f, false, true);
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.PI / 6 - Mathf.PI / 2; bool major = i % 3 == 0;
            var at = c + Rotate(new Vector2(Mathf.Cos(a) * (r - 23), Mathf.Sin(a) * (r - 23) * .55f), -.43f);
            Star(p, at, major ? 16 : 10, (major ? .5f : .31f) * (.7f + (.5f + .5f * Mathf.Sin(a)) * .4f), false, false, -.43f, .72f);
        }
        Dot(p, c, 2.4f, .78f + .08f * Mathf.Sin(clock * .75f), true);
        Dot(p, c + new Vector2(-14, 7), .7f, .4f); Dot(p, c + new Vector2(11,-10), .6f, .4f); Dot(p, c + new Vector2(19,8), .45f, .4f);
        var centre = new Vector2(w * .93f - 58, h * .9f - 68);
        Line(p, centre + new Vector2(-20,-30), centre + new Vector2(20,-30), .95f, 1.6f);
        Line(p, centre + new Vector2(-20,30), centre + new Vector2(20,30), .95f, 1.6f);
        p.strokeColor = new Color(1,1,1,.95f); p.lineWidth = 1.6f;
        foreach (int side in new[] {-1,1})
        {
            Vector2 V(float x, float y) => centre + new Vector2(x * side, y);
            p.BeginPath(); p.MoveTo(V(16,-27)); p.BezierCurveTo(V(16,-10),V(6,-6),V(2,0)); p.BezierCurveTo(V(6,6),V(16,10),V(16,27)); p.Stroke();
        }
        float amount = .5f - .32f * Mathf.Sin(clock / 5 * Mathf.PI * 2);
        p.fillColor = new Color(1,.94f,.83f,.7f);
        p.BeginPath(); p.MoveTo(centre + new Vector2(-10,-23+amount*14)); p.LineTo(centre+new Vector2(10,-23+amount*14)); p.LineTo(centre+new Vector2(0,-5)); p.ClosePath(); p.Fill();
        p.BeginPath(); p.MoveTo(centre+new Vector2(-12,25)); p.LineTo(centre+new Vector2(0,17-amount*9)); p.LineTo(centre+new Vector2(12,25)); p.ClosePath(); p.Fill();
        for (int i=0;i<4;i++) Dot(p, centre+new Vector2(Mathf.Sin(i*2)*.65f,2+(clock*20+i*5)%17), .8f,.75f,true);
        Star(p, centre+new Vector2(-33,-15),9,.35f+.22f*Mathf.Sin(clock*1.1f)); Star(p,centre+new Vector2(31,10),7,.4f,true,true);
    }
    public void Dispose() { generateVisualContent -= Draw; RemoveFromHierarchy(); PNUiRuntime.Retire(light); }
}

// Font scope is deliberately local: no changes to RuntimeTheme, the shared USS or other UI.
internal static class PNTitleLoadingFonts
{
    internal static void Apply(TextElement element, bool bold = false)
    {
        var font = Resources.Load<Font>(bold ? "PNFont/Philosopher-Bold" : "PNFont/Philosopher-Regular");
        if (font != null) element.style.unityFontDefinition = FontDefinition.FromFont(font);
    }
}
#endif
