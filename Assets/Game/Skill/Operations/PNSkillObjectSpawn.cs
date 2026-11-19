using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

internal enum PNSkillSpawnAdmission { Skipped, Authority, Predicted }

// Admission and spawning for the registered skill Operations. Managers retain
// their own capacity, submission receipts and lifetime rules.
internal static class PNSkillObjectSpawn
{
    internal static PNSkillSpawnAdmission Admit(PNSkillActionContext context)
    {
        if (context.Mode == PNSkillMode.Predicted) return PNSkillSpawnAdmission.Predicted;
        if (context.Mode != PNSkillMode.Authority || !EveRegistrySession.Context.HasAuthority)
            return PNSkillSpawnAdmission.Skipped;
        return PNSkillSpawnAdmission.Authority;
    }

#if UNITY_5_3_OR_NEWER
    // `predicted`: the shooter's own guess on a machine without authority (plan 4.4a): same volley, but no damage, no OnHit plan; it names itself by the cast key and reports what it hits.
    internal static void CreateProjectiles(PNSkillActionContext context, JToken token, List<PNProjectileReceipt> results, bool predicted = false)
    {
        var settings = PNCreateProjectileSettings.Parse(token); var caster = context.Skill.GetComponent<PNStat>();
        if (caster == null || !caster.IsReady) throw new InvalidOperationException("Projectiles need the caster's Ready stats.");
        // A guess has no authoritative stats to capture (a client cannot apply combat effects): a source without a snapshot carries the shooter's id and layers, and the launch factors come from the replicated skill factors.
        var source = predicted ? PNCombatSource.System(caster.SessionId, caster.EntityId, caster.TargetLayers) : PNCombatSource.Capture(caster);
        PNPreparedDamage prepared = null;
        if (settings.Values != null && !predicted)
        {
            // One amount for the whole volley (sum of the components), one prepared template; the crit roll is per hit.
            double amount = 0d; foreach (var component in settings.Values.Components) amount += component.Evaluate(caster);
            var tags = context.EffectiveTags().Concat(settings.Tags).Concat(new[] { PNContentIds.ProjectileDamage }).ToArray();
            prepared = source.Snapshot.PrepareDamage(amount, settings.CanCrit, settings.IFrameDuration, tags);
        }
        var plan = context.CanFreezeOnHit && !predicted ? context.Skill.Effects.FreezeOnHit(context) : null;
        var launchLayers = source.TargetLayers.Union(settings.TargetLayers);
        if (launchLayers.IsEmpty) throw new InvalidOperationException("create_projectile: the caster targets no layer and the payload adds none.");
        var rule = PNTargetRules.For(caster);
        PNProjectileLaunch launch;
        if (predicted)
        {
            var factors = context.Skill.GetComponent<PNSkillScaleState>();
            launch = new PNProjectileLaunch(settings.ProjectileId, settings.Input, source, launchLayers, null, null, factors?.ReadScale(PNContentIds.ProjectileSpeed) ?? 1d,
                factors?.ReadScale(PNContentIds.ProjectileScale) ?? 1d, factors?.ReadScale(PNContentIds.ProjectileLifetime) ?? 1d, rule);
        }
        else launch = PNProjectileLaunch.FromSource(settings.ProjectileId, settings.Input, source, launchLayers, prepared, plan, rule);
        long castKey = context.Invocation != null ? context.Invocation.CastKey : 0L;
        // A guess is named by the cast; the authority's real projectile of a shooter whose machine guesses (a remote player: the skill has a decision source) leaves target hits to that machine's reports.
        if (predicted) launch = launch.AsPrediction(castKey, context.Skill.ReportSink);
        else if (castKey > 0) launch = launch.WithCast(castKey, context.Skill.DecisionSource != null && context.Invocation.ShooterGuessed);
        var scene = caster.gameObject.scene;
        PNProjectileManager manager;
        // A machine that has no projectile service yet (a fixture, a scene still loading) makes no guess; the authority's record still shows the projectile.
        if (predicted) { try { manager = PNProjectileManager.Resolve(EveRegistrySession.Context, scene); } catch (InvalidOperationException) { return; } }
        else manager = PNProjectileManager.Resolve(EveRegistrySession.Context, scene);
        if (!ReferenceEquals(manager.Session.Registry, context.Registry)) throw new InvalidOperationException("Projectile operation session mismatch.");
        var position = context.Position ?? caster.transform.position;
        var aim = context.Direction.Value; PNStat aimTarget = null;
        if (settings.AimRadius.HasValue)
        {
            aimTarget = PNTargetSelector.Select(manager.Targets.Acquire(context.World), new PNTargetQuery
            {
                Origin = position, Range = (float)settings.AimRadius.Value, LayerMask = launchLayers.Mask, Rule = rule, Exclude = caster.EntityId, Planar = true
            });
            if (aimTarget != null)
            {
                var toTarget = aimTarget.transform.position - position; toTarget.y = 0;
                if (toTarget.sqrMagnitude > 1e-8f) aim = toTarget;
            }
        }
        for (int i = 0; i < settings.Count; i++)
        {
            float angle = (float)((i - (settings.Count - 1) / 2d) * settings.Spread);
            var heading = Quaternion.AngleAxis(angle, Vector3.up) * aim;
            if (settings.Pitches != null && settings.Pitches[i] != 0)
            {
                // Tilt about the horizontal axis perpendicular to the heading: positive pitch raises the nose.
                var side = Vector3.Cross(Vector3.up, heading);
                if (side.sqrMagnitude > 1e-8f) heading = Quaternion.AngleAxis(-(float)settings.Pitches[i], side.normalized) * heading;
            }
            var receipt = manager.Create(launch, new PNProjectileSpawn(scene, position, heading, aimTarget, Math.Min(i, PNProjectileManager.MaxReportedIndex)));
            results.Add(receipt);
            if (receipt.Status == PNProjectileStatus.Failed) throw new InvalidOperationException("Projectile creation failed.", receipt.Error);
        }
    }

