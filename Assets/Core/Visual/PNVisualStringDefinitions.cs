using System;
using System.Collections.Generic;
using EveEngine;

// EN: Data of the puppet strings (Docs/VisualInkStringsPlan.md): a string is what the stars hang a puppet by. Three registries (shape = how the line runs, colour = how it looks, style = a named
//     bundle of both with defaults) and the entry an appearance lists per string. Everything is plain data; the resolver is the one place that decides precedence (style chain < entry),
//     and the run-time API of PNVisualActor calls the same function. Limits are refused, never clamped.
// VI: Dữ liệu của dây rối: hình dạng (shape), màu (color), kiểu dựng sẵn (style) và mục dây trong appearance. Một hàm giải quyết thứ tự ưu tiên: style < entry.

public static class PNVisualStringLimits
{
    public const int MaxPerPuppet = 4, MinSegments = 4, MaxSegments = 48, MaxRegistry = 64, MaxExtendsDepth = 4;
    public const float MinWidth = .002f, MaxWidth = .08f, MaxSwayAmplitude = .05f, MaxSwayFrequency = 5f, MaxSlack = .2f, MaxLength = 20f, MaxLeaveDistance = 1f, MaxOffset = 100f;
}

// How the line runs: the direction it leaves the socket, how far it keeps that direction, a bend, a little slack, a sway and its thickness. `Length` is metres, or 0 = up to the top of the view.
public sealed class PNVisualStringShapeDefinition : EveIIdentified
{
    public EveId Id { get; }
    public float LeaveX { get; } public float LeaveY { get; } public float LeaveDistance { get; } public float Curve { get; } public float Slack { get; }
    public float SwayAmplitude { get; } public float SwayFrequency { get; } public float WidthRoot { get; } public float WidthTop { get; } public float Length { get; } public int Segments { get; }
    public bool ToTop => Length <= 0f;
    public PNVisualStringShapeDefinition(EveId id, float leaveX, float leaveY, float leaveDistance, float curve, float slack, float swayAmplitude, float swayFrequency, float widthRoot, float widthTop, float length, int segments)
    {
        if (!id.IsValid) throw new FormatException("string shape needs a valid id.");
        Id = id;
        if (!PNVisualLimits.Finite(leaveX) || !PNVisualLimits.Finite(leaveY) || (leaveX == 0f && leaveY == 0f)) throw new FormatException("string shape " + id + ": leave must be a non-zero direction.");
        float n = (float)Math.Sqrt(leaveX * leaveX + leaveY * leaveY); LeaveX = leaveX / n; LeaveY = leaveY / n;
        LeaveDistance = PNVisualLimits.InRange(leaveDistance, 0f, PNVisualStringLimits.MaxLeaveDistance, "string leaveDistance");
        Curve = PNVisualLimits.InRange(curve, -1f, 1f, "string curve"); Slack = PNVisualLimits.InRange(slack, 0f, PNVisualStringLimits.MaxSlack, "string slack");
        SwayAmplitude = PNVisualLimits.InRange(swayAmplitude, 0f, PNVisualStringLimits.MaxSwayAmplitude, "string sway amplitude"); SwayFrequency = PNVisualLimits.InRange(swayFrequency, 0f, PNVisualStringLimits.MaxSwayFrequency, "string sway frequency");
        WidthRoot = PNVisualLimits.InRange(widthRoot, PNVisualStringLimits.MinWidth, PNVisualStringLimits.MaxWidth, "string width"); WidthTop = PNVisualLimits.InRange(widthTop, PNVisualStringLimits.MinWidth, PNVisualStringLimits.MaxWidth, "string width");
        Length = length == 0f ? 0f : PNVisualLimits.InRange(length, .01f, PNVisualStringLimits.MaxLength, "string length");
        if (segments < PNVisualStringLimits.MinSegments || segments > PNVisualStringLimits.MaxSegments) throw new FormatException("string segments must be " + PNVisualStringLimits.MinSegments + ".." + PNVisualStringLimits.MaxSegments + ".");
        Segments = segments;
    }
}

// How it looks: the core line, the soft halo and the knot where it meets the puppet (sRGB), the overall alpha and how much of the top fades out.
public sealed class PNVisualStringColorDefinition : EveIIdentified
{
    public EveId Id { get; } public PNVisualColor Core { get; } public PNVisualColor Halo { get; } public PNVisualColor Knot { get; } public float Alpha { get; } public float FadeTop { get; }
    public PNVisualStringColorDefinition(EveId id, PNVisualColor core, PNVisualColor halo, PNVisualColor knot, float alpha, float fadeTop)
    {
        if (!id.IsValid) throw new FormatException("string colour needs a valid id.");
        Id = id; Core = core; Halo = halo; Knot = knot; Alpha = PNVisualLimits.InRange(alpha, 0f, 1f, "string alpha"); FadeTop = PNVisualLimits.InRange(fadeTop, 0f, 1f, "string fadeTop");
    }
}

