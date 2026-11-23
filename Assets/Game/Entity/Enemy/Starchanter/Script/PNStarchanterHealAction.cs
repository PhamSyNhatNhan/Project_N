#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Starchanter heal: it vanishes (teleportOut), reappears next to the chosen ally (the Position of the cast is the landing point; the cast's first target is the ally, the caster itself when it heals
// alone and does not move), stands still (teleportIn then `duration` seconds of channel) with a block_hits guard on itself and heals every ally in `radius` (itself too): `tickPercent` of each one's
// maximum health every `interval`, and `finalPercent` when the channel ends. If the guard is lost (its hits spent, or removed) the cast ends at once and the final heal is skipped.
// Every number is the registered payload; the cooldown is spent when the cast starts and never refunded.
// The picture (vanish/arrive/channel, the teleport light, the healing zone and its pulses) is `PNStarchanterHealView`, placed by the run's age on every machine; this Action keeps the teleport, the guard and the healing
// and presents one `heal` fact per pulse (final flag + up to 3 recipient ids).
public sealed class PNStarchanterHealAction : PNSkillAction
{
    private PNSkillActionContext context; private PNStat caster, ally; private Vector3 landing; private PNMove move; private PNSkillOperation movementLock;
    private PNBuffManager buffs; private PNBuffHandle guard; private bool hasGuard, guardSkipped;
    private float outSeconds, inSeconds, duration, interval, tickPercent, finalPercent, radius, recovery;
    private float age, channelAge, nextTick, recoverAge; private bool performed, teleported, channeling, finished, disposed, aborted;
    public override bool HasAuxiliaryTimeline => true;
    public override void Start(PNSkillActionContext c)
    {
        if (context != null || disposed) throw new InvalidOperationException("Fresh cast required");
        PNStarchanterCombatContent.ValidateHeal(c.Data.Action, c.Registry);
        if (!c.Position.HasValue || !c.Direction.HasValue) throw new InvalidOperationException("Landing point and direction required");
        var p = c.Data.Action.Perform;
        outSeconds = p["teleportOut"].Value<float>(); inSeconds = p["teleportIn"].Value<float>(); duration = p["duration"].Value<float>(); interval = p["interval"].Value<float>();
        tickPercent = p["tickPercent"].Value<float>(); finalPercent = p["finalPercent"].Value<float>(); radius = p["radius"].Value<float>(); recovery = p["recovery"].Value<float>();
        context = c; context.BeginAction(); caster = c.Skill.GetComponent<PNStat>(); landing = c.Position.Value;
        ally = c.Targets != null ? c.Targets.FirstOrDefault() : null; if (ally == null) ally = caster;
        move = c.Skill.GetComponent<PNMove>(); buffs = c.Skill.GetComponent<PNBuffManager>();
        nextTick = interval;
    }
    public override void Perform(PNSkillActionContext c) { if (!ReferenceEquals(context, c) || performed || disposed) throw new InvalidOperationException("Invalid heal Perform."); performed = true; }
    public override bool AdvanceAuxiliary(float dt)
    {
        if (disposed) return true;
        if (!performed) return false;
        age += dt;
        if (!teleported)
        {
            if (age < outSeconds) return false;
            teleported = true;
            if (!ReferenceEquals(ally, caster) && move != null)
            {
                try { move.TeleportToPoint(new Vector2(landing.x, landing.z), 0f); }
                catch (Exception error) { Debug.LogException(error, caster); aborted = true; return true; }
            }
            movementLock = CreateOperation(context, EveId.Parse("base:lock_movement")); movementLock.Execute(context, null);
        }
        if (age < outSeconds + inSeconds) return false;
        if (!channeling)
        {
            channeling = true;
            if (!ApplyGuard()) { aborted = true; return true; }
        }
        if (finished) { recoverAge += dt; return recoverAge >= recovery; }
        channelAge += dt;
        if (!GuardAlive()) { aborted = true; return true; }                                   // the guard broke: the cast ends, no final heal
        while (nextTick < duration - 1e-4f && channelAge >= nextTick) { HealZone(tickPercent); nextTick += interval; }
        if (channelAge < duration) return false;
        HealZone(finalPercent); finished = true;
        return recovery <= 0f;
    }
    private bool ApplyGuard()
    {
        var g = context.Data.Action.Perform["guard"];
        if (buffs == null || !buffs.IsReady) return false;
        var outcome = context.ApplyBuff(buffs, EveId.Parse(g["buffId"].Value<string>()), caster,
            new JObject { ["duration"] = g["duration"].Value<double>(), ["hits"] = g["hits"].Value<int>() });
        if (!outcome.Applied) { guardSkipped = true; return true; }                // this machine does not hold the guard: the authority does, and the picture goes on
        if (!outcome.Succeeded) return false;
        guard = outcome.Receipt.Handle; hasGuard = true; return true;
    }
    private bool GuardAlive() => guardSkipped || (hasGuard && buffs != null && buffs.IsReady && buffs.Buffs.Contains(guard));
    private void HealZone(float fraction)
    {
        var world = context.World; if (world == null) return;
        var healed = new List<double> { channelAge >= duration ? 1d : 0d };
        var all = world.Session.Entities.Snapshot(world).Select(e => e.GetComponent<PNStat>()).Where(s => s != null);
        foreach (var target in PNStarchanterSupport.Within(caster, caster.transform.position, radius, all))
        {
            double amount = PNStarchanterSupport.HealAmount(target, fraction); if (amount <= 0d) continue;
            try { if (context.Heal(target, new PNHeal(target.SessionId, caster.EntityId, amount)).Applied && healed.Count <= PNStarchanterHealView.MaxRecipients) healed.Add(target.EntityId.Value); }
            catch (Exception error) { Debug.LogException(error, target); }
        }
        context.Present(PNStarchanterHealView.HealFact, healed.ToArray());          // one pulse of the zone; the view finds the recipients by id
    }
    public override void Exit(PNSkillActionContext c) { if (!ReferenceEquals(context, c)) throw new InvalidOperationException("Invalid heal Exit."); c.CompleteAction(); Dispose(); }
    public override void Cancel(PNSkillActionContext c) { try { c.CancelEffects(); } finally { Dispose(); } }
    public override void Dispose()
    {
        if (disposed) return; disposed = true;
        try { movementLock?.Dispose(); } finally { movementLock = null; }
    }
}

