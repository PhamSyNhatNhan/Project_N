using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// EN: Placeable things of a grid world (Docs/SandboxPlan.md, step G3): a wall, a crafting table, a bush that is put down and broken. Kind `placeable` of the shared `instances` registry. A definition holds the tuning
//     (footprint in cells, whether it blocks the way, how much it takes to break it, the spawn rules it carries for the future spawner) and names a LOGIC class that decides what happens when it is broken. Logic
//     does not own the footprint: the cells are the single truth, so a placed thing that blocks the way turns its footprint cells solid (rule 2 of the plan: nothing blocks a body that the grid does not know about),
//     and the wall mesh and colliders follow from the cells like any other wall. `PNGridPlacements` keeps the list of what is placed and goes through the edit gateway (PNGridEditor) for every cell it changes.
// VI: Vật đặt của world lưới (G3): kind `placeable` của registry `instances`. Định nghĩa giữ phần chỉnh (dấu chân, chặn đường, độ bền, luật sinh) và chọn LỚP LOGIC cho việc xảy ra khi bị phá. Ô là sự thật duy nhất:
//     vật chặn đường biến các ô dấu chân thành ô đặc qua cổng sửa ô, mesh và collider theo ô.
public static class PNPlaceableLimits
{
    public const int MaxFootprint = 8, MaxHealth = 100000, MaxPlaced = 100000, MaxText = 128;
    public const double MaxSpawnRateScale = 4, MaxSafetyRadius = 32;
}

// EN: The `data` payload of a placeable. Required: footprint ({"width","depth"} in cells, 1 to 8). Optional: blocksPath (default true), blocksSight (false), breakable (true), health (10, 1 to 100000, needs
//     breakable), spawnBlock (false: monsters do not spawn within safetyRadius), spawnRateScale (1, 0 to 4: scales the spawn rate within safetyRadius), safetyRadius (0, metres, 0 to 32), params (for the logic class).
//     blocksSight, spawnBlock, spawnRateScale and safetyRadius are data for the spawner and the sight rules of later steps; this step only stores and validates them.
public sealed class PNPlaceableSettings
{
    public int Width { get; }
    public int Depth { get; }
    public bool BlocksPath { get; }
    public bool BlocksSight { get; }
    public bool Breakable { get; }
    public int Health { get; }
    public bool SpawnBlock { get; }
    public double SpawnRateScale { get; }
    public double SafetyRadius { get; }
    [JsonIgnore] public JToken Params { get; }

    public PNPlaceableSettings(JToken data)
    {
        var obj = data as JObject ?? throw new FormatException("A placeable's data is an object.");
        foreach (var property in obj.Properties())
            if (Array.IndexOf(Fields, property.Name) < 0) throw new FormatException("Unknown placeable field: " + property.Name);
        if (!(obj["footprint"] is JObject foot)) throw new FormatException("placeable footprint is required ({\"width\",\"depth\"}).");
        foreach (var property in foot.Properties()) if (property.Name != "width" && property.Name != "depth") throw new FormatException("Unknown placeable footprint field: " + property.Name);
        Width = Int(foot, "width", "footprint.width"); Depth = Int(foot, "depth", "footprint.depth");
        if (Width < 1 || Width > PNPlaceableLimits.MaxFootprint || Depth < 1 || Depth > PNPlaceableLimits.MaxFootprint)
            throw new FormatException("placeable footprint is 1 to " + PNPlaceableLimits.MaxFootprint + " cells on a side.");
        BlocksPath = Bool(obj, "blocksPath", true); BlocksSight = Bool(obj, "blocksSight", false); Breakable = Bool(obj, "breakable", true); SpawnBlock = Bool(obj, "spawnBlock", false);
        Health = obj["health"] == null ? 10 : Int(obj, "health", "health");
        if (Health < 1 || Health > PNPlaceableLimits.MaxHealth) throw new FormatException("placeable health is 1 to " + PNPlaceableLimits.MaxHealth + ".");
        if (!Breakable && obj["health"] != null) throw new FormatException("placeable health needs breakable.");
        SpawnRateScale = obj["spawnRateScale"] == null ? 1 : Number(obj, "spawnRateScale");
        if (SpawnRateScale < 0 || SpawnRateScale > PNPlaceableLimits.MaxSpawnRateScale) throw new FormatException("placeable spawnRateScale is 0 to " + PNPlaceableLimits.MaxSpawnRateScale + ".");
        SafetyRadius = obj["safetyRadius"] == null ? 0 : Number(obj, "safetyRadius");
        if (SafetyRadius < 0 || SafetyRadius > PNPlaceableLimits.MaxSafetyRadius) throw new FormatException("placeable safetyRadius is 0 to " + PNPlaceableLimits.MaxSafetyRadius + " metres.");
        if (obj["params"] != null && !(obj["params"] is JObject)) throw new FormatException("placeable params must be an object.");
        Params = obj["params"]?.DeepClone();
    }
    private static readonly string[] Fields = { "footprint", "blocksPath", "blocksSight", "breakable", "health", "spawnBlock", "spawnRateScale", "safetyRadius", "params" };
    private static int Int(JObject obj, string field, string name)
        => obj[field]?.Type == JTokenType.Integer ? checked(obj[field].Value<int>()) : throw new FormatException("placeable " + name + " must be an integer.");
    private static bool Bool(JObject obj, string field, bool fallback)
        => obj[field] == null ? fallback : obj[field].Type == JTokenType.Boolean ? obj[field].Value<bool>() : throw new FormatException("placeable " + field + " must be a boolean.");
    private static double Number(JObject obj, string field)
    {
        var token = obj[field];
        if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) throw new FormatException("placeable " + field + " must be a number.");
        double value = token.Value<double>();
        if (double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException("placeable " + field + " must be finite.");
        return value;
    }
}

