using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

// EN: The map tool's core (Docs/SandboxPlan.md, step G9). A document is an OFFLINE grid world (cells, placed things, the map block with spawn points, gates, zones and groups) edited through the same gateway as a
//     running world (`PNGridEditor`, `PNGridPlacements`), so a tool can never make a state that a live world cannot reach. Every operation is one undoable step; a compound step (a pasted stamp) is one step too.
//     Pure C#, no Unity: the Editor window and the in-game UI are only faces for this class, and the smoke suite checks it. The result is a `PNGridWorldSnapshot` (the file format of step G7 plus the optional
//     map block), so a map file and a saved world are the same kind of file.
// VI: Lõi công cụ dựng map (G9). Tài liệu là world lưới NGOẠI TUYẾN, sửa qua cùng cổng (`PNGridEditor`, `PNGridPlacements`) nên công cụ không thể tạo trạng thái mà world chạy không đạt tới. Mỗi thao tác là một bước hoàn tác được.
public enum PNMapIssueLevel { Error, Warning }

public sealed class PNMapIssue
{
    public PNMapIssueLevel Level;
    public string Code, Message;
    public int X = -1, Z = -1;
    public override string ToString() => (Level == PNMapIssueLevel.Error ? "error " : "warning ") + Code + ": " + Message + (X >= 0 ? " at (" + X + "," + Z + ")" : "");
}

public sealed class PNGridMapDoc
{
    public const int MaxUndo = 256, MaxCellsPerSlice = 60000;
    private sealed class Step { public string Name; public Action Undo, Redo; }

    public PNGridWorldDescriptor Descriptor { get; }
    public PNGridWorldData Data { get; }
    public PNGridEditor Editor { get; }
    public PNGridPlacements Placements { get; }
    public PNGridMapMeta Meta { get; private set; } = new PNGridMapMeta();
    public IReadOnlyList<string> Palette => Data.Palette;
    public bool Dirty { get; private set; }
    public int UndoCount => undo.Count;
    public int RedoCount => redo.Count;
    public string LastUndoName => undo.Count == 0 ? null : undo[undo.Count - 1].Name;

    private readonly bool[] walkable;
    private readonly Func<string, PNPlaceableDefinition> resolve;
    private readonly List<Step> undo = new List<Step>(), redo = new List<Step>();
    private List<Step> compound;
    private readonly List<PNGridRegion> scratch = new List<PNGridRegion>();

    // `walkable[i]`: palette entry i can be walked on (and built on). `resolve(id)`: the placeable definition of that id, or null.
    public PNGridMapDoc(PNGridWorldDescriptor descriptor, IReadOnlyList<string> palette, bool[] walkable, Func<string, PNPlaceableDefinition> resolve)
    {
        Descriptor = (descriptor ?? throw new ArgumentNullException(nameof(descriptor))).WithoutRestore();
        Data = new PNGridWorldData(Descriptor, palette ?? throw new ArgumentNullException(nameof(palette)));
        if (walkable == null || walkable.Length != Data.Palette.Count) throw new ArgumentException("One walkability flag per palette entry is required.", nameof(walkable));
        this.walkable = (bool[])walkable.Clone();
        this.resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        Editor = new PNGridEditor(Data) { AllowHeight = true };
        Placements = new PNGridPlacements(Data, Editor, index => index >= 0 && index < this.walkable.Length && this.walkable[index]);
    }

