using System;
using System.Collections.Generic;

// EN: Targeting by layer. TargetLayers = the layers this entity attacks and targets: the stat class default or the character's combat.targetLayers,
//     plus layers Buffs add with counted leases (a Buff releases only what it added). There are no teams: an attack hits a layer when the attacker's
//     TargetLayers (plus the attack's own targetLayers) contain it. Asymmetric; not replicated (the host resolves hits and damage).
// VI: Nhắm mục tiêu theo layer. TargetLayers = các layer entity này đánh/nhắm tới: mặc định theo lớp stat hoặc combat.targetLayers của character,
//     cộng các layer Buff thêm bằng lease có đếm (Buff chỉ nhả cái nó thêm). Không còn phe: đòn trúng một layer khi TargetLayers của người đánh
//     (cộng targetLayers của đòn) chứa nó. Bất đối xứng; không nhân bản (host xử lý trúng và sát thương).
public partial class PNStat
{
    // Subclasses set the class default (Player -> Enemy, Enemy -> Player); the base targets nothing.
    protected virtual PNTargetLayers DefaultTargetLayers => PNTargetLayers.None;
    private PNTargetLayers baseTargetLayers = PNTargetLayers.None, effectiveTargetLayers = PNTargetLayers.None;
    private int[] grantedCounts;                       // per-layer lease counts, allocated on the first grant
    private List<int> grantedOrder;                    // layers with a count above zero, in first-grant order
    private int targetGeneration;
    // Cached: rebuilt only when the base or a lease changes, never per read.
    public PNTargetLayers TargetLayers => effectiveTargetLayers;
    public int Layer => gameObject.layer;
    // True when the other entity's layer is in my TargetLayers (not myself, not null).
    public bool Targets(PNStat other) => other != null && !ReferenceEquals(other, this) && effectiveTargetLayers.Contains(other.Layer);
    // True when the other entity is on my layer (an ally for pack / support behaviour).
    public bool SharesLayer(PNStat other) => other != null && !ReferenceEquals(other, this) && other.Layer == Layer;

    private void SetBaseTargetLayers(PNTargetLayers layers) { baseTargetLayers = layers ?? PNTargetLayers.None; RebuildTargetLayers(); }
    private void RebuildTargetLayers()
    {
        var granted = PNTargetLayers.None;
        if (grantedOrder != null && grantedOrder.Count > 0) granted = PNTargetLayers.Of(grantedOrder.ToArray());
        effectiveTargetLayers = baseTargetLayers.Union(granted);
    }
    private void ResetTargetLayers()
    {
        grantedCounts = null; grantedOrder = null; targetGeneration++;
        baseTargetLayers = PNTargetLayers.None; effectiveTargetLayers = PNTargetLayers.None;
    }

    // Adds layers for as long as the returned lease lives. Disposing is idempotent; two grants of one layer keep it until both end.
    // Layers that were not already targeted go after the base layers, in first-grant order.
    public IDisposable GrantTargetLayers(PNTargetLayers layers)
    {
        if (layers == null) throw new ArgumentNullException(nameof(layers));
        if (layers.IsEmpty) return TargetLayerLease.Empty;
        if (grantedCounts == null) { grantedCounts = new int[32]; grantedOrder = new List<int>(); }
        foreach (var layer in layers.Order)
        {
            if (grantedCounts[layer]++ == 0) grantedOrder.Add(layer);
        }
        RebuildTargetLayers();
        return new TargetLayerLease(this, layers, targetGeneration);
    }

    private void ReleaseTargetLayers(PNTargetLayers layers, int generation)
    {
        if (generation != targetGeneration || grantedCounts == null) return;
        foreach (var layer in layers.Order)
        {
            if (--grantedCounts[layer] == 0) grantedOrder.Remove(layer);
        }
        RebuildTargetLayers();
    }

    private sealed class TargetLayerLease : IDisposable
    {
        public static readonly TargetLayerLease Empty = new TargetLayerLease(null, null, 0);
        private PNStat stat; private readonly PNTargetLayers layers; private readonly int generation;
        public TargetLayerLease(PNStat stat, PNTargetLayers layers, int generation) { this.stat = stat; this.layers = layers; this.generation = generation; }
        public void Dispose()
        {
            var owner = stat; stat = null;
            if (owner != null) owner.ReleaseTargetLayers(layers, generation);
        }
    }
}
