#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

internal static class PNPlacementWorkService
{
    internal static void Apply(PNSkillActionContext context, PNWorkActionSettings settings, ref int worked, ref int broken)
    {
        var world = context.World;
        if (context.Mode != PNSkillMode.Authority || world == null || !world.CanExecute ||
            !world.Session.HasAuthority || !PNWorldGameplay.CanExecute(context.Skill)) return;
        var controller = PNGridWorldController.Find(world);
        if (controller == null) return;
        var owner = context.Skill != null ? context.Skill.GetComponent<EveEntityLoader>() : null;
        // "A player" is the role; anything else that works a thing is a monster to the thing's `enemy` flag.
        bool fromEnemy = owner == null || !PNEntityRoles.IsPlayer(owner);
        var packet = settings.CreatePacket(fromEnemy);
        var point = context.Position.Value;
        if (context.Direction.HasValue && settings.Forward > 0f) { var flat = new Vector3(context.Direction.Value.x, 0f, context.Direction.Value.z); if (flat.sqrMagnitude > 1e-6f) point += flat.normalized * settings.Forward; }
        var data = controller.Data; float cell = data.CellSize, radius = settings.Radius;
        var region = new PNGridRegion(Mathf.FloorToInt((point.x - radius) / cell), Mathf.FloorToInt((point.z - radius) / cell), Mathf.FloorToInt((point.x + radius) / cell), Mathf.FloorToInt((point.z + radius) / cell));
        var reached = new List<PNPlacedObject>();
        foreach (var placed in controller.Placements.InRegion(region)) if (Near(placed, point, radius, cell)) reached.Add(placed);
        // Collected first: breaking a thing changes the list the region was read from.
        foreach (var placed in reached)
        {
            var outcome = controller.Placements.Work(placed.Id, packet);
            if (outcome == PNWorkOutcome.Worked) worked++;
            else if (outcome == PNWorkOutcome.Broken) { worked++; broken++; }
        }
    }

    // The nearest point of the thing's footprint to the point is within the radius.
    private static bool Near(PNPlacedObject placed, Vector3 point, float radius, float cell)
    {
        float x0 = placed.X * cell, x1 = (placed.X + placed.CellsX) * cell, z0 = placed.Z * cell, z1 = (placed.Z + placed.CellsZ) * cell;
        float dx = Mathf.Max(Mathf.Max(x0 - point.x, 0f), point.x - x1), dz = Mathf.Max(Mathf.Max(z0 - point.z, 0f), point.z - z1);
        return dx * dx + dz * dz <= radius * radius;
    }

}
#endif
