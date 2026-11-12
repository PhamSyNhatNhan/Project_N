#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// EN: The map tool's Editor face (Docs/SandboxPlan.md, step G9; decision of 2026-11-12: the Editor window first, the in-game face after it has been used). It is only a view and a set of buttons on `PNGridMapDoc`
//     (Assets/Core/GridWorld/Tools), so everything it does goes through the same gateway as a running world and is one undoable step. Nothing is put in a scene, so no authored scene is touched.
//     Layout (the usual map-editor one): a top bar (file, undo/redo, 2D / 3D / split view, fit, validate), a column of tools on the left with hotkeys, the canvas in the middle (zoom, pan, grid, minimap, cell under the
//     cursor), a resizable side panel on the right (tool options with a palette of grounds and things, layers to show or hide, the inspector of the selected thing, the map block, generator, stamps, checks) and a status bar.
//     The 3D view only looks: a mesh of a window of cells around the canvas centre (ground colours and heights, walls, placed things) drawn with a PreviewRenderUtility. Menu: Tools/Project N/Grid Map Editor.
//     Hotkeys (cursor over the window, not typing): V inspect, B ground, W wall, E erase wall, T place, X remove thing, R rotate, [ ] brush, G grid, F fit, Tab view mode, Del delete selection, Ctrl+Z/Y.
// VI: Mặt Editor của công cụ dựng map (G9), bố cục thường gặp: thanh trên, cột công cụ trái có phím tắt, canvas giữa (zoom, kéo, lưới, minimap, tọa độ ô), panel phải kéo đổi rộng được (tùy chọn công cụ kèm bảng chọn đất/vật,
//     layer bật tắt, inspector, khối map, bộ sinh, stamp, kiểm tra) và thanh trạng thái. Khung 3D chỉ để xem. Mọi thao tác qua `PNGridMapDoc`, một bước hoàn tác được; không đặt gì vào scene.
public sealed class PNGridMapEditorWindow : EditorWindow
{
    public enum Tool { Inspect, Ground, Wall, EraseWall, Place, RemoveThing, PlayerSpawn, MonsterSpawn, Gate, Zone, Group, Copy, Paste }
    private enum ViewMode { Flat, Solid, Split }

    private static readonly (Tool tool, string label, KeyCode key, string tip)[] Tools =
    {
        (Tool.Inspect, "Select", KeyCode.V, "Select a spawn, gate, zone, group or thing and edit it in the panel (V)"),
        (Tool.Ground, "Ground", KeyCode.B, "Paint the chosen ground (B)"),
        (Tool.Wall, "Wall", KeyCode.W, "Draw solid cells (W)"),
        (Tool.EraseWall, "Erase", KeyCode.E, "Clear solid cells (E)"),
        (Tool.Place, "Thing", KeyCode.T, "Place the chosen thing (T, R rotates)"),
        (Tool.RemoveThing, "Del thing", KeyCode.X, "Remove the thing under the cursor (X)"),
        (Tool.PlayerSpawn, "Player", KeyCode.None, "Put a player spawn point"),
        (Tool.MonsterSpawn, "Monster", KeyCode.None, "Put a monster spawn point"),
        (Tool.Gate, "Gate", KeyCode.None, "Drag a rectangle for a gate"),
        (Tool.Zone, "Zone", KeyCode.None, "Drag a rectangle for a zone"),
        (Tool.Group, "Group", KeyCode.None, "Put a fixed group of monsters"),
        (Tool.Copy, "Copy", KeyCode.None, "Drag a rectangle to copy it as the stamp"),
        (Tool.Paste, "Paste", KeyCode.None, "Paste the stamp at the cursor"),
    };

    private const float ToolColumn = 78f, MinPanel = 200f, MaxPanel = 560f, MiniSize = 150f;
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

    // layers
    public bool ShowGrid = true, ShowZones = true, ShowGates = true, ShowSpawns = true, ShowGroups = true, ShowThings = true, ShowSafety = true, ShowMini = true;

    private Texture2D texture;
    private bool viewDirty = true, fitPending = true;
    private int meshVersion, builtVersion = -1, builtCx = int.MinValue, builtCz = int.MinValue;
    private Vector2 pan = new Vector2(10, 10), panelScroll, issueScroll;
    private float zoom = 4f, panelWidth = 300f;
    private ViewMode view = ViewMode.Flat;
    private readonly HashSet<long> stroke = new HashSet<long>();
    private float bodyWidth = 1000; private Rect lastBody;
    private bool dragging, splitterDrag, miniDrag, spaceHeld; private int dragX, dragZ, hoverX = -1, hoverZ = -1;
    private string inspect = "";
    // selection (the inspector)
    private string selKind = "", selName = ""; private int selA, selB, selC, selD; private bool selBlock; private double selRate = 1; private string selText = "", selTo = "", selSpawnKind = "";

