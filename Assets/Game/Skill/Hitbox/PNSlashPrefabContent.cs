using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using EveEngine;
using UnityEngine;

public static class PNSlashPrefabContent
{
    public static void Validate(PNSlashVfxSettings settings, bool combat)
    {
        var prefab = Resources.Load<GameObject>(settings.Prefab);
        if (prefab == null || prefab.GetComponentInChildren<PNArcSlashVisual>(true) == null)
            throw new FormatException("Slash prefab missing visual: " + settings.Prefab);
        if (combat && settings.HasPrefab)
        {
            var definition = prefab.GetComponent<PNSlashPrefab>();
            if (definition == null) throw new FormatException("Combat prefab requires PNSlashPrefab: " + settings.Prefab);
            var tracks = definition.Capture();
            if (tracks.Length > 64) throw new FormatException("Attack allows at most 64 tracks.");
        }
    }
    public static string Fingerprint(PNSlashPrefab prefab)
    {
        var text = new StringBuilder();
        foreach (var track in prefab.Capture())
        {
            text.Append(JsonUtility.ToJson(track));
            text.Append(JsonUtility.ToJson(new Anchor { Position = track.AnchorOffset, Rotation = track.AnchorRotation, LayerMask = track.LayerMask }));
        }
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
    }
    [Serializable] private sealed class Anchor { public Vector3 Position; public Quaternion Rotation; public int LayerMask; }
    public static IReadOnlyDictionary<string, string> Hashes(EveRegistryHub hub)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var skill in hub.Skills.Snapshot().Values)
        {
            if (!(skill.Data is Newtonsoft.Json.Linq.JObject root) || !(root["action"] is Newtonsoft.Json.Linq.JObject)) continue;
            var data = PNSkillData.Parse(root);
            if (!typeof(PNSlashAction).IsAssignableFrom(hub.Actions().Get(data.Action.Id).ImplementationType)) continue;
            string path = new PNSlashVfxSettings(data.Action.Perform).Prefab;
            var prefab = Resources.Load<GameObject>(path)?.GetComponent<PNSlashPrefab>();
            if (prefab != null) hashes[path] = Fingerprint(prefab);
        }
        return hashes;
    }
}
