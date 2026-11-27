using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

// EN: Gura's own skill class (Docs/GuraCharacter.md). Every slot is a registered behavior binding (PNGuraContent); the shared player logic keeps the Dash, but with Gura's own Dash data
//     (2 charges, 7 s each, `gura_dash_charge`). The Dash gestures are Gura's: a tap is the plain Dash; keeping the button held dashes once and puts the water coat on her (move speed, a timed State whose
//     registered `stateEffects` apply and take back the bonus). The coat lasts while the button is held, at most its own duration; a stun or a menu counts as letting go. When it ends - either way -
//     it raises the next-strike flag (the next Attack press is the third strike); death takes the coat with no flag. The states are Ability state, so the flag and the coat follow the entity on every machine.
// VI: Class skill rieng cua Gura: moi o la binding dang ky; Dash dung logic chung voi du lieu Dash cua Gura. Tap = Dash thuong; giu = Dash mot lan + ao nuoc den khi buong (toi da theo thoi luong), ao tat moi cap co.
[DisallowMultipleComponent]
public class PNGuraSkill : PNPlayerSkill
{
    protected override bool UsesCharacterBindings => true;
    protected override EveId PlayerDashSkillId => PNGuraContent.DashSkill;
    protected override EveId DefaultDashBindingProfile => PNGuraContent.DashProfile;

    private PNStat owner;
    private PNControlState controlled;
    private bool heldDashed;

    public override void Load(EveEntityLoadContext context)
    {
        base.Load(context);
        // The Ability states are declared by the moveset skill data (loaded with the character before this skill); the effects bound to them are declared there too.
        BindStateEffects(Abilities, PNStateEffectData.ParseAll((GetData(PNGuraContent.Moveset).Skill as JObject)?["stateEffects"]));
        owner = GetComponent<PNStat>(); if (owner != null) owner.Died += OnOwnerDied;
        controlled = Controls; controlled.Changed += OnControlsChanged;
    }
    public override void Unload()
    {
        if (owner != null) owner.Died -= OnOwnerDied;
        if (controlled != null && !controlled.IsDisposed) controlled.Changed -= OnControlsChanged;
        owner = null; controlled = null; heldDashed = false;
        UnbindStateEffects(); base.Unload();
    }

    // Held Dash: once per press, the Dash itself and then the water coat. Nothing happens when the Dash did not start (no charge, busy); that press asks only once.
    protected override void StartRepeatDash() => heldDashed = false;
    protected override void RepeatDash()
    {
        if (heldDashed) return;
        heldDashed = true;
        if (TryDash()) Raise(PNGuraContent.WaterCoat);
    }
    // Only a press that asked for the coat ends it (a quick tap is canceled on this path too).
    protected override void CanceledRepeatDash() { bool held = heldDashed; heldDashed = false; if (held) EndCoat(); }

    // A stun (every skill locked) counts as letting go.
    private void OnControlsChanged() { if (controlled != null && !controlled.IsAllowed(PNControlIds.SkillAll)) EndCoat(); }
    private void OnOwnerDied(PNDamageResult result) => EndCoat();
    private void EndCoat()
    {
        if (Abilities != null && GuraResources.IsOn(Abilities, PNGuraContent.WaterCoat)) GuraResources.End(Abilities, PNGuraContent.WaterCoat);
    }

    // The one place the coat pays its flag: it ran out or was let go. A reset (death, a new life) and a dead owner give none.
    protected override void OnAbilityEnded(EveId state, PNAbilityChangeReason reason)
    {
        base.OnAbilityEnded(state, reason);
        if (state != PNGuraContent.WaterCoat || reason != PNAbilityChangeReason.Ended && reason != PNAbilityChangeReason.Expired) return;
        if (owner != null && !owner.IsDead) Raise(PNGuraContent.NextFour);
    }

    // A tap Dash gives its reward (skill data `skill.reward`), a held Dash none.
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
        // A skill that says `flagOnHit` (the forms of the Skill) raises the next-strike flag by hitting.
        if (FlagsOnHit(context.SkillId)) Raise(PNGuraContent.NextFour);
        // A reward marked `once` is paid once for the whole Action, however many of its hits land.
        if (ReferenceEquals(context, lastPaid) && PaysOnce(context.SkillId)) return;
        // One reward per sweep (a sweep that hits three enemies is one hit of the table): hits of one sweep follow each other within microseconds.
        long now = System.Diagnostics.Stopwatch.GetTimestamp(); if (ReferenceEquals(context, lastPaid) && now - lastPaidAt < System.Diagnostics.Stopwatch.Frequency / 200) return;
        if (Pay(context.SkillId, true)) { lastPaid = context; lastPaidAt = now; }
    }
    private bool FlagsOnHit(EveId skillId) => (GetData(skillId).Skill as JObject)?["flagOnHit"]?.Value<bool>() == true;
    private bool PaysOnce(EveId skillId) => (GetData(skillId).Skill as JObject)?["reward"]?["once"]?.Value<bool>() == true;
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
