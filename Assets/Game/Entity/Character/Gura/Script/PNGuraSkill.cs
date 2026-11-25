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

    // Starts the timed state, or refreshes it when it is already on (the collection's grant: it works on the owner's guess too).
    private void Raise(EveId id) => Abilities.GrantState(id);
}
