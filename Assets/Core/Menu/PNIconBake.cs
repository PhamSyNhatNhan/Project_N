using System;

// EN: Pixel maths for the character-button icon (UI_Icon_Charator*). The artwork is black lines on a transparent/white sheet; the button wants white
//     lines on transparent, a little thicker, with a soft halo like the pause star. Everything works on float alpha planes (row-major, width*height),
//     so it stays testable without Unity types; PNCharacterIconArt does the texture reads and writes.
// VI: Tính điểm ảnh cho icon nút nhân vật: nét đen -> nét trắng nền trong suốt, dày thêm một chút, có quầng mờ như ngôi sao pause.
public static class PNIconBake
{
    // White-line alpha of one source pixel: the line is dark, the paper is light or transparent.
    public static float LineAlpha(float r, float g, float b, float a)
    {
        float darkness = 1f - (r + g + b) / 3f;
        return Clamp01(a) * Clamp01(darkness);
    }

    // Union of two coverage planes (1 - (1-a)(1-b)); the result goes into `into`.
    public static void Union(float[] into, float[] other)
    {
        if (into == null || other == null || into.Length != other.Length) throw new ArgumentException("planes must have the same size");
        for (int i = 0; i < into.Length; i++) into[i] = 1f - (1f - into[i]) * (1f - other[i]);
    }

    // Halves the plane size (box filter); the source sides must be even.
    public static float[] Downsample2(float[] src, int width, int height)
    {
        if (src == null || width <= 0 || height <= 0 || (width & 1) != 0 || (height & 1) != 0 || src.Length != width * height) throw new ArgumentException("bad plane size");
        int w = width / 2, h = height / 2; var dst = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (2 * y) * width + 2 * x;
                dst[y * w + x] = (src[i] + src[i + 1] + src[i + width] + src[i + width + 1]) * .25f;
            }
        return dst;
    }

    // Grows the lines: each pixel takes the maximum of its (2r+1)^2 neighbourhood. One texel of a 512 px plane shown at 56 px is 0.11 px per side.
    public static float[] Dilate(float[] src, int width, int height, int radius)
    {
        if (src == null || src.Length != width * height || radius < 0) throw new ArgumentException("bad plane or radius");
        if (radius == 0) return (float[])src.Clone();
        var rows = new float[src.Length];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float m = 0; int x0 = Math.Max(0, x - radius), x1 = Math.Min(width - 1, x + radius);
                for (int k = x0; k <= x1; k++) m = Math.Max(m, src[y * width + k]);
                rows[y * width + x] = m;
            }
        var dst = new float[src.Length];
        for (int y = 0; y < height; y++)
        {
            int y0 = Math.Max(0, y - radius), y1 = Math.Min(height - 1, y + radius);
            for (int x = 0; x < width; x++)
            {
                float m = 0;
                for (int k = y0; k <= y1; k++) m = Math.Max(m, rows[k * width + x]);
                dst[y * width + x] = m;
            }
        }
        return dst;
    }

    // Two box-blur passes (radius in texels) of the plane: the soft halo.
    public static float[] Blur(float[] src, int width, int height, int radius)
    {
        if (src == null || src.Length != width * height || radius < 0) throw new ArgumentException("bad plane or radius");
        var a = (float[])src.Clone();
        if (radius == 0) return a;
        var b = new float[a.Length];
        for (int pass = 0; pass < 2; pass++) { BoxPass(a, b, width, height, radius, true); BoxPass(b, a, width, height, radius, false); }
        return a;
    }

    private static void BoxPass(float[] src, float[] dst, int width, int height, int radius, bool horizontal)
    {
        int lines = horizontal ? height : width, length = horizontal ? width : height, stride = horizontal ? 1 : width, lineStep = horizontal ? width : 1;
        float scale = 1f / (2 * radius + 1);
        for (int line = 0; line < lines; line++)
        {
            int o = line * lineStep; float sum = 0;
            for (int k = -radius; k <= radius; k++) sum += src[o + Math.Min(length - 1, Math.Max(0, k)) * stride];
            for (int i = 0; i < length; i++)
            {
                dst[o + i * stride] = sum * scale;
                int add = Math.Min(length - 1, i + radius + 1), sub = Math.Max(0, i - radius);
                sum += src[o + add * stride] - src[o + sub * stride];
            }
        }
    }

    // Final alpha: the lines at full strength, the halo (blurred lines) at `haloStrength` behind them.
    public static float[] WithHalo(float[] lines, float[] blurred, float haloStrength)
    {
        if (lines == null || blurred == null || lines.Length != blurred.Length) throw new ArgumentException("planes must have the same size");
        var dst = new float[lines.Length];
        for (int i = 0; i < dst.Length; i++) dst[i] = Math.Max(lines[i], Clamp01(blurred[i] * haloStrength * 2f));
        return dst;
    }

    private static float Clamp01(float v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
}
