#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// EN: Shared backdrop of the common menu screens (mode, setup, room list): the grey panel surface, a soft aura that can be tinted and
//     twinkling sky stars (pause timings: life 2.4-5.4 s). The character screen and the pause menu build their own backgrounds, so the
//     decoration (aura and stars) fades away for a screen that does not use it while the grey surface stays.
// VI: Nền chung cho các màn dùng chung; màn nhân vật và pause tự dựng nền nên phần trang trí mờ đi còn nền xám vẫn giữ.
internal sealed class PNMenuBackdrop : IDisposable
{
    private sealed class Glint { internal VisualElement Element; internal Vector2 Center; internal float Start, Life; }
    private readonly VisualElement surface, decoration, aura, sky, orbitHost, orbitStar;
    private readonly VisualElement[] orbits = new VisualElement[2];
    private bool orbitsOn, orbitEverOn;
    private float orbitClock, orbitTime;
    private readonly Texture2D light;
    private readonly List<Glint> glints = new List<Glint>();
    private readonly System.Random random = new System.Random();
    private float time, decorationAlpha = 1f, surfaceAlpha;
    private Color tint = Color.white, shownTint = Color.white;

    public VisualElement Root => surface;

    public PNMenuBackdrop()
    {
        surface = new VisualElement { name = "pn-menu-backdrop", pickingMode = PickingMode.Position };
        surface.AddToClassList("pn-panel-surface");
        surface.style.position = Position.Absolute; surface.style.left = 0; surface.style.right = 0; surface.style.top = 0; surface.style.bottom = 0; surface.style.overflow = Overflow.Hidden;
        decoration = new VisualElement { name = "pn-menu-decoration", pickingMode = PickingMode.Ignore };
        Stretch(decoration); surface.Add(decoration);
        light = PNPauseArtwork.Gradient(false);
        aura = new VisualElement { name = "pn-menu-aura", pickingMode = PickingMode.Ignore };
        aura.style.position = Position.Absolute; aura.style.left = Length.Percent(50); aura.style.top = Length.Percent(50);
        aura.style.width = 900; aura.style.height = 900; aura.style.marginLeft = -450; aura.style.marginTop = -450;
        aura.style.backgroundImage = light; decoration.Add(aura);
        sky = new VisualElement { name = "pn-menu-sky", pickingMode = PickingMode.Ignore }; Stretch(sky); decoration.Add(sky);
        var positions = new[] { new Vector2(13, 17), new Vector2(32, 28), new Vector2(42, 69), new Vector2(9, 77), new Vector2(52, 15), new Vector2(28, 85), new Vector2(82, 17), new Vector2(91, 78) };
        for (int i = 0; i < positions.Length; i++)
        {
            var star = new PNPauseDrawing(PNPauseDrawing.Shape.Star);
            star.style.position = Position.Absolute; star.style.width = i == 0 ? 18 : 12; star.style.height = i == 0 ? 22 : 16;
            sky.Add(star);
            var glint = new Glint { Element = star, Center = positions[i] }; glints.Add(glint); Reset(glint, -(float)random.NextDouble() * 3);
        }
        // The two tilted orbits (the small one with a travelling star) are part of the shared backdrop; they grow in / collapse out with the
        // pause menu's timings whenever the shown screen uses the backdrop, independently of the fading aura.
        orbitHost = new VisualElement { name = "pn-menu-orbits", pickingMode = PickingMode.Ignore }; Stretch(orbitHost); surface.Add(orbitHost);
        orbits[1] = Orbit(); orbits[0] = Orbit(); orbitHost.Add(orbits[1]); orbitHost.Add(orbits[0]);
        orbitStar = new PNPauseDrawing(PNPauseDrawing.Shape.Star) { name = "pn-menu-star" };
        orbitStar.style.position = Position.Absolute; orbitStar.style.width = 24; orbitStar.style.height = 28; orbitHost.Add(orbitStar);
        orbits[0].style.opacity = 0; orbits[1].style.opacity = 0; orbitStar.style.opacity = 0;
        surface.style.opacity = 0;
    }

    private static VisualElement Orbit() { var o = new PNPauseDrawing(PNPauseDrawing.Shape.Orbit); o.style.position = Position.Absolute; return o; }

    // true: the orbits grow in (from where they are); false: they collapse out. Does nothing when already heading there.
    public void SetOrbits(bool on)
    {
        if (on == orbitsOn) return;
        orbitsOn = on; orbitClock = 0f;
    }
    public bool OrbitsOn => orbitsOn;

