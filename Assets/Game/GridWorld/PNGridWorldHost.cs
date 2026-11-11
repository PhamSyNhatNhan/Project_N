#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// EN: The Unity side of a grid world (Docs/SandboxPlan.md, step G1): one root object with one child per 32x32 chunk, a ground mesh + its collider (layer 13 `Ground`) and, where the chunk has solid cells, a wall child
//     (layer 9 `Wall`) with a mesh and merged box colliders. The cells (PNGridWorldData) are the truth; this keeps meshes and colliders equal to them by comparing each chunk's version with the one it built.
//     Chunk objects are made BEFORE the root is registered with the world presentation, because registration scans (and watches) every child once (measured: 4096 children cost 74 ms to scan once, 1.4 ms per later
//     change), and rebuilding a chunk only swaps its mesh, never adds or removes children of the registered root. The look is a placeholder: flat vertex colours per ground type.
// VI: Phía Unity của world lưới: một root, mỗi mảnh 32x32 một con (mesh nền + collider layer 13; mảnh có ô đặc thì thêm con tường layer 9). Ô là sự thật; lớp này giữ mesh/collider khớp phiên bản của mảnh.
public sealed class PNGridWorldHost : MonoBehaviour
{
    public const int GroundLayer = 13, WallLayer = 9;
    private static readonly List<Vector3> vertices = new List<Vector3>();
    private static readonly List<Vector3> normals = new List<Vector3>();
    private static readonly List<Color32> colors = new List<Color32>();
    private static readonly List<int> triangles = new List<int>();

    public PNGridWorldData Data { get; private set; }
    public int ChunksBuilt { get; private set; }
    public int Rebuilds { get; private set; }
    public int PendingChunks { get { int n = 0; for (int i = 0; i < built.Length; i++) if (built[i] != Data.ChunkVersionAt(i)) n++; return n; } }
    public int ChunkObjectCount { get { int n = 0; foreach (var go in chunkObjects) if (go != null) n++; return n; } }
    public int WallObjectCount { get { int n = 0; foreach (var go in wallObjects) if (go != null) n++; return n; } }
    private Color32[] groundColors;
    private GameObject[] chunkObjects, wallObjects;
    private Mesh[] groundMeshes, wallMeshes;
    private int[] built;
    private Material material;
    private bool disposed;

