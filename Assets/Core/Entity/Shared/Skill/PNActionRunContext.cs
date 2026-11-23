using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// EN: G8c (Docs/SkillNetworkFrameworkPlan.md 4.6.1). The context an Action run needs on another machine, frozen as plain numbers for the Action stream:
//     direction (planar), position and the ids of the targets. The Action's own `RequiredContext` says which parts it needs; a part it does not need is never sent.
//     Only a value type of the stream: nobody who writes an Action or a skill builds one.
// VI: Ngữ cảnh một lần chạy Action cần ở máy khác, đóng băng thành số thuần cho luồng Action: hướng (mặt phẳng), vị trí và id mục tiêu. Phần nào Action không đòi thì không gửi.
public sealed class PNActionRunContext
{
    public const int DirectionBit = 1, PositionBit = 2, TargetsBit = 4, AllBits = 7;
    public const int MaxTargets = 8;
    public const double MaxCoordinate = 1000000d;
    public static readonly long[] NoTargets = new long[0];

    public int Mask { get; }
    public double X { get; }
    public double Z { get; }
    public double Px { get; }
    public double Py { get; }
    public double Pz { get; }
    public IReadOnlyList<long> Targets { get; }
    public bool HasDirection => (Mask & DirectionBit) != 0;
    public bool HasPosition => (Mask & PositionBit) != 0;
    public bool HasTargets => (Mask & TargetsBit) != 0;
    public bool HasExtra => (Mask & (PositionBit | TargetsBit)) != 0;

    private PNActionRunContext(int mask, double x, double z, double px, double py, double pz, long[] targets)
    {
        Mask = mask; X = x; Z = z; Px = px; Py = py; Pz = pz; Targets = targets ?? NoTargets;
    }

    // The only context the first stages could carry; the wire row for it is the old one.
    public static PNActionRunContext FromDirection(double x, double z)
    {
        if (double.IsNaN(x) || double.IsNaN(z) || Math.Abs(x) > 1 || Math.Abs(z) > 1 || x * x + z * z <= 1e-8) throw new ArgumentException("Direction");
        return new PNActionRunContext(DirectionBit, x, z, 0, 0, 0, null);
    }

    public static PNActionRunContext Of(bool direction, double x, double z, bool position, double px, double py, double pz, IReadOnlyList<long> targets)
    {
        int mask = 0;
        if (direction)
        {
            if (double.IsNaN(x) || double.IsNaN(z) || Math.Abs(x) > 1 || Math.Abs(z) > 1 || x * x + z * z <= 1e-8) throw new ArgumentException("Direction");
            mask |= DirectionBit;
        }
        else { x = 0; z = 0; }
        if (position)
        {
            if (!Finite(px) || !Finite(py) || !Finite(pz)) throw new ArgumentException("Position");
            mask |= PositionBit;
        }
        else { px = 0; py = 0; pz = 0; }
        long[] ids = null;
        if (targets != null && targets.Count > 0)
        {
            if (targets.Count > MaxTargets) throw new ArgumentException("Targets");
            ids = new long[targets.Count];
            for (int i = 0; i < ids.Length; i++) { if (targets[i] < 1) throw new ArgumentException("Targets"); ids[i] = targets[i]; }
            mask |= TargetsBit;
        }
        return new PNActionRunContext(mask, x, z, px, py, pz, ids);
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) <= MaxCoordinate;

    // What an Action asked for in its definition, taken from the context the framework built for it. A direction is what every run needs, so a missing one refuses the run; a position or targets the cast does not have
    // (one skill of an Action may use a position, another not) are simply not sent.
    public static bool TryFrom(PNSkillActionContext context, PNSkillContextRequirements required, out PNActionRunContext result)
    {
        result = null;
        bool wantDirection = (required & PNSkillContextRequirements.Direction) != 0;
        bool wantPosition = (required & PNSkillContextRequirements.Position) != 0;
        bool wantTargets = (required & PNSkillContextRequirements.Targets) != 0;
        if (wantDirection && !context.Direction.HasValue) return false;
        wantPosition &= context.Position.HasValue;
        List<long> ids = null;
        if (wantTargets)
        {
            if (context.Targets.Count > MaxTargets) return false;
            ids = new List<long>(context.Targets.Count);
            foreach (var target in context.Targets)
            {
                var id = target != null ? target.EntityId.Value : 0;
                if (id < 1) continue; // a target without an entity identity cannot be named elsewhere; the observer simply has fewer
                ids.Add(id);
            }
        }
        var direction = wantDirection ? context.Direction.Value : Vector3.zero;
        var position = wantPosition ? context.Position.Value : Vector3.zero;
        try { result = Of(wantDirection, direction.x, direction.z, wantPosition, position.x, position.y, position.z, ids); }
        catch (ArgumentException) { return false; }
        return true;
    }
}
