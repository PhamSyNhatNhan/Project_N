using EveEngine;
using Newtonsoft.Json.Linq;

// EN: The built-in entity roles (Docs/EntitySpawnPlan.md section 3.2). Each is a definition in the shared instances registry plus a public base: alias.
//     base:scene_spawn is the old name of the player role: checkpoints and the coop wire still carry it in their `template` field.
// VI: Các vai trò entity dựng sẵn. base:scene_spawn là tên cũ của vai trò người chơi.
public static class PNEntityGameplayContent
{
    public static readonly EveId Player = EveId.Parse("project_n:player");
    public static readonly EveId Enemy = EveId.Parse("project_n:enemy");
    public static readonly EveId Npc = EveId.Parse("project_n:npc");
    public static readonly EveId Summon = EveId.Parse("project_n:summon");
    public static readonly EveId Prop = EveId.Parse("project_n:prop");

    private static JObject Payload(string owner, bool persist, bool followsOwner, bool countsForClear, bool dropsLoot)
        => new JObject { ["owner"] = owner, ["persist"] = persist, ["followsOwner"] = followsOwner, ["lifetime"] = JValue.CreateNull(), ["countsForClear"] = countsForClear, ["dropsLoot"] = dropsLoot };

    public static void Register(EveRegistryHub hub)
    {
        var instances = hub.Instances();
        instances.Register(new PNEntityDefinition(Player, typeof(PNPlayerEntityRecipe), () => new PNPlayerEntityRecipe(), Payload("peer", true, true, false, false)));
        instances.Register(new PNEntityDefinition(Enemy, typeof(PNHostEntityRecipe), () => new PNHostEntityRecipe(), Payload("host", true, false, true, true)));
        instances.Register(new PNEntityDefinition(Npc, typeof(PNHostEntityRecipe), () => new PNHostEntityRecipe(), Payload("host", true, false, false, false)));
        instances.Register(new PNEntityDefinition(Summon, typeof(PNHostEntityRecipe), () => new PNHostEntityRecipe(), Payload("host", false, false, false, false)));
        instances.Register(new PNEntityDefinition(Prop, typeof(PNHostEntityRecipe), () => new PNHostEntityRecipe(), Payload("host", true, false, false, false)));
        // One public alias per identity: the player role keeps only the old template name below.
        foreach (var id in new[] { Enemy, Npc, Summon, Prop })
            instances.RegisterBase<PNEntityDefinition>(new EveId("base", id.Path), id, "project_n", compatible: (original, replacement) => true);
        instances.RegisterBase<PNEntityDefinition>(EveId.Parse("base:scene_spawn"), Player, "project_n", compatible: (original, replacement) => true);
    }
}
