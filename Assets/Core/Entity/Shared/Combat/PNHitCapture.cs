using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;

// Immutable "capture" shared by every instant attack (Strike, later Blast): the attacker's combat snapshot, the scale factor read ONCE from it
// (Scale route over 1, e.g. melee_scale +20% = 1.2) and the damage tags (the attack's own tags plus its *_damage tag).
// Taken when the attack's shape is first committed or shown, so later Buffs never change an attack already on screen.
// A library, not a system: it owns no timer, no manager and no shape; the caller builds shapes from Scale(...) and queries them with PNHitboxCast.
public sealed class PNHitCapture
{
    private readonly EveId[] tags;
    public PNCombatSourceSnapshot Source { get; }
    public double ScaleFactor { get; }
    public IReadOnlyList<EveId> Tags => tags;
    public PNHitCapture(PNCombatSourceSnapshot source, double scaleFactor, IEnumerable<EveId> tags)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (double.IsNaN(scaleFactor) || double.IsInfinity(scaleFactor) || scaleFactor < 0d) throw new ArgumentOutOfRangeException(nameof(scaleFactor), "Scale must be finite and nonnegative.");
        ScaleFactor = scaleFactor; this.tags = (tags ?? throw new ArgumentNullException(nameof(tags))).Distinct().ToArray();
    }
    // Strike (melee): melee_scale for the size, melee_damage added to the damage tags.
    public static PNHitCapture Melee(PNCombatSourceSnapshot source, IEnumerable<EveId> attackTags)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        return new PNHitCapture(source, source.Calculate(1d, PNContentIds.MeleeScale), (attackTags ?? Array.Empty<EveId>()).Concat(new[] { PNContentIds.MeleeDamage }));
    }
    // Radius, thickness, height and inner radius scale; an angle and a travel distance do not (D8).
    // A positive length never collapses to zero (a zero-size shape is invalid): the floor is 1 mm.
    public float Scale(float length) => length <= 0f ? length : Math.Max(.001f, (float)(length * ScaleFactor));
    // One formula pass; every hit then takes its own crit roll through Damage(prepared).
    public PNPreparedDamage Prepare(double amount, bool canCrit, double? iframeDuration) => Source.PrepareDamage(amount, canCrit, iframeDuration, tags);
    public PNDamage Damage(PNPreparedDamage prepared) => Source.CreateDamage(prepared);
}
