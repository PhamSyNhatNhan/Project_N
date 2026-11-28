using System;
using System.Collections.Generic;
using EveEngine;

// EN: A light on part of a puppet's own paint, declared in content (`extensions.project_n.visualGlows`). Nothing is redrawn: the puppet shader lights the paint that matches.
//     Each definition is also registered as a LOOK of the same ID, so a skill names it like any look (`stateEffects[].look`, `perform.look`, `SetLook`) and no character needs code.
//     Network: nothing of its own; the look rides the entity's picture, every machine draws it. Display only, never gameplay.
// VI: Anh sang tren chinh mau ve cua con roi, khai bao bang JSON. Moi dinh nghia cung la mot look cung ID. Khong can code mang, chi la hinh.
public sealed class PNVisualGlowDefinition : EveIIdentified
{
    public const int MaxParts = 32, MaxRegistry = 256;
    public EveId Id { get; }
    public IReadOnlyList<string> Parts { get; }     // bone ids (free parts) and slot ids of any rig; null = the whole puppet
    public PNVisualColor? Key { get; }              // the paint that lights up (by hue); null = all paint but the ink
    public PNVisualColor Color { get; }
    public PNVisualColor Core { get; }              // the hot middle
    public float Tolerance { get; }                 // how far in hue from the key still lights
    public float Amount { get; }
    public float Spill { get; }                     // strength of the bleed onto the paint around
    public float Spread { get; }                    // width of that bleed (1 = default)
    public int Priority { get; }                    // two glows on one part: the higher one is drawn, then the newer
    public float Rise { get; }                      // seconds to come up
    public float Release { get; }                   // seconds to fade once it is let go
    public float PulseDepth { get; }                // 0 = steady
    public float PulseRate { get; }                 // breaths a second
    public EveId? WarningState { get; }             // blink during the last `WarningSeconds` of this Ability state (where this machine knows it)
    public float WarningSeconds { get; }

    public PNVisualGlowDefinition(EveId id, IReadOnlyList<string> parts, PNVisualColor? key, PNVisualColor color, PNVisualColor core, float tolerance = .5f, float amount = 1f,
        float spill = 0f, float spread = 1f, int priority = 0, float rise = 0f, float release = 0f, float pulseDepth = 0f, float pulseRate = 0f, EveId? warningState = null, float warningSeconds = 0f)
    {
        if (!id.IsValid) throw new ArgumentException("Glow ID is required.", nameof(id));
        if (parts != null)
        {
            if (parts.Count == 0 || parts.Count > MaxParts) throw new FormatException("glow " + id + " parts must list 1.." + MaxParts + " names (leave it out for the whole puppet).");
            foreach (var part in parts) if (string.IsNullOrEmpty(part) || part.Length > 64) throw new FormatException("glow " + id + " has a bad part name.");
        }
        Id = id; Parts = parts; Key = key; Color = color; Core = core;
        Tolerance = PNVisualLimits.InRange(tolerance, .05f, 1f, "glow tolerance");
        Amount = PNVisualLimits.InRange(amount, 0f, 1f, "glow amount");
        Spill = PNVisualLimits.InRange(spill, 0f, 1f, "glow spill");
        Spread = PNVisualLimits.InRange(spread, .25f, 3f, "glow spread");
        if (priority < -100 || priority > 100) throw new FormatException("glow priority must be between -100 and 100.");
        Priority = priority;
        Rise = PNVisualLimits.InRange(rise, 0f, 5f, "glow rise");
        Release = PNVisualLimits.InRange(release, 0f, 5f, "glow release");
        PulseDepth = PNVisualLimits.InRange(pulseDepth, 0f, 1f, "glow pulse depth");
        PulseRate = PNVisualLimits.InRange(pulseRate, 0f, 10f, "glow pulse rate");
        WarningState = warningState;
        WarningSeconds = PNVisualLimits.InRange(warningSeconds, 0f, 30f, "glow warning seconds");
    }
}

// What can show a glow on an entity (the puppet's body). A handle is unique for the process, so a stale one never touches another puppet.
public interface IPNGlowSurface
{
    bool GlowReady { get; }
    // `remaining`: seconds left of the state the glow warns about, or null when unknown.
    int AddGlow(PNVisualGlowDefinition glow, Func<double?> remaining);
    void RemoveGlow(int handle, bool now);
}

// The look behind every glow definition. The puppet animates the glow (rise, pulse, fade out) on its own clock, so the look only adds and lets go.
public sealed class PNGlowLook : PNLook
{
    private IPNGlowSurface surface;
    private int handle;

    private PNGlowLook(IPNGlowSurface surface, int handle) { this.surface = surface; this.handle = handle; }

    public static PNLook Create(PNVisualGlowDefinition glow, PNLookBuild build)
    {
#if UNITY_5_3_OR_NEWER
        if (build.Skill == null) return null;
        var surface = build.Skill.GetComponent<IPNGlowSurface>();
        if (surface == null) return new PNGlowLook(null, 0);        // a body that can never glow: nothing to wait for
        if (!surface.GlowReady) return null;                        // the puppet is not built yet: the engine tries again
        Func<double?> remaining = null;
        if (glow.WarningState.HasValue && glow.WarningSeconds > 0f)
        {
            var skill = build.Skill; var id = glow.WarningState.Value; PNAbility state = null;
            remaining = () =>
            {
                if (state == null || !state.IsActive)
                {
                    state = null;
                    var abilities = skill != null ? skill.Abilities : null;
                    if (abilities == null) return null;
                    try
                    {
                        var all = abilities.Find(id);
                        for (int i = 0; i < all.Count && state == null; i++) if (all[i].IsActive) state = all[i];
                    }
                    catch (KeyNotFoundException) { return null; }
                }
                return state?.DurationRemaining;
            };
        }
        return new PNGlowLook(surface, surface.AddGlow(glow, remaining));
#else
        return null;
#endif
    }

    public override void End(PNActionViewEnd kind)
    {
        if (surface == null) return;
        var target = surface; surface = null;
        if (target as UnityEngine.Object != null || !(target is UnityEngine.Object)) target.RemoveGlow(handle, kind == PNActionViewEnd.Cancelled);
    }
}
