using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;

// EN: A stamp (Docs/SandboxPlan.md, step G9): a piece of map that can be put down again, in the same cell form as the world (ground types by NAME, floor heights, solid flags) plus the placed things inside it.
//     Corner and size are multiples of the 2 m ground cell, at most 128 x 128 cells. A map file and a stamp use the same kinds of data, so the generator and the tool share it. Pure C#.
// VI: Mẩu map: một mảnh map đặt lại được, cùng dạng ô như world (loại đất theo TÊN, độ cao, ô đặc) cùng các vật đặt bên trong. Góc và cỡ là bội của ô đất 2 m, tối đa 128 x 128 ô.
public sealed class PNStampThing { public string Definition; public int X, Z, Direction; }

public sealed class PNGridStamp
{
    public const int MaxSide = 128, Format = 1, MaxThings = 4096;
    public int Width { get; }
    public int Depth { get; }
    public int GroundFactor { get; }
    public IReadOnlyList<string> Palette { get; }
    public IReadOnlyList<PNStampThing> Things { get; }
    private readonly ushort[] ground;   // ground cells (Width / GroundFactor x Depth / GroundFactor)
    private readonly short[] height;
    private readonly byte[] solid;

    public PNGridStamp(int width, int depth, int groundFactor, IReadOnlyList<string> palette, ushort[] ground, short[] height, byte[] solid, IReadOnlyList<PNStampThing> things)
    {
        if (groundFactor < 1 || width < groundFactor || depth < groundFactor || width > MaxSide || depth > MaxSide || width % groundFactor != 0 || depth % groundFactor != 0) throw new FormatException("A stamp is a whole number of ground cells, at most " + MaxSide + " cells on a side.");
        if (palette == null || palette.Count == 0 || palette.Count > PNGridWorldLimits.MaxPalette || palette.Any(string.IsNullOrEmpty) || palette.Distinct(StringComparer.Ordinal).Count() != palette.Count) throw new FormatException("A stamp needs distinct ground names.");
        int gc = (width / groundFactor) * (depth / groundFactor);
        if (ground == null || height == null || solid == null || ground.Length != gc || height.Length != width * depth || solid.Length != width * depth) throw new FormatException("A stamp's arrays do not match its size.");
        if (ground.Any(x => x >= palette.Count)) throw new FormatException("A stamp's ground type is not in its palette.");
        if (solid.Any(x => x > 1)) throw new FormatException("A solid flag is 0 or 1.");
        things = things ?? Array.Empty<PNStampThing>();
        if (things.Count > MaxThings) throw new FormatException("A stamp holds at most " + MaxThings + " things.");
        foreach (var t in things)
            if (t == null || string.IsNullOrEmpty(t.Definition) || t.Definition.Length > PNPlaceableLimits.MaxText || t.X < 0 || t.Z < 0 || t.X >= width || t.Z >= depth || t.Direction < 0 || t.Direction > 3) throw new FormatException("A stamp's thing is invalid.");
        Width = width; Depth = depth; GroundFactor = groundFactor; Palette = Array.AsReadOnly(palette.ToArray());
        this.ground = (ushort[])ground.Clone(); this.height = (short[])height.Clone(); this.solid = (byte[])solid.Clone(); Things = Array.AsReadOnly(things.ToArray());
    }

    public ushort GroundAt(int gx, int gz) => ground[gz * (Width / GroundFactor) + gx];
    public short HeightAt(int x, int z) => height[z * Width + x];
    public bool SolidAt(int x, int z) => solid[z * Width + x] != 0;

    // Index of each of this stamp's ground names in `palette` (the world's); a name the world does not have is refused.
    public int[] PaletteMap(IReadOnlyList<string> palette)
    {
        var map = new int[Palette.Count];
        for (int i = 0; i < map.Length; i++)
        {
            int at = -1; for (int j = 0; j < palette.Count; j++) if (palette[j] == Palette[i]) { at = j; break; }
            if (at < 0) throw new InvalidOperationException("The world has no ground type " + Palette[i] + ".");
            map[i] = at;
        }
        return map;
    }

    public static PNGridStamp Capture(PNGridWorldData data, PNGridPlacements placements, PNGridRegion region)
    {
        int f = data.GroundFactor;
        if (!region.IsValid || region.X0 % f != 0 || region.Z0 % f != 0 || region.Width % f != 0 || region.Depth % f != 0) throw new InvalidOperationException("A stamp is copied in whole ground cells (" + f + " cells).");
        if (region.Width > MaxSide || region.Depth > MaxSide) throw new InvalidOperationException("A stamp is at most " + MaxSide + " cells on a side.");
        int w = region.Width, d = region.Depth, gw = w / f, gd = d / f;
        var ground = new ushort[gw * gd]; var height = new short[w * d]; var solid = new byte[w * d];
        for (int gz = 0; gz < gd; gz++) for (int gx = 0; gx < gw; gx++) ground[gz * gw + gx] = data.GroundCell(region.X0 / f + gx, region.Z0 / f + gz);
        for (int z = 0; z < d; z++) for (int x = 0; x < w; x++)
        { height[z * w + x] = data.HeightRaw(region.X0 + x, region.Z0 + z); solid[z * w + x] = data.IsSolid(region.X0 + x, region.Z0 + z) ? (byte)1 : (byte)0; }
        var things = new List<PNStampThing>();
        foreach (var t in placements.InRegion(region).OrderBy(x => x.Id))
        {
            if (t.X < region.X0 || t.Z < region.Z0 || t.X + t.CellsX - 1 > region.X1 || t.Z + t.CellsZ - 1 > region.Z1) throw new InvalidOperationException("The region cuts through a " + t.Definition.Id + ".");
            things.Add(new PNStampThing { Definition = t.Definition.Id.ToString(), X = t.X - region.X0, Z = t.Z - region.Z0, Direction = t.Direction });
        }
        return new PNGridStamp(w, d, f, data.Palette, ground, height, solid, things);
    }

