#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;

// EN: Why a summon's owner is gone.
// VI: Vì sao chủ của summon không còn.
public enum PNSummonOwnerGone { Unloaded, Died, ChangedWorld }

// EN: A behavior on a summon implements it to decide what happens when the owner is gone. Returning true keeps the summon (it then removes itself through
//     PNSummon.Retire when it wants to); no component keeping it means the default: retire.
// VI: Hành vi trên summon cài đặt để tự quyết khi chủ biến mất. true = giữ lại; không ai giữ thì mặc định gỡ.
public interface IPNSummonOwnerGone
{
    bool KeepWhenOwnerGone(PNSummonOwnerGone reason);
}

// EN: The light component of a summon (Docs/SummonPlan.md): who summoned it, the owner's combat source captured once at summon time, its limit group and its
//     creation order. It holds no behavior and no stats; what a summon IS and DOES is its own character and components. Only the machine with authority
//     watches the owner; every summon is an entity spawned through an IPNEntityHost, so removing it is host.Retire.
// VI: Component nhẹ của summon: ai triệu hồi, nguồn chiến đấu của chủ chụp một lần, nhóm giới hạn và thứ tự tạo. Không có hành vi hay stat.
[DisallowMultipleComponent]
public sealed class PNSummon : MonoBehaviour
{
    public EveEntityId Summoner { get; private set; }
    // The owner's combat source as it was when the summon was created: immutable, survives the owner, also what kills are credited to.
    public PNCombatSource OwnerSource { get; private set; }
    // Limit group chosen by the summoning skill ("" = none); entities of the same summoner and group are counted together.
    public string Group { get; private set; }
    // Creation order across the whole session (smaller = older).
    public long Sequence { get; private set; }
    // The owner's summon_scale at creation (1 = unchanged). Begin multiplies the object's local scale by it and Clear gives the scale back, so a pooled object keeps its prefab size.
    public double Scale { get; private set; } = 1d;
    private Vector3 baseLocalScale = Vector3.one;
    public bool IsActive { get; private set; }
    private bool ownerGoneHandled, retiring;
    // True once it is being removed: it no longer counts toward a limit.
    public bool IsRetiring => retiring;

    // Called while the object is inactive (the creator's configure step).
    public void Begin(PNCombatSource ownerSource, EveEntityId summoner, string group, long sequence, double scale)
    {
        if (IsActive) throw new InvalidOperationException("This summon is already active.");
        if (ownerSource == null) throw new ArgumentNullException(nameof(ownerSource));
        if (!summoner.IsValid) throw new ArgumentException("The summoner needs a valid entity id.", nameof(summoner));
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0d) throw new ArgumentOutOfRangeException(nameof(scale));
        OwnerSource = ownerSource; Summoner = summoner; Group = group ?? ""; Sequence = sequence; Scale = scale;
        ownerGoneHandled = false; retiring = false; IsActive = true;
        baseLocalScale = transform.localScale; transform.localScale = baseLocalScale * (float)scale;
        PNSummons.Add(this);
    }
    // Pooled object returned (factory Return) or disabled: forget the owner so a reused object starts clean.
    public void Clear()
    {
        if (!IsActive) return;
        PNSummons.Remove(this);
        transform.localScale = baseLocalScale;
        OwnerSource = null; Summoner = default; Group = ""; Sequence = 0; Scale = 1d; IsActive = false; ownerGoneHandled = false; retiring = false;
    }
    private void OnDisable() => Clear();
    private void OnDestroy() => Clear();

    // Removes this summon through the host of its session. False when it is already being removed.
    public bool Retire()
    {
        if (!IsActive || retiring || !EveRegistrySession.IsReady) return false;
        var loader = GetComponent<EveEntityLoader>();
        var host = PNEntityHosts.Find(EveRegistrySession.Context);
        if (loader == null || host == null) return false;
        retiring = true;
        return host.Retire(loader);
    }

    // The reason the owner is gone, or null while it is present and alive in the summon's world.
    public PNSummonOwnerGone? OwnerState()
    {
        var owner = OwnerSource?.LoadedSource;
        if (owner == null) return PNSummonOwnerGone.Unloaded;
        if (owner.IsDead) return PNSummonOwnerGone.Died;
        var mine = GetComponent<EveEntityIdentity>(); var theirs = owner.GetComponent<EveEntityIdentity>();
        if (mine == null || theirs == null || !ReferenceEquals(mine.World, theirs.World)) return PNSummonOwnerGone.ChangedWorld;
        return null;
    }

    private void Update() => Tick();
    // One check of the owner (Update calls it every frame; a fixture without a player loop calls it by hand).
    public void Tick()
    {
        if (!IsActive || retiring || ownerGoneHandled || !EveRegistrySession.IsReady || !EveRegistrySession.Context.HasAuthority) return;
        var loader = GetComponent<EveEntityLoader>();
        if (loader == null || !loader.IsReady) return;
        var gone = OwnerState();
        if (!gone.HasValue) return;
        ownerGoneHandled = true;
        foreach (var hook in GetComponents<IPNSummonOwnerGone>()) if (hook.KeepWhenOwnerGone(gone.Value)) return;
        Retire();
    }
}
#endif
