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
public sealed class PNStarchanterHealAction : PNSkillAction
{
    private PNSkillActionContext context; private PNStat caster, ally; private Vector3 landing; private PNMove move; private PNVisualBody body; private PNSkillOperation movementLock;
    private PNStarchanterPresentation presentation;
    private PNBuffManager buffs; private PNBuffHandle guard; private bool hasGuard;
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
        move = c.Skill.GetComponent<PNMove>(); buffs = c.Skill.GetComponent<PNBuffManager>(); body = c.Skill.GetComponent<PNVisualBody>();
        presentation = c.Skill.GetComponent<PNStarchanterPresentation>();
        nextTick = interval;
    }
    public override void Perform(PNSkillActionContext c) { if (!ReferenceEquals(context, c) || performed || disposed) throw new InvalidOperationException("Invalid heal Perform."); performed = true; body?.PlayAction("heal_teleport_out", outSeconds); presentation?.Heal(0, outSeconds, inSeconds, duration, interval, radius); }
    public override bool AdvanceAuxiliary(float dt)
    {
        if (disposed) return true;
        if (!performed) return false;
        age += dt;
        presentation?.Heal(age, outSeconds, inSeconds, duration, interval, radius);
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
            presentation?.TeleportArrived();
            body?.PlayAction("heal_teleport_in", inSeconds);
        }
        if (age < outSeconds + inSeconds) return false;
        if (!channeling)
        {
            channeling = true; body?.PlayAction("heal_channel", duration + .05f);
            if (!ApplyGuard()) { aborted = true; return true; }
        }
        if (finished) { recoverAge += dt; return recoverAge >= recovery; }
        channelAge += dt;
        if (!GuardAlive()) { aborted = true; return true; }                                   // the guard broke: the cast ends, no final heal
        while (nextTick < duration - 1e-4f && channelAge >= nextTick) { HealZone(tickPercent); nextTick += interval; }
        if (channelAge < duration) return false;
        HealZone(finalPercent); finished = true; body?.EndAction();
        return recovery <= 0f;
    }
    private bool ApplyGuard()
    {
        var g = context.Data.Action.Perform["guard"];
        if (buffs == null || !buffs.IsReady) return false;
        var receipt = buffs.Buffs.Apply(new PNBuffApplyRequest(EveId.Parse(g["buffId"].Value<string>()), PNCombatSource.Capture(caster),
            new JObject { ["duration"] = g["duration"].Value<double>(), ["hits"] = g["hits"].Value<int>() }));
        if (receipt.Status != PNBuffStatus.Applied && receipt.Status != PNBuffStatus.Reapplied) return false;
        guard = receipt.Handle; hasGuard = true; return true;
    }
    private bool GuardAlive() => hasGuard && buffs != null && buffs.IsReady && buffs.Buffs.Contains(guard);
    private void HealZone(float fraction)
    {
        if (!EveRegistrySession.Context.HasAuthority) return;
        var world = context.World; if (world == null) return;
        presentation?.PulseReady();
        var all = world.Session.Entities.Snapshot(world).Select(e => e.GetComponent<PNStat>()).Where(s => s != null);
        foreach (var target in PNStarchanterSupport.Within(caster, caster.transform.position, radius, all))
        {
            double amount = PNStarchanterSupport.HealAmount(target, fraction); if (amount <= 0d) continue;
            try { target.ReceiveHeal(new PNHeal(target.SessionId, caster.EntityId, amount)); presentation?.HealRecipient(target.transform.position, channelAge >= duration); }
            catch (Exception error) { Debug.LogException(error, target); }
        }
    }
    public override void Exit(PNSkillActionContext c) { if (!ReferenceEquals(context, c)) throw new InvalidOperationException("Invalid heal Exit."); c.CompleteAction(); Dispose(); }
    public override void Cancel(PNSkillActionContext c) { try { c.CancelEffects(); } finally { Dispose(); } }
    public override void Dispose()
    {
        if (disposed) return; disposed = true;
        try { movementLock?.Dispose(); } finally { movementLock = null; presentation?.EndCast(finished && !aborted); presentation = null; body?.EndAction(); body = null; }
    }
}

