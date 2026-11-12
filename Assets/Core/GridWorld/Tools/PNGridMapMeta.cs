using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: What a map says besides its cells (Docs/SandboxPlan.md, step G9): where players and monsters may appear, the gates to other places, zones with spawn rules and fixed monster groups. Plain data, pure C#: the
//     map tool edits it, the save file carries it (field "map" of the grid save, optional) and the spawner and the run frame read it. Coordinates are cells. Parsing is strict: unknown fields, wrong types and
//     out-of-range values are refused, names are lower-case words so they can be referenced from content.
// VI: Phần dữ liệu của một map ngoài các ô (G9): điểm sinh, cổng, vùng có luật sinh, nhóm quái cố định. Thuần dữ liệu; công cụ sửa, file lưu mang (trường "map" tùy chọn), bộ sinh quái và khung run đọc.
public static class PNGridMapLimits
{
    public const int MaxEach = 256, MaxName = 32, MaxEntries = 16, MaxCount = 64;
    public const double MaxRateScale = 4;
}

public sealed class PNMapSpawn
{
    // kind "player": where players enter (entry name = Name); kind "monster": a fixed point a spawner may use.
    public string Name, Kind;
    public int X, Z;
}

public sealed class PNMapGate
{
    public string Name, To;   // To: the id of the place the gate leads to ("" when the run frame decides)
    public PNGridRegion Region;
}

public sealed class PNMapZone
{
    public string Name;
    public PNGridRegion Region;
    public bool SpawnBlock;
    public double RateScale = 1;
}

public sealed class PNMapGroupEntry { public string Entity; public int Count; }

// A named place at a point. A sandbox group holds fixed monsters (Entries); a roguelite room map names the places its waves use and leaves Entries empty (the room definition says which monsters) -
// the room director picks a spot within Radius metres of the point (G10a).
public sealed class PNMapGroup
{
    public const double DefaultRadius = 3, MaxRadius = 50;
    public string Name;
    public int X, Z;
    public double Radius = DefaultRadius;
    public List<PNMapGroupEntry> Entries = new List<PNMapGroupEntry>();
}

public sealed class PNGridMapMeta
{
    public const string PlayerKind = "player", MonsterKind = "monster";
    public List<PNMapSpawn> Spawns { get; } = new List<PNMapSpawn>();
    public List<PNMapGate> Gates { get; } = new List<PNMapGate>();
    public List<PNMapZone> Zones { get; } = new List<PNMapZone>();
    public List<PNMapGroup> Groups { get; } = new List<PNMapGroup>();
    public bool IsEmpty => Spawns.Count == 0 && Gates.Count == 0 && Zones.Count == 0 && Groups.Count == 0;

    public PNGridMapMeta Clone()
    {
        var copy = new PNGridMapMeta();
        copy.Spawns.AddRange(Spawns.Select(x => new PNMapSpawn { Name = x.Name, Kind = x.Kind, X = x.X, Z = x.Z }));
        copy.Gates.AddRange(Gates.Select(x => new PNMapGate { Name = x.Name, To = x.To, Region = x.Region }));
        copy.Zones.AddRange(Zones.Select(x => new PNMapZone { Name = x.Name, Region = x.Region, SpawnBlock = x.SpawnBlock, RateScale = x.RateScale }));
        copy.Groups.AddRange(Groups.Select(x => new PNMapGroup { Name = x.Name, X = x.X, Z = x.Z, Radius = x.Radius, Entries = x.Entries.Select(e => new PNMapGroupEntry { Entity = e.Entity, Count = e.Count }).ToList() }));
        return copy;
    }

