using System;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;

// EN: `project_n:gura_blessing` (Docs/GuraCharacter.md 4.4, the tide's gift to a player standing in it): a timed Buff that holds a list of stat modifiers and one skill contribution (the 5% heal on a normal attack, as an
//     `OnHit` effect) for as long as it lasts. Payload {duration, clock, modifiers[{statId, value, group, ...}], contribution}. The tide area re-applies it while the player stays inside (applying again resets the time),
//     so it lapses a moment after he leaves. It works for any character: it only touches stats and the skill contribution layer.
// VI: Buff theo thoi gian: giu danh sach modifier chi so va mot contribution cua skill (hoi 5% khi danh thuong). Vung thuy trieu ap lai khi nguoi choi con o trong; roi vung thi het sau mot luc.
public sealed class PNGuraBlessingBehavior : PNBuffTimedBehavior
{
    public static readonly EveId Id = EveId.Parse("project_n:gura_blessing");
    public override bool EventDrivenReplication => true;
    private IDisposable modifier;
    public PNGuraBlessingBehavior(JToken data) : base(data) { }
    public override void OnApply(PNBuffContext context, JToken input) { base.OnApply(context, input); OnResume(context); }
    public override bool OnReapply(PNBuffContext context, PNBuffApplyRequest request) { context.SetDuration(PNBuffContentBatch.Number(Data, "duration")); return true; }
    public override void OnResume(PNBuffContext context)
    {
        var values = PNBuffContentBatch.Array(Data["modifiers"]).Select(token => PNStatModifierData.Parse(PNBuffContentBatch.Object(token), 1)).ToArray();
        modifier?.Dispose(); modifier = context.ModifyStats(values);
        if (Data["contribution"] != null) context.ModifySkill(new PNSkillContribution(Data["contribution"], context.Registry));
    }
    public override void OnCleanup(PNBuffContext context) { modifier?.Dispose(); modifier = null; }

    public static void Register(EveRegistryHub hub)
    {
        var payload = JObject.Parse("{'duration':0.4,'clock':'Game','modifiers':[{'statId':'base:attack_speed','value':20,'group':0},{'statId':'base:move_speed','value':0.2,'group':0},{'statId':'base:attack_power','value':0.5,'group':2}],'contribution':{'readMode':'Live','categoryIds':['base:attack'],'effects':[{'trigger':'OnHit','kind':'Operation','id':'project_n:heal','target':'Self','hitScope':'PerTarget','data':{'value':[{'amount':5,'statId':'base:health'}]}}]}}");
        hub.Buffs().Register(new PNBuffDefinition(Id, Id, data => new PNGuraBlessingBehavior(data), payload, new[] { EveId.Parse("project_n:buff") },
            validate: Validate, validateReferences: (data, context) => { foreach (var entry in (JArray)data["modifiers"]) PNBuffPayloadReferences.Modifier(entry, context); PNBuffPayloadReferences.Contribution(data["contribution"], context); }));
    }
    public static void Validate(JToken data, EveRegistryHub hub)
    {
        var obj = PNBuffContentBatch.Object(data); PNBuffContentBatch.Fields(obj, "duration", "clock", "modifiers", "contribution");
        PNBuffCollection.Seconds(PNBuffContentBatch.Number(obj, "duration"), false); PNBuffBuiltinSettings.Clock(obj);
        if (!(obj["modifiers"] is JArray list)) throw new FormatException("gura_blessing needs modifiers.");
        foreach (var token in list) { var entry = PNBuffContentBatch.Object(token); PNBuffContentBatch.Fields(entry, "statId", "value", "group", "calculationId", "contributionId"); PNStatModifierData.Validate(entry, hub); }
        if (obj["contribution"] != null) { PNBuffContentBatch.Object(obj["contribution"]); if (hub.IsSealed) new PNSkillContribution(obj["contribution"], hub); }
    }
}
