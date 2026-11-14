#if UNITY_EDITOR
using System;
using EveEngine;

// Test probe over Eve's skill: opens/closes an attack activation without input and lets a check drive the attack timeline directly.
public sealed class PNEveAttackSpeedProbe : EveCharacterSkill
{
    public void BeginCategoryReview()
    {
        var id = EveId.Parse("base:eve_attack_entry");
        if (!CanActivateSkill(id)) throw new InvalidOperationException("Category review attack admission failed.");
        BeginActivation(id);
    }
    public void EndCategoryReview() => EndActivation();
    public void TickAttack(float delta) => AdvanceAttack(delta);
    public void EnsureInput() { if (pc == null) Awake(); OnEnable(); }
}
#endif
