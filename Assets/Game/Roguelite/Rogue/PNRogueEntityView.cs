#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// One item's HUD numbers as every machine reads them (ids only, no text).
public readonly struct PNRogueHudItem
{
    public readonly string Id;
    public readonly PNRogueDisplay Display;
    public PNRogueHudItem(string id, PNRogueDisplay display) { Id = id; Display = display; }
}

// EN: The Roguelite Buff numbers of ONE player entity as every machine sees them (Docs/RogueBuffPlan.md section 7): the Entity sync block `project_n:rogue_view`. The authority writes it from the manager's
//     `PNRogueRuntime` (the items whose `Display` is not empty, in build order) when a number changed; every machine, the host and an offline player too, reads it back through the same sink, so the HUD never
//     looks at the runtime and a client sees the numbers of the entity it owns. It is attached next to `PNRogueBuffManager` and `PNSyncEntity` (which binds the entity's id + generation: a rebuilt entity is a
//     new life, so numbers of an old floor never reach the new one). Not a trust boundary: the numbers are display only.
// VI: Số hiển thị của Roguelite Buff trên MỘT thực thể người chơi, như mọi máy thấy: khối Entity `project_n:rogue_view`. Authority ghi từ runtime của manager khi số đổi; mọi máy đọc lại qua cùng sink nên HUD
//     không nhìn runtime. Gắn cạnh manager và `PNSyncEntity`.
[DisallowMultipleComponent]
public sealed class PNRogueEntityView : MonoBehaviour, IPNSyncEntityListener, IPNSyncStateSource, IPNSyncStateSink
{
    public static readonly EveId State = EveId.Parse("project_n:rogue_view");
    public const int StateBytes = 1024;
    private static readonly PNRogueHudItem[] None = new PNRogueHudItem[0];
    private PNSyncService sync;
    private PNSyncTarget target;
    private PNRogueBuffManager manager;
    private readonly List<IDisposable> leases = new List<IDisposable>();
    public IReadOnlyList<PNRogueHudItem> Items { get; private set; } = None;
    public bool IsBound { get; private set; }
    public EveId StateId => State;

    public void OnBound(PNSyncService service, PNSyncTarget value)
    {
        sync = service; target = value; IsBound = true; Items = None; manager = GetComponent<PNRogueBuffManager>();
        if (service.IsAuthority) leases.Add(service.RegisterSource(value, this));
        leases.Add(service.RegisterSink(value, this));
        if (service.IsAuthority) service.MarkDirty(value, State);
    }

    public void OnUnbound()
    {
        foreach (var lease in leases) lease.Dispose();
        leases.Clear(); IsBound = false; sync = null; manager = null; Items = None;
    }

    // The manager calls this after a tick in which a number changed.
    internal void Touch() { if (IsBound && sync != null && sync.IsAuthority) sync.MarkDirty(target, State); }

    public JToken Capture()
    {
        var items = new JArray(); var runtime = manager != null ? manager.Runtime : null;
        if (runtime != null)
            foreach (var entry in runtime.Entries)
            {
                var d = entry.Logic != null ? entry.Logic.Display : default;
                if (entry.Logic == null || d.IsEmpty) continue;
                var o = new JObject { ["id"] = entry.Definition.Id.ToString() };
                if (d.TopLeft.HasValue) o["tl"] = d.TopLeft.Value; if (d.TopRight.HasValue) o["tr"] = d.TopRight.Value;
                if (d.BottomLeft.HasValue) o["bl"] = d.BottomLeft.Value; if (d.BottomRight.HasValue) o["br"] = d.BottomRight.Value;
                items.Add(o);
            }
        return new JObject { ["items"] = items };
    }

    public void Apply(JToken value, ulong revision)
    {
        var list = new List<PNRogueHudItem>();
        if (value is JObject o && o["items"] is JArray items)
            foreach (var x in items)
            {
                string id = (string)x["id"]; if (string.IsNullOrEmpty(id)) continue;
                list.Add(new PNRogueHudItem(id, new PNRogueDisplay(Number(x["tl"]), Number(x["tr"]), Number(x["bl"]), Number(x["br"]))));
            }
        Items = list.Count == 0 ? None : list.ToArray();
    }

    private static long? Number(JToken token) => token != null && token.Type == JTokenType.Integer ? (long?)token : null;
}
#endif
