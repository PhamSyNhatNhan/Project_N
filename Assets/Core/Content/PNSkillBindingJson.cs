using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

public sealed partial class PNBuffContentBatch
{
    private void ParseSkillBindings(JObject root,EveContentExtensionDocument document,IReadOnlyList<EveIIdentified> pending)
    {
        foreach(var token in Array(root["skillBindingProfiles"]))
        {
            var obj=Object(token);Fields(obj,"id","entrySkillId","behaviorId","data","input","actionDataIds","abilities","resetAbilitiesOnDeath","displayAbilityIds");
            var id=Owned(String(obj,"id"),document);
            if(skillProfiles.ContainsKey(id)||hub.Get<PNSkillBindingProfile>().TryGet(id,out _))throw new FormatException("Duplicate binding profile: "+id);
            var inputObj=Object(obj["input"]);Fields(inputObj,"CanTap","PressImmediately","HoldMode","TapDuration","HoldDuration","SlowTapDuration","RepeatDelay","RepeatInterval");
            foreach(var field in inputObj.Properties())
            {
                if(field.Name=="CanTap"||field.Name=="PressImmediately"){if(field.Value.Type!=JTokenType.Boolean)throw new FormatException("Boolean input field required: "+field.Name);}
                else if(field.Name=="HoldMode"){if(field.Value.Type!=JTokenType.String)throw new FormatException("Input HoldMode string required.");}
                else if(field.Value.Type!=JTokenType.Integer&&field.Value.Type!=JTokenType.Float)throw new FormatException("Numeric input field required: "+field.Name);
            }
            var input=inputObj.ToObject<PlayerSkillInputConfig>();input.Validate();
            var ids=Array(obj["actionDataIds"]).Select(x=>EveId.Parse(x.Value<string>())).ToArray();
            var profile=new PNSkillBindingProfile(id,EveId.Parse(String(obj,"entrySkillId")),EveId.Parse(String(obj,"behaviorId")),obj["data"],input,ids,
                obj["abilities"]==null?System.Array.Empty<PNAbilityDefinition>():PNAbilityDefinition.Parse(obj["abilities"].ToString()),Bool(obj,"resetAbilitiesOnDeath"),Array(obj["displayAbilityIds"]).Select(x=>EveId.Parse(x.Value<string>())));
            skillProfiles.Add(id,profile);
            profile.SourceDocument=document;
            checks.Add(()=>{
                var refs=References(document,pending);
                // Skill base aliases may be declared in module FinalizeContent, after this staged batch.
                // All references are checked again, with document dependencies, before hub publication.
                if(profile.EntrySkillId.Namespace!="base"||hub.Skills.TryGet(profile.EntrySkillId,out _))refs.Require<EveSkillDefinition>(profile.EntrySkillId,"entrySkillId");
                refs.Require<PNSkillBehaviorDefinition>(profile.BehaviorId,"behaviorId");
                foreach(var action in profile.ActionDataIds)if(action.Namespace!="base"||hub.Skills.TryGet(action,out _))refs.Require<EveSkillDefinition>(action,"actionDataIds");
                hub.Get<PNSkillBehaviorDefinition>().Get(profile.BehaviorId).ValidatePayload(profile,hub);
            });
        }
        foreach(var token in Array(root["skillBaseBindings"]))
        {
            var obj=Object(token);Fields(obj,"characterId","slots");var id=Owned(String(obj,"characterId"),document);
            if(skillMappings.ContainsKey(id)||hub.Get<PNSkillBaseBindings>().TryGet(id,out _))throw new FormatException("Duplicate base bindings: "+id);
            var slots=Object(obj["slots"]).Properties().Select(x=>new KeyValuePair<EveId,EveId>(EveId.Parse(x.Name),EveId.Parse(x.Value.Value<string>()))).ToArray();
            var mapping=new PNSkillBaseBindings(id,slots);skillMappings.Add(id,mapping);
            checks.Add(()=>{var refs=References(document,pending);refs.Require<EveCharacterDefinition>(id,"characterId");foreach(var profile in mapping.Slots.Values)refs.Require<PNSkillBindingProfile>(profile,"slots");});
        }
    }
}
