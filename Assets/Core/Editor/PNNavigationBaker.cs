#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using EveEngine;
using Pathfinding;
using Pathfinding.Serialization;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit mesh inputs, no global scene scan, save, reload or authored object mutation.
public static class PNNavigationBaker
{
    private static MeshFilter[] Inputs(IEnumerable<GameObject> roots)
    {
        var selected = (roots ?? throw new ArgumentNullException(nameof(roots))).ToArray();
        if (selected.Length == 0 || selected.Any(x => x == null)) throw new ArgumentException("Navigation geometry roots required.");
        var meshes = selected.SelectMany(x => x.GetComponentsInChildren<MeshFilter>(true)).Where(x => x.sharedMesh != null).Distinct().ToArray();
        if (meshes.Length == 0) throw new ArgumentException("No mesh geometry to bake.");
        if (selected.SelectMany(x => x.GetComponentsInChildren<SkinnedMeshRenderer>(true)).Any()) throw new ArgumentException("Bake static geometry only.");
        return meshes;
    }
    public static string GeometryHash(IEnumerable<GameObject> roots)
    {
        var meshes = Inputs(roots);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("pn-nav-meshes:1");
        // Stable order from the caller's root and authored hierarchy order; no InstanceID/scene handles.
        foreach (var mesh in meshes)
        {
            var matrix = mesh.transform.localToWorldMatrix; for (int i = 0; i < 16; i++) writer.Write(matrix[i]);
            var vertices = mesh.sharedMesh.vertices; var triangles = mesh.sharedMesh.triangles;
            writer.Write(vertices.Length); foreach (var v in vertices) { writer.Write(v.x); writer.Write(v.y); writer.Write(v.z); }
            writer.Write(triangles.Length); foreach (var index in triangles) writer.Write(index);
        }
        writer.Flush(); using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
    }
    public static void Bake(PNNavigationCache destination, IEnumerable<GameObject> roots, string profileId,
        float radius, float height, Bounds bounds, float cellSize = .2f, float climb = .3f, float maxSlope = 50)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        PNNavigationSettings.Positive(radius, nameof(radius)); PNNavigationSettings.Positive(height, nameof(height));
        PNNavigationSettings.Positive(cellSize, nameof(cellSize)); PNNavigationSettings.Positive(climb, nameof(climb));
        PNNavigationSettings.Position(bounds.center); PNNavigationSettings.Position(bounds.size);
        if (bounds.size.x <= 0 || bounds.size.y <= 0 || bounds.size.z <= 0 || maxSlope <= 0 || maxSlope >= 90 || float.IsNaN(maxSlope)) throw new ArgumentException("Invalid bake bounds or slope.");
        if (AstarPath.active != null || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Bake in an idle editor/clone without a live navigation owner.");
        // This vendor version always gathers Recast modifiers. Reject rather than silently bake a foreign modifier.
        if (UnityEngine.Object.FindObjectsByType<RecastNavmeshModifier>().Any(x => x.isActiveAndEnabled)) throw new InvalidOperationException("Explicit mesh baker does not support active Recast modifiers.");
        var selected = roots.ToArray(); var meshes = Inputs(selected); string geometryHash = GeometryHash(selected);
        var preview = EditorSceneManager.NewPreviewScene(); GameObject owner = null;
        try
        {
            owner = new GameObject("Navigation authoring owner"); owner.SetActive(false); SceneManager.MoveGameObjectToScene(owner, preview);
            var astar = owner.AddComponent<AstarPath>(); astar.scanOnStartup = false; astar.threadCount = ThreadCount.None; astar.logPathResults = PathLog.None; owner.SetActive(true);
            var graph = astar.data.AddGraph<RecastGraph>(); graph.name = profileId;
            graph.forcedBoundsCenter = bounds.center; graph.forcedBoundsSize = bounds.size;
            graph.characterRadius = radius; graph.walkableHeight = height; graph.cellSize = cellSize;
            graph.walkableClimb = climb; graph.maxSlope = maxSlope; graph.minRegionSize = 0;
            graph.collectionSettings.rasterizeMeshes = false; graph.collectionSettings.rasterizeColliders = false;
            graph.collectionSettings.rasterizeTerrain = false; graph.collectionSettings.rasterizeTrees = false;
            graph.collectionSettings.layerMask = 0; graph.collectionSettings.tagMask = new List<string>();
            graph.collectionSettings.onCollectMeshes = gatherer =>
            {
                foreach (var input in meshes)
                {
                    int index = gatherer.AddMeshBuffers(input.sharedMesh.vertices, input.sharedMesh.triangles);
                    gatherer.AddMesh(new Pathfinding.Graphs.Navmesh.RecastMeshGatherer.GatheredMesh {
                        meshDataIndex = index, matrix = input.transform.localToWorldMatrix, indexStart = 0, indexEnd = -1,
                        area = 0, solid = false, doubleSided = false, flatten = false, areaIsTag = false });
                }
            };
            astar.Scan(graph); int walkable = 0; graph.GetNodes(node => { if (node.Walkable) walkable++; });
            if (walkable == 0) throw new InvalidOperationException("Bake produced no walkable nodes.");
            // TinyJson in 5.3.3 serializes this public callback despite its documented runtime-only role.
            // The nodes are already baked; keep executable delegates out of the cache settings.
            graph.collectionSettings.onCollectMeshes = null;
            var bytes = astar.data.SerializeGraphs(SerializeSettings.NodesAndSettings);
            destination.SetBakedData(bytes, geometryHash, profileId, radius, height);
            if (AssetDatabase.Contains(destination)) EditorUtility.SetDirty(destination);
        }
        finally { if (owner != null) UnityEngine.Object.DestroyImmediate(owner); EditorSceneManager.ClosePreviewScene(preview); }
    }
    [MenuItem("Tools/Project N/Navigation/Bake selected mesh roots")]
    private static void BakeSelected()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Bake in idle Edit Mode.");
        var roots = Selection.gameObjects; var meshes = Inputs(roots); var bounds = meshes[0].GetComponent<Renderer>().bounds;
        foreach (var filter in meshes.Skip(1)) bounds.Encapsulate(filter.GetComponent<Renderer>().bounds);
        bounds.Expand(new Vector3(1, 4, 1));
        string path = EditorUtility.SaveFilePanelInProject("Navigation cache", "GroundSmallNavigation", "asset", "Choose cache asset");
        if (string.IsNullOrEmpty(path)) return;
        var cache = AssetDatabase.LoadAssetAtPath<PNNavigationCache>(path); bool create = cache == null;
        if (create) cache = ScriptableObject.CreateInstance<PNNavigationCache>();
        try { Bake(cache, roots, "ground-small", .25f, 1.5f, bounds); if (create) AssetDatabase.CreateAsset(cache, path); AssetDatabase.SaveAssetIfDirty(cache); Selection.activeObject = cache; }
        catch { if (create) UnityEngine.Object.DestroyImmediate(cache); throw; }
    }
}

// Build-time detection of stale authored geometry without opening/saving original scenes.
public sealed class PNNavigationBuildValidation : IProcessSceneWithReport
{
    public int callbackOrder => 0;
    public void OnProcessScene(Scene scene, BuildReport report)
    {
        foreach (var source in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PNWorldNavigationSource>(true)))
        {
            source.Validate();
            if (PNNavigationBaker.GeometryHash(source.GeometryRoots) != source.Cache.MapHash)
                throw new BuildFailedException("Stale navigation geometry: " + source.name + ". Bake its declared roots again.");
        }
    }
}
#endif
