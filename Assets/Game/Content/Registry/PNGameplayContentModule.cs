using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: Registers concrete game actions and operations, separate from Core contracts.
// VI: Đăng ký action và operation cụ thể của game, tách khỏi hợp đồng nền Core.
public class PNGameplayContentModule : EveIContentModule, EveIContentFinalizeModule, EveIContentReferenceValidator, EveIContentExtension
{
    private readonly List<(EveId id,string owner,IReadOnlyCollection<string> dependencies)> motionReferences=new List<(EveId,string,IReadOnlyCollection<string>)>();
    public static readonly EveId EveSlash = EveId.Parse("project_n:eve_slash");
    public static readonly EveId SlashAction = EveId.Parse("project_n:slash_action");
    public static readonly EveId SlashVfxOperation = EveId.Parse("project_n:slash_vfx");
    public static readonly EveId LockMovementOperation = EveId.Parse("project_n:lock_movement");
    public static readonly EveId DamageAction = EveId.Parse("project_n:damage_action");
    public static readonly EveId SlashHitOperation = EveId.Parse("project_n:slash_hit");
    public static readonly EveId HealOperation = EveId.Parse("project_n:heal");
    public static readonly EveId EveAttackEntry = EveId.Parse("project_n:eve_attack_entry");
    public static readonly EveId EveSkillEntry = EveId.Parse("project_n:eve_skill_entry");
    public static readonly EveId EveUltimateEntry = EveId.Parse("project_n:eve_ultimate_entry");
    public static readonly EveId EveBurstEntry = EveId.Parse("project_n:eve_burst_entry");

    public static readonly EveId SweepAction=EveId.Parse("project_n:eve_sweep_action");
    public static readonly EveId MotionAction=EveId.Parse("project_n:motion_action");
    public static readonly EveId PlayerDash=EveId.Parse("project_n:player_dash");
    public static readonly EveId EveDash=EveId.Parse("project_n:eve_dash");
    public static readonly EveId PlayerDashRecovery=EveId.Parse("project_n:player_dash_ready");