// Starchanter boost: a short stand-still, then speed_boost on every ally within `radius` of the Starchanter and on itself (each one its own instance). The behavior walks to the ally first.
public sealed class PNStarchanterBoostAction : PNSkillAction
{
    private PNSkillActionContext context; private PNStat caster; private PNVisualBody body; private PNSkillOperation movementLock;
    private PNStarchanterPresentation presentation;
    private float windup, recovery, radius, age; private EveId buffId; private bool performed, applied, disposed;
    public override bool HasAuxiliaryTimeline => true;
    public override void Start(PNSkillActionContext c)
    {
        if (context != null || disposed) throw new InvalidOperationException("Fresh cast required");
        PNStarchanterCombatContent.ValidateBoost(c.Data.Action, c.Registry);
        var p = c.Data.Action.Perform; windup = p["windup"].Value<float>(); recovery = p["recovery"].Value<float>(); radius = p["radius"].Value<float>(); buffId = EveId.Parse(p["buffId"].Value<string>());
        context = c; context.BeginAction(); caster = c.Skill.GetComponent<PNStat>(); body = c.Skill.GetComponent<PNVisualBody>(); presentation = c.Skill.GetComponent<PNStarchanterPresentation>();
        movementLock = CreateOperation(context, EveId.Parse("base:lock_movement")); movementLock.Execute(context, null);
    }
    public override void Perform(PNSkillActionContext c) { if (!ReferenceEquals(context, c) || performed || disposed) throw new InvalidOperationException("Invalid boost Perform."); performed = true; body?.PlayAction("heal_channel", windup + recovery); presentation?.Boost(0, windup, radius); }
    public override bool AdvanceAuxiliary(float dt)
    {
        if (disposed) return true;
        if (!performed) return false;
        age += dt; presentation?.Boost(age, windup, radius);
        if (!applied && age >= windup) { applied = true; Apply(); }
        return applied && age >= windup + recovery;
    }
    private void Apply()
    {
        if (!EveRegistrySession.Context.HasAuthority) return;
        var world = context.World; if (world == null) return;
        presentation?.BoostPulse();
        var all = world.Session.Entities.Snapshot(world).Select(e => e.GetComponent<PNStat>()).Where(s => s != null);
        var moveSpeed = context.Registry.Stats.ResolveIdentity(EveId.Parse("base:move_speed"));
        foreach (var target in PNStarchanterSupport.Within(caster, caster.transform.position, radius, all))
        {
            var manager = target.GetComponent<PNBuffManager>(); if (manager == null || !manager.IsReady) continue;
            if (!target.Set.TryGetBase(moveSpeed, out _)) continue;                  // a dummy or a pet without a move speed has nothing to speed up
            try { var receipt = manager.Buffs.Apply(new PNBuffApplyRequest(buffId, PNCombatSource.Capture(caster)));
                if (receipt.Status == PNBuffStatus.Applied || receipt.Status == PNBuffStatus.Reapplied)
                { presentation?.BoostRecipient(target.transform.position); } }
            catch (Exception error) { Debug.LogException(error, target); }
        }
    }
    public override void Exit(PNSkillActionContext c) { if (!ReferenceEquals(context, c)) throw new InvalidOperationException("Invalid boost Exit."); c.CompleteAction(); Dispose(); }
    public override void Cancel(PNSkillActionContext c) { try { c.CancelEffects(); } finally { Dispose(); } }
    public override void Dispose()
    {
        if (disposed) return; disposed = true;
        try { movementLock?.Dispose(); } finally { movementLock = null; presentation?.EndCast(applied); presentation = null; body?.EndAction(); body = null; }
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
