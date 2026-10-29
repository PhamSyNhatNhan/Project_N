#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using EveEngine;
using UnityEngine;

// EN: Where the PNG of a Visual part is read from: the folder of the pack that owns the part (its id's namespace), the path relative to it. The runtime publishes the packs when
//     content is published; tests and tools may set a folder by hand. A path that leaves the pack folder is refused.
// VI: Nơi đọc PNG của part: thư mục của pack sở hữu part (namespace của id). Đường dẫn ra ngoài thư mục pack bị từ chối.
public static class PNVisualPacks
{
    private static readonly Dictionary<string, string> directories = new Dictionary<string, string>(StringComparer.Ordinal);
    public static int Count => directories.Count;
    public static void Clear() => directories.Clear();
    public static void Set(string contentNamespace, string directory)
    {
        if (string.IsNullOrEmpty(contentNamespace)) throw new ArgumentException("namespace required", nameof(contentNamespace));
        if (string.IsNullOrEmpty(directory)) throw new ArgumentException("directory required", nameof(directory));
        directories[contentNamespace] = Path.GetFullPath(directory);
    }
    public static void Publish(IEnumerable<EveContentPack> packs)
    {
        directories.Clear();
        if (packs == null) return;
        foreach (var pack in packs) if (pack != null && !string.IsNullOrEmpty(pack.Namespace) && !string.IsNullOrEmpty(pack.Directory)) directories[pack.Namespace] = Path.GetFullPath(pack.Directory);
    }
    public static bool TryResolve(string contentNamespace, string relative, out string fullPath)
    {
        fullPath = null;
        if (contentNamespace == null || relative == null || !directories.TryGetValue(contentNamespace, out var root)) return false;
        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString()) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        fullPath = full; return true;
    }
    public static byte[] Read(string contentNamespace, string relative)
    {
        if (!TryResolve(contentNamespace, relative, out var path)) throw new FileNotFoundException("Visual image outside its pack or the pack is unknown: " + contentNamespace + ":" + relative);
        if (!File.Exists(path)) throw new FileNotFoundException("Visual image missing: " + contentNamespace + ":" + relative);
        return File.ReadAllBytes(path);
    }
}

// One texture holding every PNG the loaded content uses, packed once when the content is ready (mods add parts, so it cannot be baked in the Editor).
public sealed class PNVisualAtlas : IDisposable
{
    public const int MaxSize = 4096, Padding = 4;
    private readonly Dictionary<string, Rect> rects = new Dictionary<string, Rect>(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector2Int> sizes = new Dictionary<string, Vector2Int>(StringComparer.Ordinal);
    public Texture2D Texture { get; private set; }
    public int ImageCount => rects.Count;
    public static string Key(string contentNamespace, string path) => contentNamespace + "|" + path;

    public static PNVisualAtlas Build(IEnumerable<KeyValuePair<string, string>> images)   // (namespace, path) pairs
    {
        var atlas = new PNVisualAtlas(); var keys = new List<string>(); var textures = new List<Texture2D>();
        try
        {
            foreach (var pair in images)
            {
                string key = Key(pair.Key, pair.Value); if (atlas.rects.ContainsKey(key) || keys.Contains(key)) continue;
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
                if (!texture.LoadImage(PNVisualPacks.Read(pair.Key, pair.Value), false)) { UnityEngine.Object.DestroyImmediate(texture); throw new InvalidDataException("Not a readable PNG: " + key); }
                keys.Add(key); textures.Add(texture); atlas.sizes[key] = new Vector2Int(texture.width, texture.height);
            }
            if (textures.Count == 0) { atlas.Texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave }; atlas.Texture.SetPixel(0, 0, Color.white); atlas.Texture.Apply(); return atlas; }
            long area = 0; foreach (var t in textures) area += (long)(t.width + Padding * 2) * (t.height + Padding * 2);
            if (area > (long)MaxSize * MaxSize * .8) throw new InvalidOperationException("Visual images do not fit one " + MaxSize + " atlas page.");
            var texture2 = new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var packed = texture2.PackTextures(textures.ToArray(), Padding, MaxSize, false);
            if (packed == null || packed.Length != keys.Count) { UnityEngine.Object.DestroyImmediate(texture2); throw new InvalidOperationException("Visual atlas packing failed."); }
            for (int i = 0; i < keys.Count; i++) atlas.rects[keys[i]] = packed[i];
            atlas.Texture = texture2;
            return atlas;
        }
        catch { atlas.Dispose(); throw; }
        finally { foreach (var t in textures) if (t != null) UnityEngine.Object.DestroyImmediate(t); }
    }

    public bool TryGet(string contentNamespace, string path, out Rect uv, out Vector2Int size)
    {
        string key = Key(contentNamespace, path); size = default;
        if (!rects.TryGetValue(key, out uv)) return false;
        size = sizes[key]; return true;
    }

    public void Dispose()
    {
        if (Texture != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(Texture); else UnityEngine.Object.DestroyImmediate(Texture); Texture = null; }
        rects.Clear(); sizes.Clear();
    }
}
#endif