    public virtual void Register(EveRegistryHub hub)
    {
#if UNITY_5_3_OR_NEWER
        // A new content build starts without the previous build's mod prefabs (mods register theirs after the base modules).
        PNEntityPrefabs.ClearMods();
        var worlds=hub.GetOrCreate<PNWorldDefinition>();
        var transferParticipants=hub.GetOrCreate<PNWorldTransferParticipantDefinition>();
        transferParticipants.Register(new PNWorldTransferParticipantDefinition(EveId.Parse("project_n:world_gameplay"),typeof(PNWorldGameplayTransferParticipant),()=>new PNWorldGameplayTransferParticipant()));
        transferParticipants.RegisterBase(EveId.Parse("base:world_gameplay"),EveId.Parse("project_n:world_gameplay"),"project_n",compatible:(a,b)=>true);
        var stateProviders=hub.GetOrCreate<PNWorldStateDefinition>();
        stateProviders.Register(new PNWorldStateDefinition(EveId.Parse("project_n:actor_world_state"),1,8*1024*1024,typeof(PNActorWorldStateProvider),()=>new PNActorWorldStateProvider()));
        stateProviders.RegisterBase(EveId.Parse("base:actor_world_state"),EveId.Parse("project_n:actor_world_state"),"project_n",compatible:(a,b)=>a.Schema==b.Schema);
        worlds.Register(new PNWorldDefinition(EveId.Parse("project_n:scene_world"),PNWorldDefinition.HashContent("project_n:scene_world/v1/Assets/Game/Scenes/Test/ErMode/ErMode.unity"),typeof(PNSceneWorldProvider),()=>new PNSceneWorldProvider("Assets/Game/Scenes/Test/ErMode/ErMode.unity")));
        worlds.RegisterBase(EveId.Parse("base:scene_world"),EveId.Parse("project_n:scene_world"),"project_n",compatible:(a,b)=>true);
        worlds.Register(new PNWorldDefinition(EveId.Parse("project_n:hall_world"),PNWorldDefinition.HashContent("project_n:hall_world/v1/"+PNScenes.Hall),typeof(PNSceneWorldProvider),()=>new PNSceneWorldProvider(PNScenes.Hall)));
        worlds.RegisterBase(EveId.Parse("base:hall_world"),EveId.Parse("project_n:hall_world"),"project_n",compatible:(a,b)=>true);
        // The lobby exists only to gather a roguelite party; once everyone moved on, it is discarded.
        worlds.Register(new PNWorldDefinition(EveId.Parse("project_n:roguelite_lobby"),PNWorldDefinition.HashContent("project_n:roguelite_lobby/v1/"+PNScenes.Lobby),typeof(PNSceneWorldProvider),()=>new PNSceneWorldProvider(PNScenes.Lobby),new PNWorldPolicy(PNEmptyWorldPolicy.Discard)));
        worlds.RegisterBase(EveId.Parse("base:roguelite_lobby"),EveId.Parse("project_n:roguelite_lobby"),"project_n",compatible:(a,b)=>true);
        worlds.Register(new PNWorldDefinition(EveId.Parse("project_n:generated_world"),PNWorldDefinition.HashContent("project_n:generated_world/v1/empty-main"),typeof(PNGeneratedWorldProvider),()=>new PNGeneratedWorldProvider()));
        worlds.RegisterBase(EveId.Parse("base:generated_world"),EveId.Parse("project_n:generated_world"),"project_n",compatible:(a,b)=>true);
        var modes=hub.GetOrCreate<PNModeDefinition>();
        var rogueliteListing=new PNModeListing("Roguelite","Vượt qua các căn phòng, một mình hoặc cùng bạn bè trong mạng LAN.",new[]{231,185,143},"",0,
            new[]{new PNModeStartOption("solo","Chơi đơn",PNModeStart.Offline),new PNModeStartOption("host","Tạo phòng LAN",PNModeStart.Host),new PNModeStartOption("join","Vào phòng LAN",PNModeStart.Join)});
        modes.Register(new PNModeDefinition(EveId.Parse("project_n:roguelite"),typeof(PNRoguelitePolicy),profile=>new PNRoguelitePolicy(profile),rogueliteListing));
        modes.RegisterBase(EveId.Parse("base:roguelite"),EveId.Parse("project_n:roguelite"),"project_n",compatible:(original,replacement)=>true);
        modes.Register(new PNModeDefinition(EveId.Parse("project_n:hall"),typeof(PNHallPolicy),profile=>new PNHallPolicy(profile)));
        modes.RegisterBase(EveId.Parse("base:hall"),EveId.Parse("project_n:hall"),"project_n",compatible:(original,replacement)=>true);
        var screens=hub.GetOrCreate<PNTransitionScreenDefinition>();
        screens.Register(new PNTransitionScreenDefinition(PNTransitionSelector.DefaultScreen,PNTransitionKind.UiToolkit,PNTransitionPayload.Default,typeof(PNUiToolkitTransitionScreen).FullName,payload=>new PNUiToolkitTransitionScreen(payload)));
        screens.RegisterBase(EveId.Parse("base:default_transition"),PNTransitionSelector.DefaultScreen,"project_n",compatible:(a,b)=>true);
        screens.Register(new PNTransitionScreenDefinition(EveId.Parse("project_n:no_transition"),PNTransitionKind.None,PNTransitionPayload.Default,"none",payload=>null));
        screens.RegisterBase(PNTransitionSelector.NoScreen,EveId.Parse("project_n:no_transition"),"project_n",compatible:(a,b)=>true);
#endif
        PNGameSkillBindings.RegisterBehaviors(hub);
        PNBuffGameplayContent.Register(hub);
        PNInstances.Create(hub);
        PNAreaGameplayContent.Register(hub);
        PNProjectileGameplayContent.Register(hub);
        PNEntityGameplayContent.Register(hub);
        PNSummonGameplayContent.Register(hub);
        PNEnemyGameplayContent.Register(hub);
        PNStarweaverCombatContent.Register(hub);
        PNStarkeeperCombatContent.Register(hub);
#if UNITY_5_3_OR_NEWER
        PNAgentToolkitContent.Register(hub);
#endif
        PNShaderWarmupContent.Register(hub);
        motionReferences.Clear();
        // Activation identities are not action profiles and do not duplicate their tuning.
        foreach (var entry in new[] { EveAttackEntry, EveSkillEntry, EveUltimateEntry, EveBurstEntry })
        {
            string category = entry == EveAttackEntry ? "attack" : entry == EveSkillEntry ? "skill" : entry == EveUltimateEntry ? "ultimate" : "burst";
            hub.GetOrCreate<EveSkillDefinition>().Register(new EveSkillDefinition(entry, new JObject { ["categoryIds"] = new JArray("base:" + category), ["affectedByAttackSpeed"] = entry == EveAttackEntry }));
        }
        hub.GetOrCreate<PNSkillActionDefinition>().Register(
            new PNSkillActionDefinition(SlashAction, typeof(PNSlashAction), ValidateEffectAction, PNSkillContextRequirements.Direction));
        hub.Actions().Register(new PNSkillActionDefinition(DamageAction, typeof(PNDamageAction), ValidateEffectAction, PNSkillContextRequirements.Targets));
        hub.Actions().Register(new PNSkillActionDefinition(SweepAction, typeof(EveSweepAction), ValidateEffectAction, PNSkillContextRequirements.Direction));
        hub.Actions().Register(new PNSkillActionDefinition(MotionAction,typeof(PNMotionAction), ValidateEffectAction, PNSkillContextRequirements.Direction));
        hub.Actions().Register(new PNSkillActionDefinition(EveId.Parse("project_n:buff_action"), typeof(PNBuffAction), ValidateEffectAction, PNSkillContextRequirements.None,
            (data, context) => { var obj = PNBuffContentBatch.Object(data); context.Require<PNSkillOperationDefinition>(obj["operationId"], "operationId"); context.Registry.Operations().Get(context.Read(obj["operationId"], "operationId")).ValidateReferences(obj["data"], context); }));
        hub.GetOrCreate<EveSkillDefinition>().Register(new EveSkillDefinition(PlayerDash,JObject.Parse(@"{
          'categoryIds':['base:dash'],'affectedByAttackSpeed':false,
          'skill': {'recoveryId':'project_n:player_dash_ready','applyCooldownReduction':true,
            'abilities':[{'id':'project_n:player_dash_ready','kind':'Cooldown','showInUI':true,'iconPath':'','displayOrder':0,'maximum':3,'initialAmount':3,
              'cooldown':5,'recovery':'PerStack','autoRecover':true,'clock':'Game'}]},
          'action': {'id':'base:motion_action','start':{},'perform':{'distance':1.5,'duration':0.16,'ignoreEntities':true,'permissionId':'base:motion_voluntary'},'exit':{},'cancel':{}}
        }")));
        var operations = hub.GetOrCreate<PNSkillOperationDefinition>();
        operations.Register(new PNSkillOperationDefinition(EveId.Parse("project_n:apply_buff"), typeof(PNApplyBuffOperation), (data, content) => { PNApplyBuffOperation.Validate(data, content); }, PNSkillContextRequirements.Targets,
            (data, context) => context.Require<PNBuffDefinition>(PNBuffContentBatch.Object(data)["buffId"], "buffId"), sourceIndependent: true));
        operations.Register(new PNSkillOperationDefinition(EveId.Parse("project_n:remove_buff"), typeof(PNRemoveBuffOperation), (data, content) => { PNRemoveBuffOperation.Validate(data, content); }, PNSkillContextRequirements.None,
            (data, context) => context.Require<PNBuffDefinition>(PNBuffContentBatch.Object(data)["buffId"], "buffId"), sourceIndependent: true));
        operations.Register(new PNSkillOperationDefinition(EveId.Parse("project_n:bypass_remove_buff"), typeof(PNBypassRemoveBuffOperation), (data, content) => { PNRemoveBuffOperation.Validate(data, content); }, PNSkillContextRequirements.None,
            (data, context) => context.Require<PNBuffDefinition>(PNBuffContentBatch.Object(data)["buffId"], "buffId"), sourceIndependent: true));
        operations.Register(new PNSkillOperationDefinition(EveId.Parse("project_n:trigger_buff"), typeof(PNTriggerBuffOperation), (data, content) => { PNTriggerBuffOperation.Validate(data, content); }, PNSkillContextRequirements.None,
            (data, context) => context.Require<PNBuffDefinition>(PNBuffContentBatch.Object(data)["buffId"], "buffId"), sourceIndependent: true));
        operations.Register(new PNSkillOperationDefinition(EveId.Parse("project_n:change_buff_stacks"), typeof(PNChangeBuffStacksOperation), (data, content) => { PNTriggerBuffOperation.Validate(data, content, true); }, PNSkillContextRequirements.None,
            (data, context) => context.Require<PNBuffDefinition>(PNBuffContentBatch.Object(data)["buffId"], "buffId"), sourceIndependent: true));
        operations.Register(new PNSkillOperationDefinition(SlashVfxOperation, typeof(PNSlashVfxOperation)));
        operations.Register(new PNSkillOperationDefinition(LockMovementOperation, typeof(PNLockMovementOperation)));
        operations.Register(new PNSkillOperationDefinition(SlashHitOperation, typeof(PNSlashHitOperation)));
        operations.Register(new PNSkillOperationDefinition(HealOperation, typeof(PNHealOperation), (data, content) => PNHealSettings.Parse(data).ValidateContent(content)));
        foreach (var definition in hub.Actions().Snapshot().Values)
            if (definition.Id.Namespace == "project_n")
                hub.Actions().RegisterBase(new EveId("base", definition.Id.Path), definition.Id, "project_n",
                    compatible: (original, replacement) => original.ImplementationType.IsAssignableFrom(replacement.ImplementationType));
        foreach (var definition in operations.Snapshot().Values)
            if (definition.Id.Namespace == "project_n")
                operations.RegisterBase(new EveId("base", definition.Id.Path), definition.Id, "project_n",
                    compatible: (original, replacement) => original.ImplementationType.IsAssignableFrom(replacement.ImplementationType));
    }

    public virtual void FinalizeContent(EveRegistryHub hub)
    {
        PNGameSkillBindings.Register(hub);
        PNSummonGameplayContent.RegisterBindings(hub);
        foreach(var behavior in hub.Get<PNSkillBehaviorDefinition>().Snapshot().Values.Where(x=>x.Id.Namespace=="project_n"))
            hub.Get<PNSkillBehaviorDefinition>().RegisterBase(new EveId("base",behavior.Id.Path),behavior.Id,"project_n",compatible:(original,replacement)=>true);
        foreach(var profile in hub.Get<PNSkillBindingProfile>().Snapshot().Values.Where(x=>x.Id.Namespace=="project_n"))
            hub.Get<PNSkillBindingProfile>().RegisterBase(new EveId("base",profile.Id.Path),profile.Id,"project_n",compatible:(original,replacement)=>true);
        foreach (var definition in hub.Buffs().Snapshot().Values)
            if (definition.Id.Namespace == "project_n" && !hub.Buffs().BindingSnapshot().Any(x => x.Identity == definition.Id))
                hub.Buffs().RegisterBase(new EveId("base", definition.Id.Path), definition.Id, "project_n", compatible: (original, replacement) => true);
        foreach (var definition in hub.Characters.Snapshot().Values)
            if (definition.Id.Namespace == "project_n")
                hub.Characters.RegisterBase(new EveId("base", definition.Id.Path), definition.Id, "project_n");
        foreach (var definition in hub.Skills.Snapshot().Values)
            if (definition.Id.Namespace == "project_n")
                hub.Skills.RegisterBase(new EveId("base", definition.Id.Path), definition.Id, "project_n",
                    compatible:definition.Id==PlayerDash?(Func<EveSkillDefinition,EveSkillDefinition,bool>)CompatiblePlayerDash:null);
    }

    private static bool CompatiblePlayerDash(EveSkillDefinition original,EveSkillDefinition replacement)
    {
        try
        {
            var data=PNSkillData.Parse(replacement.Data);
            var settings=PNPlayerDashSettings.Parse(data);
            var abilities=PNAbilityDefinition.Parse(((JObject)data.Skill)["abilities"].ToString());
            return data.Action.Id==PNSkillData.Parse(original.Data).Action.Id &&
                abilities.Count(x=>x.Id==settings.RecoveryId && x.Kind==PNAbilityKind.Cooldown)==1;
        }
        catch(Exception error) when(error is FormatException || error is ArgumentException || error is NullReferenceException){return false;}
    }

    // EN: Validate recognized Core envelopes against the selected action before publishing a session.
    private static void ValidateEffectAction(PNSkillActionData data, EveRegistryHub hub)
    {
        var implementation = hub.Actions().Get(data.Id).ImplementationType;
        if (typeof(PNBuffAction).IsAssignableFrom(implementation)) PNBuffAction.Validate(data.Perform, hub);
#if UNITY_5_3_OR_NEWER
        else if (typeof(PNAreaAction).IsAssignableFrom(implementation)) PNAreaAction.Validate(data.Perform, hub);
        else if (typeof(PNProjectileAction).IsAssignableFrom(implementation)) PNProjectileAction.Validate(data.Perform, hub);
#endif
        else if (typeof(PNMotionAction).IsAssignableFrom(implementation)) hub.ControlPermissions().Get(PNMotionSettings.Parse(data.Perform).PermissionId);
        else if (typeof(EveSweepAction).IsAssignableFrom(implementation))
        { EveSweepSettings.Parse(data.Perform).Values.ValidateStats(hub); new PNStatSet(hub).ResolveDamageType(data.PerformTags); }
        else if (typeof(PNDamageAction).IsAssignableFrom(implementation))
        { PNSkillValues.Parse((data.Perform as JObject)?["value"]).ValidateStats(hub); PNDamage.ParseIFrameDuration(data.Perform as JObject); new PNStatSet(hub).ResolveDamageType(data.PerformTags); }
        else if (typeof(PNSlashAction).IsAssignableFrom(implementation))
        { var hit = PNSlashHitSettings.Parse(data.Perform); if (hit != null) { hit.Values.ValidateStats(hub); new PNStatSet(hub).ResolveDamageType(data.PerformTags); } }
    }

    // VI: Kiểm tra khung Core được nhận diện với action đã chọn trước khi công bố phiên.
    public virtual void ValidateContent(EveRegistryHub hub)
    {
#if UNITY_5_3_OR_NEWER
        PNEntityPrefabReferences.Validate(hub, PNEntityPrefabs.Source);
        foreach(var world in hub.Get<PNWorldDefinition>().Snapshot().Values)
        {
            foreach(var id in world.Policy.TransferParticipants)hub.Get<PNWorldTransferParticipantDefinition>().Get(id);
            foreach(var id in world.Policy.ActorStateProviders.Concat(world.Policy.WorldStateProviders))hub.Get<PNWorldStateDefinition>().Get(id);
        }
#endif
        PNSkillBindingContent.Validate(hub);
        PNPetSettings.ValidateAll(hub);
        foreach (var category in hub.SkillCategories().Snapshot().Values) category.Validate(hub);
        foreach (var definition in hub.Buffs().Snapshot().Values) definition.Validate(hub);
        foreach (var definition in hub.Instances().Snapshot().Values) definition.Validate(hub);
        foreach(var reference in motionReferences)ValidateReference(reference.id,hub.Skills,reference.owner,reference.dependencies);
        foreach (var definition in hub.Skills.Snapshot().Values)
        {
            if (!(definition.Data is JObject root)) continue;
            if (root["categoryIds"] != null || root["affectedByAttackSpeed"] != null || root["effects"] != null) PNSkillMetadata.Parse(root, hub);
            if (!(root["action"] is JObject)) continue;
            var data = PNSkillData.Parse(root);
            var action = hub.Actions().Get(data.Action.Id);
            if (typeof(PNBuffAction).IsAssignableFrom(action.ImplementationType)) PNBuffAction.Validate(data.Action.Perform, hub);
#if UNITY_5_3_OR_NEWER
            if (typeof(PNAreaAction).IsAssignableFrom(action.ImplementationType)) PNAreaAction.Validate(data.Action.Perform, hub);
            else if (typeof(PNProjectileAction).IsAssignableFrom(action.ImplementationType)) PNProjectileAction.Validate(data.Action.Perform, hub);
            else if (typeof(PNSummonAction).IsAssignableFrom(action.ImplementationType)) PNSummonAction.Validate(data.Action.Perform, hub);
#endif
            data.Action.ValidateTags(hub);
            var metadata = data.Skill as JObject;
            if (metadata?["abilities"] != null) PNAbilityDefinition.Parse(metadata["abilities"].ToString());
            if (metadata?["tuning"] != null && definition.Id == EveId.Parse("project_n:eve_moveset"))
            {
                var moveset=EveMovesetSettings.Parse(metadata["tuning"]);
                hub.Buffs().Get(moveset.BlazingAssaultBuffId); hub.Buffs().Get(moveset.RisingMomentumBuffId);
                PNSkillData Motion(EveId id)
                {
                    var selected=PNSkillData.Parse(hub.Skills.GetEffective(id).Data);
                    if(!typeof(PNMotionAction).IsAssignableFrom(hub.Actions().Get(selected.Action.Id).ImplementationType))throw new FormatException("Eve motion profile requires a motion-compatible action: "+id);
                    return selected;
                }
                moveset.ResolveMotionProfiles(Motion,PNPlayerDashSettings.Parse(Motion(EveId.Parse("base:eve_dash"))).Motion);
            }
            if(typeof(PNMotionAction).IsAssignableFrom(action.ImplementationType)) hub.ControlPermissions().Get(PNMotionSettings.Parse(data.Action.Perform).PermissionId);
            if(metadata?["recoveryId"]!=null)PNPlayerDashSettings.Parse(data);
            if (typeof(EveSweepAction).IsAssignableFrom(action.ImplementationType))
            {
                var sweep = EveSweepSettings.Parse(data.Action.Perform);
                sweep.Values.ValidateStats(hub);
                new PNStatSet(hub).ResolveDamageType(data.Action.PerformTags);
            }
            if (typeof(PNSlashAction).IsAssignableFrom(action.ImplementationType))
            {
                var hit = PNSlashHitSettings.Parse(data.Action.Perform);
#if UNITY_5_3_OR_NEWER
                PNSlashPrefabContent.Validate(new PNSlashVfxSettings(data.Action.Perform), hit != null);
#endif
                if (hit != null)
                {
                    hit.Values.ValidateStats(hub);
                    new PNStatSet(hub).ResolveDamageType(data.Action.PerformTags);
                }
            }
            if (typeof(PNDamageAction).IsAssignableFrom(action.ImplementationType))
            {
                PNDamage.ParseIFrameDuration(data.Action.Perform as JObject);
                var values = PNSkillValues.Parse((data.Action.Perform as JObject)?["value"]);
                values.ValidateStats(hub);
            }
        }
    }

    public EveIContentExtensionBatch PrepareExtensions(IReadOnlyList<EveContentExtensionDocument> documents, EveRegistryHub hub, IReadOnlyList<EveIIdentified> pendingDefinitions) => new PNBuffContentBatch(documents, hub, pendingDefinitions);
    public string ExtensionKey => "project_n";

    public virtual void ValidateReferences(EveId skillId, object payload, string contentNamespace,
        IReadOnlyCollection<string> dependencies, EveRegistryHub hub)
    {
        if (!(payload is JObject root)) return;
        foreach (var category in PNSkillEffect.Ids(root["categoryIds"])) ValidateReference(category, hub.SkillCategories(), contentNamespace, dependencies);
        ValidateEffectReferences(root["effects"], hub, contentNamespace, dependencies);
        if (!(root["action"] is JObject)) return;
        var data = PNSkillData.Parse(root);
        ValidateReference(data.Action.Id, hub.Actions(), contentNamespace, dependencies);
        foreach (var id in data.Action.StartTags.Concat(data.Action.PerformTags).Concat(data.Action.ExitTags).Concat(data.Action.CancelTags))
            ValidateReference(id, hub.Tags(), contentNamespace, dependencies);
        var tuning=(data.Skill as JObject)?["tuning"] as JObject;
        foreach(var key in new[]{"skillMotion","empoweredMotion","repeatMotion","ultimateMotion","retreatMotion"})
            if(tuning?[key]?.Type==JTokenType.String)motionReferences.Add((EveId.Parse(tuning[key].Value<string>()),contentNamespace,dependencies));
        foreach (var key in new[] { "blazingAssaultBuffId", "risingMomentumBuffId" })
            if (tuning?[key]?.Type == JTokenType.String) ValidateReference(EveId.Parse(tuning[key].Value<string>()), hub.Buffs(), contentNamespace, dependencies);
        foreach (var phasePayload in new[] { data.Action.Start, data.Action.Perform, data.Action.Exit, data.Action.Cancel })
        {
            var operation = (phasePayload as JObject)?["operationId"];
            if (operation?.Type == JTokenType.String)
            {
                ValidateReference(EveId.Parse(operation.Value<string>()), hub.Operations(), contentNamespace, dependencies);
                var buffId = (phasePayload as JObject)?["data"]?["buffId"];
                if (buffId?.Type == JTokenType.String) ValidateReference(EveId.Parse(buffId.Value<string>()), hub.Buffs(), contentNamespace, dependencies);
                var areaId = (phasePayload as JObject)?["data"]?["areaId"];
                if (areaId?.Type == JTokenType.String) ValidateReference(EveId.Parse(areaId.Value<string>()), hub.Instances(), contentNamespace, dependencies);
                var projectileId = (phasePayload as JObject)?["data"]?["projectileId"];
                if (projectileId?.Type == JTokenType.String) ValidateReference(EveId.Parse(projectileId.Value<string>()), hub.Instances(), contentNamespace, dependencies);
            }
            if ((phasePayload as JObject)?["permissionId"]?.Type == JTokenType.String)
                ValidateReference(EveId.Parse(phasePayload["permissionId"].Value<string>()), hub.ControlPermissions(), contentNamespace, dependencies);
            var value = (phasePayload as JObject)?["value"];
            if (value == null) continue;
            var components = value is JArray array ? (System.Collections.Generic.IEnumerable<JToken>)array : new[] { value };
            foreach (var component in components)
                if ((component as JObject)?["statId"]?.Type == JTokenType.String)
                    ValidateReference(EveId.Parse(component["statId"].Value<string>()), hub.Stats, contentNamespace, dependencies);
        }
    }

    private static void ValidateReference<T>(EveId id, EveRegistry<T> registry, string contentNamespace,
        IReadOnlyCollection<string> dependencies) where T : class, EveIIdentified
    {
        string owner = id.Namespace == "base" ? registry.BindingOwner(id) : id.Namespace;
        if (contentNamespace == null || dependencies == null || owner == contentNamespace || owner == "eve" || dependencies.Contains(owner)) return;
        throw new FormatException($"Reference {id} requires pack {contentNamespace} to depend on {owner}.");
    }
    private static void ValidateEffectReferences(JToken token, EveRegistryHub hub, string owner, IReadOnlyCollection<string> dependencies)
    {
        if (token is JObject obj)
        {
            if (obj["kind"]?.Type == JTokenType.String && obj["id"]?.Type == JTokenType.String)
            {
                var id = EveId.Parse(obj["id"].Value<string>());
                if (obj["kind"].Value<string>() == "Action") ValidateReference(id, hub.Actions(), owner, dependencies);
                else ValidateReference(id, hub.Operations(), owner, dependencies);
            }
            if (obj["buffId"]?.Type == JTokenType.String) ValidateReference(EveId.Parse(obj["buffId"].Value<string>()), hub.Buffs(), owner, dependencies);
            if (obj["areaId"]?.Type == JTokenType.String) ValidateReference(EveId.Parse(obj["areaId"].Value<string>()), hub.Instances(), owner, dependencies);
            if (obj["projectileId"]?.Type == JTokenType.String) ValidateReference(EveId.Parse(obj["projectileId"].Value<string>()), hub.Instances(), owner, dependencies);
            if (obj["statId"]?.Type == JTokenType.String) ValidateReference(EveId.Parse(obj["statId"].Value<string>()), hub.Stats, owner, dependencies);
            foreach (var tag in PNSkillEffect.Ids(obj["tags"])) ValidateReference(tag, hub.Tags(), owner, dependencies);
            foreach (var value in obj.Properties()) ValidateEffectReferences(value.Value, hub, owner, dependencies);
        }
        else if (token is JArray array) foreach (var value in array) ValidateEffectReferences(value, hub, owner, dependencies);
    }
}
