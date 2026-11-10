using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

// EN: What the registry holds for Roguelite Buff, as one read-only index (Docs/RogueBuffPlan.md section 4): constellations, and the Sparks and Lodestars of each (an item points UP to its
//     constellation, so this index is where "what does a constellation contain" is answered). Built once from a sealed registry; everything is sorted by id so a draw is the same on every machine.
// VI: Chỉ mục chỉ đọc của Roguelite Buff trong registry: chòm sao, và Tia sáng/Sao dẫn của từng chòm. Sắp theo id để rút giống nhau trên mọi máy.
public sealed class PNRogueCatalog
{
    private readonly Dictionary<EveId, PNRogueConstellationDefinition> constellations = new Dictionary<EveId, PNRogueConstellationDefinition>();
    private readonly Dictionary<EveId, PNRogueItemDefinition> items = new Dictionary<EveId, PNRogueItemDefinition>();
    private readonly Dictionary<EveId, List<PNRogueItemDefinition>> byConstellation = new Dictionary<EveId, List<PNRogueItemDefinition>>();
    private static readonly IReadOnlyList<PNRogueItemDefinition> none = Array.AsReadOnly(new PNRogueItemDefinition[0]);
    public IReadOnlyList<PNRogueConstellationDefinition> Constellations { get; }
    public IReadOnlyList<PNRogueItemDefinition> Items { get; }

    private PNRogueCatalog(IEnumerable<PNRogueConstellationDefinition> allConstellations, IEnumerable<PNRogueItemDefinition> allItems)
    {
        Constellations = Array.AsReadOnly(allConstellations.OrderBy(x => x.Id.ToString(), StringComparer.Ordinal).ToArray());
        Items = Array.AsReadOnly(allItems.OrderBy(x => x.Id.ToString(), StringComparer.Ordinal).ToArray());
        foreach (var c in Constellations) constellations.Add(c.Id, c);
        foreach (var item in Items)
        {
            if (items.ContainsKey(item.Id)) throw new InvalidOperationException("A Spark and a Lodestar share the id " + item.Id + ".");
            if (!constellations.ContainsKey(item.Constellation)) throw new InvalidOperationException(item.Kind + " " + item.Id + " points at unknown constellation " + item.Constellation + ".");
            items.Add(item.Id, item);
            if (!byConstellation.TryGetValue(item.Constellation, out var list)) byConstellation.Add(item.Constellation, list = new List<PNRogueItemDefinition>());
            list.Add(item);
        }
    }

    public static PNRogueCatalog Build(EveRegistryHub hub)
    {
        if (hub == null) throw new ArgumentNullException(nameof(hub));
        return new PNRogueCatalog(hub.RogueConstellations().Snapshot().Values,
            hub.RogueSparks().Snapshot().Values.Cast<PNRogueItemDefinition>().Concat(hub.RogueLodestars().Snapshot().Values));
    }

    public bool TryConstellation(EveId id, out PNRogueConstellationDefinition constellation) => constellations.TryGetValue(id, out constellation);
    public PNRogueConstellationDefinition Constellation(EveId id) => constellations.TryGetValue(id, out var c) ? c : throw new KeyNotFoundException("Unknown constellation " + id);
    public bool TryItem(EveId id, out PNRogueItemDefinition item) => items.TryGetValue(id, out item);
    public bool TryItem(string text, out PNRogueItemDefinition item)
    {
        item = null;
        return !string.IsNullOrEmpty(text) && EveId.TryParse(text, out var id) && items.TryGetValue(id, out item);
    }
    public PNRogueItemDefinition Item(EveId id) => items.TryGetValue(id, out var item) ? item : throw new KeyNotFoundException("Unknown Roguelite Buff item " + id);
    public IReadOnlyList<PNRogueItemDefinition> ItemsOf(EveId constellation) => byConstellation.TryGetValue(constellation, out var list) ? list : none;
}

// EN: A frozen copy of the set, handed to the code of each item when the player is built (their `Initialize` reads it). It never changes after it is taken.
public sealed class PNRogueSnapshot
{
    private readonly HashSet<EveId> ids;
    public IReadOnlyList<PNRogueItemDefinition> Items { get; }
    internal PNRogueSnapshot(IReadOnlyList<PNRogueItemDefinition> items) { Items = items; ids = new HashSet<EveId>(items.Select(x => x.Id)); }
    public bool Has(EveId id) => ids.Contains(id);
    public int CountIn(EveId constellation) => Items.Count(x => x.Constellation == constellation);
    public PNRogueLodestarDefinition LodestarOf(EveId constellation) => Items.OfType<PNRogueLodestarDefinition>().FirstOrDefault(x => x.Constellation == constellation);
}

