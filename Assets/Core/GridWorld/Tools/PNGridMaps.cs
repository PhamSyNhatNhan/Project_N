using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

// EN: The maps a grid world can be made from (Docs/SandboxPlan.md, step G9), by id. A map is either a FILE (a snapshot made with the map tool: cells, placed things, the map block) or a GENERATED one (a chain of
//     generator steps run once with the world's seed). The world provider asks for a map by the id in the world's descriptor; the built-in test maps (`PNGridTestMaps`) stay as they are. A map file is the snapshot of step G7
//     plus its descriptor, not wrapped in the world-save envelope (a map belongs to no world definition). Registration is a startup act: content modules and the tool register, nothing registers during play.
// VI: Các map mà world lưới có thể dựng từ, theo id: FILE (snapshot do công cụ tạo) hoặc SINH (chuỗi bước chạy một lần với seed của world). Provider hỏi map theo id trong descriptor; map thử dựng sẵn giữ nguyên.
public sealed class PNGridMapEntry
{
    public string Id { get; }
    // The map block, known without building (a client uses it for the player entries; it never builds the cells).
    public PNGridMapMeta Meta { get; }
    private readonly PNGridWorldSnapshot snapshot;
    private readonly JArray chain;
    private readonly Func<string, PNGridStamp> stamps;

    internal PNGridMapEntry(string id, PNGridWorldSnapshot snapshot) { Id = id; this.snapshot = snapshot; Meta = snapshot.Meta; }
    internal PNGridMapEntry(string id, JArray chain, Func<string, PNGridStamp> stamps, PNGridMapMeta meta) { Id = id; this.chain = chain; this.stamps = stamps; Meta = meta; }

    // The cells of the map for a world of this shape. A generated map is made now (same seed, same cells).
    public PNGridWorldSnapshot Build(PNGridWorldDescriptor descriptor, IReadOnlyList<string> palette, bool[] walkable, Func<string, PNPlaceableDefinition> resolve)
    {
        if (snapshot != null) return snapshot;
        var doc = new PNGridMapDoc(descriptor, palette, walkable, resolve);
        PNGridMapGen.Run(doc, chain, descriptor.Seed, stamps);
        if (Meta != null)
        {
            foreach (var s in Meta.Spawns) doc.SetSpawn(s.Name, s.Kind, s.X, s.Z);
            foreach (var g in Meta.Gates) doc.SetGate(g.Name, g.Region, g.To);
            foreach (var z in Meta.Zones) doc.SetZone(z.Name, z.Region, z.SpawnBlock, z.RateScale);
            foreach (var g in Meta.Groups) doc.SetGroup(g.Name, g.X, g.Z, g.Entries.Select(e => (e.Entity, e.Count)), g.Radius);
        }
        return doc.ToSnapshot();
    }
}

public static class PNGridMaps
{
    public const int MaxFileBytes = 64 * 1024 * 1024, FileFormat = 1;
    private static readonly Dictionary<string, PNGridMapEntry> entries = new Dictionary<string, PNGridMapEntry>(StringComparer.Ordinal);

    private static void Add(PNGridMapEntry entry)
    {
        lock (entries) { if (entries.ContainsKey(entry.Id)) throw new InvalidOperationException("Grid map " + entry.Id + " is already registered."); entries[entry.Id] = entry; }
    }
    private static string CheckId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > PNGridWorldLimits.MaxMapIdLength || !EveEngine.EveId.TryParse(id, out _)) throw new ArgumentException("A map id of the form namespace:name is required.", nameof(id));
        if (PNGridTestMaps.Knows(id)) throw new ArgumentException("Map " + id + " is a built-in test map.", nameof(id));
        return id;
    }

    public static void RegisterFile(string id, PNGridWorldSnapshot snapshot) => Add(new PNGridMapEntry(CheckId(id), snapshot ?? throw new ArgumentNullException(nameof(snapshot))));

    // `meta` (optional) is the map block of a generated map: spawn points, gates, zones and groups are all put into the built map.
    public static void RegisterGenerated(string id, JArray chain, Func<string, PNGridStamp> stamps = null, PNGridMapMeta meta = null)
    {
        PNGridMapGen.Validate(chain ?? throw new ArgumentNullException(nameof(chain)));
        Add(new PNGridMapEntry(CheckId(id), (JArray)chain.DeepClone(), stamps, meta));
    }

    public static bool Has(string id) { lock (entries) return id != null && entries.ContainsKey(id); }
    public static PNGridMapEntry Get(string id) { lock (entries) return id != null && entries.TryGetValue(id, out var entry) ? entry : null; }
    public static void Remove(string id) { lock (entries) entries.Remove(id); }

    // ---- the file ----
    public static string ToFileJson(PNGridWorldSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        var file = new JObject { ["format"] = FileFormat, ["descriptor"] = snapshot.Descriptor.ToJson(), ["world"] = snapshot.ToJson() };
        return file.ToString(Newtonsoft.Json.Formatting.None);
    }

    public static PNGridWorldSnapshot ParseFile(string json)
    {
        if (string.IsNullOrEmpty(json) || json.Length > MaxFileBytes) throw new FormatException("A map file is 1 byte to " + MaxFileBytes + " bytes.");
        JObject file;
        try { file = JObject.Parse(json); } catch (Newtonsoft.Json.JsonException error) { throw new FormatException("A map file is not valid JSON: " + error.Message); }
        foreach (var p in file.Properties()) if (p.Name != "format" && p.Name != "descriptor" && p.Name != "world") throw new FormatException("Unknown map file field: " + p.Name);
        if (file["format"]?.Type != JTokenType.Integer || (int)file["format"] != FileFormat) throw new FormatException("Unknown map file format.");
        var descriptor = PNGridWorldDescriptor.Parse(file["descriptor"]);
        return PNGridWorldSnapshot.Parse(file["world"], descriptor);
    }

    public static void WriteFile(string path, PNGridWorldSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("A path is required.", nameof(path));
        var bytes = new UTF8Encoding(false).GetBytes(ToFileJson(snapshot));
        if (bytes.Length > MaxFileBytes) throw new InvalidOperationException("The map is too large for a map file.");
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
    }

    public static PNGridWorldSnapshot ReadFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("No map file at " + path);
        if (info.Length > MaxFileBytes) throw new FormatException("A map file is at most " + MaxFileBytes + " bytes.");
        return ParseFile(File.ReadAllText(path, Encoding.UTF8));
    }
}