    // The root is created inactive and outside any world; the provider activates it after the world registered it.
    public static PNGridWorldHost Create(PNGridWorldData data, Color32[] groundColors)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (groundColors == null || groundColors.Length != data.Palette.Count) throw new ArgumentException("One colour per palette entry is required.", nameof(groundColors));
        var root = new GameObject("PN Grid World"); root.SetActive(false);
        var host = root.AddComponent<PNGridWorldHost>();
        host.Data = data; host.groundColors = (Color32[])groundColors.Clone();
        int n = data.ChunkCount;
        host.chunkObjects = new GameObject[n]; host.wallObjects = new GameObject[n]; host.groundMeshes = new Mesh[n]; host.wallMeshes = new Mesh[n]; host.built = new int[n];
        for (int i = 0; i < n; i++) host.built[i] = -1;
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        if (shader != null) host.material = new Material(shader) { name = "PN Grid World (placeholder)" };
        return host;
    }

    // Builds every chunk, yielding every `chunksPerStep` chunks so a big world does not stall one frame.
    public IEnumerator BuildAll(int chunksPerStep = 16)
    {
        int count = 0;
        for (int cz = 0; cz < Data.ChunksZ; cz++) for (int cx = 0; cx < Data.ChunksX; cx++)
        {
            BuildChunk(cx, cz);
            if (++count % chunksPerStep == 0) yield return null;
        }
    }

    // Rebuilds up to `budget` chunks whose data changed since they were built; returns how many.
    public int RebuildDirty(int budget)
    {
        if (disposed || Data == null) return 0;
        int done = 0;
        for (int i = 0; i < built.Length && done < budget; i++)
        {
            if (built[i] == Data.ChunkVersionAt(i)) continue;
            BuildChunk(i % Data.ChunksX, i / Data.ChunksX); done++;
        }
        return done;
    }

    private void Update() { if (Data != null && !disposed && isActiveAndEnabled) RebuildDirty(2); }

    public void BuildChunk(int cx, int cz)
    {
        if (disposed) throw new ObjectDisposedException(nameof(PNGridWorldHost));
        int index = Data.ChunkIndex(cx, cz); var region = Data.ChunkRegion(cx, cz); float cell = Data.CellSize;
        int version = Data.ChunkVersionAt(index);
        // ---- ground ----
        Begin();
        bool flat = true; short level = Data.HeightRaw(region.X0, region.Z0);
        for (int z = region.Z0; z <= region.Z1; z++) for (int x = region.X0; x <= region.X1; x++)
        {
            if (Data.HeightRaw(x, z) != level) flat = false;
            if (Data.IsSolid(x, z)) continue;
            float y = Data.Height(x, z); var color = groundColors[Data.GroundAt(x, z)];
            AddQuad(new Vector3(x * cell, y, z * cell), new Vector3(x * cell, y, (z + 1) * cell), new Vector3((x + 1) * cell, y, (z + 1) * cell), new Vector3((x + 1) * cell, y, z * cell), Vector3.up, color);
        }
        var chunk = chunkObjects[index];
        if (chunk == null)
        {
            chunk = new GameObject("chunk " + cx + "_" + cz) { layer = GroundLayer };
            chunk.transform.SetParent(transform, false);
            chunk.AddComponent<MeshFilter>(); var renderer = chunk.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            chunkObjects[index] = chunk;
        }
        groundMeshes[index] = Fill(groundMeshes[index], "ground " + cx + "_" + cz);
        chunk.GetComponent<MeshFilter>().sharedMesh = groundMeshes[index];
        foreach (var old in chunk.GetComponents<Collider>()) DestroyImmediate(old);
        if (flat)
        {
            var box = chunk.AddComponent<BoxCollider>(); float size = PNGridWorldLimits.ChunkCells * cell;
            box.center = new Vector3((region.X0 * cell) + size * .5f, level * PNGridWorldLimits.HeightStep - .05f, (region.Z0 * cell) + size * .5f); box.size = new Vector3(size, .1f, size);
        }
        else chunk.AddComponent<MeshCollider>().sharedMesh = groundMeshes[index];
        // ---- walls ----
        Begin();
        bool anySolid = false;
        for (int z = region.Z0; z <= region.Z1; z++) for (int x = region.X0; x <= region.X1; x++)
        {
            if (!Data.IsSolid(x, z)) continue;
            anySolid = true; float y0 = Data.Height(x, z), y1 = y0 + PNGridWorldLimits.WallHeight; float x0 = x * cell, x1 = (x + 1) * cell, z0 = z * cell, z1 = (z + 1) * cell;
            var gray = new Color32(104, 104, 112, 255); var side = new Color32(84, 84, 92, 255);
            AddQuad(new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.up, gray);
            if (Open(x - 1, z)) AddQuad(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), Vector3.left, side);
            if (Open(x + 1, z)) AddQuad(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Vector3.right, side);
            if (Open(x, z - 1)) AddQuad(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), Vector3.back, side);
            if (Open(x, z + 1)) AddQuad(new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), Vector3.forward, side);
        }
        var walls = wallObjects[index];
        if (!anySolid)
        {
            if (walls != null) { Kill(walls); wallObjects[index] = null; }
            if (wallMeshes[index] != null) { Kill(wallMeshes[index]); wallMeshes[index] = null; }
        }
        else
        {
            if (walls == null)
            {
                walls = new GameObject("walls " + cx + "_" + cz) { layer = WallLayer };
                walls.transform.SetParent(chunk.transform, false);
                walls.AddComponent<MeshFilter>(); var renderer = walls.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                wallObjects[index] = walls;
            }
            wallMeshes[index] = Fill(wallMeshes[index], "walls " + cx + "_" + cz);
            walls.GetComponent<MeshFilter>().sharedMesh = wallMeshes[index];
            foreach (var old in walls.GetComponents<Collider>()) DestroyImmediate(old);
            AddWallBoxes(walls, region);
        }
        if (built[index] < 0) ChunksBuilt++; else Rebuilds++;
        built[index] = version;
    }

    // A wall face is drawn only where the neighbour cell is inside the world and not solid.
    private bool Open(int x, int z) => Data.InBounds(x, z) && !Data.IsSolid(x, z);

    // Merged box colliders: runs of solid cells along x, joined with the run above when it has the same extent.
    private void AddWallBoxes(GameObject walls, PNGridRegion region)
    {
        float cell = Data.CellSize; var open = new Dictionary<(int, int), int>(); var finished = new List<(int x0, int x1, int z0, int z1)>();
        for (int z = region.Z0; z <= region.Z1 + 1; z++)
        {
            var runs = new List<(int, int)>();
            if (z <= region.Z1)
                for (int x = region.X0; x <= region.X1; x++)
                {
                    if (!Data.IsSolid(x, z)) continue;
                    int start = x; while (x + 1 <= region.X1 && Data.IsSolid(x + 1, z)) x++;
                    runs.Add((start, x));
                }
            foreach (var key in new List<(int, int)>(open.Keys))
                if (!runs.Contains(key)) { finished.Add((key.Item1, key.Item2, open[key], z - 1)); open.Remove(key); }
            foreach (var run in runs) if (!open.ContainsKey(run)) open[run] = z;
        }
        foreach (var f in finished)
        {
            var box = walls.AddComponent<BoxCollider>(); float y0 = Data.Height(f.x0, f.z0), h = PNGridWorldLimits.WallHeight;
            box.center = new Vector3((f.x0 + f.x1 + 1) * cell * .5f, y0 + h * .5f, (f.z0 + f.z1 + 1) * cell * .5f);
            box.size = new Vector3((f.x1 - f.x0 + 1) * cell, h, (f.z1 - f.z0 + 1) * cell);
        }
    }

    // Old colliders are removed at once (a ray in the same frame must not see a wall that was taken away); everything else goes through Kill. Edit Mode cannot Destroy, Play Mode should not DestroyImmediate.
    private static void Kill(UnityEngine.Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }

    private static void Begin() { vertices.Clear(); normals.Clear(); colors.Clear(); triangles.Clear(); }

    private static void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Color32 color)
    {
        // Unity's front face is clockwise: the cross product of the first two edges points at the viewer.
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0) { var t = b; b = d; d = t; }
        int i = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
        for (int k = 0; k < 4; k++) { normals.Add(outward); colors.Add(color); }
        triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2); triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 3);
    }

    private static Mesh Fill(Mesh mesh, string name)
    {
        if (mesh == null) { mesh = new Mesh { name = name }; mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; }
        else mesh.Clear();
        mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var mesh in groundMeshes ?? Array.Empty<Mesh>()) if (mesh != null) DestroyImmediate(mesh);
        foreach (var mesh in wallMeshes ?? Array.Empty<Mesh>()) if (mesh != null) DestroyImmediate(mesh);
        if (material != null) DestroyImmediate(material);
        if (gameObject != null) DestroyImmediate(gameObject);
    }

    private void OnDestroy()
    {
        if (disposed) return; disposed = true;
        foreach (var mesh in groundMeshes ?? Array.Empty<Mesh>()) if (mesh != null) Kill(mesh);
        foreach (var mesh in wallMeshes ?? Array.Empty<Mesh>()) if (mesh != null) Kill(mesh);
        if (material != null) Kill(material);
    }
}
#endif