    internal static PNAreaReceipt CreateArea(PNSkillActionContext context, JToken token)
    {
        var data = (JObject)token; var caster = context.Skill.GetComponent<PNStat>();
        var manager = PNAreaManager.Resolve(EveRegistrySession.Context, caster.gameObject.scene);
        if (!ReferenceEquals(manager.Session.Registry, context.Registry)) throw new InvalidOperationException("Area operation session mismatch.");
        var request = new PNAreaCreateRequest(EveId.Parse(PNBuffContentBatch.String(data, "areaId")), data["input"],
            PNCombatSource.Capture(caster), caster.gameObject.scene, context.Position.Value, caster,
            PNBuffContentBatch.Bool(data, "followCaster") ? caster : null);
        // The cast names the area in its record, so the shooter's own guess (CreateAreaGuess) is taken over instead of a second one being drawn.
        if (context.Invocation != null) request.WithCast(context.Invocation.CastKey, (int)Math.Min(context.ActionOrdinal, 255L));
        return manager.Create(request);
    }

    // The shooter's guess of an area on a machine without authority: drawn only (no Buff, no clock effect), named by the cast, taken over by the authority's record. A machine without an area service makes none.
    internal static void CreateAreaGuess(PNSkillActionContext context, JToken token)
    {
        long castKey = context.Invocation != null ? context.Invocation.CastKey : 0L;
        if (castKey <= 0) return;
        var data = (JObject)token; var caster = context.Skill.GetComponent<PNStat>();
        if (caster == null || !caster.IsReady) return;
        PNAreaManager manager;
        try { manager = PNAreaManager.Resolve(EveRegistrySession.Context, caster.gameObject.scene); } catch (InvalidOperationException) { return; }
        if (!ReferenceEquals(manager.Session.Registry, context.Registry)) throw new InvalidOperationException("Area operation session mismatch.");
        var factors = context.Skill.GetComponent<PNSkillScaleState>();
        var request = PNAreaCreateRequest.Replica(EveId.Parse(PNBuffContentBatch.String(data, "areaId")), data["input"], caster.gameObject.scene, context.Position.Value, caster,
            PNBuffContentBatch.Bool(data, "followCaster") ? caster : null, factors?.ReadScale(PNContentIds.AreaScale) ?? 1d).WithCast(castKey, (int)Math.Min(context.ActionOrdinal, 255L));
        var receipt = manager.CreateGuess(request);
        if (receipt.Status == PNAreaStatus.Failed) Debug.LogException(receipt.Error);
    }

