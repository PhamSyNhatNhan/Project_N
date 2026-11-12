#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

// EN: The map tool's Editor face (Docs/SandboxPlan.md, step G9), built with UI Toolkit. It is only a view and a set of buttons on `PNGridMapDoc` (Assets/Core/GridWorld/Tools), so everything it does goes through the
//     same gateway as a running world and is one undoable step; nothing is put in a scene. Layout, floating over the canvas like Blender's overlays: a thin menu bar (File, Edit, View, Map; undo, redo, 2D / 3D / split),
//     a rounded strip of tool icons on the left, an options bar for the chosen tool at the top, a collapsible panel on the right with icon tabs (Palette, Selection, Layers, Map: block, generator, stamps, check, new
//     map) that can be resized by its left edge, and a status pill at the bottom. The canvas (2D, with zoom, pan, grid, minimap) and the 3D view (view only, a window of cells around the canvas centre) are drawn
//     by IMGUI containers underneath the overlays. Tools are a data table (`Tools`): id, label, glyph, key. Hotkeys follow that table. Menu: Tools/Project N/Grid Map Editor.
// VI: Mặt Editor của công cụ dựng map (G9), viết bằng UI Toolkit. Chỉ là khung nhìn và các nút trên `PNGridMapDoc`, mọi thao tác qua cùng cổng với world chạy và là một bước hoàn tác được; không đặt gì vào scene.
//     Bố cục nổi trên canvas như Blender: thanh menu mỏng, dải icon công cụ bo góc bên trái, thanh tuỳ chọn công cụ ở trên, panel phải thu gọn được có tab icon (kéo mép trái để đổi rộng), thanh trạng thái nhỏ ở dưới.
public sealed class PNGridMapEditorWindow : EditorWindow
{
    public enum Tool { Inspect, Ground, Solid, Eraser, Place, RemoveThing, PlayerSpawn, MonsterSpawn, Gate, Zone, Group, Copy, Paste, Fill, Picker }
    private enum ViewMode { Flat, Solid, Split }
    private enum Tab { Palette, Selection, Layers, Map }

    // The tool table: the strip, the tooltips and the hotkeys all come from here. `RemoveThing` is kept as a value for old scripts and is not shown (the eraser does it).
    private static readonly (Tool tool, string label, string glyph, KeyCode key, string tip)[] Tools =
    {
        (Tool.Inspect, "Select", "select", KeyCode.V, "Select a spawn, gate, zone, group or thing and edit it in the Selection tab"),
        (Tool.Ground, "Brush", "brush", KeyCode.B, "Paint the chosen ground"),
        (Tool.Solid, "Solid", "wall", KeyCode.W, "Draw solid cells (the grid keeps them as walls; things that block the way make them too)"),
        (Tool.Eraser, "Eraser", "erase", KeyCode.E, "Erase what is under the brush (the chips in the options bar choose what)"),
        (Tool.Fill, "Fill", "fill", KeyCode.G, "Fill the connected ground of the same kind with the chosen ground"),
        (Tool.Picker, "Picker", "picker", KeyCode.I, "Pick the ground or thing under the cursor"),
        (Tool.Place, "Thing", "cube", KeyCode.T, "Place the chosen thing (R rotates)"),
        (Tool.PlayerSpawn, "Player spawn", "player", KeyCode.None, "Put a player spawn point"),
        (Tool.MonsterSpawn, "Monster spawn", "monster", KeyCode.None, "Put a monster spawn point"),
        (Tool.Gate, "Gate", "gate", KeyCode.None, "Drag a rectangle for a gate"),
        (Tool.Zone, "Zone", "zone", KeyCode.None, "Drag a rectangle for a zone"),
        (Tool.Group, "Monster group", "group", KeyCode.None, "Put a fixed group of monsters"),
        (Tool.Copy, "Copy region", "copy", KeyCode.M, "Drag a rectangle to copy it as the stamp"),
        (Tool.Paste, "Paste stamp", "paste", KeyCode.None, "Paste the stamp at the cursor"),
    };

    private static readonly Color PanelColor = new Color(.13f, .14f, .17f, .94f), EdgeColor = new Color(1, 1, 1, .09f), Accent = new Color(.25f, .5f, .92f), TextColor = new Color(.87f, .88f, .91f), DimColor = new Color(.6f, .62f, .67f), FieldColor = new Color(.2f, .21f, .25f, 1f);

    private EveRegistryHub hub;
    private PNGroundDefinition[] grounds = new PNGroundDefinition[0];
    private PNPlaceableDefinition[] things = new PNPlaceableDefinition[0];
    private bool[] walkable = new bool[0];
    private string[] groundNames = new string[0], thingNames = new string[0];

    public PNGridMapDoc Doc { get; private set; }
    public string FilePath { get; private set; }
    public string Status { get; private set; } = "";
    public List<PNMapIssue> Issues { get; private set; } = new List<PNMapIssue>();
    public Tool Current = Tool.Ground;
    public int Brush = 2, GroundIndex, ThingIndex, ThingDirection;
    public string SpawnName = "player", GateName = "exit", GateTo = "", ZoneName = "zone", GroupName = "pack", GroupEntries = "";
    public bool ZoneSpawnBlock = true;
    public double ZoneRate = 1;
    public int NewWidth = 128, NewDepth = 128;
    public float NewCell = .5f;
    public long Seed = 1;
    public string Chain = "[\n  { \"step\": \"noise_ground\", \"ground\": \"project_n:sand\", \"scale\": 14, \"threshold\": 0.55 }\n]";
    public readonly Dictionary<string, PNGridStamp> Stamps = new Dictionary<string, PNGridStamp>(StringComparer.Ordinal);
    // eraser chips
    public bool EraseSolid = true, EraseThings = true, EraseMarkers = true, EraseGround = true;
    // layers
    public bool ShowGrid = true, ShowZones = true, ShowGates = true, ShowSpawns = true, ShowGroups = true, ShowThings = true, ShowSafety = true, ShowMini = true;

    private Texture2D texture;
    private bool viewDirty = true, fitPending = true;
    private int meshVersion, builtVersion = -1, builtCx = int.MinValue, builtCz = int.MinValue;
    private Vector2 pan = new Vector2(10, 10);
    private float zoom = 4f;
    private ViewMode view = ViewMode.Flat;
    private readonly HashSet<long> stroke = new HashSet<long>();
    private bool dragging, miniDrag, spaceHeld; private int dragX, dragZ, hoverX = -1, hoverZ = -1;
    private string inspect = "";
    private Rect canvas = new Rect(0, 0, 800, 600);
    // selection (the inspector)
    private string selKind = "", selName = ""; private int selA, selB, selC, selD; private bool selBlock; private double selRate = 1; private string selText = "", selTo = "", selSpawnKind = "";
    // 3D
    // The 3D view with the game's own material and wall colours (what PNGridWorldHost draws), instead of the tool's flat-shaded one.
    public bool GameLook;
    private Button gameButton; private Material gameMaterial;
    private PreviewRenderUtility preview; private Mesh mesh; private Material material;
    private float yaw = 35f, pitch = 50f, distance = 40f; private Vector3 target;

    // ---- UI Toolkit ----
    private IMGUIContainer flatBox, solidBox;
    private VisualElement canvasHost, toolStrip, optionsBar, rightPanel, tabStrip, tabBody, statusPill, hint, panelResize;
    private Label statusLabel, cellLabel, fileLabel;
    private Glyph undoGlyph, redoGlyph;
    private readonly Dictionary<Tool, Button> toolButtons = new Dictionary<Tool, Button>();
    private readonly Dictionary<ViewMode, Button> viewButtons = new Dictionary<ViewMode, Button>();
    private readonly Dictionary<Tab, Button> tabButtons = new Dictionary<Tab, Button>();
    private Tab tab = Tab.Palette; private bool panelOpen = true; private float panelWidth = 300f;
    private string optionsKey = "", bodyKey = "";

    // 0 = 2D, 1 = 3D, 2 = both side by side (also for scripted checks).
    public int ViewIndex { get => (int)view; set { view = (ViewMode)Mathf.Clamp(value, 0, 2); ApplyView(); } }

    [MenuItem("Tools/Project N/Grid Map Editor")]
    public static PNGridMapEditorWindow Open()
    {
        var window = GetWindow<PNGridMapEditorWindow>("Grid Map Editor");
        window.minSize = new Vector2(820, 520);
        return window;
    }

    private void Dirty() { viewDirty = true; meshVersion++; Redraw(); }
    private void Redraw() { flatBox?.MarkDirtyRepaint(); solidBox?.MarkDirtyRepaint(); }

    // ---- content (grounds and placeables) ----
    public bool LoadContent()
    {
        try
        {
            hub = new EveRegistryHub();
            new EveBuiltInContentModule().Register(hub); new PNContentModule().Register(hub); new PNGameplayContentModule().Register(hub);
            grounds = hub.Grounds().Snapshot().Values.OrderBy(x => x.Id.ToString(), StringComparer.Ordinal).ToArray();
            walkable = grounds.Select(x => x.Walkable).ToArray();
            groundNames = grounds.Select(x => x.Id.ToString()).ToArray();
            things = hub.Instances().Snapshot().Values.OfType<PNPlaceableDefinition>().OrderBy(x => x.Id.ToString(), StringComparer.Ordinal).ToArray();
            thingNames = things.Select(x => x.Id.ToString()).ToArray();
            return true;
        }
        catch (Exception error) { Status = "Content failed to load: " + error.Message; Debug.LogException(error); return false; }
    }
    private PNPlaceableDefinition Resolve(string id)
    {
        if (!EveId.TryParse(id, out var parsed)) return null;
        try { return hub.Instances().GetEffectiveKind<PNPlaceableDefinition>(parsed); } catch { return null; }
    }

    // ---- documents ----
    public bool NewMap(int width, int depth, float cell, long seed)
    {
        if (hub == null && !LoadContent()) return false;
        try
        {
            Doc = new PNGridMapDoc(new PNGridWorldDescriptor(width, depth, cell, "project_n:flat", seed), groundNames, walkable, Resolve);
            FilePath = null; Issues.Clear(); Dirty(); fitPending = true; ClearSelection(); Status = "New map " + width + " x " + depth + " cells."; return true;
        }
        catch (Exception error) { Status = error.Message; return false; }
    }

    public bool OpenFile(string path)
    {
        if (hub == null && !LoadContent()) return false;
        try
        {
            var snapshot = PNGridMaps.ReadFile(path);
            Doc = PNGridMapDoc.FromSnapshot(snapshot, walkable, Resolve);
            FilePath = path; Issues.Clear(); Dirty(); fitPending = true; ClearSelection(); Status = "Opened " + Path.GetFileName(path); return true;
        }
        catch (Exception error) { Status = "Could not open: " + error.Message; return false; }
    }

    public bool SaveFile(string path)
    {
        if (Doc == null) return false;
        try { PNGridMaps.WriteFile(path, Doc.ToSnapshot()); FilePath = path; Doc.MarkSaved(); Status = "Saved " + Path.GetFileName(path); return true; }
        catch (Exception error) { Status = "Could not save: " + error.Message; return false; }
    }

    public void Validate() { if (Doc == null) return; Issues = Doc.Validate(id => EveId.TryParse(id, out var parsed) && hub.Get<PNEntityDefinition>().TryGet(parsed, out _)); Status = Issues.Count == 0 ? "The map is valid." : Issues.Count(x => x.Level == PNMapIssueLevel.Error) + " errors, " + Issues.Count(x => x.Level == PNMapIssueLevel.Warning) + " warnings."; }

