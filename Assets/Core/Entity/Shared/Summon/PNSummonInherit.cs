#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;

// EN: The inheritance step of a summon (Docs/SpawnedObjectSyncPlan.md 4.6 item 4). A summon uses only its own stats unless this component is on its prefab; with it, the owner's combat
//     snapshot taken when the summon was made (PNSummon.OwnerSource.Snapshot, never the live owner) gives the summon, once, at load:
//       - every non-resource base stat the summon itself declares (attack power, crit rate, crit damage, move speed...) takes the owner's final value, as one flat modifier;
//       - every modifier that is not on a stat (damage tags such as projectile_damage, scale tags, penetration, elements) is copied.
//     Modifiers that sit on a stat are not copied: the owner's final value above already contains them (copying them too would count them twice). Resource stats (health) never carry over.
//     A copied modifier the summon cannot use (its calculation or tag is not registered here) is skipped, not an error. All of it is one source, `project_n:summon_inherit`.
//     It runs after the Stat and before Ability / Buff / replication read the stats; with no owner snapshot (a client, a spawn that bypassed the summoning skill) it does nothing.
//     A summon with special rules writes its own step in place of this one.
// VI: Bước kế thừa của summon: mặc định summon chỉ dùng chỉ số của nó; có component này thì lúc nạp, một lần, nó nhận từ snapshot của chủ: giá trị cuối của mọi chỉ số nền (không phải resource) mà
//     summon tự khai báo, và mọi modifier không nằm trên chỉ số (tag sát thương, scale, penetration, nguyên tố). Không chép modifier nằm trên chỉ số vì giá trị cuối đã chứa chúng.
[DisallowMultipleComponent]
public sealed class PNSummonInherit : MonoBehaviour, EveIEntityLoadStep
{
    public static readonly EveId SourceId = EveId.Parse("project_n:summon_inherit");
    private static readonly EveId TrialId = EveId.Parse("project_n:summon_inherit_trial");
    private PNStat stat; private bool applied;
    // How many modifiers the step installed (0 when nothing was inherited).
    public int Count { get; private set; }
    public int LoadOrder => -1;                                           // before Ability (0), Buff (10) and PNStatReplication (11); the dependency below still loads the Stat first
    public IEnumerable<Type> Dependencies => new[] { typeof(PNStat) };

    public void Load(EveEntityLoadContext context)
    {
        stat = GetComponent<PNStat>(); Count = 0; applied = false;
        var snapshot = GetComponent<PNSummon>()?.OwnerSource?.Snapshot;
        if (stat == null || snapshot == null) return;
        var modifiers = Build(stat, snapshot);
        if (modifiers.Count == 0) return;
        stat.SetSource(SourceId, modifiers); applied = true; Count = modifiers.Count;
    }
    public void Unload()
    {
        try { if (applied && stat != null) stat.RemoveSource(SourceId); }
        finally { applied = false; stat = null; Count = 0; }
    }

    // What the summon takes from the owner's snapshot (a pure function of the two, so a fixture can call it).
    public static List<EveStatModifier> Build(PNStat summon, PNCombatSourceSnapshot owner)
    {
        if (summon == null) throw new ArgumentNullException(nameof(summon)); if (owner == null) throw new ArgumentNullException(nameof(owner));
        var result = new List<EveStatModifier>(); var set = summon.Set;
        foreach (var pair in set.BaseSnapshot())
        {
            if (pair.Value is EveResourceStat || !owner.TryEvaluate(pair.Key, out double ownerValue)) continue;
            double delta = ownerValue - set.Evaluate(pair.Key);
            if (!double.IsNaN(delta) && !double.IsInfinity(delta) && delta != 0d) result.Add(new EveStatModifier(pair.Key, delta, 0));
        }
        foreach (var modifier in owner.ExportModifiers(m => !owner.IsStat(m.Id)))
            if (Usable(summon, modifier)) result.Add(modifier);
        return result;
    }
    // Asks the summon's own stat set whether it accepts the modifier, by trying it as a source and taking it away again.
    private static bool Usable(PNStat summon, EveStatModifier modifier)
    {
        try { summon.SetSource(TrialId, new[] { modifier }); return true; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        finally { summon.RemoveSource(TrialId); }
    }
}
#endif
