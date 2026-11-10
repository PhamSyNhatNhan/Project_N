using System;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: The two implementations every game has, for definitions that need no code of their own (Docs/RogueBuffPlan.md section 3): `project_n:rogue_stats_only` (a Spark or Lodestar whose whole effect is its
//     `stats` block, which the framework adds as one modifier source) and `project_n:rogue_passive` (a constellation with no mechanism). Their `data` must be an empty object. A definition that does something
//     more names a factory of its own, registered by a content module like these.
// VI: Hai triển khai có sẵn cho định nghĩa không cần mã riêng: chỉ số thuần (Tia sáng/Sao dẫn) và chòm không cơ chế. `data` phải rỗng.
public sealed class PNRogueStatsOnlyLogic : PNRogueItemLogic { }
public sealed class PNRoguePassiveLogic : PNRogueConstellationLogic { }

public static class PNRogueBuiltIn
{
    public static readonly EveId StatsOnly = EveId.Parse("project_n:rogue_stats_only"), Passive = EveId.Parse("project_n:rogue_passive");

    private static void Empty(JObject data, EveRegistryHub hub)
    {
        if (data.Count != 0) throw new FormatException("this implementation takes no data (an empty object).");
    }

    public static void Register(EveRegistryHub hub)
    {
        var factories = hub.RogueFactories();
        factories.Register(new PNRogueFactoryDefinition(StatsOnly, PNRogueKinds.Spark | PNRogueKinds.Lodestar, typeof(PNRogueStatsOnlyLogic), () => new PNRogueStatsOnlyLogic(), Empty));
        factories.Register(new PNRogueFactoryDefinition(Passive, PNRogueKinds.Constellation, typeof(PNRoguePassiveLogic), () => new PNRoguePassiveLogic(), Empty));
    }
}
