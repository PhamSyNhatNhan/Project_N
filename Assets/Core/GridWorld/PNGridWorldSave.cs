using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: The saved form of a running grid world (Docs/SandboxPlan.md, step G7). Pure C#: the smoke suite checks it. A save keeps what a player made and nothing that the game rebuilds by itself: the ground (types,
//     floor heights, solid cells), the placed things, and where each player stood. Ordinary enemies are NOT kept (the world decides, see the plan) and neither are the players' bodies. The cells are cut into the
//     same chunks of 32 x 32 cells as the world, and every chunk is one small deflated blob, so a save only encodes the chunks that changed since the last one. It travels inside the existing portable world
//     envelope (`PNWorldSaveEnvelope`), in the one `world` entry of its single state record, so the file store, the format checks and the compatibility check are the ones the rest of the project uses.
// VI: Dạng lưu của world lưới đang chạy (G7). Thuần C#. Lưu thứ người chơi tạo (nền: loại đất, độ cao, ô đặc; vật đặt; chỗ đứng của từng người chơi); KHÔNG lưu quái thường và thân người chơi. Ô cắt theo mảnh
//     32 x 32 như world, mỗi mảnh là một blob deflate nhỏ nên mỗi lần lưu chỉ mã hóa mảnh đã đổi. Đi trong phong bì world portable có sẵn.
public static class PNGridSaveIds
{
    // The world feature that gives a grid world its save service.
    public static readonly EveId Feature = EveId.Parse("project_n:world_save");
    public const string Kind = "project_n:grid_world";
}

public static class PNGridSaveLimits
{
    public const int Format = 1, MaxPlayers = 64, MaxKeyLength = 64, MaxChunkBlobBytes = 1 << 16;
}

// A thing placed in the world, as the save keeps it.
public sealed class PNGridSavedPlacement
{
    public int Id, X, Z, Direction, Health;
    public string Definition;
}

// Where a player stood, by the player's key (`owner:<id>` for the owner id of its identity).
public sealed class PNGridSavedPlayer
{
    public string Key;
    public float X, Y, Z;
}

// An immutable-by-convention copy of one chunk, taken on the main thread and encoded on a worker.
public sealed class PNGridChunkCopy
{
    public int Cx { get; }
    public int Cz { get; }
    public int Index { get; }
    // The chunk version at the moment of the copy.
    public int Version { get; }
    public ushort[] Ground { get; private set; }
    public short[] Height { get; private set; }
    public byte[] Solid { get; private set; }

    private PNGridChunkCopy(int cx, int cz, int index, int version, ushort[] ground, short[] height, byte[] solid)
    { Cx = cx; Cz = cz; Index = index; Version = version; Ground = ground; Height = height; Solid = solid; }

    // The three arrays of a copy are reused: a save that copies hundreds of chunks on the main thread would otherwise allocate about 3 KB per chunk, and a garbage collection in the middle of a capture frame is the one hitch
    // the time budget cannot prevent. `Release` (called by whoever is done with the copy, normally the worker after `Encode`) hands the arrays back; the copy must not be used afterwards.
    private static readonly Stack<ushort[]> groundPool = new Stack<ushort[]>();
    private static readonly Stack<short[]> heightPool = new Stack<short[]>();
    private static readonly Stack<byte[]> solidPool = new Stack<byte[]>();
    private static readonly object poolLock = new object();
    private const int PoolLimit = 2048;
    public static int PooledCount { get { lock (poolLock) return solidPool.Count; } }
    public static void ClearPool() { lock (poolLock) { groundPool.Clear(); heightPool.Clear(); solidPool.Clear(); } }

    public void Release()
    {
        var ground = Ground; var height = Height; var solid = Solid;
        if (ground == null) return;
        Ground = null; Height = null; Solid = null;
        lock (poolLock)
        {
            if (solidPool.Count >= PoolLimit) return;
            groundPool.Push(ground); heightPool.Push(height); solidPool.Push(solid);
        }
    }

