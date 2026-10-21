#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.UIElements;

// EN: One gate card of the mode ring: the art (or the accent colour) as a textured mesh clipped to the rounded-hexagon gate with a darkening
//     toward the bottom, the name inside, a feathered accent glow, a thin white line and a separate outer outline (a small gap from the card,
//     scaled 1.068 x 1.035). The mesh is rebuilt only when the size or the quantised "front" amount changes.
// VI: Một thẻ cổng của vòng chế độ: ảnh (hoặc màu chủ đạo) cắt theo hình cổng, tên bên trong, glow, viền trắng và viền ngoài tách rời.
internal sealed class PNGateView : VisualElement
{
    private const float Pad = .12f;                     // the overlay extends this fraction of the width past the card
    private static readonly float[] Outline = PNGateShape.Outline(8);
    private static float[] stripY, stripLeft, stripRight;
    private readonly Texture2D art;
    private readonly Color accent, glowColor;
    private readonly VisualElement artLayer, glowLayer, lineLayer;
    private readonly Label nameLabel;
    private float width = 100f, front = 1f, drawnFront = -1f;
    public float Width => width;
    public float Height => width * PNGateShape.Ratio;

    public PNGateView(string name, int[] accentRgb, Texture2D art)
    {
        this.art = art; pickingMode = PickingMode.Ignore;
        accent = new Color(accentRgb[0] / 255f, accentRgb[1] / 255f, accentRgb[2] / 255f, 1f);
        glowColor = Color.Lerp(accent, Color.white, .3f);
        style.position = Position.Absolute; style.overflow = Overflow.Visible;
        if (stripY == null) PNGateShape.Strips(Outline, 24, out stripY, out stripLeft, out stripRight);
        artLayer = Layer(); artLayer.generateVisualContent += DrawArt; Add(artLayer);
        glowLayer = Layer(Pad); glowLayer.generateVisualContent += DrawGlow; Add(glowLayer);
        lineLayer = Layer(Pad); lineLayer.generateVisualContent += DrawLines; Add(lineLayer);
        nameLabel = new Label(name) { pickingMode = PickingMode.Ignore };
        nameLabel.style.position = Position.Absolute; nameLabel.style.left = 0; nameLabel.style.right = 0;
        nameLabel.style.unityTextAlign = TextAnchor.MiddleCenter; nameLabel.style.color = Color.white;
        nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold; nameLabel.style.whiteSpace = WhiteSpace.NoWrap;
        Add(nameLabel);
        SetWidth(width);
    }

    private static VisualElement Layer(float pad = 0)
    {
        var e = new VisualElement { pickingMode = PickingMode.Ignore };
        e.style.position = Position.Absolute;
        e.style.left = Length.Percent(-pad * 100); e.style.right = Length.Percent(-pad * 100);
        e.style.top = Length.Percent(-pad * 100 / PNGateShape.Ratio); e.style.bottom = Length.Percent(-pad * 100 / PNGateShape.Ratio);
        return e;
    }

    public void SetWidth(float w)
    {
        width = Mathf.Max(8f, w);
        style.width = width; style.height = Height;
        nameLabel.style.top = Height * .66f; nameLabel.style.height = Height * .1f; nameLabel.style.fontSize = Mathf.Max(8f, width * .16f);
        drawnFront = -1f; artLayer.MarkDirtyRepaint(); glowLayer.MarkDirtyRepaint(); lineLayer.MarkDirtyRepaint();
    }

    // front: 1 for the gate in front, toward 0 for gates behind (darker art, weaker glow, dimmer name).
    public void SetFront(float value)
    {
        front = Mathf.Clamp01(value);
        glowLayer.style.opacity = .3f + .7f * front;
        nameLabel.style.opacity = .55f + .45f * front;
        if (Mathf.Abs(front - drawnFront) >= .03f || (front >= .999f) != (drawnFront >= .999f)) { drawnFront = front; artLayer.MarkDirtyRepaint(); }
    }

    private Vector2 Map(float x, float y, float scale, float ox, float oy) => new Vector2(ox + x * scale, oy + y * scale);

