#if UNITY_5_3_OR_NEWER
using System;
using System.Linq;
using EveEngine;
using UnityEngine;

// The authority's side of the combat event channel (PNCombatEvents): every result a stat applies becomes one event. Presentation only; nothing here changes a result.
public partial class PNStat
{
    private PNObjectWorld CombatEventWorld()
    {
        var identity = GetComponent<EveEntityIdentity>();
        return identity != null && identity.World != null ? PNObjectWorld.Of(identity.World) : default;
    }
    private void PublishCombatEvent(PNDamageResult result, PNCombatEventKind kind)
    {
        try
        {
            var damage = result.Damage;
            var tags = damage.Tags.Take(PNCombatEvent.MaxTags).Select(x => x.ToString()).Where(x => x.Length <= PNCombatEvent.MaxText).ToArray();
            PNCombatEvents.Publish(new PNCombatEvent
            {
                Kind = kind, World = CombatEventWorld(), TargetId = EntityId.Value, SourceId = damage.SourceId.IsValid ? damage.SourceId.Value : 0L,
                Applied = (float)result.AppliedDamage, Calculated = (float)result.CalculatedDamage, LoricaAbsorbed = (float)result.LoricaAbsorbed,
                HealthBefore = (float)result.HealthBefore, HealthAfter = (float)result.HealthAfter, Critical = result.IsCritical, Killed = result.Killed, LoricaBroke = result.LoricaBroke,
                InIFrame = result.IFrameBefore > 0d, HasPoint = damage.HasHitPoint, PointX = damage.HitX, PointY = damage.HitY, PointZ = damage.HitZ, HitKey = damage.HitKey, Tags = tags
            });
        }
        catch (Exception error) { Debug.LogException(error, this); }
    }
    private void PublishHealEvent(PNHealResult result)
    {
        if (result.AppliedHeal <= 0d) return;
        try
        {
            PNCombatEvents.Publish(new PNCombatEvent
            {
                Kind = PNCombatEventKind.Heal, World = CombatEventWorld(), TargetId = EntityId.Value, SourceId = result.Heal.SourceId.IsValid ? result.Heal.SourceId.Value : 0L,
                Applied = (float)result.AppliedHeal, Calculated = (float)result.CalculatedHeal, HealthBefore = (float)result.HealthBefore, HealthAfter = (float)result.HealthAfter
            });
        }
        catch (Exception error) { Debug.LogException(error, this); }
    }
}
#endif
