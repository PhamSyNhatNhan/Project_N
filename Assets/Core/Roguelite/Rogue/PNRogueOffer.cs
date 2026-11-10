using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

// EN: Which items may be offered and how three are drawn (Docs/RogueBuffPlan.md section 4). Pure: the same set, catalog and random stream give the same offer on every machine, which is why a
//     continued run offers the same three again without saving them. `allows` is the game's answer to "may an item of this availability appear" (default yes, locked no, forbidden only with the
//     testing switch); Core does not know the switch. `weightFactor` is the hook for rate-raising items (1 = unchanged, 0 = never drawn).
// VI: Mục nào được đề xuất và cách rút. Thuần: cùng tập, catalog, luồng ngẫu nhiên thì cùng kết quả trên mọi máy, nên tiếp tục run ra lại đúng ba mục đó mà không cần lưu.
public static class PNRogueOffer
{
    // Everything that may be offered now, sorted by id: not owned; a Lodestar only while its constellation has none; a Spark only when the Lodestar it requires is owned; the item and its
    // constellation both allowed to appear. `exclude` removes ids already in play (the other slots of a shop).
    public static List<PNRogueItemDefinition> Eligible(PNRogueCatalog catalog, PNRogueSet set, Func<PNRogueAvailability, bool> allows, IEnumerable<EveId> exclude = null)
    {
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));
        if (set == null) throw new ArgumentNullException(nameof(set));
        if (allows == null) throw new ArgumentNullException(nameof(allows));
        var skipped = new HashSet<EveId>(exclude ?? Enumerable.Empty<EveId>());
        var lodestars = new HashSet<EveId>(set.Items.OfType<PNRogueLodestarDefinition>().Select(x => x.Constellation));
        var owned = new HashSet<EveId>(set.Items.Select(x => x.Id));
        var result = new List<PNRogueItemDefinition>();
        foreach (var item in catalog.Items)
        {
            if (owned.Contains(item.Id) || skipped.Contains(item.Id)) continue;
            if (!allows(item.Availability) || !allows(catalog.Constellation(item.Constellation).Availability)) continue;
            if (item is PNRogueLodestarDefinition && lodestars.Contains(item.Constellation)) continue;
            if (item is PNRogueSparkDefinition spark && spark.Requires.HasValue && !owned.Contains(spark.Requires.Value)) continue;
            result.Add(item);
        }
        return result;
    }

    // Draws up to `count` distinct items, each draw proportional to the weights that are left; fewer when fewer qualify, none when none do. Uses only the Reward stream.
    public static List<PNRogueItemDefinition> Draw(IReadOnlyList<PNRogueItemDefinition> eligible, int count, PNRunRandom random, Func<PNRogueItemDefinition, double> weightFactor = null)
    {
        if (eligible == null) throw new ArgumentNullException(nameof(eligible));
        if (random == null) throw new ArgumentNullException(nameof(random));
        if (count < 1 || count > PNRogueLimits.MaxOffer) throw new ArgumentOutOfRangeException(nameof(count));
        var pool = new List<KeyValuePair<PNRogueItemDefinition, double>>();
        foreach (var item in eligible)
        {
            double factor = weightFactor == null ? 1d : weightFactor(item);
            if (double.IsNaN(factor) || double.IsInfinity(factor) || factor < 0) throw new ArgumentException("A weight factor is a finite number from 0.", nameof(weightFactor));
            double weight = item.Weight * factor;
            if (weight > 0) pool.Add(new KeyValuePair<PNRogueItemDefinition, double>(item, weight));
        }
        var result = new List<PNRogueItemDefinition>();
        while (result.Count < count && pool.Count > 0)
        {
            double total = 0; foreach (var entry in pool) total += entry.Value;
            double roll = random.NextDouble(PNRunStream.Reward) * total; int pick = pool.Count - 1;
            for (int i = 0; i < pool.Count; i++) { roll -= pool[i].Value; if (roll < 0) { pick = i; break; } }
            result.Add(pool[pick].Key); pool.RemoveAt(pick);
        }
        return result;
    }
}
