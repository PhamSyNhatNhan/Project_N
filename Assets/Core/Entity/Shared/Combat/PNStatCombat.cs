using System;
using System.Linq;
using EveEngine;
using UnityEngine;

// EN: Local authoritative damage flow; hitboxes and actions call this shared entity API.
// VI: Luồng damage nội bộ có thẩm quyền; hitbox và action gọi API entity dùng chung này.
public partial class PNStat
{
    private PNAbilityCollection combatAbilities;
    private PNAbilityState iframeState;
    private static readonly PNAbilityDefinition iframeDefinition = new PNAbilitySettings {
        Id = "project_n:iframe", Kind = PNAbilityKind.State, Clock = PNAbilityClock.Game, ShowInUI = false
    }.Create();
    public virtual double DefaultIFrameDuration => .2d;
    public double IFrameRemaining => iframeState != null && iframeState.IsAttached && iframeState.IsActive
        ? iframeState.DurationRemaining.GetValueOrDefault() : 0d;
    public const double IFrameDamageMultiplier = .3d;

    private void InitializeCombatAbilities()
    {
        combatAbilities?.Dispose();
        combatAbilities = new PNAbilityCollection();
        iframeState = (PNAbilityState)combatAbilities.Define(iframeDefinition);
    }
    public void AdvanceCombatTime(double gameDelta)
    {
        PNAbilityDefinition.RequireSeconds(gameDelta, true);
        if (IsReady && EveRegistrySession.IsReady && (loadContext==null||loadContext.CanSimulate)) combatAbilities?.Advance(gameDelta);
    }
    protected virtual void Update() => AdvanceCombatTime(Time.deltaTime);

    public event Action<PNDamageResult> DamageReceived;
    public event Action<PNDamageResult> Died;
    public virtual bool IsDead => GetCurrent(PNContentIds.Health) <= 0d;

    // EN: Capture outgoing bonuses once so delayed effects remain valid after the source unloads.
    // VI: Chụp bonus phía gây một lần để hiệu ứng trì hoãn vẫn dùng được sau khi nguồn unload.
    public virtual PNDamage CreateDamage(double amount, params EveId[] tags)
        => CreateDamage(amount, true, tags);

    private static readonly System.Random criticalRandom = new System.Random();
    // Roll only at the authoritative sender, once per immutable damage packet.
    protected virtual double NextCriticalRoll() => criticalRandom.NextDouble();

    public virtual PNDamage CreateDamage(double amount, bool canCrit, params EveId[] tags)
    {
        var stats = RequireCombatReady();
        if (IsDead) throw new InvalidOperationException("A dead entity cannot create new damage.");
        if (double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0d)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (tags == null) throw new ArgumentNullException(nameof(tags));
        var preparedTags = stats.PrepareDamageTags(tags);
        // Public virtual Calculate may retain or mutate its array in a mod override.
        double outgoing = Calculate(PNContentIds.Scale, amount, (EveId[])preparedTags.Values.Clone());
        double rate = stats.TryGetBase(PNContentIds.CritRate, out _) ? Evaluate(PNContentIds.CritRate) : 0d;
        double criticalDamage = stats.TryGetBase(PNContentIds.CritDamage, out _) ? Evaluate(PNContentIds.CritDamage) : 150d;
        double probability = Math.Max(0d, Math.Min(100d, rate)) / 100d;
        bool critical = false;
        if (canCrit && probability > 0d)
        {
            if (probability >= 1d) critical = true;
            else
            {
                double roll = NextCriticalRoll();
                if (double.IsNaN(roll) || double.IsInfinity(roll) || roll < 0d || roll >= 1d)
                    throw new InvalidOperationException("Critical roll must be in [0, 1).");
                critical = roll < probability;
            }
        }
        if (critical) outgoing *= criticalDamage / 100d;
        if (double.IsNaN(outgoing) || double.IsInfinity(outgoing) || outgoing < 0d)
            throw new OverflowException("Critical damage must be finite and nonnegative.");
        return new PNDamage(SessionId, EntityId, outgoing, preparedTags, null, canCrit, critical, rate, criticalDamage);
    }

    public virtual PNDamage CreateDamage(double amount, bool canCrit, double? iframeDuration, params EveId[] tags)
    {
        if (iframeDuration.HasValue && (double.IsNaN(iframeDuration.Value) || double.IsInfinity(iframeDuration.Value)))
            throw new ArgumentOutOfRangeException(nameof(iframeDuration));
        return CreateDamage(amount, canCrit, tags).WithIFrameDuration(iframeDuration);
    }

