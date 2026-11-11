#if UNITY_5_3_OR_NEWER
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: The two sync commands that put things into a grid world and take them out (Docs/SandboxPlan.md, step G3). Both go to the world object `grid` (the root of the grid host) and run on the authority; offline and
//     the host send them to themselves through the same path. The host decides everything: the sender has a living player in that world and is within reach of the cell, the placeable is registered, the cells
//     are free. A command that fails a check is dropped without a word. Payloads are tiny plain JSON (<= 192 bytes): place {"def","x","z","dir"}, break {"x","z"}. A break is one HIT of `HitDamage`;
//     what a tool does (more damage) comes with the tools. Clients see the result as cells and chunks once the chunk channel exists (step G8).
//     The handlers only find the world, the controller and the sender's player; the rules are in `ApplyPlace` / `ApplyBreak`, which a test can call with a position.
// VI: Hai lệnh đồng bộ đặt và gỡ vật trong world lưới (G3). Gửi tới world object `grid`, chạy ở authority; host quyết định mọi thứ. Handler chỉ tìm world, controller và người chơi; luật nằm ở ApplyPlace / ApplyBreak.
public static class PNGridCommands
{
    public static readonly EveId Place = EveId.Parse("project_n:grid_place");
    public static readonly EveId Break = EveId.Parse("project_n:grid_break");
    public const string ObjectKey = "grid";
    public const int CommandBytes = 192;
    // Metres from the player to the nearest edge of the footprint; a little slack covers latency.
    public const float Reach = 6f, ReachSlack = .5f;
    public const int HitDamage = 5;

    // The world, the controller and the sender's living player; false when any check fails.
    internal static bool Prepare(PNSyncCommandContext context, out EveWorldContext world, out PNGridWorldController controller, out Transform actor)
    {
        world = null; controller = null; actor = null;
        var worldObject = context.TargetObject as PNWorldObject;
        if (worldObject == null || worldObject.Key != ObjectKey || context.Session == null) return false;
        if (!context.Session.Worlds.TryGet(context.Target.World, context.Target.Incarnation, out world) || !world.CanExecute) return false;
        controller = PNGridWorldController.Find(world);
        if (controller == null) return false;
        var player = context.Session.Entities.Snapshot(world).FirstOrDefault(x => x.IsReady && PNEntityRoles.IsPlayer(x) && x.LoadContext.Identity.OwnerId == context.SenderOwnerId);
        if (player == null) return false;
        var stat = player.GetComponent<PNStat>();
        if (stat == null || stat.IsDead) return false;
        actor = player.transform; return true;
    }

    public static bool InReach(PNGridWorldData data, Vector3 position, PNGridRegion footprint)
    {
        float cell = data.CellSize, x0 = footprint.X0 * cell, x1 = (footprint.X1 + 1) * cell, z0 = footprint.Z0 * cell, z1 = (footprint.Z1 + 1) * cell;
        float dx = Mathf.Max(Mathf.Max(x0 - position.x, 0), position.x - x1), dz = Mathf.Max(Mathf.Max(z0 - position.z, 0), position.z - z1);
        return Mathf.Sqrt(dx * dx + dz * dz) <= Reach + ReachSlack;
    }

    internal static bool Int(JObject obj, string field, out int value)
    {
        value = 0; var token = obj[field];
        if (token == null || token.Type != JTokenType.Integer) return false;
        long raw = token.Value<long>(); if (raw < int.MinValue || raw > int.MaxValue) return false;
        value = (int)raw; return true;
    }

    // The rules of a place command for a sender standing at `position`: true when the thing was placed.
    public static bool ApplyPlace(EveRegistryHub registry, PNGridWorldController controller, Vector3 position, JToken token)
    {
        var payload = token as JObject; if (payload == null) return false;
        if (payload.Properties().Any(p => p.Name != "def" && p.Name != "x" && p.Name != "z" && p.Name != "dir")) return false;
        if (payload["def"]?.Type != JTokenType.String || !EveId.TryParse(payload["def"].Value<string>(), out var id)) return false;
        if (!Int(payload, "x", out int x) || !Int(payload, "z", out int z) || !Int(payload, "dir", out int direction)) return false;
        if (direction < 0 || direction > 3 || !controller.Data.InBounds(x, z)) return false;
        PNPlaceableDefinition definition;
        try { definition = registry.Instances().GetEffectiveKind<PNPlaceableDefinition>(id); } catch (System.Exception) { return false; }
        if (definition == null) return false;
        bool turned = (direction & 1) == 1; int w = turned ? definition.Settings.Depth : definition.Settings.Width, d = turned ? definition.Settings.Width : definition.Settings.Depth;
        if (!InReach(controller.Data, position, new PNGridRegion(x, z, x + w - 1, z + d - 1))) return false;
        return controller.Placements.TryPlace(definition, x, z, direction, out _, out _);
    }

    // The rules of a break command: one hit on whatever stands on the cell; true when the thing broke.
    public static bool ApplyBreak(PNGridWorldController controller, Vector3 position, JToken token)
    {
        var payload = token as JObject; if (payload == null) return false;
        if (payload.Properties().Any(p => p.Name != "x" && p.Name != "z")) return false;
        if (!Int(payload, "x", out int x) || !Int(payload, "z", out int z) || !controller.Data.InBounds(x, z)) return false;
        var placed = controller.Placements.At(x, z); if (placed == null) return false;
        if (!InReach(controller.Data, position, placed.Region)) return false;
        return controller.Placements.Damage(placed.Id, HitDamage);
    }
}

public sealed class PNGridPlaceCommandHandler : IPNSyncCommandHandler
{
    public void Execute(PNSyncCommandContext context)
    {
        if (!PNGridCommands.Prepare(context, out _, out var controller, out var actor)) return;
        PNGridCommands.ApplyPlace(context.Session.Registry, controller, actor.position, context.Payload);
    }
}

public sealed class PNGridBreakCommandHandler : IPNSyncCommandHandler
{
    public void Execute(PNSyncCommandContext context)
    {
        if (!PNGridCommands.Prepare(context, out _, out var controller, out var actor)) return;
        PNGridCommands.ApplyBreak(controller, actor.position, context.Payload);
    }
}
#endif
