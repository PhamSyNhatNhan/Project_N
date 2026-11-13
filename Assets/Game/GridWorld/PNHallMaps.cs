using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

// EN: The maps of the Hall and the Lobby as grid data (Docs/SandboxPlan.md, step G13). Both replace the floor of the scene they used to be: a 12 x 8 m platform in the middle of a 32 x 32 m world, void around it (G12: the
//     border is terrain, a walker is held by the barrier the world builds there), the player entry `player` at the middle of the platform, and the training dummies as FIXED GROUPS (a group's entries are character ids;
//     the world makes them when it is ready, see PNFixedGroups). The Hall has a plain dummy and one with lorica, the Lobby one dummy, as the scenes had. They are GENERATED maps (a registered step), the same cells for
//     every machine, so a client builds its own copy and never goes stale when the ground types change. The look is a placeholder: art comes later.
//     Cells are 0.5 m; ground comes in 2 m blocks (4 cells), so the platform is 6 x 4 blocks (24 x 16 cells). The world is 64 x 64 cells (two chunks across); its middle is cell (32, 32), the platform's middle.
// VI: Map của Hall và Lobby dạng dữ liệu ô (G13): bệ 12 x 8 m giữa world 32 x 32 m, void xung quanh, lối vào `player` ở giữa bệ, bia tập là NHÓM CỐ ĐỊNH (entries là id nhân vật; world tạo khi sẵn sàng).
public static class PNHallMaps
{
    public const string HallMapId = "project_n:hall_map", LobbyMapId = "project_n:lobby_map", StepName = "platform";
    public const int Cells = 64, Middle = 32, BlockX0 = 5, BlockZ0 = 6, BlockX1 = 10, BlockZ1 = 9;   // the platform: blocks 5..10 x 6..9 = cells 20..43 x 24..39
    public const float CellSize = .5f;
    public const string DummyCharacter = "project_n:dummy", DummyLoricaCharacter = "project_n:dummy_lorica";

    private static PNGridMapMeta Meta(bool hall)
    {
        var meta = new PNGridMapMeta();
        meta.Spawns.Add(new PNMapSpawn { Name = "player", Kind = PNGridMapMeta.PlayerKind, X = Middle, Z = Middle });
        meta.Groups.Add(Group("dummy", Middle + 4, DummyCharacter));                          // 2 m to the +x side of the middle, as in the scenes
        if (hall) meta.Groups.Add(Group("dummy_lorica", Middle + 7, DummyLoricaCharacter));   // 3.5 m
        return meta;
    }

    private static PNMapGroup Group(string name, int x, string character)
    {
        var group = new PNMapGroup { Name = name, X = x, Z = Middle, Radius = .5 };
        group.Entries.Add(new PNMapGroupEntry { Entity = character, Count = 1 });
        return group;
    }

    public static PNGridWorldDescriptor HallDescriptor() => new PNGridWorldDescriptor(Cells, Cells, CellSize, HallMapId, 1);
    public static PNGridWorldDescriptor LobbyDescriptor() => new PNGridWorldDescriptor(Cells, Cells, CellSize, LobbyMapId, 1);

    // Safe to call more than once and from several sessions (the registries are static).
    public static void EnsureRegistered()
    {
        if (PNGridMapSteps.Get(StepName) == null) PNGridMapSteps.Register(new PNGridMapStep(StepName, new string[0], BuildPlatform));
        var chain = new JArray(new JObject { ["step"] = StepName });
        if (!PNGridMaps.Has(HallMapId)) PNGridMaps.RegisterGenerated(HallMapId, chain, null, Meta(true));
        if (!PNGridMaps.Has(LobbyMapId)) PNGridMaps.RegisterGenerated(LobbyMapId, (JArray)chain.DeepClone(), null, Meta(false));
    }

    private static void BuildPlatform(PNGridGenContext context, JObject args)
    {
        var doc = context.Doc; var data = doc.Data;
        if (data.Width != Cells || data.Depth != Cells) throw new FormatException("The hall map is " + Cells + " x " + Cells + " cells.");
        int floor = doc.Palette.ToList().IndexOf(PNGridIds.RoomFloor.ToString());
        if (floor < 0) throw new FormatException("The hall map needs the ground " + PNGridIds.RoomFloor + ".");
        int nothing = doc.Palette.ToList().IndexOf(PNGridIds.Void.ToString());
        var edits = new List<PNGridEdit>();
        for (int gz = 0; gz < data.GroundRows; gz++) for (int gx = 0; gx < data.GroundColumns; gx++)
        {
            bool inside = gx >= BlockX0 && gx <= BlockX1 && gz >= BlockZ0 && gz <= BlockZ1;
            edits.Add(new PNGridEdit(PNGridEditKind.Ground, gx * data.GroundFactor, gz * data.GroundFactor, inside || nothing < 0 ? floor : nothing));
        }
        doc.Edit("platform", edits);
    }
}