    public virtual PNDamageResult SendDamage(PNStat target, double amount, params EveId[] tags)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        target.RequireCombatReady();
        return target.ReceiveDamage(CreateDamage(amount, tags));
    }

    // EN: Validate and calculate before mutation; rejected hits cannot partially change health.
    // VI: Kiểm tra và tính trước khi sửa; hit bị từ chối không làm đổi một phần HP.
    public virtual PNDamageResult ReceiveDamage(PNDamage damage)
    {
        using (EveHotPathMetrics.Measure("combat.receive"))
        {
        var stats = RequireCombatReady();
        if (damage == null) throw new ArgumentNullException(nameof(damage));
        if (damage.SessionId != SessionId) throw new ArgumentException("Damage belongs to another session.", nameof(damage));
        stats.ResolveDamageType(damage.Tags);
        double iframeBefore = IFrameRemaining;
        double duration = damage.IFrameDuration ?? DefaultIFrameDuration;
        double iframeAfter = iframeBefore > 0d ? Math.Max(0d, iframeBefore - duration) : Math.Abs(duration);
        if (double.IsNaN(iframeAfter) || double.IsInfinity(iframeAfter))
            throw new OverflowException("IFrame time must remain finite.");
        double multiplier = iframeBefore > 0d ? IFrameDamageMultiplier : 1d;
        double calculated = CalculateDamage(damage.Amount, damage.Tags.ToArray());
        calculated *= multiplier;
        if (double.IsNaN(calculated) || double.IsInfinity(calculated) || calculated < 0d)
            throw new InvalidOperationException("Incoming damage must be finite and nonnegative.");
        double before = GetCurrent(PNContentIds.Health);
        double after = Math.Max(0d, before - calculated);
        var result = new PNDamageResult(damage, EntityId, calculated, before, after,
            multiplier, iframeBefore, before == 0d ? iframeBefore : iframeAfter, duration);
        if (before == 0d) return result;
        SetCurrent(PNContentIds.Health, after);
        if (iframeAfter == 0d) iframeState.End();
        else if (iframeState.IsActive) iframeState.RefreshDuration(iframeAfter);
        else iframeState.TryActivate(iframeAfter);
#if UNITY_5_3_OR_NEWER
        // Spawn before observers can destroy the receiver. Presentation cannot roll back HP.
        try { using (EveHotPathMetrics.Measure("combat.damage-popup")) ShowDamagePopup(result); } catch (Exception error) { Debug.LogException(error, this); }
#endif
        NotifyCombat(OnDamageReceived, result);
        if (result.Killed) NotifyCombat(OnDied, result);
        return result;
    
        }
    }

    protected virtual void OnDamageReceived(PNDamageResult result) => NotifyCombat(DamageReceived, result);
    protected virtual void OnDied(PNDamageResult result) => NotifyCombat(Died, result);

    private EveEntityLoader combatLoader;
    private PNStatSet RequireCombatReady()
    {
        if (!IsReady || !(Set is PNStatSet stats) || !EveRegistrySession.IsReady)
            throw new InvalidOperationException("Combat effects require initialized game stats in an active session.");
        var session = EveRegistrySession.Context;
        if (!session.HasAuthority) throw new InvalidOperationException("Only offline or host authority can apply combat effects.");
        var loader = combatLoader; if (loader == null) combatLoader = loader = GetComponent<EveEntityLoader>();
        if (loader == null || !loader.IsReady || loader.LoadContext?.CanSimulate!=true || loader.SessionId != SessionId ||
            !ReferenceEquals(stats.Registry, session.Registry) ||
            !session.Entities.TryGetPublishedPresence(EntityId, loader.Generation, out var registered) || !ReferenceEquals(loader, registered))
            throw new InvalidOperationException("Combat effects require a Ready entity bound to the current registry and session.");
        return stats;
    }

    // EN: Observers run after the HP commit; observer failure must not invite a duplicate damage retry.
    // VI: Observer chạy sau khi ghi HP; lỗi observer không được gây thử lại damage trùng.
    private void NotifyCombat(Action<PNDamageResult> listeners, PNDamageResult result)
    {
        using (EveHotPathMetrics.Measure("combat.notify"))
        {
        if (listeners == null) return;
        foreach (Action<PNDamageResult> listener in listeners.GetInvocationList())
            try { listener(result); } catch (Exception error) { Debug.LogException(error, this); }
        }
    }

    private void ClearCombatListeners()
    {
        capturedCombatSet = null; capturedCombatSource = null;
        combatAbilities?.Dispose(); combatAbilities = null; iframeState = null;
        DamageReceived = null;
        Died = null;
        HealReceived = null;
    }
}