    public JObject ToJson()
    {
        var raw = new byte[ground.Length * 2 + height.Length * 2 + solid.Length]; int at = 0;
        foreach (var v in ground) { raw[at++] = (byte)v; raw[at++] = (byte)(v >> 8); }
        foreach (var v in height) { raw[at++] = (byte)v; raw[at++] = (byte)(v >> 8); }
        Buffer.BlockCopy(solid, 0, raw, at, solid.Length);
        string packed;
        using (var output = new MemoryStream())
        {
            using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, true)) deflate.Write(raw, 0, raw.Length);
            packed = Convert.ToBase64String(output.ToArray());
        }
        return new JObject
        {
            ["format"] = Format, ["width"] = Width, ["depth"] = Depth, ["factor"] = GroundFactor, ["palette"] = new JArray(Palette), ["cells"] = packed,
            ["things"] = new JArray(Things.Select(t => new JObject { ["def"] = t.Definition, ["x"] = t.X, ["z"] = t.Z, ["dir"] = t.Direction }))
        };
    }

    public static PNGridStamp Parse(JToken token)
    {
        var obj = token as JObject ?? throw new FormatException("A stamp is a JSON object.");
        foreach (var p in obj.Properties()) if (p.Name != "format" && p.Name != "width" && p.Name != "depth" && p.Name != "factor" && p.Name != "palette" && p.Name != "cells" && p.Name != "things") throw new FormatException("Unknown stamp field: " + p.Name);
        int Int(string name) => obj[name]?.Type == JTokenType.Integer ? checked((int)obj[name]) : throw new FormatException("Stamp field " + name + " must be an integer.");
        if (Int("format") != Format) throw new FormatException("Unknown stamp format.");
        int w = Int("width"), d = Int("depth"), f = Int("factor");
        if (f < 1 || w < f || d < f || w > MaxSide || d > MaxSide || w % f != 0 || d % f != 0) throw new FormatException("A stamp is a whole number of ground cells, at most " + MaxSide + " cells on a side.");
        if (!(obj["palette"] is JArray names) || names.Any(x => x.Type != JTokenType.String)) throw new FormatException("A stamp's palette is an array of names.");
        if (obj["cells"]?.Type != JTokenType.String) throw new FormatException("A stamp's cells are a base64 string.");
        int gc = (w / f) * (d / f), expected = gc * 2 + w * d * 2 + w * d;
        var raw = new byte[expected];
        try
        {
            using (var input = new MemoryStream(Convert.FromBase64String((string)obj["cells"])))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < expected) { int n = deflate.Read(raw, read, expected - read); if (n == 0) throw new FormatException("A stamp's cells are shorter than its size."); read += n; }
                if (deflate.ReadByte() != -1) throw new FormatException("A stamp's cells are longer than its size.");
            }
        }
        catch (InvalidDataException error) { throw new FormatException("A stamp's cells are damaged: " + error.Message); }
        catch (FormatException) { throw; }
        var ground = new ushort[gc]; var height = new short[w * d]; var solid = new byte[w * d]; int at = 0;
        for (int i = 0; i < gc; i++, at += 2) ground[i] = (ushort)(raw[at] | raw[at + 1] << 8);
        for (int i = 0; i < height.Length; i++, at += 2) height[i] = (short)(raw[at] | raw[at + 1] << 8);
        Buffer.BlockCopy(raw, at, solid, 0, solid.Length);
        var things = new List<PNStampThing>();
        if (obj["things"] != null)
        {
            if (!(obj["things"] is JArray list)) throw new FormatException("A stamp's things are an array.");
            foreach (var item in list)
            {
                var o = item as JObject ?? throw new FormatException("A stamp's thing is an object.");
                foreach (var p in o.Properties()) if (p.Name != "def" && p.Name != "x" && p.Name != "z" && p.Name != "dir") throw new FormatException("Unknown stamp thing field: " + p.Name);
                if (o["def"]?.Type != JTokenType.String || o["x"]?.Type != JTokenType.Integer || o["z"]?.Type != JTokenType.Integer || o["dir"]?.Type != JTokenType.Integer) throw new FormatException("A stamp's thing has the wrong field types.");
                things.Add(new PNStampThing { Definition = (string)o["def"], X = (int)o["x"], Z = (int)o["z"], Direction = (int)o["dir"] });
            }
        }
        return new PNGridStamp(w, d, f, names.Select(x => (string)x).ToList(), ground, height, solid, things);
    }
}
