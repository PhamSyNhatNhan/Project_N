#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;

// Native (edit mode) checks of the test arena map (Docs/SandboxPlan.md, step G14): the cells the map builds, the named points, the rock wall, and which points a walker can reach from the entry.
public static class PNArenaMapChecks
{
    public static string Run()
    {
        var rows = new List<string>(); void Check(bool value, string label) { if (!value) throw new Exception("FAIL " + label); rows.Add("PASS " + label); }
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), ".utmp/no-mods", Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "fixture:arena-map"));
        try
        {
            var session = EveRegistrySession.Context;
            var definition = session.Registry.Get<PNWorldDefinition>().Snapshot().Values.FirstOrDefault(x => x.Id.ToString() == "project_n:test_arena");
            Check(definition != null && definition.CameraView != null, "the arena world is registered with a camera view");
            var descriptor = PNArenaMaps.Descriptor();
            Check(descriptor.Width == 128 && descriptor.Depth == 128 && descriptor.Map == PNArenaMaps.ArenaMapId, "the descriptor is the 128 x 128 arena map");
            var grounds = session.Registry.Get<PNGroundDefinition>().Snapshot().Values.OrderBy(x => x.Id.ToString(), StringComparer.Ordinal).ToArray();
            var data = new PNGridWorldData(descriptor, grounds.Select(x => x.Id.ToString()).ToArray());
            PNArenaMaps.EnsureRegistered();   // the static map registry is empty after a domain reload until the content module (or this call) fills it
            var entry = PNGridMaps.Get(PNArenaMaps.ArenaMapId); Check(entry != null, "the arena map is registered");
            var known = session.Registry.Instances();
            var restored = entry.Build(descriptor, data.Palette, grounds.Select(x => x.Walkable).ToArray(), id => EveId.TryParse(id, out var parsed) ? known.GetEffectiveKind<PNPlaceableDefinition>(parsed) : null);
            restored.CheckAgainst(data);
            for (int next = 0; next < restored.ChunkCount;) next = restored.ApplyChunks(data, next, 64);
            restored.Finish(data);
            var meta = restored.Meta ?? entry.Meta; Check(meta != null, "the map carries its points");
            PNMapSpawn Point(string name) => meta.Spawns.FirstOrDefault(s => s.Name == name);
            Check(Point("player")?.Kind == PNGridMapMeta.PlayerKind && new[] { "monster_a", "monster_b", "island" }.All(n => Point(n)?.Kind == PNGridMapMeta.MonsterKind), "the entry is a player point and the three named points are monster points");
            Check(new[] { "dummy", "starkeeper", "starchanter", "starreaper", "starforger", "starherald" }.All(n => meta.Groups.Count(g => g.Name == n && g.Entries.Count == 1 && g.Entries[0].Count == 1) == 1) && meta.Groups.Count == 6, "six fixed groups: the dummy and the five star creatures");

            bool Standable(int x, int z) => data.InBounds(x, z) && !data.IsSolid(x, z) && grounds[data.GroundAt(x, z)].Walkable;
            foreach (var name in new[] { "player", "monster_a", "monster_b", "island" }) Check(Standable(Point(name).X, Point(name).Z), "the point " + name + " stands on walkable, open ground");
            Check(data.IsSolid(WallX(), PNArenaMaps.WallZ) && data.IsSolid(WallX() + 1, PNArenaMaps.WallZ + 1), "the rock wall blocks its cells");
            Check(Standable(PNArenaMaps.WallX1 + 4, PNArenaMaps.WallZ), "the wall has a gap at its east end");

            var seen = new HashSet<int>(); var queue = new Queue<int>(); var player = Point("player");
            int Key(int x, int z) => z * data.Width + x; queue.Enqueue(Key(player.X, player.Z)); seen.Add(Key(player.X, player.Z));
            while (queue.Count > 0)
            {
                int k = queue.Dequeue(); int x = k % data.Width, z = k / data.Width;
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) { int nx = x + dx, nz = z + dz; if (Standable(nx, nz) && seen.Add(Key(nx, nz))) queue.Enqueue(Key(nx, nz)); }
            }
            Check(seen.Contains(Key(Point("monster_a").X, Point("monster_a").Z)) && seen.Contains(Key(Point("monster_b").X, Point("monster_b").Z)), "from the entry a walker reaches both monster points (round the wall)");
            Check(!seen.Contains(Key(Point("island").X, Point("island").Z)), "the island point is NOT reachable from the entry (the disconnected case)");
            // Without the gap the wall would cut the floor: close it and monster_a must become unreachable, proving the wall is what forces the detour.
            var blocked = new HashSet<int>(); queue.Clear(); queue.Enqueue(Key(player.X, player.Z)); blocked.Add(Key(player.X, player.Z));
            bool Open(int x, int z) => Standable(x, z) && !(z >= PNArenaMaps.WallZ - 1 && z <= PNArenaMaps.WallZ + 2 && x >= PNArenaMaps.WallX1);
            while (queue.Count > 0)
            {
                int k = queue.Dequeue(); int x = k % data.Width, z = k / data.Width;
                foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }) { int nx = x + dx, nz = z + dz; if (Open(nx, nz) && blocked.Add(Key(nx, nz))) queue.Enqueue(Key(nx, nz)); }
            }
            Check(!blocked.Contains(Key(Point("monster_a").X, Point("monster_a").Z)), "with the gap closed the far side is cut off, so the detour is real");
        }
        finally { EveRegistrySession.End(); }
        return "PASS " + rows.Count + " arena map cases (edit-mode)\n" + string.Join("\n", rows);
        int WallX() => PNArenaMaps.WallX0;
    }
}
#endif
