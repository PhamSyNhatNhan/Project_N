using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: Work on placeable things (Docs/SandboxPlan.md, "Thiết kế work của vật đặt"). Work is a packet of its own, NOT a PNDamage: it carries work types (registered `work_types`, like pickaxe or axe) with a power per
//     type and an amount. Tags only classify the work; they never pick a Scale or a Mitigation route. A placeable that takes work names the types it accepts, how much work it holds (`left`), the least power a type
//     must have for the packet to count (`minPower`) and whether a monster may work it (`enemy`). Core only offers the gate (`PNGridPlacements.Work`); a skill that wants to break things uses an Action that sends a packet.
// VI: Work của vật đặt: gói riêng, không đi qua PNDamage. Mang các loại work (đăng ký ở `work_types`, như pickaxe, axe) với sức (power) theo từng loại và một lượng (amount). Tag chỉ phân loại. Core chỉ có cổng.
public sealed class PNWorkTypeDefinition : EveIIdentified
{
    public const int MaxText = 128, MaxDescription = 256;
    public EveId Id { get; }
    public string Description { get; }
    public PNWorkTypeDefinition(EveId id, string description = "")
    {
        if (!id.IsValid || id.ToString().Length > MaxText) throw new ArgumentException("A work type needs a valid id of at most " + MaxText + " characters.", nameof(id));
        if (description != null && description.Length > MaxDescription) throw new ArgumentException("A work type description is at most " + MaxDescription + " characters.", nameof(description));
        Id = id; Description = description ?? "";
    }
}

public static class PNWorkIds
{
    public static readonly EveId Pickaxe = EveId.Parse("project_n:pickaxe");
    public static readonly EveId Axe = EveId.Parse("project_n:axe");
}

public static class PNWorkLimits
{
    public const int MaxWorkLeft = 100000, MaxPower = 100000, MaxAmount = 100000, MaxTypes = 8;
    public const double MaxRadius = 16;
}

// One work type of a packet and how strong it is.
public readonly struct PNWorkEntry
{
    public EveId Type { get; }
    public int Power { get; }
    public PNWorkEntry(EveId type, int power)
    {
        if (!type.IsValid) throw new ArgumentException("A work entry needs a work type.", nameof(type));
        if (power < 0 || power > PNWorkLimits.MaxPower) throw new ArgumentOutOfRangeException(nameof(power), "Work power is 0 to " + PNWorkLimits.MaxPower + ".");
        Type = type; Power = power;
    }
}

// What one hit of work carries. `Amount` is taken off the thing's work when at least one entry is accepted by it.
public sealed class PNWorkPacket
{
    public IReadOnlyList<PNWorkEntry> Entries { get; }
    public int Amount { get; }
    // True when a monster sends it: a thing takes it only if it allows monsters (`enemy`).
    public bool FromEnemy { get; }
    public PNWorkPacket(IEnumerable<PNWorkEntry> entries, int amount, bool fromEnemy = false)
    {
        var list = (entries ?? throw new ArgumentNullException(nameof(entries))).ToArray();
        if (list.Length == 0 || list.Length > PNWorkLimits.MaxTypes) throw new ArgumentException("A work packet carries 1 to " + PNWorkLimits.MaxTypes + " work types.", nameof(entries));
        if (list.Select(x => x.Type).Distinct().Count() != list.Length) throw new ArgumentException("A work packet names a work type once.", nameof(entries));
        if (amount < 1 || amount > PNWorkLimits.MaxAmount) throw new ArgumentOutOfRangeException(nameof(amount), "Work amount is 1 to " + PNWorkLimits.MaxAmount + ".");
        Entries = Array.AsReadOnly(list); Amount = amount; FromEnemy = fromEnemy;
    }
}

public enum PNWorkOutcome { None, Refused, Worked, Broken }

// The `work` object of a placeable: {"types": ["namespace:id", ...], "left": 1..100000, "minPower": 0.. (0), "enemy": false}.
public sealed class PNPlaceableWork
{
    public IReadOnlyList<EveId> Types { get; }
    public int Left { get; }
    public int MinPower { get; }
    public bool Enemy { get; }

    public PNPlaceableWork(JToken token)
    {
        var obj = token as JObject ?? throw new FormatException("placeable work is an object.");
        foreach (var property in obj.Properties()) if (property.Name != "types" && property.Name != "left" && property.Name != "minPower" && property.Name != "enemy") throw new FormatException("Unknown placeable work field: " + property.Name);
        if (!(obj["types"] is JArray types) || types.Count == 0 || types.Count > PNWorkLimits.MaxTypes) throw new FormatException("placeable work.types is 1 to " + PNWorkLimits.MaxTypes + " work type ids.");
        var list = new List<EveId>();
        foreach (var item in types)
        {
            if (item.Type != JTokenType.String || !EveId.TryParse(item.Value<string>(), out var id)) throw new FormatException("placeable work.types holds registered work type ids.");
            if (list.Contains(id)) throw new FormatException("placeable work.types names " + id + " twice.");
            list.Add(id);
        }
        Types = list.AsReadOnly();
        Left = Int(obj, "left", -1);
        if (Left < 1 || Left > PNWorkLimits.MaxWorkLeft) throw new FormatException("placeable work.left is 1 to " + PNWorkLimits.MaxWorkLeft + ".");
        MinPower = Int(obj, "minPower", 0);
        if (MinPower < 0 || MinPower > PNWorkLimits.MaxPower) throw new FormatException("placeable work.minPower is 0 to " + PNWorkLimits.MaxPower + ".");
        if (obj["enemy"] != null && obj["enemy"].Type != JTokenType.Boolean) throw new FormatException("placeable work.enemy must be a boolean.");
        Enemy = obj["enemy"] != null && obj["enemy"].Value<bool>();
    }
    private static int Int(JObject obj, string field, int fallback)
        => obj[field] == null ? fallback : obj[field].Type == JTokenType.Integer ? checked(obj[field].Value<int>()) : throw new FormatException("placeable work." + field + " must be an integer.");