    public static PNGridChunkCopy Capture(PNGridWorldData data, int cx, int cz)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        ushort[] ground = null; short[] height = null; byte[] solid = null;
        lock (poolLock)
        {
            if (solidPool.Count > 0 && groundPool.Peek().Length == data.GroundCellsPerChunk) { ground = groundPool.Pop(); height = heightPool.Pop(); solid = solidPool.Pop(); }
        }
        if (ground == null) { ground = new ushort[data.GroundCellsPerChunk]; height = new short[PNGridWorldData.CellsPerChunk]; solid = new byte[PNGridWorldData.CellsPerChunk]; }
        int version = data.ChunkVersion(cx, cz);
        data.ExportChunk(cx, cz, ground, height, solid);
        return new PNGridChunkCopy(cx, cz, data.ChunkIndex(cx, cz), version, ground, height, solid);
    }

    // The blob: a format byte, the deflated little-endian ground, height and solid arrays, and the Adler-32 sum of the raw bytes (four bytes, little-endian). The sum also pins the end of the blob: bytes added or cut off change it.
    public byte[] Encode()
    {
        var raw = new byte[Ground.Length * 2 + Height.Length * 2 + Solid.Length]; int at = 0;
        foreach (var value in Ground) { raw[at++] = (byte)value; raw[at++] = (byte)(value >> 8); }
        foreach (var value in Height) { raw[at++] = (byte)value; raw[at++] = (byte)(value >> 8); }
        Buffer.BlockCopy(Solid, 0, raw, at, Solid.Length);
        using (var output = new MemoryStream())
        {
            output.WriteByte(1);
            using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, true)) deflate.Write(raw, 0, raw.Length);
            uint sum = Adler(raw);
            output.WriteByte((byte)sum); output.WriteByte((byte)(sum >> 8)); output.WriteByte((byte)(sum >> 16)); output.WriteByte((byte)(sum >> 24));
            return output.ToArray();
        }
    }

    // Adler-32 of the chunk's raw bytes as they are now (ground, height, solid; the same layout the blob checksums). A client compares it with the host's after it applied a delta.
    public static uint Checksum(PNGridWorldData data, int index)
    {
        var copy = Capture(data, index % data.ChunksX, index / data.ChunksX);
        try
        {
            uint a = 1, b = 0;
            foreach (var value in copy.Ground) { a = (a + (byte)value) % 65521; b = (b + a) % 65521; a = (a + (byte)(value >> 8)) % 65521; b = (b + a) % 65521; }
            foreach (var value in copy.Height) { a = (a + (byte)value) % 65521; b = (b + a) % 65521; a = (a + (byte)(value >> 8)) % 65521; b = (b + a) % 65521; }
            foreach (var value in copy.Solid) { a = (a + value) % 65521; b = (b + a) % 65521; }
            return b << 16 | a;
        }
        finally { copy.Release(); }
    }

    private static uint Adler(byte[] data)
    {
        uint a = 1, b = 0;
        for (int i = 0; i < data.Length; i++) { a = (a + data[i]) % 65521; b = (b + a) % 65521; }
        return b << 16 | a;
    }

    // Reads a blob back. The deflated size is exact, so a blob that inflates to more or less is refused (no decompression bomb, no truncated file).
    public static void Decode(byte[] blob, int groundPerChunk, out ushort[] ground, out short[] height, out byte[] solid)
    {
        if (blob == null || blob.Length < 6 || blob.Length > PNGridSaveLimits.MaxChunkBlobBytes) throw new FormatException("A saved chunk is 6 to " + PNGridSaveLimits.MaxChunkBlobBytes + " bytes.");
        if (blob[0] != 1) throw new FormatException("Unknown saved chunk format " + blob[0] + ".");
        int groundCount = groundPerChunk * groundPerChunk, cells = PNGridWorldData.CellsPerChunk, expected = groundCount * 2 + cells * 2 + cells;
        var raw = new byte[expected];
        try
        {
            using (var input = new MemoryStream(blob, 1, blob.Length - 5))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < expected) { int n = deflate.Read(raw, read, expected - read); if (n == 0) throw new FormatException("A saved chunk is shorter than its size."); read += n; }
                if (deflate.ReadByte() != -1) throw new FormatException("A saved chunk is longer than its size.");
            }
        }
        catch (InvalidDataException error) { throw new FormatException("A saved chunk is damaged: " + error.Message); }
        uint stored = (uint)(blob[blob.Length - 4] | blob[blob.Length - 3] << 8 | blob[blob.Length - 2] << 16 | blob[blob.Length - 1] << 24);
        if (stored != Adler(raw)) throw new FormatException("A saved chunk fails its checksum.");
        ground = new ushort[groundCount]; height = new short[cells]; solid = new byte[cells]; int at = 0;
        for (int i = 0; i < groundCount; i++, at += 2) ground[i] = (ushort)(raw[at] | raw[at + 1] << 8);
        for (int i = 0; i < cells; i++, at += 2) height[i] = (short)(raw[at] | raw[at + 1] << 8);
        Buffer.BlockCopy(raw, at, solid, 0, cells);
    }
}

