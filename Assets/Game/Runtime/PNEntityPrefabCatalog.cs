#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EveEngine;
using UnityEngine;

// Entity prefabs by key (Docs/EntitySpawnPlan.md section 3.4). A character names its prefab with `data.body.prefab` (or the older
// `data.playable.prefab`) and an entity definition may override it; the spawn point's own prefab stays the default for characters that name none
// (Eve). The asset lives at Resources/PNEntities/PNEntityPrefabs so the host and every client resolve the same prefab. Keys are namespaced
// ("project_n:eve_2"); a key without a namespace is read as project_n:. Mods add their own through PNEntityPrefabs.Register.
public sealed class PNEntityPrefabCatalog : ScriptableObject
{
    [Serializable] public struct Entry { public string Key; public GameObject Prefab; }
    public const string ResourcePath = "PNEntities/PNEntityPrefabs";
    public Entry[] Entries = new Entry[0];

    public GameObject Find(string key)
    {
        key = PNBodySettings.NormalizeKey(key);
        foreach (var entry in Entries) if (!string.IsNullOrEmpty(entry.Key) && PNBodySettings.NormalizeKey(entry.Key) == key && entry.Prefab != null) return entry.Prefab;
        return null;
    }
}

// The one place that turns a key into a prefab: the catalog asset plus mod registrations. Static because the catalog is an asset and mods register
// while the content is being built; mod entries are cleared each time the gameplay module registers (a new content build).
public sealed class PNEntityPrefabs : IPNEntityPrefabSource
{
    public static readonly PNEntityPrefabs Source = new PNEntityPrefabs();
    private static readonly Dictionary<string, GameObject> mods = new Dictionary<string, GameObject>(StringComparer.Ordinal);
    private static PNEntityPrefabCatalog catalog;
    // Tests replace the catalog; null = the Resources asset.
    public static PNEntityPrefabCatalog CatalogOverride { get; set; }

    private static PNEntityPrefabCatalog Catalog => CatalogOverride != null ? CatalogOverride : catalog != null ? catalog : catalog = Resources.Load<PNEntityPrefabCatalog>(PNEntityPrefabCatalog.ResourcePath);

    internal static void ClearMods() => mods.Clear();

    // A code mod contributes a prefab under its own namespace ("mymod:golem"). Duplicates and keys of another namespace (including project_n) are rejected.
    public static void Register(string key, GameObject prefab, string ownerNamespace)
    {
        if (prefab == null) throw new ArgumentNullException(nameof(prefab));
        if (string.IsNullOrEmpty(ownerNamespace) || ownerNamespace == "project_n" || ownerNamespace == "base") throw new ArgumentException("Only a mod namespace can register entity prefabs.");
        PNBodySettings.ValidateKey(key, "entity prefab");
        if (key.IndexOf(':') < 0 || !key.StartsWith(ownerNamespace + ":", StringComparison.Ordinal)) throw new ArgumentException("A mod registers prefab keys inside its own namespace: " + ownerNamespace + ":name.");
        if (mods.ContainsKey(key) || Catalog?.Find(key) != null) throw new InvalidOperationException("Entity prefab key already registered: " + key);
        Require(prefab, key);
        mods.Add(key, prefab);
    }

    public static bool TryFind(string key, out GameObject prefab)
    {
        key = PNBodySettings.NormalizeKey(key);
        if (mods.TryGetValue(key, out prefab)) return true;
        prefab = Catalog?.Find(key);
        return prefab != null;
    }

    private static void Require(GameObject prefab, string key)
    {
        if (prefab.GetComponent<EveEntityLoader>() == null || prefab.GetComponent<EveEntityIdentity>() == null)
            throw new InvalidOperationException("Entity prefab '" + key + "' needs an entity loader and identity.");
    }

    // The prefab an entity of `character` spawns from: the entity's own key, else the character's, else `fallback` (the spawn point's prefab).
    public static GameObject For(EveSessionContext session, string character, GameObject fallback, PNEntityDefinition entity = null)
    {
        string key = entity != null && entity.Settings.Prefab.Length > 0 ? PNBodySettings.NormalizeKey(entity.Settings.Prefab) : "";
        if (key.Length == 0 && !string.IsNullOrEmpty(character))
        {
            var data = session.Registry.Characters.Get(EveId.Parse(character)).Data;
            key = PNBodySettings.Parse(data).EffectivePrefab(PNPlayableSettings.Parse(data));
        }
        if (key.Length == 0) return fallback;
        if (!TryFind(key, out var prefab)) throw new InvalidOperationException("Prefab '" + key + "' is not in the entity prefab catalog.");
        Require(prefab, key);
        return prefab;
    }

    public bool Has(string key) => TryFind(key, out _);
    public string Fingerprint(string key)
    {
        if (!TryFind(key, out var prefab)) throw new InvalidOperationException("Prefab '" + key + "' is not in the entity prefab catalog.");
        return Fingerprint(prefab);
    }

    // Only what changes gameplay: the child tree (paths), the component types, layers, collider shapes and Rigidbody settings. Not meshes, materials,
    // animators or other serialized values (Unity references serialize to instance IDs that differ between machines).
    public static string Fingerprint(GameObject prefab)
    {
        var text = new StringBuilder();
        Walk(prefab.transform, "", text);
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
    }
    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string V(Vector3 value) => F(value.x) + "," + F(value.y) + "," + F(value.z);
    private static void Walk(Transform node, string path, StringBuilder text)
    {
        var go = node.gameObject;
        text.Append(path).Append('/').Append(go.name).Append("|L").Append(go.layer);
        foreach (var component in go.GetComponents<Component>())
        {
            if (component == null) { text.Append("|missing"); continue; }
            text.Append('|').Append(component.GetType().FullName);
            switch (component)
            {
                case BoxCollider box: text.Append('[').Append(V(box.size)).Append(';').Append(V(box.center)).Append(';').Append(box.isTrigger).Append(']'); break;
                case SphereCollider sphere: text.Append('[').Append(F(sphere.radius)).Append(';').Append(V(sphere.center)).Append(';').Append(sphere.isTrigger).Append(']'); break;
                case CapsuleCollider capsule: text.Append('[').Append(F(capsule.radius)).Append(';').Append(F(capsule.height)).Append(';').Append(capsule.direction).Append(';').Append(V(capsule.center)).Append(';').Append(capsule.isTrigger).Append(']'); break;
                case Collider other: text.Append('[').Append(other.isTrigger).Append(']'); break;
                case Rigidbody body: text.Append('[').Append(body.isKinematic).Append(';').Append((int)body.constraints).Append(']'); break;
            }
        }
        text.Append('\n');
        for (int i = 0; i < node.childCount; i++) Walk(node.GetChild(i), path + "/" + go.name, text);
    }
}
#endif
