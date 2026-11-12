using EveEngine;
using Newtonsoft.Json.Linq;

// EN: The placeables the game ships with (Docs/SandboxPlan.md, step G3): three samples that exercise the footprint rules, the two logic classes and a thing that does not block the way. Registered in the gameplay
//     content module; a JSON `instances` entry derives more of them (same logic class, other numbers). Public ids with compatible `base:` aliases.
// VI: Các vật đặt có sẵn (G3): ba mẫu thử luật dấu chân, hai lớp logic và một vật không chặn đường. Đăng ký ở gameplay content module; JSON `instances` suy ra thêm.
public static class PNPlaceableContent
{
    public static void Register(EveRegistryHub hub)
    {
        var instances = hub.Instances();
        // A 1x1 block that blocks the way and simply disappears when broken.
        instances.Register(new PNPlaceableDefinition(PNPlaceableIds.Vanish, typeof(PNPlaceableVanishLogic), () => new PNPlaceableVanishLogic(),
            new JObject { ["footprint"] = new JObject { ["width"] = 1, ["depth"] = 1 }, ["health"] = 30 }));
        // A 2x2 block that blocks the way and leaves sand behind.
        instances.Register(new PNPlaceableDefinition(PNPlaceableIds.LeaveGround, typeof(PNPlaceableLeaveGroundLogic), () => new PNPlaceableLeaveGroundLogic(),
            new JObject { ["footprint"] = new JObject { ["width"] = 2, ["depth"] = 2 }, ["health"] = 20, ["params"] = new JObject { ["ground"] = PNGridIds.Sand.ToString() } },
            PNPlaceableLeaveGroundLogic.ValidateParams, PNPlaceableLeaveGroundLogic.ValidateReferences));
        // A 1x1 marker that does not block the way (a bodies walk over it) and keeps monsters from spawning near it.
        instances.Register(new PNPlaceableDefinition(PNPlaceableIds.Marker, typeof(PNPlaceableVanishLogic), () => new PNPlaceableVanishLogic(),
            new JObject { ["footprint"] = new JObject { ["width"] = 1, ["depth"] = 1 }, ["blocksPath"] = false, ["health"] = 5, ["spawnBlock"] = true, ["safetyRadius"] = 6 }));
        // Work samples (Docs/SandboxPlan.md, work of placeables): a tree only an axe brings down, a rock that needs a pickaxe of power 50, and a wall that takes both work and damage (a monster may work it).
        instances.Register(new PNPlaceableDefinition(PNPlaceableIds.Tree, typeof(PNPlaceableVanishLogic), () => new PNPlaceableVanishLogic(),
            new JObject { ["footprint"] = new JObject { ["width"] = 1, ["depth"] = 1 }, ["breakable"] = false, ["work"] = new JObject { ["types"] = new JArray(PNWorkIds.Axe.ToString()), ["left"] = 30 } }));
        instances.Register(new PNPlaceableDefinition(PNPlaceableIds.Rock, typeof(PNPlaceableVanishLogic), () => new PNPlaceableVanishLogic(),
            new JObject { ["footprint"] = new JObject { ["width"] = 2, ["depth"] = 2 }, ["breakable"] = false, ["work"] = new JObject { ["types"] = new JArray(PNWorkIds.Pickaxe.ToString()), ["left"] = 60, ["minPower"] = 50 } }));
        instances.Register(new PNPlaceableDefinition(PNPlaceableIds.Wall, typeof(PNPlaceableVanishLogic), () => new PNPlaceableVanishLogic(),
            new JObject { ["footprint"] = new JObject { ["width"] = 1, ["depth"] = 1 }, ["health"] = 40, ["work"] = new JObject { ["types"] = new JArray(PNWorkIds.Pickaxe.ToString(), PNWorkIds.Axe.ToString()), ["left"] = 20, ["enemy"] = true } }));
        foreach (var id in new[] { PNPlaceableIds.Vanish, PNPlaceableIds.LeaveGround, PNPlaceableIds.Marker, PNPlaceableIds.Tree, PNPlaceableIds.Rock, PNPlaceableIds.Wall })
            instances.RegisterBase<PNPlaceableDefinition>(new EveId("base", id.Path), id, "project_n", compatible: (original, replacement) => true);
    }
}
