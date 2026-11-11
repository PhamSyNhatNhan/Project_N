#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// EN: The live side of editing a grid world (Docs/SandboxPlan.md, step G3), one per world, on the authority. It owns the edit gateway (PNGridEditor) and the placed things (PNGridPlacements), applies a limited
//     number of queued cell edits per frame, hands the regions that changed to the navigation lease (which updates its graph and tells the enemies that follow a route) and gives the chunk mesh host nothing to do:
//     the host sees the chunk versions change and rebuilds by itself. A placed thing that blocks the way is drawn by the wall mesh of its cells; one that does not block gets a small placeholder marker, kept
//     in a separate root (children added to the registered world root would be scanned by the presentation). Clients never edit: they send a command and later receive the result (step G8).
// VI: Phía chạy của việc sửa world lưới (G3), mỗi world một cái, ở authority: giữ cổng sửa ô và danh sách vật đặt, áp một số ô mỗi frame, đưa vùng đổi cho lease điều hướng; host mesh tự dựng lại theo phiên bản mảnh.
public sealed class PNGridWorldController : MonoBehaviour
{
    public const int CellsPerFrame = 512;
    // Regions handed to the graph per frame: a graph update costs about 0.2 to 0.4 ms per region plus a constant 1.2 to 2.3 ms (measured on a 2048 x 2048 world, the machine varies), so a burst of far-apart edits is spread over frames.
    public const int NavRegionsPerFrame = 8;
    private readonly Queue<PNGridRegion> navPending = new Queue<PNGridRegion>();
    private readonly List<PNGridRegion> navSlice = new List<PNGridRegion>();
    public int PendingNavRegions => navPending.Count;
    private static readonly Dictionary<EveWorldContext, PNGridWorldController> controllers = new Dictionary<EveWorldContext, PNGridWorldController>();
    private readonly List<PNGridRegion> regions = new List<PNGridRegion>();
    private readonly Dictionary<int, GameObject> markers = new Dictionary<int, GameObject>();
    private GameObject markerRoot;
    private Material markerMaterial;
    private bool disposed;

    public EveWorldContext World { get; private set; }
    public PNGridWorldData Data { get; private set; }
    public PNGridEditor Editor { get; private set; }
    public PNGridPlacements Placements { get; private set; }
    public PNGridWorldHost Host { get; private set; }
    public int TicksWithEdits { get; private set; }
    public int CellsPerFrameLimit { get; set; } = CellsPerFrame;

    public static PNGridWorldController Find(EveWorldContext world) => world != null && controllers.TryGetValue(world, out var value) ? value : null;

    // `buildable[i]` says whether something may be put on palette entry i.
    public static PNGridWorldController Create(EveWorldContext world, PNGridWorldHost host, bool[] buildable)
    {
        if (world == null || host == null) throw new ArgumentNullException(world == null ? nameof(world) : nameof(host));
        if (controllers.ContainsKey(world)) throw new InvalidOperationException("This world already has a grid controller.");
        var controller = host.gameObject.AddComponent<PNGridWorldController>();
        controller.World = world; controller.Host = host; controller.Data = host.Data;
        controller.Editor = new PNGridEditor(host.Data);
        controller.Placements = new PNGridPlacements(host.Data, controller.Editor, index => index >= 0 && index < buildable.Length && buildable[index]);
        controller.Placements.Added += controller.OnAdded; controller.Placements.Removed += controller.OnRemoved;
        controllers[world] = controller; return controller;
    }

    private void Update()
    {
        if (disposed || World == null || !World.CanExecute || !World.Session.HasAuthority) return;
        Pump();
    }

    // Applies queued edits (one frame's worth) and passes what changed on to the navigation lease. Public so a test can drive it without waiting for frames.
    public int Pump()
    {
        regions.Clear();
        int taken = Editor.Tick(CellsPerFrameLimit, regions);
        if (taken > 0) TicksWithEdits++;
        foreach (var region in regions) navPending.Enqueue(region);
        if (navPending.Count == 0) return taken;
        navSlice.Clear();
        while (navPending.Count > 0 && navSlice.Count < NavRegionsPerFrame) navSlice.Add(navPending.Dequeue());
        PNWorldNavigation.Find(World)?.ApplyGridChanges(navSlice);
        return taken;
    }

    private void OnAdded(PNPlacedObject placed)
    {
        if (placed.Definition.Settings.BlocksPath) return;
        if (markerRoot == null)
        {
            markerRoot = new GameObject("PN Grid Markers");
            markerRoot.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(markerRoot, gameObject.scene);
            markerRoot.SetActive(true);
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader != null) markerMaterial = new Material(shader) { name = "PN Grid Marker (placeholder)", color = new Color(1f, .85f, .3f, 1f) };
        }
        float cell = Data.CellSize;
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var collider = marker.GetComponent<Collider>(); if (collider != null) DestroyImmediate(collider);   // the marker is a picture; bodies walk over it
        marker.name = "marker " + placed.Id; marker.transform.SetParent(markerRoot.transform, false);
        marker.transform.localPosition = new Vector3((placed.X + placed.CellsX * .5f) * cell, .15f, (placed.Z + placed.CellsZ * .5f) * cell);
        marker.transform.localScale = new Vector3(placed.CellsX * cell * .8f, .3f, placed.CellsZ * cell * .8f);
        if (markerMaterial != null) marker.GetComponent<Renderer>().sharedMaterial = markerMaterial;
        markers[placed.Id] = marker;
    }

    private void OnRemoved(PNPlacedObject placed)
    {
        if (markers.TryGetValue(placed.Id, out var marker)) { markers.Remove(placed.Id); if (marker != null) DestroyImmediate(marker); }
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (World != null) controllers.Remove(World);
        if (Placements != null) { Placements.Added -= OnAdded; Placements.Removed -= OnRemoved; }
        markers.Clear();
        if (markerRoot != null) DestroyImmediate(markerRoot);
        if (markerMaterial != null) DestroyImmediate(markerMaterial);
        markerRoot = null; markerMaterial = null;
    }

    private void OnDestroy() { if (!disposed) Dispose(); }
}
#endif
