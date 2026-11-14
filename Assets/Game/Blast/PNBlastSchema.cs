using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// The input schema of the blasts of this game (shared by the Unity runtime and the standalone checks).
public static class PNBlastSchema
{
    public static readonly string[] Common = { "delay", "tail", "radius", "height", "value", "canCrit", "tags" };
    public static readonly string[] Polygon = Common.Concat(new[] { "sides", "rotation" }).ToArray();
    public static void ValidateCommon(JObject o, EveRegistryHub hub = null)
    {
        double delay = PNBuffContentBatch.Number(o, "delay"), tail = PNBuffContentBatch.Number(o, "tail"), radius = PNBuffContentBatch.Number(o, "radius"), height = PNBuffContentBatch.Number(o, "height");
        if (!(delay > 0 && delay <= 30)) throw new FormatException("delay must be in (0, 30].");
        if (!(tail >= 0 && tail <= 30)) throw new FormatException("tail must be in [0, 30].");
        if (!(radius > 0 && radius <= 50)) throw new FormatException("radius must be in (0, 50].");
        if (!(height > 0 && height <= 50)) throw new FormatException("height must be in (0, 50].");
        var value = PNSkillValues.Parse(o["value"]); value.ValidateNonnegativeAmounts(); if (hub != null) value.ValidateStats(hub);
        PNBuffContentBatch.Bool(o, "canCrit"); PNSkillEffect.Ids(o["tags"]).ToArray();
    }
    public static void ValidatePolygon(JToken token, EveRegistryHub hub = null)
    {
        var o = PNBuffContentBatch.Object(token); PNBuffContentBatch.Fields(o, Polygon); ValidateCommon(o, hub);
        int sides = PNBuffContentBatch.Integer(o, "sides"); if (sides < 3 || sides > 16) throw new FormatException("sides must be 3..16.");
        double rotation = PNBuffContentBatch.Number(o, "rotation"); if (double.IsNaN(rotation) || double.IsInfinity(rotation)) throw new FormatException("rotation must be finite.");
    }
}
