using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// The Herald's registered combat data (Docs/StarheraldPlan.md): ONE generic Action, `herald_sidus_action`, shared by every Herald variant (the Starherald is the first). A variant is data only: its skill (cooldown
// Ability `<skill id>_ready`, this Action and the payload below) is written in its character JSON, its Sidus Buff package in the same document's `extensions.project_n.buffs`, and its prefab points the behavior at the
// skill with `skillId`. The Action locks movement for windup + release + recovery with a block_hits guard on itself (`guard`: its own hits and seconds; losing it does NOT stop the cast), and at the end of the windup gives
// `buffId` to the chosen targets (at most `targets`) that are still alive and within `radius`. It never targets itself. Which targets are chosen is PNHeraldTargets. `castRange` is how close an ally must be
// for it to cast; `approach` is how close it walks to the nearest ally; `radius` is the reach of the Buff; `seek` is how far away an ally can be and still be walked to; `follow` is the distance kept from the nearest player when it has no ally.
public static class PNHeraldCombatContent
{
    public static readonly EveId SidusAction = EveId.Parse("project_n:herald_sidus_action");
    public static void Register(EveRegistryHub hub)
    {
        hub.Actions().Register(new PNSkillActionDefinition(SidusAction, typeof(PNHeraldSidusAction), (a, h) => Validate(a, h), PNSkillContextRequirements.Direction,
            (data, references) =>
            {
                references.Require<PNSkillOperationDefinition>(new JValue("base:lock_movement"), "movementLock");
                references.Require<PNBuffDefinition>(data["buffId"], "sidusBuff");
                references.Require<PNBuffDefinition>(data["guard"]["buffId"], "guardBuff");
            }));
    }
    private static void Positive(JObject p, string key, double maximum)
    { double n = PNBuffContentBatch.Number(p, key); if (double.IsNaN(n) || double.IsInfinity(n) || n <= 0 || n > maximum) throw new FormatException("Invalid Herald number: " + key); }
    public static void Validate(PNSkillActionData data, EveRegistryHub hub)
    {
        var p = PNBuffContentBatch.Object(data.Perform);
        PNBuffContentBatch.Fields(p, "windup", "release", "recovery", "radius", "approach", "castRange", "targets", "seek", "follow", "buffId", "guard");
        Positive(p, "windup", 10); Positive(p, "release", 10); Positive(p, "recovery", 10); Positive(p, "radius", 20); Positive(p, "approach", 20); Positive(p, "castRange", 20); Positive(p, "seek", 100); Positive(p, "follow", 50);
        int targets = PNBuffContentBatch.Integer(p, "targets"); if (targets < 1 || targets > 16) throw new FormatException("Invalid Herald targets.");
        if (p["buffId"]?.Type != JTokenType.String) throw new FormatException("Herald buffId must be a Buff id.");
        if (!EveId.Parse(p["buffId"].Value<string>()).IsValid) throw new FormatException("Herald buffId is not a valid id.");
        if (PNBuffContentBatch.Number(p, "approach") > PNBuffContentBatch.Number(p, "castRange") || PNBuffContentBatch.Number(p, "castRange") > PNBuffContentBatch.Number(p, "radius")) throw new FormatException("The Herald distances must satisfy approach <= castRange <= radius.");
        var guard = PNBuffContentBatch.Object(p["guard"]); PNBuffContentBatch.Fields(guard, "buffId", "duration", "hits");
        if (guard["buffId"]?.Type != JTokenType.String || !EveId.Parse(guard["buffId"].Value<string>()).IsValid) throw new FormatException("Herald guard buffId must be a Buff id.");
        Positive(guard, "duration", 600); int hits = PNBuffContentBatch.Integer(guard, "hits"); if (hits < 1 || hits > 1000) throw new FormatException("Invalid Herald guard hits.");
    }
}
