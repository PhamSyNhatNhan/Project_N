using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

// EN: Where the modifier sources of the items go (the entity's stat, or a recorder in a test). A source is added once per item with a `stats` block and removed when the item is disposed.
public interface IPNRogueStatSink
{
    void SetSource(EveId sourceId, IReadOnlyList<EveStatModifier> modifiers);
    void RemoveSource(EveId sourceId);
}

// EN: Builds, runs and disposes the code of every item the team owns for ONE player entity (Docs/RogueBuffPlan.md section 5). Pure: it needs no Unity object, only the registry, the catalog, a stat sink
//     and a binding, so its order and its handling of failures are checked in the smoke suite; `PNRogueBuffManager` (Game) is the thin glue that gives it the entity.
//     Order: the constellations that have an owned item (sorted by id), then the Lodestars, then the Sparks (each sorted by id). For each object: create it through its factory, attach the binding, add the
//     `stats` block as one source, call `Initialize`. `Tick` goes in the same order; `Dispose` goes in the reverse order and also removes the source. The set is read ONCE, from the snapshot in the
//     binding, so nothing that happens to the team's set while the entity lives reaches it (decision D6). A failure in one object is reported, that object (and, for a constellation, its items) is
//     disposed and dropped, and the others go on.
// VI: Dựng, chạy và huỷ mã của mọi mục đội đang sở hữu cho MỘT thực thể người chơi. Thuần: kiểm được bằng smoke. Thứ tự: chòm có mục, rồi Sao dẫn, rồi Tia sáng (mỗi nhóm theo id). Tập chỉ đọc một lần
//     từ ảnh chụp. Lỗi ở một đối tượng: báo, huỷ và bỏ nó (chòm thì bỏ cả mục của nó), các đối tượng khác vẫn chạy.
public sealed class PNRogueRuntime
{
    public sealed class Entry
    {
        public PNRogueDefinition Definition { get; internal set; }
        public PNRogueLogic Logic { get; internal set; }
        internal EveId Source;
        internal bool HasSource;
        internal PNRogueDisplay Shown;
    }

    private readonly EveRegistryHub hub;
    private readonly PNRogueCatalog catalog;
    private readonly IPNRogueStatSink stats;
    private readonly Action<string, Exception> report;
    private readonly List<Entry> entries = new List<Entry>();
    private bool built, disposed;

    public PNRogueRuntime(EveRegistryHub hub, PNRogueCatalog catalog, IPNRogueStatSink stats, Action<string, Exception> report = null)
    {
        this.hub = hub ?? throw new ArgumentNullException(nameof(hub));
        this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        this.stats = stats;
        this.report = report;
    }

    public IReadOnlyList<Entry> Entries => entries;
    public int Failures { get; private set; }
    // True after a `Tick` in which some object's `Display` changed; `AcknowledgeDisplays` clears it (the manager pushes the new numbers once, not every frame).
    public bool DisplayChanged { get; private set; }

    // The id under which an item's `stats` block is added to the entity's stat.
    public static EveId SourceId(PNRogueDefinition definition) => new EveId("rogue", definition.Kind.ToString().ToLowerInvariant() + "/" + definition.Id.Namespace + "/" + definition.Id.Path);

    public void Build(PNRogueBinding binding)
    {
        if (binding == null) throw new ArgumentNullException(nameof(binding));
        if (built || disposed) throw new InvalidOperationException("A Roguelite Buff runtime is built once.");
        built = true;
        var owned = binding.Set.Items;
        var constellationLogic = new Dictionary<EveId, PNRogueConstellationLogic>();
        foreach (var id in owned.Select(x => x.Constellation).Distinct().OrderBy(x => x.ToString(), StringComparer.Ordinal))
        {
            var entry = Start(catalog.Constellation(id), binding, null);
            if (entry != null) constellationLogic.Add(id, (PNRogueConstellationLogic)entry.Logic);
        }
        foreach (var kind in new[] { PNRogueKind.Lodestar, PNRogueKind.Spark })
            foreach (var item in owned.Where(x => x.Kind == kind).OrderBy(x => x.Id.ToString(), StringComparer.Ordinal))
            {
                if (!constellationLogic.TryGetValue(item.Constellation, out var constellation)) { Fail("Skipped " + item.Kind + " " + item.Id + ": its constellation did not start.", null); continue; }
                Start(item, binding, constellation);
            }
    }