    internal static PNBlastReceipt CreateBlast(PNSkillActionContext context, JToken token)
    {
        var data = (JObject)token; var caster = context.Skill.GetComponent<PNStat>();
        var manager = PNBlastManager.Resolve(EveRegistrySession.Context, caster.gameObject.scene);
        if (!ReferenceEquals(manager.Session.Registry, context.Registry)) throw new InvalidOperationException("Blast operation session mismatch.");
        var request = new PNBlastCreateRequest(EveId.Parse(PNBuffContentBatch.String(data, "blastId")), data["input"], PNCombatSource.Capture(caster),
            caster.gameObject.scene, context.Position.Value, caster, PNCreateBlastOperation.ParseDependency(data));
        if (context.Invocation != null) request.WithCast(context.Invocation.CastKey, (int)Math.Min(context.ActionOrdinal, 255L));
        return manager.Create(request);
    }

    // The shooter's guess of a blast on a machine without authority: drawn only (never any damage), named by the cast, taken over by the authority's record. Null when this machine has no blast service or no cast name.
    internal static PNBlastReceipt CreateBlastGuess(PNSkillActionContext context, JToken token)
    {
        long castKey = context.Invocation != null ? context.Invocation.CastKey : 0L;
        if (castKey <= 0) return null;
        var data = (JObject)token; var caster = context.Skill.GetComponent<PNStat>();
        if (caster == null || !caster.IsReady) return null;
        PNBlastManager manager;
        try { manager = PNBlastManager.Resolve(EveRegistrySession.Context, caster.gameObject.scene); } catch (InvalidOperationException) { return null; }
        if (!ReferenceEquals(manager.Session.Registry, context.Registry)) throw new InvalidOperationException("Blast operation session mismatch.");
        var factors = context.Skill.GetComponent<PNSkillScaleState>();
        var request = PNBlastCreateRequest.Replica(EveId.Parse(PNBuffContentBatch.String(data, "blastId")), data["input"], caster.gameObject.scene, context.Position.Value, caster,
            factors?.ReadScale(PNContentIds.BlastScale) ?? 1d).WithCast(castKey, (int)Math.Min(context.ActionOrdinal, 255L));
        var receipt = manager.CreateGuess(request);
        if (receipt.Status == PNBlastStatus.Failed) Debug.LogException(receipt.Error);
        return receipt;
    }

    internal static PNSummonStatus CreateSummon(PNSkillActionContext context, JToken token, ref int replaced, out EveEntityPreparationScheduler.Candidate accepted)
    {
        accepted = null;
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
                if (oldest == null || !oldest.Retire()) return PNSummonStatus.Rejected;
                replaced++;
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
            Lifetime = settings.Lifetime, Summoner = owner, Scale = scale
        };
        EveEntityPreparationScheduler.Candidate candidate = null;
        candidate = host.TrySpawn(request, loader =>
        {
            var summon = loader.GetComponent<PNSummon>() ?? loader.gameObject.AddComponent<PNSummon>();
            summon.Begin(source, owner, settings.Group, PNSummons.NextSequence(), scale);
            PNSummons.Fulfil(candidate);
        }, null, error => Debug.LogException(error));
        if (candidate == null) return PNSummonStatus.QueueFull;
        PNSummons.Reserve(owner, settings.Group, candidate);
        accepted = candidate;
        return PNSummonStatus.Spawning;
    }
#endif
}