    public bool RunGenerator()
    {
        if (Doc == null) return false;
        try { PNGridMapGen.Run(Doc, JArray.Parse(Chain), Seed, name => Stamps.TryGetValue(name, out var stamp) ? stamp : null); Dirty(); Status = "Generated."; return true; }
        catch (Exception error) { Status = "Generator: " + error.Message; return false; }
    }

    // ---- editing (the same calls the mouse makes) ----
    public bool Try(Action action)
    {
        try { action(); Dirty(); return true; }
        catch (Exception error) { Status = error.Message; return false; }
    }

    private PNGridRegion Square(int x, int z)
    {
        int half = Brush / 2;
        return new PNGridRegion(x - half, z - half, x - half + Brush - 1, z - half + Brush - 1).Clip(Doc.Data.Width, Doc.Data.Depth);
    }

    private bool IsBrushTool(Tool t) => t == Tool.Ground || t == Tool.Solid || t == Tool.Eraser;

    private void Commit()
    {
        if (Doc == null || stroke.Count == 0) { stroke.Clear(); return; }
        var cells = stroke.Select(k => ((int)(k & 0xffffffff), (int)(k >> 32))).ToList(); stroke.Clear();
        if (Current == Tool.Eraser) { Try(() => Doc.Compound("erase", () => Erase(cells))); return; }
        var edits = new List<PNGridEdit>();
        foreach (var (x, z) in cells)
        {
            if (Current == Tool.Ground) edits.Add(new PNGridEdit(PNGridEditKind.Ground, x, z, GroundIndex));
            else if (Doc.Placements.At(x, z) == null) edits.Add(new PNGridEdit(PNGridEditKind.Solid, x, z, 1));
        }
        Try(() => Doc.Edit(Current == Tool.Ground ? "paint ground" : "draw solid", edits));
    }

    // The eraser: what the chips ask for, under the cells of the stroke, as one undoable step.
    private void Erase(List<(int x, int z)> cells)
    {
        if (EraseThings)
        {
            var seen = new HashSet<PNPlacedObject>();
            foreach (var (x, z) in cells) { var thing = Doc.Placements.At(x, z); if (thing != null && seen.Add(thing)) Doc.RemoveAt(thing.X, thing.Z); }
        }
        if (EraseMarkers)
        {
            var set = new HashSet<long>(cells.Select(c => ((long)c.z << 32) | (uint)c.x));
            foreach (var s in Doc.Meta.Spawns.Where(s => set.Contains(((long)s.Z << 32) | (uint)s.X)).ToList()) Doc.RemoveSpawn(s.Name);
            foreach (var g in Doc.Meta.Groups.Where(g => set.Contains(((long)g.Z << 32) | (uint)g.X)).ToList()) Doc.RemoveGroup(g.Name);
        }
        var edits = new List<PNGridEdit>();
        foreach (var (x, z) in cells)
        {
            if (EraseSolid && Doc.Data.IsSolid(x, z) && Doc.Placements.At(x, z) == null) edits.Add(new PNGridEdit(PNGridEditKind.Solid, x, z, 0));
            if (EraseGround) edits.Add(new PNGridEdit(PNGridEditKind.Ground, x, z, VoidIndex));
        }
        if (edits.Count > 0) Doc.Edit("erase", edits);
    }