    public void ValidateReferences(PNContentReferenceContext context)
    { foreach (var type in Types) context.Require<PNWorkTypeDefinition>(type, "placeable work.types"); }

    // Whether this packet counts for the thing: a monster needs `enemy`, and one entry must be a type the thing takes with at least `minPower`.
    public bool Accepts(PNWorkPacket packet)
    {
        if (packet == null || (packet.FromEnemy && !Enemy)) return false;
        foreach (var entry in packet.Entries) if (entry.Power >= MinPower && Types.Contains(entry.Type)) return true;
        return false;
    }
}

// The `perform` payload of the work Action: {"types":[{"id","power"}...], "amount": 1.., "radius": metres (0 < r <= 16), "forward": metres (0)}.
// The packet goes to every placed thing within `radius` of the point `forward` metres along the skill's direction from the skill's position.
public sealed class PNWorkActionSettings
{
    public IReadOnlyList<PNWorkEntry> Entries { get; }
    public int Amount { get; }
    public float Radius { get; }
    public float Forward { get; }

    public static PNWorkActionSettings Parse(JToken token) => new PNWorkActionSettings(token);
    private PNWorkActionSettings(JToken token)
    {
        var obj = token as JObject ?? throw new FormatException("Work action perform payload must be an object.");
        foreach (var property in obj.Properties()) if (property.Name != "types" && property.Name != "amount" && property.Name != "radius" && property.Name != "forward") throw new FormatException("Unknown work action field: " + property.Name);
        if (!(obj["types"] is JArray types) || types.Count == 0 || types.Count > PNWorkLimits.MaxTypes) throw new FormatException("Work action types is 1 to " + PNWorkLimits.MaxTypes + " entries {\"id\",\"power\"}.");
        var list = new List<PNWorkEntry>();
        foreach (var item in types)
        {
            var entry = item as JObject ?? throw new FormatException("A work action type is an object {\"id\",\"power\"}.");
            foreach (var property in entry.Properties()) if (property.Name != "id" && property.Name != "power") throw new FormatException("Unknown work action type field: " + property.Name);
            if (entry["id"]?.Type != JTokenType.String || !EveId.TryParse(entry["id"].Value<string>(), out var id)) throw new FormatException("A work action type needs a registered work type id.");
            if (entry["power"]?.Type != JTokenType.Integer) throw new FormatException("A work action type needs an integer power.");
            long power = entry["power"].Value<long>(); if (power < 0 || power > PNWorkLimits.MaxPower) throw new FormatException("Work power is 0 to " + PNWorkLimits.MaxPower + ".");
            if (list.Any(x => x.Type == id)) throw new FormatException("A work action names " + id + " twice.");
            list.Add(new PNWorkEntry(id, (int)power));
        }
        Entries = list.AsReadOnly();
        if (obj["amount"]?.Type != JTokenType.Integer) throw new FormatException("Work action amount must be an integer.");
        long amount = obj["amount"].Value<long>(); if (amount < 1 || amount > PNWorkLimits.MaxAmount) throw new FormatException("Work action amount is 1 to " + PNWorkLimits.MaxAmount + ".");
        Amount = (int)amount;
        Radius = Number(obj, "radius", true); Forward = obj["forward"] == null ? 0 : Number(obj, "forward", false);
        if (Radius <= 0 || Radius > PNWorkLimits.MaxRadius) throw new FormatException("Work action radius is above 0 and at most " + PNWorkLimits.MaxRadius + " metres.");
        if (Forward < 0 || Forward > PNWorkLimits.MaxRadius) throw new FormatException("Work action forward is 0 to " + PNWorkLimits.MaxRadius + " metres.");
    }
    private static float Number(JObject obj, string field, bool required)
    {
        var token = obj[field];
        if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) throw new FormatException("Work action " + field + " must be a number.");
        double value = token.Value<double>();
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException("Work action " + field + " must be finite.");
        return (float)value;
    }
    public PNWorkPacket CreatePacket(bool fromEnemy) => new PNWorkPacket(Entries, Amount, fromEnemy);
    public void ValidateReferences(EveRegistryHub hub)
    {
        var registry = hub.Get<PNWorkTypeDefinition>();
        foreach (var entry in Entries) registry.Get(entry.Type);
    }
}
