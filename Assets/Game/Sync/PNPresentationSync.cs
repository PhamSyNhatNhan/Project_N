#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: G8. The Entity block `project_n:presentation`, sent to every client: the authority's whole `PNSkill.Presentation` picture (the last value of every key). It is state, not an event, so a client that
//     joins late gets the current picture with its first image. A client applies it to its own `Presentation` (which raises `Changed` for each difference); the authority never reads it back.
// VI: Khối Entity `project_n:presentation` gửi cho mọi client: cả bức tranh `Presentation` của authority (giá trị cuối của từng khóa). Là trạng thái, không phải sự kiện, nên người vào muộn nhận ngay ảnh đầu tiên.
[DisallowMultipleComponent]
public sealed class PNPresentationSync : MonoBehaviour, IPNSyncEntityListener, IPNSyncStateSource, IPNSyncStateSink
{
    public static readonly EveId State = EveId.Parse("project_n:presentation");
    public const int StateBytes = 512;
    private PNSyncService sync;
    private PNSyncTarget target;
    private PNSkill skill;
    private readonly List<IDisposable> leases = new List<IDisposable>();
    public bool IsBound { get; private set; }
    public EveId StateId => State;
    public int AppliedCount { get; private set; }

    public void OnBound(PNSyncService service, PNSyncTarget value)
    {
        sync = service; target = value; IsBound = true; AppliedCount = 0; skill = GetComponent<PNSkill>();
        if (service.IsAuthority)
        {
            leases.Add(service.RegisterSource(value, this));
            if (skill != null) skill.Presentation.Dirty += OnDirty;
            service.MarkDirty(value, State);
        }
        leases.Add(service.RegisterSink(value, this));
    }

    public void OnUnbound()
    {
        if (skill != null) skill.Presentation.Dirty -= OnDirty;
        foreach (var lease in leases) lease.Dispose();
        leases.Clear(); IsBound = false; sync = null; skill?.Presentation.Reset(); skill = null;
    }

    private void OnDirty() { if (IsBound && sync != null && sync.IsAuthority) sync.MarkDirty(target, State); }

    public JToken Capture()
    {
        var picture = new JObject();
        if (skill != null) foreach (var pair in skill.Presentation.Values) picture[pair.Key] = pair.Value;
        return new JObject { ["v"] = picture };
    }

    // Reads one block; throws FormatException on a malformed value (also used by the checks).
    public static Dictionary<string, string> Parse(JToken value)
    {
        if (!(value is JObject o) || !(o["v"] is JObject picture)) throw new FormatException("Presentation block needs v.");
        if (picture.Count > PNPresentation.MaxEntries) throw new FormatException("Presentation block holds at most " + PNPresentation.MaxEntries + " values.");
        var result = new Dictionary<string, string>();
        foreach (var property in picture.Properties())
        {
            if (!PNPresentation.IsValidKey(property.Name) || property.Value.Type != JTokenType.String) throw new FormatException("Presentation key or value.");
            string text = (string)property.Value;
            if (!PNPresentation.IsValidValue(text) || text.Length == 0) throw new FormatException("Presentation value.");
            result[property.Name] = text;
        }
        return result;
    }

    public void Apply(JToken value, ulong revision)
    {
        if (sync == null || sync.IsAuthority || skill == null) return; // the authority is the source
        var picture = Parse(value);
        skill.Presentation.Apply(picture); AppliedCount++;
    }
}
#endif
