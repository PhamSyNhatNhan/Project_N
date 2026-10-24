using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Newtonsoft.Json.Linq;

// EN: Immutable ordered set of layers. Mask answers "is this layer targeted" fast (hot loops read it once); Order keeps the authored order, which is
//     reserved for autotarget priority (nothing reads it yet). First = highest priority; no duplicates. JSON form: ["Enemy","Projectile"].
// VI: Tập layer bất biến có thứ tự. Mask trả lời nhanh "layer này có bị nhắm không" (vòng lặp nóng đọc một lần); Order giữ thứ tự khai báo,
//     dành cho ưu tiên autotarget (chưa ai đọc). Đầu tiên = ưu tiên cao nhất; không trùng. Dạng JSON: ["Enemy","Projectile"].
public sealed class PNTargetLayers
{
    public static readonly PNTargetLayers None = new PNTargetLayers(Array.Empty<int>(), 0);
    public int Mask { get; }
    public IReadOnlyList<int> Order { get; }
    public bool IsEmpty => Mask == 0;
    private PNTargetLayers(int[] order, int mask) { Order = new ReadOnlyCollection<int>(order); Mask = mask; }

    // Game-code defaults, for example Of(PNLayers.Enemy). Layers must be 0..31 and unique.
    public static PNTargetLayers Of(params int[] layers)
    {
        if (layers == null) throw new ArgumentNullException(nameof(layers));
        if (layers.Length == 0) return None;
        int mask = 0;
        foreach (var layer in layers)
        {
            if (layer < 0 || layer > 31) throw new ArgumentOutOfRangeException(nameof(layers), "Layer must be 0..31.");
            if (PNLayers.Contains(mask, layer)) throw new ArgumentException("Duplicate layer " + layer + ".", nameof(layers));
            mask |= PNLayers.Bit(layer);
        }
        return new PNTargetLayers((int[])layers.Clone(), mask);
    }

    // ["Enemy","Projectile"] -> ordered set. Absent token -> None. Non-array, non-string, unknown or duplicate names are rejected.
    public static PNTargetLayers Parse(JToken token, string path)
    {
        if (token == null) return None;
        if (!(token is JArray array)) throw new FormatException(path + ": an array of layer names is required.");
        var layers = new int[array.Count];
        for (int i = 0; i < array.Count; i++)
        {
            if (array[i].Type != JTokenType.String) throw new FormatException(path + "[" + i + "]: a layer name string is required.");
            layers[i] = PNLayers.Index(array[i].Value<string>(), path + "[" + i + "]");
            for (int j = 0; j < i; j++) if (layers[j] == layers[i]) throw new FormatException(path + "[" + i + "]: duplicate layer \"" + array[i].Value<string>() + "\".");
        }
        return Of(layers);
    }

    // This first, then the layers of other that are not in this, in other's order. Returns this or other when the result equals one of them (no allocation).
    public PNTargetLayers Union(PNTargetLayers other)
    {
        if (other == null || other.Mask == 0 || (other.Mask & ~Mask) == 0) return this;
        if (Mask == 0) return other;
        var merged = new List<int>(Order);
        foreach (var layer in other.Order) if (!PNLayers.Contains(Mask, layer)) merged.Add(layer);
        return Of(merged.ToArray());
    }

    public bool Contains(int layer) => PNLayers.Contains(Mask, layer);
}
