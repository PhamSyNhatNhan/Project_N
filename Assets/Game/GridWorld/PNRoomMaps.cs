using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

// EN: The map of the roguelite rooms as grid data (Docs/SandboxPlan.md, step G10a). It replaces the scene PNRoomStarTest01 with the same room: a 24 x 24 m floor with a ring of void all around (G12: the border is terrain, not solid cells; a walker is stopped by the barrier the world builds there, a flyer is not), the player entry
//     in the south, the three spawn places `north`, `center` and `south` (named points with a radius), and the seven interactables as placeables with a key: `start_switch`, the gates `gate_a`/`gate_b`/`gate_c`, the
//     `fountain` and the two shop stands. It is a GENERATED map (a registered step, the same cells for every machine and every start), so it never goes stale when the ground types change, and a client that builds
//     its own copy of the placements gets the same keys as the host. The look is a placeholder: art comes later.
//     Cells are 0.5 m; a grid world is a whole number of 32-cell chunks, so the world is 128 x 128 cells with the room 10 m from the corner (the Lobby's floor sits around the world's origin). Ground comes in 2 m blocks
//     (4 cells), so the floor is cells Inset+1 .. Last-1 (24..71, the 48 cells = 24 m) and the ring cells Inset (23) and Last (72) and everything outside are void. Things and places are written for a floor starting at cell 8 and shifted by `Shift`.
// VI: Map phòng roguelite dưới dạng dữ liệu ô, thay cảnh PNRoomStarTest01: sàn 24 x 24 m có tường một ô, điểm vào ở phía nam, ba chỗ sinh quái (điểm có tên cộng bán kính) và bảy vật tương tác là vật đặt có khóa.
//     Map SINH (bước đã đăng ký) nên luôn khớp loại đất hiện có và client dựng cùng khóa với host.
public static class PNRoomMaps
{
    public const string StarRoomMapId = "project_n:star_room_01", StepName = "star_room";
    // The room sits well away from the world's corner: the Lobby scene keeps a floor of 12 x 8 m around the origin, and it must not show through the room (the world's origin is the same point for every world).
    public const int Cells = 128, Inset = 23, Last = Inset + 49, Shift = Inset - 7;
    public const float CellSize = .5f;

    // The room's things: placeable, key, origin cell. Positions follow the old scene (metres from the room's middle, 1 m = 2 cells).
    private static readonly (string placeable, string key, int x, int z)[] Things =
    {
        ("project_n:placeable_room_start", "start_switch", 32, 26),
        ("project_n:placeable_fountain", "fountain", 18, 26),
        ("project_n:placeable_shop_rogue", "shop_rogue", 41, 25),
        ("project_n:placeable_shop_heal", "shop_heal", 45, 25),
        ("project_n:placeable_room_exit", "gate_a", 19, 54),
        ("project_n:placeable_room_exit", "gate_b", 31, 54),
        ("project_n:placeable_room_exit", "gate_c", 43, 54)
    };

    public static PNGridMapMeta Meta()
    {
        var meta = new PNGridMapMeta();
        meta.Spawns.Add(new PNMapSpawn { Name = "player", Kind = PNGridMapMeta.PlayerKind, X = 32 + Shift, Z = 16 + Shift });
        meta.Groups.Add(new PNMapGroup { Name = "north", X = 32 + Shift, Z = 46 + Shift, Radius = 3 });
        meta.Groups.Add(new PNMapGroup { Name = "center", X = 32 + Shift, Z = 38 + Shift, Radius = 2.5 });
        meta.Groups.Add(new PNMapGroup { Name = "south", X = 18 + Shift, Z = 34 + Shift, Radius = 3 });
        return meta;
    }

    public static PNGridWorldDescriptor Descriptor() => new PNGridWorldDescriptor(Cells, Cells, CellSize, StarRoomMapId, 1);

    // Safe to call more than once and from several sessions (the registries are static).
    public static void EnsureRegistered()
    {
        if (PNGridMapSteps.Get(StepName) == null) PNGridMapSteps.Register(new PNGridMapStep(StepName, new string[0], BuildRoom));
        if (!PNGridMaps.Has(StarRoomMapId)) PNGridMaps.RegisterGenerated(StarRoomMapId, new JArray(new JObject { ["step"] = StepName }), null, Meta());
    }

    private static void BuildRoom(PNGridGenContext context, JObject args)
    {
        var doc = context.Doc; var data = doc.Data;
        if (data.Width != Cells || data.Depth != Cells) throw new FormatException("The room map is " + Cells + " x " + Cells + " cells.");
        int floor = doc.Palette.ToList().IndexOf(PNGridIds.RoomFloor.ToString());
        if (floor < 0) throw new FormatException("The room map needs the ground " + PNGridIds.RoomFloor + ".");
        int nothing = doc.Palette.ToList().IndexOf(PNGridIds.Void.ToString());
        var edits = new List<PNGridEdit>();
        for (int gz = 0; gz < data.GroundRows; gz++) for (int gx = 0; gx < data.GroundColumns; gx++)
        {
            int x = gx * data.GroundFactor, z = gz * data.GroundFactor;
            bool inside = x > Inset && x < Last && z > Inset && z < Last;   // ground comes in 2 m blocks (4 cells): the floor is cells Inset+1 .. Last-1, the ring cells Inset and Last and everything outside are void
            edits.Add(new PNGridEdit(PNGridEditKind.Ground, x, z, inside || nothing < 0 ? floor : nothing));
        }
        doc.Edit("room floor", edits);
        foreach (var thing in Things) doc.Place(thing.placeable, thing.x + Shift, thing.z + Shift, 0, thing.key);
    }
}
