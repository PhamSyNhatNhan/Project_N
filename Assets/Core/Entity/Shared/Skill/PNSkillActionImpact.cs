using System;
using System.Globalization;
using EveEngine;
using UnityEngine;

// EN: Where an Action hit landed, told to whoever draws it (plan 4.4b item 1). One hit of a cast is told ONCE on every machine that shows the cast:
//     Real = the authority applied it; Guess = the shooter's own machine reported it (the early phase, before the host answers); Copy = another player's machine, told by the combat event that carries the hit key.
//     A skill or presentation subscribes `PNSkill.ActionImpact` and draws the impact (spark, sound); it never changes state from it. The key of a hit is castKey:a<action ordinal>:<hit index>:<target id>.
// VI: Cu danh cua Action trung o dau. Moi cu trung cua mot cast duoc bao MOT lan tren moi may thay cast do: Real (authority da ap), Guess (may nguoi ban tu bao, som), Copy (may khac, nhan tu su kien combat mang khoa cu trung).
public enum PNActionImpactOrigin { Real, Guess, Copy }

public readonly struct PNActionImpact
{
    public PNActionImpactOrigin Origin { get; }
    public long CastKey { get; }
    public long ActionOrdinal { get; }
    public int HitIndex { get; }
    public long TargetId { get; }
    // The entity hit when this machine has it (a copy may not have loaded it yet).
    public PNStat Target { get; }
    public Vector3 Point { get; }
    // From the attacker towards the hit point (zero when unknown).
    public Vector3 Direction { get; }
    public PNActionImpact(PNActionImpactOrigin origin, long castKey, long ordinal, int hitIndex, long targetId, PNStat target, Vector3 point, Vector3 direction)
    { Origin = origin; CastKey = castKey; ActionOrdinal = ordinal; HitIndex = hitIndex; TargetId = targetId; Target = target; Point = point; Direction = direction; }

    internal static string Key(long castKey, long ordinal, int hitIndex, long targetId)
        => castKey.ToString(CultureInfo.InvariantCulture) + ":a" + ordinal.ToString(CultureInfo.InvariantCulture) + ":" + hitIndex.ToString(CultureInfo.InvariantCulture) + ":" + targetId.ToString(CultureInfo.InvariantCulture);
    internal static bool TryParse(string key, out long castKey, out long ordinal, out int hitIndex, out long targetId)
    {
        castKey = 0; ordinal = 0; hitIndex = 0; targetId = 0;
        if (string.IsNullOrEmpty(key)) return false;
        var part = key.Split(':');
        if (part.Length != 4 || part[1].Length < 2 || part[1][0] != 'a') return false;
        var style = NumberStyles.None; var culture = CultureInfo.InvariantCulture;
        return long.TryParse(part[0], style, culture, out castKey) && long.TryParse(part[1].Substring(1), style, culture, out ordinal)
            && int.TryParse(part[2], style, culture, out hitIndex) && long.TryParse(part[3], style, culture, out targetId);
    }
}

public partial class PNSkill
{
    private Action<PNActionImpact> actionImpact;
    private bool observingImpacts;
    public int ActionImpactsReal { get; private set; }
    public int ActionImpactsGuess { get; private set; }
    public int ActionImpactsCopy { get; private set; }

    // Told once per hit of this skill's Actions on every machine that shows them (see PNActionImpact). A faulty listener is isolated.
    public event Action<PNActionImpact> ActionImpact
    {
        add { actionImpact += value; UpdateImpactObserver(); }
        remove { actionImpact -= value; UpdateImpactObserver(); }
    }

    internal bool HasActionImpactListeners => actionImpact != null;

    internal void RaiseActionImpact(PNActionImpactOrigin origin, long castKey, long ordinal, int hitIndex, long targetId, PNStat target, Vector3 point)
    {
        switch (origin) { case PNActionImpactOrigin.Real: ActionImpactsReal++; break; case PNActionImpactOrigin.Guess: ActionImpactsGuess++; break; default: ActionImpactsCopy++; break; }
        var handlers = actionImpact; if (handlers == null) return;
        var from = transform.position; var direction = point - from; float lengthSquared = direction.x * direction.x + direction.y * direction.y + direction.z * direction.z; direction = lengthSquared > 1e-6f ? direction * (1f / (float)System.Math.Sqrt(lengthSquared)) : Vector3.zero;
        var impact = new PNActionImpact(origin, castKey, ordinal, hitIndex, targetId, target, point, direction);
        foreach (Action<PNActionImpact> handler in handlers.GetInvocationList())
        {
            try { handler(impact); }
            catch (Exception error) { Debug.LogException(error, this); }
        }
    }

    // Another player's machine is told of the hits by the combat events of the authority; the owner's client and the authority raise their own.
    private void UpdateImpactObserver()
    {
        bool want = actionImpact != null;
        if (want == observingImpacts) return;
        observingImpacts = want;
        PNCombatEvents.Observed -= OnActionCombatEvent;
        if (want) PNCombatEvents.Observed += OnActionCombatEvent;
    }
    private void StopActionImpactObserver() { observingImpacts = false; PNCombatEvents.Observed -= OnActionCombatEvent; }

    private void OnActionCombatEvent(PNCombatEvent combatEvent, bool matchedEarly)
    {
        if (combatEvent.Kind != PNCombatEventKind.Damage || combatEvent.HitKey == null || !IsReady) return;
        if (HasGameplayAuthority || IsOwnerClient) return;
        var stat = GetComponent<PNStat>(); if (stat == null || !stat.IsReady || combatEvent.SourceId != stat.EntityId.Value) return;
        if (!PNActionImpact.TryParse(combatEvent.HitKey, out long castKey, out long ordinal, out int hitIndex, out long targetId) || targetId != combatEvent.TargetId) return;
        PNStat target = null;
        if (EveRegistrySession.IsReady && EveRegistrySession.Context.Entities.TryGet(new EveEntityId(targetId), out var loader) && loader != null) target = loader.GetComponent<PNStat>();
        var point = combatEvent.HasPoint ? new Vector3(combatEvent.PointX, combatEvent.PointY, combatEvent.PointZ) : target != null ? target.transform.position : transform.position;
        RaiseActionImpact(PNActionImpactOrigin.Copy, castKey, ordinal, hitIndex, targetId, target, point);
    }
}
