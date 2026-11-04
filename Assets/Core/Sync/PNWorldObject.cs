#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// EN: An authored object of a world that the state-and-command layer can address (Docs/NetworkStatePlan.md N1): a switch, a gate, a chest, a door, the controller of a room.
//     It is NOT an entity: no loader, no stat, no shell on the network. Its identity is the world instance + incarnation + content slot + this key, so the host and a client that
//     loaded the same map name the same object, and two worlds built from one map never mix. The key is authored text (lower case letters, digits, '_', '.', '-', at most 64)
//     and must be unique inside its content slot; a missing or repeated key makes the world refuse to load.
// VI: Object đặt sẵn của world mà lớp đồng bộ có thể nhắm tới. KHÔNG phải entity. Danh tính = world instance + incarnation + content slot + khoá này.
// EN: A component on a world object that wants to know when the object became addressable (so it can register its state blocks) and when it stops being.
public interface IPNWorldObjectListener
{
    void OnBound(PNSyncService sync, PNSyncTarget target);
    void OnUnbound();
}

[DisallowMultipleComponent]
public sealed class PNWorldObject : MonoBehaviour
{
    [SerializeField] private string key = "";
    public string Key => key;
    // Set when the world became ready and the object was made addressable; default (no target) before and after.
    public PNSyncTarget Target { get; private set; }
    public bool IsBound { get; private set; }

    public void Configure(string value) { key = PNSyncKey.Require(value, "A world object key"); }
    public void Validate() { PNSyncKey.Require(key, "World object key of " + name); }

    // EN: The keys of one content slot: every object has a valid key and no two objects share one.
    public static void ValidateSet(IEnumerable<PNWorldObject> objects)
    {
        if (objects == null) throw new ArgumentNullException(nameof(objects));
        var seen = new HashSet<string>();
        foreach (var item in objects)
        {
            if (item == null) throw new InvalidOperationException("Missing world object component.");
            item.Validate();
            if (!seen.Add(item.key)) throw new InvalidOperationException("Duplicate world object key '" + item.key + "' in one content slot.");
        }
    }

    public void Bind(PNSyncTarget target) { Target = target; IsBound = true; }
    public void Unbind() { Target = default; IsBound = false; }
}
#endif