    private void TickOrbits(float dt)
    {
        orbitClock += dt; orbitTime += dt;
        PNMenuMotion.Orbit(orbitsOn, orbitClock, 0, out var sx0, out var sy0, out var a0);
        PNMenuMotion.Orbit(orbitsOn, orbitClock, 1, out var sx1, out var sy1, out var a1);
        // Before the very first grow-in the orbits are invisible; after a collapse they stay gone.
        if (!orbitsOn && !orbitEverOn) { a0 = a1 = 0; }
        if (orbitsOn) orbitEverOn = true;
        orbits[0].style.scale = new Scale(new Vector2(sx0, sy0)); orbits[0].style.opacity = a0 * .7f;
        orbits[1].style.scale = new Scale(new Vector2(sx1, sy1)); orbits[1].style.opacity = a1 * .52f;
        orbitStar.style.opacity = a0;
        float w = surface.resolvedStyle.width, h = surface.resolvedStyle.height;
        if (!float.IsFinite(w) || !float.IsFinite(h) || w < 2 || h < 2) return;
        float k = Mathf.Clamp(h / 600f, .8f, 1.5f), cx = w * .5f, cy = h * .54f;
        float[] rx = { 222f * k, 390f * k }, ry = { 100f * k, 245f * k }, tilt = { -27f, 12f };
        for (int i = 0; i < 2; i++)
        {
            var o = orbits[i];
            o.style.width = rx[i] * 2; o.style.height = ry[i] * 2; o.style.left = cx - rx[i]; o.style.top = cy - ry[i]; o.style.rotate = new Rotate(tilt[i]);
        }
        // The star keeps the small orbit's scale and tilt, one lap in 26 s.
        float angle = (.14f + orbitTime / 26f) * Mathf.PI * 2f, t = tilt[0] * Mathf.Deg2Rad;
        var point = new Vector2(rx[0] * Mathf.Cos(angle) * sx0, ry[0] * Mathf.Sin(angle) * sy0);
        var at = new Vector2(point.x * Mathf.Cos(t) - point.y * Mathf.Sin(t), point.x * Mathf.Sin(t) + point.y * Mathf.Cos(t));
        orbitStar.style.left = cx + at.x - 12f; orbitStar.style.top = cy + at.y - 14f;
    }

    private static void Stretch(VisualElement e) { e.style.position = Position.Absolute; e.style.left = 0; e.style.right = 0; e.style.top = 0; e.style.bottom = 0; }

    // Opacity of the whole backdrop (the menu's fade in / out).
    public float SurfaceAlpha { get => surfaceAlpha; set { surfaceAlpha = Mathf.Clamp01(value); surface.style.opacity = surfaceAlpha; } }
    // 0 = grey surface only, 1 = with aura and stars.
    public float DecorationAlpha { get => decorationAlpha; set { decorationAlpha = Mathf.Clamp01(value); decoration.style.opacity = decorationAlpha; } }
    // Colour the aura glows in (the centred gate's accent); changes ease in.
    public Color Tint { get => tint; set => tint = value; }

    public void Tick(float dt)
    {
        time += dt;
        TickOrbits(dt);
        shownTint = Color.Lerp(shownTint, tint, 1f - Mathf.Exp(-6f * dt));
        aura.style.unityBackgroundImageTintColor = new Color(shownTint.r, shownTint.g, shownTint.b, .55f);
        foreach (var glint in glints)
        {
            float t = (time - glint.Start) / glint.Life;
            if (t >= 1) { Reset(glint, time + .25f + (float)random.NextDouble() * .9f); t = -1; }
            float alpha = t < 0 ? 0 : Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(t)), 2) * .75f;
            glint.Element.style.opacity = alpha; float size = .65f + alpha * .5f; glint.Element.style.scale = new Scale(new Vector2(size, size));
        }
    }

    private void Reset(Glint glint, float start)
    {
        glint.Start = start; glint.Life = 2.4f + (float)random.NextDouble() * 3;
        glint.Element.style.opacity = 0;
        glint.Element.style.left = Length.Percent(Mathf.Clamp(glint.Center.x + ((float)random.NextDouble() - .5f) * 12, 3, 97));
        glint.Element.style.top = Length.Percent(Mathf.Clamp(glint.Center.y + ((float)random.NextDouble() - .5f) * 12, 3, 97));
    }

    public void Dispose()
    {
        surface.RemoveFromHierarchy(); PNUiRuntime.Retire(light);
    }
}
#endif
