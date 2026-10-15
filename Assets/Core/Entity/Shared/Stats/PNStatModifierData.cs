using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// Parses modifier fields inside owning Buff schemas. Base/resource entries do not use this contract.
public static class PNStatModifierData
{
    private static EveId Optional(JObject data, string field)
    {
        if (data.Property(field) == null) return default;
        if (data[field].Type != JTokenType.String || string.IsNullOrWhiteSpace(data[field].Value<string>()))
            throw new FormatException(field + " must be a nonempty ID when supplied.");
        return EveId.Parse(data[field].Value<string>());
    }
    public static EveStatModifier Parse(JObject data, double multiplier = 1d, string selectorField = "statId")
    {
        int group = PNBuffContentBatch.Integer(data, "group");
        if (group != 0 && data.Property("contributionId") != null)
            throw new FormatException("Only group 0 may declare contributionId.");
        return new EveStatModifier(EveId.Parse(PNBuffContentBatch.String(data, selectorField)),
            PNBuffContentBatch.Number(data, "value") * multiplier, group,
            Optional(data, "calculationId"), Optional(data, "contributionId"));
    }
    public static void Validate(JObject data, EveRegistryHub hub, string selectorField = "statId")
    {
        var modifier = Parse(data, 1d, selectorField);
        if (hub.IsSealed && !hub.Stats.TryGet(modifier.Id, out _) && !hub.Tags().TryGet(modifier.Id, out _))
            throw new FormatException("Unknown modifier selector " + modifier.Id);
        if (modifier.CalculationId.IsValid) hub.Calculations().Get(modifier.CalculationId);
        if (modifier.ContributionId.IsValid) hub.Contributions().Get(modifier.ContributionId);
        if (hub.IsSealed)
        {
            var stats = new PNStatSet(hub);
            if (hub.Stats.TryGet(modifier.Id, out var definition))
                stats.AddBase(definition.Kind == EveStatKind.Resource
                    ? (EveBaseStat)new EveResourceStat(modifier.Id, 0d, 0d, definition.MaxChangePolicy)
                    : new EveBaseStat(modifier.Id, 0d));
            stats.SetSource(EveId.Parse("project_n:validate_modifier"), new[] { modifier });
        }
    }
}
