#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EveEngine;
using UnityEngine;

// EN: Anything that owns its own clock instead of being an entity (a projectile, an area, later a blast) and can be slowed or stopped by an effect.
//     It registers in PNTimeTargets of its world; a time effect asks the list by shape and layer, then puts owned modifiers on the clock. Entities are
//     not here: they take their time effects through a Buff (PNTimeScaleArea.Poll).
// VI: Mọi thứ có đồng hồ riêng mà không phải entity (đạn, vùng, sau này là blast) và có thể bị làm chậm hoặc dừng. Đăng ký vào PNTimeTargets của world;
//     hiệu ứng thời gian hỏi danh sách theo hình và layer rồi đặt modifier có chủ lên đồng hồ. Entity không ở đây: chúng nhận qua Buff.
public interface IPNTimeTarget
{
    EveTimeScale TimeClock { get; }
    // A gameplay layer of PNLayers (Projectile, Blast, Area), the one a time effect's `targetLayers` selects. It is not the GameObject's layer.
    int TimeLayer { get; }
    Vector3 TimePosition { get; }
    // False once the object ended or is not bound: the registry never hands it out.
    bool TimeActive { get; }
}

public sealed class PNTimeTargets
{
    // One registration. Id is unique for the process, so a pooled object that registers again is a new entry (an effect can tell it from the old life).
    public sealed class Entry
    {
        public long Id { get; internal set; }
        public IPNTimeTarget Target { get; internal set; }
        internal int Index = -1; internal PNTimeTargets Owner;
        public bool IsRegistered => Owner != null;
    }
    private static readonly ConditionalWeakTable<EveWorldContext, PNTimeTargets> byWorld = new ConditionalWeakTable<EveWorldContext, PNTimeTargets>();
    private static readonly PNTimeTargets withoutWorld = new PNTimeTargets();
    private static long sequence;
    private readonly List<Entry> entries = new List<Entry>();
    public int Count => entries.Count;

    // The list of a world (the shared list when the object has no world, as in a bare test scene).
    public static PNTimeTargets For(EveWorldContext world) => world == null ? withoutWorld : byWorld.GetValue(world, _ => new PNTimeTargets());

    public Entry Register(IPNTimeTarget target)
    {
        if (target == null) throw new System.ArgumentNullException(nameof(target));
        var entry = new Entry { Id = ++sequence, Target = target, Owner = this, Index = entries.Count };
        entries.Add(entry); return entry;
    }
    // Idempotent: a second call, or a call for an entry of another list, does nothing.
    public static void Unregister(Entry entry)
    {
        var owner = entry?.Owner; if (owner == null) return;
        var list = owner.entries; int index = entry.Index; var last = list[list.Count - 1];
        list[index] = last; last.Index = index; list.RemoveAt(list.Count - 1);
        entry.Owner = null; entry.Index = -1; entry.Target = null;
    }
    // Appends the registered, active targets whose position is within `radius` of `center` and whose layer is in `layerMask` (a PNLayers bit mask).
    // Allocation-free; the caller filters further (a layer rule, an exclude mode).
    public int Query(Vector3 center, float radius, int layerMask, List<Entry> results)
    {
        if (results == null) throw new System.ArgumentNullException(nameof(results));
        float limit = radius * radius; int added = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i]; var target = entry.Target;
            if (target == null || !target.TimeActive || !PNLayers.Contains(layerMask, target.TimeLayer)) continue;
            var clock = target.TimeClock; if (clock == null || !clock.isActiveAndEnabled) continue;
            if ((target.TimePosition - center).sqrMagnitude > limit) continue;
            results.Add(entry); added++;
        }
        return added;
    }
}

// EN: The clock of an area (a standard EveTimeScale, minScale 0) driven by the area manager, like PNProjectileClock; its own Update must not advance it.
public sealed class PNAreaClock : EveTimeScale
{
    protected override void Update() { }
}

// EN: The clock of a blast (a standard EveTimeScale, minScale 0) driven by the blast manager, like PNAreaClock; its own Update must not advance it.
public sealed class PNBlastClock : EveTimeScale
{
    protected override void Update() { }
}
#endif