    private void DrawArt(MeshGenerationContext context)
    {
        float s = width / PNGateShape.Width;
        int rows = stripY.Length;
        var vertices = new Vertex[rows * 2]; var indices = new ushort[(rows - 1) * 6];
        // Cover crop (centred): scale the picture to fill the card box, cut what sticks out.
        float u0 = 0, u1 = 1, v0 = 0, v1 = 1;
        if (art != null && art.width > 0 && art.height > 0)
        {
            float boxRatio = PNGateShape.Width / PNGateShape.Height, texRatio = art.width / (float)art.height;
            if (texRatio > boxRatio) { float span = boxRatio / texRatio; u0 = (1 - span) * .5f; u1 = u0 + span; }
            else { float span = texRatio / boxRatio; v0 = (1 - span) * .5f; v1 = v0 + span; }
        }
        float dark = 1f - front;
        for (int k = 0; k < rows; k++)
        {
            float y = stripY[k], t = Mathf.Clamp01((y - 80f) / 149f);
            // Darker toward the bottom (the name sits there) and for gates behind the front one.
            float shade = (1f - .55f * t * t * (3 - 2 * t)) * (1f - .38f * dark);
            Color32 tint = art != null ? new Color(shade, shade, shade, 1f) : new Color(accent.r * shade, accent.g * shade, accent.b * shade, 1f);
            for (int side = 0; side < 2; side++)
            {
                float x = side == 0 ? stripLeft[k] : stripRight[k];
                float u = Mathf.Lerp(u0, u1, x / PNGateShape.Width), v = 1f - Mathf.Lerp(v0, v1, y / PNGateShape.Height);
                vertices[k * 2 + side] = new Vertex { position = new Vector3(x * s, y * s, Vertex.nearZ), tint = tint, uv = new Vector2(u, v) };
            }
            if (k < rows - 1)
            {
                int a = k * 2, b = k * 2 + 1, c = k * 2 + 2, d = k * 2 + 3, i = k * 6;
                indices[i] = (ushort)a; indices[i + 1] = (ushort)b; indices[i + 2] = (ushort)c;
                indices[i + 3] = (ushort)b; indices[i + 4] = (ushort)d; indices[i + 5] = (ushort)c;
            }
        }
        var mesh = context.Allocate(vertices.Length, indices.Length, art != null ? art : Texture2D.whiteTexture);
        mesh.SetAllVertices(vertices); mesh.SetAllIndices(indices);
    }

    private void Path(Painter2D p, float scale, float ox, float oy, float aboutX, float aboutY, float sx, float sy)
    {
        p.BeginPath();
        for (int i = 0; i < Outline.Length; i += 2)
        {
            float x = aboutX + (Outline[i] - aboutX) * sx, y = aboutY + (Outline[i + 1] - aboutY) * sy;
            var point = Map(x, y, scale, ox, oy);
            if (i == 0) p.MoveTo(point); else p.LineTo(point);
        }
        p.ClosePath();
    }

    private void DrawGlow(MeshGenerationContext context)
    {
        var p = context.painter2D; float s = width / PNGateShape.Width, ox = width * Pad, oy = width * Pad;
        p.lineJoin = LineJoin.Round;
        p.strokeColor = new Color(glowColor.r, glowColor.g, glowColor.b, .16f); p.lineWidth = 9f * s; Path(p, s, ox, oy, 40, 114.5f, 1, 1); p.Stroke();
        p.strokeColor = new Color(glowColor.r, glowColor.g, glowColor.b, .32f); p.lineWidth = 4.5f * s; Path(p, s, ox, oy, 40, 114.5f, 1, 1); p.Stroke();
    }

    private void DrawLines(MeshGenerationContext context)
    {
        var p = context.painter2D; float s = width / PNGateShape.Width, ox = width * Pad, oy = width * Pad;
        p.lineJoin = LineJoin.Round;
        p.strokeColor = new Color(1, 1, 1, .9f); p.lineWidth = Mathf.Max(1f, 1.1f * s); Path(p, s, ox, oy, 40, 114.5f, 1, 1); p.Stroke();
        // The outer outline is separate: a small gap from the card, half the thickness it first had.
        p.strokeColor = new Color(1, 1, 1, .8f); p.lineWidth = Mathf.Max(.5f, .6f * s); Path(p, s, ox, oy, 40, 114.5f, 1.068f, 1.035f); p.Stroke();
    }
}
#endif
