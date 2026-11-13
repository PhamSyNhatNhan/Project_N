using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

// EN: Terrains and locomotions (Docs/SandboxPlan.md, step G12). A ground type names the TERRAIN it makes (land, void, rock; a mod registers more), and a body has a LOCOMOTION (registered `locomotions`) that lists the terrains
//     it can stand on. Walking is one locomotion, flying another; a mod adds swimming, hovering and so on. Where two cells of different terrain meet, the grid world builds a barrier on a layer of its own (see PNTerrainLayers):
//     a body is stopped by the barrier of every terrain its locomotion cannot stand on. Nothing here is a wall placed by hand and nothing changes by itself: a locomotion changes only when something pushes one
//     on a body (PNTraversal.Push), and a high jump does not make a body fly.
// VI: Địa hình (terrain) và cách di chuyển (locomotion). Ground khai báo terrain nó tạo ra; thân có locomotion liệt kê các terrain nó đứng được. Ở biên giữa hai ô khác terrain, world lưới dựng rào trên layer riêng; thân bị
//     chặn bởi rào của mọi terrain mà locomotion của nó không đứng được. Locomotion không tự đổi: chỉ đổi khi có Push.
public sealed class PNTerrainDefinition : EveIIdentified
{
    public const int MaxText = 128, MaxDescription = 256;
    public EveId Id { get; }
    public string Description { get; }
    public PNTerrainDefinition(EveId id, string description = "")
    {
        if (!id.IsValid || id.ToString().Length > MaxText) throw new ArgumentException("A terrain needs a valid id of at most " + MaxText + " characters.", nameof(id));
        if (description != null && description.Length > MaxDescription) throw new ArgumentException("A terrain description is at most " + MaxDescription + " characters.", nameof(description));
        Id = id; Description = description ?? "";
    }
}

public sealed class PNLocomotionDefinition : EveIIdentified
{
    public const int MaxText = 128, MaxDescription = 256, MaxStandsOn = 32, MaxPriority = 1000000;
    private readonly HashSet<EveId> stands;
    public EveId Id { get; }
    // The terrains a body with this locomotion can stand on and walk across.
    public IReadOnlyList<EveId> StandsOn { get; }
    // When several locomotions are pushed on one body the highest priority is the one in force; the body's own locomotion has priority 0 only in the sense that it loses to any pushed one.
    public int Priority { get; }
    public string Description { get; }
    public PNLocomotionDefinition(EveId id, IEnumerable<EveId> standsOn, int priority = 0, string description = "")
    {
        if (!id.IsValid || id.ToString().Length > MaxText) throw new ArgumentException("A locomotion needs a valid id of at most " + MaxText + " characters.", nameof(id));
        if (standsOn == null) throw new ArgumentNullException(nameof(standsOn));
        var list = standsOn.ToList();
        if (list.Count == 0) throw new ArgumentException("A locomotion stands on at least one terrain.", nameof(standsOn));
        if (list.Count > MaxStandsOn) throw new ArgumentException("A locomotion stands on at most " + MaxStandsOn + " terrains.", nameof(standsOn));
        foreach (var terrain in list) if (!terrain.IsValid) throw new ArgumentException("A locomotion names only valid terrain ids.", nameof(standsOn));
        if (list.Distinct().Count() != list.Count) throw new ArgumentException("A locomotion names each terrain once.", nameof(standsOn));
        if (priority < -MaxPriority || priority > MaxPriority) throw new ArgumentException("A locomotion priority is within +-" + MaxPriority + ".", nameof(priority));
        if (description != null && description.Length > MaxDescription) throw new ArgumentException("A locomotion description is at most " + MaxDescription + " characters.", nameof(description));
        Id = id; StandsOn = list; stands = new HashSet<EveId>(list); Priority = priority; Description = description ?? "";
    }
    public bool CanStandOn(EveId terrain) => stands.Contains(terrain);
}

public static class PNTerrainIds
{
    // Ground one can walk on.
    public static readonly EveId Land = EveId.Parse("project_n:land");
    // Nothing under the cell (a hole, the outside of a map): no floor.
    public static readonly EveId Void = EveId.Parse("project_n:void");
    // A floor that is drawn but cannot be walked on (bare stone).
    public static readonly EveId Rock = EveId.Parse("project_n:rock");
    // The default locomotion: on land only.
    public static readonly EveId Ground = EveId.Parse("project_n:ground");
    // Flying: land, void and rock alike.
    public static readonly EveId Air = EveId.Parse("project_n:air");
}

