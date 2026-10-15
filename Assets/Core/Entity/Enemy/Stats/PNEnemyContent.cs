using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// Shared parser used by startup validation and entity initialization.
public static class PNEnemyContent
{
    public static EveId ReadTypeId(object data)
    {
        if (data == null || data is JToken empty && empty.Type == JTokenType.Null)
            return EveId.Parse("base:normal");
        if (!(data is JObject obj)) throw new FormatException("Project_N character data must be an object.");
        var value = obj["enemyTypeId"];
        if (value == null) return EveId.Parse("base:normal");
        if (value.Type != JTokenType.String) throw new FormatException("enemyTypeId must be a registered ID string.");
        return EveId.Parse(value.Value<string>());
    }

    public static PNEnemyTypeDefinition ResolveType(object data, EveRegistryHub hub)
    {
        if (hub == null) throw new ArgumentNullException(nameof(hub));
        var registry = hub.EnemyTypes();
        return registry.GetEffective(registry.ResolveIdentity(ReadTypeId(data)));
    }
}