// Starchanter boost: a short stand-still, then speed_boost on every ally within `radius` of the Starchanter and on itself (each one its own instance). The behavior walks to the ally first.
// The picture is `PNStarchanterBoostView` (gathering light and the pulse by age); this Action gives the Buff and presents a `boost` fact [recipient ids].
public sealed class PNStarchanterBoostAction : PNSkillAction
{
    private PNSkillActionContext context; private PNStat caster; private PNSkillOperation movementLock;
    private float windup, recovery, radius, age; private EveId buffId; private bool performed, applied, disposed;
    public override bool HasAuxiliaryTimeline => true;
    public override void Start(PNSkillActionContext c)
    {
        if (context != null || disposed) throw new InvalidOperationException("Fresh cast required");
        PNStarchanterCombatContent.ValidateBoost(c.Data.Action, c.Registry);
        var p = c.Data.Action.Perform; windup = p["windup"].Value<float>(); recovery = p["recovery"].Value<float>(); radius = p["radius"].Value<float>(); buffId = EveId.Parse(p["buffId"].Value<string>());
        context = c; context.BeginAction(); caster = c.Skill.GetComponent<PNStat>();
        movementLock = CreateOperation(context, EveId.Parse("base:lock_movement")); movementLock.Execute(context, null);
    }
    public override void Perform(PNSkillActionContext c) { if (!ReferenceEquals(context, c) || performed || disposed) throw new InvalidOperationException("Invalid boost Perform."); performed = true; }
    public override bool AdvanceAuxiliary(float dt)
    {
        if (disposed) return true;
        if (!performed) return false;
        age += dt;
        if (!applied && age >= windup) { applied = true; Apply(); }
        return applied && age >= windup + recovery;
    }
    private void Apply()
    {
        var world = context.World; if (world == null) return;
        var boosted = new List<double>();
        var all = world.Session.Entities.Snapshot(world).Select(e => e.GetComponent<PNStat>()).Where(s => s != null);
        var moveSpeed = context.Registry.Stats.ResolveIdentity(EveId.Parse("base:move_speed"));
        foreach (var target in PNStarchanterSupport.Within(caster, caster.transform.position, radius, all))
        {
            var manager = target.GetComponent<PNBuffManager>(); if (manager == null || !manager.IsReady) continue;
            if (!target.Set.TryGetBase(moveSpeed, out _)) continue;                  // a dummy or a pet without a move speed has nothing to speed up
            try { if (context.ApplyBuff(manager, buffId, caster).Succeeded && boosted.Count < PNStarchanterBoostView.MaxRecipients) boosted.Add(target.EntityId.Value); }
            catch (Exception error) { Debug.LogException(error, target); }
        }
        context.Present(PNStarchanterBoostView.BoostFact, boosted.ToArray());
    }
    public override void Exit(PNSkillActionContext c) { if (!ReferenceEquals(context, c)) throw new InvalidOperationException("Invalid boost Exit."); c.CompleteAction(); Dispose(); }
    public override void Cancel(PNSkillActionContext c) { try { c.CancelEffects(); } finally { Dispose(); } }
    public override void Dispose()
    {
        if (disposed) return; disposed = true;
        try { movementLock?.Dispose(); } finally { movementLock = null; }
    }
}
#else
// Standalone validates registered schemas; Unity-only physics and rendering execute in native checks.
public sealed class PNStarchanterHealAction : PNSkillAction
{
    public override void Start(PNSkillActionContext c) { PNStarchanterCombatContent.ValidateHeal(c.Data.Action, c.Registry); }
    public override void Perform(PNSkillActionContext c) { }
    public override void Exit(PNSkillActionContext c) { }
    public override void Cancel(PNSkillActionContext c) { }
}
public sealed class PNStarchanterBoostAction : PNSkillAction
{
    public override void Start(PNSkillActionContext c) { PNStarchanterCombatContent.ValidateBoost(c.Data.Action, c.Registry); }
    public override void Perform(PNSkillActionContext c) { }
    public override void Exit(PNSkillActionContext c) { }
    public override void Cancel(PNSkillActionContext c) { }
}
#endif
