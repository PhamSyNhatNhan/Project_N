using System;
using System.Linq;
using EveEngine;

public static class PNSkillBindingContent
{
    public static void RegisterRegistries(EveRegistryHub hub)
    {
        hub.GetOrCreate<PNSkillBehaviorDefinition>();hub.NameRegistry<PNSkillBehaviorDefinition>("skill_behaviors");
        hub.GetOrCreate<PNSkillBindingProfile>();hub.NameRegistry<PNSkillBindingProfile>("skill_binding_profiles");
        hub.GetOrCreate<PNSkillBaseBindings>();hub.NameRegistry<PNSkillBaseBindings>("skill_base_bindings");
    }
    public static void Validate(EveRegistryHub hub)
    {
        foreach(var profile in hub.Get<PNSkillBindingProfile>().Snapshot().Values)profile.Validate(hub);
        foreach(var mapping in hub.Get<PNSkillBaseBindings>().Snapshot().Values)
        {hub.Characters.Get(mapping.Id);foreach(var profile in mapping.Slots.Values)hub.Get<PNSkillBindingProfile>().Get(profile);}
    }
}
