using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

// EN: The developers' test arena as grid data (Docs/SandboxPlan.md, step G14). It replaces the old scene world `ErMode` (a scene adopted as a world, Recast navigation) with a GENERATED map, the same cells for every machine:
//     a 40 x 40 m floor with void all around, a wall of rocks across it with a gap at one end (a detour for the navigation reviews), and a small island of floor across a void strip that no walker can reach (the
//     "disconnected" case). The player enters at `player`; `monster_a`, `monster_b` and `island` are fixed points a review or a spawner can look up by name (kind `monster`); the group `dummy` is a training dummy
//     beside the entry, as the Hall has. The look is a placeholder: art comes later. Cells are 0.5 m, ground comes in 2 m blocks (4 cells); the world is 128 x 128 cells (four 32-cell chunks across).
// VI: Map arena thử nghiệm dạng dữ liệu ô (G14), thay world scene `ErMode`: sàn 40 x 40 m, tường đá chắn ngang có một khe ở đầu (để thử đi vòng), và một đảo nhỏ cách một dải void (vùng tách rời, không đi tới được).
//     Điểm `player`, các điểm `monster_a`, `monster_b`, `island` (kind monster) tra theo tên; nhóm `dummy` là bia tập cạnh lối vào.
public static class PNArenaMaps
{
    public const string ArenaMapId = "project_n:arena_map", StepName = "arena";
    public const int Cells = 128, FloorBlock0 = 2, FloorBlock1 = 21, IslandBlockX0 = 24, IslandBlockX1 = 29, IslandBlockZ0 = 8, IslandBlockZ1 = 13;   // floor: blocks 2..21 = cells 8..91; island: blocks 24..29 x 8..13 = cells 96..119 x 32..55
    public const float CellSize = .5f;
    public const int WallZ = 48, WallX0 = 8, WallX1 = 72;   // the rock wall: 2 x 2 cells each from x = 8 to 71, one row at z 48; the floor beyond x = 72 is the gap
    public const string DummyCharacter = "project_n:dummy";

    public static PNGridMapMeta Meta()
    {
        var meta = new PNGridMapMeta();
        meta.Spawns.Add(new PNMapSpawn { Name = "player", Kind = PNGridMapMeta.PlayerKind, X = 24, Z = 24 });
        meta.Spawns.Add(new PNMapSpawn { Name = "monster_a", Kind = PNGridMapMeta.MonsterKind, X = 24, Z = 72 });     // beyond the wall: the walker must go round it
        meta.Spawns.Add(new PNMapSpawn { Name = "monster_b", Kind = PNGridMapMeta.MonsterKind, X = 72, Z = 24 });
        meta.Spawns.Add(new PNMapSpawn { Name = "island", Kind = PNGridMapMeta.MonsterKind, X = 108, Z = 44 });       // on the island: no path from the entry
        var dummy = new PNMapGroup { Name = "dummy", X = 28, Z = 24, Radius = .5 };
        dummy.Entries.Add(new PNMapGroupEntry { Entity = DummyCharacter, Count = 1 });
        meta.Groups.Add(dummy);
        return meta;
    }

    public static PNGridWorldDescriptor Descriptor() => new PNGridWorldDescriptor(Cells, Cells, CellSize, ArenaMapId, 1);

    // Safe to call more than once and from several sessions (the registries are static).
    public static void EnsureRegistered()
    {
        if (PNGridMapSteps.Get(StepName) == null) PNGridMapSteps.Register(new PNGridMapStep(StepName, new string[0], Build));
        if (!PNGridMaps.Has(ArenaMapId)) PNGridMaps.RegisterGenerated(ArenaMapId, new JArray(new JObject { ["step"] = StepName }), null, Meta());
    }

    private static void Build(PNGridGenContext context, JObject args)
    {
        var doc = context.Doc; var data = doc.Data;
        if (data.Width != Cells || data.Depth != Cells) throw new FormatException("The arena map is " + Cells + " x " + Cells + " cells.");
        int floor = doc.Palette.ToList().IndexOf(PNGridIds.RoomFloor.ToString());
        if (floor < 0) throw new FormatException("The arena map needs the ground " + PNGridIds.RoomFloor + ".");
        int nothing = doc.Palette.ToList().IndexOf(PNGridIds.Void.ToString());
        var edits = new List<PNGridEdit>();
        for (int gz = 0; gz < data.GroundRows; gz++) for (int gx = 0; gx < data.GroundColumns; gx++)
        {
            bool main = gx >= FloorBlock0 && gx <= FloorBlock1 && gz >= FloorBlock0 && gz <= FloorBlock1;
            bool island = gx >= IslandBlockX0 && gx <= IslandBlockX1 && gz >= IslandBlockZ0 && gz <= IslandBlockZ1;
            edits.Add(new PNGridEdit(PNGridEditKind.Ground, gx * data.GroundFactor, gz * data.GroundFactor, main || island || nothing < 0 ? floor : nothing));
        }
        doc.Edit("arena floor", edits);
        for (int x = WallX0; x < WallX1; x += 2) doc.Place(PNPlaceableIds.Rock.ToString(), x, WallZ, 0);
    }
}