    // Opens a map file (or a saved world): cells, placed things and the map block.
    public static PNGridMapDoc FromSnapshot(PNGridWorldSnapshot snapshot, bool[] walkable, Func<string, PNPlaceableDefinition> resolve)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        var doc = new PNGridMapDoc(snapshot.Descriptor, snapshot.Palette, walkable, resolve);
        snapshot.ApplyAll(doc.Data);
        foreach (var item in snapshot.Placed)
        {
            var definition = resolve(item.Definition) ?? throw new FormatException("The map places " + item.Definition + ", which this game does not register.");
            doc.Placements.Restore(definition, item.Id, item.X, item.Z, item.Direction, item.Health, item.Key);
        }
        if (snapshot.Meta != null) doc.Meta = snapshot.Meta.Clone();
        doc.Dirty = false;
        return doc;
    }

    public PNGridWorldSnapshot ToSnapshot()
    {
        var placed = Placements.All.OrderBy(x => x.Id).Select(x => new PNGridSavedPlacement { Id = x.Id, Definition = x.Definition.Id.ToString(), X = x.X, Z = x.Z, Direction = x.Direction, Health = x.Health, Key = x.Key }).ToList();
        return PNGridWorldSnapshot.Build(Data, Descriptor, placed, null, Meta.IsEmpty ? null : Meta.Clone());
    }

    public void MarkSaved() { Dirty = false; }

    // ---- steps and undo ----
    private void Do(string name, Action redoAction, Action undoAction)
    {
        redoAction();
        var step = new Step { Name = name, Undo = undoAction, Redo = redoAction };
        if (compound != null) { compound.Add(step); return; }
        Push(step);
    }
    private void Push(Step step)
    {
        undo.Add(step); if (undo.Count > MaxUndo) undo.RemoveAt(0);
        redo.Clear(); Dirty = true;
    }
    // Runs `body`; everything it does becomes ONE undoable step. A failure inside rolls the already done parts back and rethrows.
    public void Compound(string name, Action body)
    {
        if (compound != null) { body(); return; }
        compound = new List<Step>();
        var done = compound;
        try { body(); }
        catch { compound = null; for (int i = done.Count - 1; i >= 0; i--) done[i].Undo(); throw; }
        compound = null;
        if (done.Count == 0) return;
        Push(new Step
        {
            Name = name,
            Undo = () => { for (int i = done.Count - 1; i >= 0; i--) done[i].Undo(); },
            Redo = () => { foreach (var s in done) s.Redo(); }
        });
    }
    public bool Undo()
    {
        if (undo.Count == 0) return false;
        var step = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1);
        step.Undo(); redo.Add(step); Dirty = true; return true;
    }
    public bool Redo()
    {
        if (redo.Count == 0) return false;
        var step = redo[redo.Count - 1]; redo.RemoveAt(redo.Count - 1);
        step.Redo(); undo.Add(step); Dirty = true; return true;
    }

    // ---- cell edits through the gateway ----
    // Applies edits as 64 x 64 tile batches and drains the queue (a document has no frame to spread them over).
    private void Apply(IReadOnlyList<PNGridEdit> edits)
    {
        var tiles = new Dictionary<long, PNGridEditBatch>(); int queued = 0;
        void Flush()
        {
            if (tiles.Count == 0) return;
            var result = Editor.SubmitGroup(tiles.Values.ToList());
            if (!result.Accepted) throw new InvalidOperationException(result.Reason);
            tiles.Clear(); queued = 0;
            while (Editor.QueuedCells > 0) { scratch.Clear(); Editor.Tick(PNGridEditLimits.MaxCellsPerTick, scratch); }
        }
        foreach (var e in edits)
        {
            long key = ((long)(e.Z >> 6) << 32) | (uint)(e.X >> 6);
            if (!tiles.TryGetValue(key, out var batch)) tiles[key] = batch = new PNGridEditBatch();
            switch (e.Kind)
            {
                case PNGridEditKind.Solid: batch.AddSolid(e.X, e.Z, e.Value != 0); break;
                case PNGridEditKind.Ground: batch.AddGround(e.X, e.Z, e.Value); break;
                default: batch.AddHeight(e.X, e.Z, e.Value); break;
            }
            if (++queued >= MaxCellsPerSlice) Flush();
        }
        Flush();
    }

    // Edits that really change something, with the values they replace.
    // Several cell edits as one undoable step (the generator and the UI use it). Cells that already hold the value are skipped.
    public void Edit(string name, IEnumerable<PNGridEdit> wanted) => Change(name, wanted);

    private void Change(string name, IEnumerable<PNGridEdit> wanted)
    {
        var forward = new List<PNGridEdit>(); var backward = new List<PNGridEdit>(); var seenGround = new HashSet<long>();
        foreach (var e in wanted)
        {
            switch (e.Kind)
            {
                case PNGridEditKind.Solid:
                    if (Data.IsSolid(e.X, e.Z) == (e.Value != 0)) continue;
                    forward.Add(e); backward.Add(new PNGridEdit(PNGridEditKind.Solid, e.X, e.Z, e.Value != 0 ? 0 : 1)); break;
                case PNGridEditKind.Ground:
                    int gx = e.X / Data.GroundFactor, gz = e.Z / Data.GroundFactor;
                    if (Data.GroundCell(gx, gz) == e.Value || !seenGround.Add(((long)gz << 32) | (uint)gx)) continue;
                    forward.Add(new PNGridEdit(PNGridEditKind.Ground, gx * Data.GroundFactor, gz * Data.GroundFactor, e.Value));
                    backward.Add(new PNGridEdit(PNGridEditKind.Ground, gx * Data.GroundFactor, gz * Data.GroundFactor, Data.GroundCell(gx, gz))); break;
                default:
                    if (Data.HeightRaw(e.X, e.Z) == e.Value) continue;
                    forward.Add(e); backward.Add(new PNGridEdit(PNGridEditKind.Height, e.X, e.Z, Data.HeightRaw(e.X, e.Z))); break;
            }
        }
        if (forward.Count == 0) return;
        Do(name, () => Apply(forward), () => Apply(backward));
    }

    private PNGridRegion Clip(PNGridRegion region)
    {
        var r = region.Clip(Data.Width, Data.Depth);
        if (!r.IsValid) throw new ArgumentException("The region is outside the world.");
        return r;
    }

    public void PaintGround(PNGridRegion region, int paletteIndex)
    {
        if (paletteIndex < 0 || paletteIndex >= Data.Palette.Count) throw new ArgumentOutOfRangeException(nameof(paletteIndex));
        var r = Clip(region);
        Change("paint ground", Cells(r, (x, z) => new PNGridEdit(PNGridEditKind.Ground, x, z, paletteIndex)));
    }

    // Walls on or off. A cell under a placed thing is refused (take the thing away first): its footprint and the cells must agree.
    public void SetSolid(PNGridRegion region, bool value)
    {
        var r = Clip(region);
        var inside = Placements.InRegion(r).FirstOrDefault(t => value || t.Definition.Settings.BlocksPath);
        if (inside != null) throw new InvalidOperationException("A " + inside.Definition.Id + " stands in the region; remove it first.");
        Change(value ? "draw wall" : "erase wall", Cells(r, (x, z) => new PNGridEdit(PNGridEditKind.Solid, x, z, value ? 1 : 0)));
    }

    public void SetHeight(PNGridRegion region, int raw)
    {
        var r = Clip(region);
        Change("set height", Cells(r, (x, z) => new PNGridEdit(PNGridEditKind.Height, x, z, raw)));
    }

    private static IEnumerable<PNGridEdit> Cells(PNGridRegion r, Func<int, int, PNGridEdit> make)
    {
        for (int z = r.Z0; z <= r.Z1; z++) for (int x = r.X0; x <= r.X1; x++) yield return make(x, z);
    }

    // ---- placed things ----
    public PNPlacedObject Place(string definitionId, int x, int z, int direction)
    {
        var definition = resolve(definitionId) ?? throw new ArgumentException("No such placeable: " + definitionId);
        if (!Placements.CanPlace(definition, x, z, direction, out string reason)) throw new InvalidOperationException(reason);
        PNPlacedObject placed = null;
        Do("place " + definitionId, () =>
        {
            if (!Placements.TryPlace(definition, x, z, direction, out placed, out string why)) throw new InvalidOperationException(why);
            Drain();
        }, () => { Placements.Remove(placed.Id); Drain(); });
        return placed;
    }

    // Places a thing with an interactable under an authored key (G9c). The key is checked before anything changes.
    public PNPlacedObject Place(string definitionId, int x, int z, int direction, string key)
    {
        var definition = resolve(definitionId) ?? throw new ArgumentException("No such placeable: " + definitionId);
        if (!Placements.CanPlace(definition, x, z, direction, out string reason)) throw new InvalidOperationException(reason);
        PNPlacedObject placed = null;
        Do("place " + definitionId, () =>
        {
            if (!Placements.TryPlace(definition, x, z, direction, key, out placed, out string why)) throw new InvalidOperationException(why);
            Drain();
        }, () => { Placements.Remove(placed.Id); Drain(); });
        return placed;
    }

    // Gives the thing at a cell another authored key (null or empty = the generated one).
    public void SetKey(int x, int z, string key)
    {
        var thing = Placements.At(x, z) ?? throw new ArgumentException("No thing at (" + x + "," + z + ").");
        key = string.IsNullOrEmpty(key) ? null : key; var before = thing.Key; int id = thing.Id;
        if (before == key) return;
        Do("key of " + thing.Definition.Id, () => { if (!Placements.SetKey(id, key, out string why)) throw new InvalidOperationException(why); }, () => Placements.SetKey(id, before, out _));
    }

    public bool RemoveAt(int x, int z)
    {
        var thing = Placements.At(x, z);
        if (thing == null) return false;
        var definition = thing.Definition; int tx = thing.X, tz = thing.Z, dir = thing.Direction; string key = thing.Key; PNPlacedObject current = thing;
        Do("remove " + definition.Id, () => { Placements.Remove(current.Id); Drain(); },
            () => { if (!Placements.TryPlace(definition, tx, tz, dir, key, out current, out string why)) throw new InvalidOperationException(why); Drain(); });
        return true;
    }

    private void Drain() { while (Editor.QueuedCells > 0) { scratch.Clear(); Editor.Tick(PNGridEditLimits.MaxCellsPerTick, scratch); } }

    // ---- the map block ----
    private void Meta2(string name, Action<PNGridMapMeta> edit)
    {
        var before = Meta.Clone(); var after = Meta.Clone(); edit(after);
        after.CheckShape(Data.Width, Data.Depth);
        Do(name, () => Meta = after.Clone(), () => Meta = before.Clone());
    }

    public void SetSpawn(string name, string kind, int x, int z) => Meta2("spawn " + name, m =>
    {
        m.Spawns.RemoveAll(s => s.Name == name); m.Spawns.Add(new PNMapSpawn { Name = name, Kind = kind, X = x, Z = z });
    });
    public void RemoveSpawn(string name) => Meta2("remove spawn " + name, m => { if (m.Spawns.RemoveAll(s => s.Name == name) == 0) throw new ArgumentException("No spawn point " + name); });

    public void SetGate(string name, PNGridRegion region, string to = "") => Meta2("gate " + name, m =>
    {
        m.Gates.RemoveAll(g => g.Name == name); m.Gates.Add(new PNMapGate { Name = name, Region = region, To = to ?? "" });
    });
    public void RemoveGate(string name) => Meta2("remove gate " + name, m => { if (m.Gates.RemoveAll(g => g.Name == name) == 0) throw new ArgumentException("No gate " + name); });

    public void SetZone(string name, PNGridRegion region, bool spawnBlock, double rateScale = 1) => Meta2("zone " + name, m =>
    {
        m.Zones.RemoveAll(z => z.Name == name); m.Zones.Add(new PNMapZone { Name = name, Region = region, SpawnBlock = spawnBlock, RateScale = rateScale });
    });
    public void RemoveZone(string name) => Meta2("remove zone " + name, m => { if (m.Zones.RemoveAll(z => z.Name == name) == 0) throw new ArgumentException("No zone " + name); });

    public void SetGroup(string name, int x, int z, IEnumerable<(string entity, int count)> entries, double radius = PNMapGroup.DefaultRadius) => Meta2("group " + name, m =>
    {
        m.Groups.RemoveAll(g => g.Name == name);
        m.Groups.Add(new PNMapGroup { Name = name, X = x, Z = z, Radius = radius, Entries = entries.Select(e => new PNMapGroupEntry { Entity = e.entity, Count = e.count }).ToList() });
    });
    public void RemoveGroup(string name) => Meta2("remove group " + name, m => { if (m.Groups.RemoveAll(g => g.Name == name) == 0) throw new ArgumentException("No group " + name); });

    // ---- stamps ----
    // Copies a region (aligned to the 2 m ground cell) with the placed things inside it.
    public PNGridStamp Copy(PNGridRegion region)
    {
        var r = Clip(region);
        return PNGridStamp.Capture(Data, Placements, r);
    }

    // Puts a stamp down with its corner at (x, z) (a multiple of the ground cell): replaces the cells and the placed things in its area, as one step.
    public void Paste(PNGridStamp stamp, int x, int z)
    {
        if (stamp == null) throw new ArgumentNullException(nameof(stamp));
        var target = new PNGridRegion(x, z, x + stamp.Width - 1, z + stamp.Depth - 1);
        if (x < 0 || z < 0 || target.X1 >= Data.Width || target.Z1 >= Data.Depth) throw new InvalidOperationException("The stamp does not fit there.");
        if (x % Data.GroundFactor != 0 || z % Data.GroundFactor != 0) throw new InvalidOperationException("A stamp goes on a multiple of the ground cell (" + Data.GroundFactor + " cells).");
        var map = stamp.PaletteMap(Data.Palette);
        Compound("paste stamp", () =>
        {
            foreach (var thing in Placements.InRegion(target).ToList()) RemoveAt(thing.X, thing.Z);
            var edits = new List<PNGridEdit>();
            for (int cz = 0; cz < stamp.Depth; cz++) for (int cx = 0; cx < stamp.Width; cx++)
            {
                edits.Add(new PNGridEdit(PNGridEditKind.Solid, x + cx, z + cz, stamp.SolidAt(cx, cz) ? 1 : 0));
                edits.Add(new PNGridEdit(PNGridEditKind.Height, x + cx, z + cz, stamp.HeightAt(cx, cz)));
            }
            for (int gz = 0; gz < stamp.Depth / Data.GroundFactor; gz++) for (int gx = 0; gx < stamp.Width / Data.GroundFactor; gx++)
                edits.Add(new PNGridEdit(PNGridEditKind.Ground, x + gx * Data.GroundFactor, z + gz * Data.GroundFactor, map[stamp.GroundAt(gx, gz)]));
            // the solid flags of a stamp already include the footprints of its blocking things
            Change("stamp cells", edits);
            foreach (var thing in stamp.Things) PlaceStamped(thing, x, z);
        });
    }

    private void PlaceStamped(PNStampThing thing, int ox, int oz)
    {
        var definition = resolve(thing.Definition) ?? throw new InvalidOperationException("The stamp places " + thing.Definition + ", which this game does not register.");
        int tx = ox + thing.X, tz = oz + thing.Z;
        // the cells of a blocking thing are solid already (they came with the stamp); the gateway wants them free, so free them for the placement
        if (definition.Settings.BlocksPath)
        {
            bool turned = (thing.Direction & 1) == 1;
            int w = turned ? definition.Settings.Depth : definition.Settings.Width, d = turned ? definition.Settings.Width : definition.Settings.Depth;
            Change("stamp thing cells", Cells(new PNGridRegion(tx, tz, tx + w - 1, tz + d - 1), (cx, cz) => new PNGridEdit(PNGridEditKind.Solid, cx, cz, 0)));
        }
        Place(thing.Definition, tx, tz, thing.Direction);
    }

    // ---- validation ----
    public const int MaxIssues = 200;

    // The checks a map must pass before it is exported: a way in for players, every spawn and gate standing on room to walk in and connected to where players enter, things on solid ground, no height yet.
    public List<PNMapIssue> Validate(Func<string, bool> entityKnown = null)
    {
        var issues = new List<PNMapIssue>();
        void Add(PNMapIssueLevel level, string code, string message, int x = -1, int z = -1)
        { if (issues.Count < MaxIssues) issues.Add(new PNMapIssue { Level = level, Code = code, Message = message, X = x, Z = z }); }
        var nav = new PNGridNavigationSource(Data, walkable, PNNavigationProfile.GroundSmall);
        var players = Meta.Spawns.Where(s => s.Kind == PNGridMapMeta.PlayerKind).ToList();
        if (players.Count == 0) Add(PNMapIssueLevel.Error, "no-player-spawn", "The map has no player spawn point.");
        if (!Data.IsFlat()) Add(PNMapIssueLevel.Error, "height", "The grid navigation supports a flat floor only; set every height back to 0.");
        foreach (var s in Meta.Spawns)
            if (!nav.Walkable(s.X, s.Z)) Add(PNMapIssueLevel.Error, "spawn-blocked", "Spawn point " + s.Name + " has no room to stand (wall, ground that cannot be walked on, or too close to a wall).", s.X, s.Z);
        foreach (var g in Meta.Groups)
            if (!nav.Walkable(g.X, g.Z)) Add(PNMapIssueLevel.Error, "group-blocked", "Group " + g.Name + " stands where a monster cannot.", g.X, g.Z);
        foreach (var g in Meta.Gates)
        {
            bool any = false;
            for (int z = g.Region.Z0; z <= g.Region.Z1 && !any; z++) for (int x = g.Region.X0; x <= g.Region.X1 && !any; x++) any = nav.Walkable(x, z);
            if (!any) Add(PNMapIssueLevel.Error, "gate-blocked", "Gate " + g.Name + " has no walkable cell.", g.Region.X0, g.Region.Z0);
        }
        if (entityKnown != null) foreach (var g in Meta.Groups) foreach (var e in g.Entries)
            if (!entityKnown(e.Entity)) Add(PNMapIssueLevel.Error, "group-entity", "Group " + g.Name + " names " + e.Entity + ", which is not registered.", g.X, g.Z);
        // connected to where the players enter (4-neighbour flood fill over the cells a small body can stand on)
        var first = players.FirstOrDefault(s => nav.Walkable(s.X, s.Z));
        if (first != null)
        {
            var reach = Flood(nav, first.X, first.Z);
            foreach (var s in Meta.Spawns) if (nav.Walkable(s.X, s.Z) && !reach[s.Z * Data.Width + s.X]) Add(PNMapIssueLevel.Error, "spawn-cut-off", "Spawn point " + s.Name + " cannot be reached from the first player spawn.", s.X, s.Z);
            foreach (var g in Meta.Groups) if (nav.Walkable(g.X, g.Z) && !reach[g.Z * Data.Width + g.X]) Add(PNMapIssueLevel.Warning, "group-cut-off", "Group " + g.Name + " cannot be reached from the first player spawn.", g.X, g.Z);
            foreach (var g in Meta.Gates)
            {
                bool reachable = false;
                for (int z = g.Region.Z0; z <= g.Region.Z1 && !reachable; z++) for (int x = g.Region.X0; x <= g.Region.X1 && !reachable; x++) reachable = nav.Walkable(x, z) && reach[z * Data.Width + x];
                if (!reachable) Add(PNMapIssueLevel.Error, "gate-cut-off", "Gate " + g.Name + " cannot be reached from the first player spawn.", g.Region.X0, g.Region.Z0);
            }
        }
        foreach (var thing in Placements.All)
            if (thing.Definition.Settings.BlocksPath)
                for (int z = thing.Z; z < thing.Z + thing.CellsZ; z++) for (int x = thing.X; x < thing.X + thing.CellsX; x++)
                    if (!Data.IsSolid(x, z)) { Add(PNMapIssueLevel.Error, "thing-cells", "A " + thing.Definition.Id + " stands on a cell that is not solid.", x, z); z = thing.Z + thing.CellsZ; break; }
        return issues;
    }

    // Cells connected (4 ways) to a start cell through walkable cells; index z * Width + x.
    public bool[] Flood(IPNNavigationSource nav, int sx, int sz)
    {
        var seen = new bool[Data.Width * Data.Depth];
        if (!nav.Walkable(sx, sz)) return seen;
        var stack = new Stack<int>(); stack.Push(sz * Data.Width + sx); seen[sz * Data.Width + sx] = true;
        while (stack.Count > 0)
        {
            int at = stack.Pop(), x = at % Data.Width, z = at / Data.Width;
            void Visit(int nx, int nz)
            {
                if (nx < 0 || nz < 0 || nx >= Data.Width || nz >= Data.Depth) return;
                int i = nz * Data.Width + nx; if (seen[i] || !nav.Walkable(nx, nz)) return;
                seen[i] = true; stack.Push(i);
            }
            Visit(x + 1, z); Visit(x - 1, z); Visit(x, z + 1); Visit(x, z - 1);
        }
        return seen;
    }
}