    // Fills the connected ground cells of the kind under (x, z) with the chosen ground (a ground cell is GroundFactor x GroundFactor map cells).
    private void FloodFill(int x, int z)
    {
        var d = Doc.Data; int f = d.GroundFactor, gx0 = x / f, gz0 = z / f, columns = (d.Width + f - 1) / f, rows = (d.Depth + f - 1) / f;
        int from = d.GroundAt(x, z); if (from == GroundIndex) { Status = "That ground is already there."; return; }
        var seen = new HashSet<int> { gz0 * columns + gx0 }; var queue = new Queue<int>(); queue.Enqueue(gz0 * columns + gx0); var edits = new List<PNGridEdit>();
        while (queue.Count > 0)
        {
            int at = queue.Dequeue(), gx = at % columns, gz = at / columns;
            edits.Add(new PNGridEdit(PNGridEditKind.Ground, gx * f, gz * f, GroundIndex));
            if (edits.Count > 500000) break;
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = gx + dx, nz = gz + dz; if (nx < 0 || nz < 0 || nx >= columns || nz >= rows) continue;
                int key = nz * columns + nx; if (seen.Contains(key) || d.GroundAt(nx * f, nz * f) != from) continue;
                seen.Add(key); queue.Enqueue(key);
            }
        }
        Doc.Edit("fill ground", edits); Status = "Filled " + edits.Count + " ground cells.";
    }

    private IEnumerable<(string, int)> ParseGroup(string text)
    {
        foreach (var part in text.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Trim().Split('*');
            yield return (pieces[0].Trim(), pieces.Length > 1 ? int.Parse(pieces[1].Trim()) : 1);
        }
    }

    // Applies the current tool to a click (or the end of a drag from (x0, z0) to (x1, z1)).
    public void Use(int x0, int z0, int x1, int z1)
    {
        if (Doc == null) return;
        var region = new PNGridRegion(Math.Min(x0, x1), Math.Min(z0, z1), Math.Max(x0, x1), Math.Max(z0, z1));
        switch (Current)
        {
            case Tool.Inspect: Select(x1, z1); break;
            case Tool.Ground: case Tool.Solid: case Tool.Eraser: AddStroke(x1, z1); Commit(); break;
            case Tool.Place: if (things.Length > 0) Try(() => Doc.Place(thingNames[ThingIndex], x1, z1, ThingDirection)); break;
            case Tool.RemoveThing: Try(() => { if (!Doc.RemoveAt(x1, z1)) Status = "Nothing to remove there."; }); break;
            case Tool.Fill: Try(() => FloodFill(x1, z1)); break;
            case Tool.Picker:
                {
                    var thing = Doc.Placements.At(x1, z1);
                    if (thing != null) { int at = Array.IndexOf(thingNames, thing.Definition.Id.ToString()); if (at >= 0) { ThingIndex = at; Current = Tool.Place; Status = "Picked " + thingNames[at] + "."; } }
                    else { GroundIndex = Mathf.Clamp(Doc.Data.GroundAt(x1, z1), 0, Math.Max(0, groundNames.Length - 1)); Current = Tool.Ground; Status = "Picked " + groundNames[GroundIndex] + "."; }
                    break;
                }
            case Tool.PlayerSpawn: Try(() => Doc.SetSpawn(SpawnName, PNGridMapMeta.PlayerKind, x1, z1)); break;
            case Tool.MonsterSpawn: Try(() => Doc.SetSpawn(SpawnName, PNGridMapMeta.MonsterKind, x1, z1)); break;
            case Tool.Gate: Try(() => Doc.SetGate(GateName, region, GateTo)); break;
            case Tool.Zone: Try(() => Doc.SetZone(ZoneName, region, ZoneSpawnBlock, ZoneRate)); break;
            case Tool.Group: Try(() => Doc.SetGroup(GroupName, x1, z1, ParseGroup(GroupEntries).ToList())); break;
            case Tool.Copy:
                Try(() =>
                {
                    int f = Doc.Data.GroundFactor;
                    var snapped = new PNGridRegion(region.X0 / f * f, region.Z0 / f * f, (region.X1 / f + 1) * f - 1, (region.Z1 / f + 1) * f - 1);
                    Stamps["clip"] = Doc.Copy(snapped); Status = "Copied " + snapped.Width + " x " + snapped.Depth + " cells as the stamp \"clip\".";
                }); break;
            case Tool.Paste:
                if (Stamps.TryGetValue("clip", out var clip)) { int f = Doc.Data.GroundFactor; Try(() => Doc.Paste(clip, x1 / f * f, z1 / f * f)); }
                else Status = "Copy a region first.";
                break;
        }
    }

    // ---- selection ----
    private int selCellX = -1, selCellZ = -1;
    private void ClearSelection() { selKind = ""; selName = ""; selCellX = selCellZ = -1; }
    // The ground that means "nothing" (no floor), or 0 when the content has none.
    private int VoidIndex { get { int i = Array.IndexOf(groundNames, PNGridIds.Void.ToString()); return i < 0 ? 0 : i; } }
    private bool IsVisibleGround(int index) => index < 0 || index >= grounds.Length || grounds[index].Visible;

    private void Select(int x, int z)
    {
        ClearSelection();
        if (!Doc.Data.InBounds(x, z)) return;
        selCellX = x; selCellZ = z; ShowTab(Tab.Selection);
        var meta = Doc.Meta;
        var spawn = meta.Spawns.FirstOrDefault(s => Math.Abs(s.X - x) <= 1 && Math.Abs(s.Z - z) <= 1);
        if (spawn != null) { selKind = "spawn"; selName = spawn.Name; selA = spawn.X; selB = spawn.Z; selSpawnKind = spawn.Kind; ShowTab(Tab.Selection); return; }
        var group = meta.Groups.FirstOrDefault(g => Math.Abs(g.X - x) <= 1 && Math.Abs(g.Z - z) <= 1);
        if (group != null) { selKind = "group"; selName = group.Name; selA = group.X; selB = group.Z; selText = string.Join(", ", group.Entries.Select(e => e.Entity + "*" + e.Count)); selRate = group.Radius; ShowTab(Tab.Selection); return; }
        var thing = Doc.Placements.At(x, z);
        if (thing != null) { selKind = "thing"; selName = thing.Definition.Id.ToString(); selA = thing.X; selB = thing.Z; selC = thing.Direction; selD = thing.Health; selText = thing.Key ?? ""; ShowTab(Tab.Selection); return; }
        var gate = meta.Gates.FirstOrDefault(g => g.Region.Contains(x, z));
        if (gate != null) { selKind = "gate"; selName = gate.Name; selA = gate.Region.X0; selB = gate.Region.Z0; selC = gate.Region.X1; selD = gate.Region.Z1; selTo = gate.To ?? ""; ShowTab(Tab.Selection); return; }
        var zone = meta.Zones.FirstOrDefault(zn => zn.Region.Contains(x, z));
        if (zone != null) { selKind = "zone"; selName = zone.Name; selA = zone.Region.X0; selB = zone.Region.Z0; selC = zone.Region.X1; selD = zone.Region.Z1; selBlock = zone.SpawnBlock; selRate = zone.RateScale; ShowTab(Tab.Selection); }
    }

    private IEnumerable<string> CellLines(int x, int z)
    {
        var d = Doc.Data; var thing = Doc.Placements.At(x, z);
        yield return "Cell (" + x + ", " + z + ")";
        yield return "Ground: " + d.GroundId(x, z) + (IsVisibleGround(d.GroundAt(x, z)) ? "" : " (nothing)") + (d.GroundAt(x, z) < walkable.Length && !walkable[d.GroundAt(x, z)] ? ", not walkable" : "");
        yield return "Solid: " + (d.IsSolid(x, z) ? (thing != null ? "yes (part of " + thing.Definition.Id + ")" : "yes") : "no");
        yield return "Height: " + d.Height(x, z).ToString("0.##") + " m   Clearance: " + d.Clearance(x, z) + " cells";
        if (thing != null) yield return "Thing: " + thing.Definition.Id + " at (" + thing.X + ", " + thing.Z + ") direction " + thing.Direction;
        foreach (var zone in Doc.Meta.Zones) if (zone.Region.Contains(x, z)) yield return "In zone " + zone.Name + (zone.SpawnBlock ? " (spawn block)" : " (rate x" + zone.RateScale + ")");
        foreach (var gate in Doc.Meta.Gates) if (gate.Region.Contains(x, z)) yield return "In gate " + gate.Name;
    }

    private bool ThingHasInteractable => selKind == "thing" && Doc != null && Doc.Placements.At(selA, selB)?.Definition.Settings.Interactable != null;

    private void DeleteSelection()
    {
        if (Doc == null || selKind == "") return;
        switch (selKind)
        {
            case "spawn": Try(() => Doc.RemoveSpawn(selName)); break;
            case "gate": Try(() => Doc.RemoveGate(selName)); break;
            case "zone": Try(() => Doc.RemoveZone(selName)); break;
            case "group": Try(() => Doc.RemoveGroup(selName)); break;
            case "thing": Try(() => Doc.RemoveAt(selA, selB)); break;
        }
        ClearSelection();
    }

    private void ApplySelection()
    {
        switch (selKind)
        {
            case "spawn": Try(() => Doc.SetSpawn(selName, selSpawnKind, selA, selB)); break;
            case "gate": Try(() => Doc.SetGate(selName, new PNGridRegion(Math.Min(selA, selC), Math.Min(selB, selD), Math.Max(selA, selC), Math.Max(selB, selD)), selTo)); break;
            case "zone": Try(() => Doc.SetZone(selName, new PNGridRegion(Math.Min(selA, selC), Math.Min(selB, selD), Math.Max(selA, selC), Math.Max(selB, selD)), selBlock, selRate)); break;
            case "group": Try(() => Doc.SetGroup(selName, selA, selB, ParseGroup(selText).ToList(), selRate)); break;
            case "thing": Try(() => Doc.SetKey(selA, selB, selText.Trim())); break;
        }
    }

    private void DescribeCell(int x, int z)
    {
        if (Doc == null || !Doc.Data.InBounds(x, z)) { inspect = ""; return; }
        var d = Doc.Data; var thing = Doc.Placements.At(x, z);
        var text = "(" + x + "," + z + ")  " + d.GroundId(x, z) + (d.IsSolid(x, z) ? "  solid" : "") + "  h " + d.Height(x, z).ToString("0.##") + "  clearance " + d.Clearance(x, z);
        if (thing != null) text += "  " + thing.Definition.Id + " dir " + thing.Direction;
        foreach (var zone in Doc.Meta.Zones) if (zone.Region.Contains(x, z)) text += "  zone " + zone.Name;
        inspect = text;
    }

    // ---- 2D view ----
    public Texture2D Render()
    {
        if (Doc == null) return null;
        var d = Doc.Data;
        if (texture == null || texture.width != d.Width || texture.height != d.Depth)
        {
            if (texture != null) DestroyImmediate(texture);
            texture = new Texture2D(d.Width, d.Depth, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        }
        var colors = new Color32[d.Width * d.Depth];
        var palette = grounds.Select(g => new Color32(g.Red, g.Green, g.Blue, 255)).ToArray();
        for (int z = 0; z < d.Depth; z++) for (int x = 0; x < d.Width; x++)
        {
            int gi = Math.Min(d.GroundAt(x, z), palette.Length - 1);
            Color32 c = palette[gi];
            if (!IsVisibleGround(gi)) c = (((x / d.GroundFactor) + (z / d.GroundFactor)) & 1) == 0 ? new Color32(26, 27, 33, 255) : new Color32(19, 20, 25, 255);   // nothing: a very dark chequer
            int h = d.HeightRaw(x, z);
            if (h != 0) { int shift = Mathf.Clamp(h * 6, -60, 60); c = new Color32((byte)Mathf.Clamp(c.r + shift, 0, 255), (byte)Mathf.Clamp(c.g + shift, 0, 255), (byte)Mathf.Clamp(c.b + shift, 0, 255), 255); }
            if (d.IsSolid(x, z)) c = new Color32(104, 104, 112, 255);   // the wall grey of the game
            colors[z * d.Width + x] = c;
        }
        if (ShowThings)
            foreach (var thing in Doc.Placements.All)
            {
                var c = thing.Definition.Settings.BlocksPath ? new Color32(214, 120, 40, 255) : new Color32(240, 210, 60, 255);
                for (int z = thing.Z; z < thing.Z + thing.CellsZ; z++) for (int x = thing.X; x < thing.X + thing.CellsX; x++) colors[z * d.Width + x] = c;
            }
        texture.SetPixels32(colors); texture.Apply(false);
        viewDirty = false;
        return texture;
    }

    private Vector2 ToScreen(float cx, float cz) => new Vector2(pan.x + cx * zoom, pan.y + (Doc.Data.Depth - cz) * zoom);
    private bool ToCell(Vector2 m, out int x, out int z)
    {
        x = Mathf.FloorToInt((m.x - pan.x) / zoom); z = Doc.Data.Depth - 1 - Mathf.FloorToInt((m.y - pan.y) / zoom);
        return Doc.Data.InBounds(x, z);
    }
    private Rect RegionRect(PNGridRegion r) { var a = ToScreen(r.X0, r.Z1 + 1); return new Rect(a.x, a.y, r.Width * zoom, r.Depth * zoom); }

    // Fits the whole map into an area (zoom in steps of a quarter, never below one quarter pixel per cell) and centres it.
    public void FitView(Vector2 area)
    {
        if (Doc == null) return;
        zoom = Mathf.Clamp(Mathf.Floor(Mathf.Min((area.x - 160) / Doc.Data.Width, (area.y - 100) / Doc.Data.Depth) * 4f) / 4f, .25f, 48f);
        pan = new Vector2((area.x - Doc.Data.Width * zoom) * .5f, (area.y - Doc.Data.Depth * zoom) * .5f);
    }

    private void Focus(int x, int z) { pan = new Vector2(canvas.width, canvas.height) * .5f - new Vector2(x * zoom, (Doc.Data.Depth - z) * zoom); target = new Vector3(x * Doc.Data.CellSize, 0, z * Doc.Data.CellSize); Redraw(); }

    private static string MapFolder()
    {
        var folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Maps");
        Directory.CreateDirectory(folder); return folder;
    }

    private void OnDisable() { FreePreview(); }
    private void OnDestroy() { if (texture != null) DestroyImmediate(texture); FreePreview(); }

    // =================================================================================================================
    // UI Toolkit
    // =================================================================================================================
    private sealed class Glyph : VisualElement
    {
        public string Id; public Color Tint = new Color(.87f, .88f, .91f); public float Size;
        public Glyph(string id, float size = 20f) { Id = id; Size = size; style.width = size; style.height = size; pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
        private void Draw(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D; float s = Size / 20f; Vector2 P(float x, float y) => new Vector2(x * s, y * s);
            p.lineWidth = Mathf.Max(1.4f, 1.6f * s); p.strokeColor = Tint; p.fillColor = Tint; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            void Poly(bool fill, params float[] v) { p.BeginPath(); p.MoveTo(P(v[0], v[1])); for (int i = 2; i < v.Length; i += 2) p.LineTo(P(v[i], v[i + 1])); p.ClosePath(); if (fill) p.Fill(); else p.Stroke(); }
            void Line(params float[] v) { p.BeginPath(); p.MoveTo(P(v[0], v[1])); for (int i = 2; i < v.Length; i += 2) p.LineTo(P(v[i], v[i + 1])); p.Stroke(); }
            void Disc(float x, float y, float r, bool fill) { p.BeginPath(); p.Arc(P(x, y), r * s, 0f, 360f); if (fill) p.Fill(); else p.Stroke(); }
            switch (Id)
            {
                case "select": Poly(true, 4, 3, 4, 16, 8, 12.5f, 11, 18, 13, 17, 10, 11.5f, 15, 11.5f); break;
                case "brush": p.lineWidth = 3.4f * s; Line(6, 14, 14, 6); p.lineWidth = 1.6f * s; Disc(5, 15, 2.2f, true); break;
                case "wall": Poly(false, 3, 5, 17, 5, 17, 15, 3, 15); Line(3, 10, 17, 10); Line(10, 5, 10, 10); Line(6.5f, 10, 6.5f, 15); Line(13.5f, 10, 13.5f, 15); break;
                case "erase": Poly(false, 3, 12, 9, 4, 17, 9, 11, 17); Line(6.2f, 8, 14, 13); break;
                case "fill": Poly(false, 4, 10, 10, 4, 15, 9, 9, 15); Line(10, 4, 8, 2); Disc(16.5f, 15.5f, 2, true); break;
                case "picker": p.lineWidth = 2.6f * s; Line(6, 14, 13, 7); p.lineWidth = 1.6f * s; Disc(15, 5, 2.6f, true); Disc(5, 15, 1.2f, true); break;
                case "cube": Poly(false, 10, 3, 16, 6.5f, 16, 13.5f, 10, 17, 4, 13.5f, 4, 6.5f); Line(4, 6.5f, 10, 10, 16, 6.5f); Line(10, 10, 10, 17); break;
                case "player": p.strokeColor = new Color(.3f, .9f, .4f); p.fillColor = p.strokeColor; Disc(10, 10, 6.5f, false); Disc(10, 10, 2.4f, true); break;
                case "monster": p.strokeColor = new Color(.95f, .35f, .35f); p.fillColor = p.strokeColor; Disc(10, 10, 6.5f, false); Disc(7.6f, 9, 1.2f, true); Disc(12.4f, 9, 1.2f, true); Line(7.5f, 13, 12.5f, 13); break;
                case "gate": Line(4, 17, 4, 9); p.BeginPath(); p.Arc(P(10, 9), 6 * s, 180f, 360f); p.Stroke(); Line(16, 9, 16, 17); Line(4, 17, 16, 17); break;
                case "zone": Line(3, 7, 3, 3, 7, 3); Line(13, 3, 17, 3, 17, 7); Line(17, 13, 17, 17, 13, 17); Line(7, 17, 3, 17, 3, 13); break;
                case "group": Disc(6, 13, 2.6f, true); Disc(14, 13, 2.6f, true); Disc(10, 6, 2.6f, true); break;
                case "copy": Poly(false, 3, 6, 12, 6, 12, 16, 3, 16); Line(8, 6, 8, 3, 17, 3, 17, 13, 12, 13); break;
                case "paste": Poly(false, 5, 4, 15, 4, 15, 17, 5, 17); Poly(true, 8, 2.5f, 12, 2.5f, 12, 5.5f, 8, 5.5f); break;
                case "undo": Line(16, 15, 16, 10, 8, 10); Poly(true, 7, 10, 11, 6.5f, 11, 13.5f); break;
                case "redo": Line(4, 15, 4, 10, 12, 10); Poly(true, 13, 10, 9, 6.5f, 9, 13.5f); break;
                case "palette": Poly(true, 3, 3, 9, 3, 9, 9, 3, 9); Poly(false, 11, 3, 17, 3, 17, 9, 11, 9); Poly(false, 3, 11, 9, 11, 9, 17, 3, 17); Poly(true, 11, 11, 17, 11, 17, 17, 11, 17); break;
                case "layers": Poly(false, 10, 3, 17, 7, 10, 11, 3, 7); Line(3, 10.5f, 10, 14.5f, 17, 10.5f); Line(3, 14, 10, 18, 17, 14); break;
                case "map": Disc(10, 10, 7, false); Line(3, 10, 17, 10); Line(10, 3, 10, 17); p.BeginPath(); p.Arc(P(10, 10), 3.2f * s, 0f, 360f); p.Stroke(); break;
                case "info": Disc(10, 10, 7, false); Disc(10, 6.5f, .9f, true); Line(10, 9, 10, 14); break;
                default: Disc(10, 10, 5, false); break;
            }
        }
    }

    private static void Pad(IStyle s, float v) { s.paddingLeft = v; s.paddingRight = v; s.paddingTop = v; s.paddingBottom = v; }
    private static void Radius(IStyle s, float v) { s.borderTopLeftRadius = v; s.borderTopRightRadius = v; s.borderBottomLeftRadius = v; s.borderBottomRightRadius = v; }
    private static void Border(IStyle s, float w, Color c) { s.borderTopWidth = w; s.borderBottomWidth = w; s.borderLeftWidth = w; s.borderRightWidth = w; s.borderTopColor = c; s.borderBottomColor = c; s.borderLeftColor = c; s.borderRightColor = c; }

    private VisualElement Floating(float? left = null, float? right = null, float? top = null, float? bottom = null)
    {
        var e = new VisualElement(); var s = e.style; s.position = Position.Absolute;
        if (left.HasValue) s.left = left.Value; if (right.HasValue) s.right = right.Value; if (top.HasValue) s.top = top.Value; if (bottom.HasValue) s.bottom = bottom.Value;
        s.backgroundColor = PanelColor; Radius(s, 8); Border(s, 1, EdgeColor);
        return e;
    }

    private Label Text(string text, bool dim = false, bool bold = false)
    {
        var l = new Label(text); l.style.color = dim ? DimColor : TextColor; l.style.fontSize = 12; if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold; l.style.whiteSpace = WhiteSpace.Normal; return l;
    }

    private Button Chip(string text, Action click, bool active = false)
    {
        var b = new Button(click) { text = text }; var s = b.style; s.backgroundColor = active ? Accent : FieldColor; s.color = TextColor; Radius(s, 5); Border(s, 0, Color.clear);
        s.marginLeft = 0; s.marginRight = 4; s.marginTop = 2; s.marginBottom = 2; s.paddingLeft = 8; s.paddingRight = 8; s.paddingTop = 3; s.paddingBottom = 3; s.fontSize = 12; return b;
    }

    private void Skin(VisualElement field)
    {
        field.style.marginLeft = 0; field.style.marginRight = 0; field.style.marginTop = 2; field.style.marginBottom = 2;
        field.Query<Label>().ForEach(l => l.style.color = DimColor);
        field.Query<TextElement>(className: "unity-base-field__input").ForEach(t => t.style.color = TextColor);
        field.Query(className: "unity-base-field__input").ForEach(t => { t.style.backgroundColor = FieldColor; Radius(t.style, 4); Border(t.style, 0, Color.clear); });
    }

    private void BuildUi()
    {
        var root = rootVisualElement; root.Clear(); root.style.backgroundColor = new Color(.1f, .11f, .13f);
        root.focusable = true;
        root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        root.RegisterCallback<KeyUpEvent>(e => { if (e.keyCode == KeyCode.Space) spaceHeld = false; }, TrickleDown.TrickleDown);

        // canvases (IMGUI) under everything
        canvasHost = new VisualElement(); var ch = canvasHost.style; ch.position = Position.Absolute; ch.left = 0; ch.right = 0; ch.top = 24; ch.bottom = 0; ch.flexDirection = FlexDirection.Row; ch.backgroundColor = new Color(.13f, .14f, .16f);
        flatBox = new IMGUIContainer(DrawFlat) { focusable = true };
        solidBox = new IMGUIContainer(DrawSolid) { focusable = true };
        foreach (var box in new[] { flatBox, solidBox }) { box.style.flexGrow = 1; box.style.flexBasis = 0; box.style.height = Length.Percent(100); }
        flatBox.RegisterCallback<PointerMoveEvent>(OnPointerMoved);
        flatBox.RegisterCallback<PointerDownEvent>(e => rootVisualElement.Focus());
        canvasHost.Add(flatBox); canvasHost.Add(solidBox);
        root.Add(canvasHost);

        hint = new Label("File > New to start a map, or File > Open"); hint.style.position = Position.Absolute; hint.style.left = 0; hint.style.right = 0; hint.style.top = Length.Percent(45); hint.style.unityTextAlign = TextAnchor.MiddleCenter; hint.style.color = DimColor; hint.style.fontSize = 14; hint.pickingMode = PickingMode.Ignore;
        root.Add(hint);

        BuildMenuBar(root); BuildToolStrip(root); BuildOptionsBar(root); BuildRightPanel(root); BuildStatus(root);
        ApplyView(); root.schedule.Execute(RefreshUi).Every(120);
        RefreshUi();
    }

    private void BuildMenuBar(VisualElement root)
    {
        var bar = new VisualElement(); var s = bar.style; s.position = Position.Absolute; s.left = 0; s.right = 0; s.top = 0; s.height = 24; s.backgroundColor = new Color(.11f, .12f, .14f, 1f); s.flexDirection = FlexDirection.Row; s.alignItems = Align.Center; s.paddingLeft = 6; s.paddingRight = 8;
        s.borderBottomWidth = 1; s.borderBottomColor = EdgeColor;
        ToolbarMenu Menu(string title, Action<ToolbarMenu> fill) { var m = new ToolbarMenu { text = title }; m.style.color = TextColor; m.style.marginRight = 2; fill(m); return m; }
        bar.Add(Menu("File", m =>
        {
            m.menu.AppendAction("New map", a => { ShowTab(Tab.Map); NewMap(NewWidth, NewDepth, NewCell, Seed); });
            m.menu.AppendAction("Open...", a => { var p = EditorUtility.OpenFilePanel("Open map", MapFolder(), "json"); if (!string.IsNullOrEmpty(p)) OpenFile(p); });
            m.menu.AppendSeparator();
            m.menu.AppendAction("Save", a => { if (Doc != null) { var p = FilePath ?? EditorUtility.SaveFilePanel("Save map", MapFolder(), "map", "json"); if (!string.IsNullOrEmpty(p)) SaveFile(p); } }, a => Doc != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            m.menu.AppendAction("Save as...", a => { if (Doc != null) { var p = EditorUtility.SaveFilePanel("Save map", MapFolder(), "map", "json"); if (!string.IsNullOrEmpty(p)) SaveFile(p); } }, a => Doc != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }));
        bar.Add(Menu("Edit", m =>
        {
            m.menu.AppendAction("Undo\tCtrl+Z", a => { Doc?.Undo(); Dirty(); }, a => Doc != null && Doc.UndoCount > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            m.menu.AppendAction("Redo\tCtrl+Y", a => { Doc?.Redo(); Dirty(); }, a => Doc != null && Doc.RedoCount > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            m.menu.AppendSeparator();
            m.menu.AppendAction("Delete selection\tDel", a => DeleteSelection(), a => selKind != "" ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }));
        bar.Add(Menu("View", m =>
        {
            m.menu.AppendAction("Fit map\tF", a => fitPending = true);
            m.menu.AppendSeparator();
            m.menu.AppendAction("2D", a => ViewIndex = 0, a => view == ViewMode.Flat ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            m.menu.AppendAction("3D", a => ViewIndex = 1, a => view == ViewMode.Solid ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            m.menu.AppendAction("2D + 3D", a => ViewIndex = 2, a => view == ViewMode.Split ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            m.menu.AppendSeparator();
            m.menu.AppendAction("Grid\tH", a => { ShowGrid = !ShowGrid; Redraw(); }, a => ShowGrid ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            m.menu.AppendAction("Minimap", a => { ShowMini = !ShowMini; Redraw(); }, a => ShowMini ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
        }));
        bar.Add(Menu("Map", m =>
        {
            m.menu.AppendAction("Validate", a => { Validate(); ShowTab(Tab.Map); });
            m.menu.AppendAction("Run generator", a => RunGenerator());
        }));
        var spacer = new VisualElement(); spacer.style.flexGrow = 1; bar.Add(spacer);
        fileLabel = Text("No map", true); fileLabel.style.marginRight = 10; bar.Add(fileLabel);
        Button IconButton(Glyph g, string tip, Action click) { var b = new Button(click) { tooltip = tip }; b.Add(g); var bs = b.style; bs.width = 22; bs.height = 20; bs.backgroundColor = Color.clear; Border(bs, 0, Color.clear); Pad(bs, 0); bs.marginLeft = 1; bs.marginRight = 1; bs.alignItems = Align.Center; bs.justifyContent = Justify.Center; return b; }
        undoGlyph = new Glyph("undo", 16); redoGlyph = new Glyph("redo", 16);
        bar.Add(IconButton(undoGlyph, "Undo (Ctrl+Z)", () => { Doc?.Undo(); Dirty(); }));
        bar.Add(IconButton(redoGlyph, "Redo (Ctrl+Y)", () => { Doc?.Redo(); Dirty(); }));
        var gap = new VisualElement(); gap.style.width = 8; bar.Add(gap);
        foreach (var mode in new[] { ViewMode.Flat, ViewMode.Solid, ViewMode.Split })
        {
            var captured = mode; var b = new Button(() => ViewIndex = (int)captured) { text = mode == ViewMode.Flat ? "2D" : mode == ViewMode.Solid ? "3D" : "2D|3D", tooltip = "Tab cycles the view" };
            var bs = b.style; bs.height = 18; bs.fontSize = 11; Radius(bs, 4); Border(bs, 0, Color.clear); bs.marginLeft = 1; bs.marginRight = 1; bs.paddingLeft = 6; bs.paddingRight = 6; bs.color = TextColor;
            viewButtons[mode] = b; bar.Add(b);
        }
        gameButton = new Button(() => { GameLook = !GameLook; meshVersion++; Redraw(); RefreshUi(); }) { text = "Game look", tooltip = "3D view with the game's own shader and wall colours (the material PNGridWorldHost uses)" };
        var gs = gameButton.style; gs.height = 18; gs.fontSize = 11; Radius(gs, 4); Border(gs, 0, Color.clear); gs.marginLeft = 6; gs.marginRight = 1; gs.paddingLeft = 6; gs.paddingRight = 6; gs.color = TextColor;
        bar.Add(gameButton);
        root.Add(bar);
    }

    private void BuildToolStrip(VisualElement root)
    {
        toolStrip = Floating(left: 8, top: 34); toolStrip.style.width = 40; Pad(toolStrip.style, 3); toolStrip.style.alignItems = Align.Center;
        foreach (var (tool, label, glyph, key, tip) in Tools)
        {
            var captured = tool; var b = new Button(() => SetTool(captured)) { tooltip = label + (key == KeyCode.None ? "" : " (" + key + ")") + "\n" + tip };
            b.Add(new Glyph(glyph, 20)); var bs = b.style; bs.width = 32; bs.height = 30; bs.marginLeft = 0; bs.marginRight = 0; bs.marginTop = 1; bs.marginBottom = 1; Pad(bs, 0); Radius(bs, 6); Border(bs, 0, Color.clear); bs.alignItems = Align.Center; bs.justifyContent = Justify.Center; bs.backgroundColor = Color.clear;
            toolButtons[tool] = b; toolStrip.Add(b);
            if (tool == Tool.Picker || tool == Tool.Place || tool == Tool.Group) { var line = new VisualElement(); line.style.height = 1; line.style.width = 24; line.style.backgroundColor = EdgeColor; line.style.marginTop = 3; line.style.marginBottom = 3; toolStrip.Add(line); }
        }
        root.Add(toolStrip);
    }

    private void SetTool(Tool tool) { Current = tool; RefreshUi(); Redraw(); }

    private void BuildOptionsBar(VisualElement root)
    {
        optionsBar = Floating(left: 56, top: 34); optionsBar.style.flexDirection = FlexDirection.Row; optionsBar.style.alignItems = Align.Center; optionsBar.style.paddingLeft = 8; optionsBar.style.paddingRight = 6; optionsBar.style.height = 32; optionsBar.style.maxWidth = Length.Percent(60);
        root.Add(optionsBar);
    }

    private void RebuildOptions()
    {
        optionsBar.Clear();
        var info = Tools.FirstOrDefault(t => t.tool == Current);
        var title = Text(info.label ?? Current.ToString(), bold: true); title.style.marginRight = 10; optionsBar.Add(title);
        void Gap() { var g = new VisualElement(); g.style.width = 10; optionsBar.Add(g); }
        void BrushSlider()
        {
            optionsBar.Add(Text("Size", true)); var sl = new SliderInt(1, 16) { value = Brush }; sl.style.width = 110; sl.style.marginLeft = 6; sl.style.marginRight = 4; optionsBar.Add(sl);
            var n = Text(Brush.ToString()); n.style.width = 18; optionsBar.Add(n); sl.RegisterValueChangedCallback(e => { Brush = e.newValue; n.text = e.newValue.ToString(); Redraw(); });
        }
        void GroundSwatch()
        {
            if (grounds.Length == 0) return; var g = grounds[Mathf.Clamp(GroundIndex, 0, grounds.Length - 1)];
            var sw = new VisualElement(); sw.style.width = 18; sw.style.height = 18; sw.style.backgroundColor = (Color)new Color32(g.Red, g.Green, g.Blue, 255); Radius(sw.style, 4); Border(sw.style, 1, EdgeColor); sw.style.marginRight = 6; optionsBar.Add(sw); optionsBar.Add(Text(groundNames[Mathf.Clamp(GroundIndex, 0, groundNames.Length - 1)], true));
        }
        void Field(string label, string value, Action<string> set, float width = 90)
        {
            optionsBar.Add(Text(label, true)); var f = new TextField { value = value }; f.style.width = width; f.style.marginLeft = 6; f.style.marginRight = 8; Skin(f); f.RegisterValueChangedCallback(e => set(e.newValue)); optionsBar.Add(f);
        }
        switch (Current)
        {
            case Tool.Ground: BrushSlider(); Gap(); GroundSwatch(); break;
            case Tool.Solid: BrushSlider(); break;
            case Tool.Eraser:
                BrushSlider(); Gap();
                Chip2("Solid", () => EraseSolid, v => EraseSolid = v); Chip2("Things", () => EraseThings, v => EraseThings = v); Chip2("Markers", () => EraseMarkers, v => EraseMarkers = v); Chip2("Ground", () => EraseGround, v => EraseGround = v);
                break;
            case Tool.Fill: GroundSwatch(); break;
            case Tool.Place:
                if (things.Length > 0) optionsBar.Add(Text(thingNames[Mathf.Clamp(ThingIndex, 0, things.Length - 1)], true));
                Gap(); optionsBar.Add(Text("Direction", true)); for (int d = 0; d < 4; d++) { int dir = d; var c = Chip(dir.ToString(), () => { ThingDirection = dir; optionsKey = ""; RefreshUi(); }, ThingDirection == dir); c.style.marginLeft = 4; optionsBar.Add(c); }
                break;
            case Tool.PlayerSpawn: case Tool.MonsterSpawn: Field("Name", SpawnName, v => SpawnName = v); break;
            case Tool.Gate: Field("Name", GateName, v => GateName = v); Field("Leads to", GateTo, v => GateTo = v); break;
            case Tool.Zone:
                Field("Name", ZoneName, v => ZoneName = v);
                Chip2("Spawn block", () => ZoneSpawnBlock, v => ZoneSpawnBlock = v); optionsBar.Add(Text("Rate", true)); var rate = new Slider(0, 4) { value = (float)ZoneRate }; rate.style.width = 90; rate.style.marginLeft = 6; rate.RegisterValueChangedCallback(e => ZoneRate = e.newValue); optionsBar.Add(rate);
                break;
            case Tool.Group: Field("Name", GroupName, v => GroupName = v, 70); Field("Entities", GroupEntries, v => GroupEntries = v, 200); break;
            default: optionsBar.Add(Text(info.tip ?? "", true)); break;
        }
    }

    private void Chip2(string text, Func<bool> get, Action<bool> set)
    {
        Button b = null; b = Chip(text, () => { set(!get()); b.style.backgroundColor = get() ? Accent : FieldColor; });
        b.style.backgroundColor = get() ? Accent : FieldColor; optionsBar.Add(b);
    }

    private void BuildRightPanel(VisualElement root)
    {
        rightPanel = Floating(right: 8, top: 34, bottom: 34); rightPanel.style.flexDirection = FlexDirection.Row; rightPanel.style.overflow = Overflow.Hidden;
        // the resize edge
        panelResize = new VisualElement(); panelResize.style.width = 6; panelResize.style.flexShrink = 0; panelResize.style.backgroundColor = Color.clear;
        panelResize.RegisterCallback<PointerEnterEvent>(e => panelResize.style.backgroundColor = Accent); panelResize.RegisterCallback<PointerLeaveEvent>(e => { if (!panelResize.HasPointerCapture(0)) panelResize.style.backgroundColor = Color.clear; });
        panelResize.RegisterCallback<PointerDownEvent>(e => { panelResize.CapturePointer(e.pointerId); e.StopPropagation(); });
        panelResize.RegisterCallback<PointerMoveEvent>(e => { if (!panelResize.HasPointerCapture(e.pointerId)) return; panelWidth = Mathf.Clamp(panelWidth - e.deltaPosition.x, 240, Mathf.Max(240, Mathf.Min(620, rootVisualElement.layout.width - 300))); ApplyPanelWidth(); e.StopPropagation(); });
        panelResize.RegisterCallback<PointerUpEvent>(e => { panelResize.ReleasePointer(e.pointerId); panelResize.style.backgroundColor = Color.clear; });
        panelResize.RegisterCallback<PointerCaptureOutEvent>(e => panelResize.style.backgroundColor = Color.clear);
        tabBody = new VisualElement(); tabBody.style.flexGrow = 1; tabBody.style.flexShrink = 1; tabBody.style.minWidth = 0;
        tabStrip = new VisualElement(); tabStrip.style.width = 38; tabStrip.style.flexShrink = 0; tabStrip.style.alignItems = Align.Center; tabStrip.style.paddingTop = 4; tabStrip.style.borderLeftWidth = 1; tabStrip.style.borderLeftColor = EdgeColor;
        foreach (var (t, glyph, tip) in new[] { (Tab.Palette, "palette", "Palette: grounds and things"), (Tab.Selection, "select", "Selection: edit the selected item"), (Tab.Layers, "layers", "Layers: what the canvas shows"), (Tab.Map, "map", "Map: block, generator, stamps, check, new map") })
        {
            var captured = t; var b = new Button(() => ClickTab(captured)) { tooltip = tip }; b.Add(new Glyph(glyph, 20)); var bs = b.style; bs.width = 32; bs.height = 32; Pad(bs, 0); Radius(bs, 6); Border(bs, 0, Color.clear); bs.marginLeft = 0; bs.marginRight = 0; bs.marginTop = 1; bs.marginBottom = 1; bs.alignItems = Align.Center; bs.justifyContent = Justify.Center; bs.backgroundColor = Color.clear;
            tabButtons[t] = b; tabStrip.Add(b);
        }
        rightPanel.Add(panelResize); rightPanel.Add(tabBody); rightPanel.Add(tabStrip);
        root.Add(rightPanel); ApplyPanelWidth();
    }

    private void ApplyPanelWidth() { rightPanel.style.width = panelOpen ? panelWidth + 44 : 40; panelResize.style.display = panelOpen ? DisplayStyle.Flex : DisplayStyle.None; tabBody.style.display = panelOpen ? DisplayStyle.Flex : DisplayStyle.None; }
    private void ClickTab(Tab t) { if (t == tab && panelOpen) panelOpen = false; else { tab = t; panelOpen = true; } ApplyPanelWidth(); bodyKey = ""; RefreshUi(); }
    private void ShowTab(Tab t) { tab = t; panelOpen = true; if (rightPanel != null) ApplyPanelWidth(); bodyKey = ""; }

    private void BuildStatus(VisualElement root)
    {
        statusPill = Floating(left: 8, bottom: 6); statusPill.style.flexDirection = FlexDirection.Row; statusPill.style.alignItems = Align.Center; statusPill.style.height = 22; statusPill.style.paddingLeft = 10; statusPill.style.paddingRight = 10; Radius(statusPill.style, 11); statusPill.style.maxWidth = Length.Percent(70);
        statusLabel = Text(""); statusLabel.style.whiteSpace = WhiteSpace.NoWrap; statusLabel.style.overflow = Overflow.Hidden; statusLabel.style.marginRight = 14;
        cellLabel = Text("", true); cellLabel.style.whiteSpace = WhiteSpace.NoWrap;
        statusPill.Add(statusLabel); statusPill.Add(cellLabel); root.Add(statusPill);
    }

    private void ApplyView()
    {
        if (flatBox == null) return;
        flatBox.style.display = view == ViewMode.Solid ? DisplayStyle.None : DisplayStyle.Flex;
        solidBox.style.display = view == ViewMode.Flat ? DisplayStyle.None : DisplayStyle.Flex;
        fitPending = true; Redraw(); RefreshUi();
    }

    // Brings the widgets in line with the state; every 120 ms and after an action.
    private void RefreshUi()
    {
        if (rootVisualElement == null || toolStrip == null) return;
        foreach (var pair in toolButtons) pair.Value.style.backgroundColor = pair.Key == Current ? Accent : Color.clear;
        foreach (var pair in viewButtons) pair.Value.style.backgroundColor = pair.Key == view ? Accent : FieldColor;
        gameButton.style.backgroundColor = GameLook ? Accent : FieldColor;
        foreach (var pair in tabButtons) pair.Value.style.backgroundColor = panelOpen && pair.Key == tab ? Accent : Color.clear;
        undoGlyph.Tint = Doc != null && Doc.UndoCount > 0 ? TextColor : new Color(.4f, .41f, .45f); undoGlyph.MarkDirtyRepaint();
        redoGlyph.Tint = Doc != null && Doc.RedoCount > 0 ? TextColor : new Color(.4f, .41f, .45f); redoGlyph.MarkDirtyRepaint();
        hint.style.display = Doc == null ? DisplayStyle.Flex : DisplayStyle.None;
        fileLabel.text = Doc == null ? "No map" : (FilePath == null ? "(unsaved)" : Path.GetFileName(FilePath)) + (Doc.Dirty ? " *" : "") + "   " + Doc.Data.Width + " x " + Doc.Data.Depth + " cells of " + Doc.Data.CellSize + " m";
        statusLabel.text = Status; cellLabel.text = inspect + (Doc != null ? "   zoom " + zoom.ToString("0.##") + "x" : "");
        statusPill.style.display = string.IsNullOrEmpty(Status) && string.IsNullOrEmpty(inspect) && Doc == null ? DisplayStyle.None : DisplayStyle.Flex;
        string okey = Current + "|" + (Current == Tool.Ground || Current == Tool.Fill ? GroundIndex : 0) + "|" + (Current == Tool.Place ? ThingIndex + "," + ThingDirection : "") + "|" + (hub == null ? 0 : grounds.Length);
        if (okey != optionsKey) { optionsKey = okey; RebuildOptions(); }
        string key = tab + "|" + panelOpen + "|" + (Doc == null ? "none" : Doc.GetHashCode().ToString()) + "|" + GroundIndex + "," + ThingIndex + "|" + selKind + selName + selA + selB + selC + selD + "," + selCellX + "," + selCellZ + "|" + (Doc == null ? "" : Doc.Meta.Spawns.Count + "," + Doc.Meta.Gates.Count + "," + Doc.Meta.Zones.Count + "," + Doc.Meta.Groups.Count) + "|" + Issues.Count + "|" + Stamps.ContainsKey("clip");
        if (key != bodyKey) { bodyKey = key; if (panelOpen) RebuildBody(); }
    }

    // The colour of a thing in the palette and the one-line summary under it.
    private static Color ThingColor(PNPlaceableDefinition def)
    {
        var work = def.Settings.Work;
        if (work != null && work.Types.Count == 1) return work.Types[0] == PNWorkIds.Axe ? new Color(.62f, .42f, .22f) : work.Types[0] == PNWorkIds.Pickaxe ? new Color(.58f, .6f, .66f) : new Color(.55f, .5f, .8f);
        if (work != null) return new Color(.55f, .5f, .8f);   // takes several kinds of work
        return def.Settings.BlocksPath ? new Color(.84f, .47f, .16f) : new Color(.94f, .82f, .24f);
    }
    private static string ThingTip(PNPlaceableDefinition def)
    {
        var s = def.Settings; var text = s.Width + "x" + s.Depth + (s.BlocksPath ? ", blocks the way" : ", does not block") + (s.Breakable ? ", health " + s.Health : ", no health");
        if (s.Work != null) text += "; work " + string.Join("/", s.Work.Types.Select(x => x.Path)) + " " + s.Work.Left + (s.Work.MinPower > 0 ? " (power " + s.Work.MinPower + "+)" : "") + (s.Work.Enemy ? ", monsters too" : "");
        return text;
    }

    // ---- panel bodies ----
    private void RebuildBody()
    {
        tabBody.Clear();
        var scroll = new ScrollView(ScrollViewMode.Vertical); scroll.style.flexGrow = 1; Pad(scroll.style, 10); scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        tabBody.Add(scroll); var c = scroll.contentContainer;
        void H(string t) { var l = Text(t, bold: true); l.style.marginTop = 8; l.style.marginBottom = 4; l.style.fontSize = 13; c.Add(l); }
        if (hub == null) LoadContent();
        switch (tab)
        {
            case Tab.Palette:
                H("Ground"); var wrap = new VisualElement(); wrap.style.flexDirection = FlexDirection.Row; wrap.style.flexWrap = Wrap.Wrap; c.Add(wrap);
                for (int i = 0; i < grounds.Length; i++)
                {
                    int at = i; var g = grounds[i]; var sw = new Button(() => { GroundIndex = at; if (Current != Tool.Fill) Current = Tool.Ground; RefreshUi(); }) { tooltip = groundNames[i] + (g.Visible ? "" : " (nothing: no floor)") + (g.Walkable ? "" : " (blocks)") };
                    var s = sw.style; s.width = 34; s.height = 34; s.marginRight = 5; s.marginBottom = 5; s.marginLeft = 0; s.marginTop = 0; Pad(s, 0); s.backgroundColor = (Color)new Color32(g.Red, g.Green, g.Blue, 255); Radius(s, 6); Border(s, i == GroundIndex ? 2 : 1, i == GroundIndex ? Color.white : EdgeColor); wrap.Add(sw);
                }
                if (grounds.Length > 0) c.Add(Text(groundNames[Mathf.Clamp(GroundIndex, 0, grounds.Length - 1)], true));
                H("Things"); var tiles = new VisualElement(); tiles.style.flexDirection = FlexDirection.Row; tiles.style.flexWrap = Wrap.Wrap; c.Add(tiles);
                for (int i = 0; i < things.Length; i++)
                {
                    int at = i; var def = things[i]; var tile = new Button(() => { ThingIndex = at; Current = Tool.Place; RefreshUi(); }) { tooltip = ThingTip(def) };
                    var ts = tile.style; ts.width = 44; ts.height = 44; ts.marginRight = 5; ts.marginBottom = 5; ts.marginLeft = 0; ts.marginTop = 0; Pad(ts, 0); ts.backgroundColor = FieldColor; Radius(ts, 7); Border(ts, i == ThingIndex ? 2 : 1, i == ThingIndex ? Color.white : EdgeColor);
                    ts.alignItems = Align.Center; ts.justifyContent = Justify.Center;
                    // The preview is the footprint to scale (the biggest registered thing fills the tile), coloured by what the thing is: work type, blocks the way or not.
                    int side = Mathf.Max(1, things.Max(x => Mathf.Max(x.Settings.Width, x.Settings.Depth))); var shape = new VisualElement(); shape.style.width = 30f * def.Settings.Width / side; shape.style.height = 30f * def.Settings.Depth / side; Radius(shape.style, 4); shape.style.backgroundColor = ThingColor(def); shape.pickingMode = PickingMode.Ignore;
                    if (!def.Settings.BlocksPath) { shape.style.borderTopWidth = shape.style.borderBottomWidth = shape.style.borderLeftWidth = shape.style.borderRightWidth = 2; var dash = new Color(1f, 1f, 1f, .7f); shape.style.borderTopColor = shape.style.borderBottomColor = shape.style.borderLeftColor = shape.style.borderRightColor = dash; }
                    tile.Add(shape); tiles.Add(tile);
                }
                if (things.Length > 0) { var pick = things[Mathf.Clamp(ThingIndex, 0, things.Length - 1)]; c.Add(Text(thingNames[Mathf.Clamp(ThingIndex, 0, things.Length - 1)], true)); c.Add(Text(ThingTip(pick), true)); }
                if (things.Length == 0) c.Add(Text("No placeable things are registered.", true));
                break;
            case Tab.Selection: SelectionBody(c, H); break;
            case Tab.Layers:
                H("Show on the canvas");
                void Toggle(string label, Func<bool> get, Action<bool> set) { var t = new Toggle(label) { value = get() }; t.style.marginTop = 3; t.Query<Label>().ForEach(l => l.style.color = TextColor); t.RegisterValueChangedCallback(e => { set(e.newValue); viewDirty = true; Redraw(); }); c.Add(t); }
                Toggle("Grid (H)", () => ShowGrid, v => ShowGrid = v); Toggle("Placed things", () => ShowThings, v => ShowThings = v); Toggle("Safety radius of things", () => ShowSafety, v => ShowSafety = v);
                Toggle("Zones", () => ShowZones, v => ShowZones = v); Toggle("Gates", () => ShowGates, v => ShowGates = v); Toggle("Spawn points", () => ShowSpawns, v => ShowSpawns = v); Toggle("Monster groups", () => ShowGroups, v => ShowGroups = v); Toggle("Minimap", () => ShowMini, v => ShowMini = v);
                break;
            case Tab.Map: MapBody(c, H); break;
        }
    }

    private void SelectionBody(VisualElement c, Action<string> H)
    {
        if (selKind == "" && selCellX < 0) { c.Add(Text("Nothing selected.\nUse Select (V) and click any cell, spawn point, gate, zone, group or thing.", true)); return; }
        if (selCellX >= 0 && Doc != null && Doc.Data.InBounds(selCellX, selCellZ))
        {
            H("Cell");
            foreach (var line in CellLines(selCellX, selCellZ)) c.Add(Text(line, line.StartsWith("Cell (")));
            var d = Doc.Data; int cx = selCellX, cz = selCellZ; var crow = new VisualElement(); crow.style.flexDirection = FlexDirection.Row; crow.style.flexWrap = Wrap.Wrap; crow.style.marginTop = 6; c.Add(crow);
            if (d.IsSolid(cx, cz) && Doc.Placements.At(cx, cz) == null) crow.Add(Chip("Remove solid", () => Try(() => Doc.Edit("remove solid", new[] { new PNGridEdit(PNGridEditKind.Solid, cx, cz, 0) }))));
            else if (!d.IsSolid(cx, cz)) crow.Add(Chip("Make solid", () => Try(() => Doc.Edit("draw solid", new[] { new PNGridEdit(PNGridEditKind.Solid, cx, cz, 1) }))));
            crow.Add(Chip("Set ground to nothing", () => Try(() => Doc.Edit("erase ground", new[] { new PNGridEdit(PNGridEditKind.Ground, cx, cz, VoidIndex) }))));
            if (selKind == "") return;
        }
        H(selKind + " " + selName);
        IntegerField Int(string label, int v, Action<int> set) { var f = new IntegerField(label) { value = v }; Skin(f); f.RegisterValueChangedCallback(e => set(e.newValue)); c.Add(f); return f; }
        switch (selKind)
        {
            case "spawn":
                var kind = new DropdownField("Kind", new List<string> { PNGridMapMeta.PlayerKind, PNGridMapMeta.MonsterKind }, selSpawnKind == PNGridMapMeta.MonsterKind ? 1 : 0); Skin(kind); kind.RegisterValueChangedCallback(e => selSpawnKind = e.newValue); c.Add(kind);
                Int("X", selA, v => selA = v); Int("Z", selB, v => selB = v); break;
            case "gate": case "zone":
                Int("X from", selA, v => selA = v); Int("Z from", selB, v => selB = v); Int("X to", selC, v => selC = v); Int("Z to", selD, v => selD = v);
                if (selKind == "gate") { var to = new TextField("Leads to") { value = selTo }; Skin(to); to.RegisterValueChangedCallback(e => selTo = e.newValue); c.Add(to); }
                else
                {
                    var block = new Toggle("Spawn block") { value = selBlock }; block.Query<Label>().ForEach(l => l.style.color = TextColor); block.RegisterValueChangedCallback(e => selBlock = e.newValue); c.Add(block);
                    var rate = new Slider("Rate scale", 0, 4) { value = (float)selRate, showInputField = true }; Skin(rate); rate.RegisterValueChangedCallback(e => selRate = e.newValue); c.Add(rate);
                }
                break;
            case "group":
                Int("X", selA, v => selA = v); Int("Z", selB, v => selB = v);
                var radius = new Slider("Radius (m)", .5f, 50) { value = (float)selRate, showInputField = true }; Skin(radius); radius.RegisterValueChangedCallback(e => selRate = e.newValue); c.Add(radius);
                var ent = new TextField("Entities") { value = selText }; Skin(ent); ent.RegisterValueChangedCallback(e => selText = e.newValue); c.Add(ent); c.Add(Text("e.g. project_n:slime*3, project_n:bat*2", true)); break;
            case "thing":
                c.Add(Text("at (" + selA + ", " + selB + ")   direction " + selC + "   health " + selD, true));
                if (ThingHasInteractable)
                {
                    var key = new TextField("Key") { value = selText }; Skin(key); key.RegisterValueChangedCallback(e => selText = e.newValue); c.Add(key);
                    c.Add(Text("The name the game finds this thing by (a-z, 0-9, _ . -). Empty = placed_<id>.", true));
                }
                break;
        }
        var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginTop = 8; c.Add(row);
        var apply = Chip("Apply", () => ApplySelection()); apply.SetEnabled(selKind != "thing" || ThingHasInteractable); row.Add(apply);
        row.Add(Chip("Delete (Del)", DeleteSelection));
    }

    private void MapBody(VisualElement c, Action<string> H)
    {
        if (Doc != null)
        {
            H("Map block"); var meta = Doc.Meta;
            if (meta.IsEmpty) c.Add(Text("Empty. Use the spawn, gate, zone and group tools.", true));
            void Row(string text, int x, int z) { var b = new Button(() => { Current = Tool.Inspect; Select(x, z); Focus(x, z); }) { text = text }; var s = b.style; s.unityTextAlign = TextAnchor.MiddleLeft; s.backgroundColor = FieldColor; s.color = TextColor; Radius(s, 5); Border(s, 0, Color.clear); s.marginLeft = 0; s.marginRight = 0; s.marginTop = 1; s.marginBottom = 1; s.fontSize = 12; c.Add(b); }
            foreach (var s in meta.Spawns) Row("spawn " + s.Name + " (" + s.Kind + ") " + s.X + "," + s.Z, s.X, s.Z);
            foreach (var g in meta.Gates) Row("gate " + g.Name + " -> " + g.To, g.Region.X0, g.Region.Z0);
            foreach (var z in meta.Zones) Row("zone " + z.Name + (z.SpawnBlock ? " (block)" : " x" + z.RateScale), z.Region.X0, z.Region.Z0);
            foreach (var g in meta.Groups) Row("group " + g.Name + " " + g.Entries.Sum(e => e.Count) + " monsters", g.X, g.Z);
            H("Generator");
            var seed = new LongField("Seed") { value = Seed }; Skin(seed); seed.RegisterValueChangedCallback(e => Seed = e.newValue); c.Add(seed);
            var chain = new TextField { multiline = true, value = Chain }; chain.style.minHeight = 90; Skin(chain); chain.RegisterValueChangedCallback(e => Chain = e.newValue); c.Add(chain);
            var run = Chip("Run generator", () => RunGenerator()); run.style.marginTop = 4; c.Add(run);
            H("Stamps");
            var sr = new VisualElement(); sr.style.flexDirection = FlexDirection.Row; c.Add(sr);
            var save = Chip("Save stamp", () => { var p = EditorUtility.SaveFilePanel("Save stamp", MapFolder(), "stamp", "json"); if (!string.IsNullOrEmpty(p)) File.WriteAllText(p, Stamps["clip"].ToJson().ToString()); }); save.SetEnabled(Stamps.ContainsKey("clip")); sr.Add(save);
            sr.Add(Chip("Load stamp", () => { var p = EditorUtility.OpenFilePanel("Load stamp", MapFolder(), "json"); if (!string.IsNullOrEmpty(p)) Try(() => { Stamps["clip"] = PNGridStamp.Parse(JObject.Parse(File.ReadAllText(p))); Status = "Stamp loaded as \"clip\"."; }); }));
            H("Check"); c.Add(Chip("Validate", Validate));
            foreach (var issue in Issues)
            {
                var iss = issue; var b = new Button(() => { if (iss.X >= 0) Focus(iss.X, iss.Z); }) { text = (iss.Level == PNMapIssueLevel.Error ? "[error] " : "[warn] ") + iss.Message }; var s = b.style; s.whiteSpace = WhiteSpace.Normal; s.unityTextAlign = TextAnchor.MiddleLeft; s.backgroundColor = Color.clear; s.color = iss.Level == PNMapIssueLevel.Error ? new Color(1f, .5f, .5f) : TextColor; Border(s, 0, Color.clear); s.fontSize = 11; s.marginLeft = 0; s.marginRight = 0; c.Add(b);
            }
        }
        H("New map");
        var w = new IntegerField("Width") { value = NewWidth }; Skin(w); w.RegisterValueChangedCallback(e => NewWidth = e.newValue); c.Add(w);
        var d = new IntegerField("Depth") { value = NewDepth }; Skin(d); d.RegisterValueChangedCallback(e => NewDepth = e.newValue); c.Add(d);
        var cell = new FloatField("Cell (m)") { value = NewCell }; Skin(cell); cell.RegisterValueChangedCallback(e => NewCell = e.newValue); c.Add(cell);
        var sd = new LongField("Seed") { value = Seed }; Skin(sd); sd.RegisterValueChangedCallback(e => Seed = e.newValue); c.Add(sd);
        var create = Chip("Create", () => NewMap(NewWidth, NewDepth, NewCell, Seed)); create.style.marginTop = 4; c.Add(create);
    }

    // ---- hotkeys ----
    private void OnKeyDown(KeyDownEvent e)
    {
        if (Doc == null || e.target is TextElement || e.target is TextField) return;
        bool ctrl = e.ctrlKey || e.commandKey;
        if (ctrl && e.keyCode == KeyCode.Z) { Doc.Undo(); Dirty(); e.StopPropagation(); }
        else if (ctrl && e.keyCode == KeyCode.Y) { Doc.Redo(); Dirty(); e.StopPropagation(); }
        else if (ctrl) return;
        else if (e.keyCode == KeyCode.Space) { spaceHeld = true; e.StopPropagation(); }
        else if (e.keyCode == KeyCode.LeftBracket) { Brush = Mathf.Max(1, Brush - 1); optionsKey = ""; e.StopPropagation(); }
        else if (e.keyCode == KeyCode.RightBracket) { Brush = Mathf.Min(16, Brush + 1); optionsKey = ""; e.StopPropagation(); }
        else if (e.keyCode == KeyCode.H) { ShowGrid = !ShowGrid; Redraw(); e.StopPropagation(); }
        else if (e.keyCode == KeyCode.F) { fitPending = true; Redraw(); e.StopPropagation(); }
        else if (e.keyCode == KeyCode.R) { ThingDirection = (ThingDirection + 1) & 3; optionsKey = ""; e.StopPropagation(); }
        else if (e.keyCode == KeyCode.Tab) { ViewIndex = ((int)view + 1) % 3; e.StopPropagation(); }
        else if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) { DeleteSelection(); e.StopPropagation(); }
        else foreach (var t in Tools) if (t.key != KeyCode.None && t.key == e.keyCode) { SetTool(t.tool); e.StopPropagation(); break; }
        RefreshUi();
    }

    private void OnPointerMoved(PointerMoveEvent e)
    {
        if (Doc == null) return;
        bool has = ToCell((Vector2)e.localPosition, out int x, out int z);
        hoverX = has ? x : -1; hoverZ = has ? z : -1; if (has) DescribeCell(x, z); else inspect = "";
        Redraw();
    }

    private void CreateGUI() { BuildUi(); }

    // =================================================================================================================
    // canvas (IMGUI under the overlays)
    // =================================================================================================================
    private void DrawFlat()
    {
        if (Doc == null) return;
        var size = flatBox.contentRect.size; if (size.x < 10 || size.y < 10) return;
        FlatCanvas(new Rect(0, 0, size.x, size.y));
    }
    private void DrawSolid()
    {
        if (Doc == null) return;
        var size = solidBox.contentRect.size; if (size.x < 10 || size.y < 10) return;
        SolidView(new Rect(0, 0, size.x, size.y));
    }

    private void FlatCanvas(Rect area)
    {
        canvas = area;
        if (fitPending && Event.current.type == EventType.Repaint) { FitView(area.size); fitPending = false; }
        if (viewDirty || texture == null) Render();
        GUI.BeginGroup(area);
        var d = Doc.Data;
        GUI.DrawTexture(new Rect(pan.x, pan.y, d.Width * zoom, d.Depth * zoom), texture, ScaleMode.StretchToFill, false);
        if (ShowGrid && zoom >= 8) DrawGrid(area.size);
        UnityEditor.Handles.DrawSolidRectangleWithOutline(new Rect(pan.x, pan.y, d.Width * zoom, d.Depth * zoom), Color.clear, new Color(1, 1, 1, .5f));
        if (ShowZones) foreach (var zone in Doc.Meta.Zones) { var r = RegionRect(zone.Region); EditorGUI.DrawRect(r, zone.SpawnBlock ? new Color(.9f, .2f, .2f, .22f) : new Color(.2f, .6f, .9f, .22f)); if (zoom >= 3) GUI.Label(r, zone.Name, EditorStyles.whiteMiniLabel); }
        if (ShowGates) foreach (var gate in Doc.Meta.Gates) { var r = RegionRect(gate.Region); EditorGUI.DrawRect(r, new Color(.3f, .5f, 1f, .6f)); if (zoom >= 3) GUI.Label(r, gate.Name, EditorStyles.whiteMiniLabel); }
        if (ShowGroups) foreach (var g in Doc.Meta.Groups) { var p = ToScreen(g.X + .5f, g.Z + .5f); EditorGUI.DrawRect(new Rect(p.x - 4, p.y - 4, 8, 8), new Color(.9f, .3f, .9f)); }
        if (ShowSpawns) foreach (var s in Doc.Meta.Spawns) { var p = ToScreen(s.X + .5f, s.Z + .5f); EditorGUI.DrawRect(new Rect(p.x - 5, p.y - 5, 10, 10), s.Kind == PNGridMapMeta.PlayerKind ? new Color(.2f, .9f, .3f) : new Color(.95f, .25f, .25f)); }
        if (ShowSafety)
            foreach (var thing in Doc.Placements.All)
            {
                var s = thing.Definition.Settings; if (s.SafetyRadius <= 0) continue;
                var c = ToScreen(thing.X + thing.CellsX * .5f, thing.Z + thing.CellsZ * .5f);
                UnityEditor.Handles.color = s.SpawnBlock ? new Color(.95f, .3f, .3f, .8f) : new Color(.3f, .8f, .95f, .8f);
                UnityEditor.Handles.DrawWireDisc(c, Vector3.forward, (float)s.SafetyRadius / d.CellSize * zoom);
            }
        if (selKind != "" || selCellX >= 0) DrawSelection();
        foreach (var cell in stroke) { int x = (int)(cell & 0xffffffff), z = (int)(cell >> 32); EditorGUI.DrawRect(RegionRect(new PNGridRegion(x, z, x, z)), new Color(1, 1, 1, .35f)); }
        if (dragging && (Current == Tool.Gate || Current == Tool.Zone || Current == Tool.Copy) && hoverX >= 0)
            EditorGUI.DrawRect(RegionRect(new PNGridRegion(Math.Min(dragX, hoverX), Math.Min(dragZ, hoverZ), Math.Max(dragX, hoverX), Math.Max(dragZ, hoverZ))), new Color(1, 1, 1, .25f));
        if (Current != Tool.Inspect && hoverX >= 0 && !dragging) EditorGUI.DrawRect(RegionRect(BrushRegion(hoverX, hoverZ)), new Color(1, 1, 1, .2f));
        if (ShowMini) Minimap(area.size);
        Handle(area);
        GUI.EndGroup();
    }

    private void DrawSelection()
    {
        Rect r;
        switch (selKind)
        {
            case "spawn": case "group": { var p = ToScreen(selA + .5f, selB + .5f); r = new Rect(p.x - 9, p.y - 9, 18, 18); break; }
            case "thing": { var t = Doc.Placements.At(selA, selB); if (t == null) return; r = RegionRect(new PNGridRegion(t.X, t.Z, t.X + t.CellsX - 1, t.Z + t.CellsZ - 1)); break; }
            case "": { if (selCellX < 0) return; r = RegionRect(new PNGridRegion(selCellX, selCellZ, selCellX, selCellZ)); break; }
            default: r = RegionRect(new PNGridRegion(Math.Min(selA, selC), Math.Min(selB, selD), Math.Max(selA, selC), Math.Max(selB, selD))); break;
        }
        UnityEditor.Handles.DrawSolidRectangleWithOutline(r, Color.clear, Color.white);
    }

    private void DrawGrid(Vector2 size)
    {
        var d = Doc.Data; UnityEditor.Handles.color = new Color(0, 0, 0, .25f);
        int x0 = Mathf.Max(0, Mathf.FloorToInt(-pan.x / zoom)), x1 = Mathf.Min(d.Width, Mathf.CeilToInt((size.x - pan.x) / zoom));
        int z0 = Mathf.Max(0, d.Depth - Mathf.CeilToInt((size.y - pan.y) / zoom)), z1 = Mathf.Min(d.Depth, d.Depth - Mathf.FloorToInt(-pan.y / zoom));
        for (int x = x0; x <= x1; x++) { float sx = pan.x + x * zoom; UnityEditor.Handles.DrawLine(new Vector3(sx, Mathf.Max(0, pan.y), 0), new Vector3(sx, Mathf.Min(size.y, pan.y + d.Depth * zoom), 0)); }
        for (int z = z0; z <= z1; z++) { float sy = pan.y + (d.Depth - z) * zoom; UnityEditor.Handles.DrawLine(new Vector3(Mathf.Max(0, pan.x), sy, 0), new Vector3(Mathf.Min(size.x, pan.x + d.Width * zoom), sy, 0)); }
    }

    // The minimap sits at the bottom, right of the tool strip and above the status pill.
    private void Minimap(Vector2 size)
    {
        var d = Doc.Data; float scale = Mathf.Min(MiniSize / d.Width, MiniSize / d.Depth);
        var box = new Rect(58, size.y - d.Depth * scale - 40, d.Width * scale, d.Depth * scale);
        if (box.width < 40) return;
        EditorGUI.DrawRect(new Rect(box.x - 2, box.y - 2, box.width + 4, box.height + 4), new Color(0, 0, 0, .6f));
        GUI.DrawTexture(box, texture, ScaleMode.StretchToFill, false);
        var viewRect = new Rect(box.x + (-pan.x / zoom) * scale, box.y + (-pan.y / zoom) * scale, size.x / zoom * scale, size.y / zoom * scale);
        var clipped = Rect.MinMaxRect(Mathf.Max(box.xMin, viewRect.xMin), Mathf.Max(box.yMin, viewRect.yMin), Mathf.Min(box.xMax, viewRect.xMax), Mathf.Min(box.yMax, viewRect.yMax));
        if (clipped.width > 0 && clipped.height > 0) UnityEditor.Handles.DrawSolidRectangleWithOutline(clipped, new Color(1, 1, 1, .08f), Color.white);
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && box.Contains(e.mousePosition)) { miniDrag = true; e.Use(); }
        if (miniDrag && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
        {
            float cx = (e.mousePosition.x - box.x) / scale, cy = (e.mousePosition.y - box.y) / scale;
            pan = new Vector2(size.x * .5f - cx * zoom, size.y * .5f - cy * zoom); e.Use(); Redraw();
        }
        if (e.type == EventType.MouseUp) miniDrag = false;
    }
    private const float MiniSize = 150f;

    // The cells the brush will change under the cursor. Ground is kept on coarser cells, so a ground brush shows the whole ground cells it will paint.
    private PNGridRegion BrushRegion(int x, int z)
    {
        if (!IsBrushTool(Current)) return new PNGridRegion(x, z, x, z);
        var r = Square(x, z);
        if (Current == Tool.Ground || (Current == Tool.Eraser && EraseGround))
        {
            int f = Doc.Data.GroundFactor;
            r = new PNGridRegion(r.X0 / f * f, r.Z0 / f * f, (r.X1 / f + 1) * f - 1, (r.Z1 / f + 1) * f - 1);
        }
        return r;
    }

    private void Handle(Rect area)
    {
        var e = Event.current; var m = e.mousePosition;
        bool inside = new Rect(0, 0, area.width, area.height).Contains(m);
        if (miniDrag) return;
        if (e.type == EventType.ScrollWheel && inside)
        {
            float before = zoom; zoom = Mathf.Clamp(zoom * (e.delta.y > 0 ? .8f : 1.25f), .25f, 48f);
            pan = m - (m - pan) * (zoom / before); e.Use(); Redraw(); return;
        }
        if (e.type == EventType.MouseDrag && (e.button == 1 || e.button == 2 || e.alt || (spaceHeld && e.button == 0))) { pan += e.delta; e.Use(); Redraw(); return; }
        bool has = ToCell(m, out int x, out int z);
        if (e.type == EventType.MouseDrag) { hoverX = has ? x : -1; hoverZ = has ? z : -1; if (has) DescribeCell(x, z); Redraw(); }
        if (!inside || e.button != 0 || e.alt || spaceHeld) return;
        bool brush = IsBrushTool(Current), rect = Current == Tool.Gate || Current == Tool.Zone || Current == Tool.Copy;
        if (e.type == EventType.MouseDown && has)
        {
            dragging = true; dragX = x; dragZ = z; hoverX = x; hoverZ = z;
            if (brush) AddStroke(x, z);
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && dragging && has) { if (brush) AddStroke(x, z); e.Use(); }
        else if (e.type == EventType.MouseUp && dragging)
        {
            dragging = false;
            if (brush) Commit(); else if (has) Use(rect ? dragX : x, rect ? dragZ : z, x, z);
            e.Use(); Redraw();
        }
    }

    private void AddStroke(int x, int z)
    {
        var r = Square(x, z);
        for (int cz = r.Z0; cz <= r.Z1; cz++) for (int cx = r.X0; cx <= r.X1; cx++) stroke.Add(((long)cz << 32) | (uint)cx);
    }

    // ---- 3D view (looks only) ----
    private const int Window = 192;   // cells on a side that are built into the 3D mesh, around the canvas centre

    private void FreePreview()
    {
        if (preview != null) { preview.Cleanup(); preview = null; }
        if (mesh != null) { DestroyImmediate(mesh); mesh = null; }
        if (material != null) { DestroyImmediate(material); material = null; }
        if (gameMaterial != null) { DestroyImmediate(gameMaterial); gameMaterial = null; }
        builtVersion = -1;
    }

    private void SolidView(Rect area)
    {
        if (Doc == null) return;
        var e = Event.current; bool inside = area.Contains(e.mousePosition);
        if (inside)
        {
            if (e.type == EventType.ScrollWheel) { distance = Mathf.Clamp(distance * (e.delta.y > 0 ? 1.15f : .87f), 3, 600); e.Use(); Redraw(); }
            else if (e.type == EventType.MouseDrag && (e.button == 1 || (e.button == 0 && e.alt))) { yaw += e.delta.x * .4f; pitch = Mathf.Clamp(pitch + e.delta.y * .4f, 8, 89); e.Use(); Redraw(); }
            else if (e.type == EventType.MouseDrag && e.button == 2)
            {
                var flat = Quaternion.Euler(0, yaw, 0); float k = distance * .0016f;
                target += flat * new Vector3(-e.delta.x * k, 0, e.delta.y * k); e.Use(); Redraw();
            }
        }
        if (Event.current.type != EventType.Repaint) return;
        if (preview == null)
        {
            preview = new PreviewRenderUtility(); preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = new Color(.13f, .14f, .16f);
            preview.camera.nearClipPlane = .1f; preview.camera.farClipPlane = 2000; preview.camera.fieldOfView = 45f;
            material = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_Cull", 0); material.SetInt("_ZWrite", 1);
            mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
            target = new Vector3(Doc.Data.Width * .5f * Doc.Data.CellSize, 0, Doc.Data.Depth * .5f * Doc.Data.CellSize); distance = Mathf.Max(20, Mathf.Max(Doc.Data.Width, Doc.Data.Depth) * Doc.Data.CellSize * 1.5f);
        }
        int cx = Mathf.Clamp(Mathf.RoundToInt(target.x / Doc.Data.CellSize), 0, Doc.Data.Width - 1), cz = Mathf.Clamp(Mathf.RoundToInt(target.z / Doc.Data.CellSize), 0, Doc.Data.Depth - 1);
        if (builtVersion != meshVersion || Mathf.Abs(cx - builtCx) > Window / 6 || Mathf.Abs(cz - builtCz) > Window / 6) { BuildMesh(cx, cz); builtVersion = meshVersion; builtCx = cx; builtCz = cz; }
        preview.BeginPreview(area, GUIStyle.none);
        var cam = preview.camera; var rot = Quaternion.Euler(pitch, yaw, 0);
        cam.transform.rotation = rot; cam.transform.position = target - rot * Vector3.forward * distance;
        if (GameLook && gameMaterial == null)
        {
            // The same shader choice as PNGridWorldHost.Create.
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader != null) gameMaterial = new Material(shader) { name = "PN Grid World (game look)", hideFlags = HideFlags.HideAndDontSave };
        }
        preview.DrawMesh(mesh, Matrix4x4.identity, GameLook && gameMaterial != null ? gameMaterial : material, 0);
        cam.Render();
        GUI.DrawTexture(area, preview.EndPreview(), ScaleMode.StretchToFill, false);
        GUI.Label(new Rect(area.x + 60, area.yMax - 60, area.width - 120, 18), "3D (view only)   right-drag: orbit   middle-drag: pan   wheel: zoom", EditorStyles.whiteMiniLabel);
    }

    private void BuildMesh(int centerX, int centerZ)
    {
        var d = Doc.Data; float cell = d.CellSize;
        int x0 = Mathf.Max(0, centerX - Window / 2), z0 = Mathf.Max(0, centerZ - Window / 2), x1 = Mathf.Min(d.Width, x0 + Window), z1 = Mathf.Min(d.Depth, z0 + Window);
        var vertices = new List<Vector3>(); var colors = new List<Color32>(); var triangles = new List<int>();
        var palette = grounds.Select(g => new Color32(g.Red, g.Green, g.Blue, 255)).ToArray();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 e, Color32 color)
        {
            // The tool's own material expects linear colours; the game material (Sprites/Default) takes the colours as the host gives them.
            if (!GameLook) color = (Color32)((Color)color).linear;
            int i = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(e); colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
            triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2); triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 3);
        }
        Color32 Shade(Color32 c, float f) => new Color32((byte)(c.r * f), (byte)(c.g * f), (byte)(c.b * f), 255);
        void Box(float x, float z, float w, float dpt, float y0, float y1, Color32 top)
        {
            var a = new Vector3(x, y1, z); var b = new Vector3(x + w, y1, z); var c = new Vector3(x + w, y1, z + dpt); var e = new Vector3(x, y1, z + dpt);
            Quad(a, e, c, b, top);
            var side = Shade(top, .75f); var side2 = Shade(top, .6f);
            Quad(new Vector3(x, y0, z), new Vector3(x, y1, z), b, new Vector3(x + w, y0, z), side);
            Quad(new Vector3(x + w, y0, z + dpt), c, e, new Vector3(x, y0, z + dpt), side);
            Quad(new Vector3(x, y0, z + dpt), e, a, new Vector3(x, y0, z), side2);
            Quad(new Vector3(x + w, y0, z), b, c, new Vector3(x + w, y0, z + dpt), side2);
        }
        for (int z = z0; z < z1; z++) for (int x = x0; x < x1; x++)
        {
            var c = palette[Math.Min(d.GroundAt(x, z), palette.Length - 1)];
            float h = d.Height(x, z);
            if (d.IsSolid(x, z)) Box(x * cell, z * cell, cell, cell, h, h + PNGridWorldLimits.WallHeight, GameLook ? new Color32(104, 104, 112, 255) : new Color32(78, 82, 96, 255));
            else if (IsVisibleGround(Math.Min(d.GroundAt(x, z), palette.Length - 1))) Quad(new Vector3(x * cell, h, z * cell), new Vector3(x * cell, h, (z + 1) * cell), new Vector3((x + 1) * cell, h, (z + 1) * cell), new Vector3((x + 1) * cell, h, z * cell), c);
        }
        foreach (var thing in Doc.Placements.All)
        {
            if (thing.X + thing.CellsX < x0 || thing.X >= x1 || thing.Z + thing.CellsZ < z0 || thing.Z >= z1) continue;
            float h = d.Height(Mathf.Clamp(thing.X, 0, d.Width - 1), Mathf.Clamp(thing.Z, 0, d.Depth - 1));
            var color = thing.Definition.Settings.BlocksPath ? new Color32(214, 120, 40, 255) : new Color32(240, 210, 60, 255);
            Box(thing.X * cell, thing.Z * cell, thing.CellsX * cell, thing.CellsZ * cell, h, h + (thing.Definition.Settings.BlocksPath ? PNGridWorldLimits.WallHeight * .9f : .6f), color);
        }
        foreach (var s in Doc.Meta.Spawns)
        {
            if (s.X < x0 || s.X >= x1 || s.Z < z0 || s.Z >= z1) continue;
            float h = d.Height(s.X, s.Z);
            Box((s.X + .1f) * cell, (s.Z + .1f) * cell, cell * .8f, cell * .8f, h, h + 1.6f, s.Kind == PNGridMapMeta.PlayerKind ? new Color32(50, 230, 80, 255) : new Color32(240, 60, 60, 255));
        }
        mesh.Clear(); mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
    }
}
#endif
