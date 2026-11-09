using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: JSON of the roguelite frame inside `extensions.project_n` (Docs/RogueliteFramePlan.md section 5): rogueliteRuns, rogueliteFloors, rogueliteRooms. They join the Buff batch like the
//     Visual registries do: parsed into pending dictionaries without touching the hub, checked together after every document (references to rooms, floors, Buffs, characters
//     and entity roles may point into the same batch), and committed atomically with the rest of the batch. That a room's world exists is checked by the game module
//     (worlds are Unity-side definitions); that its keys exist in the map is checked when the world loads.
// VI: JSON của khung roguelite trong `extensions.project_n`. Nằm chung batch với Buff/Visual: parse vào từ điển chờ, kiểm tham chiếu sau khi đủ tài liệu, commit nguyên tử.
public sealed partial class PNBuffContentBatch
{
    public static readonly string[] RogueliteFields = { "rogueliteRuns", "rogueliteFloors", "rogueliteRooms" };
    private readonly Dictionary<EveId, PNRunDefinition> rogueliteRuns = new Dictionary<EveId, PNRunDefinition>();
    private readonly Dictionary<EveId, PNFloorDefinition> rogueliteFloors = new Dictionary<EveId, PNFloorDefinition>();
    private readonly Dictionary<EveId, PNRoomDefinition> rogueliteRooms = new Dictionary<EveId, PNRoomDefinition>();

    private IEnumerable<EveIIdentified> RogueliteDefinitions => rogueliteRuns.Values.Cast<EveIIdentified>().Concat(rogueliteFloors.Values).Concat(rogueliteRooms.Values);

    private void CommitRoguelite()
    {
        foreach (var d in rogueliteRuns.Values) hub.Get<PNRunDefinition>().Register(d);
        foreach (var d in rogueliteFloors.Values) hub.Get<PNFloorDefinition>().Register(d);
        foreach (var d in rogueliteRooms.Values) hub.Get<PNRoomDefinition>().Register(d);
    }

    private void ParseRoguelite(JObject root, EveContentExtensionDocument document, IReadOnlyList<EveIIdentified> pendingDefinitions)
    {
        foreach (var token in Array(root["rogueliteRuns"])) { var obj = Object(token); var d = PNRunDefinition.Parse(obj, RogueliteId(obj, document)); AddRoguelite(rogueliteRuns, d, hub.Get<PNRunDefinition>()); }
        foreach (var token in Array(root["rogueliteFloors"])) { var obj = Object(token); var d = PNFloorDefinition.Parse(obj, RogueliteId(obj, document)); AddRoguelite(rogueliteFloors, d, hub.Get<PNFloorDefinition>()); }
        foreach (var token in Array(root["rogueliteRooms"])) { var obj = Object(token); var d = PNRoomDefinition.Parse(obj, RogueliteId(obj, document)); AddRoguelite(rogueliteRooms, d, hub.Get<PNRoomDefinition>()); }
        checks.Add(() => ValidateRoguelite(document, pendingDefinitions));
    }

    private static EveId RogueliteId(JObject obj, EveContentExtensionDocument document)
    {
        if (obj["id"] == null) throw new FormatException("A roguelite definition needs an id.");
        return Owned(String(obj, "id"), document);
    }

    private static void AddRoguelite<T>(Dictionary<EveId, T> pending, T definition, EveRegistry<T> registry) where T : class, EveIIdentified
    {
        if (pending.ContainsKey(definition.Id) || registry.TryGet(definition.Id, out _)) throw new FormatException("Duplicate roguelite definition " + definition.Id);
        if (pending.Count >= PNRogueliteLimits.MaxDefinitionsPerDocument) throw new FormatException("Too many roguelite definitions in one document.");
        pending.Add(definition.Id, definition);
    }

    // Everything a definition names must exist (in this batch or already registered) and be reachable under the pack's declared dependencies.
    private void ValidateRoguelite(EveContentExtensionDocument document, IReadOnlyList<EveIIdentified> pendingDefinitions)
    {
        var context = References(document, pendingDefinitions);
        foreach (var run in rogueliteRuns.Values)
            foreach (var floor in run.Floors) RequireRoguelite(floor, rogueliteFloors, hub.Get<PNFloorDefinition>(), document, "run " + run.Id + " floor");
        foreach (var floor in rogueliteFloors.Values)
        {
            foreach (var entry in floor.Rooms) RequireRoguelite(entry.Room, rogueliteRooms, hub.Get<PNRoomDefinition>(), document, "floor " + floor.Id + " room");
            foreach (var buff in floor.EnemyBuffs) context.Require<PNBuffDefinition>(buff, "floor " + floor.Id + " enemyBuffs");
        }
        foreach (var room in rogueliteRooms.Values)
            foreach (var wave in room.Waves)
                foreach (var spawn in wave.Spawns)
                {
                    string path = "room " + room.Id + " spawn";
                    context.Require<EveCharacterDefinition>(spawn.Character, path + " character");
                    context.Require<PNEntityDefinition>(spawn.Entity, path + " entity");
                    RequireSpawnPrefab(spawn, path, pendingDefinitions);
                }
    }

    private void RequireRoguelite<T>(EveId id, Dictionary<EveId, T> pending, EveRegistry<T> registry, EveContentExtensionDocument document, string what) where T : class, EveIIdentified
    {
        Reference(id, document, registry);
        if (!pending.ContainsKey(id) && !registry.TryGet(id, out _)) throw new FormatException(what + " uses unknown " + typeof(T).Name + " " + id);
    }

    // A spawned enemy has to resolve to a prefab: the role names one, or the character's body does. Better to fail at content load than at the first spawn.
    private void RequireSpawnPrefab(PNRoomSpawn spawn, string path, IReadOnlyList<EveIIdentified> pendingDefinitions)
    {
        PNEntityDefinition role = null;
        if (hub.Instances().TryGet(spawn.Entity, out var instance)) role = instance as PNEntityDefinition;
        if (role != null && role.Settings.Prefab.Length > 0) return;
        // The character is registered already or still pending in this batch (JSON characters are registered after the extension batches are checked).
        EveCharacterDefinition character = null;
        if (hub.Characters.TryGet(spawn.Character, out var found)) character = found;
        else if (pendingDefinitions != null) character = pendingDefinitions.OfType<EveCharacterDefinition>().FirstOrDefault(x => x.Id == spawn.Character);
        if (character == null) return;
        if (PNBodySettings.Read(character).Prefab.Length == 0) throw new FormatException(path + ": character " + spawn.Character + " has no body.prefab and role " + spawn.Entity + " names none.");
    }
}