// What the logic class sees when a placed thing is broken. The cells of the footprint are already free (if the thing blocked the way); the logic changes the world only through the editor.
public sealed class PNPlaceableBreakContext
{
    public PNGridWorldData Data { get; }
    public PNGridEditor Editor { get; }
    public PNPlacedObject Object { get; }
    public JToken Params { get; }
    public PNPlaceableBreakContext(PNGridWorldData data, PNGridEditor editor, PNPlacedObject placed, JToken parameters) { Data = data; Editor = editor; Object = placed; Params = parameters; }
}

public interface IPNPlaceableLogic { void Broken(PNPlaceableBreakContext context); }

// The default logic: the thing is gone and nothing else changes.
public sealed class PNPlaceableVanishLogic : IPNPlaceableLogic { public void Broken(PNPlaceableBreakContext context) { } }

// A sample logic: the ground under the footprint becomes another ground type (params: {"ground": "namespace:id"}).
public sealed class PNPlaceableLeaveGroundLogic : IPNPlaceableLogic
{
    public static void ValidateParams(JToken parameters)
    {
        var obj = parameters as JObject ?? throw new FormatException("placeable leave_ground needs params {\"ground\"}.");
        foreach (var property in obj.Properties()) if (property.Name != "ground") throw new FormatException("Unknown leave_ground param: " + property.Name);
        if (obj["ground"]?.Type != JTokenType.String || !EveId.TryParse(obj["ground"].Value<string>(), out _)) throw new FormatException("leave_ground ground must be a registered ground id.");
    }
    public static void ValidateReferences(JToken data, PNContentReferenceContext context)
    {
        var parameters = (data as JObject)?["params"] as JObject;
        if (parameters != null) context.Require<PNGroundDefinition>(parameters["ground"], "placeable params.ground");
    }
    public void Broken(PNPlaceableBreakContext context)
    {
        var id = (string)(context.Params as JObject)?["ground"]; if (id == null) return;
        int index = -1; for (int i = 0; i < context.Data.Palette.Count; i++) if (context.Data.Palette[i] == id) { index = i; break; }
        if (index < 0) return;   // this world's palette does not have the ground: the thing just vanishes
        var batch = new PNGridEditBatch(); var o = context.Object;
        for (int z = o.Z; z < o.Z + o.CellsZ; z++) for (int x = o.X; x < o.X + o.CellsX; x++) batch.AddGround(x, z, index);
        context.Editor.Submit(batch);
    }
}

public sealed class PNPlaceableDefinition : PNInstanceDefinition
{
    public const string KindName = "placeable";
    public override string Kind => KindName;
    [JsonIgnore] public PNPlaceableSettings Settings { get; }
    private readonly Func<IPNPlaceableLogic> create;
    private readonly Action<JToken> validateParams;
    private readonly Action<JToken, PNContentReferenceContext> references;
    private IPNPlaceableLogic logic;
    [JsonIgnore] public IPNPlaceableLogic Logic => logic ?? (logic = Make());

