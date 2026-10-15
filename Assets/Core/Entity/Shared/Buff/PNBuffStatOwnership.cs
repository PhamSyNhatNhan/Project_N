using System;
using System.Collections.Generic;
using EveEngine;

public partial class PNStat
{
    // Owned by a live Buff scope, protected from floor-local cleanup and excluded from floor snapshots.
    public virtual void SetBuffSource(EveId id, IEnumerable<EveStatModifier> modifiers)
    {
        var values = new List<EveStatModifier>(modifiers ?? throw new ArgumentNullException(nameof(modifiers)));
        if (persistentSourceIds.Contains(id)) throw new InvalidOperationException("A persistent source cannot be adopted by Buff.");
        SetSource(id, values); if (values.Count == 0) buffSourceIds.Remove(id); else buffSourceIds.Add(id);
    }
}
