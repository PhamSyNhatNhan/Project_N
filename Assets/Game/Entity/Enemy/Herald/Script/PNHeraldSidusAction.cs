#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Herald Sidus: locks movement for windup + release + recovery (the `cast` motion, a phase clock) and, when the windup ends, gives the registered Buff to the targets chosen at the cast start. Each target is
// checked again at that moment (H13): one that died, is no longer an ally or left `radius` is skipped, and the cooldown stays spent. Buffs are applied on the authority only (the Buff replicates by its own adapter).
public sealed class PNHeraldSidusAction : PNSkillAction
{
    private PNSkillActionContext context; private PNStat caster; private PNVisualBody body; private PNSkillOperation movementLock; private PNHeraldPresentation presentation;
    private float windup, release, recovery, radius, age; private EveId buffId; private bool performed, applied, disposed;
    private List<PNStat> targets; private PNBuffManager buffs; private PNBuffHandle guard; private bool hasGuard;
    public override bool HasAuxiliaryTimeline => true;
    public override void Start(PNSkillActionContext c)
    {
        if (context != null || disposed) throw new InvalidOperationException("Fresh cast required");
        PNHeraldCombatContent.Validate(c.Data.Action, c.Registry);
        var p = c.Data.Action.Perform; windup = p["windup"].Value<float>(); release = p["release"].Value<float>(); recovery = p["recovery"].Value<float>(); radius = p["radius"].Value<float>();
        buffId = EveId.Parse(p["buffId"].Value<string>());
        context = c; context.BeginAction(); caster = c.Skill.GetComponent<PNStat>(); body = c.Skill.GetComponent<PNVisualBody>(); presentation = c.Skill.GetComponent<PNHeraldPresentation>();
        targets = c.Targets != null ? c.Targets.Where(t => t != null).ToList() : new List<PNStat>();
        buffs = c.Skill.GetComponent<PNBuffManager>();
        movementLock = CreateOperation(context, EveId.Parse("base:lock_movement")); movementLock.Execute(context, null);
    }
    // The guard (block_hits, 3 hits, 1.5 s) covers the start of the cast; losing it (hits spent or time out) never stops the skill.
    private bool ApplyGuard()
    {
        var g = context.Data.Action.Perform["guard"];
        if (buffs == null || !buffs.IsReady) return false;
        var receipt = buffs.Buffs.Apply(new PNBuffApplyRequest(EveId.Parse(g["buffId"].Value<string>()), PNCombatSource.Capture(caster),
            new JObject { ["duration"] = g["duration"].Value<double>(), ["hits"] = g["hits"].Value<int>() }));
        if (receipt.Status != PNBuffStatus.Applied && receipt.Status != PNBuffStatus.Reapplied) return false;
        guard = receipt.Handle; hasGuard = true; return true;
    }
    public override void Perform(PNSkillActionContext c)
    {
        if (!ReferenceEquals(context, c) || performed || disposed) throw new InvalidOperationException("Invalid Sidus Perform.");
        performed = true; try { ApplyGuard(); } catch (Exception error) { Debug.LogException(error, caster); }      // the guard is a bonus: without it (or once broken) the cast goes on
        body?.PlayAction("cast", windup + release + recovery); presentation?.Cast(0, windup, release);
    }
    public override bool AdvanceAuxiliary(float dt)
    {
        if (disposed) return true;
        if (!performed) return false;
        age += dt; presentation?.Cast(age, windup, release);
        if (!applied && age >= windup) { applied = true; Apply(); }
        return applied && age >= windup + release + recovery;
    }
    private void Apply()
    {
        presentation?.Pulse(radius);
        if (!EveRegistrySession.Context.HasAuthority) return;
        foreach (var target in targets)
        {
            if (target == null || ReferenceEquals(target, caster) || !PNStarchanterSupport.IsAlly(caster, target)) continue;
            if (PNStarchanterSupport.Flat(target.transform.position, caster.transform.position) > radius + 1e-4f) continue;
            var manager = target.GetComponent<PNBuffManager>(); if (manager == null || !manager.IsReady) continue;
            try
            {
                var receipt = manager.Buffs.Apply(new PNBuffApplyRequest(buffId, PNCombatSource.Capture(caster)));
                if (receipt.Status == PNBuffStatus.Applied || receipt.Status == PNBuffStatus.Reapplied) presentation?.Recipient(target.transform.position);
            }
            catch (Exception error) { Debug.LogException(error, target); }
        }
    }
    private void RemoveGuard()
    { try { if (hasGuard && buffs != null && buffs.IsReady && buffs.Buffs.Contains(guard)) buffs.Buffs.Remove(guard, PNBuffEndReason.Dispelled, true); } catch (Exception error) { Debug.LogException(error, buffs); } hasGuard = false; buffs = null; }
    public override void Exit(PNSkillActionContext c) { if (!ReferenceEquals(context, c)) throw new InvalidOperationException("Invalid Sidus Exit."); c.CompleteAction(); Dispose(); }
    public override void Cancel(PNSkillActionContext c) { try { c.CancelEffects(); } finally { Dispose(); } }
    public override void Dispose()
    {
        if (disposed) return; disposed = true;
        try { movementLock?.Dispose(); } finally { movementLock = null; RemoveGuard(); presentation?.EndCast(); presentation = null; body?.EndAction(); body = null; targets = null; }
    }
}

// Builds the candidates of PNHeraldTargets from the entities of a world: living allies of the caster (never the caster), with their kind, whether a Sidus Buff is on them and its remaining time.
public static class PNHeraldSelection
{
    public static List<PNStat> Candidates(PNStat caster, IEnumerable<PNStat> all, float radius, List<PNHeraldCandidate> output)
    {
        var stats = new List<PNStat>(); output.Clear();
        foreach (var s in all)
        {
            if (ReferenceEquals(s, caster) || !PNStarchanterSupport.IsAlly(caster, s)) continue;
            var enemy = s as PNEnemyStat; if (enemy == null) continue;                        // players and pets are not "monsters"
            float d = PNStarchanterSupport.Flat(s.transform.position, caster.transform.position); if (d > radius + 1e-6f) continue;
            bool sidus = false; double remaining = 0; var manager = s.GetComponent<PNBuffManager>();
            if (manager != null && manager.IsReady)
                foreach (var snap in manager.Buffs.Snapshot())
                    if (snap.Tags.Contains(PNSidusBuffContent.Tag)) { sidus = true; remaining = snap.Remaining ?? double.MaxValue; break; }
            stats.Add(s); output.Add(new PNHeraldCandidate { Key = unchecked((int)s.EntityId.Value), TypeId = s.CharacterId.ToString(), Boss = !enemy.CanBeSidus, HasSidus = sidus, Remaining = remaining, Distance = d });
        }
        return stats;
    }
}
#else
// Standalone validates registered schemas; Unity-only physics and rendering execute in native checks.
public sealed class PNHeraldSidusAction : PNSkillAction
{
    public override void Start(PNSkillActionContext c) { PNHeraldCombatContent.Validate(c.Data.Action, c.Registry); }
    public override void Perform(PNSkillActionContext c) { }
    public override void Exit(PNSkillActionContext c) { }
    public override void Cancel(PNSkillActionContext c) { }
}
#endif
