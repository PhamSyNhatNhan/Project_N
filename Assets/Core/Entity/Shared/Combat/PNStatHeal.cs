using System;

public partial class PNStat
{
    public event Action<PNHealResult> HealReceived;

    public virtual PNHeal CreateHeal(double amount)
    {
        RequireCombatReady();
        if (IsDead) throw new InvalidOperationException("A dead entity cannot create heal packets.");
        PNHeal.RequireAmount(amount);
        double outgoing = Calculate(PNContentIds.Scale, amount, PNContentIds.Heal);
        PNHeal.RequireAmount(outgoing);
        return new PNHeal(SessionId, EntityId, outgoing, amount);
    }

    public virtual PNHealResult SendHeal(PNStat target, double amount)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        target.RequireCombatReady();
        return target.ReceiveHeal(CreateHeal(amount));
    }

    public virtual PNHealResult ReceiveHeal(PNHeal heal)
    {
        RequireCombatReady();
        if (heal == null) throw new ArgumentNullException(nameof(heal));
        if (heal.SessionId != SessionId) throw new ArgumentException("Heal belongs to another session.", nameof(heal));
        double before = GetCurrent(PNContentIds.Health);
        double calculated = before == 0d ? 0d : Calculate(PNContentIds.Mitigation, heal.Amount, PNContentIds.Heal);
        PNHeal.RequireAmount(calculated);
        double missing = Math.Max(0d, GetMax(PNContentIds.Health) - before);
        double after = before + Math.Min(missing, calculated);
        PNHealStatus status = before == 0d ? PNHealStatus.DeadTarget : calculated == 0d ? PNHealStatus.ZeroAmount :
            missing == 0d ? PNHealStatus.NoMissingHealth : PNHealStatus.Applied;
        var result = new PNHealResult(heal, EntityId, calculated, before, after, status);
        if (after != before) SetCurrent(PNContentIds.Health, after);
        OnHealReceived(result);
        return result;
    }

    protected virtual void OnHealReceived(PNHealResult result)
    {
        var listeners = HealReceived;
        if (listeners == null) return;
        foreach (Action<PNHealResult> listener in listeners.GetInvocationList())
            try { listener(result); } catch (Exception error) { UnityEngine.Debug.LogException(error, this); }
    }
}
