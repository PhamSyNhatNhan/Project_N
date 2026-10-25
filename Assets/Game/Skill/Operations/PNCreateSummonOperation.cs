using System;
using EveEngine;
using Newtonsoft.Json.Linq;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// EN: What happens when a summoning skill's group is full.
// VI: Khi nhóm của skill triệu hồi đã đầy.
public enum PNSummonFullPolicy { ReplaceOldest, Reject }
// EN: Outcome of one create_summon.
// VI: Kết quả một lần create_summon.
public enum PNSummonStatus { Pending, Spawning, Rejected, QueueFull, NoAuthority }

// Payload of create_summon (Docs/SummonPlan.md section 3.2); unknown fields are rejected:
//   character (required): a registered character, what the summon IS (stats, prefab key in its `body` block, its own behavior data),
//   entity (optional, default project_n:summon): the entity role it is built as; a role that is a player is rejected,
//   lifetime (optional, seconds > 0 or null = until removed): decided by the summoning skill, not by the role,
//   group (optional, 1..64 characters): limit group, counted together per summoner,
//   limit (optional, 1..64, needs group): most summons of the summoner in the group; no limit when absent,
//   whenFull (optional, "replaceOldest" default | "reject", needs limit): what a full group does with a new summon,
//   offset (optional, [x,y,z] meters, default [0,0,0]): world-space offset from the caster where the summon appears.
public sealed class PNCreateSummonSettings
{
    public const int MaxGroupLength = 64, MaxLimit = 64;
    public EveId Entity, Character;
    public double? Lifetime;
    public string Group = "";
    public int Limit;
    public PNSummonFullPolicy WhenFull = PNSummonFullPolicy.ReplaceOldest;
    public double[] Offset = { 0d, 0d, 0d };
    public static readonly EveId DefaultEntity = EveId.Parse("project_n:summon");

    public static PNCreateSummonSettings Parse(JToken token)
    {
        var obj = PNBuffContentBatch.Object(token); PNBuffContentBatch.Fields(obj, "character", "entity", "lifetime", "group", "limit", "whenFull", "offset");
        var result = new PNCreateSummonSettings();
        if (!EveId.TryParse(PNBuffContentBatch.String(obj, "character"), out result.Character)) throw new FormatException("create_summon character must be a namespaced id.");
        result.Entity = DefaultEntity;
        if (obj["entity"] != null && !EveId.TryParse(PNBuffContentBatch.String(obj, "entity"), out result.Entity)) throw new FormatException("create_summon entity must be a namespaced id.");
        if (obj["lifetime"] != null && obj["lifetime"].Type != JTokenType.Null)
        {
            double seconds = PNBuffContentBatch.Number(obj, "lifetime");
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0d) throw new FormatException("create_summon lifetime must be finite and above 0, or null.");
            result.Lifetime = seconds;
        }
        if (obj["group"] != null)
        {
            result.Group = PNBuffContentBatch.String(obj, "group");
            if (result.Group.Length < 1 || result.Group.Length > MaxGroupLength) throw new FormatException("create_summon group must be 1.." + MaxGroupLength + " characters.");
        }
        if (obj["limit"] != null)
        {
            if (result.Group.Length == 0) throw new FormatException("create_summon limit needs a group.");
            result.Limit = PNBuffContentBatch.Integer(obj, "limit");
            if (result.Limit < 1 || result.Limit > MaxLimit) throw new FormatException("create_summon limit must be 1.." + MaxLimit + ".");
        }
        if (obj["whenFull"] != null)
        {
            if (result.Limit == 0) throw new FormatException("create_summon whenFull needs a limit.");
            switch (PNBuffContentBatch.String(obj, "whenFull"))
            {
                case "replaceOldest": result.WhenFull = PNSummonFullPolicy.ReplaceOldest; break;
                case "reject": result.WhenFull = PNSummonFullPolicy.Reject; break;
                default: throw new FormatException("create_summon whenFull must be replaceOldest or reject.");
            }
        }
        if (obj["offset"] != null)
        {
            var list = PNBuffContentBatch.Array(obj["offset"]);
            var values = new System.Collections.Generic.List<double>();
            foreach (var item in list)
            {
                if (item.Type != JTokenType.Integer && item.Type != JTokenType.Float) throw new FormatException("create_summon offset needs numbers.");
                double value = item.Value<double>();
                if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > 1000d) throw new FormatException("create_summon offset must be finite and within 1000 m.");
                values.Add(value);
            }
            if (values.Count != 3) throw new FormatException("create_summon offset needs three numbers.");
            result.Offset = values.ToArray();
        }
        return result;
    }
}