// EN: The items a team owns in a run (Docs/RogueBuffPlan.md section 4). It wraps `PNRunState.Loadout` (the live list that is saved and synchronized), so the order is the order of
//     acquisition; every rule about the set lives here: an id is owned once, a constellation holds at most one Lodestar, the set holds at most `MaxOwned`. It changes only the list. What
//     the items DO is built from it when a player entity is built, never while one is running (decision D6).
// VI: Các mục cả đội sở hữu trong run. Bọc `PNRunState.Loadout` (danh sách sống được lưu và đồng bộ), thứ tự là thứ tự nhận. Mọi luật về tập nằm ở đây; nó chỉ đổi danh sách.
public sealed class PNRogueSet
{
    private readonly PNRogueCatalog catalog;
    private readonly List<string> ids;
    public PNRogueSet(PNRogueCatalog catalog, List<string> ids)
    {
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public IReadOnlyList<string> Ids => ids;
    public int Count => ids.Count;
    public bool Has(EveId id) => ids.Contains(id.ToString());
    public IEnumerable<PNRogueItemDefinition> Items => ids.Select(x => catalog.TryItem(x, out var item) ? item : throw new InvalidOperationException("The set holds unknown item " + x + "."));
    public int CountIn(EveId constellation) => Items.Count(x => x.Constellation == constellation);
    public PNRogueLodestarDefinition LodestarOf(EveId constellation) => Items.OfType<PNRogueLodestarDefinition>().FirstOrDefault(x => x.Constellation == constellation);
    public PNRogueSnapshot Snapshot() => new PNRogueSnapshot(Array.AsReadOnly(Items.ToArray()));

    // Why this item cannot be added now, or null when it can: unknown, owned already, the constellation already has a Lodestar, or the set is full.
    public string WhyNotAdd(EveId id)
    {
        if (!catalog.TryItem(id, out var item)) return "unknown item";
        if (Has(id)) return "owned";
        if (item is PNRogueLodestarDefinition && LodestarOf(item.Constellation) != null) return "constellation has a Lodestar";
        if (ids.Count >= PNRogueLimits.MaxOwned) return "set is full";
        return null;
    }
    public void Add(EveId id)
    {
        string why = WhyNotAdd(id);
        if (why != null) throw new InvalidOperationException("Cannot add " + id + ": " + why + ".");
        ids.Add(id.ToString());
    }
    public bool Remove(EveId id) => ids.Remove(id.ToString());

    // A Lodestar for another Lodestar of the same constellation, in the same place of the order.
    public void Exchange(EveId from, EveId to)
    {
        if (!catalog.TryItem(from, out var a) || !(a is PNRogueLodestarDefinition) || !Has(from)) throw new InvalidOperationException("Only an owned Lodestar can be exchanged.");
        if (!catalog.TryItem(to, out var b) || !(b is PNRogueLodestarDefinition) || b.Constellation != a.Constellation) throw new InvalidOperationException("A Lodestar is exchanged for another Lodestar of its constellation.");
        if (Has(to)) throw new InvalidOperationException("Already owned: " + to);
        ids[ids.IndexOf(from.ToString())] = to.ToString();
    }

    // The problem with a saved list of ids, or null: unknown or repeated id, a constellation with two Lodestars, more than MaxOwned. (A Spark whose Lodestar was exchanged away stays valid.)
    public static string Problem(PNRogueCatalog catalog, IEnumerable<string> saved)
    {
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));
        var list = (saved ?? Enumerable.Empty<string>()).ToList();
        if (list.Count > PNRogueLimits.MaxOwned) return "more than " + PNRogueLimits.MaxOwned + " items";
        var seen = new HashSet<string>(StringComparer.Ordinal); var lodestarOf = new HashSet<EveId>();
        foreach (var text in list)
        {
            if (!catalog.TryItem(text, out var item)) return "unknown item " + text;
            if (!seen.Add(text)) return "repeated item " + text;
            if (item is PNRogueLodestarDefinition && !lodestarOf.Add(item.Constellation)) return "two Lodestars in " + item.Constellation;
        }
        return null;
    }
}