    // validateParams checks the logic class's own `params` object (null = the logic takes none).
    public PNPlaceableDefinition(EveId id, Type logicType, Func<IPNPlaceableLogic> create, JToken data, Action<JToken> validateParams = null, Action<JToken, PNContentReferenceContext> references = null)
        : base(id, RequireLogic(logicType), data, (payload, hub) => Check(payload, validateParams), input => { }, references)
    {
        this.create = create ?? throw new ArgumentNullException(nameof(create));
        this.validateParams = validateParams; this.references = references;
        Settings = Check(data, validateParams);
    }
    private static PNPlaceableSettings Check(JToken data, Action<JToken> validateParams)
    {
        var settings = new PNPlaceableSettings(data);
        if (validateParams != null) validateParams(settings.Params);
        else if (settings.Params != null && settings.Params.HasValues) throw new FormatException("this placeable takes no params.");
        return settings;
    }
    private IPNPlaceableLogic Make()
    {
        var made = create();
        if (made == null || made.GetType() != ImplementationType) throw new InvalidOperationException("Placeable " + Id + " logic factory must return a " + ImplementationType.Name + ".");
        return made;
    }
    private static Type RequireLogic(Type type)
    {
        if (type == null || type.IsAbstract || type.IsInterface || type.ContainsGenericParameters || !typeof(IPNPlaceableLogic).IsAssignableFrom(type))
            throw new ArgumentException("Placeable implementation must be a concrete IPNPlaceableLogic class.");
        return type;
    }
    public override PNInstanceDefinition Derive(EveId id, JToken data, JObject entry) => new PNPlaceableDefinition(id, ImplementationType, create, data, validateParams, references);
}

public static class PNPlaceableIds
{
    public static readonly EveId Vanish = EveId.Parse("project_n:placeable_vanish");
    public static readonly EveId LeaveGround = EveId.Parse("project_n:placeable_leave_ground");
    public static readonly EveId Marker = EveId.Parse("project_n:placeable_marker");
}

// One placed thing: where its footprint starts (the lowest cell), which way it faces (0 to 3, a quarter turn each; an odd direction swaps the footprint's sides) and how much health is left.
public sealed class PNPlacedObject
{
    public int Id { get; }
    public PNPlaceableDefinition Definition { get; }
    public int X { get; }
    public int Z { get; }
    public int Direction { get; }
    public int CellsX { get; }
    public int CellsZ { get; }
    public int Health { get; internal set; }
    internal PNPlacedObject(int id, PNPlaceableDefinition definition, int x, int z, int direction)
    {
        Id = id; Definition = definition; X = x; Z = z; Direction = direction;
        bool turned = (direction & 1) == 1; CellsX = turned ? definition.Settings.Depth : definition.Settings.Width; CellsZ = turned ? definition.Settings.Width : definition.Settings.Depth;
        Health = definition.Settings.Health;
    }
    public bool Covers(int x, int z) => x >= X && x < X + CellsX && z >= Z && z < Z + CellsZ;
    public PNGridRegion Region => new PNGridRegion(X, Z, X + CellsX - 1, Z + CellsZ - 1);
}

// The placed things of one grid world. Every cell it changes goes through the edit gateway, so the clearance field, the graph and the meshes follow.
public sealed class PNGridPlacements
{
    private readonly PNGridWorldData data;
    private readonly PNGridEditor editor;
    private readonly Func<int, bool> buildableGround;
    private readonly Dictionary<int, PNPlacedObject> byId = new Dictionary<int, PNPlacedObject>();
    private readonly Dictionary<long, int> occupied = new Dictionary<long, int>();
    private int nextId = 1;
    public event Action<PNPlacedObject> Added;
    public event Action<PNPlacedObject> Removed;
    public int Count => byId.Count;
    public IEnumerable<PNPlacedObject> All => byId.Values;

