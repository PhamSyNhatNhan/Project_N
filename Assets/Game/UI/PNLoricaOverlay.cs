#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using UnityEngine.UIElements;

// EN: Lorica as a silver-blue glass rim over the enemy health bar (docs: Docs/EnemyLoricaDesign.md). Drawn with Painter2D, no image, nothing moves: the width follows the lorica ratio
//     like a second health bar; the middle is almost clear (alpha .06) so the white health below stays fully readable, and three nested outlines in a soft blue-grey deepen
//     toward the rounded border. No streaks or veins (the HUD is flat), a thin pale edge along the top and at the current lorica level. A broken lorica fades out; while it
//     is locked (recovery) it is dimmer. The ring border around the bar is untouched. Presentation only. Colour chosen by the user on 2026-10-21 (candidate 5 of the review).
// VI: Lorica là viền kính xanh bạc nhạt phủ trên thanh máu: giữa gần trong suốt, viền lồng đậm dần ra mép, không vân. Khi vỡ mờ dần; khi bị khoá thì mờ hơn.
internal sealed class PNLoricaOverlay : VisualElement
{
    private const float FadeSeconds = .35f;
    private float ratio, shownRatio, alpha, lastNow, drawnRatio, drawnAlpha; private bool recovery, drawnRecovery;
    public PNLoricaOverlay()
    {
        name = "lorica-overlay"; pickingMode = PickingMode.Ignore;
        style.position = Position.Absolute; style.left = 0; style.right = 0; style.top = 0; style.bottom = 0; style.display = DisplayStyle.None;
        generateVisualContent += Draw;
    }
    // `ratio` 0..1 of lorica, `has` false when the enemy has no lorica at all. Call every frame the bar is visible.
    public void Present(bool has, float ratio, bool recovery, float now)
    {
        float delta = lastNow == 0f ? 0f : Mathf.Clamp(now - lastNow, 0f, .25f); lastNow = now;
        this.recovery = recovery;
        float target = has && ratio > 0f ? 1f : 0f;
        if (has && ratio > 0f) shownRatio = Mathf.Clamp01(ratio);
        alpha = Mathf.MoveTowards(alpha, target, delta / FadeSeconds);
        this.ratio = ratio;
        bool visible = alpha > .004f;
        style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        // Nothing animates, so the plate is redrawn only when its width, opacity or state changed.
        if (visible && (Mathf.Abs(shownRatio - drawnRatio) > .002f || Mathf.Abs(alpha - drawnAlpha) > .004f || recovery != drawnRecovery)) MarkDirtyRepaint();
    }
    private static void Pill(Painter2D p, float x, float y, float w, float h, float r)
    {
        r = Mathf.Min(r, w * .5f, h * .5f);
        p.BeginPath(); p.MoveTo(new Vector2(x + r, y)); p.LineTo(new Vector2(x + w - r, y));
        p.Arc(new Vector2(x + w - r, y + r), r, Angle.Degrees(-90), Angle.Degrees(0)); p.LineTo(new Vector2(x + w, y + h - r));
        p.Arc(new Vector2(x + w - r, y + h - r), r, Angle.Degrees(0), Angle.Degrees(90)); p.LineTo(new Vector2(x + r, y + h));
        p.Arc(new Vector2(x + r, y + h - r), r, Angle.Degrees(90), Angle.Degrees(180)); p.LineTo(new Vector2(x, y + r));
        p.Arc(new Vector2(x + r, y + r), r, Angle.Degrees(180), Angle.Degrees(270)); p.ClosePath();
    }
    private void Draw(MeshGenerationContext context)
    {
        drawnRatio = shownRatio; drawnAlpha = alpha; drawnRecovery = recovery;
        float width = contentRect.width, height = contentRect.height;
        float fill = width * shownRatio; if (width <= 0f || height <= 0f || fill < 1f || alpha <= .004f) return;
        float a = alpha * (recovery ? .55f : 1f), radius = height * .5f; var p = context.painter2D;
        // Almost clear body: the health bar below shows through.
        Pill(p, 0, 0, fill, height, radius); p.fillColor = new Color(.55f, .76f, .92f, .06f * a); p.Fill();
        // Soft blue-grey rim: three nested outlines, each one pixel further in and fainter (the plate is only 8 px tall).
        float[] depth = { .62f, .38f, .18f };
        for (int k = 0; k < depth.Length; k++)
        {
            float inset = .5f + k; float w = fill - 2f * inset, h = height - 2f * inset; if (w < 1f || h < 1f) break;
            Pill(p, inset, inset, w, h, Mathf.Max(.5f, radius - inset)); p.lineWidth = 1f; p.strokeColor = new Color(.38f, .62f, .82f, Mathf.Clamp01(depth[k] * 1.55f) * a); p.Stroke();
        }
        // A thin pale edge along the top and the edge at the current lorica level.
        p.lineWidth = .8f; p.strokeColor = new Color(.90f, .96f, 1f, .55f * a); p.BeginPath(); p.MoveTo(new Vector2(radius, 1.1f)); p.LineTo(new Vector2(Mathf.Max(radius, fill - radius), 1.1f)); p.Stroke();
        if (fill < width - .5f) { p.strokeColor = new Color(.90f, .96f, 1f, .75f * a); p.lineWidth = 1f; p.BeginPath(); p.MoveTo(new Vector2(fill - .6f, 1.2f)); p.LineTo(new Vector2(fill - .6f, height - 1.2f)); p.Stroke(); }
    }
}

// Reads what the overlay and the break effect need, from the replicated stat and Buff presentation (works on host, offline and clients).
internal static class PNLoricaView
{
    public static bool TryRead(PNEnemyStat stat, out float ratio, out bool recovery)
    {
        ratio = 0f; recovery = false;
        if (stat == null || !stat.IsReady || !EveRegistrySession.IsReady) return false;
        double current, max;
        if (EveRegistrySession.Context.HasAuthority)
        {
            if (!stat.HasLorica) return false;
            current = stat.LoricaCurrent; max = stat.GetMax(PNContentIds.Lorica);
        }
        else if (!PNVitals.TryRead(stat, PNContentIds.Lorica, out current, out max, out _) || max <= 0d) return false;
        ratio = (float)Math.Max(0d, Math.Min(1d, current / max));
        var presentation = stat.GetComponent<PNBuffManager>()?.Presentation;
        if (presentation != null)
            foreach (var state in presentation.PresentationSnapshot()) if (state.Id == PNContentIds.LoricaRecovery) { recovery = true; break; }
        return true;
    }
}
#endif
