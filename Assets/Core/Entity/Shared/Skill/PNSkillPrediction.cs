using EveEngine;
using Newtonsoft.Json.Linq;

// EN: What the owner's client may guess about a slot press other than Dash (Docs/SkillNetworkingAPlan.md S3): a profile that names `cooldownAbilityId` in its data spends that cooldown when it casts, so the
//     client shows the spent charge at once and takes it back if the authority declines. Anything else about the cast (the pet itself, hits) is the authority's.
// VI: Điều client chủ nhân được đoán về lần bấm ô khác Dash: profile có `cooldownAbilityId` trong data sẽ tiêu cooldown đó khi cast; client hiện ngay và trả lại nếu authority từ chối.
public partial class PNSkill
{
    public bool TryPredictSlotCooldown(EveId slot, out PNAbilityPrediction prediction)
    {
        prediction = default;
        if (HasGameplayAuthority || Bindings == null || !Bindings.TryGet(slot, out var id)) return false;
        var data = BindingProfile(id).Data as JObject; var token = data?["cooldownAbilityId"];
        if (token == null || token.Type != JTokenType.String || !EveId.TryParse((string)token, out var abilityId)) return false;
        PNAbilityCooldown cooldown; try { cooldown = Abilities.GetCooldown(abilityId); } catch (System.Exception) { return false; }
        return cooldown != null && cooldown.TryPredictConsume(out prediction);
    }
}
