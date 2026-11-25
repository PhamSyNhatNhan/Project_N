using EveEngine;

// EN: Gura's three resources and her Burst state (Docs/GuraCharacter.md 3, 4.6), read and written only through her Ability collection (Counter/State/Cooldown), so they follow the entity on every machine.
//     Outside Burst a strike gives BurstStack (and shortens the Ultimate cooldown); inside Burst the same strike gives Wave instead (BurstStack does not grow in Burst). Counters clamp at their maximum.
// VI: Tai nguyen cua Gura qua Ability. Ngoai Burst don danh cho BurstStack va giam hoi chieu Ultimate; trong Burst cung don do cho Song.
public static class GuraResources
{
    public static bool IsOn(PNAbilityCollection abilities, EveId id)
    {
        foreach (var found in abilities.Find(id)) if (found is PNAbilityState state && state.IsActive) return true;
        return false;
    }
    public static bool InBurst(PNAbilityCollection abilities) => IsOn(abilities, PNGuraContent.BurstState);
    // The cooldown cut is the authority's (a guess keeps the cooldown it was given). One strike's reward: (burst, wave) by state; `cooldownCut` seconds off the Ultimate cooldown, outside Burst only.
    public static void Gain(PNAbilityCollection abilities, int burst, int wave, double cooldownCut, bool authority)
    {
        if (InBurst(abilities)) { if (wave > 0) abilities.GetCounter(PNGuraContent.WaveStack).Add(wave); return; }
        if (burst > 0) abilities.GetCounter(PNGuraContent.BurstStack).Add(burst);
        if (cooldownCut > 0 && authority) { var cd = abilities.GetCooldown(PNGuraContent.UltimateReady); if (cd.IsRecovering) cd.ReduceRemaining(cooldownCut); }
    }
    public static void End(PNAbilityCollection abilities, EveId state)
    {
        foreach (var found in abilities.Find(state)) if (found is PNAbilityState s && s.IsActive) s.End();
    }
}
