using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: The batch-time checks of Roguelite Buff definitions (Docs/RogueBuffPlan.md section 3): what a definition names has to exist in this batch or already be registered and be reachable under the
//     pack's declared dependencies. Checks that need the sealed registry (unknown stat selectors, references inside `data`) are PNRogueValidation, run by the game module.
// VI: Kiểm ở mức batch của định nghĩa Roguelite Buff: mọi thứ định nghĩa nhắc tới phải có (trong batch hoặc đã đăng ký) và với tới được theo dependencies của pack.
public sealed partial class PNBuffContentBatch
{
    private void ValidateRogue(EveContentExtensionDocument document, IReadOnlyList<EveIIdentified> pendingDefinitions)
    {
        var factories = hub.Get<PNRogueFactoryDefinition>();
        var constellations = hub.Get<PNRogueConstellationDefinition>(); var sparks = hub.Get<PNRogueSparkDefinition>(); var lodestars = hub.Get<PNRogueLodestarDefinition>();
        foreach (var definition in rogueConstellations.Values.Cast<PNRogueDefinition>().Concat(rogueSparks.Values).Concat(rogueLodestars.Values))
        {
            string what = definition.Kind + " " + definition.Id;
            Reference(definition.FactoryId, document, factories);
            if (!factories.TryGet(definition.FactoryId, out var factory)) throw new FormatException(what + " uses unknown factory " + definition.FactoryId);
            if (!factory.Serves(definition.Kind)) throw new FormatException(what + ": factory " + factory.Id + " does not serve a " + definition.Kind + ".");
            factory.ValidateData(definition.Data, hub);
        }
        foreach (var item in rogueSparks.Values.Cast<PNRogueItemDefinition>().Concat(rogueLodestars.Values))
        {
            RequireRoguelite(item.Constellation, rogueConstellations, constellations, document, item.Kind + " " + item.Id + " constellation");
            // An id names one item: a Spark and a Lodestar cannot share it (the saved set stores bare ids).
            if (item is PNRogueSparkDefinition && (rogueLodestars.ContainsKey(item.Id) || lodestars.TryGet(item.Id, out _))) throw new FormatException("Spark " + item.Id + " has the id of a Lodestar.");
            if (item is PNRogueLodestarDefinition && sparks.TryGet(item.Id, out _)) throw new FormatException("Lodestar " + item.Id + " has the id of a Spark.");
        }
        foreach (var spark in rogueSparks.Values)
        {
            if (!spark.Requires.HasValue) continue;
            var required = spark.Requires.Value;
            Reference(required, document, lodestars);
            PNRogueLodestarDefinition lodestar;
            if (!rogueLodestars.TryGetValue(required, out lodestar) && !lodestars.TryGet(required, out lodestar)) throw new FormatException("Spark " + spark.Id + " requires unknown Lodestar " + required);
            if (lodestar.Constellation != spark.Constellation) throw new FormatException("Spark " + spark.Id + " requires Lodestar " + required + " of another constellation.");
        }
    }
}

// EN: The checks that need the sealed registry (run from the game module's ValidateContent, so code-registered and JSON definitions are checked alike): the factory exists and serves the kind,
//     its validator accepts `data` with every registry in place, each modifier names a registered stat or tag (and is accepted by the calculators), items point at a constellation, `requires`
//     names a Lodestar of the same constellation, and no Spark shares an id with a Lodestar.
public static class PNRogueValidation
{
    public static void Validate(EveRegistryHub hub)
    {
        if (hub == null) throw new ArgumentNullException(nameof(hub));
        var factories = hub.Get<PNRogueFactoryDefinition>(); var constellations = hub.Get<PNRogueConstellationDefinition>();
        var sparks = hub.Get<PNRogueSparkDefinition>(); var lodestars = hub.Get<PNRogueLodestarDefinition>();
        foreach (var definition in constellations.Snapshot().Values.Cast<PNRogueDefinition>().Concat(sparks.Snapshot().Values).Concat(lodestars.Snapshot().Values))
        {
            string what = definition.Kind + " " + definition.Id;
            if (!factories.TryGet(definition.FactoryId, out var factory)) throw new FormatException(what + " uses unknown factory " + definition.FactoryId);
            if (!factory.Serves(definition.Kind)) throw new FormatException(what + ": factory " + factory.Id + " does not serve a " + definition.Kind + ".");
            factory.ValidateData(definition.Data, hub);
            foreach (JObject entry in definition.StatsJson) PNStatModifierData.Validate(entry, hub);
        }
        foreach (var item in sparks.Snapshot().Values.Cast<PNRogueItemDefinition>().Concat(lodestars.Snapshot().Values))
            if (!constellations.TryGet(item.Constellation, out _)) throw new FormatException(item.Kind + " " + item.Id + " uses unknown constellation " + item.Constellation);
        // the world features a run gives its rooms must be registered (a mistyped id would silently match no module)
        foreach (var run in hub.Get<PNRunDefinition>().Snapshot().Values)
            foreach (var feature in PNRunFeatures.Of(run).Ids)
                if (!hub.Get<PNWorldFeatureDefinition>().TryGet(feature, out _)) throw new FormatException("Run " + run.Id + " gives its rooms unregistered world feature " + feature + ".");
        foreach (var spark in sparks.Snapshot().Values)
        {
            if (lodestars.TryGet(spark.Id, out _)) throw new FormatException("Spark " + spark.Id + " has the id of a Lodestar.");
            if (!spark.Requires.HasValue) continue;
            if (!lodestars.TryGet(spark.Requires.Value, out var lodestar)) throw new FormatException("Spark " + spark.Id + " requires unknown Lodestar " + spark.Requires.Value);
            if (lodestar.Constellation != spark.Constellation) throw new FormatException("Spark " + spark.Id + " requires Lodestar " + lodestar.Id + " of another constellation.");
        }
    }
}