    private Entry Start(PNRogueDefinition definition, PNRogueBinding binding, PNRogueConstellationLogic constellation)
    {
        var entry = new Entry { Definition = definition };
        try
        {
            var factory = hub.RogueFactories().Get(definition.FactoryId);
            entry.Logic = factory.Create(definition);
            if (entry.Logic is PNRogueItemLogic item) item.Constellation = constellation;
            entry.Logic.Attach(binding);
            entries.Add(entry);   // from here on a failure is cleaned up by Drop (the object may already hold things)
            if (definition.Modifiers.Count > 0)
            {
                if (stats == null) throw new InvalidOperationException(definition.Kind + " " + definition.Id + " has stats but the entity has no stat to put them on.");
                entry.Source = SourceId(definition); stats.SetSource(entry.Source, definition.Modifiers); entry.HasSource = true;
            }
            entry.Logic.Initialize();
            entry.Shown = entry.Logic.Display;
            if (!entry.Shown.IsEmpty) DisplayChanged = true;
            return entry;
        }
        catch (Exception error)
        {
            Fail("Roguelite Buff " + definition.Kind + " " + definition.Id + " failed to start.", error);
            if (entries.Contains(entry)) Drop(entry); else Release(entry);
            return null;
        }
    }

    public void Tick(double deltaSeconds)
    {
        if (!built || disposed) return;
        if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        List<Entry> failed = null;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            try
            {
                entry.Logic.Tick(deltaSeconds);
                var shown = entry.Logic.Display;
                if (!shown.Equals(entry.Shown)) { entry.Shown = shown; DisplayChanged = true; }
            }
            catch (Exception error)
            {
                Fail("Roguelite Buff " + entry.Definition.Kind + " " + entry.Definition.Id + " failed in Tick.", error);
                (failed ?? (failed = new List<Entry>())).Add(entry);
            }
        }
        // The objects that failed are removed after the pass, so every other object still ticked this frame (nothing is allocated unless something fails).
        if (failed != null) foreach (var entry in failed) if (entries.Contains(entry)) DropWithItems(entry);
    }

    public void AcknowledgeDisplays() { DisplayChanged = false; }

    // Undo everything, in the reverse order of the build. Safe to call twice.
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        for (int i = entries.Count - 1; i >= 0; i--) Release(entries[i]);
        entries.Clear();
    }

    // Removes one object that failed (and, for a constellation, the items that hang on it).
    private void DropWithItems(Entry failed)
    {
        var doomed = new List<Entry> { failed };
        if (failed.Logic is PNRogueConstellationLogic group) doomed.AddRange(entries.Where(x => x.Logic is PNRogueItemLogic item && ReferenceEquals(item.Constellation, group)));
        foreach (var entry in doomed.OrderByDescending(x => entries.IndexOf(x)).ToList()) Drop(entry);
        DisplayChanged = true;
    }
    private void Drop(Entry entry) { entries.Remove(entry); Release(entry); }
    private void Release(Entry entry)
    {
        try { entry.Logic?.Dispose(); } catch (Exception error) { Fail("Roguelite Buff " + entry.Definition.Kind + " " + entry.Definition.Id + " failed in Dispose.", error); }
        if (entry.HasSource) { entry.HasSource = false; try { stats?.RemoveSource(entry.Source); } catch (Exception error) { Fail("Roguelite Buff " + entry.Definition.Kind + " " + entry.Definition.Id + " source was not removed.", error); } }
    }
    private void Fail(string what, Exception error) { Failures++; try { report?.Invoke(what, error); } catch (Exception) { } }
}