// Spawns one summon of the caster through its session's entity host. The summon is an ordinary entity (role + character) with the PNSummon component added
// while it is configured, so a pooled object starts clean and a returned one forgets its owner.
public class PNCreateSummonOperation : PNSkillOperation
{
    private bool executed;
    public PNSummonStatus Status { get; private set; } = PNSummonStatus.Pending;
    // How many older summons of the group this cast removed to make room.
    public int Replaced { get; private set; }
#if UNITY_5_3_OR_NEWER
    public EveEntityPreparationScheduler.Candidate Candidate { get; private set; }
#endif
    public static void Validate(JToken token, EveRegistryHub hub)
    {
        var settings = PNCreateSummonSettings.Parse(token);
        var role = hub.Instances().GetEffectiveKind<PNEntityDefinition>(settings.Entity);
        if (role.IsPlayer) throw new FormatException("create_summon entity " + settings.Entity + " is a player role.");
    }
    public static void ValidateReferences(JToken token, PNContentReferenceContext context)
    {
        var obj = PNBuffContentBatch.Object(token);
        context.Require<EveCharacterDefinition>(obj["character"], "character");
        if (obj["entity"] != null) context.Require<PNEntityDefinition>(obj["entity"], "entity");
    }
    public override void Execute(PNSkillActionContext context, JToken token)
    {
        if (executed || context == null || context.Skill == null || !context.Skill.IsReady) throw new InvalidOperationException("Fresh Ready context required.");
        Validate(token, context.Registry); executed = true;
#if UNITY_5_3_OR_NEWER
        // Clients render the action but only the machine with authority creates summons.
        if (!EveRegistrySession.Context.HasAuthority) { Status = PNSummonStatus.NoAuthority; return; }
        var settings = PNCreateSummonSettings.Parse(token);
        var caster = context.Skill.GetComponent<PNStat>();
        if (caster == null || !caster.IsReady) throw new InvalidOperationException("Summons need the caster's Ready stats.");
        var session = EveRegistrySession.Context; var host = PNEntityHosts.For(session);
        var owner = caster.EntityId;
        // A full group: remove the oldest summons first (their removal frees the slot at once, see PNSummons.Count) or refuse the new one.
        if (settings.Limit > 0)
            while (PNSummons.Count(owner, settings.Group) >= settings.Limit)
            {
                var oldest = settings.WhenFull == PNSummonFullPolicy.ReplaceOldest ? PNSummons.Oldest(owner, settings.Group) : null;
                if (oldest == null || !oldest.Retire()) { Status = PNSummonStatus.Rejected; return; }
                Replaced++;
            }
        var source = PNCombatSource.Capture(caster);
        double scale = source.Snapshot.Calculate(1d, PNContentIds.SummonScale);
        if (!(scale > 0d) || double.IsInfinity(scale)) scale = 1d;
        var identity = caster.GetComponent<EveEntityIdentity>(); var marker = caster.GetComponent<PNFactoryEntity>();
        var request = new PNEntityRequest
        {
            Entity = settings.Entity.ToString(), Template = settings.Entity.ToString(), Character = settings.Character.ToString(), World = identity.World, WorldIncarnation = identity.World.Incarnation,
            ContentSlot = identity.ContentSlot, SceneKey = string.IsNullOrWhiteSpace(marker?.SceneKey) ? "summon:runtime" : marker.SceneKey, EntityKey = "",
            Position = caster.transform.position + new Vector3((float)settings.Offset[0], (float)settings.Offset[1], (float)settings.Offset[2]),
            Lifetime = settings.Lifetime, Summoner = owner
        };
        EveEntityPreparationScheduler.Candidate candidate = null;
        candidate = host.TrySpawn(request, loader =>
        {
            var summon = loader.GetComponent<PNSummon>() ?? loader.gameObject.AddComponent<PNSummon>();
            summon.Begin(source, owner, settings.Group, PNSummons.NextSequence(), scale);
            PNSummons.Fulfil(candidate);
        }, null, error => Debug.LogException(error));
        if (candidate == null) { Status = PNSummonStatus.QueueFull; return; }
        PNSummons.Reserve(owner, settings.Group, candidate);
        Candidate = candidate; Status = PNSummonStatus.Spawning;
#else
        // Standalone runner validates invocation/payload only; it has no Unity entity runtime.
#endif
    }
    public override void Dispose() { executed = true; }
}
