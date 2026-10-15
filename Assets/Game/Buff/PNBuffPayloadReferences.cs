using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// Explicit built-in schemas. Mods supply callbacks for their own schemas.
public static class PNBuffPayloadReferences
{
    public static void Modifier(JToken token, PNContentReferenceContext context, string selector = "statId")
    {
        var data = PNBuffContentBatch.Object(token);
        context.StatOrTag(data[selector], selector);
        context.Optional<PNStatCalculationDefinition>(data, "calculationId");
        context.Optional<PNStatContributionDefinition>(data, "contributionId");
    }
    public static void Template(string template, JToken token, PNContentReferenceContext c)
    {
        var d = PNBuffContentBatch.Object(token);
        switch (template)
        {
            case "buff_stat_template": Modifier(d, c); break;
            case "buff_dot_template": case "buff_bomb_template": c.Array<PNTagDefinition>(d["damageTags"], "damageTags"); break;
            case "buff_control_template": c.Array<PNControlPermissionDefinition>(d["permissionIds"], "permissionIds"); break;
            case "buff_stack_limit_template": c.Require<PNBuffDefinition>(d["targetBuffId"], "targetBuffId"); break;
            case "buff_override_template": c.Require<PNBuffDefinition>(d["targetBuffId"], "targetBuffId"); c.Require<PNBuffDefinition>(d["profileId"], "profileId"); break;
            case "buff_immunity_template": c.Array<PNBuffDefinition>(d["buffIds"], "buffIds"); c.Array<PNBuffTagDefinition>(d["tagIds"], "tagIds"); break;
            case "buff_skill_effect_template": Contribution(d["contribution"], c); break;
            default: throw new FormatException("Unknown built-in reference schema " + template);
        }
    }
    public static void Contribution(JToken token, PNContentReferenceContext c)
    {
        var d = PNBuffContentBatch.Object(token);
        foreach (var field in new[] { "categoryIds", "addCategories", "suppressCategories" }) if (d[field] != null) c.Array<PNSkillCategoryDefinition>(d[field], "contribution." + field);
        if (d["skillIds"] != null) c.Array<EveSkillDefinition>(d["skillIds"], "contribution.skillIds");
        foreach (var field in new[] { "addTags", "suppressTags" }) if (d[field] != null) c.Array<PNTagDefinition>(d[field], "contribution." + field);
        if (d["effects"] is JArray effects) foreach (var item in effects)
        {
            var e = PNBuffContentBatch.Object(item); var kind = PNBuffContentBatch.String(e, "kind");
            if (kind == "Operation") c.Require<PNSkillOperationDefinition>(e["id"], "contribution.effects.id");
            else if (kind == "Action") c.Require<PNSkillActionDefinition>(e["id"], "contribution.effects.id");
            else throw new FormatException("Unknown effect reference kind.");
            var id = c.Read(e["id"], "contribution.effects.id");
            if (kind == "Operation") c.Registry.Operations().Get(id).ValidateReferences(e["data"], c);
            else c.Registry.Actions().Get(id).ValidateReferences(e["data"], c);
        }
    }
}