// The physics layers of the barriers a grid world builds between terrains. Unity has 32 layers, so a fixed block of them is set aside: every terrain except land takes one, by id order, and the same terrain always gets the
// same layer on every machine (the order depends only on the registered ids). More terrains than layers is a content error, reported when the content is published.
public static class PNTerrainLayers
{
    public const int FirstLayer = 14, LayerCount = 4;

    // Layer by terrain (land has none). Throws when more than LayerCount terrains need one.
    public static Dictionary<EveId, int> Assign(IEnumerable<EveId> terrains)
    {
        var result = new Dictionary<EveId, int>();
        if (terrains == null) return result;
        foreach (var id in terrains.Where(x => x != PNTerrainIds.Land).Distinct().OrderBy(x => x.ToString(), StringComparer.Ordinal))
        {
            if (result.Count >= LayerCount) throw new InvalidOperationException("More than " + LayerCount + " terrains besides land are registered; each needs a barrier layer (" + string.Join(", ", terrains.Select(x => x.ToString()).OrderBy(x => x, StringComparer.Ordinal)) + ").");
            result[id] = FirstLayer + result.Count;
        }
        return result;
    }

    // Navigation tag of every terrain (G12b): land is tag 0 and the other terrains follow in the same id order as their barrier layers, so every machine tags the same. The tags of a world fit in an A* tag (0..31).
    public static Dictionary<EveId, int> Tags(IEnumerable<EveId> terrains)
    {
        var result = new Dictionary<EveId, int> { [PNTerrainIds.Land] = 0 };
        foreach (var layer in Assign(terrains)) result[layer.Key] = layer.Value - FirstLayer + 1;
        return result;
    }

    // The tag mask of a locomotion: one bit per terrain tag it stands on (terrains the tag table does not know are left out).
    public static uint TagMask(PNLocomotionDefinition locomotion, IReadOnlyDictionary<EveId, int> tags)
    {
        uint mask = 0;
        foreach (var terrain in locomotion.StandsOn) if (tags.TryGetValue(terrain, out int tag)) mask |= 1u << tag;
        return mask;
    }

    // The layers a locomotion is not stopped by: the barrier layers of the terrains it stands on.
    public static int ExcludeMask(PNLocomotionDefinition locomotion, IReadOnlyDictionary<EveId, int> layers)
    {
        int mask = 0;
        foreach (var terrain in locomotion.StandsOn) if (layers.TryGetValue(terrain, out int layer)) mask |= 1 << layer;
        return mask;
    }

    // The checks that need the whole registered content: every ground names a registered terrain, every locomotion stands on registered terrains, the default locomotion exists, and the layers are enough.
    public static void Validate(EveRegistryHub hub)
    {
        var terrains = hub.Get<PNTerrainDefinition>();
        foreach (var ground in hub.Get<PNGroundDefinition>().Snapshot().Values)
            if (!terrains.TryGet(ground.Terrain, out _)) throw new FormatException("Ground " + ground.Id + " names terrain " + ground.Terrain + ", which is not registered.");
        var locomotions = hub.Get<PNLocomotionDefinition>();
        foreach (var locomotion in locomotions.Snapshot().Values)
            foreach (var terrain in locomotion.StandsOn)
                if (!terrains.TryGet(terrain, out _)) throw new FormatException("Locomotion " + locomotion.Id + " stands on terrain " + terrain + ", which is not registered.");
        if (!locomotions.TryGet(PNTerrainIds.Ground, out var ground2) || !ground2.CanStandOn(PNTerrainIds.Land)) throw new FormatException("The default locomotion " + PNTerrainIds.Ground + " must be registered and stand on " + PNTerrainIds.Land + ".");
        Assign(terrains.Snapshot().Values.Select(x => x.Id));
    }
}

// What a body tells the navigation about how it moves (G12b): the locomotion in force and a revision that changes every time it changes, so a body following a route knows to ask for a new one. Implemented by PNTraversal.
public interface IPNLocomotionSource
{
    PNLocomotionDefinition Locomotion { get; }
    int LocomotionRevision { get; }
}