    // 3D
    private PreviewRenderUtility preview; private Mesh mesh; private Material material;
    private float yaw = 35f, pitch = 50f, distance = 40f; private Vector3 target;

    [MenuItem("Tools/Project N/Grid Map Editor")]
    public static PNGridMapEditorWindow Open()
    {
        var window = GetWindow<PNGridMapEditorWindow>("Grid Map Editor");
        window.minSize = new Vector2(760, 480);
        return window;
    }

    // 0 = 2D, 1 = 3D, 2 = both side by side (also for scripted checks).
    public int ViewIndex { get => (int)view; set { view = (ViewMode)Mathf.Clamp(value, 0, 2); Repaint(); } }

    private void Dirty() { viewDirty = true; meshVersion++; }

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

    private void Commit()
    {
        if (Doc == null || stroke.Count == 0) { stroke.Clear(); return; }
        var cells = stroke.Select(k => ((int)(k & 0xffffffff), (int)(k >> 32))).ToList(); stroke.Clear();
        var edits = new List<PNGridEdit>();
        foreach (var (x, z) in cells)
        {
            if (Current == Tool.Ground) edits.Add(new PNGridEdit(PNGridEditKind.Ground, x, z, GroundIndex));
            else if (Doc.Placements.At(x, z) == null) edits.Add(new PNGridEdit(PNGridEditKind.Solid, x, z, Current == Tool.Wall ? 1 : 0));
        }
        Try(() => Doc.Edit(Current == Tool.Ground ? "paint ground" : Current == Tool.Wall ? "draw wall" : "erase wall", edits));
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
            case Tool.Place: if (things.Length > 0) Try(() => Doc.Place(thingNames[ThingIndex], x1, z1, ThingDirection)); break;
            case Tool.RemoveThing: Try(() => { if (!Doc.RemoveAt(x1, z1)) Status = "Nothing to remove there."; }); break;
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
    private void ClearSelection() { selKind = ""; selName = ""; }
    private void Select(int x, int z)
    {
        ClearSelection();
        var meta = Doc.Meta;
        var spawn = meta.Spawns.FirstOrDefault(s => Math.Abs(s.X - x) <= 1 && Math.Abs(s.Z - z) <= 1);
        if (spawn != null) { selKind = "spawn"; selName = spawn.Name; selA = spawn.X; selB = spawn.Z; selSpawnKind = spawn.Kind; return; }
        var group = meta.Groups.FirstOrDefault(g => Math.Abs(g.X - x) <= 1 && Math.Abs(g.Z - z) <= 1);
        if (group != null) { selKind = "group"; selName = group.Name; selA = group.X; selB = group.Z; selText = string.Join(", ", group.Entries.Select(e => e.Entity + "*" + e.Count)); return; }
        var thing = Doc.Placements.At(x, z);
        if (thing != null) { selKind = "thing"; selName = thing.Definition.Id.ToString(); selA = thing.X; selB = thing.Z; selC = thing.Direction; selD = thing.Health; return; }
        var gate = meta.Gates.FirstOrDefault(g => g.Region.Contains(x, z));
        if (gate != null) { selKind = "gate"; selName = gate.Name; selA = gate.Region.X0; selB = gate.Region.Z0; selC = gate.Region.X1; selD = gate.Region.Z1; selTo = gate.To ?? ""; return; }
        var zone = meta.Zones.FirstOrDefault(zn => zn.Region.Contains(x, z));
        if (zone != null) { selKind = "zone"; selName = zone.Name; selA = zone.Region.X0; selB = zone.Region.Z0; selC = zone.Region.X1; selD = zone.Region.Z1; selBlock = zone.SpawnBlock; selRate = zone.RateScale; }
    }

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
            case "group": Try(() => Doc.SetGroup(selName, selA, selB, ParseGroup(selText).ToList())); break;
        }
    }

    private void DescribeCell(int x, int z)
    {
        if (Doc == null || !Doc.Data.InBounds(x, z)) { inspect = ""; return; }
        var d = Doc.Data; var thing = Doc.Placements.At(x, z);
        var text = "(" + x + "," + z + ")  " + d.GroundId(x, z) + (d.IsSolid(x, z) ? "  wall" : "") + "  h " + d.Height(x, z).ToString("0.##") + "  clearance " + d.Clearance(x, z);
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
            Color32 c = palette[Math.Min(d.GroundAt(x, z), palette.Length - 1)];
            int h = d.HeightRaw(x, z);
            if (h != 0) { int shift = Mathf.Clamp(h * 6, -60, 60); c = new Color32((byte)Mathf.Clamp(c.r + shift, 0, 255), (byte)Mathf.Clamp(c.g + shift, 0, 255), (byte)Mathf.Clamp(c.b + shift, 0, 255), 255); }
            if (d.IsSolid(x, z)) c = new Color32(52, 54, 64, 255);
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
        zoom = Mathf.Clamp(Mathf.Floor(Mathf.Min((area.x - 20) / Doc.Data.Width, (area.y - 20) / Doc.Data.Depth) * 4f) / 4f, .25f, 48f);
        pan = new Vector2((area.x - Doc.Data.Width * zoom) * .5f, (area.y - Doc.Data.Depth * zoom) * .5f);
    }

    // ---- window ----
    private void OnEnable() { wantsMouseMove = true; }
    private void OnDisable() { FreePreview(); }
    private void OnDestroy() { if (texture != null) DestroyImmediate(texture); FreePreview(); }

    private void OnGUI()
    {
        if (hub == null) LoadContent();
        TopBar();
        // The whole body is ONE rect from the layout engine (it knows the real size of the window in GUI points), cut into the tools, canvas, splitter, panel and status bar by hand.
        var got = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        if (Event.current.type != EventType.Layout) lastBody = got;   // in the Layout event the engine gives a placeholder, so the areas of that pass use the last real rect (same controls in both passes)
        var body = lastBody.width > 20 ? lastBody : new Rect(0, 21, position.width, position.height - 47);
        bodyWidth = body.width;
        float statusHeight = 22f;
        var status = new Rect(body.x, body.yMax - statusHeight, body.width, statusHeight);
        float height = Mathf.Max(10, body.height - statusHeight);
        panelWidth = Mathf.Clamp(panelWidth, MinPanel, Mathf.Max(MinPanel, Mathf.Min(MaxPanel, body.width - ToolColumn - 200)));
        var toolRect = new Rect(body.x, body.y, ToolColumn, height);
        var panelRect = new Rect(body.xMax - panelWidth, body.y, panelWidth, height);
        var splitter = new Rect(panelRect.x - 5, body.y, 5, height);
        var center = new Rect(body.x + ToolColumn + 2, body.y, Mathf.Max(10, splitter.x - body.x - ToolColumn - 2), height);
        GUILayout.BeginArea(toolRect); ToolBar(); GUILayout.EndArea();
        Splitter(splitter);
        GUILayout.BeginArea(panelRect);
        panelScroll = GUILayout.BeginScrollView(panelScroll, GUIStyle.none, GUI.skin.verticalScrollbar);   // no horizontal bar: rows clip to the panel width
        GUILayout.BeginVertical(GUILayout.Width(panelWidth - 22));
        Panel();
        GUILayout.EndVertical();
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        Canvas(center);
        GUILayout.BeginArea(status); StatusBar(); GUILayout.EndArea();
        Hotkeys();
    }

    private void Splitter(Rect r)
    {
        EditorGUI.DrawRect(new Rect(r.x + 2, r.y, 1, r.height), new Color(0, 0, 0, .5f));
        EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeHorizontal);
        var e = Event.current;
        if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) { splitterDrag = true; e.Use(); }
        else if (splitterDrag && e.type == EventType.MouseDrag) { panelWidth = Mathf.Clamp(panelWidth - e.delta.x, MinPanel, Mathf.Min(MaxPanel, bodyWidth - 360)); e.Use(); Repaint(); }
        else if (e.type == EventType.MouseUp) splitterDrag = false;
    }

    private void TopBar()
    {
        using (new GUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(44))) NewMap(NewWidth, NewDepth, NewCell, Seed);
            if (GUILayout.Button("Open", EditorStyles.toolbarButton, GUILayout.Width(44))) { var p = EditorUtility.OpenFilePanel("Open map", MapFolder(), "json"); if (!string.IsNullOrEmpty(p)) OpenFile(p); }
            GUI.enabled = Doc != null;
            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(44))) { var p = FilePath ?? EditorUtility.SaveFilePanel("Save map", MapFolder(), "map", "json"); if (!string.IsNullOrEmpty(p)) SaveFile(p); }
            if (GUILayout.Button("Save as", EditorStyles.toolbarButton, GUILayout.Width(54))) { var p = EditorUtility.SaveFilePanel("Save map", MapFolder(), "map", "json"); if (!string.IsNullOrEmpty(p)) SaveFile(p); }
            GUILayout.Space(10);
            GUI.enabled = Doc != null && Doc.UndoCount > 0; if (GUILayout.Button(new GUIContent("Undo", "Ctrl+Z"), EditorStyles.toolbarButton, GUILayout.Width(44))) { Doc.Undo(); Dirty(); }
            GUI.enabled = Doc != null && Doc.RedoCount > 0; if (GUILayout.Button(new GUIContent("Redo", "Ctrl+Y"), EditorStyles.toolbarButton, GUILayout.Width(44))) { Doc.Redo(); Dirty(); }
            GUI.enabled = Doc != null;
            GUILayout.Space(10);
            view = (ViewMode)GUILayout.Toolbar((int)view, new[] { "2D", "3D", "2D + 3D" }, EditorStyles.toolbarButton, GUILayout.Width(160));
            if (GUILayout.Button(new GUIContent("Fit", "Fit the map in the view (F)"), EditorStyles.toolbarButton, GUILayout.Width(34))) fitPending = true;
            if (GUILayout.Button(new GUIContent("Validate", "Check the map"), EditorStyles.toolbarButton, GUILayout.Width(60))) Validate();
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.Label(Doc == null ? "No map" : (FilePath == null ? "(unsaved)" : Path.GetFileName(FilePath)) + (Doc.Dirty ? " *" : "") + "   " + Doc.Data.Width + " x " + Doc.Data.Depth + " cells of " + Doc.Data.CellSize + " m", EditorStyles.miniLabel);
        }
    }

    private void ToolBar()
    {
        GUILayout.Space(4);
        foreach (var (tool, label, key, tip) in Tools)
        {
            var content = new GUIContent(key == KeyCode.None ? label : label + "  " + key, tip);
            bool on = Current == tool;
            if (GUILayout.Toggle(on, content, EditorStyles.miniButton, GUILayout.Height(24)) && !on) Current = tool;
        }
        GUILayout.Space(8);
        GUILayout.Label("Brush " + Brush, EditorStyles.miniLabel);
        Brush = Mathf.RoundToInt(GUILayout.HorizontalSlider(Brush, 1, 16));
    }

    private void Panel()
    {
        if (Doc == null)
        {
            GUILayout.Label("New map", EditorStyles.boldLabel);
            NewMapFields();
            GUILayout.Label(Status, EditorStyles.wordWrappedLabel);
            return;
        }
        Section("Tool options", ToolOptions);
        Section("Selection", SelectionPanel);
        Section("Layers", LayersPanel);
        Section("Map block", MapBlock);
        Section("Generator", GeneratorPanel);
        Section("Stamps", StampsPanel);
        Section("Check", CheckPanel);
        Section("New map", NewMapFields, false);
    }

    private readonly Dictionary<string, bool> open = new Dictionary<string, bool>();
    private void Section(string title, Action body, bool defaultOpen = true)
    {
        if (!open.TryGetValue(title, out var state)) state = defaultOpen;
        open[title] = EditorGUILayout.BeginFoldoutHeaderGroup(state, title);
        if (open[title]) { EditorGUI.indentLevel++; body(); EditorGUI.indentLevel--; }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    private void NewMapFields()
    {
        NewWidth = EditorGUILayout.IntField("Width", NewWidth); NewDepth = EditorGUILayout.IntField("Depth", NewDepth);
        NewCell = EditorGUILayout.FloatField("Cell (m)", NewCell); Seed = EditorGUILayout.LongField("Seed", Seed);
        if (GUILayout.Button("Create")) NewMap(NewWidth, NewDepth, NewCell, Seed);
    }

    private bool Swatch(Color color, string tip, bool selected, float size = 30f)
    {
        var r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
        if (selected) EditorGUI.DrawRect(r, new Color(1, 1, 1, .95f));
        EditorGUI.DrawRect(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), color);
        return GUI.Button(r, new GUIContent("", tip), GUIStyle.none);
    }

    private void ToolOptions()
    {
        switch (Current)
        {
            case Tool.Ground:
                int columns = Mathf.Max(1, Mathf.FloorToInt((panelWidth - 40) / 34f));
                for (int i = 0; i < grounds.Length; i += columns)
                    using (new GUILayout.HorizontalScope())
                        for (int j = i; j < Mathf.Min(grounds.Length, i + columns); j++)
                            if (Swatch(new Color32(grounds[j].Red, grounds[j].Green, grounds[j].Blue, 255), groundNames[j] + (grounds[j].Walkable ? "" : " (blocks)"), GroundIndex == j)) GroundIndex = j;
                if (groundNames.Length > 0) GUILayout.Label(groundNames[Mathf.Clamp(GroundIndex, 0, groundNames.Length - 1)], EditorStyles.miniLabel);
                break;
            case Tool.Place:
                for (int i = 0; i < things.Length; i++)
                {
                    var s = things[i].Settings;
                    using (new GUILayout.HorizontalScope())
                    {
                        if (Swatch(s.BlocksPath ? new Color(.84f, .47f, .16f) : new Color(.94f, .82f, .24f), things[i].Id.ToString(), ThingIndex == i, 22)) ThingIndex = i;
                        if (GUILayout.Button(thingNames[i], EditorStyles.label)) ThingIndex = i;
                    }
                }
                ThingDirection = EditorGUILayout.IntSlider("Direction (R)", ThingDirection, 0, 3);
                break;
            case Tool.PlayerSpawn: case Tool.MonsterSpawn: SpawnName = EditorGUILayout.TextField("Name", SpawnName); break;
            case Tool.Gate: GateName = EditorGUILayout.TextField("Name", GateName); GateTo = EditorGUILayout.TextField("Leads to", GateTo); break;
            case Tool.Zone: ZoneName = EditorGUILayout.TextField("Name", ZoneName); ZoneSpawnBlock = EditorGUILayout.Toggle("Spawn block", ZoneSpawnBlock); ZoneRate = EditorGUILayout.Slider("Rate scale", (float)ZoneRate, 0, 4); break;
            case Tool.Group: GroupName = EditorGUILayout.TextField("Name", GroupName); GroupEntries = EditorGUILayout.TextField("Entities", GroupEntries); GUILayout.Label("e.g. project_n:slime*3, project_n:bat*2", EditorStyles.wordWrappedMiniLabel); break;
            default: GUILayout.Label(Tools.First(t => t.tool == Current).tip, EditorStyles.wordWrappedMiniLabel); break;
        }
    }

    private void SelectionPanel()
    {
        if (selKind == "") { GUILayout.Label("Nothing selected. Use Select (V) and click a spawn, gate, zone, group or thing.", EditorStyles.wordWrappedMiniLabel); return; }
        EditorGUILayout.LabelField(selKind, selName);
        switch (selKind)
        {
            case "spawn":
                selSpawnKind = EditorGUILayout.Popup("Kind", selSpawnKind == PNGridMapMeta.MonsterKind ? 1 : 0, new[] { PNGridMapMeta.PlayerKind, PNGridMapMeta.MonsterKind }) == 1 ? PNGridMapMeta.MonsterKind : PNGridMapMeta.PlayerKind;
                selA = EditorGUILayout.IntField("X", selA); selB = EditorGUILayout.IntField("Z", selB); break;
            case "gate": case "zone":
                selA = EditorGUILayout.IntField("X from", selA); selB = EditorGUILayout.IntField("Z from", selB); selC = EditorGUILayout.IntField("X to", selC); selD = EditorGUILayout.IntField("Z to", selD);
                if (selKind == "gate") selTo = EditorGUILayout.TextField("Leads to", selTo);
                else { selBlock = EditorGUILayout.Toggle("Spawn block", selBlock); selRate = EditorGUILayout.Slider("Rate scale", (float)selRate, 0, 4); }
                break;
            case "group": selA = EditorGUILayout.IntField("X", selA); selB = EditorGUILayout.IntField("Z", selB); selText = EditorGUILayout.TextField("Entities", selText); break;
            case "thing": GUILayout.Label("at (" + selA + "," + selB + ")  direction " + selC + "  health " + selD, EditorStyles.miniLabel); break;
        }
        using (new GUILayout.HorizontalScope())
        {
            GUI.enabled = selKind != "thing"; if (GUILayout.Button("Apply")) ApplySelection(); GUI.enabled = true;
            if (GUILayout.Button("Delete (Del)")) DeleteSelection();
        }
    }

    private void LayersPanel()
    {
        bool before = ShowThings;
        ShowGrid = EditorGUILayout.ToggleLeft("Grid (G)", ShowGrid); ShowThings = EditorGUILayout.ToggleLeft("Placed things", ShowThings); ShowSafety = EditorGUILayout.ToggleLeft("Safety radius of things", ShowSafety);
        ShowZones = EditorGUILayout.ToggleLeft("Zones", ShowZones); ShowGates = EditorGUILayout.ToggleLeft("Gates", ShowGates); ShowSpawns = EditorGUILayout.ToggleLeft("Spawn points", ShowSpawns);
        ShowGroups = EditorGUILayout.ToggleLeft("Groups", ShowGroups); ShowMini = EditorGUILayout.ToggleLeft("Minimap", ShowMini);
        if (before != ShowThings) viewDirty = true;
    }

    private void MapBlock()
    {
        var meta = Doc.Meta;
        if (meta.IsEmpty) GUILayout.Label("Empty. Use the spawn, gate, zone and group tools.", EditorStyles.wordWrappedMiniLabel);
        foreach (var s in meta.Spawns.ToList()) BlockRow("spawn " + s.Name + " (" + s.Kind + ") " + s.X + "," + s.Z, () => { Current = Tool.Inspect; Select(s.X, s.Z); Focus(s.X, s.Z); });
        foreach (var g in meta.Gates.ToList()) BlockRow("gate " + g.Name + " -> " + g.To, () => { Current = Tool.Inspect; Select(g.Region.X0, g.Region.Z0); Focus(g.Region.X0, g.Region.Z0); });
        foreach (var z in meta.Zones.ToList()) BlockRow("zone " + z.Name + (z.SpawnBlock ? " (block)" : " x" + z.RateScale), () => { Current = Tool.Inspect; Select(z.Region.X0, z.Region.Z0); Focus(z.Region.X0, z.Region.Z0); });
        foreach (var g in meta.Groups.ToList()) BlockRow("group " + g.Name + " " + g.Entries.Sum(e => e.Count) + " monsters", () => { Current = Tool.Inspect; Select(g.X, g.Z); Focus(g.X, g.Z); });
    }
    private void BlockRow(string text, Action pick) { if (GUILayout.Button(text, EditorStyles.miniButton)) pick(); }
    private void Focus(int x, int z) { pan = new Vector2(canvas.width, canvas.height) * .5f - new Vector2(x * zoom, (Doc.Data.Depth - z) * zoom); target = new Vector3(x * Doc.Data.CellSize, 0, z * Doc.Data.CellSize); }

    private void GeneratorPanel()
    {
        Chain = EditorGUILayout.TextArea(Chain, GUILayout.MinHeight(70));
        if (GUILayout.Button("Run generator (uses Seed)")) RunGenerator();
    }

    private void StampsPanel()
    {
        using (new GUILayout.HorizontalScope())
        {
            GUI.enabled = Stamps.ContainsKey("clip");
            if (GUILayout.Button("Save stamp")) { var p = EditorUtility.SaveFilePanel("Save stamp", MapFolder(), "stamp", "json"); if (!string.IsNullOrEmpty(p)) File.WriteAllText(p, Stamps["clip"].ToJson().ToString()); }
            GUI.enabled = true;
            if (GUILayout.Button("Load stamp")) { var p = EditorUtility.OpenFilePanel("Load stamp", MapFolder(), "json"); if (!string.IsNullOrEmpty(p)) Try(() => { Stamps["clip"] = PNGridStamp.Parse(JObject.Parse(File.ReadAllText(p))); Status = "Stamp loaded as \"clip\"."; }); }
        }
    }

    private void CheckPanel()
    {
        if (GUILayout.Button("Validate")) Validate();
        issueScroll = GUILayout.BeginScrollView(issueScroll, GUILayout.MaxHeight(160));
        foreach (var issue in Issues)
            if (GUILayout.Button((issue.Level == PNMapIssueLevel.Error ? "[error] " : "[warn] ") + issue.Message, EditorStyles.wordWrappedMiniLabel) && issue.X >= 0) Focus(issue.X, issue.Z);
        GUILayout.EndScrollView();
    }

    private void StatusBar()
    {
        using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            string tip = GUI.tooltip;
            GUILayout.Label(!string.IsNullOrEmpty(tip) ? tip : Status, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label(inspect, EditorStyles.miniLabel);
            if (Doc != null) GUILayout.Label("zoom " + zoom.ToString("0.##") + "x", EditorStyles.miniLabel, GUILayout.Width(70));
        }
    }

    private static string MapFolder()
    {
        var folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Maps");
        Directory.CreateDirectory(folder); return folder;
    }

    // ---- hotkeys ----
    private void Hotkeys()
    {
        var e = Event.current;
        if (e.type == EventType.KeyUp && e.keyCode == KeyCode.Space) spaceHeld = false;
        if (e.type != EventType.KeyDown || Doc == null || EditorGUIUtility.editingTextField) return;
        bool ctrl = e.control || e.command;
        if (ctrl && e.keyCode == KeyCode.Z) { Doc.Undo(); Dirty(); e.Use(); }
        else if (ctrl && e.keyCode == KeyCode.Y) { Doc.Redo(); Dirty(); e.Use(); }
        else if (ctrl) return;
        else if (e.keyCode == KeyCode.Space) { spaceHeld = true; e.Use(); }
        else if (e.keyCode == KeyCode.LeftBracket) { Brush = Mathf.Max(1, Brush - 1); e.Use(); }
        else if (e.keyCode == KeyCode.RightBracket) { Brush = Mathf.Min(16, Brush + 1); e.Use(); }
        else if (e.keyCode == KeyCode.G) { ShowGrid = !ShowGrid; e.Use(); }
        else if (e.keyCode == KeyCode.F) { fitPending = true; e.Use(); }
        else if (e.keyCode == KeyCode.R) { ThingDirection = (ThingDirection + 1) & 3; e.Use(); }
        else if (e.keyCode == KeyCode.Tab) { view = (ViewMode)(((int)view + 1) % 3); e.Use(); }
        else if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) { DeleteSelection(); e.Use(); }
        else foreach (var t in Tools) if (t.key != KeyCode.None && t.key == e.keyCode) { Current = t.tool; e.Use(); break; }
        Repaint();
    }

    // ---- canvas ----
    private Rect canvas;
    private void Canvas(Rect area)
    {
        EditorGUI.DrawRect(area, new Color(.13f, .14f, .16f));
        if (Doc == null) { GUI.Label(area, "Create or open a map.", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 14 }); return; }
        Rect flat = area, solid = area;
        if (view == ViewMode.Solid) flat = Rect.zero;
        else if (view == ViewMode.Split) { flat = new Rect(area.x, area.y, area.width * .5f - 1, area.height); solid = new Rect(area.x + area.width * .5f + 1, area.y, area.width * .5f - 1, area.height); }
        if (view != ViewMode.Solid) FlatCanvas(flat);
        if (view != ViewMode.Flat) SolidView(solid);
    }

    private void FlatCanvas(Rect area)
    {
        canvas = new Rect(0, 0, area.width, area.height);
        if (fitPending && Event.current.type == EventType.Repaint) { FitView(area.size); fitPending = false; }
        if (viewDirty || texture == null) Render();
        GUI.BeginGroup(area);
        var d = Doc.Data;
        GUI.DrawTexture(new Rect(pan.x, pan.y, d.Width * zoom, d.Depth * zoom), texture, ScaleMode.StretchToFill, false);
        if (ShowGrid && zoom >= 8) DrawGrid(area.size);
        // the edge of the map
        Handles.color = new Color(1, 1, 1, .5f); var o = new Rect(pan.x, pan.y, d.Width * zoom, d.Depth * zoom);
        Handles.DrawSolidRectangleWithOutline(o, Color.clear, new Color(1, 1, 1, .5f));
        if (ShowZones) foreach (var zone in Doc.Meta.Zones) { var r = RegionRect(zone.Region); EditorGUI.DrawRect(r, zone.SpawnBlock ? new Color(.9f, .2f, .2f, .22f) : new Color(.2f, .6f, .9f, .22f)); if (zoom >= 3) GUI.Label(r, zone.Name, EditorStyles.whiteMiniLabel); }
        if (ShowGates) foreach (var gate in Doc.Meta.Gates) { var r = RegionRect(gate.Region); EditorGUI.DrawRect(r, new Color(.3f, .5f, 1f, .6f)); if (zoom >= 3) GUI.Label(r, gate.Name, EditorStyles.whiteMiniLabel); }
        if (ShowGroups) foreach (var g in Doc.Meta.Groups) { var p = ToScreen(g.X + .5f, g.Z + .5f); EditorGUI.DrawRect(new Rect(p.x - 4, p.y - 4, 8, 8), new Color(.9f, .3f, .9f)); }
        if (ShowSpawns) foreach (var s in Doc.Meta.Spawns) { var p = ToScreen(s.X + .5f, s.Z + .5f); EditorGUI.DrawRect(new Rect(p.x - 5, p.y - 5, 10, 10), s.Kind == PNGridMapMeta.PlayerKind ? new Color(.2f, .9f, .3f) : new Color(.95f, .25f, .25f)); }
        if (ShowSafety)
            foreach (var thing in Doc.Placements.All)
            {
                var s = thing.Definition.Settings; if (s.SafetyRadius <= 0) continue;
                var c = ToScreen(thing.X + thing.CellsX * .5f, thing.Z + thing.CellsZ * .5f);
                Handles.color = s.SpawnBlock ? new Color(.95f, .3f, .3f, .8f) : new Color(.3f, .8f, .95f, .8f);
                Handles.DrawWireDisc(c, Vector3.forward, (float)s.SafetyRadius / d.CellSize * zoom);
            }
        if (selKind != "") DrawSelection();
        foreach (var cell in stroke) { int x = (int)(cell & 0xffffffff), z = (int)(cell >> 32); EditorGUI.DrawRect(RegionRect(new PNGridRegion(x, z, x, z)), new Color(1, 1, 1, .35f)); }
        if (dragging && (Current == Tool.Gate || Current == Tool.Zone || Current == Tool.Copy))
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
            default: r = RegionRect(new PNGridRegion(Math.Min(selA, selC), Math.Min(selB, selD), Math.Max(selA, selC), Math.Max(selB, selD))); break;
        }
        Handles.DrawSolidRectangleWithOutline(r, Color.clear, Color.white);
    }

    private void DrawGrid(Vector2 size)
    {
        var d = Doc.Data; Handles.color = new Color(0, 0, 0, .25f);
        int x0 = Mathf.Max(0, Mathf.FloorToInt(-pan.x / zoom)), x1 = Mathf.Min(d.Width, Mathf.CeilToInt((size.x - pan.x) / zoom));
        int z0 = Mathf.Max(0, d.Depth - Mathf.CeilToInt((size.y - pan.y) / zoom)), z1 = Mathf.Min(d.Depth, d.Depth - Mathf.FloorToInt(-pan.y / zoom));
        for (int x = x0; x <= x1; x++) { float sx = pan.x + x * zoom; Handles.DrawLine(new Vector3(sx, Mathf.Max(0, pan.y), 0), new Vector3(sx, Mathf.Min(size.y, pan.y + d.Depth * zoom), 0)); }
        for (int z = z0; z <= z1; z++) { float sy = pan.y + (d.Depth - z) * zoom; Handles.DrawLine(new Vector3(Mathf.Max(0, pan.x), sy, 0), new Vector3(Mathf.Min(size.x, pan.x + d.Width * zoom), sy, 0)); }
    }

    private void Minimap(Vector2 size)
    {
        var d = Doc.Data; float scale = Mathf.Min(MiniSize / d.Width, MiniSize / d.Depth);
        var box = new Rect(size.x - d.Width * scale - 10, size.y - d.Depth * scale - 10, d.Width * scale, d.Depth * scale);
        if (box.width < 40) return;
        EditorGUI.DrawRect(new Rect(box.x - 2, box.y - 2, box.width + 4, box.height + 4), new Color(0, 0, 0, .6f));
        GUI.DrawTexture(box, texture, ScaleMode.StretchToFill, false);
        var view = new Rect(box.x + (-pan.x / zoom) * scale, box.y + (-pan.y / zoom) * scale, size.x / zoom * scale, size.y / zoom * scale);
        Handles.DrawSolidRectangleWithOutline(view, new Color(1, 1, 1, .08f), Color.white);
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && box.Contains(e.mousePosition)) { miniDrag = true; e.Use(); }
        if (miniDrag && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
        {
            float cx = (e.mousePosition.x - box.x) / scale, cy = (e.mousePosition.y - box.y) / scale;
            pan = new Vector2(size.x * .5f - cx * zoom, size.y * .5f - cy * zoom); e.Use(); Repaint();
        }
        if (e.type == EventType.MouseUp) miniDrag = false;
    }

    private PNGridRegion BrushRegion(int x, int z) => Current == Tool.Ground || Current == Tool.Wall || Current == Tool.EraseWall ? Square(x, z) : PNGridRegion.Cell(x, z);

    private void Handle(Rect area)
    {
        var e = Event.current; var m = e.mousePosition;
        bool inside = new Rect(0, 0, area.width, area.height).Contains(m);
        if (miniDrag) return;
        if (e.type == EventType.ScrollWheel && inside)
        {
            float before = zoom; zoom = Mathf.Clamp(zoom * (e.delta.y > 0 ? .8f : 1.25f), .25f, 48f);
            pan = m - (m - pan) * (zoom / before); e.Use(); Repaint(); return;
        }
        if (e.type == EventType.MouseDrag && (e.button == 1 || e.button == 2 || e.alt || (spaceHeld && e.button == 0))) { pan += e.delta; e.Use(); Repaint(); return; }
        bool has = ToCell(m, out int x, out int z);
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag) { hoverX = has ? x : -1; hoverZ = has ? z : -1; if (has) DescribeCell(x, z); Repaint(); }
        if (!inside || e.button != 0 || e.alt || spaceHeld) return;
        bool brush = Current == Tool.Ground || Current == Tool.Wall || Current == Tool.EraseWall, rect = Current == Tool.Gate || Current == Tool.Zone || Current == Tool.Copy;
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
            e.Use(); Repaint();
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
        builtVersion = -1;
    }

    private void SolidView(Rect area)
    {
        if (Doc == null) return;
        var e = Event.current; bool inside = area.Contains(e.mousePosition);
        // camera control over the 3D area
        if (inside)
        {
            if (e.type == EventType.ScrollWheel) { distance = Mathf.Clamp(distance * (e.delta.y > 0 ? 1.15f : .87f), 3, 600); e.Use(); Repaint(); }
            else if (e.type == EventType.MouseDrag && (e.button == 1 || (e.button == 0 && e.alt))) { yaw += e.delta.x * .4f; pitch = Mathf.Clamp(pitch + e.delta.y * .4f, 8, 89); e.Use(); Repaint(); }
            else if (e.type == EventType.MouseDrag && e.button == 2)
            {
                var flat = Quaternion.Euler(0, yaw, 0); float k = distance * .0016f;
                target += flat * new Vector3(-e.delta.x * k, 0, e.delta.y * k); e.Use(); Repaint();
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
        preview.DrawMesh(mesh, Matrix4x4.identity, material, 0);
        cam.Render();
        GUI.DrawTexture(area, preview.EndPreview(), ScaleMode.StretchToFill, false);
        GUI.Label(new Rect(area.x + 6, area.y + 4, area.width - 12, 18), "3D (view only)   right-drag: orbit   middle-drag: pan   wheel: zoom", EditorStyles.whiteMiniLabel);
    }

    private void BuildMesh(int centerX, int centerZ)
    {
        var d = Doc.Data; float cell = d.CellSize;
        int x0 = Mathf.Max(0, centerX - Window / 2), z0 = Mathf.Max(0, centerZ - Window / 2), x1 = Mathf.Min(d.Width, x0 + Window), z1 = Mathf.Min(d.Depth, z0 + Window);
        var vertices = new List<Vector3>(); var colors = new List<Color32>(); var triangles = new List<int>();
        var palette = grounds.Select(g => new Color32(g.Red, g.Green, g.Blue, 255)).ToArray();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 e, Color32 color)
        {
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
            if (d.IsSolid(x, z)) Box(x * cell, z * cell, cell, cell, h, h + PNGridWorldLimits.WallHeight, new Color32(78, 82, 96, 255));
            else Quad(new Vector3(x * cell, h, z * cell), new Vector3(x * cell, h, (z + 1) * cell), new Vector3((x + 1) * cell, h, (z + 1) * cell), new Vector3((x + 1) * cell, h, z * cell), c);
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
