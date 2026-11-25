using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: Gura's own skill class (Docs/GuraCharacter.md). Every slot is a registered behavior binding (PNGuraContent); the shared player logic keeps the Dash, but with Gura's own Dash data
//     (3 charges, 7 s each, `gura_dash_charge`). The Dash gestures are Gura's: a tap is the plain Dash; a hold is a Dash that also puts the water coat on her (move speed, a timed State whose registered
//     `stateEffects` apply and take back the bonus) and raises the next-strike flag (the next Attack press is the fourth strike). The states are Ability state, so the flag and the coat follow the entity on every machine.
// VI: Class skill rieng cua Gura: moi o la binding dang ky; Dash dung logic chung voi du lieu Dash cua Gura. Tap = Dash thuong; giu = Dash + ao nuoc (toc do di chuyen) va co nhat chem ke tiep.
[DisallowMultipleComponent]
public class PNGuraSkill : PNPlayerSkill
{
    protected override bool UsesCharacterBindings => true;
    protected override EveId PlayerDashSkillId => PNGuraContent.DashSkill;
    protected override EveId DefaultDashBindingProfile => PNGuraContent.DashProfile;

    public override void Load(EveEntityLoadContext context)
    {
        base.Load(context);
        // The Ability states are declared by the moveset skill data (loaded with the character before this skill); the effects bound to them are declared there too.
        BindStateEffects(Abilities, PNStateEffectData.ParseAll((GetData(PNGuraContent.Moveset).Skill as JObject)?["stateEffects"]));
    }
    public override void Unload() { UnbindStateEffects(); base.Unload(); }

    // A hold Dash: the Dash itself, then the water coat and the next-strike flag. Nothing happens when the Dash did not start (no charge, busy).
    protected override void HoldDash()
    {
        if (!TryDash()) return;
        Raise(PNGuraContent.WaterCoat); Raise(PNGuraContent.NextFour);
    }

    // A tap Dash gives its reward (skill data `skill.reward`), a hold Dash none.
    protected override void TapDash() { if (TryDash()) Pay(PNGuraContent.DashSkill, true); }

    // ---- stack rewards (Docs/GuraCharacter.md 4.6): a strike pays only when it HITS, per hit, from the hit skill's own data `skill.reward {burst, wave, cut}` ----
    private readonly System.Collections.Generic.Dictionary<EveId, int[]> rewards = new System.Collections.Generic.Dictionary<EveId, int[]>();
    private readonly System.Collections.Generic.Dictionary<EveId, double> cuts = new System.Collections.Generic.Dictionary<EveId, double>();
    private PNSkillActionContext prepaid, lastPaid; private long lastPaidAt = long.MinValue;
    // The strike was paid when it was cast (the flagged fourth strike needs no hit): its hits pay nothing more.
    public void Prepaid(PNSkillActionContext context) => prepaid = context;
    public override void OnHitConfirmed(PNSkillActionContext context, PNStat target, PNDamageResult result)
    {
        base.OnHitConfirmed(context, target, result);
        if (ReferenceEquals(context, prepaid)) return;
        // One reward per sweep (a sweep that hits three enemies is one hit of the table): hits of one sweep follow each other within microseconds.
        long now = System.Diagnostics.Stopwatch.GetTimestamp(); if (ReferenceEquals(context, lastPaid) && now - lastPaidAt < System.Diagnostics.Stopwatch.Frequency / 200) return;
        if (Pay(context.SkillId, true)) { lastPaid = context; lastPaidAt = now; }
    }
    // Gives the reward of a skill (false when it has none). `authority` is whether the machine may also cut the Ultimate cooldown.
    public bool Pay(EveId skillId, bool authority)
    {
        if (!rewards.TryGetValue(skillId, out var values))
        {
            var reward = (GetData(skillId).Skill as JObject)?["reward"] as JObject;
            values = reward == null ? null : new[] { reward["burst"]?.Value<int>() ?? 0, reward["wave"]?.Value<int>() ?? 0 };
            rewards[skillId] = values; cuts[skillId] = reward?["cut"]?.Value<double>() ?? 0d;
        }
        if (values == null) return false;
        GuraResources.Gain(Abilities, values[0], values[1], cuts[skillId], authority); return true;
    }

    // Starts the timed state, or refreshes it when it is already on (the collection's grant: it works on the owner's guess too).
    private void Raise(EveId id) => Abilities.GrantState(id);
}