    // `buildableGround(paletteIndex)` says whether something may be put down on that ground type.
    public PNGridPlacements(PNGridWorldData data, PNGridEditor editor, Func<int, bool> buildableGround)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data)); this.editor = editor ?? throw new ArgumentNullException(nameof(editor));
        this.buildableGround = buildableGround ?? throw new ArgumentNullException(nameof(buildableGround));
    }
    private long Key(int x, int z) => (long)z * data.Width + x;
    public PNPlacedObject Get(int id) => byId.TryGetValue(id, out var value) ? value : null;
    public PNPlacedObject At(int x, int z) => occupied.TryGetValue(Key(x, z), out var id) ? byId[id] : null;
    public IEnumerable<PNPlacedObject> InRegion(PNGridRegion region) => byId.Values.Where(o => o.X <= region.X1 && o.X + o.CellsX - 1 >= region.X0 && o.Z <= region.Z1 && o.Z + o.CellsZ - 1 >= region.Z0);

    // Checks a placement without doing it: inside the world, every footprint cell free, not occupied, on buildable ground.
    public bool CanPlace(PNPlaceableDefinition definition, int x, int z, int direction, out string reason)
    {
        reason = null;
        if (definition == null) { reason = "No such placeable."; return false; }
        if (direction < 0 || direction > 3) { reason = "Direction is 0 to 3."; return false; }
        if (byId.Count >= PNPlaceableLimits.MaxPlaced) { reason = "The world holds no more placed things."; return false; }
        bool turned = (direction & 1) == 1; int w = turned ? definition.Settings.Depth : definition.Settings.Width, d = turned ? definition.Settings.Width : definition.Settings.Depth;
        if (!data.InBounds(x, z) || !data.InBounds(x + w - 1, z + d - 1)) { reason = "The footprint leaves the world."; return false; }
        for (int cz = z; cz < z + d; cz++) for (int cx = x; cx < x + w; cx++)
        {
            if (data.IsSolid(cx, cz)) { reason = "Cell (" + cx + "," + cz + ") is solid."; return false; }
            if (occupied.ContainsKey(Key(cx, cz))) { reason = "Cell (" + cx + "," + cz + ") is taken."; return false; }
            if (!buildableGround(data.GroundAt(cx, cz))) { reason = "Cell (" + cx + "," + cz + ") is not buildable ground."; return false; }
        }
        return true;
    }

    public bool TryPlace(PNPlaceableDefinition definition, int x, int z, int direction, out PNPlacedObject placed, out string reason)
    {
        placed = null;
        if (!CanPlace(definition, x, z, direction, out reason)) return false;
        var candidate = new PNPlacedObject(nextId, definition, x, z, direction);
        if (definition.Settings.BlocksPath)
        {
            var batch = new PNGridEditBatch().AddSolidRegion(candidate.Region, true);
            var result = editor.Submit(batch);
            if (!result.Accepted) { reason = result.Reason; return false; }
        }
        nextId++; byId[candidate.Id] = candidate;
        for (int cz = z; cz < z + candidate.CellsZ; cz++) for (int cx = x; cx < x + candidate.CellsX; cx++) occupied[Key(cx, cz)] = candidate.Id;
        placed = candidate; Added?.Invoke(candidate); return true;
    }

    // Takes `amount` of health; at zero the thing is broken. Returns true when it was broken.
    public bool Damage(int id, int amount)
    {
        if (amount <= 0 || !byId.TryGetValue(id, out var placed) || !placed.Definition.Settings.Breakable) return false;
        placed.Health = Math.Max(0, placed.Health - amount);
        return placed.Health == 0 && Break(id);
    }

    // Breaks a thing: it leaves, its cells are freed and the logic class runs. An unbreakable thing can still be taken away with Remove.
    public bool Break(int id)
    {
        if (!byId.TryGetValue(id, out var placed) || !Detach(placed)) return false;
        placed.Definition.Logic.Broken(new PNPlaceableBreakContext(data, editor, placed, placed.Definition.Settings.Params));
        return true;
    }

    // Takes a thing away without running its logic (a tool or a save that rebuilds the world).
    public bool Remove(int id) => byId.TryGetValue(id, out var placed) && Detach(placed);

    private bool Detach(PNPlacedObject placed)
    {
        if (placed.Definition.Settings.BlocksPath)
        {
            var result = editor.Submit(new PNGridEditBatch().AddSolidRegion(placed.Region, false));
            if (!result.Accepted) return false;   // the queue is full: keep the thing and try again later
        }
        byId.Remove(placed.Id);
        for (int cz = placed.Z; cz < placed.Z + placed.CellsZ; cz++) for (int cx = placed.X; cx < placed.X + placed.CellsX; cx++) occupied.Remove(Key(cx, cz));
        Removed?.Invoke(placed); return true;
    }
}
