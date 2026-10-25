using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

// EN: Where entity prefabs come from, as far as content validation and the coop hash are concerned (Docs/EntitySpawnPlan.md section 3.4).
//     The real source (PNEntityPrefabs) reads the catalog asset plus what mods registered; tests pass a fake one.
// VI: Nguồn prefab entity nhìn từ phía kiểm nội dung và băm coop. Nguồn thật đọc catalog cộng phần mod đăng ký; test dùng nguồn giả.
public interface IPNEntityPrefabSource
{
    // Namespaced key ("project_n:eve_2").
    bool Has(string key);
    // Hash of the gameplay structure of the prefab (not its looks); the same prefab gives the same value on every machine.
    string Fingerprint(string key);
}

// The prefab keys content refers to: a character's `body.prefab` (or the older `playable.prefab`) and an entity definition's `prefab`.
public static class PNEntityPrefabReferences
{
    public const string HashPrefix = "entity_prefab:";

    // Key -> who asks for it (for messages), sorted by key.
    public static SortedDictionary<string, string> Collect(EveRegistryHub hub)
    {
        var keys = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var character in hub.Characters.Snapshot().Values)
        {
            string key = PNBodySettings.Parse(character.Data).EffectivePrefab(PNPlayableSettings.Parse(character.Data));
            if (key.Length > 0 && !keys.ContainsKey(key)) keys.Add(key, "character " + character.Id);
        }
        foreach (var entity in hub.Instances().Snapshot().Values.OfType<PNEntityDefinition>())
        {
            string key = entity.Settings.Prefab.Length > 0 ? PNBodySettings.NormalizeKey(entity.Settings.Prefab) : "";
            if (key.Length > 0 && !keys.ContainsKey(key)) keys.Add(key, "entity " + entity.Id);
        }
        return keys;
    }

    // Every referenced key must exist in the source.
    public static void Validate(EveRegistryHub hub, IPNEntityPrefabSource source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        foreach (var pair in Collect(hub))
            if (!source.Has(pair.Key)) throw new FormatException("The " + pair.Value + " names prefab '" + pair.Key + "' which the entity prefab catalog does not hold.");
    }

    // Coop hash entries: one per referenced key. Only referenced prefabs are hashed.
    public static IReadOnlyDictionary<string, string> Hashes(EveRegistryHub hub, IPNEntityPrefabSource source)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in Collect(hub)) hashes.Add(HashPrefix + pair.Key, source.Fingerprint(pair.Key));
        return hashes;
    }
}