    public static bool ValidName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > PNGridMapLimits.MaxName) return false;
        foreach (char c in name) if (!(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '_')) return false;
        return true;
    }

    // Shape check against the world size (not about the cells: that is the tool's validation). Throws FormatException.
    public void CheckShape(int width, int depth)
    {
        void Names(IEnumerable<string> names, string what)
        {
            var list = names.ToList();
            if (list.Count > PNGridMapLimits.MaxEach) throw new FormatException("A map holds at most " + PNGridMapLimits.MaxEach + " " + what + ".");
            if (list.Any(x => !ValidName(x))) throw new FormatException("A " + what + " name is 1 to " + PNGridMapLimits.MaxName + " characters of a-z, 0-9 and _.");
            if (list.Distinct(StringComparer.Ordinal).Count() != list.Count) throw new FormatException("Two " + what + " share a name.");
        }
        Names(Spawns.Select(x => x.Name), "spawn points"); Names(Gates.Select(x => x.Name), "gates"); Names(Zones.Select(x => x.Name), "zones"); Names(Groups.Select(x => x.Name), "groups");
        bool In(int x, int z) => x >= 0 && z >= 0 && x < width && z < depth;
        foreach (var s in Spawns)
        {
            if (s.Kind != PlayerKind && s.Kind != MonsterKind) throw new FormatException("A spawn point is of kind player or monster.");
            if (!In(s.X, s.Z)) throw new FormatException("Spawn point " + s.Name + " is outside the world.");
        }
        foreach (var g in Gates)
        {
            if (!g.Region.IsValid || !In(g.Region.X0, g.Region.Z0) || !In(g.Region.X1, g.Region.Z1)) throw new FormatException("Gate " + g.Name + " is outside the world.");
            if (g.To == null || g.To.Length > 128 || g.To.Length > 0 && !EveId.TryParse(g.To, out _)) throw new FormatException("Gate " + g.Name + " leads to a place id of the form namespace:name, or nowhere.");
        }
        foreach (var z in Zones)
        {
            if (!z.Region.IsValid || !In(z.Region.X0, z.Region.Z0) || !In(z.Region.X1, z.Region.Z1)) throw new FormatException("Zone " + z.Name + " is outside the world.");
            if (double.IsNaN(z.RateScale) || z.RateScale < 0 || z.RateScale > PNGridMapLimits.MaxRateScale) throw new FormatException("Zone " + z.Name + " rate scale is 0 to " + PNGridMapLimits.MaxRateScale + ".");
        }
        foreach (var g in Groups)
        {
            if (!In(g.X, g.Z)) throw new FormatException("Group " + g.Name + " is outside the world.");
            if (g.Entries.Count > PNGridMapLimits.MaxEntries) throw new FormatException("Group " + g.Name + " has 0 to " + PNGridMapLimits.MaxEntries + " entries.");
            if (double.IsNaN(g.Radius) || double.IsInfinity(g.Radius) || g.Radius <= 0 || g.Radius > PNMapGroup.MaxRadius) throw new FormatException("Group " + g.Name + " radius is above 0 and at most " + PNMapGroup.MaxRadius + " metres.");
            foreach (var e in g.Entries)
                if (e.Entity == null || e.Entity.Length > 128 || !EveId.TryParse(e.Entity, out _) || e.Count < 1 || e.Count > PNGridMapLimits.MaxCount) throw new FormatException("Group " + g.Name + " has a bad entry.");
        }
    }

    public JObject ToJson()
    {
        return new JObject
        {
            ["spawns"] = new JArray(Spawns.Select(x => new JObject { ["name"] = x.Name, ["kind"] = x.Kind, ["x"] = x.X, ["z"] = x.Z })),
            ["gates"] = new JArray(Gates.Select(x => new JObject { ["name"] = x.Name, ["to"] = x.To, ["x0"] = x.Region.X0, ["z0"] = x.Region.Z0, ["x1"] = x.Region.X1, ["z1"] = x.Region.Z1 })),
            ["zones"] = new JArray(Zones.Select(x => new JObject { ["name"] = x.Name, ["x0"] = x.Region.X0, ["z0"] = x.Region.Z0, ["x1"] = x.Region.X1, ["z1"] = x.Region.Z1, ["spawnBlock"] = x.SpawnBlock, ["rateScale"] = x.RateScale })),
            ["groups"] = new JArray(Groups.Select(x => new JObject { ["name"] = x.Name, ["x"] = x.X, ["z"] = x.Z, ["radius"] = x.Radius, ["entries"] = new JArray(x.Entries.Select(e => new JObject { ["entity"] = e.Entity, ["count"] = e.Count })) }))
        };
    }

    public static PNGridMapMeta Parse(JToken token, int width, int depth)
    {
        var obj = token as JObject ?? throw new FormatException("A map block is an object.");
        foreach (var p in obj.Properties()) if (p.Name != "spawns" && p.Name != "gates" && p.Name != "zones" && p.Name != "groups") throw new FormatException("Unknown map field: " + p.Name);
        JArray Items(string name) => obj[name] == null ? new JArray() : obj[name] as JArray ?? throw new FormatException("Map field " + name + " must be an array.");
        var meta = new PNGridMapMeta();
        foreach (var item in Items("spawns"))
        {
            var o = Obj(item, "name", "kind", "x", "z");
            meta.Spawns.Add(new PNMapSpawn { Name = Str(o, "name"), Kind = Str(o, "kind"), X = Int(o, "x"), Z = Int(o, "z") });
        }
        foreach (var item in Items("gates"))
        {
            var o = Obj(item, "name", "to", "x0", "z0", "x1", "z1");
            meta.Gates.Add(new PNMapGate { Name = Str(o, "name"), To = Str(o, "to"), Region = new PNGridRegion(Int(o, "x0"), Int(o, "z0"), Int(o, "x1"), Int(o, "z1")) });
        }
        foreach (var item in Items("zones"))
        {
            var o = Obj(item, "name", "x0", "z0", "x1", "z1", "spawnBlock", "rateScale");
            if (o["spawnBlock"]?.Type != JTokenType.Boolean) throw new FormatException("Zone spawnBlock must be a boolean.");
            if (o["rateScale"] == null || o["rateScale"].Type != JTokenType.Integer && o["rateScale"].Type != JTokenType.Float) throw new FormatException("Zone rateScale must be a number.");
            meta.Zones.Add(new PNMapZone { Name = Str(o, "name"), Region = new PNGridRegion(Int(o, "x0"), Int(o, "z0"), Int(o, "x1"), Int(o, "z1")), SpawnBlock = (bool)o["spawnBlock"], RateScale = (double)o["rateScale"] });
        }
        foreach (var item in Items("groups"))
        {
            var o = Obj(item, new[] { "name", "x", "z", "entries" }, "radius");
            var group = new PNMapGroup { Name = Str(o, "name"), X = Int(o, "x"), Z = Int(o, "z") };
            if (o["radius"] != null)
            {
                if (o["radius"].Type != JTokenType.Integer && o["radius"].Type != JTokenType.Float) throw new FormatException("Group radius must be a number.");
                group.Radius = (double)o["radius"];
            }
            if (!(o["entries"] is JArray entries)) throw new FormatException("Group entries must be an array.");
            foreach (var entry in entries)
            {
                var e = Obj(entry, "entity", "count");
                group.Entries.Add(new PNMapGroupEntry { Entity = Str(e, "entity"), Count = Int(e, "count") });
            }
            meta.Groups.Add(group);
        }
        meta.CheckShape(width, depth);
        return meta;
    }

    private static JObject Obj(JToken token, params string[] fields) => Obj(token, fields, new string[0]);
    // `optional` fields may be missing; any other field not in `fields` is refused.
    private static JObject Obj(JToken token, string[] fields, params string[] optional)
    {
        var o = token as JObject ?? throw new FormatException("A map entry is an object.");
        foreach (var p in o.Properties()) if (Array.IndexOf(fields, p.Name) < 0 && Array.IndexOf(optional, p.Name) < 0) throw new FormatException("Unknown map entry field: " + p.Name);
        foreach (var f in fields) if (o[f] == null) throw new FormatException("Map entry field missing: " + f);
        return o;
    }
    private static string Str(JObject o, string f) => o[f].Type == JTokenType.String ? (string)o[f] : throw new FormatException("Map entry field " + f + " must be a string.");
    private static int Int(JObject o, string f) => o[f].Type == JTokenType.Integer ? checked((int)o[f]) : throw new FormatException("Map entry field " + f + " must be an integer.");
}
