#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;

// Player prefabs by key. A character names its prefab with `data.playable.prefab`; the spawn point's own prefab stays the default for characters
// that name none (Eve). The asset lives at Resources/PNCharacters/PNPlayerPrefabs so the host and every client resolve the same prefab for the
// character an actor carries.
public sealed class PNPlayerPrefabCatalog : ScriptableObject
{
    [Serializable] public struct Entry { public string Key; public GameObject Prefab; }
    public const string ResourcePath = "PNCharacters/PNPlayerPrefabs";
    public Entry[] Entries = new Entry[0];

    public GameObject Find(string key)
    {
        foreach (var entry in Entries) if (entry.Key == key && entry.Prefab != null) return entry.Prefab;
        return null;
    }

    // The prefab a player of `character` spawns from: the character's own when it names one, else `fallback` (the spawn point's prefab).
    public static GameObject For(EveSessionContext session, string character, GameObject fallback)
    {
        if (string.IsNullOrEmpty(character)) return fallback;
        var playable = PNPlayableSettings.Parse(session.Registry.Characters.Get(EveId.Parse(character)).Data);
        if (playable == null || playable.Prefab.Length == 0) return fallback;
        var prefab = Resources.Load<PNPlayerPrefabCatalog>(ResourcePath)?.Find(playable.Prefab);
        if (prefab == null) throw new InvalidOperationException("Character " + character + " names prefab '" + playable.Prefab + "' which the player prefab catalog does not hold.");
        if (prefab.GetComponent<EveEntityLoader>() == null || prefab.GetComponent<EveEntityIdentity>() == null) throw new InvalidOperationException("Player prefab '" + playable.Prefab + "' needs an entity loader and identity.");
        return prefab;
    }
}
#endif