// A named role: shape and colour plus defaults. `Extends` merges a base style first; every field is optional so a child only overrides what it names.
public sealed class PNVisualStringStyleDefinition : EveIIdentified
{
    public const string EntranceNone = "none", EntranceSlumped = "slumped", EntranceLowered = "lowered";
    public EveId Id { get; } public EveId? Extends { get; } public EveId? Shape { get; } public EveId? Color { get; } public float? Lean { get; } public string Entrance { get; }
    public PNVisualStringStyleDefinition(EveId id, EveId? extends, EveId? shape, EveId? color, float? lean, string entrance)
    {
        if (!id.IsValid) throw new FormatException("string style needs a valid id.");
        Id = id; Extends = extends; Shape = shape; Color = color;
        Lean = lean.HasValue ? PNVisualLimits.InRange(lean.Value, -1f, 1f, "string lean") : (float?)null;
        if (entrance != null && entrance != EntranceNone && entrance != EntranceSlumped && entrance != EntranceLowered) throw new FormatException("string style " + id + ": entrance must be none, slumped or lowered.");
        Entrance = entrance;
    }
}

// One string an appearance lists: where it is tied (a socket of the rig), a style or an explicit shape and colour, and small per-entry changes.
public sealed class PNVisualStringEntry
{
    public string Socket { get; } public EveId? Style { get; } public EveId? Shape { get; } public EveId? Color { get; } public float OffsetX { get; } public float OffsetY { get; } public float? Lean { get; }
    public PNVisualStringEntry(string socket, EveId? style, EveId? shape, EveId? color, float offsetX = 0f, float offsetY = 0f, float? lean = null)
    {
        Socket = PNVisualLimits.Name(socket, "string socket"); Style = style; Shape = shape; Color = color;
        if (!style.HasValue && !(shape.HasValue && color.HasValue)) throw new FormatException("a string needs a style, or both a shape and a colour.");
        OffsetX = PNVisualLimits.InRange(offsetX, -PNVisualStringLimits.MaxOffset, PNVisualStringLimits.MaxOffset, "string offset"); OffsetY = PNVisualLimits.InRange(offsetY, -PNVisualStringLimits.MaxOffset, PNVisualStringLimits.MaxOffset, "string offset");
        Lean = lean.HasValue ? PNVisualLimits.InRange(lean.Value, -1f, 1f, "string lean") : (float?)null;
    }
}

// An entry after the style chain: everything the renderer needs.
public sealed class PNVisualStringSpec
{
    public string Socket { get; } public PNVisualStringShapeDefinition Shape { get; } public PNVisualStringColorDefinition Color { get; } public float OffsetX { get; } public float OffsetY { get; } public float Lean { get; } public string Entrance { get; }
    public PNVisualStringSpec(string socket, PNVisualStringShapeDefinition shape, PNVisualStringColorDefinition color, float offsetX, float offsetY, float lean, string entrance)
    { Socket = socket; Shape = shape; Color = color; OffsetX = offsetX; OffsetY = offsetY; Lean = lean; Entrance = entrance; }
    // The direction the line leaves the socket and the lean, mirrored when the puppet faces left (every asset faces right and left is a mirror).
    public void Leave(bool facingLeft, out float x, out float y) { x = facingLeft ? -Shape.LeaveX : Shape.LeaveX; y = Shape.LeaveY; }
    public float LeanFor(bool facingLeft) => facingLeft ? -Lean : Lean;
}

public static class PNVisualStringResolver
{
    // One place for precedence: the style chain (base first), then the entry's own fields. Throws FormatException for an unknown reference, a cycle or a chain deeper than the limit.
    public static PNVisualStringSpec Resolve(PNVisualStringEntry entry, Func<EveId, PNVisualStringStyleDefinition> styles, Func<EveId, PNVisualStringShapeDefinition> shapes, Func<EveId, PNVisualStringColorDefinition> colors)
    {
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        EveId? shape = null, color = null; float lean = 0f; string entrance = PNVisualStringStyleDefinition.EntranceNone;
        if (entry.Style.HasValue)
        {
            var chain = new List<PNVisualStringStyleDefinition>(); var seen = new HashSet<EveId>(); var current = entry.Style;
            while (current.HasValue)
            {
                if (!seen.Add(current.Value)) throw new FormatException("string style " + entry.Style.Value + " extends itself through a cycle.");
                if (chain.Count >= PNVisualStringLimits.MaxExtendsDepth) throw new FormatException("string style " + entry.Style.Value + " extends more than " + PNVisualStringLimits.MaxExtendsDepth + " levels.");
                var style = styles(current.Value) ?? throw new FormatException("unknown string style " + current.Value);
                chain.Insert(0, style); current = style.Extends;
            }
            foreach (var style in chain)
            {
                if (style.Shape.HasValue) shape = style.Shape; if (style.Color.HasValue) color = style.Color; if (style.Lean.HasValue) lean = style.Lean.Value; if (style.Entrance != null) entrance = style.Entrance;
            }
        }
        if (entry.Shape.HasValue) shape = entry.Shape; if (entry.Color.HasValue) color = entry.Color; if (entry.Lean.HasValue) lean = entry.Lean.Value;
        if (!shape.HasValue) throw new FormatException("string on socket " + entry.Socket + " has no shape (its style names none).");
        if (!color.HasValue) throw new FormatException("string on socket " + entry.Socket + " has no colour (its style names none).");
        var shapeDefinition = shapes(shape.Value) ?? throw new FormatException("unknown string shape " + shape.Value);
        var colorDefinition = colors(color.Value) ?? throw new FormatException("unknown string colour " + color.Value);
        return new PNVisualStringSpec(entry.Socket, shapeDefinition, colorDefinition, entry.OffsetX, entry.OffsetY, lean, entrance);
    }
}
