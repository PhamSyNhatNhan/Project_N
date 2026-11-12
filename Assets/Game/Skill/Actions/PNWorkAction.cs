#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: Sends one packet of work (PNWork.cs) to the placed things of the grid world around a point: the skill's position, moved `forward` metres along its direction. It is the registered way for a skill to bring
//     things down; nothing else sends work. It touches no damage: a skill that also hurts what it hits runs a damage Action next to it. Only the authority changes a world, so on a replica it does nothing.
//     Tunable values (types and power, amount, radius, forward) live in the registered `perform` payload; the skill only supplies the position and direction.
// VI: Gửi một gói work tới các vật đặt quanh một điểm của world lưới. Là cách đăng ký để skill hạ vật; không đụng damage. Chỉ authority sửa world nên replica không làm gì.
public class PNWorkAction : PNSkillAction
{
    private PNSkillActionContext invocation;
    private PNWorkActionSettings settings;
    private bool performed, ended;
    private readonly List<PNPlacedObject> reached = new List<PNPlacedObject>();
    private int worked, broken;
    // Things the packet was accepted by, and the ones it brought down (for checks and for a skill that wants to react).
    public int Worked => worked;
    public int Broken => broken;

    public static PNWorkActionSettings Validate(JToken payload, EveRegistryHub hub)
    { var parsed = PNWorkActionSettings.Parse(payload); parsed.ValidateReferences(hub); return parsed; }

    public override void Start(PNSkillActionContext context)
    {
        if (invocation != null || ended) throw new InvalidOperationException("Work action cannot start twice.");
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (!context.Position.HasValue) throw new InvalidOperationException("Work action needs a fresh position context.");
        settings = Validate(context.Data.Action.Perform, context.Registry);
        invocation = context;
        context.BeginAction();
    }

    public override void Perform(PNSkillActionContext context)
    {
        if (ended || invocation == null || !ReferenceEquals(context, invocation)) throw new InvalidOperationException("Work action is not active for this context.");
        if (performed) throw new InvalidOperationException("Work action performs once.");
        performed = true;
        var world = context.World;
        if (world == null || !world.Session.HasAuthority) return;
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

    public override void Exit(PNSkillActionContext context)
    {
        if (ended || invocation == null || !ReferenceEquals(context, invocation)) throw new InvalidOperationException("Work action is not active for this context.");
        if (!performed) throw new InvalidOperationException("Work action must perform before exit.");
        context.CompleteAction(); Dispose();
    }

    public override void Cancel(PNSkillActionContext context)
    {
        if (invocation != null && !ReferenceEquals(context, invocation)) throw new InvalidOperationException("Wrong work invocation context.");
        context.CancelEffects(); Dispose();
    }

    public override void Dispose() { ended = true; invocation = null; settings = null; reached.Clear(); }
}
#endif