public sealed class PNGridWorldSnapshot
{
    public PNGridWorldDescriptor Descriptor { get; }
    public IReadOnlyList<string> Palette { get; }
    // One blob per chunk, in chunk index order.
    public IReadOnlyList<byte[]> Blobs { get; }
    public IReadOnlyList<PNGridSavedPlacement> Placed { get; }
    public IReadOnlyList<PNGridSavedPlayer> Players { get; }
    public int ChunkCount { get; }

    public PNGridWorldSnapshot(PNGridWorldDescriptor descriptor, IReadOnlyList<string> palette, IReadOnlyList<byte[]> blobs, IReadOnlyList<PNGridSavedPlacement> placed, IReadOnlyList<PNGridSavedPlayer> players)
    {
        Descriptor = (descriptor ?? throw new ArgumentNullException(nameof(descriptor))).WithoutRestore();
        if (palette == null || palette.Count == 0 || palette.Count > PNGridWorldLimits.MaxPalette || palette.Any(string.IsNullOrEmpty) || palette.Distinct(StringComparer.Ordinal).Count() != palette.Count) throw new FormatException("A save needs 1 to " + PNGridWorldLimits.MaxPalette + " distinct ground ids.");
        ChunkCount = (descriptor.Width / PNGridWorldLimits.ChunkCells) * (descriptor.Depth / PNGridWorldLimits.ChunkCells);
        if (blobs == null || blobs.Count != ChunkCount || blobs.Any(x => x == null || x.Length < 6 || x.Length > PNGridSaveLimits.MaxChunkBlobBytes)) throw new FormatException("A save holds exactly one blob (6 to " + PNGridSaveLimits.MaxChunkBlobBytes + " bytes) for each of the " + ChunkCount + " chunks.");
        placed = placed ?? Array.Empty<PNGridSavedPlacement>(); players = players ?? Array.Empty<PNGridSavedPlayer>();
        if (placed.Count > PNPlaceableLimits.MaxPlaced) throw new FormatException("A save holds at most " + PNPlaceableLimits.MaxPlaced + " placed things.");
        var ids = new HashSet<int>();
        foreach (var item in placed)
        {
            if (item == null || item.Id < 1 || !ids.Add(item.Id) || item.Direction < 0 || item.Direction > 3 || item.Health < 0 || item.X < 0 || item.Z < 0 || item.X >= descriptor.Width || item.Z >= descriptor.Depth ||
                string.IsNullOrEmpty(item.Definition) || item.Definition.Length > PNPlaceableLimits.MaxText || !EveId.TryParse(item.Definition, out _)) throw new FormatException("A saved placed thing is invalid.");
        }
        if (players.Count > PNGridSaveLimits.MaxPlayers) throw new FormatException("A save holds at most " + PNGridSaveLimits.MaxPlayers + " players.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var player in players)
        {
            if (player == null || string.IsNullOrEmpty(player.Key) || player.Key.Length > PNGridSaveLimits.MaxKeyLength || player.Key.Any(c => !(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == ':' || c == '_' || c == '-')) || !keys.Add(player.Key) ||
                !IsFinite(player.X) || !IsFinite(player.Y) || !IsFinite(player.Z)) throw new FormatException("A saved player is invalid.");
        }
        Palette = Array.AsReadOnly(palette.ToArray()); Blobs = Array.AsReadOnly(blobs.ToArray()); Placed = Array.AsReadOnly(placed.ToArray()); Players = Array.AsReadOnly(players.ToArray());
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    // All chunks of a world, encoded (a first save, and the tests).
    public static PNGridWorldSnapshot Build(PNGridWorldData data, PNGridWorldDescriptor descriptor, IEnumerable<PNGridSavedPlacement> placed, IEnumerable<PNGridSavedPlayer> players)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        var blobs = new byte[data.ChunkCount][];
        for (int cz = 0; cz < data.ChunksZ; cz++) for (int cx = 0; cx < data.ChunksX; cx++) blobs[data.ChunkIndex(cx, cz)] = PNGridChunkCopy.Capture(data, cx, cz).Encode();
        return new PNGridWorldSnapshot(descriptor, data.Palette, blobs, placed?.ToList(), players?.ToList());
    }

    public JObject ToJson()
    {
        return new JObject
        {
            ["format"] = PNGridSaveLimits.Format,
            ["palette"] = new JArray(Palette),
            ["chunks"] = new JArray(Blobs.Select(Convert.ToBase64String)),
            ["placed"] = new JArray(Placed.Select(x => new JObject { ["id"] = x.Id, ["def"] = x.Definition, ["x"] = x.X, ["z"] = x.Z, ["dir"] = x.Direction, ["hp"] = x.Health })),
            ["players"] = new JArray(Players.Select(x => new JObject { ["key"] = x.Key, ["pos"] = new JArray(x.X, x.Y, x.Z) }))
        };
    }

    // The state array of the portable envelope: one record, no actors, the grid data as the one `world` entry.
    public JArray ToState(double time)
    {
        if (double.IsNaN(time) || double.IsInfinity(time) || time < 0) throw new ArgumentOutOfRangeException(nameof(time));
        return new JArray(new JObject { ["schema"] = 1, ["time"] = time, ["actors"] = new JArray(), ["world"] = new JArray(new JObject { ["kind"] = PNGridSaveIds.Kind, ["data"] = ToJson() }) });
    }

    public static PNGridWorldSnapshot FromState(JToken state, PNGridWorldDescriptor descriptor)
    {
        var record = (state as JArray)?.Count == 1 ? ((JArray)state)[0] as JObject : null;
        var world = record?["world"] as JArray;
        if (world == null || world.Count != 1 || !(world[0] is JObject entry) || (string)entry["kind"] != PNGridSaveIds.Kind) throw new FormatException("This save does not hold a grid world.");
        return Parse(entry["data"], descriptor);
    }

    public static PNGridWorldSnapshot Parse(JToken token, PNGridWorldDescriptor descriptor)
    {
        if (!(token is JObject obj)) throw new FormatException("A grid save is a JSON object.");
        foreach (var property in obj.Properties()) if (property.Name != "format" && property.Name != "palette" && property.Name != "chunks" && property.Name != "placed" && property.Name != "players") throw new FormatException("Unknown grid save field: " + property.Name);
        if (obj["format"]?.Type != JTokenType.Integer || (int)obj["format"] != PNGridSaveLimits.Format) throw new FormatException("Unknown grid save format.");
        JArray Items(string name) => obj[name] as JArray ?? throw new FormatException("Grid save field " + name + " must be an array.");
        var palette = Items("palette").Select(x => x.Type == JTokenType.String ? (string)x : throw new FormatException("A palette entry is a string.")).ToList();
        var blobs = new List<byte[]>();
        foreach (var chunk in Items("chunks"))
        {
            if (chunk.Type != JTokenType.String) throw new FormatException("A chunk is a base64 string.");
            try { blobs.Add(Convert.FromBase64String((string)chunk)); } catch (FormatException) { throw new FormatException("A chunk is not valid base64."); }
        }
        int Int(JToken value, string what) => value?.Type == JTokenType.Integer ? checked((int)value) : throw new FormatException("A saved " + what + " must be an integer.");
        var placed = new List<PNGridSavedPlacement>();
        foreach (var item in Items("placed"))
        {
            if (!(item is JObject o)) throw new FormatException("A placed thing is an object.");
            foreach (var property in o.Properties()) if (property.Name != "id" && property.Name != "def" && property.Name != "x" && property.Name != "z" && property.Name != "dir" && property.Name != "hp") throw new FormatException("Unknown placed field: " + property.Name);
            if (o["def"]?.Type != JTokenType.String) throw new FormatException("A placed thing needs its definition id.");
            placed.Add(new PNGridSavedPlacement { Id = Int(o["id"], "id"), Definition = (string)o["def"], X = Int(o["x"], "x"), Z = Int(o["z"], "z"), Direction = Int(o["dir"], "direction"), Health = Int(o["hp"], "health") });
        }
        var players = new List<PNGridSavedPlayer>();
        foreach (var item in Items("players"))
        {
            if (!(item is JObject o)) throw new FormatException("A player is an object.");
            foreach (var property in o.Properties()) if (property.Name != "key" && property.Name != "pos") throw new FormatException("Unknown player field: " + property.Name);
            var pos = o["pos"] as JArray;
            if (o["key"]?.Type != JTokenType.String || pos == null || pos.Count != 3 || pos.Any(x => x.Type != JTokenType.Float && x.Type != JTokenType.Integer)) throw new FormatException("A saved player needs a key and a position of three numbers.");
            players.Add(new PNGridSavedPlayer { Key = (string)o["key"], X = (float)pos[0], Y = (float)pos[1], Z = (float)pos[2] });
        }
        return new PNGridWorldSnapshot(descriptor, palette, blobs, placed, players);
    }

    // The save must have been taken from a world of this shape and these ground types, or it is refused whole.
    public void CheckAgainst(PNGridWorldData data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (data.Width != Descriptor.Width || data.Depth != Descriptor.Depth || Math.Abs(data.CellSize - Descriptor.CellSize) > 1e-4f) throw new FormatException("The save is of a different world size.");
        if (!data.Palette.SequenceEqual(Palette, StringComparer.Ordinal)) throw new FormatException("The save was made with other ground types than this game registers.");
    }

    // Restores `count` chunks from chunk index `start` and returns the next index; the caller loops (over frames, if it likes) until it reaches ChunkCount and then calls Finish. Cells are written straight into the world.
    public int ApplyChunks(PNGridWorldData data, int start, int count)
    {
        CheckAgainst(data);
        if (start < 0 || count < 0 || start > ChunkCount) throw new ArgumentOutOfRangeException(nameof(start));
        int end = Math.Min(ChunkCount, start + count);
        for (int i = start; i < end; i++)
        {
            PNGridChunkCopy.Decode(Blobs[i], data.GroundPerChunk, out var ground, out var height, out var solid);
            data.ImportChunk(i % data.ChunksX, i / data.ChunksX, ground, height, solid);
        }
        return end;
    }

    public void Finish(PNGridWorldData data) => data.RecomputeClearance(new PNGridRegion(0, 0, data.Width - 1, data.Depth - 1));

    public void ApplyAll(PNGridWorldData data) { ApplyChunks(data, 0, ChunkCount); Finish(data); }
}
