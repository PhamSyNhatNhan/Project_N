#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

public sealed class PNListedMode
{
    public EveId Id { get; }
    public PNModeListing Listing { get; }
    internal PNListedMode(EveId id, PNModeListing listing) { Id = id; Listing = listing; }
}

// EN: The modes the menu shows: registered modes with a listing (the Hall has none), the effective definition's listing winning over the
//     original's, mobile builds limited to shipped content, ordered by Order then id. Mods register modes in code with a listing.
// VI: Danh sách chế độ menu hiện: chế độ có listing, theo thứ tự; mobile chỉ nội dung gốc.
public static class PNModeCatalog
{
    public static IReadOnlyList<PNListedMode> Listed(EveRegistryHub hub, bool mobile)
    {
        if (hub == null) throw new ArgumentNullException(nameof(hub));
        var registry = hub.Get<PNModeDefinition>();
        var result = new List<PNListedMode>();
        foreach (var original in registry.Snapshot().Values)
        {
            if (!PNModeListing.VisibleOn(original.Id, mobile)) continue;
            var effective = registry.GetEffective(registry.ResolveIdentity(original.Id));
            var listing = effective.Listing ?? original.Listing;
            if (listing != null) result.Add(new PNListedMode(original.Id, listing));
        }
        return result.OrderBy(x => x.Listing.Order).ThenBy(x => x.Id.ToString(), StringComparer.Ordinal).ToList().AsReadOnly();
    }
}
#endif
